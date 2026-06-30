using MidgardStudio.Core.Model;
using MidgardStudio.Core.Schema;
using MidgardStudio.Core.Validation;

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

    // Type-applicability predicates — rAthena zeroes/ignores these on the wrong item type, so the editor
    // (and the schema-driven Forge) hides them rather than letting a user enter a value the server drops.
    private static bool IsWeapon(DbRecord r) => r.GetString("Type") == "Weapon";
    private static bool IsWeaponOrAmmo(DbRecord r) => r.GetString("Type") is "Weapon" or "Ammo";
    private static bool IsArmorOrShadow(DbRecord r) => r.GetString("Type") is "Armor" or "ShadowGear";
    private static bool IsWeaponOrArmor(DbRecord r) => r.GetString("Type") is "Weapon" or "Armor";
    private static bool IsEquipType(DbRecord r) => r.GetString("Type") is "Weapon" or "Armor" or "ShadowGear";
    private static bool HasEquipLocation(DbRecord r) => r.GetString("Type") is "Weapon" or "Armor" or "ShadowGear" or "Ammo" or "PetArmor";
    // Functional applicability for the nested flag groups (rAthena stores them regardless of type, but they're
    // inert on the wrong type, so the editor hides them): Delay/NoUse are use-time behaviors (consumables),
    // Stack is for non-equipment (equipment never stacks).
    private static bool IsUsable(DbRecord r) => r.GetString("Type") is "Healing" or "Usable" or "DelayConsume" or "Cash";
    private static bool IsStackable(DbRecord r) => r.GetString("Type") is not ("Weapon" or "Armor" or "ShadowGear");

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
        // rAthena caps Override at 100 (GM group level); a higher value is warned + capped on load.
        new FieldSchema { Name = "Override", Label = "Override", Kind = FieldKind.Int, Default = 100, Min = 0, Max = 100 },
        FieldSchema.Bool("Sitting", "Sitting"),
    });

    public static readonly DbSchema Trade = DbSchema.Nested("ItemTrade", new[]
    {
        new FieldSchema { Name = "Override", Label = "Override", Kind = FieldKind.Int, Default = 100, Min = 0, Max = 100 },
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
            new FieldSchema { Name = "AegisName", Label = "Aegis Name", Kind = FieldKind.String, Group = GGeneral, Description = "Server name (no spaces).", IsRequired = true, Unique = true, MaxLength = 50, MaxLengthSeverity = ValidationSeverity.Error },
            new FieldSchema { Name = "Name", Label = "Name", Kind = FieldKind.String, IsDisplay = true, Group = GGeneral, MaxLength = 50 },
            FieldSchema.EnumField("Type", "Type", ItemEnums.Type, "Etc", GGeneral),
            new FieldSchema
            {
                Name = "SubType", Label = "Sub Type", Kind = FieldKind.Enum, Enum = ItemEnums.SubType, Group = GGeneral,
                IsApplicable = r => r.GetString("Type") is "Weapon" or "Ammo" or "Card",
                ResetOnChangeOf = "Type", // a weapon subtype is invalid once the item becomes ammo/card
                EnumSelector = r => r.GetString("Type") switch  // show only the Type's own subtypes
                {
                    "Weapon" => ItemEnums.WeaponSubType,
                    "Ammo" => ItemEnums.AmmoSubType,
                    "Card" => ItemEnums.CardSubType,
                    _ => ItemEnums.SubType,
                },
            },
            FieldSchema.Int("Buy", "Buy", group: GGeneral),
            FieldSchema.Int("Sell", "Sell", group: GGeneral),
            FieldSchema.Int("Weight", "Weight", group: GGeneral),

            new FieldSchema { Name = "Attack", Label = "Attack", Kind = FieldKind.Int, Default = 0, Group = GCombat, IsApplicable = IsWeaponOrAmmo },
            new FieldSchema { Name = "MagicAttack", Label = "Magic Attack", Kind = FieldKind.Int, Default = 0, Group = GCombat, Renewal = RenewalScope.RenewalOnly, IsApplicable = IsWeapon },
            new FieldSchema { Name = "Defense", Label = "Defense", Kind = FieldKind.Int, Default = 0, Group = GCombat, IsApplicable = IsArmorOrShadow },
            new FieldSchema { Name = "Range", Label = "Range", Kind = FieldKind.Int, Default = 0, Group = GCombat, IsApplicable = IsWeapon },
            new FieldSchema { Name = "Slots", Label = "Slots", Kind = FieldKind.Int, Default = 0, Group = GCombat, Min = 0, Max = 4, IsApplicable = IsEquipType },
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
            new FieldSchema { Name = "Locations", Label = "Locations", Kind = FieldKind.BoolMap, Enum = ItemEnums.Locations, Group = GRestrict, IsApplicable = HasEquipLocation },
            FieldSchema.Int("EquipLevelMin", "Equip Level Min", group: GRestrict),
            FieldSchema.Int("EquipLevelMax", "Equip Level Max", group: GRestrict),
            new FieldSchema { Name = "Refineable", Label = "Refineable", Kind = FieldKind.Bool, Default = false, Group = GRestrict, IsApplicable = IsEquipType },
            new FieldSchema { Name = "Gradable", Label = "Gradable", Kind = FieldKind.Bool, Default = false, Group = GRestrict, Renewal = RenewalScope.RenewalOnly, IsApplicable = IsWeaponOrArmor },
            new FieldSchema { Name = "View", Label = "View (sprite id)", Kind = FieldKind.Int, Default = 0, Group = GRestrict, IsApplicable = IsEquipType },
            new FieldSchema { Name = "AliasName", Label = "Alias Name", Kind = FieldKind.Reference, Enum = EnumSource.Reference("ItemAlias", "item_db"), Group = GRestrict, ReferenceSeverity = ValidationSeverity.Error },

            FieldSchema.ObjectField("Flags", "Flags", Flags, GFlags),
            new FieldSchema { Name = "Delay", Label = "Delay", Kind = FieldKind.Object, ObjectSchema = Delay, Group = GFlags, IsApplicable = IsUsable },
            new FieldSchema { Name = "Stack", Label = "Stack", Kind = FieldKind.Object, ObjectSchema = Stack, Group = GFlags, IsApplicable = IsStackable },
            new FieldSchema { Name = "NoUse", Label = "No Use", Kind = FieldKind.Object, ObjectSchema = NoUse, Group = GFlags, IsApplicable = IsUsable },
            FieldSchema.ObjectField("Trade", "Trade", Trade, GFlags),

            FieldSchema.ScriptField("Script", "Script", GScripts),
            FieldSchema.ScriptField("EquipScript", "Equip Script", GScripts),
            FieldSchema.ScriptField("UnEquipScript", "Unequip Script", GScripts),
        },
    };
}
