using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MidgardStudio.App.Services;
using MidgardStudio.Core.Commands;
using MidgardStudio.Core.Model;
using MidgardStudio.Core.Overlay;
using MidgardStudio.Core.Schema;
using MidgardStudio.Core.Scripting;
using MidgardStudio.Core.Workspace;

namespace MidgardStudio.App.ViewModels;

/// <summary>
/// Dedicated editor for <c>item_combos</c> — a keyless database where each entry is a <b>combo set</b>:
/// one shared bonus <c>Script</c> plus one or more alternative item-sets (<c>Combo</c>) that each trigger
/// it (e.g. the refined and un-refined variants of the same gear). The generic grid can't represent this
/// (no id/name, a list-of-lists), so this screen renders combos as readable item groups with a proper
/// item picker and a prominent script editor. All edits go through the undo stack and the import overlay,
/// so Save All persists them like any other database.
/// </summary>
public sealed partial class ComboEditorViewModel : ObservableObject, IDisposable
{
    private readonly WorkspaceSession _session;
    private readonly DbSchema _schema;
    private readonly DbSchema _comboElement; // nested "Combo" alternative schema
    private readonly DropService _drops;
    private OverlayTable? _overlay;

    public ComboEditorViewModel(WorkspaceSession session, DbSchema schema, DropService drops)
    {
        _session = session;
        _schema = schema;
        _drops = drops;
        _comboElement = _schema.Field("Combos")!.ObjectSchema!;
        RowsView = CollectionViewSource.GetDefaultView(Rows);
        RowsView.Filter = MatchesSearch;
        _session.Commands.UndoRedoPerformed += OnUndoRedo;
    }

    /// <summary>After an undo/redo, refresh the script editor + selected row summary (structural changes
    /// are already reflected by the command do/undo delegates that mutate the bound collections).</summary>
    private void OnUndoRedo()
    {
        OnPropertyChanged(nameof(Script));
        SelectedRow?.Refresh();
    }

    public void Dispose() => _session.Commands.UndoRedoPerformed -= OnUndoRedo;

    private EditCommandStack Stack => _session.Commands;

    [ObservableProperty] private bool _isLoading = true;

    public ObservableCollection<ComboSetRowViewModel> Rows { get; } = new();

    /// <summary>Searchable view of the combo sets bound to the master list.</summary>
    public ICollectionView RowsView { get; }

    [ObservableProperty] private string _searchText = string.Empty;
    partial void OnSearchTextChanged(string value) => RowsView.Refresh();

    private bool MatchesSearch(object o)
    {
        if (string.IsNullOrWhiteSpace(SearchText) || o is not ComboSetRowViewModel row) return true;
        string q = SearchText.Trim();
        return row.PrimaryText.Contains(q, StringComparison.OrdinalIgnoreCase)
            || row.SecondaryText.Contains(q, StringComparison.OrdinalIgnoreCase)
            || row.ScriptText.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    [ObservableProperty] private ComboSetRowViewModel? _selectedRow;

    /// <summary>The alternative item-sets of the selected combo (the detail pane).</summary>
    public ObservableCollection<ComboAlternativeViewModel> Alternatives { get; } = new();

    public bool HasSelection => SelectedRow is not null;
    public bool IsEditable => SelectedRow is { IsCustom: true };
    public bool IsBaseSelected => SelectedRow is { IsCustom: false };

    partial void OnSelectedRowChanged(ComboSetRowViewModel? value) => RebuildDetail();

    private void RebuildDetail()
    {
        Alternatives.Clear();
        if (SelectedRow is { } row)
            foreach (var alt in row.AlternativeRecords())
                Alternatives.Add(new ComboAlternativeViewModel(this, alt));

        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(IsEditable));
        OnPropertyChanged(nameof(IsBaseSelected));
        OnPropertyChanged(nameof(Script));
    }

    /// <summary>The selected combo set's shared bonus script (committed on edit).</summary>
    public string Script
    {
        get => SelectedRow?.Record.GetScript("Script")?.Text ?? string.Empty;
        set
        {
            if (SelectedRow is not { IsCustom: true } row || value == Script) return;
            Stack.Execute(new SetFieldCommand(row.Record, "Script", new ScriptValue(value)));
            row.Refresh();
            OnPropertyChanged(nameof(Script));
        }
    }

