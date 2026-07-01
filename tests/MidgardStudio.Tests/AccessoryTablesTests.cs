using System.Collections.Generic;
using System.IO;
using System.Linq;
using MidgardStudio.Core.Lua;
using MidgardStudio.Core.Sprites;
using MidgardStudio.Core.Workspace;

namespace MidgardStudio.Tests;

public class AccessoryTablesTests
{
    private static string DataInfo =>
        Path.Combine(WorkspaceConfigService.DefaultRepoRoot, "lua-files", "datainfo");

    [Fact]
    public void Reads_real_accessory_constants_and_names()
    {
        string idPath = Path.Combine(DataInfo, "accessoryid.lub");
        string namePath = Path.Combine(DataInfo, "accname.lub");
        if (!File.Exists(idPath) || !File.Exists(namePath)) return;

        var codec = new LuaFileCodec(1252);
        var constants = AccessoryTables.ReadConstants(codec.ReadText(idPath));
        Assert.True(constants.Count > 100, $"expected many accessory constants, got {constants.Count}");

        var names = AccessoryTables.ReadNames(codec.ReadText(namePath));
        Assert.True(names.Count > 50, $"expected many accname mappings, got {names.Count}");
    }

    [Fact]
    public void FindId_resolves_a_real_registered_sprite_to_its_real_view_id()
    {
        // The Forge reuse path: a sprite already present in the real client tables must resolve to the id it
        // already holds (so no duplicate accessory entry is written). Uses the shipped 1252 files as-is.
        string idPath = Path.Combine(DataInfo, "accessoryid.lub");
        string namePath = Path.Combine(DataInfo, "accname.lub");
        if (!File.Exists(idPath) || !File.Exists(namePath)) return;

        var codec = new LuaFileCodec(1252);
        var constants = AccessoryTables.ReadConstants(codec.ReadText(idPath));
        var names = AccessoryTables.ReadNames(codec.ReadText(namePath));

        // Pick a real entry present in BOTH tables and assert the reverse lookup recovers its id.
        var sample = names.FirstOrDefault(kv => constants.ContainsKey(kv.Key));
        Assert.NotNull(sample.Key); // there is at least one fully-mapped accessory in the shipped files

        int expected = constants[sample.Key];
        Assert.Equal(expected, SpriteRegistry.FindId(constants, names, new List<PendingRegistration>(), sample.Value));
        // A name that isn't in the tables stays unregistered (would trigger a fresh registration, not a reuse).
        Assert.Null(SpriteRegistry.FindId(constants, names, new List<PendingRegistration>(), "_definitely_not_a_real_sprite_zzz"));
    }

    [Fact]
    public void Append_constant_inserts_and_reparses()
    {
        string idPath = Path.Combine(DataInfo, "accessoryid.lub");
        if (!File.Exists(idPath)) return;

        var codec = new LuaFileCodec(1252);
        string text = codec.ReadText(idPath);
        var before = AccessoryTables.ReadConstants(text);
        int nextId = AccessoryTables.NextFreeId(before);

        string updated = AccessoryTables.AppendConstant(text, "ACCESSORY_IDs", "ACCESSORY_TEST_MIDGARD", nextId);
        var after = AccessoryTables.ReadConstants(updated);

        Assert.Equal(before.Count + 1, after.Count);
        Assert.Equal(nextId, after["ACCESSORY_TEST_MIDGARD"]);
    }

    [Fact]
    public void Append_after_a_comma_less_last_entry_adds_the_separator_and_stays_valid()
    {
        // The shipped accessoryid.lub / accname.lub end their LAST entry WITHOUT a trailing comma. Appending a
        // new entry must add the separator to that final entry (else the two entries abut = Lua syntax error the
        // strict client rejects), and both must remain present so the client doesn't crash on a half-mapped id.
        string idText =
            "ACCESSORY_IDs = {\n" +
            "\tACCESSORY_C_Jaow_Pirun = 2810,\n" +
            "\tACCESSORY_C_Spirit_Cat_TH = 2811\n" +   // <-- no trailing comma, exactly like the real file
            "}\n";
        string updatedId = AccessoryTables.AppendConstant(idText, "ACCESSORY_IDs", "ACCESSORY_C_New_Hat", 2812);
        var consts = AccessoryTables.ReadConstants(updatedId);
        Assert.Equal(2811, consts["ACCESSORY_C_Spirit_Cat_TH"]); // previous last entry still parses (got its comma)
        Assert.Equal(2812, consts["ACCESSORY_C_New_Hat"]);       // new entry parses
        Assert.Contains("ACCESSORY_C_Spirit_Cat_TH = 2811,", updatedId); // separator inserted on the old last line

        string nameText =
            "AccNameTable = {\n" +
            "\t[ACCESSORY_IDs.ACCESSORY_C_Jaow_Pirun] = \"_C_Jaow_Pirun\",\n" +
            "\t[ACCESSORY_IDs.ACCESSORY_C_Spirit_Cat_TH] = \"_C_Spirit_Cat_TH\"\n" + // no trailing comma
            "}\n";
        string updatedName = AccessoryTables.AppendName(nameText, "AccNameTable", "ACCESSORY_IDs", "ACCESSORY_C_New_Hat", "_C_New_Hat");
        var names = AccessoryTables.ReadNames(updatedName);
        Assert.Equal("_C_Spirit_Cat_TH", names["ACCESSORY_C_Spirit_Cat_TH"]); // sibling intact
        Assert.Equal("_C_New_Hat", names["ACCESSORY_C_New_Hat"]);             // new mapping present in the file too
    }

