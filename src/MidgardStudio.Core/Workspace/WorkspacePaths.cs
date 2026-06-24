namespace MidgardStudio.Core.Workspace;

/// <summary>
/// Absolute roots the app reads from and writes to. Defaults follow the custom-items
/// repository layout (server-db/db, lua-files, SystemEN).
/// </summary>
public sealed class WorkspacePaths
{
    /// <summary>Root of the server YAML databases, e.g. ...\server-db\db.</summary>
    public string ServerDbRoot { get; set; } = "";

    /// <summary>Root of the loose client lua/lub data files, e.g. ...\lua-files.</summary>
    public string LuaFilesRoot { get; set; } = "";

    /// <summary>Root of the SystemEN client files (legacy; kept to derive itemInfo paths when the
    /// explicit ones below are unset). New profiles set the two itemInfo file paths directly.</summary>
    public string SystemEnRoot { get; set; } = "";

    /// <summary>The base/official client item table (itemInfo.lua, with a single <c>tbl</c>). Read for
    /// the core item text + icon resource names. A "unified" server has only this file and the app
    /// writes edits back into it. Optional.</summary>
    public string ItemInfoPath { get; set; } = "";

    /// <summary>The custom client item table (itemInfo_C.lua, with <c>tbl_custom</c>/<c>tbl_override</c>).
    /// When set, the app writes here and leaves the base file pristine. Optional.</summary>
    public string ItemInfoCustomPath { get; set; } = "";

    public static WorkspacePaths CreateDefault(string repoRoot) => new()
    {
        ServerDbRoot = Path.Combine(repoRoot, "server-db", "db"),
        LuaFilesRoot = Path.Combine(repoRoot, "lua-files"),
        SystemEnRoot = Path.Combine(repoRoot, "SystemEN"),
        ItemInfoPath = Path.Combine(repoRoot, "SystemEN", "LuaFiles514", "itemInfo.lua"),
        ItemInfoCustomPath = Path.Combine(repoRoot, "SystemEN", "itemInfo_C.lua"),
    };

    /// <summary>True when the profile is usable — the server DB root (the one required to load) exists.</summary>
    public bool AllExist() =>
        !string.IsNullOrWhiteSpace(ServerDbRoot) && Directory.Exists(ServerDbRoot);
}
