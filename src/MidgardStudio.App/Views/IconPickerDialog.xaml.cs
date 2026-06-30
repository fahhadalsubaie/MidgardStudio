using MidgardStudio.App.ViewModels;
using Wpf.Ui.Controls;

namespace MidgardStudio.App.Views;

/// <summary>Modal icon picker — copy an existing item's icon (search by id/name), or browse one chosen
/// GRF/loose source's inventory icons. Returns the chosen resource via the view-model's <c>Result</c>.</summary>
public partial class IconPickerDialog : FluentWindow
{
    private readonly IconPickerViewModel _vm;

    public IconPickerDialog(IconPickerViewModel vm)
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
