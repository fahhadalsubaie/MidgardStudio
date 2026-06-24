using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MidgardStudio.App.ViewModels;
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

        string appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Midgard Studio");
        Directory.CreateDirectory(appData);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(appData, "logs", "MidgardStudio-.log"),
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

        ApplicationThemeManager.Apply(ApplicationTheme.Dark);

        var window = Services.GetRequiredService<MainWindow>();
        window.Show();

        base.OnStartup(e);
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
        services.AddSingleton<Services.SpriteLinkService>();
        services.AddSingleton<Services.MobSpriteService>();
        services.AddSingleton<Services.DropService>();
        services.AddSingleton<Services.BackupService>();
        services.AddSingleton<Services.MapCacheService>();
        services.AddSingleton<Services.AppSettingsService>();
        services.AddSingleton<Services.CrossFileValidator>();
        services.AddSingleton<GrfBrowserViewModel>();
        services.AddSingleton<ValidationViewModel>();
        services.AddSingleton<ConfigurationWizardViewModel>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<MainWindow>();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled dispatcher exception");
        // A data editor must not die to a stray exception and take the unsaved session with it.
        try
        {
            Views.ConfirmDialog.Alert("Unexpected error",
                "Something went wrong, but Midgard Studio will keep running so you don't lose your work:\n\n" +
                e.Exception.Message);
        }
        catch { /* never let the error handler itself bring the app down */ }
        e.Handled = true;
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
