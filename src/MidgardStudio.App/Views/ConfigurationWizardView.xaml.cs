using System.Windows.Controls;
using System.Windows.Input;
using MidgardStudio.App.ViewModels;

namespace MidgardStudio.App.Views;

public partial class ConfigurationWizardView : UserControl
{
    public ConfigurationWizardView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Esc closes the configuration window. Handled at the tunneling (Preview) stage on the view root so it
    /// fires reliably even when a focused child (a TextBox, the profiles list, etc.) would otherwise swallow
    /// the key — more robust than a bubbling KeyBinding on the host overlay.
    /// </summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is ConfigurationWizardViewModel vm && vm.CloseCommand.CanExecute(null))
        {
            vm.CloseCommand.Execute(null);
            e.Handled = true;
        }
    }
}
