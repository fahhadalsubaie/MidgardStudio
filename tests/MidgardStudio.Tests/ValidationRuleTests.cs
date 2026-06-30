using MidgardStudio.Core.Lookup;
using MidgardStudio.Core.Model;
using MidgardStudio.Core.Overlay;
using MidgardStudio.Core.Schema;
using MidgardStudio.Core.Schemas;
using MidgardStudio.Core.Validation;
using MidgardStudio.Core.Workspace;

namespace MidgardStudio.Tests;

/// <summary>
/// Headless rule tests for the validation engine. Each builds an in-memory overlay + a fake reference
/// index and asserts on the stable <c>RuleId</c> + <c>Severity</c> (resilient to message wording).
/// The verified-fact edge cases (strict mob-id bounds, renewal-aware refine caps, rejection vs
/// silent-skip severities) are covered explicitly.
/// </summary>
public class ValidationRuleTests
{
    private static InMemoryReferenceIndex Refs() => new InMemoryReferenceIndex()
        .Add("item_db", "Red_Potion", "Item_A", "Knife")
        .Add("mob_db", "PORING")
        .Add("skill_db", "NV_BASIC");

    private static List<ValidationIssue> Validate(
        OverlayTable overlay, IReferenceIndex refs,
        ServerMode mode = ServerMode.Renewal, ValidationScope scope = ValidationScope.CustomOnly) =>
        ValidationEngine.CreateDefault()
            .ValidateOverlay(overlay, scope, ValidationContext.Create(refs, mode))
            .ToList();

    private static OverlayTable Overlay(DbSchema schema, params DbRecord[] customs)
    {
        var import = new DbLayer();
        foreach (var r in customs) import.Add(r);
        return new OverlayTable(schema, new DbLayer(), import, "x.yml");
    }

    private static DbRecord Item(int id, string aegis, string type = "Etc")
    {
        var r = new DbRecord(ItemDbSchema.Instance);
        r.SetRaw("Id", id);
        r.SetRaw("AegisName", aegis);
        r.SetRaw("Name", "N" + id);
        r.SetRaw("Type", type);
        return r;
    }

    private static DbRecord Mob(int id, string aegis = "TEST_MOB")
    {
        var r = new DbRecord(MobDbSchema.Instance);
        r.SetRaw("Id", id);
        r.SetRaw("AegisName", aegis);
        r.SetRaw("Name", "Mob " + id);
        r.SetRaw("Level", 10);
        return r;
    }

    private static bool Has(IEnumerable<ValidationIssue> issues, string ruleId, ValidationSeverity severity) =>
        issues.Any(i => i.RuleId == ruleId && i.Severity == severity);

    // ---- Mob ID range (strict inequalities) ----

    [Theory]
    [InlineData(1000)]
    [InlineData(3999)]
    [InlineData(20020)]
    [InlineData(31999)]
    public void MobId_boundary_values_are_invalid(int id)
    {
        var issues = Validate(Overlay(MobDbSchema.Instance, Mob(id)), Refs());
        Assert.True(Has(issues, "MOB.ID_RANGE", ValidationSeverity.Error), $"expected {id} to be flagged");
    }

    [Theory]
    [InlineData(1001)]
    [InlineData(3998)]
    [InlineData(20021)]
    [InlineData(31998)]
    public void MobId_in_range_values_are_valid(int id)
    {
        var issues = Validate(Overlay(MobDbSchema.Instance, Mob(id)), Refs());
        Assert.DoesNotContain(issues, i => i.RuleId == "MOB.ID_RANGE");
    }

    // ---- Renewal-aware refine caps ----

