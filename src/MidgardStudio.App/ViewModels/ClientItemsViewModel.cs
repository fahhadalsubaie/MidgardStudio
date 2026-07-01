using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using MidgardStudio.App.Services;
using MidgardStudio.Core.Commands;
using MidgardStudio.Core.Lua;
using MidgardStudio.Core.Model;
using MidgardStudio.Core.Overlay;
using MidgardStudio.Core.Schema;

namespace MidgardStudio.App.ViewModels;

/// <summary>
/// The client-side item editor (separate from the Server Items section): the same item list, but the
/// detail panel edits the client itemInfo (display/resource/description names, slots, ClassNum, costume,
/// headgear sprite + icon) read from itemInfo.lua / itemInfo_C.lua.
/// </summary>
public sealed partial class ClientItemsViewModel : ObservableObject, IDisposable
{
    private readonly WorkspaceSession _session;
    private readonly ClientItemService _clientItems;
    private readonly GrfImageService _images;
    private readonly SpriteLinkService _sprite;
    private readonly AppSettingsService _settings;
    private readonly DbSchema _itemSchema;
    private readonly Action<string, RecordKey>? _navigate;
    private readonly Action? _syncItems;
    private OverlayTable? _overlay;

    public ClientItemsViewModel(WorkspaceSession session, ClientItemService clientItems, GrfImageService images,
        SpriteLinkService sprite, AppSettingsService settings, DbSchema itemSchema,
        Action<string, RecordKey>? navigate = null, Action? syncItems = null)
    {
        _session = session;
        _clientItems = clientItems;
        _images = images;
        _sprite = sprite;
        _settings = settings;
        _itemSchema = itemSchema;
        _navigate = navigate;
        _syncItems = syncItems;
        _session.Commands.UndoRedoPerformed += OnUndoRedo;
    }

    private DbSchema Schema => _itemSchema;

    private void OnUndoRedo()
    {
        List?.SyncWithOverlay();
        RefreshEditor();
        OnPropertyChanged(nameof(CanRestore));
    }

    public void Dispose() => _session.Commands.UndoRedoPerformed -= OnUndoRedo;

    /// <summary>True when the selected row is an override of a base entry (so it can be reverted).</summary>
    public bool CanRestore =>
        _overlay is not null && List?.SelectedRow is { } r && _overlay.OriginOf(r.Key) == RecordOrigin.Overridden;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private DbListViewModel? _list;

    [ObservableProperty]
    private ClientTextViewModel? _editor;

    public bool HasEditor => Editor is not null;

    partial void OnEditorChanged(ClientTextViewModel? value) => OnPropertyChanged(nameof(HasEditor));

    /// <summary>Autocompletes the selected item(s): bulk (with confirm + summary) when several rows are
    /// selected, otherwise the single open editor.</summary>
    [RelayCommand]
    private void AutocompleteSelected()
    {
        if (List?.SelectedRows is { Count: > 1 } rows) BulkAutocomplete(rows.ToList());
        else Editor?.AutocompleteCommand.Execute(null);
    }

    /// <summary>Copies the selected item's client itemInfo Lua block to the clipboard.</summary>
    [RelayCommand]
    private void CopyClientLua() => Editor?.CopyLuaCommand.Execute(null);

