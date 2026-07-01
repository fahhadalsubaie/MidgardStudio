using Microsoft.Extensions.DependencyInjection;
using MidgardStudio.App.Services;
using MidgardStudio.App.ViewModels;
using Wpf.Ui.Controls;

namespace MidgardStudio.App.Views;

/// <summary>Refine / Grade conditional-bonus builder. Seeded from the current item script's managed block (if
/// present) and exposes the updated script (<see cref="ResultScript"/>) on Save. Grade tiers are hidden on a
/// Pre-Renewal profile.</summary>
public partial class ConditionalBonusBuilderDialog : FluentWindow
{
    private readonly ConditionalBonusBuilderViewModel _vm;

    /// <param name="renewal">False on a Pre-Renewal profile — hides grade tiers (enchant grade is renewal-only)
    /// and the renewal-only per-tier effects. The caller passes the active mode (reliable; no DI lookup).</param>
    public ConditionalBonusBuilderDialog(string? currentScript, bool renewal)
    {
        InitializeComponent();

        SkillLookupService? skills = null;
        try { skills = App.Services.GetService<SkillLookupService>(); } catch { /* host not ready */ }

        _vm = new ConditionalBonusBuilderViewModel(currentScript, renewal, skills);
        DataContext = _vm;

        InsertButton.Click += (_, _) => { ResultScript = _vm.BuildScript(); DialogResult = true; Close(); };
        CancelButton.Click += (_, _) => { DialogResult = false; Close(); };
    }

    /// <summary>The item script with the conditional managed block updated to the built tiers.</summary>
    public string ResultScript { get; private set; } = string.Empty;
}
