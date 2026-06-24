using System.Collections;
using MidgardStudio.Core.Model;
using MidgardStudio.Core.Schema;

namespace MidgardStudio.Core.Schemas;

/// <summary>Schema for item_combos (COMBO_DB v1). Keyless file — each combo entry gets a synthetic
/// key from its member list so single-combo overrides don't clobber siblings.</summary>
public static class ItemComboSchema
{
    private static readonly DbSchema ComboEntry = DbSchema.Nested("Combo", new[]
    {
        new FieldSchema { Name = "Combo", Label = "Items", Kind = FieldKind.ScalarList, ElementKind = FieldKind.String },
    });

    public static readonly DbSchema Instance = new()
    {
        Id = "item_combos",
        DisplayName = "Item Combos",
        HeaderType = "COMBO_DB",
        HeaderVersion = 1,
        Key = KeyStrategy.Computed(ComputeKey),
        Layout = FileLayout.Standard("item_combos.yml"),
        Fields = new[]
        {
            FieldSchema.ObjectListField("Combos", "Combos", ComboEntry),
            FieldSchema.ScriptField("Script", "Script"),
        },
    };

    private static string ComputeKey(DbRecord record)
    {
        var combos = record.GetList("Combos");
        if (combos is null || combos.Count == 0) return string.Empty;

        var parts = new List<string>();
        foreach (var combo in combos)
        {
            if (combo.Get("Combo") is IEnumerable items and not string)
            {
                var members = items.Cast<object?>().Select(x => x?.ToString() ?? string.Empty);
                parts.Add(string.Join("+", members));
            }
        }
        return string.Join("&", parts);
    }
}

/// <summary>
/// Schema for skill_db (SKILL_DB v4) — full support. Models the scalar/enum/flag fields, the
/// <b>dual-typed</b> per-level numeric fields (Range, HitCount, CastTime, Cooldown, costs, …) via the
/// <see cref="FieldKind.LevelInt"/> kind (a single value OR a per-level array), and the nested
/// <c>Requires</c>, <c>Unit</c>, <c>CopyFlags</c> and <c>NoNearNPC</c> objects. Any rare per-level form
/// of a scalar field (e.g. per-level Element) is still preserved verbatim and round-trips unchanged.
/// </summary>
public static class SkillDbSchema
{
    private const string GGeneral = "General";
    private const string GDamage = "Damage & Range";
    private const string GCast = "Cast & Delay";
    private const string GFlags = "Flags";
    private const string GReq = "Requirements";
    private const string GUnit = "Ground Unit";

    private static readonly DbSchema CopyFlags = DbSchema.Nested("SkillCopyFlags", new[]
    {
        FieldSchema.BoolMapField("Skill", "Copyable by", CommonEnums.SkillCopySkill),
        FieldSchema.BoolMapField("RemoveRequirement", "Drop requirements", CommonEnums.SkillRemoveRequirement),
    });

    private static readonly DbSchema NoNearNpc = DbSchema.Nested("SkillNoNearNpc", new[]
    {
        FieldSchema.Int("AdditionalRange", "Additional Range"),
        FieldSchema.BoolMapField("Type", "NPC Types", CommonEnums.SkillNoNearNpc),
    });

    private static readonly DbSchema ItemCost = DbSchema.Nested("SkillItemCost", new[]
    {
        new FieldSchema { Name = "Item", Label = "Item", Kind = FieldKind.Reference, Enum = EnumSource.Reference("ReqItem", "item_db") },
        FieldSchema.Int("Amount", "Amount", 1),
        FieldSchema.Int("Level", "Level (optional)"),
    });

    private static readonly DbSchema Requires = DbSchema.Nested("SkillRequire", new[]
    {
        FieldSchema.Level("HpCost", "HP Cost", "Amount"),
        FieldSchema.Level("SpCost", "SP Cost", "Amount"),
        FieldSchema.Level("ApCost", "AP Cost", "Amount"),
        FieldSchema.Level("HpRateCost", "HP Rate Cost %", "Amount"),
        FieldSchema.Level("SpRateCost", "SP Rate Cost %", "Amount"),
        FieldSchema.Level("ApRateCost", "AP Rate Cost %", "Amount"),
        FieldSchema.Level("MaxHpTrigger", "Max HP Trigger", "Amount"),
        FieldSchema.Level("ZenyCost", "Zeny Cost", "Amount"),
        FieldSchema.Level("SpiritSphereCost", "Spirit Sphere Cost", "Amount"),
        FieldSchema.EnumField("State", "Required State", CommonEnums.SkillState, "None"),
        FieldSchema.BoolMapField("Weapon", "Weapon", CommonEnums.SkillWeapon),
        FieldSchema.BoolMapField("Ammo", "Ammo", CommonEnums.SkillAmmo),
        FieldSchema.Level("AmmoAmount", "Ammo Amount", "Amount"),
        FieldSchema.ObjectListField("ItemCost", "Item Cost", ItemCost),
    });

