using MidgardStudio.Core.Model;
using MidgardStudio.Core.Overlay;

namespace MidgardStudio.App.Services;

/// <summary>One occurrence of an item in a mob's drop table (used for the item "Dropped by" view).</summary>
public readonly record struct DropOccurrence(
    RecordKey MobKey, string MobName, string Field, bool IsMvp, int Rate, int Index, DbRecord Drop);

/// <summary>A pickable item/mob row for the drop pickers.</summary>
public readonly record struct PickerItem(int Id, string Aegis, string Name);

/// <summary>
/// Resolves item names for drop tables and computes the reverse "which mobs drop this item" index.
/// Backed by the live item/mob overlays so it always reflects current edits; the item name lookup is
/// cached and rebuilt when the workspace profile changes.
/// </summary>
public sealed class DropService
{
    private readonly WorkspaceSession _session;
    private readonly SchemaRegistry _schemas;
    private Dictionary<string, (int Id, string Name)>? _itemByAegis;

    public DropService(WorkspaceSession session, SchemaRegistry schemas)
    {
        _session = session;
        _schemas = schemas;
        _session.WorkspaceReloaded += () => _itemByAegis = null;
    }

    public OverlayTable Items => _session.GetActiveOverlay(_schemas.Get("item_db")!);

    public OverlayTable Mobs => _session.GetActiveOverlay(_schemas.Get("mob_db")!);

    private Dictionary<string, (int Id, string Name)> ItemByAegis
    {
        get
        {
            if (_itemByAegis is null)
            {
                var map = new Dictionary<string, (int, string)>(StringComparer.OrdinalIgnoreCase);
                foreach (var r in Items.Effective())
                {
                    var aegis = r.GetString("AegisName");
                    if (string.IsNullOrEmpty(aegis)) continue;
                    map[aegis] = (r.GetInt("Id"), r.GetString("Name") ?? aegis);
                }
                _itemByAegis = map;
            }
            return _itemByAegis;
        }
    }

    /// <summary>Item id + display name for a drop's aegis (id 0 / aegis as name if unknown).</summary>
    public (int Id, string Name) ResolveItem(string? aegis) =>
        !string.IsNullOrEmpty(aegis) && ItemByAegis.TryGetValue(aegis!, out var v) ? v : (0, aegis ?? string.Empty);

    public string? AegisForItemId(int id)
    {
        var rec = Items.GetEffective(RecordKey.Of(id));
        return rec?.GetString("AegisName");
    }

    public void InvalidateItemIndex() => _itemByAegis = null;

    public IReadOnlyList<PickerItem> SearchItems(string query, int limit = 60) => Search(Items, query, limit);

    public IReadOnlyList<PickerItem> SearchMobs(string query, int limit = 60) => Search(Mobs, query, limit);

    private static IReadOnlyList<PickerItem> Search(OverlayTable overlay, string query, int limit)
    {
        string q = query.Trim();
        var results = new List<PickerItem>(limit);
        foreach (var r in overlay.Effective())
        {
            string aegis = r.GetString("AegisName") ?? string.Empty;
            string name = r.GetString("Name") ?? aegis;
            int id = r.GetInt("Id");
            if (q.Length == 0
                || id.ToString().Contains(q, StringComparison.OrdinalIgnoreCase)
                || aegis.Contains(q, StringComparison.OrdinalIgnoreCase)
                || name.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(new PickerItem(id, aegis, name));
                if (results.Count >= limit) break;
            }
        }
        return results;
    }

    /// <summary>All mob drop entries (normal + MVP) referencing the given item aegis.</summary>
    public IEnumerable<DropOccurrence> DroppedBy(string? itemAegis)
    {
        if (string.IsNullOrEmpty(itemAegis)) yield break;
        foreach (var mob in Mobs.Effective())
        {
            string mobName = mob.GetString("Name") ?? string.Empty;
            foreach (var (field, isMvp) in Fields)
            {
                var list = mob.GetList(field);
                if (list is null) continue;
                foreach (var d in list)
                    if (string.Equals(d.GetString("Item"), itemAegis, StringComparison.OrdinalIgnoreCase))
                        yield return new DropOccurrence(mob.Key, mobName, field, isMvp, d.GetInt("Rate"), d.GetInt("Index"), d);
            }
        }
    }

    private static readonly (string Field, bool IsMvp)[] Fields = { ("Drops", false), ("MvpDrops", true) };
}