    [Fact]
    public void Append_constant_throws_when_table_missing()
    {
        // The target table isn't in the file — must fail loud (edit kept, file untouched), not silently no-op.
        Assert.Throws<InvalidDataException>(() =>
            AccessoryTables.AppendConstant("local other = {}\n", "SKID", "CUSTOM_1", 6608));
    }

    [Fact]
    public void Append_name_throws_when_table_missing()
    {
        Assert.Throws<InvalidDataException>(() =>
            AccessoryTables.AppendName("local other = {}\n", "AccNameTable", "ACCESSORY_IDs", "CUSTOM_1", "_custom"));
    }

    [Fact]
    public void Append_constant_throws_when_brace_unbalanced()
    {
        // Table opens but never closes — a malformed file. Must throw, not corrupt or silently drop.
        Assert.Throws<InvalidDataException>(() =>
            AccessoryTables.AppendConstant("SKID = {\n\tAT_FOO = 1,\n", "SKID", "CUSTOM_1", 6608));
    }

    [Fact]
    public void SetOrAppendName_updates_in_place_and_never_duplicates() // audit #7
    {
        string text =
            "JobNameTable = {\n" +
            "\t[jobtbl.JT_PORING] = \"Poring\",\n" +
            "\t[jobtbl.JT_LUNATIC] = \"Lunatic\",\n" +
            "}\n";

        // Re-registering an existing key replaces the value in place — no duplicate line.
        string updated = AccessoryTables.SetOrAppendName(text, "JobNameTable", "jobtbl", "JT_PORING", "Poring_Custom");
        var names = AccessoryTables.ReadNames(updated, "JobNameTable");
        Assert.Equal("Poring_Custom", names["JT_PORING"]);
        Assert.Equal("Lunatic", names["JT_LUNATIC"]);                 // sibling untouched
        Assert.Equal(1, CountOccurrences(updated, "[jobtbl.JT_PORING]")); // exactly ONE — no duplicate appended
    }

    [Fact]
    public void Append_does_not_put_a_comma_on_a_trailing_comment() // audit #16
    {
        string text =
            "ACCESSORY_IDs = {\n" +
            "\tACCESSORY_FOO = 100,\n" +
            "\t-- end of custom accessories\n" +
            "}\n";
        string result = AccessoryTables.AppendConstant(text, "ACCESSORY_IDs", "ACCESSORY_BAR", 101);

        Assert.Contains("-- end of custom accessories\n", result); // comment line intact
        Assert.DoesNotContain("accessories,", result);             // no stray comma appended to the comment
        var consts = AccessoryTables.ReadConstants(result);
        Assert.Equal(101, consts["ACCESSORY_BAR"]);                // new entry inserted + parseable
        Assert.Equal(100, consts["ACCESSORY_FOO"]);
    }

    [Fact]
    public void EncodeText_preserves_the_files_line_endings() // audit #11 / #14
    {
        var codec = new LuaFileCodec(1252);
        Assert.DoesNotContain((byte)'\r', codec.EncodeText("a\nb\nc\n")); // LF-only stays LF
        Assert.Contains((byte)'\r', codec.EncodeText("a\r\nb\r\n"));      // CRLF stays CRLF

        // End-to-end: an LF-only sprite table, appended to and encoded, stays LF — a one-line append
        // must not rewrite every line to CRLF (the reported whole-file +N-bytes diff churn).
        string lfTable = "AccNameTable = {\n\t[ACCESSORY_IDs.AC_A] = \"a\",\n}\n";
        string appended = AccessoryTables.AppendName(lfTable, "AccNameTable", "ACCESSORY_IDs", "AC_B", "b");
        byte[] encoded = codec.EncodeText(appended);
        Assert.DoesNotContain((byte)'\r', encoded);                      // no CR inserted anywhere
        Assert.Equal(appended.Length, encoded.Length);                  // byte-for-byte (1252 single-byte, LF preserved)
    }

    [Fact]
    public void AppendName_escapes_the_sprite_value() // audit sweep — sprite is user free-text
    {
        string r = AccessoryTables.AppendName("AccNameTable = {\n}\n", "AccNameTable", "ACCESSORY_IDs", "AC_X", "a\"b\\c");
        // the value is escaped on write and decodes back to the original on read (symmetric, not corrupt)
        Assert.Equal("a\"b\\c", AccessoryTables.ReadNames(r, "AccNameTable")["AC_X"]);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int n = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, System.StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }
}
