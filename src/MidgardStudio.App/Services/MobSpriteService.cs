using System.IO;
using MidgardStudio.Core.IO;
using MidgardStudio.Core.Lua;
using MidgardStudio.Core.Workspace;

namespace MidgardStudio.App.Services;

/// <summary>
/// Registers a monster's client sprite: appends <c>JT_&lt;NAME&gt; = &lt;mobId&gt;</c> to npcidentity.lub's
/// jobtbl and <c>[jobtbl.JT_&lt;NAME&gt;] = "&lt;sprite&gt;"</c> to jobname.lub's JobNameTable, atomically.
/// </summary>
public sealed class MobSpriteService
{
    private readonly WorkspaceSession _session;
    private readonly LuaFileCodec _codec = new(1252);

    public MobSpriteService(WorkspaceSession session) => _session = session;

    private WorkspacePaths Paths => _session.Paths;

    private string DataInfoDir => Path.Combine(Paths.LuaFilesRoot, "datainfo");
    private string NpcIdentityPath => Path.Combine(DataInfoDir, "npcidentity.lub");
    private string JobNamePath => Path.Combine(DataInfoDir, "jobname.lub");

    public bool IsAvailable => File.Exists(NpcIdentityPath) && File.Exists(JobNamePath);

    public sealed record MobSpriteResult(string ConstantName, string Sprite, bool AlreadyRegistered);

    /// <summary>Looks up an existing JT_ constant mapped to the given mob id, if any.</summary>
    public string? FindConstantForMob(int mobId)
    {
        if (!File.Exists(NpcIdentityPath)) return null;
        var constants = AccessoryTables.ReadConstants(_codec.ReadText(NpcIdentityPath), "jobtbl");
        return constants.FirstOrDefault(kv => kv.Value == mobId).Key;
    }

    public MobSpriteResult RegisterMob(int mobId, string aegisName, string spriteName)
    {
        string idText = _codec.ReadText(NpcIdentityPath);
        var constants = AccessoryTables.ReadConstants(idText, "jobtbl");

        // Reuse an existing constant for this mob id if present, else create JT_<AEGIS>.
        string? existing = constants.FirstOrDefault(kv => kv.Value == mobId).Key;
        if (existing is not null)
        {
            string jobText0 = AccessoryTables.AppendName(_codec.ReadText(JobNamePath), "JobNameTable", "jobtbl", existing, spriteName);
            var tx0 = new FileTransaction(Path.Combine(Paths.LuaFilesRoot, ".midgard-backup"));
            tx0.Stage(JobNamePath, _codec.EncodeText(jobText0));
            tx0.Commit();
            return new MobSpriteResult(existing, spriteName, AlreadyRegistered: true);
        }

        string baseName = "JT_" + Sanitize(aegisName);
        string constName = baseName;
        int suffix = 1;
        while (constants.ContainsKey(constName)) constName = $"{baseName}_{suffix++}";

        string newIdText = AccessoryTables.AppendConstant(idText, "jobtbl", constName, mobId);
        string newJobText = AccessoryTables.AppendName(_codec.ReadText(JobNamePath), "JobNameTable", "jobtbl", constName, spriteName);

        var tx = new FileTransaction(Path.Combine(Paths.LuaFilesRoot, ".midgard-backup"));
        tx.Stage(NpcIdentityPath, _codec.EncodeText(newIdText));
        tx.Stage(JobNamePath, _codec.EncodeText(newJobText));
        tx.Commit();

        return new MobSpriteResult(constName, spriteName, AlreadyRegistered: false);
    }

    private static string Sanitize(string s) =>
        new(s.Select(c => char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : '_').ToArray());
}
