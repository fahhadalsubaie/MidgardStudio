using System;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MidgardStudio.App.Services;
using MidgardStudio.Core.Model;
using MidgardStudio.Core.Commands;
using MidgardStudio.Core.Lookup;
using MidgardStudio.Core.Overlay;
using MidgardStudio.Core.Schema;

namespace MidgardStudio.App.ViewModels;

/// <summary>Master-detail editor for one database: the filtered list plus the schema-generated form.</summary>
public sealed partial class DbWorkspaceViewModel : ObservableObject, IDisposable
{
    private readonly WorkspaceSession _session;
    private readonly DbSchema _schema;
    private readonly IReferenceResolver _references;
    private readonly ClientItemService? _clientItems;
    private readonly GrfImageService? _images;
    private readonly MobSpriteService? _mobSprite;
    private readonly DropService? _drops;
    private readonly Action<string, RecordKey>? _navigate;
    private ModeSet? _modeSet;
    private OverlayTable? _overlay;

    public DbWorkspaceViewModel(WorkspaceSession session, DbSchema schema, IReferenceResolver references,
        ClientItemService? clientItems = null, GrfImageService? images = null,
        MobSpriteService? mobSprite = null, DropService? drops = null, Action<string, RecordKey>? navigate = null)
    {
        _session = session;
        _schema = schema;
        _references = references;
        _clientItems = clientItems;
        _images = images;
        _mobSprite = mobSprite;
        _drops = drops;
        _navigate = navigate;
    }

    /// <summary>True for the Mobs database, which gets the client sprite-registration side editor.</summary>
    private bool SupportsMobSpriteEditing => _schema.Id == "mob_db" && _mobSprite is not null && _images is not null;

    public string Title => _schema.DisplayName;

    public string SchemaId => _schema.Id;

    /// <summary>Palette search over this workspace's rows (only when loaded).</summary>
    public IEnumerable<(MidgardStudio.Core.Model.RecordKey Key, string Display)> SearchRows(string query, int limit)
    {
        if (List is null) yield break;
        foreach (var row in List.Search(query, limit))
            yield return (row.Key, $"#{row.KeyText}  {row.Name}  ({row.AegisName})");
    }

    public void SelectRow(MidgardStudio.Core.Model.RecordKey key)
    {
        if (List is not null) List.SelectByKey(key);
        else _pendingSelect = key; // applied once the workspace finishes loading (cross-DB navigation)
    }

    [RelayCommand]
    private void ShowYaml()
    {
        if (Editor is not { HasRecord: true } editor) return;
        var dlg = new Views.YamlPreviewDialog(editor.Title, editor.YamlPreview)
        { Owner = System.Windows.Application.Current.MainWindow };
        dlg.ShowDialog();
    }

    /// <summary>True for the item database — enables the Client-Items context-menu actions.</summary>
    public bool IsItemDb => _schema.Id == "item_db";

    [RelayCommand]
    private void DeleteEntry()
    {
        if (_overlay is null || List?.SelectedRow is not { } row) return;
        if (_overlay.OriginOf(row.Key) == RecordOrigin.Base) { PortReportText = "Base entries are read-only; nothing to delete."; return; }
        if (_overlay.GetEffective(row.Key) is not { } import) return;
        _session.Commands.Execute(new RemoveImportCommand(_overlay, import));
        List.SyncWithOverlay();
        ApplySelection(List.SelectedRow);
    }

    /// <summary>True when the selected row is an override of a base entry (so it can be reverted).</summary>
    public bool CanRestore =>
        _overlay is not null && List?.SelectedRow is { } r && _overlay.OriginOf(r.Key) == RecordOrigin.Overridden;

