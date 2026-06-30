using System;
using System.IO;
using System.Linq;
using System.Text;
using MidgardStudio.Core.Commands;
using MidgardStudio.Core.IO;
using MidgardStudio.Core.Lua;
using MidgardStudio.Core.Workspace;

namespace MidgardStudio.App.Services;

/// <summary>
/// Loads and saves the client item text. Reads the base/official itemInfo.lua (single <c>tbl</c>) so
/// core items show their text + icon resource name, and the custom itemInfo_C.lua
/// (<c>tbl_custom</c>/<c>tbl_override</c>) for new/overridden items. Writes to the custom file when one
/// is configured; otherwise (a "unified" server with only itemInfo.lua) splices edits into the base file.
/// </summary>
public sealed class ClientItemService : IDirtySource
{
    private readonly WorkspaceSession _session;
    private LuaFileCodec _codec => _session.ClientCodec; // fixed Windows-1252 (the RO client boundary), independent of the profile Display Encoding
    private readonly ItemInfoReader _reader = new();
    private ItemInfoFile? _file;
    private OfficialItemInfo? _official;
    private string? _baseText;

    public ClientItemService(WorkspaceSession session)
    {
        _session = session;
        // A profile switch points at a different server -> drop the cached client tables.
        _session.WorkspaceReloaded += () => { _file = null; _official = null; _baseText = null; _savedSignature = null; _signatureCache = null; };
        // Every client content change goes through the command stack, so drop the memoized signature on any
        // stack change instead of re-serializing all entries on every dirty poll (CanExecute requery).
        _session.Commands.Changed += () => _signatureCache = null;
    }

    /// <summary>The serialized client-edit content captured at load and after each save; the dirty check
    /// compares the current content to it. Null until the client file is first read.</summary>
    private string? _savedSignature;

    /// <summary>Memoized current signature, recomputed lazily after any command-stack change.</summary>
    private string? _signatureCache;

    /// <summary>True when the in-memory client tables differ from what's on disk — drives both the Save
    /// button and the save-write decision. Computed by content comparison, so editing an item and then
    /// undoing back to its loaded/saved state correctly reports "nothing to save" (no sticky flag to leave
    /// lit), and a redundant override identical to the official entry is not treated as a change.</summary>
    public bool IsDirty => _file is not null && Signature() != _savedSignature;

    /// <summary>Fires when dirtiness may have changed — forwarded from the command stack, since every
    /// client edit runs through it (so <see cref="CompositeDirtyState"/> can treat this as a source).</summary>
    public event Action? DirtyChanged
    {
        add => _session.Commands.Changed += value;
        remove => _session.Commands.Changed -= value;
    }

    private WorkspacePaths Paths => _session.Paths;

    private string? RawCustom => string.IsNullOrWhiteSpace(Paths.ItemInfoCustomPath) ? null : Paths.ItemInfoCustomPath;
    private string? RawBase => string.IsNullOrWhiteSpace(Paths.ItemInfoPath) ? null : Paths.ItemInfoPath;

    /// <summary>Base/official itemInfo.lua. Falls back to the legacy SystemEN layout when not set explicitly.</summary>
    private string BasePath => RawBase ?? Path.Combine(Paths.SystemEnRoot, "LuaFiles514", "itemInfo.lua");

    /// <summary>Custom itemInfo_C.lua, or empty when this is a unified-file server (no custom file).</summary>
    private string CustomPath =>
        RawCustom ?? (RawBase is null && !string.IsNullOrWhiteSpace(Paths.SystemEnRoot)
            ? Path.Combine(Paths.SystemEnRoot, "itemInfo_C.lua")
            : string.Empty);

    /// <summary>True when there is no custom file — edits are written back into the unified base file.</summary>
    private bool UnifiedMode => string.IsNullOrEmpty(CustomPath);

    /// <summary>The file edits are written to (custom file, else the unified base file).</summary>
    private string WriteTargetPath => UnifiedMode ? BasePath : CustomPath;

    /// <summary>The file the next <see cref="Save"/> writes the client item text to (for the save summary).</summary>
    public string SaveTargetPath => WriteTargetPath;

    private string BaseText => _baseText ??= File.Exists(BasePath) ? _codec.ReadText(BasePath) : string.Empty;

    /// <summary>Lazily-indexed base/official itemInfo (entries parsed on demand — the base file is ~7 MB).</summary>
    private OfficialItemInfo Official => _official ??= new OfficialItemInfo(BaseText);

    private ItemInfoFile ClientFile
    {
        get
        {
            if (_file is null)
            {
                _file = File.Exists(CustomPath)
                    ? _reader.ReadCustomFile(_codec.ReadText(CustomPath))
                    : new ItemInfoFile();
                _savedSignature = Signature(); // baseline = the just-loaded content
            }
            return _file;
        }
    }

    public bool IsOfficial(int id) => Official.Contains(id);

    /// <summary>The official/base client entry for an id (a deep copy), or null when the item isn't an
    /// official one. Lets Autocomplete restore canonical client text instead of synthesizing.</summary>
    public ItemInfoEntry? OfficialEntry(int id) => Official.Entry(id)?.Clone();

    public ItemInfoTarget TargetFor(int id) => Official.Contains(id) ? ItemInfoTarget.Override : ItemInfoTarget.Custom;

    public bool Has(int id) => ClientFile.Custom.ContainsKey(id) || ClientFile.Override.ContainsKey(id) || Official.Contains(id);

