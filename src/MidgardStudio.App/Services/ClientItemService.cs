using System.IO;
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
public sealed class ClientItemService
{
    private readonly WorkspaceSession _session;
    private readonly LuaFileCodec _codec = new(1252);
    private readonly ItemInfoReader _reader = new();
    private ItemInfoFile? _file;
    private OfficialItemInfo? _official;
    private string? _baseText;

    public ClientItemService(WorkspaceSession session)
    {
        _session = session;
        // A profile switch points at a different server -> drop the cached client tables.
        _session.WorkspaceReloaded += () => { _file = null; _official = null; _baseText = null; IsDirty = false; };
    }

    public bool IsDirty { get; private set; }

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

    private ItemInfoFile ClientFile => _file ??= System.IO.File.Exists(CustomPath)
        ? _reader.ReadCustomFile(_codec.ReadText(CustomPath))
        : new ItemInfoFile();

    public bool IsOfficial(int id) => Official.Contains(id);

    public ItemInfoTarget TargetFor(int id) => Official.Contains(id) ? ItemInfoTarget.Override : ItemInfoTarget.Custom;

    public bool Has(int id) => ClientFile.Custom.ContainsKey(id) || ClientFile.Override.ContainsKey(id) || Official.Contains(id);

    public ItemInfoEntry GetOrCreate(int id)
    {
        if (ClientFile.Custom.TryGetValue(id, out var c)) return c;
        if (ClientFile.Override.TryGetValue(id, out var o)) return o;
        if (Official.Entry(id) is { } b) return b.Clone(); // editable copy of the core entry
        return new ItemInfoEntry { Id = id };
    }

    /// <summary>Stores the entry into the correct table (routing by official id) and marks dirty.</summary>
    public void Upsert(ItemInfoEntry entry)
    {
        ClientFile.Custom.Remove(entry.Id);
        ClientFile.Override.Remove(entry.Id);
        if (TargetFor(entry.Id) == ItemInfoTarget.Override) ClientFile.Override[entry.Id] = entry;
        else ClientFile.Custom[entry.Id] = entry;
        IsDirty = true;
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
        IsDirty = false;
    }
}
