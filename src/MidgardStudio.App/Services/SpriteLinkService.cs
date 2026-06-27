using System.IO;
using MidgardStudio.Core.IO;
using MidgardStudio.Core.Lua;
using MidgardStudio.Core.Workspace;

namespace MidgardStudio.App.Services;

/// <summary>
/// Registers a headgear/accessory sprite: allocates an ACCESSORY_IDs constant, maps it to the sprite
/// file in accname.lub (+ accname_eng.lub), and returns the View id to set on the server item. The
/// three lua files are written atomically.
/// </summary>
public sealed class SpriteLinkService
{
    private readonly WorkspaceSession _session;
    private LuaFileCodec _codec => _session.ClientCodec; // fixed Windows-1252 (the RO client boundary), independent of the profile Display Encoding

    public SpriteLinkService(WorkspaceSession session) => _session = session;

    public sealed record SpriteLinkResult(int ViewId, string ConstantName, string Sprite);

    private WorkspacePaths Paths => _session.Paths;

    private string DataInfoDir => Path.Combine(Paths.LuaFilesRoot, "datainfo");
    private string AccIdPath => Path.Combine(DataInfoDir, "accessoryid.lub");
    private string AccNamePath => Path.Combine(DataInfoDir, "accname.lub");
    private string AccNameEngPath => Path.Combine(DataInfoDir, "accname_eng.lub");

    public bool IsAvailable => File.Exists(AccIdPath) && File.Exists(AccNamePath);

    /// <summary>True when an ACCESSORY_IDs constant is mapped to this View id in accessoryid.lub (validation).</summary>
    public bool HasView(int viewId)
    {
        if (!File.Exists(AccIdPath)) return false;
        try { return AccessoryTables.ReadConstants(_codec.ReadText(AccIdPath)).Values.Contains(viewId); }
        catch { return false; }
    }

    /// <summary>All View ids mapped in accessoryid.lub, parsed once — use this for a bulk check instead of
    /// calling <see cref="HasView"/> per item (which re-reads + re-parses the file).</summary>
    public HashSet<int> MappedViewIds()
    {
        if (!File.Exists(AccIdPath)) return new HashSet<int>();
        try { return new HashSet<int>(AccessoryTables.ReadConstants(_codec.ReadText(AccIdPath)).Values); }
        catch { return new HashSet<int>(); }
    }

    /// <summary>The sprite file mapped to a View id (via accessoryid.lub + accname.lub), or null.</summary>
    public string? SpriteForView(int viewId)
    {
        if (!IsAvailable) return null;
        try
        {
            var constants = AccessoryTables.ReadConstants(_codec.ReadText(AccIdPath));
            string? constName = constants.FirstOrDefault(kv => kv.Value == viewId).Key;
            if (constName is null) return null;
            var names = AccessoryTables.ReadNames(_codec.ReadText(AccNamePath), "AccNameTable");
            return names.GetValueOrDefault(constName);
        }
        catch { return null; }
    }

    public SpriteLinkResult LinkAccessory(string aegisName, string spriteFile)
    {
        string idText = _codec.ReadText(AccIdPath);
        var constants = AccessoryTables.ReadConstants(idText);

        string baseName = "ACCESSORY_" + Sanitize(aegisName);
        string constName = baseName;
        int suffix = 1;
        while (constants.ContainsKey(constName)) constName = $"{baseName}_{suffix++}";

        int id = AccessoryTables.NextFreeId(constants);
        string sprite = spriteFile.StartsWith("_", StringComparison.Ordinal) ? spriteFile : "_" + spriteFile;

        string newIdText = AccessoryTables.AppendConstant(idText, "ACCESSORY_IDs", constName, id);
        string newNameText = AccessoryTables.AppendName(_codec.ReadText(AccNamePath), "AccNameTable", "ACCESSORY_IDs", constName, sprite);

        var tx = new FileTransaction(Path.Combine(Paths.LuaFilesRoot, ".midgard-backup"));
        tx.Stage(AccIdPath, _codec.EncodeText(newIdText));
        tx.Stage(AccNamePath, _codec.EncodeText(newNameText));

        if (File.Exists(AccNameEngPath))
        {
            string newEng = AccessoryTables.AppendName(_codec.ReadText(AccNameEngPath), "AccNameTable_Eng", "ACCESSORY_IDs", constName, sprite);
            tx.Stage(AccNameEngPath, _codec.EncodeText(newEng));
        }

        tx.Commit();
        return new SpriteLinkResult(id, constName, sprite);
    }

    private static string Sanitize(string s) =>
        new(s.Select(c => char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : '_').ToArray());
}
