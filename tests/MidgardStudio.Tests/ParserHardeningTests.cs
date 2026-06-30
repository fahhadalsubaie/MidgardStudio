using MidgardStudio.Core.Lua;

namespace MidgardStudio.Tests;

/// <summary>Regression guards for two parser hardenings: LuaTableParser must not match a table name that sits
/// inside a comment/string, and ItemScriptParser must not let a // comment swallow the next statement.</summary>
public class ParserHardeningTests
{
    [Fact]
    public void ParseNamedTable_ignores_the_name_inside_a_preceding_comment()
    {
        string lua = "-- tbl_custom = { not the real table }\ntbl_custom = {\n  [501] = { 1, 2 },\n}\n";
        var table = new LuaTableParser(lua).ParseNamedTable("tbl_custom");
        Assert.NotNull(table);
        Assert.True(table!.IntKeys.ContainsKey(501)); // parsed the REAL table, not the comment
    }

    [Fact]
    public void ParseNamedTable_ignores_the_name_inside_a_string_literal()
    {
        string lua = "local note = \"tbl_custom = { fake \"\ntbl_custom = {\n  [7] = { },\n}\n";
        var table = new LuaTableParser(lua).ParseNamedTable("tbl_custom");
        Assert.NotNull(table);
        Assert.True(table!.IntKeys.ContainsKey(7));
    }

    [Fact]
    public void Parse_keeps_a_bonus_after_an_inline_line_comment()
    {
        // Before the fix, flattening newlines first merged "// note" with the next line, so the StartsWith("//")
        // ignorable check silently dropped the bAgi bonus. It must survive now.
        var r = ItemScriptParser.Parse("bonus bStr,1; // note\nbonus bAgi,1;");
        Assert.Contains(r.Bonuses, b => b.Contains("STR"));
        Assert.Contains(r.Bonuses, b => b.Contains("AGI"));
    }

    [Fact]
    public void Parse_strips_a_block_comment_without_dropping_following_statements()
    {
        var r = ItemScriptParser.Parse("bonus bStr,1; /* multi\nline note */ bonus bAgi,1;");
        Assert.Contains(r.Bonuses, b => b.Contains("STR"));
        Assert.Contains(r.Bonuses, b => b.Contains("AGI"));
    }
}
