using System;
using System.Linq;
using MidgardStudio.Core.Scripting;

namespace MidgardStudio.Tests;

/// <summary>The combo conditional-bonus engine: compound cross-piece conditions (total refine sum + per-piece
/// enchant grade), nested skill gates, the Location→EQI map, a readable preview, and round-trip.</summary>
public class ComboConditionalScriptTests
{
    private static ComboSkillGate Gate(string skill, int lv, params string[] bonuses) => new(skill, lv, bonuses);
    private static ComboTier Tier(int? refine, int? grade, string[] bonuses, params ComboSkillGate[] gates) => new(refine, grade, bonuses, gates);

    [Fact]
    public void Emit_matches_the_official_combo_form_with_sum_grade_and_skill_gate()
    {
        // Rebuild the Frontier Rune Crown (Meister) + Two-Handed Axe combo from issue #4.
        var m = new ComboConditionalScript(
            new[] { "EQI_HEAD_TOP", "EQI_HAND_R" },
            new[]
            {
                Tier(24, 4, // total refine +24, every piece Grade A
                    new[] { "bonus2 bSkillCooldown,\"MT_RUSH_STRIKE\",-150;" },
                    Gate("MT_POWERFUL_SWING", 5, "bonus4 bAutoSpellOnSkill,\"MT_RUSH_STRIKE\",\"MT_POWERFUL_SWING\",5,1000;")),
            },
            new[] { "bonus2 bSkillAtk,\"MT_RUSH_STRIKE\",25;", "bonus bNonCritAtkRate,15;" });

        string s = m.Emit();

        Assert.Contains(
            "if (getequiprefinerycnt(EQI_HEAD_TOP)+getequiprefinerycnt(EQI_HAND_R)>=24 && getenchantgrade(EQI_HEAD_TOP)>=ENCHANTGRADE_A && getenchantgrade(EQI_HAND_R)>=ENCHANTGRADE_A) {",
            s);
        Assert.Contains("if (getskilllv(\"MT_POWERFUL_SWING\")>=5) {", s);
        Assert.Contains("bonus2 bSkillCooldown,\"MT_RUSH_STRIKE\",-150;", s);
        Assert.Contains("bonus2 bSkillAtk,\"MT_RUSH_STRIKE\",25;", s);   // unconditional
        Assert.Contains("bonus bNonCritAtkRate,15;", s);
    }

    [Fact]
    public void Emit_grade_only_and_refine_only_tiers()
    {
        var grade = new ComboConditionalScript(new[] { "EQI_ARMOR" },
            new[] { Tier(null, 3, new[] { "bonus bStr,1;" }) }, Array.Empty<string>()).Emit();
        Assert.Contains("if (getenchantgrade(EQI_ARMOR)>=ENCHANTGRADE_B) {", grade);
        Assert.DoesNotContain("getequiprefinerycnt", grade);

        var refine = new ComboConditionalScript(new[] { "EQI_HEAD_TOP", "EQI_ARMOR" },
            new[] { Tier(15, null, new[] { "bonus bStr,1;" }) }, Array.Empty<string>()).Emit();
        Assert.Contains("if (getequiprefinerycnt(EQI_HEAD_TOP)+getequiprefinerycnt(EQI_ARMOR)>=15) {", refine);
        Assert.DoesNotContain("getenchantgrade", refine);
    }

    [Fact]
    public void Emit_of_an_empty_model_is_empty()
    {
        Assert.Equal(string.Empty, new ComboConditionalScript(new[] { "EQI_ARMOR" }, Array.Empty<ComboTier>(), Array.Empty<string>()).Emit());
    }

    [Theory]
    [InlineData("Head_Top", "EQI_HEAD_TOP")]
    [InlineData("Both_Hand", "EQI_HAND_R")]   // two-handed weapon -> right hand slot
    [InlineData("Left_Hand", "EQI_HAND_L")]
    [InlineData("Right_Accessory", "EQI_ACC_R")]
    [InlineData("Armor", "EQI_ARMOR")]
    [InlineData("Garment", "EQI_GARMENT")]
    [InlineData("Shadow_Weapon", "EQI_SHADOW_WEAPON")]
    public void LocationToEqi_maps_locations_to_equip_slots(string location, string eqi)
    {
        Assert.Equal(eqi, ComboConditionalScript.LocationToEqi(location));
    }

