using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MidgardStudio.Core.Commands;
using MidgardStudio.Core.Model;
using MidgardStudio.Core.Schema;

namespace MidgardStudio.App.ViewModels;

// ---- Bool-map (Jobs / Classes / Locations / Modes) rendered as toggle chips ----

/// <summary>The tri-state of a bool-map chip: not set, enabled (true), or excluded (false / "all-except").</summary>
public enum ChipState { None, Include, Exclude }

public sealed partial class BoolChipViewModel : ObservableObject
{
    private readonly bool _canExclude; // only specific tokens of an "All"-bearing field can be excluded

    public BoolChipViewModel(string key, string label, ChipState state, bool canExclude)
    {
        Key = key;
        Label = label;
        _state = state;
        _canExclude = canExclude;
    }

    /// <summary>Raw value stored/serialized (e.g. "Head_Low").</summary>
    public string Key { get; }

    /// <summary>Friendly text shown on the chip (e.g. "Lower Headgear").</summary>
    public string Label { get; }

    [ObservableProperty]
    private ChipState _state;

    public event Action? Toggled;

    partial void OnStateChanged(ChipState value) => Toggled?.Invoke();

    /// <summary>Cycles on click: None → Include → (Exclude →) None. Exclude is only reachable for the specific
    /// tokens of a field that has a universal "All" (Jobs/Classes) — the "All" chip and plain multi-select
    /// fields (Locations, Modes) just toggle None ↔ Include.</summary>
    [RelayCommand]
    private void Cycle()
    {
        State = State switch
        {
            ChipState.None => ChipState.Include,
            ChipState.Include => _canExclude ? ChipState.Exclude : ChipState.None,
            _ => ChipState.None,
        };
    }
}

public sealed class BoolMapFieldEditorViewModel : FieldEditorViewModel
{
    // Keys not represented by a chip (a future rAthena token) — preserved on commit so we never drop them.
    private readonly HashSet<string> _extraIncluded;
    private readonly HashSet<string> _extraExcluded;

    public BoolMapFieldEditorViewModel(DbRecord r, FieldSchema f, FieldEditorContext c) : base(r, f, c)
    {
        var included = r.GetSet(f.Name);          // included tokens (works for a BoolMap or a plain set)
        var excluded = r.GetBoolMap(f.Name)?.Excluded;
        var options = f.Enum?.Values ?? Array.Empty<string>();
        var optionSet = new HashSet<string>(options, StringComparer.Ordinal);

        // "All-except" only makes sense where a universal "All" token exists (Jobs/Classes). Other bool-maps
        // (Locations, mob Modes) stay plain include-only multi-select.
        SupportsExclude = optionSet.Contains("All");

        foreach (var option in options)
        {
            var state = (included?.Contains(option) ?? false) ? ChipState.Include
                      : (excluded?.Contains(option) ?? false) ? ChipState.Exclude
                      : ChipState.None;
            var chip = new BoolChipViewModel(option, f.Enum?.Label(option) ?? option, state,
                canExclude: SupportsExclude && option != "All");
            chip.Toggled += OnChipToggled;
            Chips.Add(chip);
        }

        _extraIncluded = included is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(included.Where(k => !optionSet.Contains(k)), StringComparer.Ordinal);
        _extraExcluded = excluded is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(excluded.Where(k => !optionSet.Contains(k)), StringComparer.Ordinal);
    }

    public ObservableCollection<BoolChipViewModel> Chips { get; } = new();

    /// <summary>True for Jobs/Classes — drives the "click to exclude" hint and the tri-state cycle.</summary>
    public bool SupportsExclude { get; }

    public override string Summary
    {
        get
        {
            var inc = Chips.Where(c => c.State == ChipState.Include).Select(c => c.Label).Concat(_extraIncluded).ToList();
            var exc = Chips.Where(c => c.State == ChipState.Exclude).Select(c => c.Label).Concat(_extraExcluded).ToList();
            if (inc.Count == 0 && exc.Count == 0) return "None";
            string s = string.Join(", ", inc);
            if (exc.Count > 0) s = (s.Length == 0 ? "—" : s) + " · except " + string.Join(", ", exc);
            return s.Length > 50 ? s[..50] + "…" : s;
        }
    }

