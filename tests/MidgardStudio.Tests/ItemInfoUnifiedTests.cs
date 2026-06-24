using MidgardStudio.Core.Lua;

namespace MidgardStudio.Tests;

public class ItemInfoUnifiedTests
{
    private const string Unified =
        "tbl = {\n" +
        "\t[100] = {\n" +
        "\t\tunidentifiedDisplayName = \"Old U\",\n" +
        "\t\tunidentifiedResourceName = \"res_old\",\n" +
        "\t\tunidentifiedDescriptionName = { \"u\" },\n" +
        "\t\tidentifiedDisplayName = \"Old\",\n" +
        "\t\tidentifiedResourceName = \"res_old\",\n" +
        "\t\tidentifiedDescriptionName = { \"d\" },\n" +
        "\t\tslotCount = 0,\n" +
        "\t\tClassNum = 5,\n" +
        "\t\tcostume = false\n" +
        "\t},\n" +
        "}\n" +
        "\nfunction main()\n\treturn true\nend\n";

    [Fact]
    public void Official_reader_indexes_and_lazily_parses_entries()
    {
        var official = new OfficialItemInfo(Unified);

        Assert.True(official.Contains(100));
        Assert.False(official.Contains(999));
        Assert.Null(official.Entry(999));

        var e = official.Entry(100)!;
        Assert.Equal("Old", e.IdentifiedDisplayName);
        Assert.Equal("res_old", e.IdentifiedResourceName);
        Assert.Equal(5, e.ClassNum);
    }

    [Fact]
    public void Splice_replaces_existing_entry_and_preserves_other_content()
    {
        var edit = new ItemInfoEntry { Id = 100, IdentifiedDisplayName = "New", IdentifiedResourceName = "res_new", ClassNum = 7 };
        string result = new UnifiedItemInfoWriter().Splice(Unified, new[] { edit });

        Assert.Contains("function main()", result); // trailing helper preserved
        Assert.DoesNotContain("\"Old\"", result);

        var official = new OfficialItemInfo(result);
        var e = official.Entry(100)!;
        Assert.Equal("New", e.IdentifiedDisplayName);
        Assert.Equal("res_new", e.IdentifiedResourceName);
        Assert.Equal(7, e.ClassNum);
    }

    [Fact]
    public void Splice_inserts_new_entry_before_close_keeping_existing()
    {
        var added = new ItemInfoEntry { Id = 200, IdentifiedDisplayName = "Brand New", IdentifiedResourceName = "res200" };
        string result = new UnifiedItemInfoWriter().Splice(Unified, new[] { added });

        var official = new OfficialItemInfo(result);
        Assert.True(official.Contains(100));                 // untouched entry survives
        Assert.Equal("Old", official.Entry(100)!.IdentifiedDisplayName);
        Assert.Equal("Brand New", official.Entry(200)!.IdentifiedDisplayName);
        Assert.Contains("function main()", result);
    }
}
