using MidgardStudio.Core.Serialization;

namespace MidgardStudio.Tests;

/// <summary>The comment-preserving import merge (do-not-clobber a hand-documented import file on save).</summary>
public class YamlBodyMergeTests
{
    private const string Canonical = """
        Header:
          Type: MOB_AVAIL_DB
          Version: 1
        Body:
          - Mob: PORING
            Sprite: BAPHOMET
        """;

    // A file like the shipped import/mob_avail.yml: banner + field docs + commented-out examples, no real Body.
    private const string DocumentedNoBody = """
        # This file is a part of rAthena.
        ###########################################################################
        # - Mob                     Mob to adjust.
        #   Sprite                  Sprite sent to the client instead of Mob.
        ###########################################################################

        Header:
          Type: MOB_AVAIL_DB
          Version: 1

        #Body:
          # Examples
        #  - Mob: E_OBEAUNE
        #    Sprite: PORING
        #    PetEquip: Backpack
        """;

    [Fact]
    public void Appends_body_keeping_the_banner_and_commented_examples()
    {
        string merged = YamlBodyMerge.Merge(DocumentedNoBody, Canonical);

        // Banner + field docs + commented examples all survive…
        Assert.Contains("# This file is a part of rAthena.", merged);
        Assert.Contains("#   Sprite                  Sprite sent to the client instead of Mob.", merged);
        Assert.Contains("#    Sprite: PORING", merged);
        Assert.Contains("#    PetEquip: Backpack", merged);
        // …and the new active entry is appended as a real Body.
        Assert.Contains("Body:\n  - Mob: PORING\n    Sprite: BAPHOMET", merged.Replace("\r\n", "\n"));
    }

    [Fact]
    public void Replaces_an_existing_real_body_without_touching_the_banner()
    {
        string original = """
            # My custom mob_avail notes — keep these!
            Header:
              Type: MOB_AVAIL_DB
              Version: 1
            Body:
              - Mob: OLD_MOB
                Sprite: OLD_SPRITE
            """;

        string merged = YamlBodyMerge.Merge(original, Canonical).Replace("\r\n", "\n");

        Assert.Contains("# My custom mob_avail notes — keep these!", merged);
        Assert.DoesNotContain("OLD_MOB", merged);   // old body replaced
        Assert.DoesNotContain("OLD_SPRITE", merged);
        Assert.Contains("- Mob: PORING", merged);
    }

    [Fact]
    public void Is_stable_across_a_second_merge()
    {
        // First save appends a Body; a second save (the file now HAS a real Body) replaces it, and the
        // commented examples are still preserved — no duplicate/runaway Body.
        string once = YamlBodyMerge.Merge(DocumentedNoBody, Canonical);
        string twice = YamlBodyMerge.Merge(once, Canonical).Replace("\r\n", "\n");

        Assert.Contains("#    PetEquip: Backpack", twice);              // examples still there
        Assert.Equal(1, Count(twice, "- Mob: PORING"));                // no runaway duplication
        Assert.Equal(1, Count(twice, "\nBody:\n"));                    // exactly one active Body block
    }

    [Fact]
    public void Empty_body_leaves_a_documented_file_untouched()
    {
        string emptyCanonical = "Header:\n  Type: MOB_AVAIL_DB\n  Version: 1\nBody: []";
        Assert.Equal(DocumentedNoBody, YamlBodyMerge.Merge(DocumentedNoBody, emptyCanonical));
    }

    [Fact]
    public void Falls_back_to_canonical_for_a_non_db_or_missing_original()
    {
        Assert.Equal(Canonical, YamlBodyMerge.Merge(null, Canonical));
        Assert.Equal(Canonical, YamlBodyMerge.Merge("", Canonical));
        Assert.Equal(Canonical, YamlBodyMerge.Merge("just some text with no header", Canonical));
    }

    [Fact]
    public void Preserves_comments_inside_the_body_between_entries() // audit #1 (status.yml Kyoshio notes)
    {
        string original = """
            Header:
              Type: STATUS_DB
              Version: 4
            Body:
              # VIP membership cosmetic icon (Kyoshio)
              - Status: Vipstate
                Icon: EFST_VIP
              # Auto-Battle ON cosmetic icon (Kyoshio)
              - Status: Autoattack_Icon
                Icon: EFST_AUTO
            """;

        // a regenerated Body that edited Vipstate's Icon (the entry set is unchanged)
        string canonical = """
            Header:
              Type: STATUS_DB
              Version: 4
            Body:
              - Status: Vipstate
                Icon: EFST_VIP_NEW
              - Status: Autoattack_Icon
                Icon: EFST_AUTO
            """;

        string merged = YamlBodyMerge.Merge(original, canonical).Replace("\r\n", "\n");

        Assert.Contains("Icon: EFST_VIP_NEW", merged);                       // the edit landed
        // both hand comments survive, each still above its own entry
        Assert.Contains("# VIP membership cosmetic icon (Kyoshio)\n  - Status: Vipstate", merged);
        Assert.Contains("# Auto-Battle ON cosmetic icon (Kyoshio)\n  - Status: Autoattack_Icon", merged);
    }

    [Fact]
    public void Refuses_to_merge_when_the_file_declares_a_different_db_type() // audit #8
    {
        string mobDbFile = "Header:\n  Type: MOB_DB\n  Version: 5\nBody:\n  - Id: 1002\n    AegisName: PORING\n";
        string itemCanonical = "Header:\n  Type: ITEM_DB\n  Version: 3\nBody:\n  - Id: 501\n    AegisName: Red_Potion\n";

        // Saving ITEM_DB data over a MOB_DB file would make it self-inconsistent — must refuse, not overwrite.
        Assert.Throws<System.IO.InvalidDataException>(() => YamlBodyMerge.Merge(mobDbFile, itemCanonical));

        // Matching types (and headerless imports) still merge fine.
        Assert.Contains("- Id: 501", YamlBodyMerge.Merge(
            "Header:\n  Type: ITEM_DB\n  Version: 3\nBody:\n  - Id: 909\n    AegisName: Jellopy\n", itemCanonical).Replace("\r\n", "\n"));
    }

    private static int Count(string haystack, string needle)
    {
        int n = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, System.StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }
}
