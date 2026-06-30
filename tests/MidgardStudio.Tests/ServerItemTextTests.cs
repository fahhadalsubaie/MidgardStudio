using System;
using System.Linq;
using MidgardStudio.Core.Lua;
using MidgardStudio.Core.Model;
using MidgardStudio.Core.Schemas;
using MidgardStudio.Core.Serialization;
using Xunit;

namespace MidgardStudio.Tests;

public class ServerItemTextTests
{
    [Fact]
    public void DisplayName_PrefersServerName()
    {
        var r = new DbRecord(ItemDbSchema.Instance);
        r.SetRaw("Id", 40010);
        r.SetRaw("Name", "Custom Blade");
        Assert.Equal("Custom Blade", ServerItemText.DisplayName(r));
    }

    [Fact]
    public void DisplayName_FallsBackToHumanisedAegis()
    {
        var r = new DbRecord(ItemDbSchema.Instance);
        r.SetRaw("Id", 40011);
        r.SetRaw("AegisName", "Cool_Hat");
        Assert.Equal("Cool Hat", ServerItemText.DisplayName(r));
    }

    [Fact]
    public void DisplayName_FallsBackToIdWhenNameless()
    {
        var r = new DbRecord(ItemDbSchema.Instance);
        r.SetRaw("Id", 40012);
        Assert.Equal("Item 40012", ServerItemText.DisplayName(r));
    }
}

public class ItemScriptParserTests
{
    [Fact]
    public void Heal_HpRand_IsParsed()
    {
        var ins = ItemScriptParser.Parse("itemheal rand(45,65),0;");
        Assert.Single(ins.Heals);
        Assert.Equal("45 - 65", ins.Heals[0].Amount);
        Assert.Equal("HP", ins.Heals[0].Unit);
        Assert.False(ins.HasComplex);
    }

    [Fact]
    public void Heal_SpRand_IsParsed()
    {
        var ins = ItemScriptParser.Parse("itemheal 0,rand(40,60);");
        Assert.Single(ins.Heals);
        Assert.Equal("40 - 60", ins.Heals[0].Amount);
        Assert.Equal("SP", ins.Heals[0].Unit);
    }

    [Fact]
    public void PercentHeal_BothSides_ProducesTwoLines()
    {
        var ins = ItemScriptParser.Parse("percentheal 10,5;");
        Assert.Collection(ins.Heals,
            h => { Assert.Equal("10%", h.Amount); Assert.Equal("HP", h.Unit); },
            h => { Assert.Equal("5%", h.Amount); Assert.Equal("SP", h.Unit); });
    }

    [Fact]
    public void Element_FromAtkEle_IsDetected()
    {
        Assert.Equal("Fire", ItemScriptParser.Parse("bonus bAtkEle,Ele_Fire;").Element);
        Assert.Equal("Water", ItemScriptParser.Parse("bonus bAtkEle,Ele_Water;").Element);
    }

    [Fact]
    public void StatBonuses_ParsedInOrder()
    {
        var ins = ItemScriptParser.Parse("bonus bStr,2;\nbonus bDef,1;\nbonus bMaxHP,500;");
        Assert.Equal(new[] { "STR +2", "DEF +1", "Max HP +500" }, ins.Bonuses);
    }

    [Fact]
    public void RateBonus_AppendsPercent()
    {
        Assert.Equal(new[] { "Max HP +20%" }, ItemScriptParser.Parse("bonus bMaxHPrate,20;").Bonuses);
        Assert.Equal(new[] { "ASPD +10%" }, ItemScriptParser.Parse("bonus bAspdRate,10;").Bonuses);
    }

    [Fact]
    public void RaceDamage_IsReadable()
    {
        Assert.Equal(new[] { "Damage vs Plant: +25%" }, ItemScriptParser.Parse("bonus2 bAddRace,RC_Plant,25;").Bonuses);
        Assert.Equal(new[] { "Damage vs Demi-Human: +20%" }, ItemScriptParser.Parse("bonus2 bAddRace,RC_DemiHuman,20;").Bonuses);
    }

