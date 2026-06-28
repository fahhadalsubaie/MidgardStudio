using System;
using System.Collections.Generic;
using System.Linq;

namespace MidgardStudio.Core.CashShop;

/// <summary>One item_db row the cash shop cares about: its id, Aegis (script) name, and display name.</summary>
public sealed record ItemRef(int Id, string AegisName, string Name);

/// <summary>An autocomplete suggestion for the cash-shop add box — carries the canonical Aegis name to store
/// plus a friendly label to show.</summary>
public sealed record ItemSuggestion(string AegisName, string Name, int Id)
{
    /// <summary>"Display Name  ·  AegisName  ·  #Id", omitting a blank display name.</summary>
    public string Label => string.Join("  ·  ",
        new[] { string.IsNullOrWhiteSpace(Name) ? null : Name, AegisName, "#" + Id }.Where(s => !string.IsNullOrEmpty(s)));
}

/// <summary>
/// Resolves an item_db reference typed as a display <b>Name</b>, an <b>AegisName</b>, or a numeric <b>Id</b>
/// down to the canonical Aegis name (what <c>item_cash.yml</c> stores), and powers the add box's autocomplete.
/// Built from the item_db overlay; pure and unit-testable. AegisName and Id are unique; the display Name may
/// repeat, so a Name matching more than one item is reported <see cref="ResolveStatus.Ambiguous"/> rather than
/// guessed.
/// </summary>
public sealed class CashShopItemIndex
{
    private readonly List<ItemRef> _items;
    private readonly Dictionary<string, ItemRef> _byAegis = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, ItemRef> _byId = new();
    private readonly Dictionary<string, List<ItemRef>> _byName = new(StringComparer.OrdinalIgnoreCase);

    public CashShopItemIndex(IEnumerable<ItemRef> items)
    {
        _items = items.Where(i => !string.IsNullOrEmpty(i.AegisName)).ToList();
        foreach (var it in _items)
        {
            _byAegis[it.AegisName] = it;
            _byId[it.Id] = it;
            if (!string.IsNullOrWhiteSpace(it.Name))
            {
                if (!_byName.TryGetValue(it.Name, out var list)) _byName[it.Name] = list = new List<ItemRef>();
                list.Add(it);
            }
        }
    }

    public int Count => _items.Count;

    /// <summary>Every known Aegis name — the validator's membership set.</summary>
    public IReadOnlyCollection<string> AegisNames => _byAegis.Keys;

    /// <summary>True if this exact Aegis name is a known item.</summary>
    public bool ContainsAegis(string aegisName) => _byAegis.ContainsKey(aegisName);

    /// <summary>The display Name for an Aegis name (card subtitle), or null.</summary>
    public string? DisplayName(string aegisName) => _byAegis.TryGetValue(aegisName, out var it) ? it.Name : null;

    /// <summary>The item id for an Aegis name (icon resolution), or null.</summary>
    public int? IdOf(string aegisName) => _byAegis.TryGetValue(aegisName, out var it) ? it.Id : null;

    public enum ResolveStatus { Resolved, NotFound, Ambiguous }

    /// <summary>Resolves a token (numeric Id, Aegis name, or display Name) to a unique Aegis name. Precedence:
    /// a pure number resolves by Id; else an exact Aegis name; else an exact display Name (which must be
    /// unique, else <see cref="ResolveStatus.Ambiguous"/>).</summary>
    public (ResolveStatus Status, string? Aegis) Resolve(string? token)
    {
        var t = (token ?? string.Empty).Trim();
        if (t.Length == 0) return (ResolveStatus.NotFound, null);

        if (int.TryParse(t, out var id) && _byId.TryGetValue(id, out var byId))
            return (ResolveStatus.Resolved, byId.AegisName);

        if (_byAegis.TryGetValue(t, out var byAegis))
            return (ResolveStatus.Resolved, byAegis.AegisName);

        if (_byName.TryGetValue(t, out var byName))
            return byName.Count == 1 ? (ResolveStatus.Resolved, byName[0].AegisName) : (ResolveStatus.Ambiguous, null);

        return (ResolveStatus.NotFound, null);
    }

    /// <summary>Top autocomplete matches for a query, searched across Aegis name, display Name, and Id.</summary>
    public IReadOnlyList<ItemSuggestion> Suggest(string? query, int max = 20)
    {
        var q = (query ?? string.Empty).Trim();
        if (q.Length == 0) return Array.Empty<ItemSuggestion>();
        bool numeric = int.TryParse(q, out var qid);

        return _items
            .Select(it => (it, rank: Rank(it, q, numeric, qid)))
            .Where(x => x.rank < int.MaxValue)
            .OrderBy(x => x.rank)
            .ThenBy(x => x.it.AegisName, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .Select(x => new ItemSuggestion(x.it.AegisName, x.it.Name, x.it.Id))
            .ToList();
    }

    // Lower rank = better match. Exact/prefix beats substring; id/aegis beats display name.
    private static int Rank(ItemRef it, string q, bool numeric, int qid)
    {
        if (numeric)
        {
            if (it.Id == qid) return 0;
            if (it.Id.ToString().StartsWith(q, StringComparison.Ordinal)) return 4;
        }
        if (it.AegisName.Equals(q, StringComparison.OrdinalIgnoreCase)) return 1;
        if (it.AegisName.StartsWith(q, StringComparison.OrdinalIgnoreCase)) return 2;
        if (!string.IsNullOrEmpty(it.Name) && it.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase)) return 3;
        if (it.AegisName.Contains(q, StringComparison.OrdinalIgnoreCase)) return 5;
        if (!string.IsNullOrEmpty(it.Name) && it.Name.Contains(q, StringComparison.OrdinalIgnoreCase)) return 6;
        return int.MaxValue;
    }
}