    /// <summary>Opens the visual bonus builder and appends the generated statement to the script.</summary>
    [RelayCommand]
    private void InsertBonus()
    {
        if (SelectedRow is not { IsCustom: true }) return;
        var dlg = new Views.BonusBuilderDialog(_session.Mode != Core.Workspace.ServerMode.PreRenewal) { Owner = System.Windows.Application.Current.MainWindow };
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.Result))
        {
            var current = Script;
            Script = string.IsNullOrWhiteSpace(current) ? dlg.Result : current.TrimEnd() + "\n" + dlg.Result;
        }
    }

    /// <summary>Opens the combo Refine/Grade tier builder, seeded from the combo's script and its pieces'
    /// equip slots, and writes back the generated conditional block.</summary>
    [RelayCommand]
    private void ConditionalBonuses()
    {
        if (SelectedRow is not { IsCustom: true } row) return;
        var eqi = DeriveEqiSlots(row);
        if (eqi.Count == 0)
        {
            Views.ConfirmDialog.Alert("No pieces to gate on",
                "Add at least one equippable item to this combo first — refine and grade conditions are checked across the combo's equipment slots.");
            return;
        }
        var dlg = new Views.ComboConditionalBonusBuilderDialog(Script, eqi, _session.Mode != ServerMode.PreRenewal)
        { Owner = System.Windows.Application.Current.MainWindow };
        if (dlg.ShowDialog() == true) Script = dlg.ResultScript;
    }

    /// <summary>The distinct EQI equip slots of the selected combo's first item-set, mapped from each piece's
    /// item Location — the slots the refine/grade checks run over.</summary>
    private IReadOnlyList<string> DeriveEqiSlots(ComboSetRowViewModel row)
    {
        var alt = row.AlternativeRecords().FirstOrDefault();
        if (alt?.Get("Combo") is not IList<object> members || members.Count == 0) return Array.Empty<string>();

        var byAegis = new Dictionary<string, DbRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in _drops.Items.Effective())
        {
            var a = r.GetString("AegisName");
            if (!string.IsNullOrEmpty(a)) byAegis[a] = r;
        }

        var slots = new List<string>();
        foreach (var mem in members)
        {
            if (byAegis.TryGetValue(mem?.ToString() ?? string.Empty, out var rec))
            {
                var loc = rec.GetSet("Locations")?.FirstOrDefault();
                var eqi = loc is null ? null : ComboConditionalScript.LocationToEqi(loc);
                if (eqi is not null && !slots.Contains(eqi)) slots.Add(eqi);
            }
        }
        return slots;
    }

    public async Task EnsureLoadedAsync()
    {
        if (_overlay is not null) return;
        IsLoading = true;
        try
        {
            // Resolve the active overlay on the background thread (lazy mode-load may parse a base on .For).
            _overlay = await Task.Run(() => _session.GetModeSet(_schema).For(_session.Mode));
        }
        catch (Exception ex)
        {
            IsLoading = false;
            Serilog.Log.Error(ex, "Failed to load item combos");
            Views.ConfirmDialog.Alert("Couldn't load Item Combos",
                $"Item Combos could not be loaded — a data file may be malformed:\n\n{ex.Message}");
            return;
        }

        Rows.Clear();
        foreach (var rec in _overlay.Effective())
            Rows.Add(new ComboSetRowViewModel(rec, rec.Origin, _drops));

        IsLoading = false;
        SelectedRow = Rows.FirstOrDefault();
    }

    /// <summary>Resolves an item's aegis name to a display member (name + tooltip).</summary>
    internal ComboMemberViewModel MakeMember(string aegis)
    {
        var (_, name) = _drops.ResolveItem(aegis);
        return new ComboMemberViewModel(aegis, name);
    }

    // ----- Combo-set level -----

    [RelayCommand]
    private void AddComboSet()
    {
        if (_overlay is null) return;
        SearchText = string.Empty;

        var record = new DbRecord(_schema) { Origin = RecordOrigin.NewCustom };
        var firstAlt = new DbRecord(_comboElement) { Owner = record };
        firstAlt.SetRaw("Combo", new List<object>());
        record.SetRaw("Combos", new List<DbRecord> { firstAlt });

        var row = new ComboSetRowViewModel(record, RecordOrigin.NewCustom, _drops);
        Stack.Execute(new ListMutateCommand("Add combo",
            () => { _overlay.AddImportRaw(record); record.IsDirty = true; Rows.Add(row); SelectedRow = row; },
            () => { _overlay.RemoveImportRaw(record); Rows.Remove(row); }));
    }

    [RelayCommand]
    private void DeleteComboSet(ComboSetRowViewModel? row)
    {
        row ??= SelectedRow;
        if (_overlay is null || row is null || !row.IsCustom) return;
        if (!Views.ConfirmDialog.Show("Delete combo",
                "Delete this combo set? Its script and item sets will be removed.", yes: "Delete")) return;

        var record = row.Record;
        bool wasOverride = row.Origin == RecordOrigin.Overridden;
        int index = Rows.IndexOf(row);
        Stack.Execute(new ListMutateCommand("Delete combo",
            () =>
            {
                if (wasOverride) _overlay.RevertToCore(record.Key); else _overlay.RemoveImportRaw(record);
                Rows.Remove(row);
                if (ReferenceEquals(SelectedRow, row)) SelectedRow = Rows.Count == 0 ? null : Rows[Math.Clamp(index, 0, Rows.Count - 1)];
            },
            () =>
            {
                _overlay.AddImportRaw(record); record.IsDirty = true;
                Rows.Insert(Math.Clamp(index, 0, Rows.Count), row);
                SelectedRow = row;
            }));
    }

    /// <summary>Clones the selected base combo into the import layer so it becomes editable (copy-on-write).
    /// Undoable — undo reverts the row back to the read-only base combo.</summary>
    [RelayCommand]
    private void CreateOverride()
    {
        if (_overlay is null || SelectedRow is not { IsCustom: false } row) return;
        var baseRec = row.Record;
        var clone = baseRec.DeepClone(); // same member-set key as base → overrides it on save
        clone.Origin = RecordOrigin.Overridden;
        Stack.Execute(new ListMutateCommand("Override combo",
            () =>
            {
                _overlay.AddCustom(clone);
                clone.IsDirty = true;
                row.Replace(clone, RecordOrigin.Overridden);
                if (ReferenceEquals(SelectedRow, row)) RebuildDetail();
            },
            () =>
            {
                _overlay.RevertToCore(clone.Key);
                row.Replace(baseRec, RecordOrigin.Base);
                if (ReferenceEquals(SelectedRow, row)) RebuildDetail();
            }));
    }

    /// <summary>Copies the selected combo set as a complete item_combo import YAML document.</summary>
    [RelayCommand]
    private void CopyComboYaml(ComboSetRowViewModel? row)
    {
        row ??= SelectedRow;
        if (row is null) return;
        var yaml = new Core.Serialization.YamlDbWriter().WriteToString(_schema, new[] { row.Record });
        try { System.Windows.Clipboard.SetText(yaml); } catch { /* clipboard busy */ }
    }

    // ----- Alternative (item-set) level -----

    [RelayCommand]
    private void AddAlternative()
    {
        if (SelectedRow is not { IsCustom: true } row) return;
        var combos = row.ComboList();
        var alt = new DbRecord(_comboElement) { Owner = row.Record };
        alt.SetRaw("Combo", new List<object>());
        var altVm = new ComboAlternativeViewModel(this, alt);
        Stack.Execute(new ListMutateCommand("Add combo set",
            () => { combos.Add(alt); Alternatives.Add(altVm); Touch(row); },
            () => { combos.Remove(alt); Alternatives.Remove(altVm); Touch(row); }));
    }

    [RelayCommand]
    private void RemoveAlternative(ComboAlternativeViewModel? alt)
    {
        if (alt is null || SelectedRow is not { IsCustom: true } row) return;
        var combos = row.ComboList();
        int index = combos.IndexOf(alt.Record);
        int vmIndex = Alternatives.IndexOf(alt);
        Stack.Execute(new ListMutateCommand("Remove combo set",
            () => { combos.Remove(alt.Record); Alternatives.Remove(alt); Touch(row); },
            () =>
            {
                combos.Insert(Math.Clamp(index, 0, combos.Count), alt.Record);
                Alternatives.Insert(Math.Clamp(vmIndex, 0, Alternatives.Count), alt);
                Touch(row);
            }));
    }

    // ----- Member (item) level -----

    [RelayCommand]
    private void AddItem(ComboAlternativeViewModel? alt)
    {
        if (alt is null || SelectedRow is not { IsCustom: true } row) return;
        var dlg = new Views.RecordPickerDialog("Add item to combo", _drops.SearchItems)
        { Owner = System.Windows.Application.Current.MainWindow };
        if (dlg.ShowDialog() != true || dlg.Selected is not { } picked || string.IsNullOrEmpty(picked.Aegis)) return;

        var member = MakeMember(picked.Aegis);
        Stack.Execute(new ListMutateCommand("Add combo item",
            () => { alt.Members.Add(picked.Aegis); alt.Items.Add(member); Touch(row); },
            () => { alt.Members.Remove(picked.Aegis); alt.Items.Remove(member); Touch(row); }));
    }

    [RelayCommand]
    private void RemoveItem(ComboMemberViewModel? member)
    {
        if (member is null || SelectedRow is not { IsCustom: true } row) return;
        var alt = Alternatives.FirstOrDefault(a => a.Items.Contains(member));
        if (alt is null) return;
        int index = alt.Items.IndexOf(member);
        Stack.Execute(new ListMutateCommand("Remove combo item",
            () => { alt.Items.RemoveAt(index); alt.Members.RemoveAt(index); Touch(row); },
            () =>
            {
                alt.Items.Insert(Math.Clamp(index, 0, alt.Items.Count), member);
                alt.Members.Insert(Math.Clamp(index, 0, alt.Members.Count), member.Aegis);
                Touch(row);
            }));
    }

    /// <summary>Marks the set dirty (so Save All writes it) and refreshes the master-row summary.</summary>
    private static void Touch(ComboSetRowViewModel row)
    {
        row.Record.IsDirty = true;
        row.Refresh();
    }
}