    private static readonly DbSchema Unit = DbSchema.Nested("SkillUnit", new[]
    {
        new FieldSchema { Name = "Id", Label = "Unit Id", Kind = FieldKind.String, IsDisplay = true },
        new FieldSchema { Name = "AlternateId", Label = "Alternate Id", Kind = FieldKind.String },
        FieldSchema.Level("Layout", "Layout", "Size"),
        FieldSchema.Level("Range", "Range", "Size"),
        FieldSchema.Int("Interval", "Interval (ms)"),
        FieldSchema.EnumField("Target", "Target", CommonEnums.SkillUnitTarget, "All"),
        FieldSchema.BoolMapField("Flag", "Unit Flags", CommonEnums.SkillUnitFlag),
    });

    public static readonly DbSchema Instance = new()
    {
        Id = "skill_db",
        DisplayName = "Skills",
        HeaderType = "SKILL_DB",
        HeaderVersion = 4,
        Key = KeyStrategy.Int("Id"),
        Layout = FileLayout.Standard("skill_db.yml"),
        Fields = new[]
        {
            // ----- General -----
            new FieldSchema { Name = "Id", Label = "Skill ID", Kind = FieldKind.Int, IsKey = true, Group = GGeneral },
            new FieldSchema { Name = "Name", Label = "Aegis Name", Kind = FieldKind.String, Group = GGeneral },
            new FieldSchema { Name = "Description", Label = "Description", Kind = FieldKind.String, IsDisplay = true, Group = GGeneral },
            FieldSchema.Int("MaxLevel", "Max Level", group: GGeneral),
            FieldSchema.EnumField("Type", "Type", CommonEnums.SkillType, "None", GGeneral),
            FieldSchema.EnumField("TargetType", "Target Type", CommonEnums.SkillTargetType, "Passive", GGeneral),
            FieldSchema.EnumField("Hit", "Hit", CommonEnums.SkillHit, "Normal", GGeneral),
            FieldSchema.EnumField("Element", "Element", CommonEnums.SkillElement, "Neutral", GGeneral),
            new FieldSchema { Name = "Status", Label = "Inflicts Status", Kind = FieldKind.String, Group = GGeneral },

            // ----- Damage & Range (dual per-level) -----
            FieldSchema.Level("Range", "Range", "Size", GDamage),
            FieldSchema.Level("HitCount", "Hit Count", "Count", GDamage),
            FieldSchema.Level("SplashArea", "Splash Area", "Area", GDamage),
            FieldSchema.Level("Knockback", "Knockback", "Amount", GDamage),
            FieldSchema.Level("ActiveInstance", "Active Instances", "Max", GDamage),
            FieldSchema.Level("GiveAp", "Give AP", "Amount", GDamage),

            // ----- Cast & Delay (dual per-level, ms) -----
            new FieldSchema { Name = "CastCancel", Label = "Cast Cancel", Kind = FieldKind.Bool, Default = true, Group = GCast },
            FieldSchema.Int("CastDefenseReduction", "Cast Defense Reduction %", group: GCast),
            FieldSchema.Level("CastTime", "Cast Time", "Time", GCast),
            new FieldSchema { Name = "FixedCastTime", Label = "Fixed Cast Time", Kind = FieldKind.LevelInt, LevelValueKey = "Time", Group = GCast, Renewal = RenewalScope.RenewalOnly, IsSearchable = false },
            FieldSchema.Level("AfterCastActDelay", "After-Cast Act Delay", "Time", GCast),
            FieldSchema.Level("AfterCastWalkDelay", "After-Cast Walk Delay", "Time", GCast),
            FieldSchema.Level("Cooldown", "Cooldown", "Time", GCast),
            FieldSchema.Level("Duration1", "Duration 1", "Time", GCast),
            FieldSchema.Level("Duration2", "Duration 2", "Time", GCast),

            // ----- Flags -----
            FieldSchema.BoolMapField("Flags", "Flags", CommonEnums.SkillFlags, GFlags),
            FieldSchema.BoolMapField("DamageFlags", "Damage Flags", CommonEnums.SkillDamageFlags, GFlags),
            FieldSchema.BoolMapField("CastTimeFlags", "Cast Time Flags", CommonEnums.SkillCastFlags, GFlags),
            FieldSchema.BoolMapField("CastDelayFlags", "Cast Delay Flags", CommonEnums.SkillCastFlags, GFlags),
            FieldSchema.ObjectField("CopyFlags", "Copy Flags", CopyFlags, GFlags),
            FieldSchema.ObjectField("NoNearNPC", "No Near NPC", NoNearNpc, GFlags),

            // ----- Requirements / Unit -----
            FieldSchema.ObjectField("Requires", "Requirements", Requires, GReq),
            FieldSchema.ObjectField("Unit", "Ground Unit", Unit, GUnit),
        },
    };
}
