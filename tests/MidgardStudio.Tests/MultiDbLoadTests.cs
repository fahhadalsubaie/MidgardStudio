using System.IO;
using System.Linq;
using MidgardStudio.Core.Schema;
using MidgardStudio.Core.Schemas;
using MidgardStudio.Core.Serialization;
using MidgardStudio.Core.Workspace;

namespace MidgardStudio.Tests;

public class MultiDbLoadTests
{
    [Fact]
    public void All_databases_load_from_real_files_and_round_trip_a_record()
    {
        var paths = WorkspacePaths.CreateDefault(WorkspaceConfigService.DefaultRepoRoot);
        if (!Directory.Exists(paths.ServerDbRoot)) return; // skip when repo data is absent

        DbSchema[] schemas =
        {
            MobDbSchema.Instance,
            PetDbSchema.Instance,
            ItemGroupSchema.Instance,
            ItemComboSchema.Instance,
            SkillDbSchema.Instance,
            AchievementDbSchema.Instance,
            AbraDbSchema.Instance,
            MobSummonSchema.Instance,
        };

        var loader = new WorkspaceLoader();
        var writer = new YamlDbWriter();
        var reader = new YamlDbReader();

        foreach (var schema in schemas)
        {
            var overlay = loader.LoadOverlay(schema, paths, ServerMode.Renewal);
            Assert.True(overlay.BaseCount > 0, $"{schema.Id} loaded no base records");

            var first = overlay.Effective().First();
            var file = new DbFile { HeaderType = schema.HeaderType, HeaderVersion = schema.HeaderVersion };
            file.Records.Add(first);

            string yaml = writer.WriteToString(schema, file);
            var back = reader.Read(yaml, schema);

            Assert.Single(back.Records);
            Assert.Equal(first.Key.ToString(), back.Records[0].Key.ToString());
        }
    }
}
