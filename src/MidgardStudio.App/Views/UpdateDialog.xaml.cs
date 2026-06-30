using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MidgardStudio.Core.Updates;
using Wpf.Ui.Controls;

namespace MidgardStudio.App.Views;

/// <summary>
/// Branded "Check for updates" dialog in the splash aesthetic: an animated checking state, then a result —
/// up to date, an available update with a Download button, or a failed check with Retry. Reuses the Core
/// <see cref="UpdateChecker"/>; the only App concern is presentation + opening the release page.
/// </summary>
public partial class UpdateDialog : Window
{
    private readonly UpdateChecker _checker;
    private readonly string _currentVersion;
    private readonly string _fallbackUrl;
    private Action? _primaryAction;
    private bool _checking;

    public UpdateDialog(UpdateChecker checker, string currentVersion, string fallbackUrl)
    {
        InitializeComponent();
        _checker = checker;
        _currentVersion = currentVersion;
        _fallbackUrl = fallbackUrl;
        CurrentVersionText.Text = currentVersion;

        CloseX.Click += (_, _) => Close();
        SecondaryBtn.Click += (_, _) => Close();
        PrimaryBtn.Click += (_, _) => _primaryAction?.Invoke();
        Loaded += async (_, _) => await RunCheckAsync();
    }

    private async Task RunCheckAsync()
    {
        if (_checking) return;
        _checking = true;
        ResultPanel.Visibility = Visibility.Collapsed;
        CheckingPanel.Visibility = Visibility.Visible;

        UpdateCheckResult result;
        try { result = await _checker.CheckDetailedAsync(_currentVersion); }
        catch { result = UpdateCheckResult.Failed; }

        await Task.Delay(650); // let the animation breathe so the result never flickers past
        _checking = false;
        ShowResult(result);
    }

    private void ShowResult(UpdateCheckResult r)
    {
        switch (r.Status)
        {
            case UpdateStatus.UpdateAvailable:
                Configure("#332DA0F2", "#C9A6FF", SymbolRegular.ArrowDownload48,
                    "Update available",
                    $"Version {r.Update!.Version} is ready to download.\nYou're on {_currentVersion}.");
                string url = string.IsNullOrEmpty(r.Update.Url) ? _fallbackUrl : r.Update.Url;
                SetPrimary("Download", () => { OpenUrl(url); Close(); });
                SecondaryBtn.Content = "Later";
                break;

            case UpdateStatus.CheckFailed:
                Configure("#33E0A23C", "#F2C879", SymbolRegular.Warning48,
                    "Couldn't check for updates",
                    "We couldn't reach the update server. Check your connection and try again.");
                SetPrimary("Retry", () => _ = RunCheckAsync());
                SecondaryBtn.Content = "Close";
                break;

            default: // UpToDate
                Configure("#2249C97A", "#5AE0A0", SymbolRegular.CheckmarkCircle48,
                    "You're up to date",
                    $"Midgard Studio {_currentVersion} is the latest version.");
                PrimaryBtn.Visibility = Visibility.Collapsed;
                _primaryAction = null;
                SecondaryBtn.Content = "Close";
                break;
        }

        CheckingPanel.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Visible;

        ResultPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.3)));
        var pop = new DoubleAnimation(0.6, 1, TimeSpan.FromSeconds(0.42))
        {
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 },
        };
        StatusIconScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        StatusIconScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }

    private void Configure(string bg, string fg, SymbolRegular icon, string title, string subtitle)
    {
        var fgColor = Hex(fg);
        StatusIconBg.Background = new SolidColorBrush(Hex(bg));
        StatusIcon.Foreground = new SolidColorBrush(fgColor);
        StatusIcon.Symbol = icon;
        GlowStop.Color = Color.FromArgb(0x55, fgColor.R, fgColor.G, fgColor.B); // tint the glow toward the result colour
        ResultTitle.Text = title;
        ResultSubtitle.Text = subtitle;
    }

    private void SetPrimary(string label, Action action)
    {
        PrimaryBtn.Content = label;
        PrimaryBtn.Visibility = Visibility.Visible;
        _primaryAction = action;
    }

    private static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex)!;

    // Validate the scheme before launching: the URL is the feed's server-supplied html_url, so only an
    // http/https link is opened (a non-web scheme falls back to the fixed releases page).
    private static void OpenUrl(string url) => Common.ExternalLink.Open(url, Services.GitHubReleaseFeed.ReleasesPage);

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    /// <summary>Opens the dialog modally over the main window and runs the check.</summary>
    public static void ShowCheck(UpdateChecker checker, string currentVersion, string fallbackUrl)
    {
        var dialog = new UpdateDialog(checker, currentVersion, fallbackUrl) { Owner = Application.Current.MainWindow };
        dialog.ShowDialog();
    }
}
