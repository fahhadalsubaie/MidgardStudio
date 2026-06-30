using MidgardStudio.App.ViewModels;
using Wpf.Ui.Controls;

namespace MidgardStudio.App.Views;

/// <summary>Modal "Change Item ID" dialog — validates the entered id against the ids already in use and
/// returns the chosen id via the view-model's <c>Result</c>.</summary>
public partial class ChangeIdDialog : FluentWindow
{
    private readonly ChangeIdViewModel _vm;

    public ChangeIdDialog(ChangeIdViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = vm;
        _vm.CloseRequested += OnCloseRequested;
    }

    private void OnCloseRequested(bool ok)
    {
        _vm.CloseRequested -= OnCloseRequested;
        DialogResult = ok;
        Close();
    }
}
