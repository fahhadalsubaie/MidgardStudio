using MidgardStudio.Core.Schema;
using MidgardStudio.Core.Validation;

namespace MidgardStudio.Core.Schemas;

/// <summary>Schema for mob_db (MOB_DB v5): stats, behavior modes, and drop tables.</summary>
public static class MobDbSchema
{
    private const string GId = "General";
    private const string GStats = "Stats";
    private const string GCombat = "Combat";
    private const string GBehavior = "Behavior";
    private const string GDrops = "Drops";

    public static readonly DbSchema DropElement = DbSchema.Nested("MobDrop", new[]
    {
        new FieldSchema { Name = "Item", Label = "Item", Kind = FieldKind.Reference, Enum = EnumSource.Reference("DropItem", "item_db") },
        new FieldSchema { Name = "Rate", Label = "Rate", Kind = FieldKind.Int, Default = 0, RateScale = 10000, Min = 0, Max = 10000 },
        FieldSchema.Bool("StealProtected", "Steal Protected"),
        new FieldSchema { Name = "RandomOptionGroup", Label = "Random Option Group", Kind = FieldKind.String },
        FieldSchema.Int("Index", "Index"),
    });

    public static readonly DbSchema Instance = new()
    {
        Id = "mob_db",
        DisplayName = "Mobs",
        HeaderType = "MOB_DB",
        HeaderVersion = 5,
        Key = KeyStrategy.Int("Id"),
        Layout = FileLayout.Standard("mob_db.yml"),
        Fields = new[]
        {
            new FieldSchema { Name = "Id", Label = "Mob ID", Kind = FieldKind.Int, IsKey = true, Group = GId },
            new FieldSchema { Name = "AegisName", Label = "Aegis Name", Kind = FieldKind.String, Group = GId, Description = "Server name; also the client sprite name.", IsRequired = true, Unique = true, MaxLength = 24, MaxLengthSeverity = ValidationSeverity.Error },
            new FieldSchema { Name = "Name", Label = "Name", Kind = FieldKind.String, IsDisplay = true, Group = GId, MaxLength = 24 },
            new FieldSchema { Name = "JapaneseName", Label = "Japanese Name", Kind = FieldKind.String, Group = GId },
            // The colored on-screen MVP/boss label (e.g. "<Red Pepper>"), capped to NAME_LENGTH (24).
            new FieldSchema { Name = "Title", Label = "Title", Kind = FieldKind.String, Group = GId, MaxLength = 24 },
            FieldSchema.Int("Level", "Level", group: GId),

            FieldSchema.Int("Hp", "HP", group: GStats),
            FieldSchema.Int("Sp", "SP", group: GStats),
            // EXP is read server-side as uint64 (t_exp), capped at MAX_EXP = INT64_MAX on renewal — so it must
            // be Long, not Int, or a high-rate value above 2.1B can't be entered/edited (it would clamp to int32).
            new FieldSchema { Name = "BaseExp", Label = "Base EXP", Kind = FieldKind.Long, Default = 0L, Group = GStats },
            new FieldSchema { Name = "JobExp", Label = "Job EXP", Kind = FieldKind.Long, Default = 0L, Group = GStats },
            new FieldSchema { Name = "MvpExp", Label = "MVP EXP", Kind = FieldKind.Long, Default = 0L, Group = GStats },
            FieldSchema.Int("Str", "STR", group: GStats),
            FieldSchema.Int("Agi", "AGI", group: GStats),
            FieldSchema.Int("Vit", "VIT", group: GStats),
            FieldSchema.Int("Int", "INT", group: GStats),
            FieldSchema.Int("Dex", "DEX", group: GStats),
            FieldSchema.Int("Luk", "LUK", group: GStats),

            FieldSchema.Int("Attack", "Attack", group: GCombat),
            FieldSchema.Int("Attack2", "Attack2 / MATK", group: GCombat),
            FieldSchema.Int("Defense", "Defense", group: GCombat),
            FieldSchema.Int("MagicDefense", "Magic Defense", group: GCombat),
            FieldSchema.Int("Resistance", "Resistance", group: GCombat),
            FieldSchema.Int("MagicResistance", "Magic Resistance", group: GCombat),
            FieldSchema.Int("AttackRange", "Attack Range", group: GCombat),
            FieldSchema.Int("SkillRange", "Skill Range", group: GCombat),
            FieldSchema.Int("ChaseRange", "Chase Range", group: GCombat),
            FieldSchema.EnumField("Size", "Size", CommonEnums.MobSize, "Small", GCombat),
            FieldSchema.EnumField("Race", "Race", CommonEnums.MobRace, "Formless", GCombat),
            FieldSchema.BoolMapField("RaceGroups", "Race Groups", CommonEnums.RaceGroups, GCombat),
            FieldSchema.EnumField("Element", "Element", CommonEnums.Element, "Neutral", GCombat),
            FieldSchema.Int("ElementLevel", "Element Level", 1, GCombat),

            FieldSchema.Int("WalkSpeed", "Walk Speed", group: GBehavior),
            FieldSchema.Int("AttackDelay", "Attack Delay", group: GBehavior),
            FieldSchema.Int("AttackMotion", "Attack Motion", group: GBehavior),
            FieldSchema.Int("ClientAttackMotion", "Client Attack Motion", group: GBehavior),
            FieldSchema.Int("DamageMotion", "Damage Motion", group: GBehavior),
            FieldSchema.Int("DamageTaken", "Damage Taken %", 100, GBehavior),
            new FieldSchema { Name = "Ai", Label = "AI", Kind = FieldKind.String, Default = "06", Group = GBehavior },
            FieldSchema.EnumField("Class", "Class", CommonEnums.MobClass, "Normal", GBehavior),
            // uint16 spawn-group id (HP/loot sharing). Optional; only emitted when set.
            new FieldSchema { Name = "GroupId", Label = "Group ID", Kind = FieldKind.Int, Default = 0, Group = GBehavior, Min = 0, Max = 65535 },
            FieldSchema.FlagsField("Modes", "Modes", CommonEnums.MobModes, GBehavior),

            new FieldSchema { Name = "MvpDrops", Label = "MVP Drops", Kind = FieldKind.ObjectList, ObjectSchema = DropElement, Group = GDrops, IsSearchable = false, HideInForm = true },
            new FieldSchema { Name = "Drops", Label = "Drops", Kind = FieldKind.ObjectList, ObjectSchema = DropElement, Group = GDrops, IsSearchable = false, HideInForm = true },
        },
    };
}
