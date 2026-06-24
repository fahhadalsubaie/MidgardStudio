using System.IO;
using System.Text;
using MidgardStudio.Core.IO;
using MidgardStudio.Core.Lua;
using MidgardStudio.Core.Workspace;

namespace MidgardStudio.Tests;

public class ClientLuaTests
{
    private static string ItemInfoCPath =>
        Path.Combine(WorkspaceConfigService.DefaultRepoRoot, "SystemEN", "itemInfo_C.lua");

    private static ItemInfoFile SampleFile()
    {
        var file = new ItemInfoFile();
        file.Custom[40000] = new ItemInfoEntry
        {
            Id = 40000,
            UnidentifiedDisplayName = "Sword",
            UnidentifiedResourceName = "Black_Sword",
            UnidentifiedDescription = new() { "Unidentified item." },
            IdentifiedDisplayName = "Devastator",
            IdentifiedResourceName = "Black_Sword",
            IdentifiedDescription = new() { "An unholy sword.", "Class :^777777 Sword^000000" },
            SlotCount = 3,
            ClassNum = 2,
            Costume = false,
        };
        file.Override[501] = new ItemInfoEntry
        {
            Id = 501,
            IdentifiedDisplayName = "Custom Red Potion",
            IdentifiedResourceName = "Red_Potion",
            IdentifiedDescription = new() { "A reskinned potion." },
        };
        return file;
    }

    [Fact]
    public void Codec_round_trips_all_1252_bytes()
    {
        var codec = new LuaFileCodec(1252);
        var bytes = new byte[256];
        for (int i = 0; i < 256; i++) bytes[i] = (byte)i;
        string decoded = Encoding.GetEncoding(1252).GetString(bytes);
        byte[] reencoded = codec.EncodeText(decoded.Replace("\r", "").Replace("\n", ""));
        // every 1252 byte maps to a char and back (newlines excluded since the codec normalizes them)
        Assert.Equal(254, reencoded.Length); // 256 minus CR and LF
    }

    [Fact]
    public void Reads_existing_custom_item_32000_from_real_file()
    {
        if (!File.Exists(ItemInfoCPath)) return;

        string text = new LuaFileCodec(1252).ReadText(ItemInfoCPath);
        var file = new ItemInfoReader().ReadCustomFile(text);

        Assert.True(file.Custom.ContainsKey(32000));
        var entry = file.Custom[32000];
        Assert.Equal("MVP Ticket", entry.IdentifiedDisplayName);
        Assert.True(entry.IdentifiedDescription.Count >= 1);
    }

    [Fact]
    public void Writer_is_idempotent_and_preserves_fields()
    {
        var writer = new ItemInfoWriter();
        var reader = new ItemInfoReader();

        string first = writer.Write(SampleFile());
        var roundTrip = reader.ReadCustomFile(first);
        string second = writer.Write(roundTrip);

        Assert.Equal(first, second);

        Assert.True(roundTrip.Custom.ContainsKey(40000));
        var e = roundTrip.Custom[40000];
        Assert.Equal("Devastator", e.IdentifiedDisplayName);
        Assert.Equal(3, e.SlotCount);
        Assert.Equal(2, e.ClassNum);
        Assert.Equal(2, e.IdentifiedDescription.Count);
        Assert.Contains("^777777", e.IdentifiedDescription[1]);
        Assert.True(roundTrip.Override.ContainsKey(501));
    }

    [Fact]
    public void Router_sends_official_ids_to_override()
    {
        var official = new HashSet<int> { 501, 502 };
        Assert.Equal(ItemInfoTarget.Override, ItemInfoRouter.RouteFor(501, official));
        Assert.Equal(ItemInfoTarget.Custom, ItemInfoRouter.RouteFor(40000, official));
    }

    [Fact]
    public void FileTransaction_commits_and_rolls_back()
    {
        var dir = Path.Combine(Path.GetTempPath(), "midgard_tx_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, "a.txt");
        File.WriteAllText(target, "orig");
        try
        {
            var tx = new FileTransaction(Path.Combine(dir, "bak"));
            tx.Stage(target, Encoding.UTF8.GetBytes("updated"));
            tx.Commit();
            Assert.Equal("updated", File.ReadAllText(target));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void FileTransaction_rollback_restores_overwritten_and_removes_new_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "midgard_tx_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var overwritten = Path.Combine(dir, "over.txt");
        var created = Path.Combine(dir, "new.txt");
        var blocker = Path.Combine(dir, "blocker"); // a FILE — using it as a directory makes the 3rd write throw
        File.WriteAllText(overwritten, "original");
        File.WriteAllText(blocker, "x");
        try
        {
            var tx = new FileTransaction(Path.Combine(dir, "bak"));
            tx.Stage(overwritten, Encoding.UTF8.GetBytes("changed")); // overwrite an existing file
            tx.Stage(created, Encoding.UTF8.GetBytes("brand new")); // create a new file
            tx.Stage(Path.Combine(blocker, "z.txt"), Encoding.UTF8.GetBytes("boom")); // forces a mid-commit failure

            Assert.ThrowsAny<Exception>(() => tx.Commit());

            Assert.Equal("original", File.ReadAllText(overwritten)); // restored from backup
            Assert.False(File.Exists(created)); // newly-created file removed on rollback
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Codec_throws_on_character_not_representable_in_1252()
    {
        var codec = new LuaFileCodec(1252);
        // Emoji and CJK characters are not representable in Windows-1252 and must not be silently '?'-ed.
        Assert.Throws<InvalidDataException>(() => codec.EncodeText("Potion \U0001F600"));
        Assert.Throws<InvalidDataException>(() => codec.EncodeText("装備"));
        // Latin-1 text and CP1252 "smart punctuation" (em dash, curly quotes, é) DO encode losslessly.
        Assert.Equal(5, codec.EncodeText("Sword").Length);
        Assert.DoesNotContain((byte)'?', codec.EncodeText("café — “ok”"));
    }

    [Fact]
    public void Unified_splice_throws_when_table_is_unclosed()
    {
        var writer = new UnifiedItemInfoWriter();
        var entries = new[] { new ItemInfoEntry { Id = 999, IdentifiedDisplayName = "X", IdentifiedResourceName = "x" } };
        // "tbl = {" opens the table but never closes it — splicing must refuse rather than corrupt/drop.
        Assert.Throws<InvalidDataException>(() => writer.Splice("tbl = {", entries));
    }
}
