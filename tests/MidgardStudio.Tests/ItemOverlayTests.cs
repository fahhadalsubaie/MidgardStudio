using System.IO;
using System.Linq;
using MidgardStudio.Core.Model;
using MidgardStudio.Core.Overlay;
using MidgardStudio.Core.Schemas;
using MidgardStudio.Core.Serialization;
using MidgardStudio.Core.Workspace;

namespace MidgardStudio.Tests;

public class ItemOverlayTests
{
    private static WorkspacePaths Paths() =>
        WorkspacePaths.CreateDefault(WorkspaceConfigService.DefaultRepoRoot);

    private static bool RepoPresent(WorkspacePaths p) => Directory.Exists(p.ServerDbRoot);

    [Fact]
    public void Loads_renewal_item_overlay_from_real_files()
    {
        var paths = Paths();
        if (!RepoPresent(paths)) return;

        var overlay = new WorkspaceLoader().LoadOverlay(ItemDbSchema.Instance, paths, ServerMode.Renewal);

        Assert.True(overlay.BaseCount > 1000, $"expected many base items, got {overlay.BaseCount}");
        Assert.Equal(RecordOrigin.Base, overlay.Effective().First().Origin);
    }

    [Fact]
    public void Override_clones_base_into_import_and_marks_overridden()
    {
        var paths = Paths();
        if (!RepoPresent(paths)) return;

        var overlay = new WorkspaceLoader().LoadOverlay(ItemDbSchema.Instance, paths, ServerMode.Renewal);
        var baseKey = overlay.Effective().First(r => r.Origin == RecordOrigin.Base).Key;

        var editable = overlay.BeginOverride(baseKey);
        editable.Set("Buy", 12345);

        Assert.Equal(RecordOrigin.Overridden, overlay.OriginOf(baseKey));
        Assert.Equal(12345, overlay.GetEffective(baseKey)!.GetInt("Buy"));
        Assert.True(overlay.IsDirty);

        // reverting removes the import entry
        Assert.True(overlay.RevertToCore(baseKey));
        Assert.Equal(RecordOrigin.Base, overlay.OriginOf(baseKey));
    }

    [Fact]
    public void Add_custom_save_to_temp_then_reload_persists()
    {
        var paths = Paths();
        if (!RepoPresent(paths)) return;

        var schema = ItemDbSchema.Instance;
        var overlay = new WorkspaceLoader().LoadOverlay(schema, paths, ServerMode.Renewal);

        var custom = new DbRecord(schema);
        custom.SetRaw("Id", 99001);
        custom.SetRaw("AegisName", "Test_Blade");
        custom.SetRaw("Name", "Test Blade");
        custom.SetRaw("Type", "Weapon");
        custom.SetRaw("SubType", "1hSword");
        custom.SetRaw("Attack", 200);
        custom.SetRaw("WeaponLevel", 4);
        custom.SetRaw("Locations", new HashSet<string>(StringComparer.Ordinal) { "Right_Hand" });
        custom.SetRaw("Script", new ScriptValue("bonus bStr,10;"));
        overlay.AddCustom(custom);

        Assert.Equal(RecordOrigin.NewCustom, overlay.OriginOf(RecordKey.Of(99001L)));

        var temp = Path.Combine(Path.GetTempPath(), "midgard_" + Guid.NewGuid().ToString("N") + ".yml");
        try
        {
            overlay.Save(temp);
            DbFile reloaded = new YamlDbReader().ReadFile(temp, schema);

            // the saved import file contains at least our custom (plus any existing import entries)
            var found = reloaded.Records.SingleOrDefault(r => r.GetInt("Id") == 99001);
            Assert.NotNull(found);
            Assert.Equal("Test_Blade", found!.GetString("AegisName"));
            Assert.Equal(200, found.GetInt("Attack"));
            Assert.Contains("bonus bStr,10;", found.GetScript("Script")!.Text);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