    /// <summary>Regenerates client text for every selected row in one undo step.</summary>
    private void BulkAutocomplete(IReadOnlyList<RecordRowViewModel> rows)
    {
        if (rows.Count == 0) return;
        var cfg = _settings.Settings.Autocomplete;
        if (!Views.ConfirmDialog.Show("Autocomplete selected",
                $"Autocomplete {rows.Count} selected item(s)? The property lines are regenerated from server data; any lore text at the top is kept.",
                yes: "Autocomplete")) return;

        var gen = new ItemAutocomplete(cfg, Resolver());
        var ops = new List<(ItemInfoEntry Old, ItemInfoEntry New)>();
        foreach (var row in rows)
        {
            // Skip orphaned client entries (no server record) — there's no server data to autocomplete from, and
            // mirroring the synthetic record's Slots/View would zero the entry's real values.
            if (_overlay?.GetEffective(row.Key) is null) continue;
            int id = (int)row.Key.AsInt;
            var oldClone = _clientItems.GetOrCreate(id).Clone();
            var updated = oldClone.Clone();

            if (cfg.OverwriteExisting || string.IsNullOrWhiteSpace(updated.IdentifiedDisplayName))
                updated.IdentifiedDisplayName = gen.DisplayName(row.Record);
            if (cfg.OverwriteExisting || updated.IdentifiedDescription.Count == 0)
                updated.IdentifiedDescription = gen.BuildDescription(row.Record, oldClone.IdentifiedDescription);
            updated.SlotCount = row.Record.GetInt("Slots");
            updated.ClassNum = row.Record.GetInt("View");
            if (cfg.DefaultUnidentifiedDescription.Length > 0 && (cfg.OverwriteExisting || updated.UnidentifiedDescription.Count == 0))
                updated.UnidentifiedDescription = cfg.DefaultUnidentifiedDescription.Replace("\r\n", "\n").Split('\n').ToList();

            ops.Add((oldClone, updated));
        }

        if (ops.Count == 0)
        {
            Views.ConfirmDialog.Show("Nothing to autocomplete",
                "The selected item(s) have no server record to autocomplete from.", yes: "OK");
            return;
        }

        _session.Commands.Execute(new ListMutateCommand($"Autocomplete {ops.Count} items",
            () => { foreach (var op in ops) _clientItems.Upsert(op.New.Clone()); RefreshEditor(); },
            () => { foreach (var op in ops) _clientItems.Upsert(op.Old.Clone()); RefreshEditor(); }));

        string skipped = ops.Count < rows.Count ? $" ({rows.Count - ops.Count} client-only item(s) skipped.)" : string.Empty;
        Views.ConfirmDialog.Show("Autocomplete complete", $"Autocompleted {ops.Count} item(s).{skipped}", yes: "OK");
    }

    /// <summary>Builds the client-text editor for a row, wired with the shared icon picker (its "copy an existing
    /// item's icon" rows come from the item_db overlay) and the sprite picker.</summary>
    private ClientTextViewModel MakeEditor(DbRecord record) =>
        new(record, _clientItems, _images, _session.Commands, _settings, Resolver(), _sprite,
            () => _overlay is null
                ? System.Array.Empty<IconItemRow>()
                : IconPickerRows.BuildItemRows(_overlay.Effective(), _images, _clientItems),
            serverIsDetached: _overlay?.GetEffective(record.Key) is null); // orphan (client-only) -> no server data to autocomplete from

    /// <summary>Rebuilds the open editor for the current row (keeps it in sync after bulk / undo). Null-safe if
    /// the selected row's server record was deleted out from under it.</summary>
    private void RefreshEditor()
    {
        Editor = List?.SelectedRow?.EffectiveRecord is { } rec ? MakeEditor(rec) : null;
    }

    /// <summary>Virtual rows for client entries that have NO server item_db record (e.g. after a "server only"
    /// delete of a custom item) — so the client text stays visible and editable in this list. Each is backed by
    /// a synthetic id-only record; the editable client fields come from the client entry itself.</summary>
    private IEnumerable<RecordRowViewModel> OrphanClientRows()
    {
        if (_overlay is null) yield break;
        string keyField = _itemSchema.KeyField?.Name ?? "Id";
        foreach (var (id, name) in _clientItems.EditableClientEntries())
        {
            var key = RecordKey.Of(id);
            if (_overlay.GetEffective(key) is not null) continue; // still has a server record -> already listed
            var synthetic = new DbRecord(_itemSchema);
            synthetic.SetRaw(keyField, id);
            if (!string.IsNullOrEmpty(name)) synthetic.SetRaw("Name", name);
            yield return new RecordRowViewModel(_overlay, key,
                k => _images.ItemIcon(_clientItems.ResourceOf((int)k.AsInt)), synthetic);
        }
    }

