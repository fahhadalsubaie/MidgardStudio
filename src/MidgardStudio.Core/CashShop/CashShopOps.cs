using System;
using System.Collections.Generic;
using System.Linq;

namespace MidgardStudio.Core.CashShop;

/// <summary>Pure list operations for the cash-shop editor's drag-reorder, kept in Core so the index
/// arithmetic is unit-testable (the drag-drop gesture itself lives in the view).</summary>
public static class CashShopOps
{
    /// <summary>A new ordering of <paramref name="items"/> with <paramref name="item"/> moved so it lands at
    /// <paramref name="targetIndex"/> (the drop position in the original list; clamped). Accounts for the gap
    /// left when the item is lifted out, so dropping an item past its own position behaves intuitively.</summary>
    public static List<CashItem> MovedWithin(IReadOnlyList<CashItem> items, CashItem item, int targetIndex)
    {
        var result = items.ToList();
        int current = result.IndexOf(item);
        if (current < 0) return result;

        result.RemoveAt(current);
        int target = targetIndex;
        if (target > current) target--;          // the removal shifted everything after `current` left by one
        target = Math.Clamp(target, 0, result.Count);
        result.Insert(target, item);
        return result;
    }
}
