using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using MidgardStudio.App.Services;
using MidgardStudio.App.ViewModels;
using Wpf.Ui.Controls;

namespace MidgardStudio.App.Views;

/// <summary>Combo Refine/Grade conditional-bonus builder. Seeded from the combo's shared script (managed block,
/// if present) and the pieces' derived equip slots; exposes the updated script (<see cref="ResultScript"/>) on
/// Save. Grade tiers are hidden on a Pre-Renewal profile (the caller passes the active mode).</summary>
public partial class ComboConditionalBonusBuilderDialog : FluentWindow
{
    private readonly ComboConditionalBonusBuilderViewModel _vm;

    public ComboConditionalBonusBuilderDialog(string? currentScript, IReadOnlyList<string> eqiSlots, bool renewal)
    {
        InitializeComponent();
        SkillLookupService? skills = null;
        try { skills = App.Services.GetService<SkillLookupService>(); } catch { /* host not ready */ }
        _vm = new ComboConditionalBonusBuilderViewModel(currentScript, eqiSlots, renewal, skills);
        DataContext = _vm;
        InsertButton.Click += (_, _) => { ResultScript = _vm.BuildScript(); DialogResult = true; Close(); };
        CancelButton.Click += (_, _) => { DialogResult = false; Close(); };
    }

    /// <summary>The combo script with the conditional managed block updated to the built tiers.</summary>
    public string ResultScript { get; private set; } = string.Empty;
}
