using System.Collections.Generic;
using System.IO;
using System.Linq;
using MidgardStudio.Core.Lua;
using MidgardStudio.Core.Validation.Validators;
using MidgardStudio.Core.Workspace;
using Xunit;

namespace MidgardStudio.Tests;

/// <summary>
/// Covers the Client Skills core: parsing the four skillinfoz tables, the expression-key scanner/splicer
/// (the new infra that addresses [SKID.X] keys), SKID allocation, lossless reformatting, and the
/// internal-consistency validator. Synthetic fixtures are deterministic; a few smoke tests run against
/// the real repo files and skip when absent.
/// </summary>
public class ClientSkillLuaTests
{
    private const string Info =
        "-- header comment with a } brace and \"quote\"\n" +
        "SKILL_INFO_LIST = {\n" +
        "\t[SKID.NV_BASIC] = {\n" +
        "\t\t\"NV_BASIC\",\n" +
        "\t\tSkillName = \"Basic Skill\",\n" +
        "\t\tMaxLv = 9,\n" +
        "\t\tAttackRange = { 1, 1, 1, 1, 1, 1, 1, 1, 1 }\n" +
        "\t},\n" +
        "\t[SKID.SM_SWORD] = {\n" +
        "\t\t\"SM_SWORD\",\n" +
        "\t\tSkillName = \"Sword Mastery\",\n" +
        "\t\tMaxLv = 10,\n" +
        "\t\tSpAmount = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }\n" +
        "\t}\n" +
        "}\n";

    // Regression: a skill with an empty descript block (HasDescript, but no lines) must report NO descript
    // content — null, the same as a skill with no descript at all. The app's dirty tracking keys the load
    // baseline, the diff, and the save off this one definition; when an empty block produced a non-null
    // baseline but a null diff, the skill latched permanently dirty and the Save button stayed lit after an
    // unrelated quick-fix (e.g. "set MaxLv to 1") was applied and then fully undone.
    [Fact]
    public void EmptyDescriptBlock_yields_null_descript_content()
    {
        var empty = new ClientSkill { Constant = "MB_MENTAL", Id = 1, HasInfo = true, MaxLv = 1, HasDescript = true };
        Assert.Empty(empty.Description);
        Assert.Null(ClientSkillContent.Descript(empty));     // nothing to write -> null on every side
        Assert.NotNull(ClientSkillContent.Info(empty));      // info still has content

        var withText = new ClientSkill { Constant = "MB_MENTAL", Id = 1, HasDescript = true, Description = { "A line." } };
        Assert.NotNull(ClientSkillContent.Descript(withText));
    }

    [Fact]
    public void Parses_info_entries()
    {
        var skills = new Dictionary<string, ClientSkill>();
        ClientSkillReader.ReadInfo(Info, skills);

        Assert.Equal(2, skills.Count);
        var basic = skills["NV_BASIC"];
        Assert.True(basic.HasInfo);
        Assert.Equal("NV_BASIC", basic.Aegis);
        Assert.Equal("Basic Skill", basic.SkillName);
        Assert.Equal(9, basic.MaxLv);
        Assert.Equal(9, basic.AttackRange.Count);
        Assert.Equal(10, skills["SM_SWORD"].SpAmount.Count);
    }

    [Fact]
    public void Splice_replaces_existing_entry_and_preserves_the_rest()
    {
        var skills = new Dictionary<string, ClientSkill>();
        ClientSkillReader.ReadInfo(Info, skills);
        var sword = skills["SM_SWORD"];
        sword.SkillName = "Two-Hand Sword Mastery";

        string result = ExprKeyTableSplicer.Splice(Info, "SKILL_INFO_LIST",
            new[] { ($"SKID.{sword.Constant}", ClientSkillWriter.FormatInfo(sword)) });

        Assert.Contains("Two-Hand Sword Mastery", result);
        Assert.Contains("-- header comment", result);         // header preserved
        Assert.Contains("SkillName = \"Basic Skill\"", result); // untouched entry preserved
        Assert.DoesNotContain("SkillName = \"Sword Mastery\"", result); // old field value replaced

        // Re-parse: still two well-formed entries.
        var again = new Dictionary<string, ClientSkill>();
        ClientSkillReader.ReadInfo(result, again);
        Assert.Equal(2, again.Count);
        Assert.Equal("Two-Hand Sword Mastery", again["SM_SWORD"].SkillName);
        Assert.Equal("Basic Skill", again["NV_BASIC"].SkillName);
    }