    private void OnChipToggled()
    {
        var map = new BoolMap(_extraIncluded);
        foreach (var chip in Chips)
            if (chip.State == ChipState.Include) map.Add(chip.Key);
        foreach (var x in _extraExcluded) map.Excluded.Add(x);
        foreach (var chip in Chips)
            if (chip.State == ChipState.Exclude) map.Excluded.Add(chip.Key);
        Commit(map);
        OnPropertyChanged(nameof(Summary));
    }
}

// ---- Nested fixed-shape object (Flags / Trade / Delay / Stack / NoUse) ----

public sealed class ObjectFieldEditorViewModel : FieldEditorViewModel
{
    private readonly DbRecord _nested;

    public ObjectFieldEditorViewModel(DbRecord parent, FieldSchema field, FieldEditorContext ctx) : base(parent, field, ctx)
    {
        var nested = parent.GetObject(field.Name);
        if (nested is null)
        {
            nested = new DbRecord(field.ObjectSchema!) { Owner = parent };
            if (ctx.IsEditable)
                parent.SetRaw(field.Name, nested); // attach; empty objects are omitted on save
        }
        _nested = nested;

        foreach (var child in field.ObjectSchema!.Fields)
        {
            var vm = FieldEditorFactory.Create(nested, child, ctx);
            vm.Changed += OnChildChanged;
            Children.Add(vm);
        }
    }

    public ObservableCollection<FieldEditorViewModel> Children { get; } = new();

    public override string Summary
    {
        get
        {
            var parts = new List<string>();
            foreach (var f in Field.ObjectSchema!.Fields)
            {
                var v = _nested.Get(f.Name);
                if (!IsDefault(v, f.Default))
                    parts.Add(v is bool ? f.Label : $"{f.Label}: {v}");
            }
            if (parts.Count == 0) return "None";
            string s = string.Join(", ", parts);
            return s.Length > 50 ? s[..50] + "…" : s;
        }
    }

    private void OnChildChanged()
    {
        RaiseChanged();
        OnPropertyChanged(nameof(Summary));
    }

    private static bool IsDefault(object? v, object? def) => v switch
    {
        null => true,
        bool b => b == (def is bool db && db),
        int i => i == (def is int di ? di : 0),
        long l => l == (def is long dl ? dl : def is int di2 ? di2 : 0L),
        string s => string.IsNullOrEmpty(s),
        MidgardStudio.Core.Model.LevelList ll => ll.IsEmpty,
        _ => false,
    };
}

// ---- Object list (mob Drops, pet Evolution, item_group List, ...) as an editable sub-grid ----

public sealed class ObjectRowViewModel : ObservableObject
{
    private readonly DbSchema _element;

    public ObjectRowViewModel(DbRecord record, DbSchema element, FieldEditorContext ctx)
    {
        Record = record;
        _element = element;
        foreach (var field in element.Fields)
            Cells.Add(FieldEditorFactory.Create(record, field, ctx));
    }

    public DbRecord Record { get; }

    public ObservableCollection<FieldEditorViewModel> Cells { get; } = new();

    /// <summary>Headline for the master list: the entry's name/reference, else its first meaningful value.</summary>
    public string PrimaryText
    {
        get
        {
            var nameField = _element.Fields.FirstOrDefault(f => f.IsDisplay)
                ?? _element.Fields.FirstOrDefault(f => f.Kind == FieldKind.Reference)
                ?? _element.Fields.FirstOrDefault(f => f.Kind == FieldKind.String);
            if (nameField is not null)
            {
                var s = Record.GetString(nameField.Name);
                if (!string.IsNullOrWhiteSpace(s)) return s!;
            }
            return ScalarParts().FirstOrDefault() ?? "(new entry)";
        }
    }

    /// <summary>Muted secondary line: the entry's key scalar values (Rate, Amount, …).</summary>
    public string SecondaryText => string.Join("   ·   ", ScalarParts().Take(4));

