using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using MidgardStudio.App.Services;
using MidgardStudio.Core.Commands;
using MidgardStudio.Core.Lua;

namespace MidgardStudio.App.ViewModels;

/// <summary>
/// Edits one client skill with GUI-driven fields: per-level values (SP, range, the four delays) are edited
/// as auto-sized strips of boxes, and prerequisites are picked from skill_db rather than hand-typed. Every
/// edit routes through the undo stack and re-evaluates the service's dirty state.
/// </summary>
public sealed partial class ClientSkillEditorViewModel : ObservableObject
{
    private readonly ClientSkill _skill;
    private readonly ClientSkillService _service;
    private readonly EditCommandStack _stack;
    private readonly List<PerLevel> _levelFields = new();
    private bool _ready;

    private sealed record PerLevel(PerLevelFieldViewModel Field, Func<List<int>> Get, Action<List<int>> Set, bool IsDelay);

    public ClientSkillEditorViewModel(ClientSkill skill, ClientSkillService service, EditCommandStack stack)
    {
        _skill = skill;
        _service = service;
        _stack = stack;

        SpField = MakeField("SP cost", "SP spent per level", () => _skill.SpAmount, v => _skill.SpAmount = v, isDelay: false);
        RangeField = MakeField("Attack range", "range in cells per level", () => _skill.AttackRange, v => _skill.AttackRange = v, isDelay: false);
        CastFixedField = MakeField("Fixed cast", "uninterruptible cast, ms", () => _skill.CastFixedDelay, v => _skill.CastFixedDelay = v, isDelay: true);
        CastVarField = MakeField("Variable cast", "reducible cast, ms", () => _skill.CastStatDelay, v => _skill.CastStatDelay = v, isDelay: true);
        GlobalCdField = MakeField("Global cooldown", "shared after-cast delay, ms", () => _skill.GlobalPostDelay, v => _skill.GlobalPostDelay = v, isDelay: true);
        SingleCdField = MakeField("Skill cooldown", "this skill's cooldown, ms", () => _skill.SinglePostDelay, v => _skill.SinglePostDelay = v, isDelay: true);

        foreach (var p in _skill.NeedSkillList)
            Prereqs.Add(new PrereqRowViewModel(p.Skid, ResolveName(p.Skid), p.Level, CommitPrereqs, RemovePrereqRow));

        _ready = true;
    }

    public string Constant => _skill.Constant;
    public int Id => _skill.Id;
    public string Header => $"{_skill.Constant}  (id {_skill.Id})";

    public PerLevelFieldViewModel SpField { get; }
    public PerLevelFieldViewModel RangeField { get; }
    public PerLevelFieldViewModel CastFixedField { get; }
    public PerLevelFieldViewModel CastVarField { get; }
    public PerLevelFieldViewModel GlobalCdField { get; }
    public PerLevelFieldViewModel SingleCdField { get; }

    public ObservableCollection<PrereqRowViewModel> Prereqs { get; } = new();

    public string SkillName
    {
        get => _skill.SkillName;
        set { var o = _skill.SkillName; if (o != value) Commit("skill name", () => _skill.SkillName = value, () => _skill.SkillName = o, nameof(SkillName)); }
    }

    public int MaxLv
    {
        get => _skill.MaxLv;
        set
        {
            if (!_ready || value < 1 || (value == _skill.MaxLv && _skill.HasMaxLv)) return;
            int old = _skill.MaxLv; bool oldHas = _skill.HasMaxLv;
            // One undo step: change MaxLv, resize every per-level strip to match, commit any array that grew/shrank.
            using (_stack.BeginBatch("Client skill: max level"))
            {
                _stack.Execute(new ListMutateCommand("max level",
                    () => { _skill.MaxLv = value; _skill.HasMaxLv = true; _service.NotifyEdited(_skill.Constant); OnPropertyChanged(nameof(MaxLv)); },
                    () => { _skill.MaxLv = old; _skill.HasMaxLv = oldHas; _service.NotifyEdited(_skill.Constant); OnPropertyChanged(nameof(MaxLv)); }));
                foreach (var pl in _levelFields)
                {
                    pl.Field.SetMaxLv(value);
                    CommitField(pl);
                }
            }
        }
    }

    public string Description
    {
        get => string.Join(Environment.NewLine, _skill.Description);
        set
        {
            var oDesc = _skill.Description; bool oHas = _skill.HasDescript;
            var n = value.Replace("\r\n", "\n").Split('\n').ToList();
            Commit("description",
                () => { _skill.Description = n; _skill.HasDescript = oHas || n.Count > 0; },
                () => { _skill.Description = oDesc; _skill.HasDescript = oHas; },
                nameof(Description));
        }
    }