    /// <summary>Discards the import override for the selected entry, reverting it to the base values.</summary>
    [RelayCommand]
    private void RestoreToDefault()
    {
        if (_overlay is null || List?.SelectedRow is not { } row) return;
        if (_overlay.OriginOf(row.Key) != RecordOrigin.Overridden)
        {
            PortReportText = "Only overridden entries can be restored to default.";
            return;
        }
        if (_overlay.GetEffective(row.Key) is not { } import) return;

        if (!Views.ConfirmDialog.Show("Restore to default",
                $"Restore #{row.Key} to its default (base) values?\nYour customizations for this entry will be discarded.",
                yes: "Restore")) return;

        _session.Commands.Execute(new RemoveImportCommand(_overlay, import));
        List.SyncWithOverlay();
        ApplySelection(List.SelectedRow);
        PortReportText = $"Restored #{row.Key} to its default values.";
    }

    [RelayCommand]
    private void ChangeId()
    {
        if (_overlay is null || List?.SelectedRow is not { } row) return;
        var keyField = _schema.KeyField;
        if (keyField is null || keyField.Kind != FieldKind.Int) return;
        if (_overlay.OriginOf(row.Key) != RecordOrigin.NewCustom) { PortReportText = "Change ID only applies to custom entries."; return; }

        int current = (int)row.Key.AsInt;
        if (PromptId("Change ID", "New ID", current) is not { } newId || newId == current) return;
        if (_overlay.GetEffective(RecordKey.Of(newId)) is not null) { PortReportText = $"ID {newId} already exists."; return; }

        var clone = _overlay.GetEffective(row.Key)!.DeepClone();
        clone.SetRaw(keyField.Name, newId);
        using (_session.Commands.BeginBatch("Change ID"))
        {
            _session.Commands.Execute(new RemoveImportCommand(_overlay, _overlay.GetEffective(row.Key)!));
            _session.Commands.Execute(new AddRecordCommand(_overlay, clone));
        }
        List.SyncWithOverlay();
        List.SelectByKey(clone.Key);
    }

    [RelayCommand]
    private void CopyToId()
    {
        if (_overlay is null || List?.SelectedRow is not { } row) return;
        var keyField = _schema.KeyField;
        if (keyField is null || keyField.Kind != FieldKind.Int) return;

        if (PromptId("Copy to ID", "Target ID", NextFreeId(keyField.Name)) is not { } newId) return;
        if (_overlay.GetEffective(RecordKey.Of(newId)) is not null) { PortReportText = $"ID {newId} already exists."; return; }

        var clone = _overlay.GetEffective(row.Key)!.DeepClone();
        clone.SetRaw(keyField.Name, newId);
        if (_schema.Field("AegisName") is not null) clone.SetRaw("AegisName", $"Custom_{newId}");
        _session.Commands.Execute(new AddRecordCommand(_overlay, clone));
        List.AddRow(List.CreateRow(clone.Key));
        List.SelectByKey(clone.Key);
    }

    [RelayCommand]
    private void SelectInClientItems()
    {
        if (List?.SelectedRow is { } row) _navigate?.Invoke("client_items", row.Key);
    }

    [RelayCommand]
    private void AddInClientItems()
    {
        if (_clientItems is null || List?.SelectedRow is not { } row) return;
        int id = (int)row.Key.AsInt;
        var entry = _clientItems.GetOrCreate(id);
        if (string.IsNullOrEmpty(entry.IdentifiedDisplayName))
            entry.IdentifiedDisplayName = row.Record.GetString("Name") ?? string.Empty;
        entry.SlotCount = row.Record.GetInt("Slots");
        entry.ClassNum = row.Record.GetInt("View");
        _clientItems.Upsert(entry);
        _navigate?.Invoke("client_items", row.Key);
    }

    private static int? PromptId(string title, string prompt, int initial)
    {
        var dlg = new Views.IdInputDialog(title, prompt, initial) { Owner = System.Windows.Application.Current.MainWindow };
        return dlg.ShowDialog() == true ? dlg.Value : null;
    }

