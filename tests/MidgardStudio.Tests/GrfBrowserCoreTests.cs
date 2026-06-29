using System.Text;
using MidgardStudio.Core.Grf;

namespace MidgardStudio.Tests;

public class GrfBrowserCoreTests
{
    static GrfBrowserCoreTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    // ---- ViewEncoding ----

    [Fact]
    public void Decode_korean_bytes_in_949_gives_korean()
    {
        const string korean = "안녕하세요"; // 안녕하세요
        byte[] bytes = Encoding.GetEncoding(949).GetBytes(korean);
        Assert.Equal(korean, ViewEncoding.Decode(bytes, 949));
    }

    [Fact]
    public void Reproject_round_trips_949_through_1252_lookup_key()
    {
        const string korean = "안녕"; // 안녕
        byte[] bytes = Encoding.GetEncoding(949).GetBytes(korean);
        // The GRF library hands us names decoded as 1252 (byte-preserving); reprojecting back to 949 restores them.
        string as1252 = Encoding.GetEncoding(1252).GetString(bytes);
        Assert.Equal(korean, ViewEncoding.Reproject(as1252, 949));
    }

    [Fact]
    public void Reproject_is_identity_for_same_codepage_and_ascii()
    {
        Assert.Equal("data\\sprite\\foo.spr", ViewEncoding.Reproject("data\\sprite\\foo.spr", 949));
        Assert.Equal("plain", ViewEncoding.Reproject("plain", 1252));
    }

    [Theory]
    [InlineData(949, true)]
    [InlineData(65001, true)]
    [InlineData(999999, false)]
    public void IsKnown_validates_codepages(int cp, bool known) => Assert.Equal(known, ViewEncoding.IsKnown(cp));

    // ---- GrfHashing ----

    [Fact]
    public void Crc32_matches_known_vector()
    {
        Assert.Equal("cbf43926", GrfHashing.Crc32(Encoding.ASCII.GetBytes("123456789")));
        Assert.Equal("00000000", GrfHashing.Crc32(Array.Empty<byte>()));
    }

    [Fact]
    public void Md5_matches_known_vector()
        => Assert.Equal("900150983cd24fb0d6963f7d28e17f72", GrfHashing.Md5(Encoding.ASCII.GetBytes("abc")));

    // ---- GrfSearch ----

    [Theory]
    [InlineData("Prontera.lua", "pront", true)]
    [InlineData("Prontera.lua", "", true)]
    [InlineData("Prontera.lua", "izlude", false)]
    public void NameMatches_is_case_insensitive_substring(string name, string q, bool expected)
        => Assert.Equal(expected, GrfSearch.NameMatches(name, q));

    [Fact]
    public void ContentMatches_requires_a_query()
    {
        Assert.True(GrfSearch.ContentMatches("local id = POTION", "potion"));
        Assert.False(GrfSearch.ContentMatches("local id = POTION", ""));
    }

    [Theory]
    [InlineData(".lua", true)]
    [InlineData(".LUB", true)]
    [InlineData(".spr", false)]
    public void IsTextExtension_gates_grep(string ext, bool expected)
        => Assert.Equal(expected, GrfSearch.IsTextExtension(ext));

    // ---- GrfEntryProperties ----

    [Fact]
    public void Format_produces_five_rows_with_offset_in_hex()
    {
        var rows = GrfEntryProperties.Format(new GrfEntryInfo(1000, 400, 0x1234, 1));
        Assert.Equal(5, rows.Count);
        Assert.Equal("0x1234", rows.Single(r => r.Label == "Offset").Value);
        Assert.Equal("File", rows.Single(r => r.Label == "Flags").Value);
    }

    [Fact]
    public void Ratio_handles_empty_file()
    {
        Assert.Equal("—", GrfEntryProperties.Ratio(0, 0));
        Assert.Equal("40%", GrfEntryProperties.Ratio(400, 1000));
    }

    [Fact]
    public void DescribeFlags_decodes_bits()
    {
        Assert.Equal("—", GrfEntryProperties.DescribeFlags(0));
        Assert.Equal("File, Data-encrypted", GrfEntryProperties.DescribeFlags(1 | (1 << 2)));
        Assert.Equal("File, LZMA", GrfEntryProperties.DescribeFlags(1 | (1 << 10)));
    }
}