    [Fact]
    public void Splice_replaces_multiple_entries_in_one_pass()
    {
        var skills = new Dictionary<string, ClientSkill>();
        ClientSkillReader.ReadInfo(Info, skills);
        skills["NV_BASIC"].SkillName = "First Edit";
        skills["SM_SWORD"].SkillName = "Second Edit";

        string result = ExprKeyTableSplicer.Splice(Info, "SKILL_INFO_LIST", new[]
        {
            ("SKID.SM_SWORD", ClientSkillWriter.FormatInfo(skills["SM_SWORD"])), // pass out of order on purpose
            ("SKID.NV_BASIC", ClientSkillWriter.FormatInfo(skills["NV_BASIC"])),
        });

        var again = new Dictionary<string, ClientSkill>();
        ClientSkillReader.ReadInfo(result, again);
        Assert.Equal(2, again.Count);
        Assert.Equal("First Edit", again["NV_BASIC"].SkillName);
        Assert.Equal("Second Edit", again["SM_SWORD"].SkillName);
        Assert.DoesNotContain("Basic Skill", result);
        Assert.DoesNotContain("Sword Mastery", result);
    }

    [Fact]
    public void Splice_inserts_new_entry_before_table_close()
    {
        var brand = new ClientSkill { Constant = "MY_CUSTOM", Aegis = "MY_CUSTOM", SkillName = "My Custom", MaxLv = 3, HasInfo = true };
        string result = ExprKeyTableSplicer.Splice(Info, "SKILL_INFO_LIST",
            new[] { ($"SKID.{brand.Constant}", ClientSkillWriter.FormatInfo(brand)) });

        var again = new Dictionary<string, ClientSkill>();
        ClientSkillReader.ReadInfo(result, again);
        Assert.Equal(3, again.Count);
        Assert.Equal("My Custom", again["MY_CUSTOM"].SkillName);
        Assert.True(again.ContainsKey("NV_BASIC") && again.ContainsKey("SM_SWORD"));
    }

    [Fact]
    public void ScanExprKeyTables_ignores_braces_in_strings_and_comments()
    {
        string s =
            "T = {\n" +
            "\t[SKID.A] = { SkillName = \"has } brace\", MaxLv = 1 }, -- trailing } comment\n" +
            "\t[SKID.B] = { MaxLv = 2 }\n" +
            "}\n";
        int open = LuaScan.FindTableOpen(s, "T");
        var (blocks, close) = LuaScan.ScanExprKeyTables(s, open);

        Assert.True(close > 0);
        Assert.Equal(2, blocks.Count);
        Assert.True(blocks.ContainsKey("SKID.A"));
        Assert.True(blocks.ContainsKey("SKID.B"));
    }

    [Fact]
    public void Splice_matches_keys_exactly_no_prefix_collision()
    {
        string s =
            "T = {\n" +
            "\t[SKID.SU_LOPE] = { SkillName = \"Lope\", MaxLv = 1 },\n" +
            "\t[SKID.SU_LOPED] = { SkillName = \"Loped\", MaxLv = 1 }\n" +
            "}\n";
        var edit = new ClientSkill { Constant = "SU_LOPE", Aegis = "SU_LOPE", SkillName = "EDITED", MaxLv = 1, HasInfo = true };
        string result = ExprKeyTableSplicer.Splice(s, "T",
            new[] { ("SKID.SU_LOPE", ClientSkillWriter.FormatInfo(edit)) });

        Assert.Contains("EDITED", result);
        Assert.Contains("\"Loped\"", result);   // the longer-named sibling is untouched
    }