    private bool _skillResolved;
    private Func<string, string?>? _skill;

    /// <summary>The skill AegisName → display-name resolver (from <see cref="SkillLookupService"/>), or null.</summary>
    private Func<string, string?>? Resolver()
    {
        if (_skillResolved) return _skill;
        _skillResolved = true;
        try { if (App.Services.GetService<SkillLookupService>() is { } s) _skill = s.Display; } catch { /* host not ready */ }
        return _skill;
    }

    // ===== Record actions (shared item overlay — mirror the Items list) =====

    [RelayCommand]
    private void AddCustom()
    {
        if (_overlay is null || List is null || Schema.KeyField is not { } keyField) return;

        var record = new DbRecord(Schema);
        if (keyField.Kind == FieldKind.Int)
        {
            if (PromptId($"New {Schema.DisplayName} entry", "ID", NextFreeId(keyField.Name)) is not { } id) return;
            if (_overlay.GetEffective(RecordKey.Of(id)) is not null || _clientItems.Exists(id)) return; // taken on either side
            record.SetRaw(keyField.Name, id);
        }
        else record.SetRaw(keyField.Name, UniqueStringKey(keyField.Name));

        if (Schema.Field("AegisName") is not null) record.SetRaw("AegisName", $"Custom_{record.Key}");
        if (Schema.DisplayField is { Kind: FieldKind.String } display && !record.Has(display.Name))
            record.SetRaw(display.Name, $"Custom {Schema.DisplayName}");

        // Seed a matching client-text entry so the new item is cross-file from the start — as ONE undo step with
        // the record add, so undoing the add drops the client text too (no orphan, no stuck Save button).
        int newId = record.GetInt(keyField.Name);
        var entry = _clientItems.GetOrCreate(newId);
        entry.IdentifiedDisplayName = record.GetString("Name") ?? string.Empty;
        entry.SlotCount = record.GetInt("Slots");
        entry.ClassNum = record.GetInt("View");
        using (_session.Commands.BeginBatch($"Add {Schema.DisplayName} {record.Key}"))
        {
            _session.Commands.Execute(new AddRecordCommand(_overlay, record));
            _session.Commands.Execute(_clientItems.SeedClientTextCommand(entry));
        }

        var row = List.CreateRow(record.Key);
        List.AddRow(row);
        List.SelectedRow = row;
        _syncItems?.Invoke();
    }

    [RelayCommand]
    private void Duplicate()
    {
        if (_overlay is null || List?.SelectedRow is not { } sel || Schema.KeyField is not { } keyField) return;

        var clone = sel.Record.DeepClone();
        if (keyField.Kind == FieldKind.Int) clone.SetRaw(keyField.Name, NextFreeId(keyField.Name));
        else clone.SetRaw(keyField.Name, UniqueStringKey(keyField.Name));
        if (Schema.Field("AegisName") is not null) clone.SetRaw("AegisName", $"Custom_{clone.Key}");

        _session.Commands.Execute(new AddRecordCommand(_overlay, clone));
        var row = List.CreateRow(clone.Key);
        List.AddRow(row);
        List.SelectedRow = row;
        _syncItems?.Invoke();
    }

