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

    // ----- Modern 4th-job bonus recognition (issue #5) -----

    [Fact]
    public void Parse_describes_the_issue5_multiline_example()
    {
        // The exact case from issue #5: two flat bonus lines. Before the fix only bAllStats was described and
        // bAllTraitStats was silently dropped (and it tripped the generic "special effect" flag).
        var r = ItemScriptParser.Parse("bonus bAllStats,10;\nbonus bAllTraitStats,5;");
        Assert.Contains(r.Bonuses, b => b.Contains("All Stats") && b.Contains("+10"));
        Assert.Contains(r.Bonuses, b => b.Contains("All Trait Stats") && b.Contains("+5"));
        Assert.False(r.HasComplex); // both recognized now → nothing falls through to "special effect"
    }

    [Theory]
    [InlineData("bonus bPow,1;", "POW", "+1")]
    [InlineData("bonus bSta,2;", "STA", "+2")]
    [InlineData("bonus bWis,3;", "WIS", "+3")]
    [InlineData("bonus bSpl,4;", "SPL", "+4")]
    [InlineData("bonus bCon,5;", "CON", "+5")]
    [InlineData("bonus bCrt,6;", "CRT", "+6")]
    [InlineData("bonus bPAtk,7;", "P.Atk", "+7")]
    [InlineData("bonus bSMatk,8;", "S.MATK", "+8")]
    [InlineData("bonus bRes,9;", "Res", "+9")]
    [InlineData("bonus bMRes,4;", "M.Res", "+4")]
    [InlineData("bonus bHPlus,3;", "H.Plus", "+3")]
    [InlineData("bonus bCRate,2;", "C.Rate", "+2")]
    [InlineData("bonus bMaxAP,50;", "Max AP", "+50")]
    public void Parse_recognizes_modern_bonuses(string script, string label, string amount)
    {
        var r = ItemScriptParser.Parse(script);
        Assert.Contains(r.Bonuses, b => b.Contains(label) && b.Contains(amount));
        Assert.False(r.HasComplex);
    }

    [Theory]
    [InlineData("bonus bPAtkRate,5;", "P.Atk")]
    [InlineData("bonus bSMatkRate,5;", "S.MATK")]
    [InlineData("bonus bResRate,5;", "Res")]
    [InlineData("bonus bMResRate,5;", "M.Res")]
    [InlineData("bonus bHPlusRate,5;", "H.Plus")]
    [InlineData("bonus bCRateRate,5;", "C.Rate")]
    [InlineData("bonus bMaxAPrate,5;", "Max AP")]
    public void Parse_renders_rate_variants_as_percent(string script, string label)
    {
        var r = ItemScriptParser.Parse(script);
        Assert.Contains(r.Bonuses, b => b.Contains(label) && b.Contains("5%"));
    }

    [Fact]
    public void Parse_modern_bonuses_are_case_insensitive()
    {
        var r = ItemScriptParser.Parse("bonus bpow,1; bonus BPATK,7; bonus ballTraitStats,3;");
        Assert.Contains(r.Bonuses, b => b.Contains("POW") && b.Contains("+1"));
        Assert.Contains(r.Bonuses, b => b.Contains("P.Atk") && b.Contains("+7"));
        Assert.Contains(r.Bonuses, b => b.Contains("All Trait Stats") && b.Contains("+3"));
    }
}
