using System.Collections.Generic;
using Wpf.Ui.Controls;

namespace MidgardStudio.App.Views;

/// <summary>
/// A themed (Fluent) confirmation shown after a manual save: a short summary of what happened plus the
/// list of files that were written to disk. Centered over the main window.
/// </summary>
public partial class SaveSummaryDialog : FluentWindow
{
    /// <summary>One written file in the summary list: a friendly label and its full path.</summary>
    public sealed record SavedFile(string Label, string Path);

    private SaveSummaryDialog()
    {
        InitializeComponent();
        CloseButton.Click += (_, _) => Close();
    }

    /// <summary>Shows the save summary modally. <paramref name="files"/> is the list of paths just written.</summary>
    public static void Show(string summary, IReadOnlyList<SavedFile> files)
    {
        var dialog = new SaveSummaryDialog { Owner = System.Windows.Application.Current.MainWindow };
        dialog.SummaryText.Text = summary;
        dialog.FilesList.ItemsSource = files;
        dialog.ShowDialog();
    }
}
