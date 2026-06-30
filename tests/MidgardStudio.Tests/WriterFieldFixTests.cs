using System;
using System.Linq;
using MidgardStudio.Core.Model;
using MidgardStudio.Core.Schema;
using MidgardStudio.Core.Schemas;
using MidgardStudio.Core.Serialization;

namespace MidgardStudio.Tests;

/// <summary>
/// Guards for the 2026-06-30 writer field/property audit: schema enum-name correctness (values must resolve
/// to a real rAthena constant), the new modeled fields, and the numeric-type widenings. These catch the exact
/// class of "silently ships a value the server rejects" bug the audit found.
/// </summary>
public class WriterFieldFixTests
{
    // ---- #1 DropEffect: the colored pillars need the _Pillar suffix or they resolve to NONE ----

    [Fact]
    public void DropEffect_colors_carry_the_pillar_suffix()
    {
        var values = ItemEnums.DropEffect.Values;
        Assert.Contains("White_Pillar", values);
        Assert.Contains("Red_Pillar", values);
        Assert.DoesNotContain("White", values); // the bare color name silently became DROPEFFECT_NONE
        foreach (var v in values)
            Assert.True(v is "None" or "Client" || v.EndsWith("_Pillar", StringComparison.Ordinal),
                $"DropEffect value '{v}' is neither None/Client nor a *_Pillar constant.");
    }

    // ---- #4 / #16 mob Modes: NoRandomWalk (not NoRandom) + NoCast ----

    [Fact]
    public void MobModes_uses_NoRandomWalk_and_includes_NoCast()
    {
        var modes = CommonEnums.MobModes.Values;
        Assert.Contains("NoRandomWalk", modes); // MD_NORANDOMWALK
        Assert.Contains("NoCast", modes);        // MD_NOCAST (was missing entirely)
        Assert.DoesNotContain("NoRandom", modes); // there is no MD_NORANDOM
    }

    // ---- #5 mob Race: the two player races, not the non-existent RC_PLAYER ----

    [Fact]
    public void MobRace_has_both_player_races_and_not_bare_Player()
    {
        var races = CommonEnums.MobRace.Values;
        Assert.Contains("Player_Human", races);
        Assert.Contains("Player_Doram", races);
        Assert.DoesNotContain("Player", races); // RC_PLAYER does not exist -> defaults to Formless
    }

    // ---- #17 / #18 mob RaceGroups: drop the deprecated pair, cover all 40 valid RC2_* ----

    [Fact]
    public void RaceGroups_drops_deprecated_and_covers_all_valid_groups()
    {
        var groups = CommonEnums.RaceGroups.Values;
        Assert.DoesNotContain("Guardian", groups);     // deprecated RC2_GUARDIAN (use Class=Guardian)
        Assert.DoesNotContain("Battlefield", groups);  // deprecated RC2_BATTLEFIELD
        Assert.Contains("Bio5_Mvp", groups);
        Assert.Contains("Clocktower", groups);
        Assert.Contains("Glast_Heim_Abyss", groups);
        Assert.Equal(40, groups.Count); // the 40 exported, non-deprecated RC2_* constants
        Assert.Equal(groups.Count, groups.Distinct().Count()); // no duplicate tokens
    }

    // ---- #11 / #30 item enums: aggregate locations + two-handed mace ----

    [Fact]
    public void Item_locations_and_subtype_cover_the_missing_tokens()
    {
        Assert.Contains("Both_Hand", ItemEnums.Locations.Values);       // EQP_ARMS (1135 base entries)
        Assert.Contains("Both_Accessory", ItemEnums.Locations.Values);  // EQP_ACC_RL (864 base entries)
        Assert.Contains("2hMace", ItemEnums.SubType.Values);            // W_2HMACE
        // Every SubType value still resolves to a friendly label (no duplicate-key crash on static init).
        foreach (var v in ItemEnums.SubType.Values)
            Assert.False(string.IsNullOrEmpty(ItemEnums.SubType.Label(v)));
    }

    // ---- #19 / #34 mob_summon Group: a closed enum, not free text ----

    [Fact]
    public void MobSummon_group_is_a_closed_six_member_enum()
    {
        var group = MobSummonSchema.Instance.Field("Group");
        Assert.NotNull(group);
        Assert.Equal(FieldKind.Enum, group!.Kind);
        Assert.NotNull(group.Enum);
        Assert.Equal(6, group.Enum!.Values.Count); // MOBG_MAX is a 6-member compile-time enum
        Assert.Contains("BLOODY_DEAD_BRANCH", group.Enum.Values);
        Assert.Contains("CLASSCHANGE", group.Enum.Values);
    }

    // ---- #29 item NoUse/Trade Override: bounded 0..100 ----

    [Fact]
    public void Override_fields_are_bounded_to_100()
    {
        var noUse = ItemDbSchema.NoUse.Field("Override");
        var trade = ItemDbSchema.Trade.Field("Override");
        Assert.Equal((0, 100), (noUse!.Min, noUse.Max));
        Assert.Equal((0, 100), (trade!.Min, trade.Max));
    }

    // ---- #32 mob EXP: a value above int32 must round-trip as a long ----

