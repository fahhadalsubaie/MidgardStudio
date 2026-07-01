using System.Linq;
using MidgardStudio.Core.Lua;
using MidgardStudio.Core.Model;
using MidgardStudio.Core.Schemas;
using MidgardStudio.Core.Scripting;

namespace MidgardStudio.Tests;

/// <summary>The conditional-bonus engine behind the Item Forge / Combos tier builder: emit idiomatic nested
/// rAthena script, render per-tier client description sections in the official 4th-job layout, round-trip its
/// own managed block, and feed the whole thing through the real description generator.</summary>
public class ConditionalScriptTests
{
    private static ConditionalScript Model(params ConditionLadder[] ladders) => new(ladders);
    private static ConditionLadder Refine(params ConditionTier[] tiers) => new(ConditionKind.Refine, tiers);
    private static ConditionLadder Grade(params ConditionTier[] tiers) => new(ConditionKind.Grade, tiers);
    private static ConditionTier Tier(int t, params string[] bonuses) => new(t, bonuses);

    [Fact]
    public void Emit_nests_tiers_cumulatively_and_marks_the_block()
    {
        var m = Model(
            Refine(Tier(7, "bonus bAtk,10;"), Tier(9, "bonus bPAtk,7;")),
            Grade(Tier(4, "bonus bMaxHPrate,5;"))); // grade A

        string s = m.Emit();

        Assert.Contains(ConditionalScript.BeginMarker, s);
        Assert.Contains(ConditionalScript.EndMarker, s);
        Assert.Contains("if (getrefine()>=7) {", s);
        Assert.Contains("if (getenchantgrade()>=ENCHANTGRADE_A) {", s);
        // The +9 tier nests INSIDE the +7 tier (cumulative), so its `if` is indented one level deeper.
        Assert.Contains("\tif (getrefine()>=9) {", s);
        Assert.True(s.IndexOf(">=7", System.StringComparison.Ordinal) < s.IndexOf(">=9", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Emit_of_an_empty_model_is_empty()
    {
        Assert.Equal(string.Empty, Model().Emit());
        Assert.Equal(string.Empty, Model(Refine(Tier(9))).Emit()); // a tier with no bonuses is nothing
    }

    [Fact]
    public void Describe_uses_the_official_layout_refine_headers_and_grade_section()
    {
        var m = Model(
            Refine(Tier(7, "bonus bAtk,10;"), Tier(9, "bonus bPAtk,7;", "bonus bStr,1;")),
            Grade(Tier(4, "bonus bMaxHPrate,5;")));

        var desc = m.Describe(useColors: true);

        Assert.Contains("^0000FFRefine Level +7^000000:", desc);
        Assert.Contains("ATK +10.", desc);
        Assert.Contains("^0000FFRefine Level +9^000000:", desc);
        Assert.Contains("P.Atk +7, STR +1.", desc);                       // tier bonuses comma-joined on one line
        Assert.Contains("^CC3D3D[Bonus by Grade]^000000", desc);
        Assert.Contains(desc, d => d.StartsWith("[Grade A]:", System.StringComparison.Ordinal) && d.Contains("Max HP") && d.Contains("5%"));
    }

    [Fact]
    public void Describe_without_colors_omits_the_codes()
    {
        var plain = Model(Refine(Tier(9, "bonus bPAtk,7;")), Grade(Tier(1, "bonus bStr,1;"))).Describe(useColors: false);
        Assert.Contains("Refine Level +9:", plain);
        Assert.Contains("[Bonus by Grade]", plain);
        Assert.DoesNotContain(plain, d => d.Contains('^'));
    }

    [Fact]
    public void Round_trips_a_multi_tier_ladder_back_into_separate_tiers()
    {
        var m = Model(Refine(Tier(7, "bonus bStr,1;"), Tier(9, "bonus bAgi,2;", "bonus bVit,3;"), Tier(12, "bonus bDex,4;")));

        var parsed = ConditionalScript.TryParse(m.Emit());

        Assert.NotNull(parsed);
        var ladder = Assert.Single(parsed!.Ladders);
        Assert.Equal(ConditionKind.Refine, ladder.Kind);
        Assert.Equal(new[] { 7, 9, 12 }, ladder.Tiers.Select(t => t.Threshold));
        Assert.Equal(new[] { "bonus bAgi,2;", "bonus bVit,3;" }, ladder.Tiers[1].Bonuses);
        Assert.Equal(new[] { "bonus bDex,4;" }, ladder.Tiers[2].Bonuses);
    }

    [Fact]
    public void Round_trips_refine_plus_grade()
    {
        var m = Model(
            Refine(Tier(9, "bonus bPAtk,7;")),
            Grade(Tier(3, "bonus bSMatk,5;"), Tier(4, "bonus bMaxHPrate,5;"))); // B then A

        var parsed = ConditionalScript.TryParse(m.Emit());

        Assert.NotNull(parsed);
        var refine = Assert.Single(parsed!.Ladders, l => l.Kind == ConditionKind.Refine);
        Assert.Equal(new[] { 9 }, refine.Tiers.Select(t => t.Threshold));
        var grade = Assert.Single(parsed.Ladders, l => l.Kind == ConditionKind.Grade);
        Assert.Equal(new[] { 3, 4 }, grade.Tiers.Select(t => t.Threshold)); // B=3, A=4
        Assert.Equal(new[] { "bonus bSMatk,5;" }, grade.Tiers[0].Bonuses);
    }

    [Fact]
    public void StripManagedBlock_removes_the_block_but_keeps_surrounding_script()
    {
        var m = Model(Refine(Tier(9, "bonus bPAtk,7;")));
        string full = "bonus bStr,1;\n" + m.Emit() + "bonus bAgi,2;\n";

        string stripped = ConditionalScript.StripManagedBlock(full);

        Assert.Contains("bonus bStr,1;", stripped);
        Assert.Contains("bonus bAgi,2;", stripped);
        Assert.DoesNotContain("getrefine", stripped);          // the block is gone
        Assert.DoesNotContain("bonus bPAtk,7;", stripped);
        Assert.DoesNotContain(ConditionalScript.BeginMarker, stripped);
    }

    [Theory]
    [InlineData("bonus bStr,1;")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_returns_null_without_a_managed_block(string? script)
    {
        Assert.Null(ConditionalScript.TryParse(script));
    }

    [Fact]
    public void ItemAutocomplete_describes_conditional_bonuses_and_does_not_double_count_them()
    {
        var cond = Model(
            Refine(Tier(9, "bonus bPAtk,7;", "bonus bStr,1;")),
            Grade(Tier(4, "bonus bMaxHPrate,5;")));
        var rec = new DbRecord(ItemDbSchema.Instance);
        rec.SetRaw("Id", 990001);
        rec.SetRaw("AegisName", "TestGear");
        rec.SetRaw("Name", "Test Gear");
        rec.SetRaw("Type", "Armor");
        rec.SetRaw("Script", new ScriptValue("bonus bMdef,5;\n" + cond.Emit()));

        var desc = new ItemAutocomplete(new AutocompleteConfig()).IdentifiedDescription(rec);

        Assert.Contains("MDEF +5", desc);                              // flat bonus (outside the block)
        Assert.Contains("^0000FFRefine Level +9^000000:", desc);       // conditional section rendered
        Assert.Contains("P.Atk +7, STR +1.", desc);                    // tier bonuses, comma-joined, once
        Assert.Contains("^CC3D3D[Bonus by Grade]^000000", desc);
        Assert.DoesNotContain("Has a special effect.", desc);          // not the generic fallback
        Assert.DoesNotContain(desc, d => d == "STR +1");               // NOT double-counted as a flat bonus
    }
}
