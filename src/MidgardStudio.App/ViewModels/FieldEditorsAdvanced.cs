using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MidgardStudio.Core.Commands;
using MidgardStudio.Core.Model;
using MidgardStudio.Core.Schema;

namespace MidgardStudio.App.ViewModels;

// ---- Bool-map (Jobs / Classes / Locations / Modes) rendered as toggle chips ----

public sealed partial class BoolChipViewModel : ObservableObject
{
    public BoolChipViewModel(string key, string label, bool selected)
    {
        Key = key;
        Label = label;
        _isSelected = selected;
    }

    /// <summary>Raw value stored/serialized (e.g. "Head_Low").</summary>
    public string Key { get; }

    /// <summary>Friendly text shown on the chip (e.g. "Lower Headgear").</summary>
    public string Label { get; }

    [ObservableProperty]
    private bool _isSelected;

    public event Action? Toggled;

    partial void OnIsSelectedChanged(bool value) => Toggled?.Invoke();
}

public sealed class BoolMapFieldEditorViewModel : FieldEditorViewModel
{
    private readonly HashSet<string> _extraKeys; // true keys not represented by a chip — preserved on commit

    public BoolMapFieldEditorViewModel(DbRecord r, FieldSchema f, FieldEditorContext c) : base(r, f, c)
    {
        var set = r.GetSet(f.Name);
        var options = f.Enum?.Values ?? Array.Empty<string>();
        var optionSet = new HashSet<string>(options, StringComparer.Ordinal);

        foreach (var option in options)
        {
            var chip = new BoolChipViewModel(option, f.Enum?.Label(option) ?? option, set?.Contains(option) ?? false);
            chip.Toggled += OnChipToggled;
            Chips.Add(chip);
        }

        _extraKeys = set is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(set.Where(k => !optionSet.Contains(k)), StringComparer.Ordinal);
    }

    public ObservableCollection<BoolChipViewModel> Chips { get; } = new();

    public override string Summary
    {
        get
        {
            var all = Chips.Where(c => c.IsSelected).Select(c => c.Label).Concat(_extraKeys).ToList();
            if (all.Count == 0) return "None";
            string s = string.Join(", ", all);
            return s.Length > 50 ? s[..50] + "…" : s;
        }
    }

    private void OnChipToggled()
    {
        var set = new HashSet<string>(_extraKeys, StringComparer.Ordinal);
        foreach (var chip in Chips)
            if (chip.IsSelected) set.Add(chip.Key);
        Commit(set);
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

    [RelayCommand]
    private void RemoveRow(ObjectRowViewModel? row)
    {
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

    public ReferenceFieldEditorViewModel(DbRecord r, FieldSchema f, FieldEditorContext c) : base(r, f, c)
    {
        _db = f.Enum?.ReferenceDb ?? string.Empty;
    }

    public string? Value
    {
        get => Record.GetString(FieldName);
        set
        {
            if (value != Value)
            {
                Commit(value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(Suggestions));
            }
        }
    }

    public IReadOnlyList<string> Suggestions =>
        Context.References?.Search(_db, Value ?? string.Empty, 40) ?? Array.Empty<string>();
}
