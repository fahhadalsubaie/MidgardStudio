using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MidgardStudio.App.Services;
using MidgardStudio.App.Views;
using MidgardStudio.Core.Commands;
using MidgardStudio.Core.Model;
using MidgardStudio.Core.Schemas;

namespace MidgardStudio.App.ViewModels;

/// <summary>One monster that drops the selected item.</summary>
public sealed class DroppedByRowViewModel
{
    public DroppedByRowViewModel(RecordKey mobKey, string mobName, int rate, bool isMvp)
    {
        MobKey = mobKey;
        MobName = mobName;
        Rate = rate;
        IsMvp = isMvp;
    }

    public RecordKey MobKey { get; }
    public string MobName { get; }
    public int Rate { get; }
    public bool IsMvp { get; }

    public int MobId => (int)MobKey.AsInt;
    public string RateText => $"{Rate / 100.0:0.##} %";
    public string Kind => IsMvp ? "MVP" : "Drop";
}

/// <summary>
/// Reverse "which monsters drop this item" card for the item editor. Lets you jump to a mob, add this
/// item as a normal/MVP drop to any mob (auto-overriding a base mob), and edit/remove existing drops.
/// </summary>
public sealed partial class DroppedByViewModel : ObservableObject
{
    private readonly DbRecord _item;
    private readonly EditCommandStack _stack;
    private readonly DropService _drops;
    private readonly Action<string, RecordKey>? _navigate;

    public DroppedByViewModel(DbRecord item, EditCommandStack stack, DropService drops, Action<string, RecordKey>? navigate)
    {
        _item = item;
        _stack = stack;
        _drops = drops;
        _navigate = navigate;
        Reload();
    }

    private string Aegis => _item.GetString("AegisName") ?? string.Empty;

    public ObservableCollection<DroppedByRowViewModel> Rows { get; } = new();
    public bool HasRows => Rows.Count > 0;
    public string Summary => Rows.Count == 0
        ? "Not dropped by any monster. Right-click to add a drop."
        : $"Dropped by {Rows.Count} monster(s).";

    public void Reload()
    {
        Rows.Clear();
        foreach (var occ in _drops.DroppedBy(Aegis).OrderByDescending(o => o.Rate))
            Rows.Add(new DroppedByRowViewModel(occ.MobKey, occ.MobName, occ.Rate, occ.IsMvp));
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(Summary));
    }

    [RelayCommand]
    private void SelectMob(DroppedByRowViewModel? row)
    {
        if (row is not null) _navigate?.Invoke("mob_db", row.MobKey);
    }

    [RelayCommand]
    private void AddNormalDrop() => AddTo(isMvp: false);

    [RelayCommand]
    private void AddMvpDrop() => AddTo(isMvp: true);

    private void AddTo(bool isMvp)
    {
        if (string.IsNullOrEmpty(Aegis)) return;
        var pick = new RecordPickerDialog($"Add {(isMvp ? "MVP" : "normal")} drop — select monster",
            (q, n) => _drops.SearchMobs(q, n)) { Owner = Application.Current.MainWindow };
        if (pick.ShowDialog() != true || pick.Selected is not { } mob) return;

        var dlg = new DropEditDialog(_drops, isMvp, _drops.ResolveItem(Aegis).Id, isMvp ? 5000 : 100, false, string.Empty)
        { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true) return;

        var mobRec = EditableMob(RecordKey.Of(mob.Id));
        if (mobRec is null) return;
        string field = isMvp ? "MvpDrops" : "Drops";
        var list = mobRec.GetList(field);
        if (list is null) { list = new List<DbRecord>(); mobRec.SetRaw(field, list); }

        var rec = new DbRecord(MobDbSchema.DropElement) { Owner = mobRec };
        rec.SetRaw("Item", dlg.Aegis);
        rec.SetRaw("Rate", dlg.Rate);
        if (!isMvp && dlg.StealProtected) rec.SetRaw("StealProtected", true);
        if (!string.IsNullOrEmpty(dlg.RandGroup)) rec.SetRaw("RandomOptionGroup", dlg.RandGroup);

        _stack.Execute(new ListMutateCommand($"Add {field.TrimEnd('s')} to {mobRec.GetString("Name")}",
            () => { if (!list.Contains(rec)) list.Add(rec); mobRec.IsDirty = true; Reload(); },
            () => { list.Remove(rec); mobRec.IsDirty = true; Reload(); }));
    }

    [RelayCommand]
    private void EditChance(DroppedByRowViewModel? row)
    {
        if (row is null) return;
        var mobRec = EditableMob(row.MobKey);
        string field = row.IsMvp ? "MvpDrops" : "Drops";
        var drop = FindDrop(mobRec, field);
        if (mobRec is null || drop is null) { Reload(); return; }

        var dlg = new DropEditDialog(_drops, row.IsMvp, _drops.ResolveItem(Aegis).Id, drop.GetInt("Rate"),
            drop.GetBool("StealProtected"), drop.GetString("RandomOptionGroup") ?? string.Empty) { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true) return;

        var old = (drop.GetInt("Rate"), drop.GetBool("StealProtected"), drop.GetString("RandomOptionGroup"));
        _stack.Execute(new ListMutateCommand($"Edit drop on {mobRec.GetString("Name")}",
            () => { drop.SetRaw("Rate", dlg.Rate); SetFlag(drop, "StealProtected", !row.IsMvp && dlg.StealProtected); SetStr(drop, "RandomOptionGroup", dlg.RandGroup); mobRec.IsDirty = true; Reload(); },
            () => { drop.SetRaw("Rate", old.Item1); SetFlag(drop, "StealProtected", old.Item2); SetStr(drop, "RandomOptionGroup", old.Item3 ?? string.Empty); mobRec.IsDirty = true; Reload(); }));
    }

    [RelayCommand]
    private void RemoveDrop(DroppedByRowViewModel? row)
    {
        if (row is null) return;
        var mobRec = EditableMob(row.MobKey);
        string field = row.IsMvp ? "MvpDrops" : "Drops";
        var list = mobRec?.GetList(field);
        var drop = FindDrop(mobRec, field);
        if (mobRec is null || list is null || drop is null) { Reload(); return; }

        int idx = list.IndexOf(drop);
        _stack.Execute(new ListMutateCommand($"Remove drop from {mobRec.GetString("Name")}",
            () => { list.Remove(drop); mobRec.IsDirty = true; Reload(); },
            () => { list.Insert(Math.Clamp(idx, 0, list.Count), drop); mobRec.IsDirty = true; Reload(); }));
    }

    /// <summary>Returns the editable (import) mob record, auto-overriding a base mob on first edit.</summary>
    private DbRecord? EditableMob(RecordKey key)
    {
        var ov = _drops.Mobs;
        return ov.OriginOf(key) == RecordOrigin.Base ? ov.BeginOverride(key) : ov.GetEffective(key);
    }

    private DbRecord? FindDrop(DbRecord? mob, string field) =>
        mob?.GetList(field)?.FirstOrDefault(d => string.Equals(d.GetString("Item"), Aegis, StringComparison.OrdinalIgnoreCase));

    private static void SetFlag(DbRecord d, string field, bool value) { if (value) d.SetRaw(field, true); else d.Remove(field); }
    private static void SetStr(DbRecord d, string field, string value) { if (string.IsNullOrEmpty(value)) d.Remove(field); else d.SetRaw(field, value); }
}
