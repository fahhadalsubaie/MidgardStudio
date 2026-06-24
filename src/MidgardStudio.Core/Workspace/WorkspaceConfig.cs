namespace MidgardStudio.Core.Workspace;

/// <summary>
/// A single named workspace profile (one server's paths + settings). Profiles let a user manage
/// several servers; they are persisted together under %APPDATA%\Midgard Studio\profiles.json with
/// one marked active. The legacy single workspace.json is migrated into a "Default" profile.
/// </summary>
public sealed class WorkspaceConfig
{
    /// <summary>Profile name (unique, case-insensitive). Shown in the Configuration Wizard.</summary>
    public string Name { get; set; } = "Default";

    public WorkspacePaths Paths { get; set; } = new();

    /// <summary>The ruleset this profile targets (Renewal or Pre-Renewal). The app hides the other
    /// system's fields and uses this as the active mode on open.</summary>
    public ServerMode DefaultMode { get; set; } = ServerMode.Renewal;

    /// <summary>Codepage used to read/write loose client lua files. Default Windows-1252.</summary>
    public int ClientCodepage { get; set; } = 1252;

    /// <summary>Layered GRF archive paths (lowest priority first; last wins), plus optional loose data folders.</summary>
    public List<string> GrfPaths { get; set; } = new();

    /// <summary>Recently opened repository roots, most recent first.</summary>
    public List<string> RecentRepoRoots { get; set; } = new();

    /// <summary>When this profile was last opened (drives the "recent profiles" ordering).</summary>
    public DateTime LastOpenedUtc { get; set; }

    public WorkspaceConfig Clone() => new()
    {
        Name = Name,
        Paths = new WorkspacePaths
        {
            ServerDbRoot = Paths.ServerDbRoot,
            LuaFilesRoot = Paths.LuaFilesRoot,
            SystemEnRoot = Paths.SystemEnRoot,
            ItemInfoPath = Paths.ItemInfoPath,
            ItemInfoCustomPath = Paths.ItemInfoCustomPath,
        },
        DefaultMode = DefaultMode,
        ClientCodepage = ClientCodepage,
        GrfPaths = new List<string>(GrfPaths),
        RecentRepoRoots = new List<string>(RecentRepoRoots),
        LastOpenedUtc = LastOpenedUtc,
    };
}