    [Fact]
    public void Splice_throws_on_unclosed_table()
    {
        string s = "SKILL_INFO_LIST = {\n\t[SKID.A] = { MaxLv = 1 },\n"; // no closing brace
        var edit = new ClientSkill { Constant = "A", Aegis = "A", SkillName = "x", MaxLv = 1, HasInfo = true };
        Assert.Throws<InvalidDataException>(() =>
            ExprKeyTableSplicer.Splice(s, "SKILL_INFO_LIST", new[] { ("SKID.A", ClientSkillWriter.FormatInfo(edit)) }));
    }

    [Fact]
    public void Skid_allocation_appends_and_skips_collisions()
    {
        string skid = "SKID = {\n\tNV_BASIC = 1,\n\tSM_SWORD = 2,\n}\n";
        var map = ClientSkillReader.ReadSkid(skid);
        Assert.Equal(2, map.Count);

        int next = AccessoryTables.NextFreeId(map);
        Assert.Equal(3, next);

        string updated = AccessoryTables.AppendConstant(skid, "SKID", "MY_CUSTOM", next);
        var map2 = ClientSkillReader.ReadSkid(updated);
        Assert.Equal(3, map2["MY_CUSTOM"]);
    }

    [Fact]
    public void NeedSkillList_round_trips()
    {
        string text =
            "SKILL_INFO_LIST = {\n" +
            "\t[SKID.X] = {\n" +
            "\t\t\"X\",\n" +
            "\t\tSkillName = \"X\",\n" +
            "\t\tMaxLv = 1,\n" +
            "\t\t_NeedSkillList = {\n" +
            "\t\t\t{ SKID.A, 3 },\n" +
            "\t\t\t{ SKID.B, 5 }\n" +
            "\t\t}\n" +
            "\t}\n" +
            "}\n";
        var skills = new Dictionary<string, ClientSkill>();
        ClientSkillReader.ReadInfo(text, skills);
        var x = skills["X"];
        Assert.Equal(2, x.NeedSkillList.Count);
        Assert.Equal(("A", 3), (x.NeedSkillList[0].Skid, x.NeedSkillList[0].Level));

        // format -> reparse -> identity
        string formatted = "T = {\n" + ClientSkillWriter.FormatInfo(x) + "}\n";
        var round = new Dictionary<string, ClientSkill>();
        ClientSkillReader.ReadInfo(formatted.Replace("T =", "SKILL_INFO_LIST ="), round);
        Assert.Equal(2, round["X"].NeedSkillList.Count);
        Assert.Equal(("B", 5), (round["X"].NeedSkillList[1].Skid, round["X"].NeedSkillList[1].Level));
    }

    [Fact]
    public void FormatInfo_is_idempotent()
    {
        var skills = new Dictionary<string, ClientSkill>();
        ClientSkillReader.ReadInfo(Info, skills);
        var sword = skills["SM_SWORD"];

        string once = ClientSkillWriter.FormatInfo(sword);
        var reparsed = new Dictionary<string, ClientSkill>();
        ClientSkillReader.ReadInfo("SKILL_INFO_LIST = {\n" + once + "}\n", reparsed);
        string twice = ClientSkillWriter.FormatInfo(reparsed["SM_SWORD"]);
        Assert.Equal(once, twice);
    }

