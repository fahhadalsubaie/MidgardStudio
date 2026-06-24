using MidgardStudio.Core.Model;
using MidgardStudio.Core.Schema;

namespace MidgardStudio.Core.Schemas;

/// <summary>
/// Declarative schema for rAthena's item_db (YAML Type ITEM_DB, Version 3). Exercises every
/// field kind: scalar, enum, bool-map (Jobs/Classes/Locations), nested objects
/// (Flags/Delay/Stack/NoUse/Trade) and literal scripts.
/// </summary>
public static class ItemDbSchema
{
    private const string GGeneral = "General";
    private const string GCombat = "Combat";
    private const string GRestrict = "Restrictions";
    private const string GFlags = "Flags & Trade";
    private const string GScripts = "Scripts";

    public static readonly DbSchema Flags = DbSchema.Nested("ItemFlags", new[]
    {
        FieldSchema.Bool("BuyingStore", "Buying Store"),
        FieldSchema.Bool("DeadBranch", "Dead Branch"),
        FieldSchema.Bool("Container", "Container"),
        FieldSchema.Bool("UniqueId", "Unique Id"),
        FieldSchema.Bool("BindOnEquip", "Bind On Equip"),
        FieldSchema.Bool("DropAnnounce", "Drop Announce"),
        FieldSchema.Bool("NoConsume", "No Consume"),
        FieldSchema.EnumField("DropEffect", "Drop Effect", ItemEnums.DropEffect, "None"),
    });

    public static readonly DbSchema Delay = DbSchema.Nested("ItemDelay", new[]
    {
        FieldSchema.Int("Duration", "Duration (ms)"),
        FieldSchema.Str("Status", "Status"),
    });

    public static readonly DbSchema Stack = DbSchema.Nested("ItemStack", new[]
    {
        FieldSchema.Int("Amount", "Amount"),
        FieldSchema.Bool("Inventory", "Inventory", @default: true),
        FieldSchema.Bool("Cart", "Cart"),
        FieldSchema.Bool("Storage", "Storage"),
        FieldSchema.Bool("GuildStorage", "Guild Storage"),
    });

    public static readonly DbSchema NoUse = DbSchema.Nested("ItemNoUse", new[]
    {
        FieldSchema.Int("Override", "Override", @default: 100),
        FieldSchema.Bool("Sitting", "Sitting"),
    });

    public static readonly DbSchema Trade = DbSchema.Nested("ItemTrade", new[]
    {
        FieldSchema.Int("Override", "Override", @default: 100),
        FieldSchema.Bool("NoDrop", "No Drop"),
        FieldSchema.Bool("NoTrade", "No Trade"),
        FieldSchema.Bool("TradePartner", "Trade Partner"),
        FieldSchema.Bool("NoSell", "No Sell"),
        FieldSchema.Bool("NoCart", "No Cart"),
        FieldSchema.Bool("NoStorage", "No Storage"),
        FieldSchema.Bool("NoGuildStorage", "No Guild Storage"),
        FieldSchema.Bool("NoMail", "No Mail"),
        FieldSchema.Bool("NoAuction", "No Auction"),
    });

    public static readonly DbSchema Instance = new()
    {
        Id = "item_db",
        DisplayName = "Items",
        HeaderType = "ITEM_DB",
        HeaderVersion = 3,
        Key = KeyStrategy.Int("Id"),
        Layout = new FileLayout
        {
            RenewalFiles = new[] { "re/item_db_equip.yml", "re/item_db_usable.yml", "re/item_db_etc.yml" },
            PreRenewalFiles = new[] { "pre-re/item_db_equip.yml", "pre-re/item_db_usable.yml", "pre-re/item_db_etc.yml" },
            ImportFile = "import/item_db.yml",
        },
        Fields = new[]
        {
            new FieldSchema { Name = "Id", Label = "Item ID", Kind = FieldKind.Int, IsKey = true, Group = GGeneral },
            new FieldSchema { Name = "AegisName", Label = "Aegis Name", Kind = FieldKind.String, Group = GGeneral, Description = "Server name (no spaces)." },
            new FieldSchema { Name = "Name", Label = "Name", Kind = FieldKind.String, IsDisplay = true, Group = GGeneral },
            FieldSchema.EnumField("Type", "Type", ItemEnums.Type, "Etc", GGeneral),
            new FieldSchema
            {
                Name = "SubType", Label = "Sub Type", Kind = FieldKind.Enum, Enum = ItemEnums.SubType, Group = GGeneral,
                IsApplicable = r => r.GetString("Type") is "Weapon" or "Ammo" or "Card",
            },
            FieldSchema.Int("Buy", "Buy", group: GGeneral),
            FieldSchema.Int("Sell", "Sell", group: GGeneral),
            FieldSchema.Int("Weight", "Weight", group: GGeneral),

            FieldSchema.Int("Attack", "Attack", group: GCombat),
            new FieldSchema { Name = "MagicAttack", Label = "Magic Attack", Kind = FieldKind.Int, Default = 0, Group = GCombat, Renewal = RenewalScope.RenewalOnly },
            FieldSchema.Int("Defense", "Defense", group: GCombat),
            FieldSchema.Int("Range", "Range", group: GCombat),
            FieldSchema.Int("Slots", "Slots", group: GCombat),
            new FieldSchema
            {
                Name = "WeaponLevel", Label = "Weapon Level", Kind = FieldKind.Int, Default = 0, Group = GCombat,
                IsApplicable = r => r.GetString("Type") == "Weapon",
            },
            new FieldSchema
            {
                Name = "ArmorLevel", Label = "Armor Level", Kind = FieldKind.Int, Default = 0, Group = GCombat,
                IsApplicable = r => r.GetString("Type") == "Armor",
            },

            FieldSchema.BoolMapField("Jobs", "Jobs", ItemEnums.Jobs, GRestrict),
            FieldSchema.BoolMapField("Classes", "Classes", ItemEnums.Classes, GRestrict),
            FieldSchema.EnumField("Gender", "Gender", ItemEnums.Gender, "Both", GRestrict),
            FieldSchema.BoolMapField("Locations", "Locations", ItemEnums.Locations, GRestrict),
            FieldSchema.Int("EquipLevelMin", "Equip Level Min", group: GRestrict),
            FieldSchema.Int("EquipLevelMax", "Equip Level Max", group: GRestrict),
            FieldSchema.Bool("Refineable", "Refineable", group: GRestrict),
            new FieldSchema { Name = "Gradable", Label = "Gradable", Kind = FieldKind.Bool, Default = false, Group = GRestrict, Renewal = RenewalScope.RenewalOnly },
            FieldSchema.Int("View", "View (sprite id)", group: GRestrict),
            new FieldSchema { Name = "AliasName", Label = "Alias Name", Kind = FieldKind.Reference, Enum = EnumSource.Reference("ItemAlias", "item_db"), Group = GRestrict },

            FieldSchema.ObjectField("Flags", "Flags", Flags, GFlags),
            FieldSchema.ObjectField("Delay", "Delay", Delay, GFlags),
            FieldSchema.ObjectField("Stack", "Stack", Stack, GFlags),
            FieldSchema.ObjectField("NoUse", "No Use", NoUse, GFlags),
            FieldSchema.ObjectField("Trade", "Trade", Trade, GFlags),

            FieldSchema.ScriptField("Script", "Script", GScripts),
            FieldSchema.ScriptField("EquipScript", "Equip Script", GScripts),
            FieldSchema.ScriptField("UnEquipScript", "Unequip Script", GScripts),
        },
    };
}