    [Fact]
    public void Cure_FromScEnd_IsCollected()
    {
        var ins = ItemScriptParser.Parse("sc_end SC_POISON; sc_end SC_SILENCE;");
        Assert.Equal(new[] { "Poison", "Silence" }, ins.Cures);
    }

    [Fact]
    public void Complex_Script_IsFlagged_NotGuessed()
    {
        var ins = ItemScriptParser.Parse("autobonus \"{ bonus bStr,1; }\",10,5000,BF_WEAPON;");
        Assert.True(ins.HasComplex);
        Assert.Empty(ins.Bonuses);
    }

    [Fact]
    public void ItemSkill_ResolvesSkillName()
    {
        var ins = ItemScriptParser.Parse("itemskill \"AL_TELEPORT\",1;", a => a == "AL_TELEPORT" ? "Teleport" : null);
        Assert.Contains("Casts Level 1 Teleport.", ins.Bonuses);
        Assert.False(ins.HasComplex); // itemskill is now understood, not flagged complex
    }

    [Fact]
    public void AutoSpell_ResolvesSkillName()
    {
        var ins = ItemScriptParser.Parse("bonus3 bAutoSpell,\"MG_FIREBOLT\",3,50;", _ => "Fire Bolt");
        Assert.Contains("Has a chance to autocast Level 3 Fire Bolt.", ins.Bonuses);
    }

    [Fact]
    public void Skill_FallsBackToHumanisedAegis_WhenUnresolved()
    {
        var ins = ItemScriptParser.Parse("itemskill \"AL_TELEPORT\",1;"); // no resolver
        Assert.Contains("Casts Level 1 Teleport.", ins.Bonuses);          // AL_TELEPORT → Teleport
    }
}

public class ItemAutocompleteTests
{
    private static readonly AutocompleteConfig Cfg = new();

    private static DbRecord Item(int id, string type, Action<DbRecord> setup)
    {
        var r = new DbRecord(ItemDbSchema.Instance);
        r.SetRaw("Id", id);
        r.SetRaw("Type", type);
        setup(r);
        return r;
    }

    [Fact]
    public void RedPotion_ShowsAuthenticHealLine()
    {
        var r = Item(501, "Healing", x =>
        {
            x.SetRaw("Name", "Red Potion");
            x.SetRaw("Weight", 70);
            x.SetRaw("Script", new ScriptValue("itemheal rand(45,65),0;"));
        });

        var lines = new ItemAutocomplete(Cfg).IdentifiedDescription(r);
        Assert.Contains("Heal:^009900 45 - 65^000000 HP", lines);
        Assert.Contains("Weight:^009900 7^000000", lines);          // weight 70 → 7.0
        Assert.Contains("Class:^0000FF Restorative^000000", lines);  // usable class color
    }

    [Fact]
    public void ElementalWeapon_ShowsClassAttackElementLevel()
    {
        var r = Item(1133, "Weapon", x =>
        {
            x.SetRaw("Name", "Fire Brand");
            x.SetRaw("SubType", "1hSword");
            x.SetRaw("Attack", 100);
            x.SetRaw("WeaponLevel", 4);
            x.SetRaw("Weight", 500);
            x.SetRaw("Locations", new HashSet<string>(StringComparer.Ordinal) { "Right_Hand" });
            x.SetRaw("Script", new ScriptValue("bonus bAtkEle,Ele_Fire;"));
        });

        var lines = new ItemAutocomplete(Cfg).IdentifiedDescription(r);
        Assert.Contains("Class:^6666CC One-Handed Sword^000000", lines);
        Assert.Contains("Attack:^CC0000 100^000000", lines);
        Assert.Contains("Element:^FF0000 Fire^000000", lines);
        Assert.Contains("Weapon Level:^009900 4^000000", lines);
    }

    [Fact]
    public void Card_ShowsBonusesClassAndCompoundTarget()
    {
        var r = Item(4001, "Card", x =>
        {
            x.SetRaw("Name", "Poring Card");
            x.SetRaw("Weight", 10);
            x.SetRaw("Locations", new HashSet<string>(StringComparer.Ordinal) { "Armor" });
            x.SetRaw("Script", new ScriptValue("bonus bLuk,2;\nbonus bFlee2,1;"));
        });

        var lines = new ItemAutocomplete(Cfg).IdentifiedDescription(r);
        Assert.Contains("LUK +2", lines);
        Assert.Contains("Perfect Dodge +1", lines);
        Assert.Contains("Class:^6666CC Card^000000", lines);
        Assert.Contains("Compound on:^00CC33 Armor^000000", lines);
        Assert.Contains("Weight:^009900 1^000000", lines);
    }