    // Regression (HIGH): ~18% of official info entries have NO MaxLv field. The reader can't tell absent from
    // 0, so before the presence flag an edited entry gained a spurious `MaxLv = 0` line (a real change for the
    // client), and the validator flagged all of them as errors — nudging the user into that very edit.
    [Fact]
    public void MaxLv_absence_is_preserved_and_not_flagged()
    {
        string text =
            "SKILL_INFO_LIST = {\n" +
            "\t[SKID.HASLV] = {\n\t\t\"HASLV\",\n\t\tSkillName = \"Has\",\n\t\tMaxLv = 5\n\t},\n" +
            "\t[SKID.NOLV] = {\n\t\t\"NOLV\",\n\t\tSkillName = \"No\"\n\t}\n" +
            "}\n";
        var skills = new Dictionary<string, ClientSkill>();
        ClientSkillReader.ReadInfo(text, skills);

        Assert.True(skills["HASLV"].HasMaxLv);
        Assert.Equal(5, skills["HASLV"].MaxLv);
        Assert.False(skills["NOLV"].HasMaxLv);

        Assert.Contains("MaxLv = 5", ClientSkillWriter.FormatInfo(skills["HASLV"]));
        Assert.DoesNotContain("MaxLv", ClientSkillWriter.FormatInfo(skills["NOLV"])); // never re-emitted as 0

        var tables = new ClientSkillTables();
        tables.Skid["NOLV"] = 1;
        skills["NOLV"].Id = 1;
        tables.Skills["NOLV"] = skills["NOLV"];
        Assert.DoesNotContain(ClientSkillValidator.Validate(tables), i => i.RuleId == "CSKILL.MAXLV_INVALID");
    }

    // Regression (HIGH): the shipped skillid.lub ends its last entry with NO trailing comma. Appending a new
    // SKID constant must insert a separator, else two fields abut with only whitespace — a Lua syntax error
    // the strict in-client interpreter rejects (the app's own reader is separator-tolerant and hides it).
    [Fact]
    public void AppendConstant_adds_separator_when_last_entry_lacks_trailing_comma()
    {
        string noComma = "SKID = {\n\tA = 1,\n\tB = 2\n}\n";   // B has no trailing comma (like the real file)
        string updated = AccessoryTables.AppendConstant(noComma, "SKID", "C", 3);
        Assert.Contains("B = 2,", updated);           // separator inserted
        Assert.DoesNotContain("B = 2,,", updated);    // not doubled
        var map = ClientSkillReader.ReadSkid(updated);
        Assert.Equal(3, map.Count);
        Assert.Equal(3, map["C"]);

        string withComma = "SKID = {\n\tA = 1,\n\tB = 2,\n}\n";
        Assert.DoesNotContain("B = 2,,", AccessoryTables.AppendConstant(withComma, "SKID", "C", 3));
    }

    [Fact]
    public void Validator_flags_internal_inconsistencies()
    {
        var tables = new ClientSkillTables();
        tables.Skid["GOOD"] = 1; // GOOD defined; BADKEY is not
        tables.Skills["GOOD"] = new ClientSkill
        {
            Constant = "GOOD", Id = 1, HasInfo = true, Aegis = "WRONG_NAME", MaxLv = 0, HasMaxLv = true,
            SpAmount = new List<int> { 1, 2 }, // length 2 < MaxLv would need MaxLv>2; MaxLv=0 so MAXLV rule fires
            NeedSkillList = new List<SkillPrereq> { new("UNKNOWN_PREREQ", 1) },
        };
        tables.Skills["BADKEY"] = new ClientSkill { Constant = "BADKEY", HasDescript = true, Description = new List<string> { "x" } };

        var issues = ClientSkillValidator.Validate(tables);
        var rules = issues.Select(i => i.RuleId).ToHashSet();

        Assert.Contains("CSKILL.NAME_MISMATCH", rules);
        Assert.Contains("CSKILL.MAXLV_INVALID", rules);
        Assert.Contains("CSKILL.NEEDSKILL_UNKNOWN", rules);
        Assert.Contains("CSKILL.SKID_MISSING", rules);   // BADKEY not in SKID
        Assert.Contains("CSKILL.INFO_MISSING", rules);   // BADKEY has descript but no info

        // a quick-fix resolves its finding
        var nameFix = issues.First(i => i.RuleId == "CSKILL.NAME_MISMATCH").Fix!;
        nameFix.Apply();
        Assert.Equal("GOOD", tables.Skills["GOOD"].Aegis);
    }

