using MidgardStudio.App.ViewModels;
using Wpf.Ui.Controls;

namespace MidgardStudio.App.Views;

/// <summary>Visual builder for a single rAthena item bonus statement. Exposes <see cref="Statement"/> on Insert.</summary>
public partial class BonusBuilderDialog : FluentWindow
{
    private readonly BonusBuilderViewModel _vm = new();

    public BonusBuilderDialog()
    {
        InitializeComponent();
        DataContext = _vm;
        InsertButton.Click += (_, _) => { Statement = _vm.Preview; DialogResult = true; Close(); };
        CancelButton.Click += (_, _) => { DialogResult = false; Close(); };
    }

    /// <summary>The generated bonus statement (e.g. <c>bonus2 bAddRace,RC_DemiHuman,5;</c>).</summary>
    public string Statement { get; private set; } = string.Empty;
}
