using System.Linq;
using MidgardStudio.Core.Model;
using MidgardStudio.Core.Schemas;
using MidgardStudio.Core.Serialization;

namespace MidgardStudio.Tests;

public class SkillSchemaTests
{
    private const string Yaml =
        """
        Header:
          Type: SKILL_DB
          Version: 4
        Body:
          - Id: 8004
            Name: TEST_SKILL
            Description: Test skill
            MaxLevel: 3
            Type: Magic
            Range: 9
            CastTime:
              - Level: 1
                Time: 1000
              - Level: 2
                Time: 2000
            Requires:
              SpCost: 25
              Weapon:
                Staff: true
              ItemCost:
                - Item: Red_Gemstone
                  Amount: 1
            Unit:
              Id: NoteDetection
              Range: 3
              Target: Enemy
        """;

    [Fact]
    public void Skill_dual_fields_and_nested_objects_round_trip()
    {
        var schema = SkillDbSchema.Instance;
        var reader = new YamlDbReader();
        var file = reader.Read(Yaml, schema);
        var rec = file.Records.Single();

        // Scalar dual field
        Assert.Equal(9, rec.GetLevel("Range")!.Scalar);

        // Per-level dual field
        var cast = rec.GetLevel("CastTime")!;
        Assert.Null(cast.Scalar);
        Assert.Equal(2, cast.Levels.Count);
        Assert.Equal(new LevelEntry(2, 2000), cast.Levels[1]);

        // Nested Requires (LevelInt + map + object-list)
        var req = rec.GetObject("Requires")!;
        Assert.Equal(25, req.GetLevel("SpCost")!.Scalar);
        Assert.Contains("Staff", req.GetSet("Weapon")!);
        Assert.Equal("Red_Gemstone", req.GetList("ItemCost")!.Single().GetString("Item"));

        // Nested Unit (string id + dual Range + enum)
        var unit = rec.GetObject("Unit")!;
        Assert.Equal("NoteDetection", unit.GetString("Id"));
        Assert.Equal(3, unit.GetLevel("Range")!.Scalar);
        Assert.Equal("Enemy", unit.GetString("Target"));
    }

    [Fact]
    public void Skill_write_read_is_idempotent()
    {
        var schema = SkillDbSchema.Instance;
        var reader = new YamlDbReader();
        var writer = new YamlDbWriter();

        var file = reader.Read(Yaml, schema);
        string out1 = writer.WriteToString(schema, file);
        var file2 = reader.Read(out1, schema);
        string out2 = writer.WriteToString(schema, file2);

        Assert.Equal(out1, out2); // stable: reading our own output and rewriting yields identical text
    }

    [Fact]
    public void Per_level_array_on_a_scalar_field_is_preserved_verbatim()
    {
        // Element is modelled as a scalar enum, but rAthena allows a rare per-level array form.
        // The reader must preserve it (in Extras) so it round-trips rather than being lost.
        const string perLevelElement =
            """
            Header:
              Type: SKILL_DB
              Version: 4
            Body:
              - Id: 9001
                Name: ELEM_TEST
                Description: Test
                MaxLevel: 2
                Element:
                  - Level: 1
                    Element: Fire
                  - Level: 2
                    Element: Water
            """;

        var schema = SkillDbSchema.Instance;
        var reader = new YamlDbReader();
        var writer = new YamlDbWriter();

        var file = reader.Read(perLevelElement, schema);
        string output = writer.WriteToString(schema, file);

        Assert.Contains("Element", output);
        Assert.Contains("Fire", output);
        Assert.Contains("Water", output);
        // and stable on re-round-trip
        string output2 = writer.WriteToString(schema, reader.Read(output, schema));
        Assert.Equal(output, output2);
    }
}
