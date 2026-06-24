using MidgardStudio.Core.Schema;

namespace MidgardStudio.Core.Schemas;

/// <summary>Schema for item_group_db (ITEM_GROUP_DB v5).</summary>
public static class ItemGroupSchema
{
    private static readonly EnumSource Algorithm = EnumSource.Static("GroupAlgorithm", "SharedPool", "Random", "All");

    private static readonly DbSchema ListItem = DbSchema.Nested("GroupListItem", new[]
    {
        FieldSchema.Int("Index", "Index"),
        new FieldSchema { Name = "Item", Label = "Item", Kind = FieldKind.Reference, Enum = EnumSource.Reference("GroupItem", "item_db") },
        FieldSchema.Int("Rate", "Rate"),
        FieldSchema.Int("Amount", "Amount", 1),
        FieldSchema.Int("Duration", "Duration (min)"),
        FieldSchema.Bool("Announced", "Announced"),
        FieldSchema.Bool("UniqueId", "Unique Id"),
        FieldSchema.Bool("Stacked", "Stacked", true),
        FieldSchema.Bool("Named", "Named"),
        new FieldSchema { Name = "Bound", Label = "Bound", Kind = FieldKind.Enum, Default = "None", Enum = ItemEnums.BindType },
        new FieldSchema { Name = "RandomOptionGroup", Label = "Random Option Group", Kind = FieldKind.String },
        FieldSchema.Int("RefineMinimum", "Refine Min"),
        FieldSchema.Int("RefineMaximum", "Refine Max"),
        new FieldSchema { Name = "GradeMinimum", Label = "Grade Min", Kind = FieldKind.Enum, Default = "None", Enum = ItemEnums.Grade },
        new FieldSchema { Name = "GradeMaximum", Label = "Grade Max", Kind = FieldKind.Enum, Default = "None", Enum = ItemEnums.Grade },
    });

    private static readonly DbSchema SubGroup = DbSchema.Nested("GroupSubGroup", new[]
    {
        FieldSchema.Int("SubGroup", "Sub Group"),
        new FieldSchema { Name = "Algorithm", Label = "Algorithm", Kind = FieldKind.Enum, Default = "SharedPool", Enum = Algorithm },
        FieldSchema.ObjectListField("List", "Items", ListItem),
    });

    public static readonly DbSchema Instance = new()
    {
        Id = "item_group_db",
        DisplayName = "Item Groups",
        HeaderType = "ITEM_GROUP_DB",
        HeaderVersion = 5,
        Key = KeyStrategy.Str("Group"),
        Layout = FileLayout.Standard("item_group_db.yml"),
        Fields = new[]
        {
            new FieldSchema { Name = "Group", Label = "Group", Kind = FieldKind.String, IsKey = true, IsDisplay = true },
            FieldSchema.ObjectListField("SubGroups", "Sub Groups", SubGroup),
        },
    };
}

/// <summary>Schema for achievement_db (ACHIEVEMENT_DB v2).</summary>
public static class AchievementDbSchema
{
    private static readonly DbSchema Target = DbSchema.Nested("AchievementTarget", new[]
    {
        FieldSchema.Int("Id", "Index"),
        new FieldSchema { Name = "Mob", Label = "Mob", Kind = FieldKind.Reference, Enum = EnumSource.Reference("AchMob", "mob_db") },
        FieldSchema.Int("Count", "Count", 1),
    });

    private static readonly DbSchema Rewards = DbSchema.Nested("AchievementRewards", new[]
    {
        new FieldSchema { Name = "Item", Label = "Item", Kind = FieldKind.Reference, Enum = EnumSource.Reference("AchItem", "item_db") },
        FieldSchema.Int("Amount", "Amount", 1),
        FieldSchema.ScriptField("Script", "Script"),
        FieldSchema.Int("TitleId", "Title Id"),
    });

    public static readonly DbSchema Instance = new()
    {
        Id = "achievement_db",
        DisplayName = "Achievements",
        HeaderType = "ACHIEVEMENT_DB",
        HeaderVersion = 2,
        Key = KeyStrategy.Int("Id"),
        Layout = FileLayout.Standard("achievement_db.yml"),
        Fields = new[]
        {
            new FieldSchema { Name = "Id", Label = "Achievement ID", Kind = FieldKind.Int, IsKey = true },
            FieldSchema.EnumField("Group", "Group", CommonEnums.AchievementGroup, "None"),
            new FieldSchema { Name = "Name", Label = "Name", Kind = FieldKind.String, IsDisplay = true },
            FieldSchema.ObjectListField("Targets", "Targets", Target),
            FieldSchema.ScriptField("Condition", "Condition"),
            new FieldSchema { Name = "Map", Label = "Map", Kind = FieldKind.String },
            FieldSchema.ObjectField("Rewards", "Rewards", Rewards),
            FieldSchema.Int("Score", "Score"),
        },
    };
}
