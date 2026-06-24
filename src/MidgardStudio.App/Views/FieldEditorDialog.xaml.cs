using Wpf.Ui.Controls;

namespace MidgardStudio.App.Views;

/// <summary>Hosts a wide field's full editor (chips / nested form / sub-grid / script) in a popup.</summary>
public partial class FieldEditorDialog : FluentWindow
{
    public FieldEditorDialog(string title, object fieldViewModel)
    {
        InitializeComponent();
        Title = title;
        TitleBarCtl.Title = title;
        Host.Content = fieldViewModel;
        CloseButton.Click += (_, _) => Close();
    }
}
