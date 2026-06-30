using System;
using System.Collections.Generic;
using System.Linq;
using MidgardStudio.Core.Validation;

namespace MidgardStudio.Core.CashShop;

/// <summary>
/// Pure cash-shop validation over the effective (base ∪ custom) items, surfaced in the global Validation panel
/// and the save gate. Rules (ADR-0004): the item must exist in item_db (<b>error</b> — the server silently
/// drops unknown cash entries); a duplicate item within the same tab (<b>warning</b>); a price of 0 or less
/// (<b>warning</b>). The same item across <i>different</i> tabs is allowed (the cash shop supports that).
/// </summary>
public static class CashShopValidator
{
    /// <summary>The synthetic db id for cash-shop findings (drives "Go To" → the Cash Shop Manager).</summary>
    public const string DbId = "item_cash";

    private const string Category = "Cash Shop";

    /// <summary>rAthena reads Price as uint32 and rejects (drops the WHOLE tab) anything above MAX_CASHPOINT
    /// (= INT_MAX). The app stores Price as a 64-bit long, so this bound must be enforced on the write path.</summary>
    public const long MaxCashPoint = int.MaxValue;

    public static IReadOnlyList<ValidationIssue> Validate(CashShopData data, IReadOnlySet<string> knownItems)
    {
        var issues = new List<ValidationIssue>();
        foreach (var tab in CashShopData.Tabs)
        {
            var items = data.Effective(tab).ToList();
            if (items.Count == 0) continue;

            var counts = items.GroupBy(i => i.Item, StringComparer.OrdinalIgnoreCase)
                              .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            var dupReported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string key = tab.ToString();

            foreach (var it in items)
            {
                if (!knownItems.Contains(it.Item))
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, DbId, key, "Item",
                        $"'{it.Item}' is not an item in item_db — the server will ignore this cash-shop entry.")
                    { RuleId = "CASHSHOP.UNKNOWN_ITEM", Category = Category });

                if (it.Price <= 0)
                    issues.Add(new ValidationIssue(ValidationSeverity.Warning, DbId, key, "Price",
                        $"'{it.Item}' in the {tab} tab has a price of {it.Price} cash points.")
                    { RuleId = "CASHSHOP.PRICE_ZERO", Category = Category });

                if (it.Price > MaxCashPoint)
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, DbId, key, "Price",
                        $"'{it.Item}' in the {tab} tab has a price of {it.Price}, above the maximum {MaxCashPoint} — the server drops the entire {tab} tab.")
                    { RuleId = "CASHSHOP.PRICE_OVERFLOW", Category = Category });

                if (counts[it.Item] > 1 && dupReported.Add(it.Item))
                    issues.Add(new ValidationIssue(ValidationSeverity.Warning, DbId, key, "Item",
                        $"'{it.Item}' appears {counts[it.Item]} times in the {tab} tab.")
                    { RuleId = "CASHSHOP.DUP_IN_TAB", Category = Category });
            }
        }
        return issues;
    }

    /// <summary>Merged per-item severity + tooltip for the editor's inline row badge (same three rules as
    /// <see cref="Validate"/>, scoped to one item). <paramref name="sameNameInTabCount"/> is how many times the
    /// item's name occurs in its tab.</summary>
    public static (ValidationSeverity? Severity, string? Message) Check(
        string item, long price, int sameNameInTabCount, IReadOnlySet<string> knownItems)
    {
        var msgs = new List<string>();
        ValidationSeverity? severity = null;
        if (!knownItems.Contains(item)) { msgs.Add("Not an item in item_db."); severity = ValidationSeverity.Error; }
        if (price > MaxCashPoint) { msgs.Add($"Price exceeds {MaxCashPoint}; the server drops the whole tab."); severity = ValidationSeverity.Error; }
        if (price <= 0) { msgs.Add("Price is 0 cash points."); severity ??= ValidationSeverity.Warning; }
        if (sameNameInTabCount > 1) { msgs.Add("Duplicated in this tab."); severity ??= ValidationSeverity.Warning; }
        return (severity, msgs.Count == 0 ? null : string.Join(" ", msgs));
    }
}