/// <summary>One item inside a combo alternative (its aegis name + resolved display name).</summary>
public sealed class ComboMemberViewModel
{
    public ComboMemberViewModel(string aegis, string displayName)
    {
        Aegis = aegis;
        DisplayName = string.IsNullOrEmpty(displayName) ? aegis : displayName;
    }

    public string Aegis { get; }
    public string DisplayName { get; }
}

/// <summary>One alternative item-set within a combo (a <c>Combo</c> entry: a list of item members).</summary>
public sealed class ComboAlternativeViewModel
{
    public ComboAlternativeViewModel(ComboEditorViewModel owner, DbRecord comboRecord)
    {
        Record = comboRecord;
        if (comboRecord.Get("Combo") is not IList<object> members)
        {
            members = new List<object>();
            comboRecord.SetRaw("Combo", members);
        }
        Members = members;
        foreach (var m in members) Items.Add(owner.MakeMember(m?.ToString() ?? string.Empty));
    }

    public DbRecord Record { get; }

    /// <summary>The raw aegis-name list backing this alternative (kept in sync with <see cref="Items"/>).</summary>
    public IList<object> Members { get; }

    public ObservableCollection<ComboMemberViewModel> Items { get; } = new();
}

/// <summary>A row in the combo master list — one combo set, summarised by its items + script.</summary>
public sealed partial class ComboSetRowViewModel : ObservableObject
{
    private readonly DropService _drops;

