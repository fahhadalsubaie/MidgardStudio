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

/// <summary>One row in a mob drop table — reads its display values live from the underlying drop record.</summary>
public sealed class DropRowViewModel : ObservableObject
{
    private readonly DropService _drops;

    public DropRowViewModel(DbRecord drop, DropService drops)
    {
        Drop = drop;
        _drops = drops;
    }

    public DbRecord Drop { get; }

    public string Aegis => Drop.GetString("Item") ?? string.Empty;
    public int ItemId => _drops.ResolveItem(Aegis).Id;
    public string ItemName => _drops.ResolveItem(Aegis).Name;
    public int Rate => Drop.GetInt("Rate");
    public string RateText => $"{Rate / 100.0:0.##} %";

    public void Refresh() => OnPropertyChanged(string.Empty);
}

/// <summary>Container for a mob's two drop tables (normal + MVP), each an editable card.</summary>
public sealed class MobDropsViewModel
{
    public MobDropsViewModel(DbRecord mob, bool isEditable, EditCommandStack stack, DropService drops, Action<string, RecordKey>? navigate)
    {
        IsEditable = isEditable;
        Normal = new DropListViewModel(mob, "Drops", "Normal drops", isMvp: false, isEditable, stack, drops, navigate);
        Mvp = new DropListViewModel(mob, "MvpDrops", "MVP drops", isMvp: true, isEditable, stack, drops, navigate);
    }

    public bool IsEditable { get; }
    public DropListViewModel Normal { get; }
    public DropListViewModel Mvp { get; }
}

/// <summary>One editable mob drop table (normal or MVP) with select / edit-chance / remove / copy / paste / add.</summary>
public sealed partial class DropListViewModel : ObservableObject
{
    private static DbRecord? _clipboard; // shared across all drop lists (Copy here, Paste there)

    private readonly DbRecord _mob;
    private readonly bool _isMvp;
    private readonly EditCommandStack _stack;
    private readonly DropService _drops;
    private readonly Action<string, RecordKey>? _navigate;
    private readonly IList<DbRecord> _list;

    public DropListViewModel(DbRecord mob, string field, string title, bool isMvp, bool isEditable,
        EditCommandStack stack, DropService drops, Action<string, RecordKey>? navigate)
    {
        _mob = mob;
        _isMvp = isMvp;
        Title = title;
        IsEditable = isEditable;
        _stack = stack;
        _drops = drops;
        _navigate = navigate;

        var existing = mob.GetList(field);
        if (existing is null)
        {
            existing = new List<DbRecord>();
            if (isEditable) mob.SetRaw(field, existing);
        }
        _list = existing;

        foreach (var d in _list) Rows.Add(new DropRowViewModel(d, drops));
    }

    public string Title { get; }
    public bool IsEditable { get; }
    public ObservableCollection<DropRowViewModel> Rows { get; } = new();
    public bool HasRows => Rows.Count > 0;

    [RelayCommand]
    private void SelectItem(DropRowViewModel? row)
    {
        if (row is { ItemId: > 0 }) _navigate?.Invoke("item_db", RecordKey.Of(row.ItemId));
    }

    [RelayCommand]
    private void AddDrop()
    {
        if (!IsEditable) return;
        var dlg = new DropEditDialog(_drops, _isMvp, 0, _isMvp ? 5000 : 100, false, string.Empty) { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true) return;

        var rec = NewDrop(dlg.Aegis, dlg.Rate, dlg.StealProtected, dlg.RandGroup);
        var row = new DropRowViewModel(rec, _drops);
        Mutate("add drop", () => { _list.Add(rec); Rows.Add(row); }, () => { _list.Remove(rec); Rows.Remove(row); });
    }

    [RelayCommand]
    private void EditDrop(DropRowViewModel? row)
    {
        if (!IsEditable || row is null) return;
        var d = row.Drop;
        var dlg = new DropEditDialog(_drops, _isMvp, row.ItemId, d.GetInt("Rate"),
            d.GetBool("StealProtected"), d.GetString("RandomOptionGroup") ?? string.Empty) { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true) return;

        var old = (d.GetString("Item"), d.GetInt("Rate"), d.GetBool("StealProtected"), d.GetString("RandomOptionGroup"));
        Mutate("edit drop",
            () => { Apply(d, dlg.Aegis, dlg.Rate, dlg.StealProtected, dlg.RandGroup); row.Refresh(); },
            () => { Apply(d, old.Item1 ?? string.Empty, old.Item2, old.Item3, old.Item4 ?? string.Empty); row.Refresh(); });
    }

    [RelayCommand]
    private void RemoveDrop(DropRowViewModel? row)
    {
        if (!IsEditable || row is null) return;
        int idx = _list.IndexOf(row.Drop);
        Mutate("remove drop",
            () => { _list.Remove(row.Drop); Rows.Remove(row); },
            () => { _list.Insert(Math.Clamp(idx, 0, _list.Count), row.Drop); Rows.Insert(Math.Clamp(idx, 0, Rows.Count), row); });
    }

    [RelayCommand]
    private void CopyDrop(DropRowViewModel? row)
    {
        if (row is not null) { _clipboard = row.Drop.DeepClone(); PasteDropCommand.NotifyCanExecuteChanged(); }
    }

    [RelayCommand(CanExecute = nameof(CanPaste))]
    private void PasteDrop()
    {
        if (!IsEditable || _clipboard is null) return;
        var rec = _clipboard.DeepClone();
        rec.Owner = _mob;
        var row = new DropRowViewModel(rec, _drops);
        Mutate("paste drop", () => { _list.Add(rec); Rows.Add(row); }, () => { _list.Remove(rec); Rows.Remove(row); });
    }

    private bool CanPaste() => IsEditable && _clipboard is not null;

    private DbRecord NewDrop(string aegis, int rate, bool steal, string rand)
    {
        var rec = new DbRecord(MobDbSchema.DropElement) { Owner = _mob };
        Apply(rec, aegis, rate, steal, rand);
        return rec;
    }

    private static void Apply(DbRecord d, string aegis, int rate, bool steal, string rand)
    {
        d.SetRaw("Item", aegis);
        d.SetRaw("Rate", rate);
        if (steal) d.SetRaw("StealProtected", true); else d.Remove("StealProtected");
        if (!string.IsNullOrEmpty(rand)) d.SetRaw("RandomOptionGroup", rand); else d.Remove("RandomOptionGroup");
    }

    private void Mutate(string desc, Action @do, Action undo)
    {
        _stack.Execute(new ListMutateCommand($"{_mob.Schema.DisplayName}: {desc}",
            () => { @do(); _mob.IsDirty = true; OnPropertyChanged(nameof(HasRows)); },
            () => { undo(); _mob.IsDirty = true; OnPropertyChanged(nameof(HasRows)); }));
    }
}
