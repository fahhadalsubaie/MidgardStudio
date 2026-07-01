using System.Collections.Generic;
using MidgardStudio.Core.Sprites;

namespace MidgardStudio.Tests;

/// <summary>The pure registration math (disk ∪ pending) used by the deferred sprite-registration services.</summary>
public class SpriteRegistryTests
{
    private static Dictionary<string, int> Disk(params (string Name, int Id)[] entries)
    {
        var d = new Dictionary<string, int>(System.StringComparer.Ordinal);
        foreach (var (n, i) in entries) d[n] = i;
        return d;
    }

    [Fact]
    public void NextFreeId_is_one_when_nothing_exists() =>
        Assert.Equal(1, SpriteRegistry.NextFreeId(Disk(), new List<PendingRegistration>()));

    [Fact]
    public void NextFreeId_follows_the_highest_disk_value() =>
        Assert.Equal(6, SpriteRegistry.NextFreeId(Disk(("A", 5), ("B", 3)), new List<PendingRegistration>()));

    [Fact]
    public void NextFreeId_accounts_for_pending_so_two_links_dont_collide()
    {
        var disk = Disk(("A", 5));
        var pending = new List<PendingRegistration> { new("B", 6, "_b") };
        Assert.Equal(7, SpriteRegistry.NextFreeId(disk, pending));
    }

    [Fact]
    public void NextFreeId_uses_pending_even_when_disk_is_empty() =>
        Assert.Equal(4, SpriteRegistry.NextFreeId(Disk(), new List<PendingRegistration> { new("X", 3, "_x") }));

    [Fact]
    public void RegisteredIds_is_disk_union_pending()
    {
        var ids = SpriteRegistry.RegisteredIds(Disk(("A", 5), ("B", 6)), new List<PendingRegistration> { new("C", 9, "_c") });
        Assert.Equal(new HashSet<int> { 5, 6, 9 }, ids);
    }

    [Fact]
    public void HasConstant_sees_both_disk_and_pending()
    {
        var disk = Disk(("ACCESSORY_HAT", 1));
        var pending = new List<PendingRegistration> { new("ACCESSORY_CAPE", 2, "_cape") };
        Assert.True(SpriteRegistry.HasConstant(disk, pending, "ACCESSORY_HAT"));
        Assert.True(SpriteRegistry.HasConstant(disk, pending, "ACCESSORY_CAPE"));
        Assert.False(SpriteRegistry.HasConstant(disk, pending, "ACCESSORY_NEW"));
    }

    // ---- FindId: reuse an already-registered sprite's View id instead of registering a duplicate ----

    private static Dictionary<string, string> Names(params (string Const, string Sprite)[] entries)
    {
        var d = new Dictionary<string, string>(System.StringComparer.Ordinal);
        foreach (var (c, s) in entries) d[c] = s;
        return d;
    }

    [Fact]
    public void FindId_returns_the_mapped_view_when_the_sprite_is_already_on_disk()
    {
        var constants = Disk(("ACCESSORY_HAT", 7), ("ACCESSORY_CAPE", 8));
        var names = Names(("ACCESSORY_HAT", "_hat"), ("ACCESSORY_CAPE", "_cape"));
        Assert.Equal(8, SpriteRegistry.FindId(constants, names, new List<PendingRegistration>(), "_cape"));
    }

    [Fact]
    public void FindId_is_null_for_a_sprite_that_isnt_registered_yet() =>
        Assert.Null(SpriteRegistry.FindId(Disk(("ACCESSORY_HAT", 7)), Names(("ACCESSORY_HAT", "_hat")),
            new List<PendingRegistration>(), "_brandnew"));

    [Fact]
    public void FindId_matches_case_insensitively_like_the_client_file_lookup()
    {
        var constants = Disk(("ACCESSORY_HAT", 7));
        var names = Names(("ACCESSORY_HAT", "_Hat"));
        Assert.Equal(7, SpriteRegistry.FindId(constants, names, new List<PendingRegistration>(), "_hat"));
    }

    [Fact]
    public void FindId_prefers_a_pending_link_over_disk()
    {
        var constants = Disk(("ACCESSORY_HAT", 7));
        var names = Names(("ACCESSORY_HAT", "_hat"));
        var pending = new List<PendingRegistration> { new("ACCESSORY_NEW", 50, "_hat") };
        Assert.Equal(50, SpriteRegistry.FindId(constants, names, pending, "_hat"));
    }

    [Fact]
    public void FindId_ignores_an_accname_entry_whose_constant_has_no_id()
    {
        // accname lists the sprite but accessoryid has no matching constant -> not resolvable, treat as absent.
        var names = Names(("ACCESSORY_ORPHAN", "_orphan"));
        Assert.Null(SpriteRegistry.FindId(Disk(), names, new List<PendingRegistration>(), "_orphan"));
    }

    [Fact]
    public void FindId_is_deterministic_when_a_sprite_maps_to_several_constants()
    {
        // Real data has ~23 sprites shared by 2+ constants; all render the same art, so any id works in-game,
        // but the lookup must be deterministic (lowest id) rather than dictionary-order dependent.
        var constants = Disk(("ACCESSORY_EAR_HI", 295), ("ACCESSORY_EAR_LO", 73), ("ACCESSORY_EAR_MID", 292));
        var names = Names(("ACCESSORY_EAR_HI", "_ear"), ("ACCESSORY_EAR_LO", "_ear"), ("ACCESSORY_EAR_MID", "_ear"));
        Assert.Equal(73, SpriteRegistry.FindId(constants, names, new List<PendingRegistration>(), "_ear"));
    }
}
