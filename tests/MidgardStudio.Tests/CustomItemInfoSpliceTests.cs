using MidgardStudio.Core.Lua;
using Xunit;

namespace MidgardStudio.Tests;

/// <summary>The custom itemInfo file is spliced in place, so hand-written content must survive a save.</summary>
public class CustomItemInfoSpliceTests
{
    [Fact]
    public void Splice_PreservesFunctionsCommentsAndOtherEntries()
    {
        string original =
            "-- my custom item info\n" +
            "tbl_custom = {\n" +
            "\t[30000] = { identifiedDisplayName = \"Old Hat\" },\n" +
            "}\n\n" +
            "function CustomHelper()\n\treturn 42\nend\n";

        var entry = new ItemInfoEntry { Id = 30001, IdentifiedDisplayName = "New Hat" };
        string result = new UnifiedItemInfoWriter().Splice(original, new[] { entry }, "tbl_custom");

        Assert.Contains("function CustomHelper()", result);   // hand-written function preserved
        Assert.Contains("-- my custom item info", result);    // comment preserved
        Assert.Contains("[30000]", result);                   // existing entry preserved
        Assert.Contains("[30001]", result);                   // new entry inserted
        Assert.Contains("New Hat", result);
    }

    [Fact]
    public void Splice_ReplacesExistingEntryInPlace()
    {
        string original = "tbl_override = {\n\t[501] = { identifiedDisplayName = \"Red Potion\" },\n}\n";
        var entry = new ItemInfoEntry { Id = 501, IdentifiedDisplayName = "Crimson Potion" };
        string result = new UnifiedItemInfoWriter().Splice(original, new[] { entry }, "tbl_override");

        Assert.Contains("Crimson Potion", result);
        Assert.DoesNotContain("Red Potion", result);
    }
}
