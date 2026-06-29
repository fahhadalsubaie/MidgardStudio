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

    [Fact]
    public void Splice_preserves_unmodeled_entry_fields() // audit #3
    {
        string original =
            "tbl_custom = {\n" +
            "\t[40000] = {\n" +
            "\t\tidentifiedDisplayName = \"Gizmo\",\n" +
            "\t\tslotCount = 0,\n" +
            "\t\tClassNum = 0,\n" +
            "\t\tbindOnEquip = true,\n" +   // a field the editor does not model
            "\t\tmagicAttribute = 7\n" +    // ditto
            "\t},\n" +
            "}\n";

        var file = new ItemInfoReader().ReadCustomFile(original);
        var entry = file.Custom[40000];
        Assert.Equal("true", entry.ExtraFields["bindOnEquip"]);
        Assert.Equal("7", entry.ExtraFields["magicAttribute"]);

        entry.IdentifiedDisplayName = "Gizmo Mk2"; // edit a MODELED field, then re-splice
        string result = new UnifiedItemInfoWriter().Splice(original, new[] { entry }, "tbl_custom");

        Assert.Contains("Gizmo Mk2", result);
        Assert.Contains("bindOnEquip = true", result);  // unmodeled fields survive the edit (were dropped before)
        Assert.Contains("magicAttribute = 7", result);
    }
}