    [RelayCommand]
    private void CopyEntry()
    {
        if (List?.SelectedRow is not { } row) return;
        var file = new Core.Serialization.DbFile { HeaderType = _schema.HeaderType, HeaderVersion = _schema.HeaderVersion };
        file.Records.Add(row.Record);
        var yaml = new Core.Serialization.YamlDbWriter().WriteToString(_schema, file);
        try { System.Windows.Clipboard.SetText(yaml); } catch { /* clipboard busy */ }
    }

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private DbListViewModel? _list;

    [ObservableProperty]
    private RecordEditorViewModel? _editor;

    [ObservableProperty]
    private string? _portReportText;

    /// <summary>Item-only: the client (itemInfo) text + sprite editor for the selected row.</summary>
    [ObservableProperty]
    private object? _sideEditor;

    /// <summary>Mob drop tables (Normal/MVP) or item "Dropped by" reverse card for the selected row.</summary>
    [ObservableProperty]
    private object? _auxEditor;

    private string? _pendingFilter;
    private MidgardStudio.Core.Model.RecordKey? _pendingSelect;

    /// <summary>Pre-filters the list (used by the Bloody/Dead Branch specialized views).</summary>
    public void FilterTo(string query)
    {
        if (List is not null) List.SearchText = query;
        else _pendingFilter = query;
    }

    [RelayCommand]
    private void Duplicate()
    {
        if (_overlay is null || List?.SelectedRow is not { } row) return;
        var keyField = _schema.KeyField;
        if (keyField is null) return;

        var clone = row.Record.DeepClone();
        if (keyField.Kind == FieldKind.Int) clone.SetRaw(keyField.Name, NextFreeId(keyField.Name));
        else clone.SetRaw(keyField.Name, UniqueStringKey(keyField.Name));
        if (_schema.Field("AegisName") is not null) clone.SetRaw("AegisName", $"Custom_{clone.Key}");

        _session.Commands.Execute(new AddRecordCommand(_overlay, clone));
        var newRow = List!.CreateRow(clone.Key);
        List.AddRow(newRow);
        List.SelectedRow = newRow;
    }