    [Fact]
    public void WeaponLevel_5_is_valid_in_renewal_invalid_in_prerenewal()
    {
        DbRecord Weapon()
        {
            var w = Item(30000, "Test_Weapon", "Weapon");
            w.SetRaw("WeaponLevel", 5);
            w.SetRaw("Locations", new HashSet<string> { "Right_Hand" });
            return w;
        }

        var renewal = Validate(Overlay(ItemDbSchema.Instance, Weapon()), Refs(), ServerMode.Renewal);
        Assert.DoesNotContain(renewal, i => i.RuleId == "ITEM.WEAPONLVL_RANGE");

        var preRe = Validate(Overlay(ItemDbSchema.Instance, Weapon()), Refs(), ServerMode.PreRenewal);
        Assert.True(Has(preRe, "ITEM.WEAPONLVL_RANGE", ValidationSeverity.Warning));
    }

    // ---- Numeric bounds (data-driven from Min/Max) ----

    [Fact]
    public void Slots_above_four_is_flagged()
    {
        // Slots is equip-only now, so use an equippable item (else the bounds check is skipped as not-applicable).
        var item = Item(30000, "Slotted", "Armor");
        item.SetRaw("Locations", new HashSet<string> { "Armor" });
        item.SetRaw("Slots", 5);
        var issues = Validate(Overlay(ItemDbSchema.Instance, item), Refs());
        Assert.True(Has(issues, "FIELD.BOUNDS", ValidationSeverity.Warning));
        Assert.Contains(issues, i => i.RuleId == "FIELD.BOUNDS" && i.Fix is not null); // clamp quick-fix offered
    }

    // ---- Enum membership ----

    [Fact]
    public void Unrecognized_enum_value_is_a_warning()
    {
        var item = Item(30000, "Weird", "Bogus"); // not in the curated set and not in (empty) base data
        var issues = Validate(Overlay(ItemDbSchema.Instance, item), Refs());
        Assert.True(Has(issues, "FIELD.ENUM_INVALID", ValidationSeverity.Warning));
    }

    [Fact]
    public void Enum_value_used_in_base_data_is_not_flagged()
    {
        // The curated achievement Group list is NOT exhaustive (e.g. "Spend_Zeny"). A value that the
        // authoritative base data actually uses must be accepted, never flagged.
        var baseLayer = new DbLayer();
        var baseAch = new DbRecord(AchievementDbSchema.Instance);
        baseAch.SetRaw("Id", 1);
        baseAch.SetRaw("Group", "Spend_Zeny");
        baseLayer.Add(baseAch);

        var custom = new DbRecord(AchievementDbSchema.Instance);
        custom.SetRaw("Id", 900000);
        custom.SetRaw("Group", "Spend_Zeny");
        var import = new DbLayer();
        import.Add(custom);

        var overlay = new OverlayTable(AchievementDbSchema.Instance, baseLayer, import, "x.yml");
        var issues = Validate(overlay, Refs());
        Assert.DoesNotContain(issues, i => i.RuleId == "FIELD.ENUM_INVALID");
    }

    [Fact]
    public void Flag_value_used_in_base_data_is_not_flagged()
    {
        // "Both_Accessory" is a valid item Location alias missing from the curated list but present in base data.
        var baseLayer = new DbLayer();
        var baseItem = Item(2000, "Base_Ring", "Armor");
        baseItem.SetRaw("Locations", new HashSet<string> { "Both_Accessory" });
        baseLayer.Add(baseItem);

        var custom = Item(30000, "Custom_Ring", "Armor");
        custom.SetRaw("Locations", new HashSet<string> { "Both_Accessory" });
        var import = new DbLayer();
        import.Add(custom);

        var overlay = new OverlayTable(ItemDbSchema.Instance, baseLayer, import, "x.yml");
        var issues = Validate(overlay, Refs());
        Assert.DoesNotContain(issues, i => i.RuleId == "FIELD.FLAG_INVALID");
    }

    // ---- Item structural / consistency ----

    [Fact]
    public void Equip_type_without_location_is_an_error()
    {
        var armor = Item(30000, "Test_Armor", "Armor"); // no Locations
        var issues = Validate(Overlay(ItemDbSchema.Instance, armor), Refs());
        Assert.True(Has(issues, "ITEM.EQUIP_NO_LOC", ValidationSeverity.Error));
    }

