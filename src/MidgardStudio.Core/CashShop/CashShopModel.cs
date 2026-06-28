using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MidgardStudio.Core.CashShop;

/// <summary>
/// The nine fixed rAthena cash-shop tabs (<c>item_cash_db</c> / <c>enum e_cash_shop_tab</c>), in client/DB
/// display order. The enum names match the YAML <c>Tab:</c> values verbatim (New, Hot, …, Sale).
/// </summary>
public enum CashShopTab
{
    New,
    Hot,
    Limited,
    Rental,
    Permanent,
    Scrolls,
    Consumables,
    Other,
    Sale,
}

/// <summary>
/// One cash-shop entry: an item (by Aegis name, exactly as stored in <c>item_cash.yml</c>) and its price in
/// cash points (<c>#CASHPOINTS</c>). Mutable so a price edit can be applied/undone in place by the editor's
/// undo stack.
/// </summary>
public sealed class CashItem
{
    public CashItem(string item, long price)
    {
        Item = item;
        Price = price;
    }

    /// <summary>The item's Aegis name (the <c>Item:</c> field). Stored verbatim so an entry round-trips even
    /// when the referenced item isn't loaded.</summary>
    public string Item { get; set; }

    /// <summary>Cost in cash points.</summary>
    public long Price { get; set; }
}

/// <summary>
/// The bespoke cash-shop model (deliberately NOT the schema-driven OverlayTable — see ADR-0004): nine fixed
/// tabs, each with a read-only <see cref="Base"/> list (from the server's base <c>item_cash.yml</c>, normally
/// empty — it's an all-custom db) and an editable <see cref="Custom"/> list (from <c>db/import/item_cash.yml</c>).
/// The editor mutates Custom only and the writer emits Custom only, so base entries are never written into
/// import. Item order within a tab is preserved (the client renders cash items in DB order).
/// </summary>
public sealed class CashShopData
{
    private readonly Dictionary<CashShopTab, List<CashItem>> _base = NewTabMap();
    private readonly Dictionary<CashShopTab, List<CashItem>> _custom = NewTabMap();

    /// <summary>The nine tabs in display order.</summary>
    public static readonly IReadOnlyList<CashShopTab> Tabs = System.Enum.GetValues<CashShopTab>();

    private static Dictionary<CashShopTab, List<CashItem>> NewTabMap() =>
        Tabs.ToDictionary(t => t, _ => new List<CashItem>());

    /// <summary>The editable (import-layer) items for a tab — mutated by the editor's undoable commands.</summary>
    public List<CashItem> Custom(CashShopTab tab) => _custom[tab];

    /// <summary>The read-only base items for a tab (shown but never written; usually empty).</summary>
    public IReadOnlyList<CashItem> Base(CashShopTab tab) => _base[tab];

    /// <summary>Base then custom, in order — what the shop actually shows in this tab.</summary>
    public IEnumerable<CashItem> Effective(CashShopTab tab) => _base[tab].Concat(_custom[tab]);

    /// <summary>Effective item count for a tab (base + custom) — drives the tab-rail badges.</summary>
    public int Count(CashShopTab tab) => _base[tab].Count + _custom[tab].Count;

    internal void AddBase(CashShopTab tab, CashItem item) => _base[tab].Add(item);

    internal void AddCustom(CashShopTab tab, CashItem item) => _custom[tab].Add(item);

    /// <summary>A canonical text fingerprint of the editable (import) content — drives content-based dirty
    /// detection, so undoing an edit back to the loaded state correctly reports "nothing to save". Order
    /// sensitive (a reorder is a real change) and ignores base items (they're never written).</summary>
    public string Signature()
    {
        var sb = new StringBuilder();
        foreach (var tab in Tabs)
        {
            var items = _custom[tab];
            if (items.Count == 0) continue;
            sb.Append((int)tab).Append(':');
            foreach (var it in items) sb.Append(it.Item).Append('=').Append(it.Price).Append(';');
            sb.Append('\n');
        }
        return sb.ToString();
    }
}
