using Microsoft.Extensions.DependencyInjection;
using MidgardStudio.App.Services;
using MidgardStudio.App.ViewModels;
using Wpf.Ui.Controls;

namespace MidgardStudio.App.Views;

/// <summary>Visual script generator for rAthena item bonuses. Exposes the generated <see cref="Result"/>
/// (the whole assembled recipe, or the single configured effect if nothing was added) on Insert.</summary>
public partial class BonusBuilderDialog : FluentWindow
{
    private readonly BonusBuilderViewModel _vm;

    public BonusBuilderDialog()
    {
        InitializeComponent();
        // Pull the skill index from the host so skill-typed params get a real picker (null-safe if absent).
        SkillLookupService? skills = null;
        try { skills = App.Services.GetService<SkillLookupService>(); } catch { /* host not ready — text fallback */ }
        _vm = new BonusBuilderViewModel(skills);
        DataContext = _vm;
        InsertButton.Click += (_, _) => { Result = _vm.Result; DialogResult = true; Close(); };
        CancelButton.Click += (_, _) => { DialogResult = false; Close(); };
    }

    /// <summary>The generated script — one or more <c>bonus…;</c> lines to append to the item script.</summary>
    public string Result { get; private set; } = string.Empty;
}
