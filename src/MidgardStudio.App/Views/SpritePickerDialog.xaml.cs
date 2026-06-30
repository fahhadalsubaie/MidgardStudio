using MidgardStudio.App.ViewModels;
using Wpf.Ui.Controls;

namespace MidgardStudio.App.Views;

/// <summary>Modal headgear sprite picker — browse one chosen GRF/loose source's accessory sprite base names.
/// Returns the chosen name via the view-model's <c>Result</c>.</summary>
public partial class SpritePickerDialog : FluentWindow
{
    private readonly SpritePickerViewModel _vm;

    public SpritePickerDialog(SpritePickerViewModel vm)
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