    public ComboSetRowViewModel(DbRecord record, RecordOrigin origin, DropService drops)
    {
        Record = record;
        Origin = origin;
        _drops = drops;
    }

    public DbRecord Record { get; private set; }
    public RecordOrigin Origin { get; private set; }

    public bool IsCustom => Origin != RecordOrigin.Base;

    public IEnumerable<DbRecord> AlternativeRecords() => Record.GetList("Combos") ?? (IList<DbRecord>)Array.Empty<DbRecord>();

    public IList<DbRecord> ComboList()
    {
        if (Record.Get("Combos") is not IList<DbRecord> list)
        {
            list = new List<DbRecord>();
            Record.SetRaw("Combos", list);
        }
        return list;
    }

    /// <summary>Headline: the first item-set's members by name, plus a "+N more sets" hint.</summary>
    public string PrimaryText
    {
        get
        {
            var combos = Record.GetList("Combos");
            if (combos is null || combos.Count == 0) return "(empty combo)";
            string first = string.Join("  +  ", Members(combos[0]).Select(NameOf));
            if (string.IsNullOrWhiteSpace(first)) first = "(no items)";
            return combos.Count > 1 ? $"{first}    (+{combos.Count - 1} more)" : first;
        }
    }

    public string SecondaryText
    {
        get
        {
            var line = ScriptText.Replace("\r", string.Empty).Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? string.Empty;
            return line.Length > 70 ? line[..70] + "…" : line;
        }
    }

    public string ScriptText => Record.GetScript("Script")?.Text ?? string.Empty;

    public void Refresh()
    {
        OnPropertyChanged(nameof(PrimaryText));
        OnPropertyChanged(nameof(SecondaryText));
        OnPropertyChanged(nameof(ScriptText));
        OnPropertyChanged(nameof(Origin));
        OnPropertyChanged(nameof(IsCustom));
    }

    public void Replace(DbRecord record, RecordOrigin origin)
    {
        Record = record;
        Origin = origin;
        Refresh();
    }

    private static IEnumerable<string> Members(DbRecord combo) =>
        combo.Get("Combo") is IList<object> list ? list.Select(x => x?.ToString() ?? string.Empty) : Enumerable.Empty<string>();

    private string NameOf(string aegis)
    {
        if (string.IsNullOrEmpty(aegis)) return "?";
        var (_, name) = _drops.ResolveItem(aegis);
        return string.IsNullOrEmpty(name) ? aegis : name;
    }
}