    [Fact]
    public void Mob_exp_above_int32_round_trips_as_long()
    {
        var schema = MobDbSchema.Instance;
        Assert.Equal(FieldKind.Long, schema.Field("MvpExp")!.Kind);

        var mob = new DbRecord(schema);
        mob.SetRaw("Id", 20021);
        mob.SetRaw("AegisName", "BIG_MVP");
        mob.SetRaw("Name", "Big MVP");
        const long big = 5_000_000_000L; // > int.MaxValue (renewal MAX_EXP is INT64_MAX)
        mob.SetRaw("MvpExp", big);

        var file = new DbFile { HeaderType = "MOB_DB", HeaderVersion = 5 };
        file.Records.Add(mob);
        string yaml = new YamlDbWriter().WriteToString(schema, file);
        Assert.Contains("MvpExp: 5000000000", yaml);

        var back = new YamlDbReader().Read(yaml, schema).Records.Single();
        Assert.Equal(big, back.GetLong("MvpExp"));
    }

    // ---- #33 mob GroupId + Title: modeled and round-tripping ----

    [Fact]
    public void Mob_groupid_and_title_round_trip()
    {
        var schema = MobDbSchema.Instance;
        var mob = new DbRecord(schema);
        mob.SetRaw("Id", 20021);
        mob.SetRaw("AegisName", "TITLED");
        mob.SetRaw("Name", "Titled");
        mob.SetRaw("GroupId", 1234);
        mob.SetRaw("Title", "<Red Pepper>");

        var file = new DbFile { HeaderType = "MOB_DB", HeaderVersion = 5 };
        file.Records.Add(mob);
        string yaml = new YamlDbWriter().WriteToString(schema, file);
        Assert.Contains("GroupId:", yaml);
        Assert.Contains("Title:", yaml);

        var back = new YamlDbReader().Read(yaml, schema).Records.Single();
        Assert.Equal(1234, back.GetInt("GroupId"));
        Assert.Equal("<Red Pepper>", back.GetString("Title"));
    }

    // ---- SubType dropdown is filtered to the selected Type's own set ----

    [Fact]
    public void SubType_options_are_filtered_by_type()
    {
        var sub = ItemDbSchema.Instance.Field("SubType")!;
        Assert.NotNull(sub.EnumSelector);
        DbRecord Of(string t) { var r = new DbRecord(ItemDbSchema.Instance); r.SetRaw("Type", t); return r; }

        Assert.Same(ItemEnums.WeaponSubType, sub.EnumSelector!(Of("Weapon")));
        Assert.Same(ItemEnums.AmmoSubType, sub.EnumSelector!(Of("Ammo")));
        Assert.Same(ItemEnums.CardSubType, sub.EnumSelector!(Of("Card")));

        Assert.Contains("2hMace", ItemEnums.WeaponSubType.Values);   // weapon-only set
        Assert.DoesNotContain("Normal", ItemEnums.WeaponSubType.Values); // not the card value
        Assert.DoesNotContain("Arrow", ItemEnums.WeaponSubType.Values);  // not the ammo value
        Assert.Equal(new[] { "Normal", "Enchant" }, ItemEnums.CardSubType.Values);
    }

    // ---- studio conditional fields: nested flag groups are Type-gated; SubType resets on Type change ----

    [Fact]
    public void Nested_flag_groups_are_type_gated_and_subtype_resets_on_type()
    {
        var schema = ItemDbSchema.Instance;
        DbRecord Of(string type) { var r = new DbRecord(schema); r.SetRaw("Type", type); return r; }

        var delay = schema.Field("Delay")!;
        Assert.True(delay.IsApplicable!(Of("Usable")));   // use-cooldown applies to consumables
        Assert.False(delay.IsApplicable!(Of("Weapon")));  // not equipment

        var noUse = schema.Field("NoUse")!;
        Assert.True(noUse.IsApplicable!(Of("Healing")));
        Assert.False(noUse.IsApplicable!(Of("Armor")));

        var stack = schema.Field("Stack")!;
        Assert.True(stack.IsApplicable!(Of("Etc")));      // non-equipment stacks
        Assert.False(stack.IsApplicable!(Of("ShadowGear")));

        // SubType is cleared when Type changes (a weapon subtype is invalid once the item is a card).
        Assert.Equal("Type", schema.Field("SubType")!.ResetOnChangeOf);
    }

    // ---- write-safety regression guard: promoting Title from Extras to a modeled field must not drop the
    // record's still-unmodeled fields, and the output must stay idempotent (no save-churn). ----

    [Fact]
    public void Promoting_title_to_modeled_keeps_unmodeled_fields_and_is_idempotent()
    {
        var schema = MobDbSchema.Instance;
        var mob = new DbRecord(schema);
        mob.SetRaw("Id", 20021);
        mob.SetRaw("AegisName", "TITLED");
        mob.SetRaw("Name", "Titled");
        mob.SetRaw("Title", "<Red Pepper>");      // now a modeled field
        mob.Extras["SomeFutureField"] = "keepme"; // a still-unmodeled key the editor must preserve

        var file = new DbFile { HeaderType = "MOB_DB", HeaderVersion = 5 };
        file.Records.Add(mob);
        var writer = new YamlDbWriter();
        string once = writer.WriteToString(schema, file);

        Assert.Contains("Title:", once);
        Assert.Contains("SomeFutureField: keepme", once); // unmodeled field survives alongside the promoted one

        // read -> write yields byte-identical text: no infinite reorder churn on save.
        var reread = new YamlDbReader().Read(once, schema);
        Assert.Equal(once, writer.WriteToString(schema, reread));

        var back = reread.Records.Single();
        Assert.Equal("<Red Pepper>", back.GetString("Title"));
        Assert.Equal("keepme", back.Extras["SomeFutureField"]); // not duplicated into Extras AND modeled — exactly one home
    }
}
