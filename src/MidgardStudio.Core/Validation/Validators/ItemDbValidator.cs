using MidgardStudio.Core.Model;
using MidgardStudio.Core.Overlay;
using MidgardStudio.Core.Workspace;

namespace MidgardStudio.Core.Validation.Validators;

/// <summary>
/// Bespoke item_db rules that the generic schema-driven validator cannot express: equip-location
/// consistency, level ordering, renewal-aware refine caps, and the custom-id convention.
/// </summary>
public sealed class ItemDbValidator : IRecordValidator
{
    // Equip types rAthena forces to Etc when they carry no equip Location (the item becomes un-equippable).
    private static readonly HashSet<string> EquipTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Weapon", "Armor", "ShadowGear",
    };

    // SubType is one combined dropdown but rAthena validates it per Type: W_<value> for Weapon, AMMO_<value>
    // for Ammo, CARD_<value> for Card. A weapon subtype on an Ammo/Card item fails the per-type lookup and
    // silently defaults (W_FIST / AMMO_NONE / CARD_NORMAL). These sets mirror the exported W_/AMMO_/CARD_
    // constants; membership is case-insensitive (the server uses strcasecmp).
    private static readonly HashSet<string> WeaponSubTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Fist", "Dagger", "1hSword", "2hSword", "1hSpear", "2hSpear", "1hAxe", "2hAxe", "Mace", "2hMace",
        "Staff", "2hStaff", "Bow", "Knuckle", "Musical", "Whip", "Book", "Katar", "Revolver", "Rifle",
        "Gatling", "Shotgun", "Grenade", "Huuma",
    };

    private static readonly HashSet<string> AmmoSubTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Arrow", "Dagger", "Bullet", "Shell", "Grenade", "Shuriken", "Kunai", "CannonBall", "ThrowWeapon",
    };

    private static readonly HashSet<string> CardSubTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Normal", "Enchant",
    };

    public bool AppliesTo(string dbId) => dbId == "item_db";

    public IEnumerable<ValidationIssue> Validate(DbRecord record, OverlayTable table, ValidationContext context)
    {
        string key = record.Key.ToString();
        string? type = record.GetString("Type");

        // SubType must belong to the set valid for the selected Type — the combined dropdown lets a weapon
        // subtype land on an Ammo/Card item, where rAthena silently coerces it to the type default.
        string? subType = record.GetString("SubType");
        if (!string.IsNullOrWhiteSpace(subType) && type is "Weapon" or "Ammo" or "Card")
        {
            var valid = type switch
            {
                "Weapon" => WeaponSubTypes,
                "Ammo" => AmmoSubTypes,
                _ => CardSubTypes,
            };
            if (!valid.Contains(subType))
                yield return new ValidationIssue(ValidationSeverity.Warning, "item_db", key, "SubType",
                    $"Sub Type '{subType}' is not a valid {type} subtype — the server will ignore it and use the {type} default.")
                { RuleId = "ITEM.SUBTYPE_TYPE_MISMATCH" };
        }

        // Equip-type item with no Location → rAthena reverts it to Etc and it can't be worn.
        if (type is not null && EquipTypes.Contains(type) && (record.GetSet("Locations")?.Count ?? 0) == 0)
            yield return new ValidationIssue(ValidationSeverity.Error, "item_db", key, "Locations",
                $"A {type} with no Location is forced to Etc by rAthena and becomes un-equippable. Set a Location.")
            { RuleId = "ITEM.EQUIP_NO_LOC" };

        // Equip level ordering.
        int min = record.GetInt("EquipLevelMin");
        int max = record.GetInt("EquipLevelMax");
        if (max > 0 && min > max)
            yield return new ValidationIssue(ValidationSeverity.Error, "item_db", key, "EquipLevelMin",
                $"Equip Level Min ({min}) must be ≤ Max ({max}).")
            {
                RuleId = "ITEM.LEVEL_ORDER",
                Fix = record.Origin == RecordOrigin.Base ? null : new QuickFix($"Set Min to {max}",
                    () => record.Set("EquipLevelMin", max), () => record.Set("EquipLevelMin", min)),
            };

        // Renewal-aware refine caps (5/4 weapon, 2/1 armor).
        if (string.Equals(type, "Weapon", StringComparison.OrdinalIgnoreCase))
        {
            int maxWeaponLevel = context.Mode == ServerMode.Renewal ? 5 : 4;
            int wl = record.GetInt("WeaponLevel");
            if (wl > maxWeaponLevel)
                yield return new ValidationIssue(ValidationSeverity.Warning, "item_db", key, "WeaponLevel",
                    $"Weapon Level {wl} exceeds the maximum of {maxWeaponLevel} for {context.Mode}.")
                { RuleId = "ITEM.WEAPONLVL_RANGE", Mode = context.Mode };
        }

        if (string.Equals(type, "Armor", StringComparison.OrdinalIgnoreCase))
        {
            int maxArmorLevel = context.Mode == ServerMode.Renewal ? 2 : 1;
            int al = record.GetInt("ArmorLevel");
            if (al > maxArmorLevel)
                yield return new ValidationIssue(ValidationSeverity.Warning, "item_db", key, "ArmorLevel",
                    $"Armor Level {al} exceeds the maximum of {maxArmorLevel} for {context.Mode}.")
                { RuleId = "ITEM.ARMORLVL_RANGE", Mode = context.Mode };
        }

        // No custom-id-range rule: official rAthena items already span huge id bands (up to 1.27M), so there
        // is no safe fixed threshold, and a genuinely new custom id can't silently collide with a base id
        // (the overlay treats a base-id match as an override, not a new record).
    }
}