    public async Task EnsureLoadedAsync()
    {
        if (_overlay is not null) return;

        IsLoading = true;
        _modeSet = await Task.Run(() => _session.GetModeSet(_schema));
        _overlay = _modeSet.For(_session.Mode);

        var editor = new RecordEditorViewModel(_overlay, _session.Commands, _references, _session.ScriptCatalog, _session.Mode);

        // Items get list icons resolved from their client resource name (lazy, per visible row).
        Func<RecordKey, ImageSource?>? iconResolver = null;
        if (_schema.Id == "item_db" && _clientItems is not null && _images is not null)
            iconResolver = key => _images.ItemIcon(_clientItems.GetOrCreate((int)key.AsInt).IdentifiedResourceName);

        var list = new DbListViewModel(_overlay, iconResolver);
        list.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DbListViewModel.SelectedRow))
                ApplySelection(list.SelectedRow);
        };
        editor.RecordChanged += () => list.SelectedRow?.Refresh();
        // Creating an override makes the record editable — rebuild the side/drop cards so they unlock.
        editor.OverrideCreated += () => ApplySelection(list.SelectedRow);

        Editor = editor;
        List = list;

        // Undo/redo mutates the overlay directly; re-sync the list and reload the editor so the UI
        // reflects added/removed entries and reverted field values.
        _session.Commands.UndoRedoPerformed += OnUndoRedo;

        IsLoading = false;

        if (_pendingFilter is not null)
        {
            list.SearchText = _pendingFilter;
            _pendingFilter = null;
        }

        // Select the first entry so the detail form is populated immediately (or a pending navigation target).
        list.SelectedRow = list.Rows.FirstOrDefault();
        if (_pendingSelect is { } sel)
        {
            list.SelectByKey(sel);
            _pendingSelect = null;
        }

        Serilog.Log.Information("Loaded {Db}: {Base} base + {Import} import = {Total} rows",
            _schema.DisplayName, _overlay.BaseCount, _overlay.ImportCount, list.TotalCount);
    }

    /// <summary>Loads the editor, the item/mob side editor, and the drop / dropped-by card for the selected row.</summary>
    private void ApplySelection(RecordRowViewModel? row)
    {
        if (Editor is null) return;
        OnPropertyChanged(nameof(CanRestore));
        if (row is null)
        {
            Editor.Clear();
            SideEditor = null;
            AuxEditor = null;
            return;
        }

        Editor.Load(row.Key);
        SideEditor = SupportsMobSpriteEditing
            ? new MobSpriteViewModel(row.Record, _mobSprite!, _images!)
            : null; // client itemInfo editing now lives in the dedicated Client Items section

        AuxEditor = BuildAuxEditor(row);
    }

    /// <summary>The mob drop tables (Drops/MvpDrops) or the item "Dropped by" reverse card.</summary>
    private object? BuildAuxEditor(RecordRowViewModel row)
    {
        if (_drops is null || _overlay is null) return null;
        bool editable = _overlay.OriginOf(row.Key) != Core.Model.RecordOrigin.Base;

        if (_schema.Id == "mob_db")
            return new MobDropsViewModel(row.Record, editable, _session.Commands, _drops, _navigate);
        if (_schema.Id == "item_db")
            return new DroppedByViewModel(row.Record, _session.Commands, _drops, _navigate);
        return null;
    }

    private void OnUndoRedo()
    {
        if (List is null) return;
        List.SyncWithOverlay();
        ApplySelection(List.SelectedRow);
    }

    public void Dispose() => _session.Commands.UndoRedoPerformed -= OnUndoRedo;

    [RelayCommand]
    private void AddCustom()
    {
        if (_overlay is null || List is null) return;

        var keyField = _schema.KeyField;
        if (keyField is null) return; // keyless DB (item_combos): needs a dedicated editor

        var record = new DbRecord(_schema);
        if (keyField.Kind == FieldKind.Int)
            record.SetRaw(keyField.Name, NextFreeId(keyField.Name));
        else
            record.SetRaw(keyField.Name, UniqueStringKey(keyField.Name));

        if (_schema.Field("AegisName") is not null)
            record.SetRaw("AegisName", $"Custom_{record.Key}");

        var display = _schema.DisplayField;
        if (display is not null && display.Kind == FieldKind.String && !record.Has(display.Name))
            record.SetRaw(display.Name, $"Custom {_schema.DisplayName}");

        _session.Commands.Execute(new AddRecordCommand(_overlay, record));

        // Items: seed a matching client-text entry so the new item is cross-file from the start.
        if (_schema.Id == "item_db" && _clientItems is not null)
        {
            int id = record.GetInt(keyField.Name);
            var entry = _clientItems.GetOrCreate(id);
            entry.IdentifiedDisplayName = record.GetString("Name") ?? string.Empty;
            entry.SlotCount = record.GetInt("Slots");
            entry.ClassNum = record.GetInt("View");
            _clientItems.Upsert(entry);
        }

        var row = List.CreateRow(record.Key);
        List.AddRow(row);
        List.SelectedRow = row;
    }

    private string UniqueStringKey(string keyField)
    {
        int n = 1;
        string candidate;
        do { candidate = $"CUSTOM_{n++}"; }
        while (_overlay!.Effective().Any(r => string.Equals(r.GetString(keyField), candidate, StringComparison.OrdinalIgnoreCase)));
        return candidate;
    }

    private int NextFreeId(string keyField)
    {
        const int start = 30000;
        var used = new HashSet<int>();
        foreach (var r in _overlay!.Effective())
            used.Add(r.GetInt(keyField));

        int id = start;
        while (used.Contains(id)) id++;
        return id;
    }
}