    // ----- real-file smoke tests (skip when the repo data isn't present) -----

    private static string SkillDir => Path.Combine(WorkspaceConfigService.DefaultRepoRoot, "lua-files", "skillinfoz");

    [Fact]
    public void Real_files_parse_and_resplice_without_loss()
    {
        if (!Directory.Exists(SkillDir)) return;
        var codec = new LuaFileCodec(1252);
        string infoText = codec.ReadText(Path.Combine(SkillDir, "skillinfolist.lub"));
        string skidText = codec.ReadText(Path.Combine(SkillDir, "skillid.lub"));
        string descText = codec.ReadText(Path.Combine(SkillDir, "skilldescript.lub"));
        string delayText = codec.ReadText(Path.Combine(SkillDir, "skilldelaylist.lub"));

        var tables = ClientSkillReader.ReadAll(skidText, infoText, descText, delayText);
        Assert.True(tables.Skills.Count > 500);
        Assert.True(tables.Skid.Count > 500);

        var bash = tables.Skills["SM_BASH"];
        Assert.Equal("Bash", bash.SkillName);
        Assert.Equal(10, bash.MaxLv);

        // edit one skill, splice back, confirm it's reflected and the header survived.
        bash.SkillName = "Bash!";
        string spliced = ExprKeyTableSplicer.Splice(infoText, "SKILL_INFO_LIST",
            new[] { ("SKID.SM_BASH", ClientSkillWriter.FormatInfo(bash)) });
        Assert.Contains("Bash!", spliced);
        Assert.Contains("[SKID.NV_BASIC]", spliced); // untouched first entry preserved (skillinfolist has no header comment)

        var reparsed = new Dictionary<string, ClientSkill>();
        ClientSkillReader.ReadInfo(spliced, reparsed);
        Assert.Equal(tables.Skills.Count(kv => kv.Value.HasInfo), reparsed.Count); // no entries lost
        Assert.Equal("Bash!", reparsed["SM_BASH"].SkillName);
    }

    /// <summary>The high-leverage guard the audit asked for: over EVERY real info entry, the writer must emit
    /// a MaxLv line iff the source had one (no spurious `MaxLv = 0`), and formatting must be stable under a
    /// re-parse (catches any field/presence drift on edit-and-save).</summary>
    [Fact]
    public void Real_skillinfoz_preserves_field_presence_for_every_entry()
    {
        if (!Directory.Exists(SkillDir)) return;
        var codec = new LuaFileCodec(1252);
        string infoText = codec.ReadText(Path.Combine(SkillDir, "skillinfolist.lub"));
        var skills = new Dictionary<string, ClientSkill>();
        ClientSkillReader.ReadInfo(infoText, skills);

        Assert.True(skills.Count > 500);
        Assert.True(skills.Values.Count(s => !s.HasMaxLv) > 100); // the real file has ~190 MaxLv-less entries

        foreach (var s in skills.Values)
        {
            string once = ClientSkillWriter.FormatInfo(s);
            Assert.Equal(s.HasMaxLv, once.Contains("MaxLv =")); // emit MaxLv iff present in source

            var round = new Dictionary<string, ClientSkill>();
            ClientSkillReader.ReadInfo("SKILL_INFO_LIST = {\n" + once + "}\n", round);
            Assert.Equal(once, ClientSkillWriter.FormatInfo(round[s.Constant])); // stable under re-parse
        }
    }

