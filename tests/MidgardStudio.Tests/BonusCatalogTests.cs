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
        Assert.Equal("bonus bNoCastCancel;", BonusCatalog.Format(def, System.Array.Empty<string>()));
    }

    [Fact]
    public void Format_MissingValues_FallsBackToDefaults()
    {
        var def = BonusCatalog.All.First(d => d.Name == "bAddRace");
        // No values supplied -> both params use their declared defaults.
        var result = BonusCatalog.Format(def, System.Array.Empty<string>());
        Assert.Equal("bonus2 bAddRace,RC_Formless,10;", result);
    }
}