    [RelayCommand]
    private void AddPrereq()
    {
        SkillLookupService? lookup = null;
        try { lookup = App.Services.GetService<SkillLookupService>(); } catch { /* host not ready */ }
        if (lookup is null) return;

        var dlg = new Views.SkillPickerDialog(lookup) { Owner = System.Windows.Application.Current.MainWindow };
        if (dlg.ShowDialog() != true || dlg.Selected is not { } sel) return;

        string skid = sel.Aegis;
        if (string.IsNullOrWhiteSpace(skid) || Prereqs.Any(p => string.Equals(p.Skid, skid, StringComparison.Ordinal))) return;

        Prereqs.Add(new PrereqRowViewModel(skid, ResolveName(skid, sel.Display), 1, CommitPrereqs, RemovePrereqRow));
        CommitPrereqs();
    }

    private void RemovePrereqRow(PrereqRowViewModel row)
    {
        Prereqs.Remove(row);
        CommitPrereqs();
    }

    [RelayCommand]
    private void CopyLua()
    {
        var sb = new System.Text.StringBuilder();
        if (_skill.HasInfo) sb.Append("-- SKILL_INFO_LIST\n").Append(ClientSkillWriter.FormatInfo(_skill)).Append('\n');
        if (_skill.Description.Count > 0) sb.Append("-- SKILL_DESCRIPT\n").Append(ClientSkillWriter.FormatDescript(_skill)).Append('\n');
        if (_skill.HasDelay) sb.Append("-- SKILL_DELAY_LIST\n").Append(ClientSkillWriter.FormatDelay(_skill));
        try { System.Windows.Clipboard.SetText(sb.ToString()); } catch { /* clipboard busy */ }
    }

    // ----- helpers -----

    private PerLevelFieldViewModel MakeField(string label, string hint, Func<List<int>> get, Action<List<int>> set, bool isDelay)
    {
        PerLevel pl = null!;
        var field = new PerLevelFieldViewModel(label, hint, get(), _skill.MaxLv, () => CommitField(pl));
        pl = new PerLevel(field, get, set, isDelay);
        _levelFields.Add(pl);
        return field;
    }

    private void CommitField(PerLevel pl)
    {
        if (!_ready) return;
        var oldV = new List<int>(pl.Get());
        var newV = pl.Field.Snapshot();
        if (oldV.SequenceEqual(newV)) return;

        _stack.Execute(new ListMutateCommand("Client skill: " + pl.Field.Label,
            () => { pl.Set(newV); if (pl.IsDelay) UpdateHasDelay(); _service.NotifyEdited(_skill.Constant); },
            () => { pl.Set(oldV); if (pl.IsDelay) UpdateHasDelay(); _service.NotifyEdited(_skill.Constant); }));
    }

    private void CommitPrereqs()
    {
        if (!_ready) return;
        var oldV = _skill.NeedSkillList;
        var newV = Prereqs.Select(p => new SkillPrereq(p.Skid, p.Level < 1 ? 1 : p.Level)).ToList();
        if (oldV.Count == newV.Count && oldV.Zip(newV).All(z => z.First == z.Second)) return;

        _stack.Execute(new ListMutateCommand("Client skill: prerequisites",
            () => { _skill.NeedSkillList = newV; _service.NotifyEdited(_skill.Constant); },
            () => { _skill.NeedSkillList = oldV; _service.NotifyEdited(_skill.Constant); }));
    }

    private void UpdateHasDelay() =>
        _skill.HasDelay = _skill.CastFixedDelay.Count > 0 || _skill.CastStatDelay.Count > 0
            || _skill.GlobalPostDelay.Count > 0 || _skill.SinglePostDelay.Count > 0 || _skill.SkillFlag.Count > 0;

    private void Commit(string description, Action apply, Action revert, string propertyName)
    {
        _stack.Execute(new ListMutateCommand(
            "Client skill: " + description,
            () => { apply(); _service.NotifyEdited(_skill.Constant); OnPropertyChanged(propertyName); },
            () => { revert(); _service.NotifyEdited(_skill.Constant); OnPropertyChanged(propertyName); }));
    }

    /// <summary>A friendly label for a SKID prerequisite: the client skill's display name when known.</summary>
    private string ResolveName(string skid, string? fallback = null)
    {
        string name = _service.Get(skid)?.SkillName ?? fallback ?? string.Empty;
        return string.IsNullOrWhiteSpace(name) || string.Equals(name, skid, StringComparison.Ordinal) ? skid : $"{name}  ·  {skid}";
    }
}
