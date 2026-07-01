using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MidgardStudio.App.Services;
using MidgardStudio.Core.Scripting;

namespace MidgardStudio.App.ViewModels;

/// <summary>A nested skill-level gate inside a combo tier: extra bonuses when the wearer knows a skill at a
/// given level.</summary>
public sealed partial class ComboSkillGateRowViewModel : ObservableObject
{
    private readonly ComboConditionalBonusBuilderViewModel _owner;

    public ComboSkillGateRowViewModel(ComboConditionalBonusBuilderViewModel owner, string skill, int level, IEnumerable<string> bonuses)
    {
        _owner = owner;
        _skill = skill;
        _level = level;
        foreach (var b in bonuses) Bonuses.Add(b);
    }

    [ObservableProperty] private string _skill;
    [ObservableProperty] private int _level;
    partial void OnSkillChanged(string value) => _owner.Refresh();
    partial void OnLevelChanged(int value) => _owner.Refresh();

    public ObservableCollection<string> Bonuses { get; } = new();
    public bool CanPickSkill => _owner.CanPickSkill;

    [RelayCommand] private void AddBonus() => _owner.AddBonusTo(Bonuses);
    [RelayCommand] private void RemoveBonus(string? line) { if (line is not null && Bonuses.Remove(line)) _owner.Refresh(); }
    [RelayCommand] private void PickSkill() => _owner.PickSkillInto(this);
    [RelayCommand] private void Remove() => _owner.RemoveGate(this);
}

/// <summary>One combo tier: a compound condition (total refine and/or all-pieces grade) plus bonuses and
/// nested skill gates.</summary>
public sealed partial class ComboTierRowViewModel : ObservableObject
{
    private readonly ComboConditionalBonusBuilderViewModel _owner;

    public ComboTierRowViewModel(ComboConditionalBonusBuilderViewModel owner, int? refine, int? grade,
        IEnumerable<string> bonuses, IEnumerable<ComboSkillGateRowViewModel> gates)
    {
        _owner = owner;
        _useRefine = refine is not null;
        _refineTotal = refine ?? 20;
        _useGrade = grade is not null;
        _grade = grade ?? 4;
        foreach (var b in bonuses) Bonuses.Add(b);
        foreach (var g in gates) SkillGates.Add(g);
    }

    [ObservableProperty] private bool _useRefine;
    [ObservableProperty] private int _refineTotal;
    [ObservableProperty] private bool _useGrade;
    [ObservableProperty] private int _grade;
    partial void OnUseRefineChanged(bool value) => _owner.Refresh();
    partial void OnRefineTotalChanged(int value) => _owner.Refresh();
    partial void OnUseGradeChanged(bool value) => _owner.Refresh();
    partial void OnGradeChanged(int value) => _owner.Refresh();

    public bool CanUseGrade => _owner.CanUseGrade;
    public IReadOnlyList<GradeOption> GradeOptions => ConditionalTierViewModel.GradeOptions;

    public ObservableCollection<string> Bonuses { get; } = new();
    public ObservableCollection<ComboSkillGateRowViewModel> SkillGates { get; } = new();

    [RelayCommand] private void AddBonus() => _owner.AddBonusTo(Bonuses);
    [RelayCommand] private void RemoveBonus(string? line) { if (line is not null && Bonuses.Remove(line)) _owner.Refresh(); }
    [RelayCommand] private void AddSkillGate()
    {
        SkillGates.Add(new ComboSkillGateRowViewModel(_owner, string.Empty, 1, Array.Empty<string>()));
        _owner.Refresh();
    }
    [RelayCommand] private void Remove() => _owner.RemoveTier(this);

    public ComboTier ToModel() => new(
        UseRefine ? RefineTotal : null,
        UseGrade && CanUseGrade ? Grade : null,
        Bonuses.ToList(),
        SkillGates.Select(g => new ComboSkillGate(g.Skill, g.Level, g.Bonuses.ToList())).ToList());
}