    private IEnumerable<string> ScalarParts()
    {
        foreach (var f in _element.Fields)
        {
            if (f.Kind is FieldKind.Reference or FieldKind.String or FieldKind.Script
                or FieldKind.Object or FieldKind.ObjectList or FieldKind.Flags or FieldKind.BoolMap)
                continue;
            var v = Record.Get(f.Name);
            if (IsDefault(v, f.Default)) continue;
            yield return v is bool ? f.Label : $"{f.Label} {v}";
        }
    }

    /// <summary>Re-reads the summary text after a field edit so the master row stays in sync.</summary>
    public void RefreshSummary()
    {
        OnPropertyChanged(nameof(PrimaryText));
        OnPropertyChanged(nameof(SecondaryText));
    }

    private static bool IsDefault(object? v, object? def) => v switch
    {
        null => true,
        bool b => b == (def is bool db && db),
        int i => i == (def is int di ? di : 0),
        long l => l == (def is long dl ? dl : def is int di2 ? di2 : 0L),
        string s => string.IsNullOrEmpty(s),
        MidgardStudio.Core.Model.LevelList ll => ll.IsEmpty,
        _ => false,
    };
}

public sealed partial class ObjectListFieldEditorViewModel : FieldEditorViewModel
{
    private readonly DbRecord _parent;
    private readonly DbSchema _element;
    private readonly FieldEditorContext _ctx;
    private readonly IList<DbRecord> _list;

    public ObjectListFieldEditorViewModel(DbRecord parent, FieldSchema field, FieldEditorContext ctx) : base(parent, field, ctx)
    {
        _parent = parent;
        _element = field.ObjectSchema!;
        _ctx = ctx;

        var existing = parent.GetList(field.Name);
        if (existing is null)
        {
            existing = new List<DbRecord>();
            if (ctx.IsEditable) parent.SetRaw(field.Name, existing);
        }
        _list = existing;

        foreach (var record in _list)
            Rows.Add(BuildRow(record));

        RowsView = CollectionViewSource.GetDefaultView(Rows);
        RowsView.Filter = MatchesSearch;
        SelectedRow = Rows.FirstOrDefault();
    }

    public ObservableCollection<ObjectRowViewModel> Rows { get; } = new();

    /// <summary>Filtered/searchable view of <see cref="Rows"/> bound to the master list.</summary>
    public ICollectionView RowsView { get; }

    /// <summary>The entry shown in the detail pane of the master-detail editor.</summary>
    [ObservableProperty]
    private ObjectRowViewModel? _selectedRow;

    /// <summary>Filter text for the master list; matches the entry's name/summary.</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    partial void OnSearchTextChanged(string value) => RowsView.Refresh();