    [Fact]
    public void Weapon_subtype_on_ammo_item_is_flagged()
    {
        var ammo = Item(30000, "Bad_Ammo", "Ammo");
        ammo.SetRaw("SubType", "1hSword"); // a weapon subtype on an ammo item -> AMMO_NONE on the server
        var issues = Validate(Overlay(ItemDbSchema.Instance, ammo), Refs());
        Assert.True(Has(issues, "ITEM.SUBTYPE_TYPE_MISMATCH", ValidationSeverity.Warning));
    }

    [Fact]
    public void Valid_subtype_for_type_is_not_flagged()
    {
        var ammo = Item(30000, "Good_Ammo", "Ammo");
        ammo.SetRaw("SubType", "Arrow"); // AMMO_ARROW — valid
        var weapon = Item(30001, "Good_Weapon", "Weapon");
        weapon.SetRaw("SubType", "2hMace"); // W_2HMACE — valid (newly added)
        weapon.SetRaw("Locations", new HashSet<string> { "Right_Hand" });
        var issues = Validate(Overlay(ItemDbSchema.Instance, ammo, weapon), Refs());
        Assert.DoesNotContain(issues, i => i.RuleId == "ITEM.SUBTYPE_TYPE_MISMATCH");
    }

    [Fact]
    public void Equip_level_min_above_max_is_an_error()
    {
        var item = Item(30000, "Lvl");
        item.SetRaw("EquipLevelMin", 100);
        item.SetRaw("EquipLevelMax", 50);
        var issues = Validate(Overlay(ItemDbSchema.Instance, item), Refs());
        Assert.True(Has(issues, "ITEM.LEVEL_ORDER", ValidationSeverity.Error));
    }

    [Fact]
    public void Aegis_name_over_max_length_is_an_error()
    {
        var item = Item(30000, new string('A', 60));
        var issues = Validate(Overlay(ItemDbSchema.Instance, item), Refs());
        Assert.True(Has(issues, "FIELD.MAXLENGTH", ValidationSeverity.Error));
    }

    [Fact]
    public void Missing_aegis_name_is_required_error()
    {
        var item = Item(30000, "");
        var issues = Validate(Overlay(ItemDbSchema.Instance, item), Refs());
        Assert.Contains(issues, i => i.RuleId == "FIELD.REQUIRED" && i.Field == "AegisName"
            && i.Severity == ValidationSeverity.Error);
    }

    // ---- Duplicate AegisName (case-insensitive) ----

    [Fact]
    public void Duplicate_aegis_name_case_insensitive_is_an_error()
    {
        var overlay = Overlay(ItemDbSchema.Instance, Item(30000, "Dup_Item"), Item(30001, "DUP_ITEM"));
        var issues = Validate(overlay, Refs());
        Assert.True(Has(issues, "DUP.AEGISNAME", ValidationSeverity.Error));
    }

    // ---- Cross-reference severities mirror rAthena ----

    [Fact]
    public void Missing_drop_item_is_a_warning_silent_skip()
    {
        var drop = new DbRecord(MobDbSchema.DropElement);
        drop.SetRaw("Item", "NoSuch_Item");
        drop.SetRaw("Rate", 100);
        var mob = Mob(1500);
        mob.SetRaw("Drops", new List<DbRecord> { drop });

        var issues = Validate(Overlay(MobDbSchema.Instance, mob), Refs());
        Assert.Contains(issues, i => i.RuleId == "XREF.REFERENCE_MISSING"
            && i.Severity == ValidationSeverity.Warning);
    }

