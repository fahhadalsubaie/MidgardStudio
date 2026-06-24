using MidgardStudio.Core.Schema;

namespace MidgardStudio.Core.Schemas;

/// <summary>
/// Schema for mob_avail (MOB_AVAIL_DB v1): reuse another mob's (or a job/player) sprite for a mob.
/// Data lives only in the import layer. Extra job-sprite fields (Sex, HairStyle, Weapon, ...) are
/// preserved verbatim via Extras.
/// </summary>
public static class MobAvailSchema
{
    public static readonly DbSchema Instance = new()
    {
        Id = "mob_avail",
        DisplayName = "Mob Sprite Reuse",
        HeaderType = "MOB_AVAIL_DB",
        HeaderVersion = 1,
        Key = KeyStrategy.Str("Mob"),
        Layout = new FileLayout
        {
            RenewalFiles = Array.Empty<string>(),
            PreRenewalFiles = Array.Empty<string>(),
            ImportFile = "import/mob_avail.yml",
        },
        Fields = new[]
        {
            new FieldSchema { Name = "Mob", Label = "Mob", Kind = FieldKind.Reference, IsKey = true, IsDisplay = true, Enum = EnumSource.Reference("AvailMob", "mob_db") },
            new FieldSchema { Name = "Sprite", Label = "Sprite (mob to copy)", Kind = FieldKind.Reference, Enum = EnumSource.Reference("AvailSprite", "mob_db") },
        },
    };
}
