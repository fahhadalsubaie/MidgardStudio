using MidgardStudio.Core.Workspace;

namespace MidgardStudio.Tests;

public class WorkspaceConfigTests
{
    [Fact]
    public void Default_config_targets_repo_layout()
    {
        WorkspaceConfig cfg = WorkspaceConfigService.CreateDefault();

        Assert.Equal(ServerMode.Renewal, cfg.DefaultMode);
        Assert.Equal(1252, cfg.ClientCodepage);
        Assert.EndsWith(Path.Combine("server-db", "db"), cfg.Paths.ServerDbRoot);
        Assert.EndsWith("lua-files", cfg.Paths.LuaFilesRoot);
        Assert.EndsWith("SystemEN", cfg.Paths.SystemEnRoot);
    }

    [Fact]
    public void Default_paths_create_from_repo_root()
    {
        WorkspacePaths p = WorkspacePaths.CreateDefault(@"C:\repo");

        Assert.Equal(@"C:\repo\server-db\db", p.ServerDbRoot);
        Assert.Equal(@"C:\repo\lua-files", p.LuaFilesRoot);
        Assert.Equal(@"C:\repo\SystemEN", p.SystemEnRoot);
    }
}