    [Fact]
    public void Missing_alias_is_an_error_record_rejected()
    {
        var item = Item(30000, "Has_Alias");
        item.SetRaw("AliasName", "NoSuch_Item");
        var issues = Validate(Overlay(ItemDbSchema.Instance, item), Refs());
        Assert.Contains(issues, i => i.RuleId == "XREF.REFERENCE_MISSING"
            && i.Field == "AliasName" && i.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void Reference_to_unloaded_database_is_not_flagged()
    {
        var item = Item(30000, "Has_Alias");
        item.SetRaw("AliasName", "Whatever");
        var emptyRefs = new InMemoryReferenceIndex(); // does not know item_db
        var issues = Validate(Overlay(ItemDbSchema.Instance, item), emptyRefs);
        Assert.DoesNotContain(issues, i => i.RuleId == "XREF.REFERENCE_MISSING");
    }

    // ---- Item combos ----

    [Fact]
    public void Combo_with_fewer_than_two_members_is_an_error()
    {
        var issues = Validate(Overlay(ItemComboSchema.Instance, Combo("Item_A")), Refs());
        Assert.True(Has(issues, "XREF.COMBO_MIN_MEMBERS", ValidationSeverity.Error));
    }

    [Fact]
    public void Combo_with_missing_member_is_an_error()
    {
        var issues = Validate(Overlay(ItemComboSchema.Instance, Combo("Item_A", "NoSuch_Item")), Refs());
        Assert.True(Has(issues, "XREF.COMBO_MEMBER_MISSING", ValidationSeverity.Error));
    }

    private static DbRecord Combo(params string[] members)
    {
        var entrySchema = ItemComboSchema.Instance.Field("Combos")!.ObjectSchema!;
        var entry = new DbRecord(entrySchema);
        entry.SetRaw("Combo", members.Cast<object>().ToList());
        var combo = new DbRecord(ItemComboSchema.Instance);
        combo.SetRaw("Combos", new List<DbRecord> { entry });
        return combo;
    }

    // ---- Sanity: a clean custom item produces no errors ----

    [Fact]
    public void Valid_custom_item_has_no_errors()
    {
        var issues = Validate(Overlay(ItemDbSchema.Instance, Item(30000, "Valid_Item")), Refs());
        Assert.DoesNotContain(issues, i => i.Severity == ValidationSeverity.Error);
    }

    // ---- LevelList renders its value, not its type name ----

    [Fact]
    public void LevelList_ToString_is_the_value_not_the_type_name()
    {
        Assert.Equal("100", LevelList.FromScalar(100).ToString());
        Assert.Equal(string.Empty, new LevelList().ToString());
        Assert.Equal("1:3, 2:5", LevelList.Parse("1:3, 2:5").ToString());
    }

    // ---- Renewal-only field on official base data is not nagged (FixedCastTime) ----

    [Fact]
    public void RenewalOnly_field_warns_for_custom_but_not_base_in_prerenewal()
    {
        DbRecord Skill(int id, string name)
        {
            var s = new DbRecord(SkillDbSchema.Instance);
            s.SetRaw("Id", id);
            s.SetRaw("Name", name);
            s.SetRaw("FixedCastTime", LevelList.FromScalar(100)); // RenewalOnly field
            return s;
        }

        var baseLayer = new DbLayer();
        baseLayer.Add(Skill(1, "BASE_SKILL"));
        var import = new DbLayer();
        import.Add(Skill(900001, "CUSTOM_SKILL"));
        var overlay = new OverlayTable(SkillDbSchema.Instance, baseLayer, import, "x.yml");

        var issues = ValidationEngine.CreateDefault()
            .ValidateOverlay(overlay, ValidationScope.FullScan,
                ValidationContext.Create(new InMemoryReferenceIndex(), ServerMode.PreRenewal))
            .ToList();

        Assert.DoesNotContain(issues, i => i.RuleId == "FIELD.RENEWAL_MISMATCH" && i.Key == "1");        // base: silent
        Assert.Contains(issues, i => i.RuleId == "FIELD.RENEWAL_MISMATCH" && i.Key == "900001");          // custom: informed
    }
}
