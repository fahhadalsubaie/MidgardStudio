using MidgardStudio.Core.Schema;

namespace MidgardStudio.Core.Schemas;

/// <summary>Schema for pet_db (PET_DB v1).</summary>
public static class PetDbSchema
{
    private static readonly DbSchema ItemReq = DbSchema.Nested("PetItemReq", new[]
    {
        new FieldSchema { Name = "Item", Label = "Item", Kind = FieldKind.Reference, Enum = EnumSource.Reference("ReqItem", "item_db") },
        FieldSchema.Int("Amount", "Amount", 1),
    });

    private static readonly DbSchema Evolution = DbSchema.Nested("PetEvolution", new[]
    {
        new FieldSchema { Name = "Target", Label = "Target Mob", Kind = FieldKind.Reference, Enum = EnumSource.Reference("EvoTarget", "mob_db") },
        FieldSchema.ObjectListField("ItemRequirements", "Item Requirements", ItemReq),
    });

    public static readonly DbSchema Instance = new()
    {
        Id = "pet_db",
        DisplayName = "Pets",
        HeaderType = "PET_DB",
        HeaderVersion = 1,
        Key = KeyStrategy.Str("Mob"),
        Layout = FileLayout.Standard("pet_db.yml"),
        Fields = new[]
        {
            new FieldSchema { Name = "Mob", Label = "Mob", Kind = FieldKind.Reference, IsKey = true, IsDisplay = true, Enum = EnumSource.Reference("PetMob", "mob_db"), Group = "General" },
            new FieldSchema { Name = "TameItem", Label = "Tame Item", Kind = FieldKind.Reference, Enum = EnumSource.Reference("TameItem", "item_db"), Group = "General" },
            new FieldSchema { Name = "EggItem", Label = "Egg Item", Kind = FieldKind.Reference, Enum = EnumSource.Reference("EggItem", "item_db"), Group = "General" },
            new FieldSchema { Name = "EquipItem", Label = "Equip Item", Kind = FieldKind.Reference, Enum = EnumSource.Reference("EquipItem", "item_db"), Group = "General" },
            new FieldSchema { Name = "FoodItem", Label = "Food Item", Kind = FieldKind.Reference, Enum = EnumSource.Reference("FoodItem", "item_db"), Group = "General" },
            FieldSchema.Int("Fullness", "Fullness", group: "Intimacy"),
            FieldSchema.Int("HungryDelay", "Hungry Delay", 60, "Intimacy"),
            FieldSchema.Int("HungerIncrease", "Hunger Increase", 20, "Intimacy"),
            FieldSchema.Int("IntimacyStart", "Intimacy Start", 250, "Intimacy"),
            FieldSchema.Int("IntimacyFed", "Intimacy Fed", 50, "Intimacy"),
            FieldSchema.Int("IntimacyOverfed", "Intimacy Overfed", -100, "Intimacy"),
            FieldSchema.Int("IntimacyHungry", "Intimacy Hungry", -5, "Intimacy"),
            FieldSchema.Int("IntimacyOwnerDie", "Intimacy Owner Die", -20, "Intimacy"),
            FieldSchema.Int("CaptureRate", "Capture Rate", group: "Combat"),
            FieldSchema.Bool("SpecialPerformance", "Special Performance", true, "Combat"),
            FieldSchema.Int("AttackRate", "Attack Rate", group: "Combat"),
            FieldSchema.Int("RetaliateRate", "Retaliate Rate", group: "Combat"),
            FieldSchema.Int("ChangeTargetRate", "Change Target Rate", group: "Combat"),
            FieldSchema.Bool("AllowAutoFeed", "Allow Auto Feed", group: "Combat"),
            FieldSchema.ScriptField("Script", "Script"),
            FieldSchema.ScriptField("SupportScript", "Support Script"),
            FieldSchema.ObjectListField("Evolution", "Evolution", Evolution, "Evolution"),
        },
    };
}

/// <summary>Schema for abra_db (ABRA_DB v1) — Abracadabra / Hocus-Pocus ("Class Change").</summary>
public static class AbraDbSchema
{
    private static readonly DbSchema ProbabilityRow = DbSchema.Nested("AbraProbability", new[]
    {
        FieldSchema.Int("Level", "Level"),
        new FieldSchema { Name = "Probability", Label = "Probability", Kind = FieldKind.Int, Default = 0, RateScale = 10000 },
    });

    public static readonly DbSchema Instance = new()
    {
        Id = "abra_db",
        DisplayName = "Class Change",
        HeaderType = "ABRA_DB",
        HeaderVersion = 1,
        Key = KeyStrategy.Str("Skill"),
        // abra data ships only in pre-re; use it for both modes.
        Layout = new FileLayout
        {
            RenewalFiles = new[] { "pre-re/abra_db.yml" },
            PreRenewalFiles = new[] { "pre-re/abra_db.yml" },
            ImportFile = "import/abra_db.yml",
        },
        Fields = new[]
        {
            new FieldSchema { Name = "Skill", Label = "Skill", Kind = FieldKind.Reference, IsKey = true, IsDisplay = true, Enum = EnumSource.Reference("AbraSkill", "skill_db") },
            FieldSchema.ObjectListField("Probability", "Probability (per level)", ProbabilityRow),
        },
    };
}

/// <summary>Schema for mob_summon (MOB_SUMMONABLE_DB v1) — Bloody/Dead Branch, Class Change pools, etc.</summary>
public static class MobSummonSchema
{
    public static readonly DbSchema SummonRow = DbSchema.Nested("SummonEntry", new[]
    {
        new FieldSchema { Name = "Mob", Label = "Mob", Kind = FieldKind.Reference, Enum = EnumSource.Reference("SummonMob", "mob_db") },
        FieldSchema.Int("Rate", "Rate (0-1,000,000)"),
    });

    public static readonly DbSchema Instance = new()
    {
        Id = "mob_summon",
        DisplayName = "Summon Groups",
        HeaderType = "MOB_SUMMONABLE_DB",
        HeaderVersion = 1,
        Key = KeyStrategy.Str("Group"),
        Layout = FileLayout.Standard("mob_summon.yml"),
        Fields = new[]
        {
            new FieldSchema { Name = "Group", Label = "Group", Kind = FieldKind.String, IsKey = true, IsDisplay = true },
            new FieldSchema { Name = "Default", Label = "Default Mob", Kind = FieldKind.Reference, Enum = EnumSource.Reference("SummonDefault", "mob_db") },
            FieldSchema.ObjectListField("Summon", "Summon List", SummonRow),
        },
    };
}
