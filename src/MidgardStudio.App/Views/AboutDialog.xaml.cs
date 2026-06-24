using System.Reflection;
using Wpf.Ui.Controls;

namespace MidgardStudio.App.Views;

/// <summary>Themed About window for Midgard Studio.</summary>
public partial class AboutDialog : FluentWindow
{
    public AboutDialog()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"Version {version?.ToString(3) ?? "1.0"}";
        OkButton.Click += (_, _) => Close();
    }

    public static void ShowAbout()
    {
        var dialog = new AboutDialog { Owner = System.Windows.Application.Current.MainWindow };
        dialog.ShowDialog();
    }
}
