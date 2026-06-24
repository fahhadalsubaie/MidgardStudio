using System.Windows;
using Wpf.Ui.Controls;

namespace MidgardStudio.App.Views;

/// <summary>Read-only popup showing the YAML that will be written to the import file for a record.</summary>
public partial class YamlPreviewDialog : FluentWindow
{
    public YamlPreviewDialog(string title, string yaml)
    {
        InitializeComponent();
        Title = $"YAML — {title}";
        TitleBarCtl.Title = Title;
        YamlBox.Text = yaml;

        CopyButton.Click += (_, _) => { try { Clipboard.SetText(yaml); } catch { /* clipboard busy */ } };
        CloseButton.Click += (_, _) => Close();
    }
}
