using System.Collections.Generic;
using MidgardStudio.Core.Model;

namespace MidgardStudio.Core.Validation;

/// <summary>
/// The item cross-file rules (server <c>item_db</c> vs client itemInfo / GRF / accessory map), lifted out
/// of the App so they're unit-testable: every dependency is a port. Reads go through
/// <see cref="IClientItemProbe"/> / <see cref="IGrfIconProbe"/> / <see cref="IAccessoryMapProbe"/>;
/// quick-fixes mutate through <see cref="IClientItemEditor"/>. Findings carry stable RuleIds and
/// <c>DbId="client_items"</c>, matching the App's previous output exactly.
/// </summary>
public static class ItemClientFileValidator
{
    public static IEnumerable<ValidationIssue> Validate(
        IEnumerable<DbRecord> items, ValidationScope scope,
        IClientItemProbe client, IGrfIconProbe grf, IAccessoryMapProbe accmap, IClientItemEditor editor)
    {
        // Parse the accessory view map once for the whole pass.
        var mappedViews = accmap.IsAvailable ? accmap.MappedViewIds() : (IReadOnlySet<int>)new HashSet<int>();

        foreach (var rec in items)
        {
            if (scope == ValidationScope.CustomOnly && rec.Origin == RecordOrigin.Base) continue;
            int id = rec.GetInt("Id");
            string key = id.ToString();

            if (!client.Exists(id))
            {
                string name = rec.GetString("Name") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name)) name = rec.GetString("AegisName") ?? $"Item {id}";
                int newSlots = rec.GetInt("Slots");
                int newView = rec.GetInt("View");
                yield return new ValidationIssue(ValidationSeverity.Warning, "client_items", key, "client",
                    "Item doesn't exist in Client Items (itemInfo.lua / itemInfo_C.lua) — it will show no name or description in-game.")
                {
                    RuleId = "XFILE.ITEM_NO_CLIENTTEXT",
                    // Not Automatic: creating a client entry seeds only name/slots/view (placeholder description),
                    // so it needs the user to finish it — apply, then jump to the new record to confirm it.
                    Fix = new QuickFix("Create client text", () => editor.CreateText(id, name, newSlots, newView), () => editor.Remove(id))
                        { ButtonLabel = "Create", ReviewAfter = true },
                };
                continue;
            }

            var entry = client.Get(id)!;

            int slots = rec.GetInt("Slots");
            if (entry.SlotCount != slots)
            {
                int oldSlots = entry.SlotCount;
                yield return new ValidationIssue(ValidationSeverity.Warning, "client_items", key, "SlotCount",
                    $"Slots count mismatch — Server [{slots}], Client [{entry.SlotCount}].")
                {
                    RuleId = "XFILE.SLOTCOUNT_MISMATCH",
                    Fix = new QuickFix($"Set client slots to {slots}", () => editor.SetSlots(id, slots), () => editor.SetSlots(id, oldSlots)) { Automatic = true },
                };
            }

            var loc = rec.GetSet("Locations");
            bool isHeadgear = loc is not null
                && (loc.Contains("Head_Top") || loc.Contains("Head_Mid") || loc.Contains("Head_Low")
                    || loc.Contains("Costume_Head_Top") || loc.Contains("Costume_Head_Mid") || loc.Contains("Costume_Head_Low"));
            bool isGarment = loc is not null && (loc.Contains("Garment") || loc.Contains("Costume_Garment"));

            // Server View only drives the broadcast worn sprite for headgear/costume-head and garment, where it
            // must equal the client ClassNum. For weapons (sprite from SubType), accessories, cards and generic
            // items the two are independent — so don't flag a mismatch there.
            int view = rec.GetInt("View");
            if ((isHeadgear || isGarment) && entry.ClassNum != view)
            {
                int oldView = entry.ClassNum;
                yield return new ValidationIssue(ValidationSeverity.Warning, "client_items", key, "ClassNum",
                    $"View / ClassNum mismatch — Server View [{view}], Client ClassNum [{entry.ClassNum}]. " +
                    "For headgear and garments these must match or the equipped sprite won't appear.")
                {
                    RuleId = "XFILE.CLASSNUM_MISMATCH",
                    Fix = new QuickFix($"Set client ClassNum to {view}", () => editor.SetClassNum(id, view), () => editor.SetClassNum(id, oldView)) { Automatic = true },
                };
            }

            if (grf.IsConfigured && !string.IsNullOrEmpty(entry.IdentifiedResourceName)
                && !grf.IconExists(entry.IdentifiedResourceName))
                yield return new ValidationIssue(ValidationSeverity.Warning, "client_items", key, "icon",
                    $"Inventory icon '{entry.IdentifiedResourceName}.bmp' not found in the configured GRF.")
                { RuleId = "XFILE.ICON_MISSING" };

            if (isHeadgear && view > 0 && accmap.IsAvailable && !mappedViews.Contains(view))
                yield return new ValidationIssue(ValidationSeverity.Warning, "client_items", key, "View",
                    $"Headgear View {view} is not mapped in accessoryid.lub / accname.lub — the sprite won't show.")
                { RuleId = "XFILE.HEADGEAR_NO_ACCMAP" };
        }
    }
}
