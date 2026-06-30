using MidgardStudio.Core.Model;

namespace MidgardStudio.Core.Lua;

/// <summary>
/// Shared helpers for deriving client-facing text from a server <c>item_db</c> record: the display name and
/// the human labels (class/category, jobs, headgear position). The full description is composed by
/// <see cref="ItemAutocomplete"/>; this type holds the pieces both it and callers reuse.
/// Field names mirror <c>ItemDbSchema</c> exactly.
/// </summary>
public static class ServerItemText
{
    /// <summary>The display name a player sees: server <c>Name</c>, falling back to a humanised AegisName.</summary>
    public static string DisplayName(DbRecord server)
    {
        var name = server.GetString("Name");
        if (!string.IsNullOrWhiteSpace(name)) return name!.Trim();

        var aegis = server.GetString("AegisName");
        if (!string.IsNullOrWhiteSpace(aegis)) return aegis!.Replace('_', ' ').Trim();

        return $"Item {server.GetInt("Id")}";
    }

    /// <summary>The "Class:" word for an item (e.g. "One-Handed Sword", "Armor", "Headgear", "Card").</summary>
    public static string CategoryLabel(string type, string? subType, ISet<string>? locations) => type switch
    {
        "Weapon" => WeaponLabel(subType),
        "Armor" => ArmorLabel(locations),
        "Card" => "Card",
        "Ammo" => AmmoLabel(subType),
        "Healing" or "Usable" or "DelayConsume" => "Restorative",
        "PetEgg" or "PetArmor" => "Pet",
        "ShadowGear" => "Shadow Equipment",
        "Cash" => "Cash Shop Item",
        _ => "Miscellaneous Item",
    };

    /// <summary>Job restriction, or null when the item is usable by every job.</summary>
    public static string? JobsLabel(ISet<string>? jobs)
    {
        if (jobs is null || jobs.Count == 0 || jobs.Contains("All")) return null;
        return string.Join(", ", jobs.Select(j => JobNames.TryGetValue(j, out var v) ? v : j));
    }

    /// <summary>The combined "who can use this" phrase — the job-class tier (item_db <c>Classes</c>) prefixed
    /// onto the jobs, e.g. Classes {Upper} + Jobs {Archer} → "Transcendent Archer". Null when the item is
    /// unrestricted (All/empty classes AND All/empty jobs) so callers can omit the line entirely.</summary>
    public static string? ClassLabel(ISet<string>? classes, ISet<string>? jobs)
    {
        string? tier = ClassTierLabel(classes);
        string? job = JobsLabel(jobs);
        if (tier is null) return job;
        if (job is null) return tier;
        return $"{tier} {job}";
    }

