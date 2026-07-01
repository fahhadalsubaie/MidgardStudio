using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MidgardStudio.App.Services;
using MidgardStudio.Core.Scripting;

namespace MidgardStudio.App.ViewModels;

/// <summary>A selectable enchant-grade for a grade tier's picker.</summary>
public readonly record struct GradeOption(int Value, string Label);

/// <summary>One condition tier in the builder: a threshold plus the generated bonus lines it grants.</summary>
public sealed partial class ConditionalTierViewModel : ObservableObject
{
    private readonly ConditionalBonusBuilderViewModel _owner;

    public ConditionalTierViewModel(ConditionalBonusBuilderViewModel owner, ConditionKind kind, int threshold, IEnumerable<string> bonuses)
    {
        _owner = owner;
        Kind = kind;
        _threshold = threshold;
        foreach (var b in bonuses) Bonuses.Add(b);
    }

    public ConditionKind Kind { get; }
    public bool IsRefine => Kind == ConditionKind.Refine;
    public bool IsGrade => Kind == ConditionKind.Grade;

    [ObservableProperty] private int _threshold;
    partial void OnThresholdChanged(int value) => _owner.Refresh();

    public ObservableCollection<string> Bonuses { get; } = new();

    public static IReadOnlyList<GradeOption> GradeOptions { get; } = new[]
    {
        new GradeOption(1, "D"), new GradeOption(2, "C"), new GradeOption(3, "B"), new GradeOption(4, "A"),
    };

    [RelayCommand] private void AddBonus() => _owner.AddBonusTo(this);
    [RelayCommand] private void RemoveBonus(string? line) { if (line is not null && Bonuses.Remove(line)) _owner.Refresh(); }
    [RelayCommand] private void RemoveTier() => _owner.RemoveTier(this);
}

/// <summary>
/// Backs the Refine / Grade conditional-bonus builder: assemble ascending refine and grade tiers (each a
/// threshold plus bonuses picked with the visual generator), watch the generated nested rAthena script and the
/// client description update live, then write a managed block into the item script. Grade tiers are unavailable
/// on a Pre-Renewal profile (enchant grade is a renewal system).
/// </summary>
public sealed partial class ConditionalBonusBuilderViewModel : ObservableObject
{
    private readonly bool _renewal;
    private readonly Func<string, string?>? _resolveSkill;
    private readonly string _originalScript;

    public ConditionalBonusBuilderViewModel(string? script, bool renewal, SkillLookupService? skill)
    {
        _renewal = renewal;
        _resolveSkill = skill is { } s ? s.Display : null;
        _originalScript = script ?? string.Empty;

        var existing = ConditionalScript.TryParse(_originalScript);
        if (existing is not null)
            foreach (var ladder in existing.Ladders)
                foreach (var tier in ladder.Tiers)
                    Collection(ladder.Kind).Add(new ConditionalTierViewModel(this, ladder.Kind, tier.Threshold, tier.Bonuses));

        Refresh();
    }

    /// <summary>Grade tiers only apply on a Renewal profile.</summary>
    public bool CanUseGrade => _renewal;

    public ObservableCollection<ConditionalTierViewModel> RefineTiers { get; } = new();
    public ObservableCollection<ConditionalTierViewModel> GradeTiers { get; } = new();

    [ObservableProperty] private string _scriptPreview = string.Empty;
    [ObservableProperty] private string _descriptionPreview = string.Empty;

    private ObservableCollection<ConditionalTierViewModel> Collection(ConditionKind kind) =>
        kind == ConditionKind.Grade ? GradeTiers : RefineTiers;

    [RelayCommand]
    private void AddRefineTier()
    {
        int next = RefineTiers.Count == 0 ? 7 : Math.Min(20, RefineTiers.Max(t => t.Threshold) + 2);
        RefineTiers.Add(new ConditionalTierViewModel(this, ConditionKind.Refine, next, Array.Empty<string>()));
        Refresh();
    }

    [RelayCommand]
    private void AddGradeTier()
    {
        if (!_renewal) return;
        int next = GradeTiers.Count == 0 ? 1 : Math.Min(4, GradeTiers.Max(t => t.Threshold) + 1);
        GradeTiers.Add(new ConditionalTierViewModel(this, ConditionKind.Grade, next, Array.Empty<string>()));
        Refresh();
    }

    internal void RemoveTier(ConditionalTierViewModel tier)
    {
        if (!RefineTiers.Remove(tier)) GradeTiers.Remove(tier);
        Refresh();
    }

    internal void AddBonusTo(ConditionalTierViewModel tier)
    {
        // Reuse the visual bonus generator, passing our mode so it hides renewal-only effects in pre-re.
        var dlg = new Views.BonusBuilderDialog(_renewal) { Owner = System.Windows.Application.Current.MainWindow };
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.Result))
        {
            foreach (var line in dlg.Result.Replace("\r", string.Empty).Split('\n'))
                if (line.Trim().Length > 0) tier.Bonuses.Add(line.Trim());
            Refresh();
        }
    }

    internal void Refresh()
    {
        var model = BuildModel();
        ScriptPreview = model.Emit();
        DescriptionPreview = string.Join(Environment.NewLine, model.Describe(useColors: true, _resolveSkill));
    }

    private ConditionalScript BuildModel()
    {
        var ladders = new List<ConditionLadder>();
        if (RefineTiers.Count > 0)
            ladders.Add(new ConditionLadder(ConditionKind.Refine, RefineTiers.OrderBy(t => t.Threshold).Select(ToTier).ToList()));
        if (GradeTiers.Count > 0)
            ladders.Add(new ConditionLadder(ConditionKind.Grade, GradeTiers.OrderBy(t => t.Threshold).Select(ToTier).ToList()));
        return new ConditionalScript(ladders);
    }

    private static ConditionTier ToTier(ConditionalTierViewModel t) => new(t.Threshold, t.Bonuses.ToList());

    /// <summary>The item script with the managed block replaced by the current model (empty model → block removed).</summary>
    public string BuildScript()
    {
        string stripped = ConditionalScript.StripManagedBlock(_originalScript).TrimEnd('\r', '\n', ' ', '\t');
        string block = BuildModel().Emit().TrimEnd('\n');
        if (block.Length == 0) return stripped;
        return stripped.Length == 0 ? block : stripped + "\n" + block;
    }
}
