using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MidgardStudio.App.ViewModels;
using MidgardStudio.Core;
using MidgardStudio.Core.Workspace;
using Serilog;
using Wpf.Ui.Appearance;

namespace MidgardStudio.App;

/// <summary>
/// Application entry point: builds the generic host (DI + Serilog), registers the legacy
/// codepage provider (RO client files default to Windows-1252), applies the Fluent dark
/// theme, and shows the main shell window.
/// </summary>
public partial class App : Application
{
    private static IHost? _host;

    public static IServiceProvider Services =>
        _host?.Services ?? throw new InvalidOperationException("Host is not initialized.");

    protected override void OnStartup(StartupEventArgs e)
    {
        // RO client lua/lub and GRF entry names use legacy single-byte codepages (default 1252).
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        // Let the Grf layer decode formats its ImageProvider can't (e.g. .jpg water textures) via WPF.
        MidgardStudio.Grf.GrfService.EncodedImageDecoder = DecodeEncodedImage;

        string logDir = Path.Combine(AppPaths.LocalDir, "logs"); // machine-local, disposable
        Directory.CreateDirectory(logDir);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(logDir, "MidgardStudio-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true)
            .CreateLogger();

        Log.Information("Midgard Studio starting up.");

        _host = Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices(ConfigureServices)
            .Build();

        _host.Start();

        // Configure GRF sources from persisted settings (optional; no-op if none).
        try
        {
            var grf = Services.GetRequiredService<MidgardStudio.Grf.GrfService>();
            grf.Configure(Services.GetRequiredService<IWorkspaceConfigService>().Load().GrfPaths);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "GRF configuration failed");
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // Fire-and-forget loads (EnsureLoadedAsync etc.) shouldn't be able to fail silently — log any
        // task exception nobody awaited so corrupt-data failures are never invisible.
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };

        // Use the Midgard brand purple as the accent instead of inheriting the user's Windows accent colour,
        // so Primary buttons, toggles, focus rings and accent-driven controls are on-brand on every machine.
        ApplicationThemeManager.Apply(ApplicationTheme.Dark, updateAccent: false);
        ApplicationAccentColorManager.Apply(
            (Color)ColorConverter.ConvertFromString("#8D2DF2")!, ApplicationTheme.Dark, false, false);

        // Show the animated splash on its own UI thread, then build the shell (which loads the workspace
        // databases on this thread — several seconds). The splash keeps animating throughout.
        var splash = ShowSplash();
        long startedAt = Environment.TickCount64;

        var window = Services.GetRequiredService<MainWindow>();

        // Keep the splash up long enough to be seen even if the load happened to be quick. Pump the
        // dispatcher (rather than block with Thread.Sleep) so the main thread's message queue stays serviced.
        long remainingMs = 1600 - (Environment.TickCount64 - startedAt);
        if (remainingMs > 0)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(remainingMs), DispatcherPriority.Background,
                (_, _) => frame.Continue = false, Dispatcher.CurrentDispatcher);
            timer.Start();
            Dispatcher.PushFrame(frame);
            timer.Stop();
        }

        window.Show();
        window.Activate();
        splash?.Dispatcher.BeginInvoke(new Action(splash.BeginShutdown));

        base.OnStartup(e);
    }

    /// <summary>
    /// Creates the splash window on a dedicated background STA thread with its own dispatcher, so it animates
    /// independently of the main thread's (blocking) workspace load. A splash failure never breaks startup.
    /// </summary>
    private static Views.SplashWindow? ShowSplash()
    {
        Views.SplashWindow? splash = null;
        var ready = new ManualResetEventSlim(false);
        var thread = new Thread(() =>
        {
            try
            {
                splash = new Views.SplashWindow();
                splash.Show();
                ready.Set();
                Dispatcher.Run();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Splash screen failed to display");
                ready.Set();
            }
        })
        {
            IsBackground = true,
            Name = "SplashScreen",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait(2000); // proceed even if the splash is slow to appear
        return splash;
    }

    // WPF-based decoder for encoded images (jpg/png) the GRF library can't decode itself → BGRA pixels.
    private static (int Width, int Height, byte[] Bgra)? DecodeEncodedImage(byte[] data)
    {
        try
        {
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            using (var ms = new System.IO.MemoryStream(data))
            {
                bmp.BeginInit();
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
            }
            bmp.Freeze();
            var conv = new System.Windows.Media.Imaging.FormatConvertedBitmap(bmp, System.Windows.Media.PixelFormats.Bgra32, null, 0);
            int w = conv.PixelWidth, h = conv.PixelHeight;
            if (w <= 0 || h <= 0) return null;
            var px = new byte[w * h * 4];
            conv.CopyPixels(px, w * 4, 0);
            return (w, h, px);
        }
        catch { return null; }
    }

    private static void ConfigureServices(HostBuilderContext context, IServiceCollection services)
    {
        services.AddSingleton<IWorkspaceConfigService, WorkspaceConfigService>();
        services.AddSingleton<Services.SchemaRegistry>();
        services.AddSingleton<Services.WorkspaceSession>();
        services.AddSingleton<Services.ReferenceResolver>();
        services.AddSingleton<MidgardStudio.Grf.GrfService>();
        services.AddSingleton<Services.GrfImageService>();
        services.AddSingleton<Services.ClientItemService>();
        services.AddSingleton<Services.ClientSkillService>();
        services.AddSingleton<Services.SpriteLinkService>();
        services.AddSingleton<Services.MobSpriteService>();
        services.AddSingleton<Services.DropService>();
        services.AddSingleton<Services.SkillLookupService>();
        services.AddSingleton<Services.BackupService>();
        services.AddSingleton<Services.MapCacheService>();
        services.AddSingleton<Services.CashShopService>();
        services.AddSingleton<Services.AppSettingsService>();
        services.AddSingleton<Services.ReferenceIndex>();
        services.AddSingleton<Services.WorkspaceValidator>();
        services.AddSingleton<GrfBrowserViewModel>();
        services.AddSingleton<ValidationViewModel>();
        services.AddSingleton<ConfigurationWizardViewModel>();
        services.AddSingleton<OnboardingViewModel>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<MainWindow>();
    }

    private string? _lastErrorMessage;
    private long _lastErrorTick;

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled dispatcher exception");
        // A data editor must not die to a stray exception and take the unsaved session with it.
        e.Handled = true;

        // Throttle the dialog: a recurring fault (a per-frame render or binding error) would otherwise spawn
        // an endless stream of identical blocking modals. Show the same message at most once per 5 seconds.
        long now = Environment.TickCount64;
        if (e.Exception.Message == _lastErrorMessage && now - _lastErrorTick < 5000) return;
        _lastErrorMessage = e.Exception.Message;
        _lastErrorTick = now;

        try
        {
            Views.ConfirmDialog.Alert("Unexpected error",
                "Something went wrong, but Midgard Studio will keep running so you don't lose your work:\n\n" +
                e.Exception.Message);
        }
        catch { /* never let the error handler itself bring the app down */ }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Midgard Studio shutting down.");
        if (_host is not null)
        {
            _host.StopAsync().GetAwaiter().GetResult();
            _host.Dispose();
        }

        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
