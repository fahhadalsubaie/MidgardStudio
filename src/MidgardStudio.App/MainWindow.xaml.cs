using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Threading;
using MidgardStudio.App.ViewModels;
using Wpf.Ui.Controls;

namespace MidgardStudio.App;

/// <summary>
/// Interaction logic for MainWindow.xaml. The shell is a WPF-UI FluentWindow with a Mica
/// backdrop; its DataContext is the <see cref="ShellViewModel"/>.
/// </summary>
public partial class MainWindow : FluentWindow
{
    private readonly ShellViewModel _viewModel;
    private bool _closing;

    public MainWindow(ShellViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.ShortcutsChanged += ApplyShortcuts;
        PaletteResultsBox.MouseDoubleClick += (_, _) =>
        {
            if (PaletteResultsBox.SelectedItem is PaletteResultViewModel result)
                _viewModel.ActivatePaletteResultCommand.Execute(result);
        };
        ApplyShortcuts();
    }

    /// <summary>Prompts to save unsaved edits before the window closes (Save / Don't save / Cancel).</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_closing && _viewModel.HasUnsavedChanges)
        {
            switch (Views.ConfirmDialog.AskSave("Unsaved changes",
                "You have unsaved changes. Save them before closing?"))
            {
                case Views.SavePrompt.Save:
                    if (!_viewModel.SaveForExit()) { e.Cancel = true; return; } // save failed → stay open
                    break;
                case Views.SavePrompt.Cancel:
                    e.Cancel = true; // stay open
                    return;
                // Discard → fall through and close without saving
            }
        }

        _closing = true;
        base.OnClosing(e);
    }

    /// <summary>Rebuilds the window's keyboard shortcuts from the (user-editable) settings.</summary>
    private void ApplyShortcuts()
    {
        InputBindings.Clear();
        foreach (var binding in _viewModel.BuildInputBindings())
            InputBindings.Add(binding);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.IsPaletteOpen) && _viewModel.IsPaletteOpen)
            Dispatcher.BeginInvoke(() => PaletteSearchBox.Focus(), DispatcherPriority.Input);
    }
}
