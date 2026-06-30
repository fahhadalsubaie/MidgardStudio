using System.IO;
using MidgardStudio.Core.Model;
using MidgardStudio.Core.Schemas;
using MidgardStudio.Core.Serialization;
using MidgardStudio.Core.Workspace;

namespace MidgardStudio.Tests;

public class ItemYamlRoundTripTests
{
    private static DbRecord MakeSword()
    {
        var r = new DbRecord(ItemDbSchema.Instance);
        r.SetRaw("Id", 40000);
        r.SetRaw("AegisName", "Devastator");
        r.SetRaw("Name", "Devastator");
        r.SetRaw("Type", "Weapon");
        r.SetRaw("SubType", "1hSword");
        r.SetRaw("Buy", 20);
        r.SetRaw("Weight", 800);
        r.SetRaw("Attack", 325);
        r.SetRaw("Range", 1);
        r.SetRaw("Slots", 0);        // default -> must be omitted
        r.SetRaw("Gender", "Both");  // default -> must be omitted
        r.SetRaw("WeaponLevel", 4);
        r.SetRaw("Refineable", true);
        r.SetRaw("Jobs", new HashSet<string>(StringComparer.Ordinal) { "Swordman", "Knight" });
        r.SetRaw("Locations", new HashSet<string>(StringComparer.Ordinal) { "Right_Hand" });
        r.SetRaw("Script", new ScriptValue("bonus bStr,5;\nbonus bAtkRate,10;"));

        var flags = new DbRecord(ItemDbSchema.Flags);
        flags.SetRaw("BindOnEquip", true);
        r.SetRaw("Flags", flags);
        return r;
    }

    [Fact]
    public void Write_then_read_then_write_is_idempotent()
    {
        var schema = ItemDbSchema.Instance;
        var file = new DbFile { HeaderType = "ITEM_DB", HeaderVersion = 3 };
        file.Records.Add(MakeSword());

        var writer = new YamlDbWriter();
        var reader = new YamlDbReader();

        string first = writer.WriteToString(schema, file);
        DbFile roundTrip = reader.Read(first, schema);
        string second = writer.WriteToString(schema, roundTrip);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Defaults_are_omitted_and_special_kinds_emit_correctly()
    {
        var schema = ItemDbSchema.Instance;
        var file = new DbFile { HeaderType = "ITEM_DB", HeaderVersion = 3 };
        file.Records.Add(MakeSword());

        string yaml = new YamlDbWriter().WriteToString(schema, file);

        Assert.Contains("Id: 40000", yaml);
        Assert.Contains("AegisName: Devastator", yaml);
        Assert.DoesNotContain("Slots:", yaml);    // default 0 omitted
        Assert.DoesNotContain("Gender:", yaml);    // default "Both" omitted
        Assert.DoesNotContain("Defense:", yaml);   // absent/default omitted
        Assert.Contains("Swordman: true", yaml);    // bool-map true keys only
        Assert.Contains("Right_Hand: true", yaml);
        Assert.Contains("BindOnEquip: true", yaml); // nested object
        Assert.Contains("Script: |", yaml);          // literal block scalar
    }

    [Fact]
    public void Read_preserves_values()
    {
        var schema = ItemDbSchema.Instance;
        var file = new DbFile { HeaderType = "ITEM_DB", HeaderVersion = 3 };
        file.Records.Add(MakeSword());

        string yaml = new YamlDbWriter().WriteToString(schema, file);
        DbRecord r = new YamlDbReader().Read(yaml, schema).Records.Single();

        Assert.Equal(40000, r.GetInt("Id"));
        Assert.Equal("Weapon", r.GetString("Type"));
        Assert.Equal(4, r.GetInt("WeaponLevel"));
        Assert.True(r.GetSet("Jobs")!.SetEquals(new[] { "Swordman", "Knight" }));
        Assert.Contains("bonus bStr,5;", r.GetScript("Script")!.Text);
        Assert.True(r.GetObject("Flags")!.GetBool("BindOnEquip"));
    }

    [Fact]
    public void BoolMap_exclusions_emit_all_except_and_roundtrip()
    {
        var schema = ItemDbSchema.Instance;
        var r = new DbRecord(schema);
        r.SetRaw("Id", 40100);
        r.SetRaw("AegisName", "AllButAcolyte");
        r.SetRaw("Name", "AllButAcolyte");
        r.SetRaw("Type", "Weapon");
        r.SetRaw("SubType", "1hSword");
        r.SetRaw("Locations", new HashSet<string>(StringComparer.Ordinal) { "Right_Hand" });
        var jobs = new BoolMap(new[] { "All" });
        jobs.Excluded.Add("Acolyte"); // All jobs EXCEPT Acolyte
        r.SetRaw("Jobs", jobs);

        var file = new DbFile { HeaderType = "ITEM_DB", HeaderVersion = 3 };
        file.Records.Add(r);

        string yaml = new YamlDbWriter().WriteToString(schema, file);
        Assert.Contains("All: true", yaml);
        Assert.Contains("Acolyte: false", yaml);
        Assert.True(yaml.IndexOf("All: true", StringComparison.Ordinal) < yaml.IndexOf("Acolyte: false", StringComparison.Ordinal),
            "All must be emitted before the exception (rAthena applies All first).");

        // Read back: the exclusion is preserved (not dropped), and isn't reported as an enabled job.
        DbRecord back = new YamlDbReader().Read(yaml, schema).Records.Single();
        var bm = back.GetBoolMap("Jobs")!;
        Assert.Contains("All", bm);
        Assert.Contains("Acolyte", bm.Excluded);
        Assert.DoesNotContain("Acolyte", bm);

        // Idempotent write.
        var file2 = new DbFile { HeaderType = "ITEM_DB", HeaderVersion = 3 };
        file2.Records.Add(back);
        Assert.Equal(yaml, new YamlDbWriter().WriteToString(schema, file2));
    }

    [Fact]
    public void BoolMap_clone_preserves_and_isolates_exclusions()
    {
        var jobs = new BoolMap(new[] { "All" });
        jobs.Excluded.Add("Acolyte");
        var r = new DbRecord(ItemDbSchema.Instance);
        r.SetRaw("Jobs", jobs);

        var bm = r.DeepClone().GetBoolMap("Jobs")!;
        Assert.Contains("All", bm);
        Assert.Contains("Acolyte", bm.Excluded);

        bm.Excluded.Add("Archer");                 // mutating the clone must not touch the source
        Assert.DoesNotContain("Archer", jobs.Excluded);
    }

    [Fact]
    public void Reads_real_item_db_equip()
    {
        string path = Path.Combine(WorkspaceConfigService.DefaultRepoRoot, "server-db", "db", "re", "item_db_equip.yml");
        if (!File.Exists(path))
            return; // environment without the repo data — skip

        DbFile file = new YamlDbReader().ReadFile(path, ItemDbSchema.Instance);

        Assert.Equal("ITEM_DB", file.HeaderType);
        Assert.True(file.Records.Count > 100);

        DbRecord first = file.Records[0];
        Assert.Equal(1100, first.GetInt("Id"));
        Assert.False(string.IsNullOrWhiteSpace(first.GetString("AegisName")));

        Assert.Contains(file.Records, r => r.GetScript("Script") is { IsEmpty: false });
        Assert.Contains(file.Records, r => r.GetSet("Jobs") is { Count: > 0 });
        Assert.Contains(file.Records, r => r.GetObject("Trade") is not null);
    }
}
