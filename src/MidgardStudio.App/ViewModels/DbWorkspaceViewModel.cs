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
    private readonly ClientSkillService? _clientSkills;
    private readonly GrfImageService? _images;
    private readonly MobSpriteService? _mobSprite;
    private readonly DropService? _drops;
    private readonly IReferenceIndex? _referenceIndex;
    private readonly Action<string, RecordKey>? _navigate;
    private ModeSet? _modeSet;
    private OverlayTable? _overlay;

    public DbWorkspaceViewModel(WorkspaceSession session, DbSchema schema, IReferenceResolver references,
        ClientItemService? clientItems = null, GrfImageService? images = null,
        MobSpriteService? mobSprite = null, DropService? drops = null, Action<string, RecordKey>? navigate = null,
        ClientSkillService? clientSkills = null, IReferenceIndex? referenceIndex = null)
    {
        _session = session;
        _schema = schema;
        _references = references;
        _clientItems = clientItems;
        _clientSkills = clientSkills;
        _images = images;
        _mobSprite = mobSprite;
        _drops = drops;
        _navigate = navigate;
        _referenceIndex = referenceIndex;
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

    // ---- List column layout (the master GridView adapts its columns per schema) ----

    /// <summary>Only the item DB resolves list icons.</summary>
    public bool ShowIconColumn => _schema.Id == "item_db";

    /// <summary>Header for the key column (e.g. "Item ID", "Group", "Skill").</summary>
    public string KeyColumnHeader => _schema.KeyField?.Label ?? "ID";

    /// <summary>Show a separate Name column only when the display field differs from the key
    /// (so string-keyed DBs like Item Groups / Summon Groups show a single column, not a duplicated one).</summary>
    public bool ShowNameColumn =>
        _schema.DisplayField is { } d && _schema.KeyField is { } k && !string.Equals(d.Name, k.Name, StringComparison.Ordinal);

    public string NameColumnHeader => _schema.DisplayField?.Label ?? "Name";

    [RelayCommand]
    private void DeleteEntry()
    {
        if (_overlay is null || List?.SelectedRow is not { } row) return;
        if (_overlay.OriginOf(row.Key) == RecordOrigin.Base) { PortReportText = "Base entries are read-only; nothing to delete."; return; }
        if (_overlay.GetEffective(row.Key) is not { } import) return;

        // Always confirm. If this item also has (deletable) client text, offer to remove both sides at once —
        // server and client are otherwise independent, so the default keeps the client text.
        bool alsoClient = false;
        if (IsItemDb && _clientItems is not null && _clientItems.HasEditableClientText((int)row.Key.AsInt))
        {
            var choice = Views.ConfirmDialog.Choose("Delete item",
                $"Delete server item #{row.Key}?\n\nThis item also has client text (itemInfo).",
                primary: "Delete both", alternate: "Server only");
            if (choice == Views.ConfirmDialog.Choice.Cancel) return;
            alsoClient = choice == Views.ConfirmDialog.Choice.Primary;
        }
        else if (!Views.ConfirmDialog.Show("Delete entry", $"Delete {_schema.DisplayName} #{row.Key}?", yes: "Delete"))
            return;

        using (_session.Commands.BeginBatch($"Delete {_schema.DisplayName} {row.Key}"))
        {
            _session.Commands.Execute(new RemoveImportCommand(_overlay, import));
            if (alsoClient && _clientItems!.RemoveClientTextCommand((int)row.Key.AsInt) is { } clientCmd)
                _session.Commands.Execute(clientCmd);
        }
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
        if (keyField is null) return;
        if (_overlay.OriginOf(row.Key) != RecordOrigin.NewCustom) { PortReportText = "Change only applies to custom entries."; return; }

        // Pick the new key: an int prompt for int keys, a reference picker (e.g. mob_avail's Mob) for reference keys.
        object newKey;
        if (keyField.Kind == FieldKind.Int)
        {
            int current = (int)row.Key.AsInt;
            if (PromptId("Change ID", "New ID", current) is not { } newId || newId == current) return;
            if (_overlay.GetEffective(RecordKey.Of(newId)) is not null) { PortReportText = $"ID {newId} already exists."; return; }
            newKey = newId;
        }
        else if (keyField.Kind == FieldKind.Reference && keyField.Enum?.ReferenceDb is { } refDb)
        {
            string current = row.Key.ToString();
            if (PromptReference($"Change {keyField.Label}", keyField.Label, refDb, current) is not { } chosen || chosen == current) return;
            if (_overlay.GetEffective(RecordKey.Of(chosen)) is not null) { PortReportText = $"'{chosen}' already has an entry."; return; }
            newKey = chosen;
        }
        else return;

        var clone = _overlay.GetEffective(row.Key)!.DeepClone();
        clone.SetRaw(keyField.Name, newKey);
        using (_session.Commands.BeginBatch("Change " + keyField.Label))
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

    /// <summary>True for the Skills database, which has a Client Skills counterpart (skillinfoz).</summary>
    public bool IsSkillDb => _schema.Id == "skill_db";

    /// <summary>Ctrl+E dispatcher: "Select in Client …" for whichever DB has a client counterpart.</summary>
    [RelayCommand]
    private void SelectInClient()
    {
        if (IsItemDb) SelectInClientItems();
        else if (IsSkillDb) SelectInClientSkills();
    }

    [RelayCommand]
    private void SelectInClientSkills()
    {
        if (!IsSkillDb || List?.SelectedRow is not { } row) return;
        string aegis = row.Record.GetString("Name") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(aegis) || _clientSkills is null || !_clientSkills.Exists(aegis))
        {
            Views.ConfirmDialog.Alert("Not in Client Skills",
                $"Skill '{(string.IsNullOrWhiteSpace(aegis) ? "#" + row.KeyText : aegis)}' doesn't exist in the Client Skills files (skillinfoz) yet.");
            return;
        }
        _navigate?.Invoke("client_skills", RecordKey.Of(aegis));
    }

    [RelayCommand]
    private void SelectInClientItems()
    {
        if (!IsItemDb || List?.SelectedRow is not { } row) return;
        int id = (int)row.Key.AsInt;
        if (_clientItems is null || !_clientItems.Exists(id))
        {
            Views.ConfirmDialog.Alert("Not in Client Items",
                $"Item #{id} doesn't exist in the Client Items files (itemInfo.lua / itemInfo_C.lua) yet.\n\n" +
                "Use “Add in Client Items” to create its client text.");
            return;
        }
        _navigate?.Invoke("client_items", row.Key);
    }

    [RelayCommand]
    private void AddInClientItems()
    {
        if (!IsItemDb || _clientItems is null || List?.SelectedRow is not { } row) return;
        int id = (int)row.Key.AsInt;
        // Already has client text? Just navigate to it. Seeding a SeedClientTextCommand over an existing entry
        // would put an unconditional Remove on the undo stack, so a later undo would silently delete the user's
        // pre-existing client text (that command is documented for NEW ids only).
        if (_clientItems.Exists(id)) { _navigate?.Invoke("client_items", row.Key); return; }
        var entry = _clientItems.GetOrCreate(id);
        if (string.IsNullOrEmpty(entry.IdentifiedDisplayName))
            entry.IdentifiedDisplayName = row.Record.GetString("Name") ?? string.Empty;
        entry.SlotCount = row.Record.GetInt("Slots");
        entry.ClassNum = row.Record.GetInt("View");
        _session.Commands.Execute(_clientItems.SeedClientTextCommand(entry)); // undoable, so it isn't a stuck orphan
        _navigate?.Invoke("client_items", row.Key);
    }

    private static int? PromptId(string title, string prompt, int initial)
    {
        var dlg = new Views.IdInputDialog(title, prompt, initial) { Owner = System.Windows.Application.Current.MainWindow };
        return dlg.ShowDialog() == true ? dlg.Value : null;
    }

    /// <summary>Prompts for a reference-db value (e.g. the mob a mob_avail entry disguises) with autocomplete.</summary>
    private string? PromptReference(string title, string prompt, string referenceDb, string initial = "")
    {
        var dlg = new Views.ReferenceInputDialog(title, prompt, q => _references.Search(referenceDb, q, 60), initial)
        {
            Owner = System.Windows.Application.Current.MainWindow,
        };
        return dlg.ShowDialog() == true ? dlg.Value : null;
    }

    /// <summary>Copies the selected entry — or every selected entry when several are multi-selected —
    /// as a complete import YAML document to the clipboard.</summary>
    [RelayCommand]
    private void CopyEntry()
    {
        var records = SelectedRecords();
        if (records.Count == 0) return;
        var yaml = new Core.Serialization.YamlDbWriter().WriteToString(_schema, records);
        try { System.Windows.Clipboard.SetText(yaml); } catch { /* clipboard busy */ }
        if (records.Count > 1) PortReportText = $"Copied {records.Count} entries as YAML to the clipboard.";
    }

    /// <summary>The records to act on for a copy: the multi-selection (in list order) when more than one
    /// row is selected, otherwise just the single selected row.</summary>
    private List<DbRecord> SelectedRecords()
    {
        if (List is null) return new List<DbRecord>();
        if (List.SelectedRows.Count > 1)
        {
            var set = new HashSet<RecordRowViewModel>(List.SelectedRows);
            return List.Rows.Where(set.Contains).Select(r => r.Record).ToList(); // preserve visible order
        }
        return List.SelectedRow is { } row ? new List<DbRecord> { row.Record } : new List<DbRecord>();
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
        try
        {
            // Resolve the active overlay on the background thread too — with lazy mode-loading, .For(Mode)
            // may parse that mode's base on first use (e.g. after a mode switch); keep it off the UI thread.
            (_modeSet, _overlay) = await Task.Run(() =>
            {
                var ms = _session.GetModeSet(_schema);
                return (ms, ms.For(_session.Mode));
            });
        }
        catch (Exception ex)
        {
            // A malformed import/base file must not leave the screen spinning forever or silently swallow
            // the error (the caller is fire-and-forget). Stop loading, log, and tell the user which DB failed.
            IsLoading = false;
            Serilog.Log.Error(ex, "Failed to load database {Schema}", _schema.Id);
            Views.ConfirmDialog.Alert("Couldn't load this database",
                $"“{_schema.Id}” could not be loaded — a data file may be malformed:\n\n{ex.Message}");
            return;
        }

        var editor = new RecordEditorViewModel(_overlay, _session.Commands, _references, _session.ScriptCatalog, _session.Mode, _session.Validation, _referenceIndex);

        // Items get list icons resolved from their client resource name (lazy, per visible row).
        Func<RecordKey, ImageSource?>? iconResolver = null;
        if (_schema.Id == "item_db" && _clientItems is not null && _images is not null)
            iconResolver = key => _images.ItemIcon(_clientItems.ResourceOf((int)key.AsInt)); // no-clone read per render

        var list = new DbListViewModel(_overlay, iconResolver);
        list.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DbListViewModel.SelectedRow))
                ApplySelection(list.SelectedRow);
        };
        editor.RecordChanged += () => list.SelectedRow?.Refresh();
        // Creating an override makes the record editable — refresh the row's origin pill and rebuild the
        // side/drop cards so they unlock.
        editor.OverrideCreated += () => { list.SelectedRow?.Refresh(); ApplySelection(list.SelectedRow); };

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
            ? new MobSpriteViewModel(row.Record, _mobSprite!, _images!, _session.Commands)
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
        {
            // Ask for the id (pre-filled with the next free one ≥ 30000); abort on cancel, reject duplicates.
            if (PromptId($"New {_schema.DisplayName} entry", "ID", NextFreeId(keyField.Name)) is not { } id) return;
            if (_overlay.GetEffective(RecordKey.Of(id)) is not null) { PortReportText = $"ID {id} already exists — pick another."; return; }
            // Items live in two files: reject an id that's already taken on the client side too (the Item Forge
            // checks both; the list add must match so it can't create a server item over existing client text).
            if (_schema.Id == "item_db" && _clientItems is not null && _clientItems.Exists(id))
            { PortReportText = $"ID {id} already has client item text — pick another."; return; }
            record.SetRaw(keyField.Name, id);
        }
        else if (keyField.Kind == FieldKind.Reference && keyField.Enum?.ReferenceDb is { } refDb)
        {
            // A reference key (mob_avail / pet_db "Mob") — prompt for the real value with autocomplete instead
            // of stamping an un-editable auto "CUSTOM_n".
            if (PromptReference($"New {_schema.DisplayName} entry", keyField.Label, refDb) is not { } chosen) return;
            if (_overlay.GetEffective(RecordKey.Of(chosen)) is not null) { PortReportText = $"'{chosen}' already has an entry — pick another."; return; }
            record.SetRaw(keyField.Name, chosen);
        }
        else
        {
            record.SetRaw(keyField.Name, UniqueStringKey(keyField.Name));
        }

        if (_schema.Field("AegisName") is not null)
            record.SetRaw("AegisName", $"Custom_{record.Key}");

        var display = _schema.DisplayField;
        if (display is not null && display.Kind == FieldKind.String && !record.Has(display.Name))
            record.SetRaw(display.Name, $"Custom {_schema.DisplayName}");

        // Items: seed a matching client-text entry so the new item is cross-file from the start — as ONE undo
        // step with the record add, so undoing the add also drops the client text (no orphan, no stuck Save).
        if (_schema.Id == "item_db" && _clientItems is not null)
        {
            int id = record.GetInt(keyField.Name);
            var entry = _clientItems.GetOrCreate(id);
            entry.IdentifiedDisplayName = record.GetString("Name") ?? string.Empty;
            entry.SlotCount = record.GetInt("Slots");
            entry.ClassNum = record.GetInt("View");
            using (_session.Commands.BeginBatch($"Add {_schema.DisplayName} {record.Key}"))
            {
                _session.Commands.Execute(new AddRecordCommand(_overlay, record));
                _session.Commands.Execute(_clientItems.SeedClientTextCommand(entry));
            }
        }
        else
        {
            _session.Commands.Execute(new AddRecordCommand(_overlay, record));
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