    private bool MatchesSearch(object o)
    {
        if (string.IsNullOrWhiteSpace(SearchText) || o is not ObjectRowViewModel row) return true;
        string q = SearchText.Trim();
        return row.PrimaryText.Contains(q, StringComparison.OrdinalIgnoreCase)
            || row.SecondaryText.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    public override string Summary =>
        _list.Count == 0 ? "None" : $"{_list.Count} " + (_list.Count == 1 ? "entry" : "entries");

    private ObjectRowViewModel BuildRow(DbRecord record)
    {
        var row = new ObjectRowViewModel(record, _element, _ctx);
        foreach (var cell in row.Cells)
        {
            cell.Changed += RaiseChanged;
            cell.Changed += row.RefreshSummary; // keep the master-row label live as fields are edited
        }
        return row;
    }

    [RelayCommand]
    private void AddRow()
    {
        if (!IsEditable) return;
        SearchText = string.Empty; // ensure the new (empty) entry isn't hidden by an active filter
        var record = new DbRecord(_element) { Owner = _parent };
        var row = BuildRow(record);
        Stack.Execute(new ListMutateCommand(
            $"{_parent.Schema.DisplayName}: add {Label} row",
            () => { _list.Add(record); Rows.Add(row); SelectedRow = row; _parent.IsDirty = true; RaiseChanged(); OnPropertyChanged(nameof(Summary)); },
            () => { _list.Remove(record); Rows.Remove(row); _parent.IsDirty = true; RaiseChanged(); OnPropertyChanged(nameof(Summary)); }));
    }

    /// <summary>Duplicates an entry (deep clone) and inserts the copy right after it. Undoable.</summary>
    [RelayCommand]
    private void DuplicateRow(ObjectRowViewModel? row)
    {
        row ??= SelectedRow;
        if (!IsEditable || row is null) return;

        var clone = row.Record.DeepClone();
        clone.Owner = _parent;
        var newRow = BuildRow(clone);
        int index = _list.IndexOf(row.Record);
        Stack.Execute(new ListMutateCommand(
            $"{_parent.Schema.DisplayName}: duplicate {Label} row",
            () =>
            {
                int i = Math.Clamp(index + 1, 0, _list.Count);
                _list.Insert(i, clone);
                Rows.Insert(Math.Clamp(index + 1, 0, Rows.Count), newRow);
                SelectedRow = newRow; _parent.IsDirty = true; RaiseChanged(); OnPropertyChanged(nameof(Summary));
            },
            () =>
            {
                _list.Remove(clone); Rows.Remove(newRow);
                _parent.IsDirty = true; RaiseChanged(); OnPropertyChanged(nameof(Summary));
            }));
    }

    /// <summary>Copies an entry as a bare YAML mapping to the clipboard (read-only safe).</summary>
    [RelayCommand]
    private void CopyRowYaml(ObjectRowViewModel? row)
    {
        row ??= SelectedRow;
        if (row is null) return;
        var yaml = new Core.Serialization.YamlDbWriter().WriteRecord(row.Record);
        try { System.Windows.Clipboard.SetText(yaml); } catch { /* clipboard busy */ }
    }

    [RelayCommand]
    private void RemoveRow(ObjectRowViewModel? row)
    {
        row ??= SelectedRow;
        if (!IsEditable || row is null) return;
        int index = _list.IndexOf(row.Record);
        Stack.Execute(new ListMutateCommand(
            $"{_parent.Schema.DisplayName}: remove {Label} row",
            () =>
            {
                _list.Remove(row.Record); Rows.Remove(row);
                SelectedRow = Rows.Count == 0 ? null : Rows[Math.Clamp(index, 0, Rows.Count - 1)];
                _parent.IsDirty = true; RaiseChanged(); OnPropertyChanged(nameof(Summary));
            },
            () =>
            {
                int i = Math.Clamp(index, 0, _list.Count);
                _list.Insert(i, row.Record);
                Rows.Insert(Math.Clamp(index, 0, Rows.Count), row);
                SelectedRow = row;
                _parent.IsDirty = true;
                RaiseChanged();
                OnPropertyChanged(nameof(Summary));
            }));
    }
}

// ---- Cross-database reference (item AliasName, drop item, pet mob, ...) ----

public sealed class ReferenceFieldEditorViewModel : FieldEditorViewModel
{
    private readonly string _db;
    private string? _query;

    public ReferenceFieldEditorViewModel(DbRecord r, FieldSchema f, FieldEditorContext c) : base(r, f, c)
    {
        _db = f.Enum?.ReferenceDb ?? string.Empty;
        _query = Record.GetString(FieldName); // start the live text at the committed value
        CommitCommand = new RelayCommand(CommitQuery);
    }

    /// <summary>The live editable text — drives <see cref="Suggestions"/> as the user types, so the dropdown
    /// filters in real time, WITHOUT pushing an undo command per keystroke. The field value is committed once
    /// on focus loss (<see cref="CommitCommand"/>) — matching the previous commit timing.</summary>
    public string? Query
    {
        get => _query;
        set
        {
            if (_query != value)
            {
                _query = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Suggestions));
            }
        }
    }

    /// <summary>The committed field value (one undo step). Set on focus loss via <see cref="CommitCommand"/>.</summary>
    public string? Value
    {
        get => Record.GetString(FieldName);
        set { if (value != Value) { Commit(value); OnPropertyChanged(); } }
    }

    /// <summary>Commits the live query text to the record — bound to the combo's lost-focus.</summary>
    public ICommand CommitCommand { get; }

    private void CommitQuery()
    {
        if ((Query ?? string.Empty) != (Value ?? string.Empty)) Value = Query;
    }

    public IReadOnlyList<string> Suggestions =>
        Context.References?.Search(_db, Query ?? string.Empty, 40) ?? Array.Empty<string>();
}