    /// <summary>True only when an entry actually exists in the client lua files (custom, override, or the
    /// base itemInfo.lua) — unlike <see cref="GetOrCreate"/>, this never fabricates a blank entry. This is
    /// the source of truth for "this item exists in Client Items" (list membership, navigation, validation).</summary>
    public bool Exists(int id) =>
        ClientFile.Custom.ContainsKey(id) || ClientFile.Override.ContainsKey(id) || Official.Contains(id);

    /// <summary>The effective identified icon resource name for an id (custom → override → official), or null —
    /// read-only, no clone (cheap enough to call across the whole item_db for the icon picker).</summary>
    public string? ResourceOf(int id)
    {
        if (ClientFile.Custom.TryGetValue(id, out var c)) return c.IdentifiedResourceName;
        if (ClientFile.Override.TryGetValue(id, out var o)) return o.IdentifiedResourceName;
        return Official.Entry(id)?.IdentifiedResourceName;
    }

    public ItemInfoEntry GetOrCreate(int id)
    {
        if (ClientFile.Custom.TryGetValue(id, out var c)) return c;
        if (ClientFile.Override.TryGetValue(id, out var o)) return o;
        if (Official.Entry(id) is { } b) return b.Clone(); // editable copy of the core entry
        return new ItemInfoEntry { Id = id };
    }

    /// <summary>Stores the entry into the correct table (routing by official id). Dirtiness is still derived by
    /// content comparison (see <see cref="IsDirty"/>); the cache reset just forces that comparison to re-run.</summary>
    public void Upsert(ItemInfoEntry entry)
    {
        ClientFile.Custom.Remove(entry.Id);
        ClientFile.Override.Remove(entry.Id);
        if (TargetFor(entry.Id) == ItemInfoTarget.Override) ClientFile.Override[entry.Id] = entry;
        else ClientFile.Custom[entry.Id] = entry;
        // Upsert is the single in-memory mutator of the client tables. Invalidate AFTER mutating (so a load
        // triggered above can't re-cache the old value) — this covers callers that DON'T go through the command
        // stack (validator quick-fixes, cross-list "Add in Client Items"), which would otherwise stay clean.
        _signatureCache = null;
    }

    /// <summary>Removes a client entry entirely (undoes a "Create client text" quick-fix so it doesn't
    /// leave a phantom custom entry behind).</summary>
    public void Remove(int id)
    {
        ClientFile.Custom.Remove(id);
        ClientFile.Override.Remove(id);
        _signatureCache = null;
    }

    public void StageSave(FileTransaction tx)
    {
        if (_file is null) return;

        if (UnifiedMode)
        {
            string updated = new UnifiedItemInfoWriter()
                .Splice(BaseText, _file.Custom.Values.Concat(_file.Override.Values));
            tx.Stage(BasePath, _codec.EncodeText(updated));
            _baseText = updated;
            _official = null; // re-index on demand
            _signatureCache = null; // the no-op-override comparison depends on the (now-changed) official text
        }
        else
        {
            // Splice the two tables into the existing custom file so any hand-written content
            // (helper functions, comments, extra tables) is preserved; only generate a fresh file
            // from the template when none exists yet.
            string existing = File.Exists(CustomPath) ? _codec.ReadText(CustomPath) : string.Empty;
            if (string.IsNullOrWhiteSpace(existing))
            {
                tx.Stage(CustomPath, _codec.EncodeText(new ItemInfoWriter().Write(_file)));
            }
            else
            {
                var splicer = new UnifiedItemInfoWriter();
                string text = splicer.Splice(existing, _file.Custom.Values, "tbl_custom");
                text = splicer.Splice(text, _file.Override.Values, "tbl_override");
                tx.Stage(CustomPath, _codec.EncodeText(text));
            }
        }
    }

    public void Save()
    {
        if (!IsDirty || _file is null) return;
        string backupDir = Path.Combine(Path.GetDirectoryName(WriteTargetPath) ?? ".", ".midgard-backup");
        var tx = new FileTransaction(backupDir);
        StageSave(tx);
        tx.Commit();
        _signatureCache = null;        // content/official may have shifted while staging
        _savedSignature = Signature(); // re-baseline: the on-disk state now matches memory
    }

    /// <summary>A canonical serialization of the unsaved client edits: every Custom entry, plus Override
    /// entries that actually differ from the official base. A no-op override identical to the official entry
    /// changes nothing in-game, so it isn't counted — that's what lets "edit an official item, then undo"
    /// settle back to a clean state instead of leaving a redundant override behind.</summary>
    private string Signature() => _signatureCache ??= ComputeSignature();

    private string ComputeSignature()
    {
        if (_file is null) return string.Empty;
        var sb = new StringBuilder();
        foreach (var id in _file.Custom.Keys.OrderBy(k => k))
            sb.Append('C').Append(id).Append('\n').Append(ItemInfoWriter.FormatEntry(_file.Custom[id])).Append("\n\n");
        foreach (var id in _file.Override.Keys.OrderBy(k => k))
        {
            var entry = _file.Override[id];
            var entryText = ItemInfoWriter.FormatEntry(entry); // format once; reuse for both the no-op check and the append
            var official = Official.Entry(id);
            if (official is not null && entryText == ItemInfoWriter.FormatEntry(official))
                continue; // override identical to the official entry — a no-op, not a real change
            sb.Append('O').Append(id).Append('\n').Append(entryText).Append("\n\n");
        }
        return sb.ToString();
    }
}
