using System.Text.RegularExpressions;

namespace MidgardStudio.Core.Lua;

/// <summary>Parses client itemInfo lua: the custom/override tables, and the official id set.</summary>
public sealed class ItemInfoReader
{
    private static readonly Regex IdRegex = new(@"\[\s*(\d+)\s*\]\s*=\s*\{", RegexOptions.Compiled);

    public ItemInfoFile ReadCustomFile(string text)
    {
        var file = new ItemInfoFile();
        ReadInto(text, "tbl_custom", file.Custom);
        ReadInto(text, "tbl_override", file.Override);
        return file;
    }

    /// <summary>Fast scan for the set of item ids defined in a (possibly huge) official itemInfo file.</summary>
    public HashSet<int> ReadOfficialIds(string text)
    {
        var set = new HashSet<int>();
        foreach (Match m in IdRegex.Matches(text))
            if (int.TryParse(m.Groups[1].Value, out var id))
                set.Add(id);
        return set;
    }

    /// <summary>Reads the full entries from a base/official/unified itemInfo file (the single <c>tbl</c>
    /// table). Used so core items show their client text + icon resource name.</summary>
    public Dictionary<int, ItemInfoEntry> ReadOfficialFile(string text, string tableName = "tbl")
    {
        var result = new Dictionary<int, ItemInfoEntry>();
        ReadInto(text, tableName, result);
        return result;
    }

    private static void ReadInto(string text, string tableName, Dictionary<int, ItemInfoEntry> target)
    {
        var table = new LuaTableParser(text).ParseNamedTable(tableName);
        if (table is null) return;
        foreach (var (id, value) in table.IntKeys)
            if (value is LuaTable entry)
                target[(int)id] = MapEntry((int)id, entry);
    }

    public static ItemInfoEntry MapEntry(int id, LuaTable t) => new()
    {
        Id = id,
        UnidentifiedDisplayName = t.GetString("unidentifiedDisplayName") ?? string.Empty,
        UnidentifiedResourceName = t.GetString("unidentifiedResourceName") ?? string.Empty,
        UnidentifiedDescription = t.GetStringArray("unidentifiedDescriptionName").ToList(),
        IdentifiedDisplayName = t.GetString("identifiedDisplayName") ?? string.Empty,
        IdentifiedResourceName = t.GetString("identifiedResourceName") ?? string.Empty,
        IdentifiedDescription = t.GetStringArray("identifiedDescriptionName").ToList(),
        SlotCount = t.GetInt("slotCount"),
        ClassNum = t.GetInt("ClassNum"),
        Costume = t.GetBool("costume"),
        EffectId = t.NameKeys.ContainsKey("EffectID") ? t.GetInt("EffectID") : null,
        PackageId = t.NameKeys.ContainsKey("PackageID") ? t.GetInt("PackageID") : null,
        Server = t.GetString("Server"),
        ExtraFields = ExtractExtras(t),
    };

    /// <summary>The name-keys this editor models; everything else in an entry is captured into
    /// <see cref="ItemInfoEntry.ExtraFields"/> and re-emitted verbatim so an edit can't drop it (audit #3).</summary>
    private static readonly HashSet<string> ModeledKeys = new(StringComparer.Ordinal)
    {
        "unidentifiedDisplayName", "unidentifiedResourceName", "unidentifiedDescriptionName",
        "identifiedDisplayName", "identifiedResourceName", "identifiedDescriptionName",
        "slotCount", "ClassNum", "costume", "EffectID", "PackageID", "Server",
    };

    private static Dictionary<string, string> ExtractExtras(LuaTable t)
    {
        var extra = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in t.NameKeys)
            if (!ModeledKeys.Contains(key))
                extra[key] = LuaValue.Serialize(value);
        return extra;
    }
}

/// <summary>
/// Lazy reader over a base/official/unified itemInfo.lua. The (possibly multi-MB) file is indexed once
/// — a single string-aware brace walk records each entry's span — and individual entries are parsed only
/// when requested, so opening a base item never blocks on parsing the whole file.
/// </summary>
public sealed class OfficialItemInfo
{
    private readonly string _text;
    private readonly Dictionary<int, LuaScan.IntKeyBlock> _blocks;
    private readonly Dictionary<int, ItemInfoEntry?> _cache = new();

    public OfficialItemInfo(string text, string tableName = "tbl")
    {
        _text = text ?? string.Empty;
        int open = string.IsNullOrEmpty(_text) ? -1 : LuaScan.FindTableOpen(_text, tableName);
        _blocks = open < 0 ? new Dictionary<int, LuaScan.IntKeyBlock>() : LuaScan.ScanIntKeyTables(_text, open).Blocks;
    }

    public IReadOnlyCollection<int> Ids => _blocks.Keys;

    public bool Contains(int id) => _blocks.ContainsKey(id);

    /// <summary>Parses (and caches) the entry for an id, or null if absent.</summary>
    public ItemInfoEntry? Entry(int id)
    {
        if (_cache.TryGetValue(id, out var cached)) return cached;

        ItemInfoEntry? entry = null;
        if (_blocks.TryGetValue(id, out var b))
        {
            string block = _text.Substring(b.ValueOpen, b.ValueClose - b.ValueOpen + 1);
            var table = new LuaTableParser("__e = " + block).ParseNamedTable("__e");
            if (table is not null) entry = ItemInfoReader.MapEntry(id, table);
        }

        _cache[id] = entry;
        return entry;
    }
}

public enum ItemInfoTarget
{
    Custom,
    Override,
}

/// <summary>Decides whether an item's client entry belongs in tbl_custom (new id) or tbl_override
/// (id already exists in the official itemInfo, so a custom entry would be silently ignored).</summary>
public static class ItemInfoRouter
{
    public static ItemInfoTarget RouteFor(int id, ISet<int> officialIds) =>
        officialIds.Contains(id) ? ItemInfoTarget.Override : ItemInfoTarget.Custom;
}
