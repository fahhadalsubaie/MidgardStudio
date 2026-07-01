using System;
using System.Linq;
using MidgardStudio.Core.Scripting;
using Xunit;

namespace MidgardStudio.Tests;

public class BonusCatalogTests
{
    [Fact]
    public void Format_SimpleStat_ProducesStatement()
    {
        var def = BonusCatalog.All.First(d => d.Name == "bStr");
        Assert.Equal("bonus bStr,10;", BonusCatalog.Format(def, new[] { "10" }));
    }

    [Fact]
    public void Format_Bonus2WithEnum_ProducesStatement()
    {
        var def = BonusCatalog.All.First(d => d.Name == "bAddRace");
        Assert.Equal("bonus2 bAddRace,RC_DemiHuman,5;", BonusCatalog.Format(def, new[] { "RC_DemiHuman", "5" }));
    }

    [Fact]
    public void Format_FlagBonus_HasNoParameters()
    {
        var def = BonusCatalog.All.First(d => d.Name == "bNoCastCancel");
        Assert.Equal("bonus bNoCastCancel;", BonusCatalog.Format(def, Array.Empty<string>()));
    }

    [Fact]
    public void Format_MissingValues_FallsBackToDefaults()
    {
        var def = BonusCatalog.All.First(d => d.Name == "bAddRace");
        Assert.Equal("bonus2 bAddRace,RC_Formless,5;", BonusCatalog.Format(def, Array.Empty<string>()));
    }

    [Fact]
    public void Format_AutoSpellChance_ScaledToTenthsOfPercent()
    {
        // The user types a real percentage (5%); rAthena stores auto-cast chance in 1/10% -> 50.
        var def = BonusCatalog.All.First(d => d.Name == "bAutoSpell");
        Assert.Equal("bonus3 bAutoSpell,\"AL_HEAL\",1,50;", BonusCatalog.Format(def, new[] { "\"AL_HEAL\"", "1", "5" }));
    }

    [Fact]
    public void Format_StatusChance_ScaledToHundredthsOfPercent()
    {
        // Status (bAddEff) chance is in 1/100% -> a typed 5% becomes 500.
        var def = BonusCatalog.All.First(d => d.Name == "bAddEff");
        Assert.Equal("bonus2 bAddEff,Eff_Stun,500;", BonusCatalog.Format(def, new[] { "Eff_Stun", "5" }));
    }

    [Fact]
    public void EveryDefinition_FormatsAndHasAKnownCategory()
    {
        foreach (var def in BonusCatalog.All)
        {
            Assert.Contains(def.Category, BonusCatalog.Categories);
            var line = BonusCatalog.Format(def, Array.Empty<string>());
            Assert.StartsWith(def.Family + " " + def.Name, line);
            Assert.EndsWith(";", line);
        }
    }

    [Theory]
    [InlineData("bPow")]        // trait stat
    [InlineData("bAllTraitStats")]
    [InlineData("bPAtk")]       // derived 4th-job sub-stat
    [InlineData("bSMatk")]
    [InlineData("bRes")]
    [InlineData("bMaxAP")]      // 4th-job resource
    public void ForMode_hides_4th_job_effects_in_pre_renewal(string renewalOnly)
    {
        Assert.Contains(BonusCatalog.ForMode(renewal: true), d => d.Name == renewalOnly);   // renewal: available
        Assert.DoesNotContain(BonusCatalog.ForMode(renewal: false), d => d.Name == renewalOnly); // pre-re: hidden
    }

    [Fact]
    public void ForMode_keeps_classic_effects_in_both_modes()
    {
        foreach (var name in new[] { "bStr", "bAllStats", "bAtk", "bMaxHP", "bAddRace" })
        {
            Assert.Contains(BonusCatalog.ForMode(renewal: true), d => d.Name == name);
            Assert.Contains(BonusCatalog.ForMode(renewal: false), d => d.Name == name);
        }
    }
}