    [Fact]
    public void UseColorsOff_ProducesPlainLines()
    {
        var cfg = new AutocompleteConfig { UseColors = false };
        var r = Item(501, "Healing", x =>
        {
            x.SetRaw("Weight", 70);
            x.SetRaw("Script", new ScriptValue("itemheal rand(45,65),0;"));
        });

        var lines = new ItemAutocomplete(cfg).IdentifiedDescription(r);
        Assert.Contains("Heal: 45 - 65 HP", lines);
        Assert.DoesNotContain(lines, l => l.Contains('^'));
    }

    [Fact]
    public void LabelOverride_RenamesTheField()
    {
        var cfg = new AutocompleteConfig();
        cfg.Labels["Weight"] = "Item Weight";
        var r = Item(501, "Healing", x => x.SetRaw("Weight", 70));

        var lines = new ItemAutocomplete(cfg).IdentifiedDescription(r);
        Assert.Contains("Item Weight:^009900 7^000000", lines);
    }

    [Fact]
    public void ToggleOff_OmitsTheSection()
    {
        var cfg = new AutocompleteConfig { IncludeWeight = false };
        var r = Item(501, "Healing", x => x.SetRaw("Weight", 70));

        var lines = new ItemAutocomplete(cfg).IdentifiedDescription(r);
        Assert.DoesNotContain(lines, l => l.Contains("Weight", StringComparison.Ordinal));
    }

    [Fact]
    public void AlwaysShowWeight_EmitsZeroWeight_WhenOff_OmitsIt()
    {
        var r = Item(909, "Etc", x => x.SetRaw("Name", "Jellopy")); // no Weight set → 0

        // Default: a 0 weight is omitted (preserves existing behaviour).
        Assert.DoesNotContain(new ItemAutocomplete(Cfg).IdentifiedDescription(r), l => l.Contains("Weight", StringComparison.Ordinal));

        // The Item Forge forces it on, so a forged item always shows the line.
        var forge = new AutocompleteConfig { AlwaysShowWeight = true };
        Assert.Contains("Weight:^009900 0^000000", new ItemAutocomplete(forge).IdentifiedDescription(r));
    }

    [Fact]
    public void ClassLabel_MergesTier_AndJob()
    {
        Assert.Equal("Transcendent Archer", ServerItemText.ClassLabel(Tokens("Upper"), Tokens("Archer")));
        Assert.Equal("Archer", ServerItemText.ClassLabel(Tokens("All"), Tokens("Archer")));   // "All" tier dropped
        Assert.Equal("Transcendent", ServerItemText.ClassLabel(Tokens("Upper"), Tokens("All")));
        Assert.Null(ServerItemText.ClassLabel(Tokens("All"), Tokens("All")));                  // unrestricted → no line
        Assert.Null(ServerItemText.ClassLabel(null, null));

        static HashSet<string> Tokens(params string[] t) => new(t, StringComparer.Ordinal);
    }

    [Fact]
    public void Description_JobLine_OnlyWhenRestricted_AndReadsAsClassPlusJob()
    {
        var open = Item(1101, "Weapon", x => { x.SetRaw("Attack", 25); x.SetRaw("SubType", "1hSword"); });
        Assert.DoesNotContain(new ItemAutocomplete(Cfg).IdentifiedDescription(open),
            l => l.Contains("Jobs", StringComparison.Ordinal) || l.Contains("Archer", StringComparison.Ordinal));

        var restricted = Item(1102, "Weapon", x =>
        {
            x.SetRaw("Attack", 25); x.SetRaw("SubType", "1hSword");
            x.SetRaw("Classes", new HashSet<string>(StringComparer.Ordinal) { "Upper" });
            x.SetRaw("Jobs", new HashSet<string>(StringComparer.Ordinal) { "Archer" });
        });
        Assert.Contains("Jobs:^6666CC Transcendent Archer^000000", new ItemAutocomplete(Cfg).IdentifiedDescription(restricted));
    }

