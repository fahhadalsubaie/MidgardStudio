using System.Collections.Generic;
using MidgardStudio.Core.Model;
using MidgardStudio.Core.Overlay;
using MidgardStudio.Core.Schemas;

namespace MidgardStudio.Core.Validation.Validators;

/// <summary>
/// Bespoke mob_avail rules the schema metadata can't express: the 10 cosmetic fields are rejected by rAthena
/// unless Sprite is a player job, and PetEquip is rejected unless the Mob is a defined pet. Each offers a
/// one-click Clear so a server-rejecting entry can't be saved. (Mob/item references + Sprite resolution are
/// already covered by the schema-driven reference checks.)
/// </summary>
public sealed class MobAvailValidator : IRecordValidator
{
    private static readonly string[] JobOnly =
        { "Sex", "HairStyle", "HairColor", "ClothColor", "Weapon", "Shield", "HeadTop", "HeadMid", "HeadLow", "Robe" };

    public bool AppliesTo(string dbId) => dbId == "mob_avail";

    public IEnumerable<ValidationIssue> Validate(DbRecord record, OverlayTable table, ValidationContext context)
    {
        string key = record.Key.ToString();

        if (!MobAvailConstants.IsJobSprite(record.GetString("Sprite")))
        {
            foreach (var field in JobOnly)
            {
                if (!HasValue(record, field)) continue;
                yield return new ValidationIssue(ValidationSeverity.Error, "mob_avail", key, field,
                    $"'{field}' only applies when Sprite is a player job — rAthena rejects this entry. Clear it, or set Sprite to a JOB_* sprite.")
                {
                    RuleId = "MOBAVAIL.JOB_ONLY_FIELD",
                    Fix = ClearFix(record, field),
                };
            }
        }

        if (HasValue(record, "PetEquip"))
        {
            string mob = record.GetString("Mob") ?? string.Empty;
            if (!context.References.Contains("pet_db", mob))
                yield return new ValidationIssue(ValidationSeverity.Error, "mob_avail", key, "PetEquip",
                    $"PetEquip only applies when the Mob ('{mob}') is a defined pet (pet_db). Clear it, or use a pet mob.")
                {
                    RuleId = "MOBAVAIL.PETEQUIP_NOT_PET",
                    Fix = ClearFix(record, "PetEquip"),
                };
        }
    }

    private static bool HasValue(DbRecord r, string field) => r.Get(field) switch
    {
        null => false,
        string s => !string.IsNullOrEmpty(s),
        int i => i != 0,
        long l => l != 0,
        _ => true,
    };

    private static QuickFix? ClearFix(DbRecord record, string field)
    {
        if (record.Origin == RecordOrigin.Base) return null; // never edit base data
        var old = record.Get(field);
        // Deterministic: the field doesn't apply in this context, so removing it is the single correct fix.
        return new QuickFix($"Clear {field}", () => record.Remove(field), () => record.SetRaw(field, old)) { Automatic = true };
    }
}