    /// <summary>Deletes the selected item's CLIENT itemInfo text (server and client are independent). Always
    /// confirms; when the item also has a server entry it offers to remove both sides in one step. A deleted
    /// custom client entry drops out of this client-filtered list; deleting an override reverts to base text.</summary>
    [RelayCommand]
    private void DeleteEntry()
    {
        if (List?.SelectedRow is not { } row) return;
        int id = (int)row.Key.AsInt;

        var clientCmd = _clientItems.RemoveClientTextCommand(id); // null when there's no deletable (custom/override) client text
        bool hasServer = _overlay is not null
            && _overlay.OriginOf(row.Key) != RecordOrigin.Base
            && _overlay.GetEffective(row.Key) is not null;

        bool alsoServer;
        if (clientCmd is not null && hasServer)
        {
            var choice = Views.ConfirmDialog.Choose("Delete client text",
                $"Delete client text for #{id}?\n\nThis item also has a server entry (item_db).",
                primary: "Delete both", alternate: "Client only");
            if (choice == Views.ConfirmDialog.Choice.Cancel) return;
            alsoServer = choice == Views.ConfirmDialog.Choice.Primary;
        }
        else if (clientCmd is not null)
        {
            if (!Views.ConfirmDialog.Show("Delete client text", $"Delete client text for #{id}?", yes: "Delete")) return;
            alsoServer = false;
        }
        else if (hasServer)
        {
            // No custom client text to delete (base-official), but there is a server entry — offer to delete that.
            if (!Views.ConfirmDialog.Show("Delete server item",
                    $"#{id} has no custom client text to delete.\n\nDelete its server item (item_db) instead?", yes: "Delete server item"))
                return;
            alsoServer = true;
        }
        else
        {
            Views.ConfirmDialog.Alert("Nothing to delete",
                "This item has no custom client text and no server entry to delete — it's a read-only official (base) entry.");
            return;
        }

        using (_session.Commands.BeginBatch($"Delete #{id}"))
        {
            if (clientCmd is not null) _session.Commands.Execute(clientCmd);
            if (alsoServer && _overlay!.GetEffective(row.Key) is { } import)
                _session.Commands.Execute(new RemoveImportCommand(_overlay, import));
        }
        List.SyncWithOverlay();  // re-applies the client-only filter: a deleted custom entry drops from the list
        RefreshEditor();          // an override reverted to base -> show the base text
        _syncItems?.Invoke();
    }

    [RelayCommand]
    private void RestoreToDefault()
    {
        if (_overlay is null || List?.SelectedRow is not { } row) return;
        if (_overlay.OriginOf(row.Key) != RecordOrigin.Overridden) return;
        if (_overlay.GetEffective(row.Key) is not { } import) return;
        if (!Views.ConfirmDialog.Show("Restore to default",
                $"Restore #{row.Key} to its default (base) values?\nYour customizations for this entry will be discarded.",
                yes: "Restore")) return;
        _session.Commands.Execute(new RemoveImportCommand(_overlay, import));
        List.SyncWithOverlay();
        _syncItems?.Invoke();
    }

    [RelayCommand]
    private void ChangeId()
    {
        if (_overlay is null || List?.SelectedRow is not { } row || Schema.KeyField is not { Kind: FieldKind.Int } keyField) return;
        if (_overlay.OriginOf(row.Key) != RecordOrigin.NewCustom) return;

        int current = (int)row.Key.AsInt;
        if (PromptId("Change ID", "New ID", current) is not { } newId || newId == current) return;
        if (_overlay.GetEffective(RecordKey.Of(newId)) is not null) return;

        var clone = _overlay.GetEffective(row.Key)!.DeepClone();
        clone.SetRaw(keyField.Name, newId);
        using (_session.Commands.BeginBatch("Change ID"))
        {
            _session.Commands.Execute(new RemoveImportCommand(_overlay, _overlay.GetEffective(row.Key)!));
            _session.Commands.Execute(new AddRecordCommand(_overlay, clone));
        }
        List.SyncWithOverlay();
        List.SelectByKey(clone.Key);
        _syncItems?.Invoke();
    }

    [RelayCommand]
    private void CopyToId()
    {
        if (_overlay is null || List?.SelectedRow is not { } row || Schema.KeyField is not { Kind: FieldKind.Int } keyField) return;

        if (_overlay.GetEffective(row.Key) is not { } source) return; // client-only (orphan) row: no server record to copy
        if (PromptId("Copy to ID", "Target ID", NextFreeId(keyField.Name)) is not { } newId) return;
        if (_overlay.GetEffective(RecordKey.Of(newId)) is not null) return;

        var clone = source.DeepClone();
        clone.SetRaw(keyField.Name, newId);
        if (Schema.Field("AegisName") is not null) clone.SetRaw("AegisName", $"Custom_{newId}");
        _session.Commands.Execute(new AddRecordCommand(_overlay, clone));
        List.AddRow(List.CreateRow(clone.Key));
        List.SelectByKey(clone.Key);
        _syncItems?.Invoke();
    }