    [Fact]
    public void LocationToEqi_null_for_unmappable()
    {
        Assert.Null(ComboConditionalScript.LocationToEqi("Not_A_Location"));
        Assert.Null(ComboConditionalScript.LocationToEqi(null));
    }

    [Fact]
    public void Describe_previews_the_condition_and_bonuses()
    {
        var m = new ComboConditionalScript(
            new[] { "EQI_HEAD_TOP", "EQI_HAND_R" },
            new[] { Tier(24, 4, new[] { "bonus bPAtk,7;" }, Gate("MG_FIREBOLT", 5, "bonus bStr,1;")) },
            Array.Empty<string>());

        var desc = m.Describe();

        Assert.Contains(desc, d => d.StartsWith("When ", StringComparison.Ordinal) && d.Contains("total refine +24") && d.Contains("Grade A"));
        Assert.Contains(desc, d => d.Contains("P.Atk +7"));
        Assert.Contains(desc, d => d.Contains("Lv 5+") && d.Contains("STR +1"));
    }

    [Fact]
    public void Round_trips_tiers_gates_unconditional_and_pieces()
    {
        var m = new ComboConditionalScript(
            new[] { "EQI_HEAD_TOP", "EQI_HAND_R" },
            new[]
            {
                Tier(24, 4,
                    new[] { "bonus2 bSkillCooldown,\"MT_RUSH_STRIKE\",-150;" },
                    Gate("MT_POWERFUL_SWING", 5, "bonus4 bAutoSpellOnSkill,\"MT_RUSH_STRIKE\",\"MT_POWERFUL_SWING\",5,1000;")),
            },
            new[] { "bonus bNonCritAtkRate,15;" });

        var parsed = ComboConditionalScript.TryParse(m.Emit());

        Assert.NotNull(parsed);
        Assert.Equal(new[] { "EQI_HEAD_TOP", "EQI_HAND_R" }, parsed!.EqiSlots);
        Assert.Contains("bonus bNonCritAtkRate,15;", parsed.Unconditional);
        var tier = Assert.Single(parsed.Tiers);
        Assert.Equal(24, tier.RefineTotal);
        Assert.Equal(4, tier.Grade);
        Assert.Contains("bonus2 bSkillCooldown,\"MT_RUSH_STRIKE\",-150;", tier.Bonuses);
        var gate = Assert.Single(tier.SkillGates);
        Assert.Equal("MT_POWERFUL_SWING", gate.Skill);
        Assert.Equal(5, gate.Level);
        Assert.Equal(new[] { "bonus4 bAutoSpellOnSkill,\"MT_RUSH_STRIKE\",\"MT_POWERFUL_SWING\",5,1000;" }, gate.Bonuses);
    }

    [Fact]
    public void StripManagedBlock_removes_the_block_but_keeps_surrounding_script()
    {
        var m = new ComboConditionalScript(new[] { "EQI_ARMOR" }, new[] { Tier(null, 4, new[] { "bonus bStr,1;" }) }, Array.Empty<string>());
        string full = "bonus bAgi,1;\n" + m.Emit() + "bonus bVit,1;\n";

        string stripped = ComboConditionalScript.StripManagedBlock(full);

        Assert.Contains("bonus bAgi,1;", stripped);
        Assert.Contains("bonus bVit,1;", stripped);
        Assert.DoesNotContain("getenchantgrade", stripped);
        Assert.DoesNotContain(ComboConditionalScript.BeginMarker, stripped);
    }

    [Theory]
    [InlineData("bonus bStr,1;")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_returns_null_without_a_managed_block(string? script)
    {
        Assert.Null(ComboConditionalScript.TryParse(script));
    }
}