    [Fact]
    public void Clone_IsIndependent_OfTheSourceLabels()
    {
        var src = new AutocompleteConfig();
        var copy = src.Clone();
        copy.Labels["Class"] = "Type";

        Assert.Equal("Type", copy.Label("Class", "Class"));
        Assert.Equal("Class", src.Label("Class", "Class")); // mutating the clone never touches the original
    }

    [Fact]
    public void Usable_WithItemSkill_ShowsCastLine()
    {
        var r = Item(12887, "Usable", x =>
        {
            x.SetRaw("Name", "Infinite Flywing");
            x.SetRaw("Script", new ScriptValue("itemskill \"AL_TELEPORT\",1;"));
        });

        var lines = new ItemAutocomplete(Cfg, a => a == "AL_TELEPORT" ? "Teleport" : null).IdentifiedDescription(r);
        Assert.Contains("Casts Level 1 Teleport.", lines);
    }

    [Fact]
    public void Unrefineable_OnlyForEquipment()
    {
        var potion = Item(501, "Healing", x => x.SetRaw("Weight", 70));
        Assert.DoesNotContain(new ItemAutocomplete(Cfg).IdentifiedDescription(potion), l => l.Contains("Unrefineable"));

        var weapon = Item(1101, "Weapon", x => { x.SetRaw("Attack", 25); x.SetRaw("SubType", "1hSword"); });
        Assert.Contains(new ItemAutocomplete(Cfg).IdentifiedDescription(weapon), l => l.Contains("Unrefineable"));
    }

    [Fact]
    public void BuildDescription_PreservesLeadingFlavor_RegeneratesProperties()
    {
        var weapon = Item(1101, "Weapon", x => { x.SetRaw("Name", "Blade"); x.SetRaw("Attack", 25); x.SetRaw("SubType", "1hSword"); });
        var existing = new[]
        {
            "A token of triumph,",
            "earned by felling MVP bosses.",
            "Class:^6666CC Old^000000",
            "Weight:^009900 99^000000",
        };

        var built = new ItemAutocomplete(Cfg).BuildDescription(weapon, existing);

        Assert.Equal("A token of triumph,", built[0]);                       // flavor kept
        Assert.Equal("earned by felling MVP bosses.", built[1]);
        Assert.Contains("Class:^6666CC One-Handed Sword^000000", built);     // properties regenerated
        Assert.DoesNotContain(built, l => l.Contains("Old", StringComparison.Ordinal));
        Assert.DoesNotContain(built, l => l.Contains("99", StringComparison.Ordinal));
    }
}

public class YamlRecordCopyTests
{
    private static DbRecord Item(int id, string name)
    {
        var r = new DbRecord(ItemDbSchema.Instance);
        r.SetRaw("Id", id);
        r.SetRaw("AegisName", name);
        r.SetRaw("Name", name);
        r.SetRaw("Type", "Etc");
        return r;
    }

    [Fact]
    public void WriteToString_MultipleRecords_EmitsHeaderAndAllEntries()
    {
        var schema = ItemDbSchema.Instance;
        string yaml = new YamlDbWriter().WriteToString(schema, new[] { Item(40020, "A"), Item(40021, "B") });

        Assert.Contains("Header:", yaml);
        Assert.Contains("Type: ITEM_DB", yaml);
        Assert.Contains("Id: 40020", yaml);
        Assert.Contains("Id: 40021", yaml);

        DbFile back = new YamlDbReader().Read(yaml, schema);
        Assert.Equal(2, back.Records.Count);
    }

    [Fact]
    public void WriteRecord_SingleRecord_EmitsBareMappingWithoutHeader()
    {
        string yaml = new YamlDbWriter().WriteRecord(Item(40022, "Solo"));

        Assert.Contains("Id: 40022", yaml);
        Assert.Contains("AegisName: Solo", yaml);
        Assert.DoesNotContain("Header:", yaml);
        Assert.DoesNotContain("Body:", yaml);
    }
}