    /// <summary>Copies the selected entry — or every selected entry — as YAML to the clipboard.</summary>
    [RelayCommand]
    private void CopyEntry()
    {
        var records = SelectedRecords();
        if (records.Count == 0) return;
        var yaml = new Core.Serialization.YamlDbWriter().WriteToString(Schema, records);
        try { System.Windows.Clipboard.SetText(yaml); } catch { /* clipboard busy */ }
    }

    private List<DbRecord> SelectedRecords()
    {
        if (List is null) return new List<DbRecord>();
        if (List.SelectedRows.Count > 1)
        {
            var set = new HashSet<RecordRowViewModel>(List.SelectedRows);
            return List.Rows.Where(set.Contains).Select(r => r.Record).ToList();
        }
        return List.SelectedRow is { } row ? new List<DbRecord> { row.Record } : new List<DbRecord>();
    }

    /// <summary>Jumps to the server Items section for the selected item.</summary>
    [RelayCommand]
    private void SelectInItems()
    {
        if (List?.SelectedRow is { } row) _navigate?.Invoke("item_db", row.Key);
    }

    private static int? PromptId(string title, string prompt, int initial)
    {
        var dlg = new Views.IdInputDialog(title, prompt, initial) { Owner = System.Windows.Application.Current.MainWindow };
        return dlg.ShowDialog() == true ? dlg.Value : null;
    }

    private int NextFreeId(string keyField)
    {
        var used = new HashSet<int>();
        foreach (var r in _overlay!.Effective()) used.Add(r.GetInt(keyField));
        int id = 30000;
        while (used.Contains(id)) id++;
        return id;
    }

    private string UniqueStringKey(string keyField)
    {
        int n = 1;
        string candidate;
        do { candidate = $"CUSTOM_{n++}"; }
        while (_overlay!.Effective().Any(r => string.Equals(r.GetString(keyField), candidate, StringComparison.OrdinalIgnoreCase)));
        return candidate;
    }

    public async Task EnsureLoadedAsync()
    {
        // Already loaded: re-sync so the client-only list reflects any server-side changes made elsewhere
        // (e.g. a "server only" delete removed the item_db record) — its filter drops now-orphaned rows.
        if (_overlay is not null) { List?.SyncWithOverlay(); return; }

        IsLoading = true;
        try
        {
            // Resolve the active overlay on the background thread (lazy mode-load may parse a base on .For).
            _overlay = await Task.Run(() => _session.GetModeSet(_itemSchema).For(_session.Mode));
        }
        catch (Exception ex)
        {
            IsLoading = false;
            Serilog.Log.Error(ex, "Failed to load client items");
            Views.ConfirmDialog.Alert("Couldn't load Client Items",
                $"Client Items could not be loaded — a data file may be malformed:\n\n{ex.Message}");
            return;
        }

        var list = new DbListViewModel(_overlay,
            key => _images.ItemIcon(_clientItems.ResourceOf((int)key.AsInt)), // no-clone read per render
            key => _clientItems.Exists((int)key.AsInt), // only ids that actually have a client entry
            OrphanClientRows); // + client-only entries whose server item was deleted, so they stay editable
        list.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(DbListViewModel.SelectedRow)) return;
            Editor = list.SelectedRow?.EffectiveRecord is { } rec ? MakeEditor(rec) : null;
            OnPropertyChanged(nameof(CanRestore));
        };

        List = list;
        IsLoading = false;
        list.SelectedRow = list.Rows.FirstOrDefault();
        if (_pendingSelect is { } sel) { list.SelectByKey(sel); _pendingSelect = null; }
    }

    private RecordKey? _pendingSelect;

    public void SelectRow(RecordKey key)
    {
        if (List is not null) List.SelectByKey(key);
        else _pendingSelect = key; // applied once loaded (navigation from Items)
    }
}