    private static string? ClassTierLabel(ISet<string>? classes)
    {
        if (classes is null || classes.Count == 0) return null;
        var parts = classes.Where(c => !string.Equals(c, "All", StringComparison.Ordinal))
                           .Select(c => ClassTiers.TryGetValue(c, out var v) ? v : c)
                           .ToList();
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    /// <summary>Headgear position(s) (Upper / Middle / Lower), or null when not headgear.</summary>
    public static string? PositionLabel(ISet<string>? locations)
    {
        if (locations is null || locations.Count == 0) return null;
        var parts = new List<string>();
        if (locations.Contains("Head_Top") || locations.Contains("Costume_Head_Top")) parts.Add("Upper");
        if (locations.Contains("Head_Mid") || locations.Contains("Costume_Head_Mid")) parts.Add("Middle");
        if (locations.Contains("Head_Low") || locations.Contains("Costume_Head_Low")) parts.Add("Lower");
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    /// <summary>The card "Compound on" target word derived from the card's equip Locations.</summary>
    public static string CompoundTarget(ISet<string>? locations)
    {
        if (locations is null || locations.Count == 0) return "Equipment";
        if (locations.Contains("Right_Hand")) return "Weapon";
        if (locations.Contains("Left_Hand")) return "Shield";
        if (locations.Contains("Armor")) return "Armor";
        if (locations.Any(l => l.Contains("Head", StringComparison.Ordinal))) return "Headgear";
        if (locations.Contains("Garment")) return "Garment";
        if (locations.Contains("Shoes")) return "Footgear";
        if (locations.Contains("Right_Accessory") || locations.Contains("Left_Accessory")) return "Accessory";
        return "Equipment";
    }

    public static bool IsHeadgear(ISet<string>? locations) =>
        locations is not null && locations.Any(l =>
            l.StartsWith("Head_", StringComparison.Ordinal) || l.StartsWith("Costume_Head", StringComparison.Ordinal));

    private static string WeaponLabel(string? subType) =>
        subType is not null && WeaponSubTypes.TryGetValue(subType, out var label) ? label : "Weapon";

    private static string AmmoLabel(string? subType) =>
        subType is not null && AmmoSubTypes.TryGetValue(subType, out var label) ? label : "Ammunition";

    private static string ArmorLabel(ISet<string>? locations)
    {
        if (locations is null || locations.Count == 0) return "Armor";
        if (locations.Any(l => l.Contains("Head", StringComparison.Ordinal))) return "Headgear";
        if (locations.Contains("Garment") || locations.Contains("Costume_Garment")) return "Garment";
        if (locations.Contains("Shoes")) return "Footgear";
        if (locations.Contains("Right_Accessory") || locations.Contains("Left_Accessory")) return "Accessory";
        if (locations.Any(l => l.StartsWith("Shadow_", StringComparison.Ordinal))) return "Shadow Equipment";
        return "Armor";
    }

    private static readonly Dictionary<string, string> WeaponSubTypes = new(StringComparer.Ordinal)
    {
        ["Fist"] = "Bare Fist", ["Dagger"] = "Dagger",
        ["1hSword"] = "One-Handed Sword", ["2hSword"] = "Two-Handed Sword",
        ["1hSpear"] = "One-Handed Spear", ["2hSpear"] = "Two-Handed Spear",
        ["1hAxe"] = "One-Handed Axe", ["2hAxe"] = "Two-Handed Axe",
        ["Mace"] = "Mace", ["2hMace"] = "Two-Handed Mace",
        ["Staff"] = "One-Handed Staff", ["2hStaff"] = "Two-Handed Staff",
        ["Bow"] = "Bow", ["Knuckle"] = "Knuckle", ["Musical"] = "Musical Instrument",
        ["Whip"] = "Whip", ["Book"] = "Book", ["Katar"] = "Katar",
        ["Revolver"] = "Revolver", ["Rifle"] = "Rifle", ["Gatling"] = "Gatling Gun",
        ["Shotgun"] = "Shotgun", ["Grenade"] = "Grenade Launcher", ["Huuma"] = "Huuma Shuriken",
    };

    private static readonly Dictionary<string, string> AmmoSubTypes = new(StringComparer.Ordinal)
    {
        ["Arrow"] = "Arrow", ["Bullet"] = "Bullet", ["Shell"] = "Shell",
        ["Shuriken"] = "Shuriken", ["Kunai"] = "Kunai", ["CannonBall"] = "Cannon Ball",
        ["ThrowWeapon"] = "Throwing Weapon",
    };

    private static readonly Dictionary<string, string> JobNames = new(StringComparer.Ordinal)
    {
        ["BardDancer"] = "Bard/Dancer", ["KagerouOboro"] = "Kagerou/Oboro",
        ["SoulLinker"] = "Soul Linker", ["StarGladiator"] = "Star Gladiator",
        ["SuperNovice"] = "Super Novice",
    };

    // item_db Classes token → display tier (mirrors ItemEnums.Classes; "All" is dropped as "no tier").
    private static readonly Dictionary<string, string> ClassTiers = new(StringComparer.Ordinal)
    {
        ["Normal"] = "Normal", ["Upper"] = "Transcendent", ["Baby"] = "Baby",
        ["Third"] = "Third Class", ["Third_Upper"] = "Third Class (Trans)", ["Third_Baby"] = "Third Class (Baby)",
        ["Fourth"] = "Fourth Class", ["All_Upper"] = "All Transcendent", ["All_Baby"] = "All Baby", ["All_Third"] = "All Third Class",
    };
}