    [Fact]
    public void Info_preserves_unmodeled_skill_fields() // audit sweep (ClientSkill twin of #3)
    {
        string info =
            "SKILL_INFO_LIST = {\n" +
            "\t[SKID.SM_BASH] = {\n" +
            "\t\t\"SM_BASH\",\n" +
            "\t\tSkillName = \"Bash\",\n" +
            "\t\tMaxLv = 10,\n" +
            "\t\tCustomFlag = true,\n" +   // a field the editor doesn't model
            "\t\tCustomCost = 42\n" +      // ditto
            "\t}\n" +
            "}\n";
        var skills = new Dictionary<string, ClientSkill>();
        ClientSkillReader.ReadInfo(info, skills);
        var s = skills["SM_BASH"];

        Assert.Equal("true", s.ExtraInfoFields["CustomFlag"]);
        Assert.Equal("42", s.ExtraInfoFields["CustomCost"]);

        string written = ClientSkillWriter.FormatInfo(s);
        Assert.Contains("CustomFlag = true", written);   // unmodeled fields survive re-emit
        Assert.Contains("CustomCost = 42", written);
    }

    [Fact]
    public void Level_less_prereqs_round_trip() // audit #6
    {
        string info =
            "SKILL_INFO_LIST = {\n" +
            "\t[SKID.WS_CARTBOOST] = {\n" +
            "\t\t\"WS_CARTBOOST\",\n" +
            "\t\tSkillName = \"Cart Boost\",\n" +
            "\t\tMaxLv = 1,\n" +
            "\t\t_NeedSkillList = { { SKID.MC_PUSHCART, 5 }, { SKID.MC_CARTREVOLUTION }, { SKID.MC_CHANGECART } }\n" +
            "\t}\n" +
            "}\n";
        var skills = new Dictionary<string, ClientSkill>();
        ClientSkillReader.ReadInfo(info, skills);
        var s = skills["WS_CARTBOOST"];

        Assert.Equal(3, s.NeedSkillList.Count);
        Assert.Equal(5, s.NeedSkillList[0].Level);
        Assert.Null(s.NeedSkillList[1].Level); // level-less prereqs preserved (were dropped before)
        Assert.Null(s.NeedSkillList[2].Level);

        string written = ClientSkillWriter.FormatInfo(s);
        Assert.Contains("{ SKID.MC_PUSHCART, 5 }", written);
        Assert.Contains("{ SKID.MC_CARTREVOLUTION }", written);    // re-emitted without a level
        Assert.Contains("{ SKID.MC_CHANGECART }", written);
        Assert.DoesNotContain("MC_CARTREVOLUTION, ", written);     // no spurious level added
    }

    [Fact]
    public void LuaString_Quote_reescapes_control_chars_and_backslash() // audit #2 / #12
    {
        Assert.Equal("\"a\\nb\"", LuaString.Quote("a\nb"));    // real newline -> \n (never a raw line break)
        Assert.Equal("\"a\\tb\"", LuaString.Quote("a\tb"));    // real tab -> \t
        Assert.Equal("\"C:\\\\x\"", LuaString.Quote("C:\\x")); // backslash -> \\
        Assert.Equal("\"q\\\"q\"", LuaString.Quote("q\"q"));   // quote -> \"
    }

    [Fact]
    public void Descript_escapes_round_trip_through_reader_and_writer() // audit #2 / #5
    {
        string src =
            "SKILL_DESCRIPT = {\n" +
            "\t[SKID.X] = {\n" +
            "\t\t\"newline\\n and tab\\t here\",\n" +     // \n and \t escapes
            "\t\t\"path C:\\Program Files\\Gravity\"\n" + // single backslashes (unknown escapes)
            "\t}\n" +
            "}\n";
        var skills = new Dictionary<string, ClientSkill>();
        ClientSkillReader.ReadDescript(src, skills);
        var s = skills["X"];

        // reader: \n/\t decode to real control chars; backslashes are preserved (were silently dropped)
        Assert.Contains("\n", s.Description[0]);
        Assert.Contains("\t", s.Description[0]);
        Assert.Contains(@"C:\Program Files\Gravity", s.Description[1]);

        // writer: control chars re-escaped (no raw newline inside the literal); backslashes doubled
        string written = ClientSkillContent.Descript(s)!;
        Assert.Contains(@"\n", written);
        Assert.Contains(@"\t", written);
        Assert.Contains(@"C:\\Program Files\\Gravity", written);
    }
}
