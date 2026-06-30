using System.Collections.Generic;
using System.Linq;
using YamlDotNet.Serialization;

namespace MidgardStudio.Core.CashShop;

/// <summary>
/// Bespoke reader/writer for <c>item_cash.yml</c> (the cash shop is a fixed-tab, nested, all-custom db that
/// fits the generic schema engine badly — see ADR-0004). Reads base ∪ import into a <see cref="CashShopData"/>;
/// writes only the import (Custom) layer back, omitting empty tabs and preserving item order. Shared DTOs keep
/// the read and write shapes from drifting.
/// </summary>
public static class CashShopYaml
{
    public const string HeaderType = "ITEM_CASH_DB";
    public const int HeaderVersion = 1;

    private static readonly IDeserializer Deserializer =
        new DeserializerBuilder().IgnoreUnmatchedProperties().Build();

    private static readonly ISerializer Serializer =
        new SerializerBuilder()
            .WithIndentedSequences() // sequences indent under their key, matching rAthena's base files
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull) // drop the Body key entirely when empty
            .Build();

    /// <summary>Parses the base + import documents into one model. Either may be null/blank (missing file);
    /// a malformed document is treated as empty rather than crashing the editor.</summary>
    public static CashShopData Load(string? baseYaml, string? importYaml)
    {
        var data = new CashShopData();
        foreach (var (tab, item) in ParseEntries(baseYaml)) data.AddBase(tab, item);
        foreach (var (tab, item) in ParseEntries(importYaml)) data.AddCustom(tab, item);
        // Preserve any import tab the 9-member enum doesn't know (typo / future constant) so Save can't drop it.
        foreach (var unknown in ParseUnknownTabs(importYaml)) data.AddUnknownImportTab(unknown);
        return data;
    }

    /// <summary>Serializes the editable (import) layer as a rAthena <c>ITEM_CASH_DB</c> document: Header +
    /// Body of the non-empty tabs (in tab order, items in order). When nothing is custom, only the Header is
    /// emitted (matching the shipped, body-less base file).</summary>
    public static string Write(CashShopData data)
    {
        var body = CashShopData.Tabs
            .Select(tab => (tab, items: data.Custom(tab)))
            .Where(t => t.items.Count > 0)
            .Select(t => new TabDto
            {
                Tab = t.tab.ToString(),
                Items = t.items.Select(i => new EntryDto { Item = i.Item, Price = i.Price }).ToList(),
            })
            .ToList();

        // Re-emit any preserved unknown tabs after the known ones, so a hand-added/future tab round-trips.
        body.AddRange(data.UnknownImportTabs.Select(u => new TabDto
        {
            Tab = u.Tab,
            Items = u.Items.Select(i => new EntryDto { Item = i.Item, Price = i.Price }).ToList(),
        }));

        var doc = new DocDto
        {
            Header = new HeaderDto { Type = HeaderType, Version = HeaderVersion },
            Body = body.Count > 0 ? body : null,
        };
        return Serializer.Serialize(doc);
    }

    private static IEnumerable<(CashShopTab Tab, CashItem Item)> ParseEntries(string? yaml)
    {
        var doc = TryDeserialize(yaml);
        if (doc?.Body is null) yield break;

        foreach (var tab in doc.Body)
        {
            if (tab?.Tab is null || tab.Items is null) continue;
            if (!System.Enum.TryParse<CashShopTab>(tab.Tab, ignoreCase: true, out var parsed)) continue; // skip unknown tab
            foreach (var entry in tab.Items)
            {
                if (string.IsNullOrEmpty(entry?.Item)) continue;
                yield return (parsed, new CashItem(entry.Item, entry.Price));
            }
        }
    }

    /// <summary>Yields the import tab blocks whose name isn't a known <see cref="CashShopTab"/> — captured so
    /// the writer can preserve them instead of silently dropping them (the bespoke model's Extras equivalent).</summary>
    private static IEnumerable<CashShopUnknownTab> ParseUnknownTabs(string? yaml)
    {
        var doc = TryDeserialize(yaml);
        if (doc?.Body is null) yield break;

        foreach (var tab in doc.Body)
        {
            if (tab?.Tab is null || tab.Items is null) continue;
            if (System.Enum.TryParse<CashShopTab>(tab.Tab, ignoreCase: true, out _)) continue; // known -> handled by ParseEntries
            var items = tab.Items
                .Where(e => !string.IsNullOrEmpty(e?.Item))
                .Select(e => new CashItem(e!.Item!, e.Price))
                .ToList();
            yield return new CashShopUnknownTab { Tab = tab.Tab, Items = items };
        }
    }

    private static DocDto? TryDeserialize(string? yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml)) return null;
        try { return Deserializer.Deserialize<DocDto>(yaml); }
        catch { return null; } // malformed -> treated as empty; the validator/UI surface the real state
    }

    // Shared YAML shape (PascalCase keys map 1:1 to the file). Used for both read and write.
    private sealed class DocDto
    {
        public HeaderDto? Header { get; set; }
        public List<TabDto>? Body { get; set; }
    }

    private sealed class HeaderDto
    {
        public string? Type { get; set; }
        public int Version { get; set; }
    }

    private sealed class TabDto
    {
        public string? Tab { get; set; }
        public List<EntryDto>? Items { get; set; }
    }

    private sealed class EntryDto
    {
        public string? Item { get; set; }
        public long Price { get; set; }
    }
}