/// <summary>
/// Backs the combo Refine/Grade builder: assemble compound-condition tiers (total refine across the combo's
/// pieces and/or a per-piece enchant grade) with nested skill gates and unconditional bonuses, watch the
/// generated script + preview update live, then write a managed block into the combo's shared script. The
/// piece equip slots are derived by the caller from the combo's items; grade is hidden on a pre-re profile.
/// </summary>
public sealed partial class ComboConditionalBonusBuilderViewModel : ObservableObject
{
    private readonly bool _renewal;
    private readonly Func<string, string?>? _resolveSkill;
    private readonly SkillLookupService? _skillLookup;
    private readonly IReadOnlyList<string> _eqi;
    private readonly string _originalScript;

    public ComboConditionalBonusBuilderViewModel(string? script, IReadOnlyList<string> eqiSlots, bool renewal, SkillLookupService? skill)
    {
        _renewal = renewal;
        _skillLookup = skill;
        _resolveSkill = skill is { } s ? s.Display : null;
        _eqi = eqiSlots;
        _originalScript = script ?? string.Empty;
        EqiSummary = eqiSlots.Count == 0 ? "(no equippable pieces found in this combo)" : string.Join("  +  ", eqiSlots);

        var existing = ComboConditionalScript.TryParse(_originalScript);
        if (existing is not null)
        {
            foreach (var u in existing.Unconditional) Unconditional.Add(u);
            foreach (var t in existing.Tiers)
            {
                var gates = t.SkillGates.Select(g => new ComboSkillGateRowViewModel(this, g.Skill, g.Level, g.Bonuses));
                Tiers.Add(new ComboTierRowViewModel(this, t.RefineTotal, t.Grade, t.Bonuses, gates));
            }
        }
        Refresh();
    }

    public bool CanUseGrade => _renewal;
    public bool CanPickSkill => _skillLookup is not null;
    public string EqiSummary { get; }

    public ObservableCollection<ComboTierRowViewModel> Tiers { get; } = new();
    public ObservableCollection<string> Unconditional { get; } = new();

    [ObservableProperty] private string _scriptPreview = string.Empty;
    [ObservableProperty] private string _descriptionPreview = string.Empty;

    [RelayCommand]
    private void AddTier()
    {
        Tiers.Add(new ComboTierRowViewModel(this, 15, _renewal ? 4 : null, Array.Empty<string>(), Array.Empty<ComboSkillGateRowViewModel>()));
        Refresh();
    }

    internal void RemoveTier(ComboTierRowViewModel tier) { Tiers.Remove(tier); Refresh(); }

    internal void RemoveGate(ComboSkillGateRowViewModel gate)
    {
        foreach (var t in Tiers) if (t.SkillGates.Remove(gate)) break;
        Refresh();
    }

    [RelayCommand] private void AddUnconditionalBonus() => AddBonusTo(Unconditional);
    [RelayCommand] private void RemoveUnconditionalBonus(string? line) { if (line is not null && Unconditional.Remove(line)) Refresh(); }

    internal void AddBonusTo(ObservableCollection<string> target)
    {
        var dlg = new Views.BonusBuilderDialog(_renewal) { Owner = System.Windows.Application.Current.MainWindow };
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.Result))
        {
            foreach (var line in dlg.Result.Replace("\r", string.Empty).Split('\n'))
                if (line.Trim().Length > 0) target.Add(line.Trim());
            Refresh();
        }
    }

    internal void PickSkillInto(ComboSkillGateRowViewModel gate)
    {
        if (_skillLookup is null) return;
        var dlg = new Views.SkillPickerDialog(_skillLookup) { Owner = System.Windows.Application.Current.MainWindow };
        if (dlg.ShowDialog() == true && dlg.Selected is { } s) gate.Skill = s.Aegis;
    }

    internal void Refresh()
    {
        var model = BuildModel();
        ScriptPreview = model.Emit();
        DescriptionPreview = string.Join(Environment.NewLine, model.Describe(_resolveSkill));
    }

    private ComboConditionalScript BuildModel() =>
        new(_eqi, Tiers.Select(t => t.ToModel()).ToList(), Unconditional.ToList());

    /// <summary>The combo script with the managed block replaced by the current model (empty → block removed).</summary>
    public string BuildScript()
    {
        string stripped = ComboConditionalScript.StripManagedBlock(_originalScript).TrimEnd('\r', '\n', ' ', '\t');
        string block = BuildModel().Emit().TrimEnd('\n');
        if (block.Length == 0) return stripped;
        return stripped.Length == 0 ? block : stripped + "\n" + block;
    }
}
