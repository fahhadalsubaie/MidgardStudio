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
    private readonly LuaFileCodec _codec = new(1252);

    public SpriteLinkService(WorkspaceSession session) => _session = session;

    public sealed record SpriteLinkResult(int ViewId, string ConstantName, string Sprite);

    private WorkspacePaths Paths => _session.Paths;

    private string DataInfoDir => Path.Combine(Paths.LuaFilesRoot, "datainfo");
    private string AccIdPath => Path.Combine(DataInfoDir, "accessoryid.lub");
    private string AccNamePath => Path.Combine(DataInfoDir, "accname.lub");
    private string AccNameEngPath => Path.Combine(DataInfoDir, "accname_eng.lub");

    public bool IsAvailable => File.Exists(AccIdPath) && File.Exists(AccNamePath);

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
