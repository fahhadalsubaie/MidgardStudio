using System.Collections.Generic;
using System.Linq;
using MidgardStudio.Core.CashShop;
using MidgardStudio.Core.Validation;

namespace MidgardStudio.Tests;

/// <summary>The bespoke cash-shop model + reader/writer + validator (ADR-0004): base ∪ import read,
/// import-only write, empty tabs omitted, order preserved, and the three validation rules.</summary>
public class CashShopTests
{
    private const string ImportYaml = """
        Header:
          Type: ITEM_CASH_DB
          Version: 1
        Body:
          - Tab: New
            Items:
              - Item: Apple
                Price: 100
              - Item: Banana
                Price: 200
          - Tab: Sale
            Items:
              - Item: Apple
                Price: 50
        """;

    [Fact]
    public void Reads_tabs_and_items_into_the_custom_layer()
    {
        var data = CashShopYaml.Load(baseYaml: null, importYaml: ImportYaml);

        Assert.Equal(new[] { "Apple", "Banana" }, data.Custom(CashShopTab.New).Select(i => i.Item));
        Assert.Equal(100, data.Custom(CashShopTab.New)[0].Price);
        Assert.Single(data.Custom(CashShopTab.Sale));
        Assert.Empty(data.Custom(CashShopTab.Hot));
    }

    [Fact]
    public void Write_round_trips_through_read_preserving_tabs_items_and_order()
    {
        var data = CashShopYaml.Load(null, ImportYaml);
        var reloaded = CashShopYaml.Load(null, CashShopYaml.Write(data));

        // Same tabs, items, prices, and order survive a write→read cycle.
        Assert.Equal(new[] { "Apple", "Banana" }, reloaded.Custom(CashShopTab.New).Select(i => i.Item));
        Assert.Equal(new long[] { 100, 200 }, reloaded.Custom(CashShopTab.New).Select(i => i.Price));
        Assert.Equal("Apple", reloaded.Custom(CashShopTab.Sale).Single().Item);
    }

    [Fact]
    public void Base_items_are_read_but_never_written_to_import()
    {
        string baseYaml = """
            Header:
              Type: ITEM_CASH_DB
              Version: 1
            Body:
              - Tab: Hot
                Items:
                  - Item: Carrot
                    Price: 999
            """;
        var data = CashShopYaml.Load(baseYaml, importYaml: null);

        // Visible in the effective view…
        Assert.Equal("Carrot", data.Effective(CashShopTab.Hot).Single().Item);
        Assert.Empty(data.Custom(CashShopTab.Hot));

        // …but absent from the written import (no custom items → header only, no Body).
        string written = CashShopYaml.Write(data);
        Assert.DoesNotContain("Carrot", written);
        Assert.DoesNotContain("Body", written);
        Assert.Contains("ITEM_CASH_DB", written);
    }

    [Fact]
    public void Empty_tabs_are_omitted_from_the_written_body()
    {
        var data = CashShopYaml.Load(null, ImportYaml);
        string written = CashShopYaml.Write(data);

        Assert.Contains("Tab: New", written);
        Assert.Contains("Tab: Sale", written);
        Assert.DoesNotContain("Tab: Hot", written);
        Assert.DoesNotContain("Tab: Permanent", written);
    }

    [Fact]
    public void Unknown_tab_names_are_skipped()
    {
        string yaml = """
            Header:
              Type: ITEM_CASH_DB
              Version: 1
            Body:
              - Tab: Bogus
                Items:
                  - Item: Apple
                    Price: 1
            """;
        var data = CashShopYaml.Load(null, yaml);
        Assert.All(CashShopData.Tabs, t => Assert.Empty(data.Custom(t)));
    }

    [Fact]
    public void Unknown_import_tab_is_preserved_and_re_emitted() // audit #25
    {
        string yaml = """
            Header:
              Type: ITEM_CASH_DB
              Version: 1
            Body:
              - Tab: New
                Items:
                  - Item: Apple
                    Price: 100
              - Tab: Bogus
                Items:
                  - Item: Mystery
                    Price: 7
            """;
        var data = CashShopYaml.Load(null, yaml);

        // The unknown tab isn't a known tab, but it's captured (not dropped) ...
        var unknown = Assert.Single(data.UnknownImportTabs);
        Assert.Equal("Bogus", unknown.Tab);
        Assert.Equal("Mystery", unknown.Items.Single().Item);

        // ... and survives a write -> read cycle byte-for-content, alongside the known tab.
        var reloaded = CashShopYaml.Load(null, CashShopYaml.Write(data));
        Assert.Equal("Apple", reloaded.Custom(CashShopTab.New).Single().Item);
        Assert.Equal("Mystery", reloaded.UnknownImportTabs.Single().Items.Single().Item);
    }

    [Fact]
    public void Validator_flags_a_price_above_max_cashpoint() // audit #6
    {
        string yaml = """
            Header:
              Type: ITEM_CASH_DB
              Version: 1
            Body:
              - Tab: New
                Items:
                  - Item: Apple
                    Price: 3000000000
            """;
        var issues = CashShopValidator.Validate(CashShopYaml.Load(null, yaml), Known("Apple"));

        var error = Assert.Single(issues, i => i.RuleId == "CASHSHOP.PRICE_OVERFLOW");
        Assert.Equal(ValidationSeverity.Error, error.Severity);
        // And the inline per-item badge agrees.
        Assert.Equal(ValidationSeverity.Error, CashShopValidator.Check("Apple", 3_000_000_000L, 1, Known("Apple")).Severity);
    }

    [Fact]
    public void Malformed_yaml_loads_as_empty_rather_than_throwing()
    {
        var data = CashShopYaml.Load(null, "this: is: not: valid: cash: yaml: [");
        Assert.All(CashShopData.Tabs, t => Assert.Empty(data.Custom(t)));
    }

    // A present-but-malformed import file loads as empty (above) — so it MUST be flagged as unreadable, so the
    // service refuses to regenerate/save over it (a wholesale rewrite from the empty model would silently wipe
    // every entry the file still holds). Blank/missing is legitimately empty; a valid document is readable.
    [Fact]
    public void IsUnreadable_flags_only_a_present_but_unparseable_file()
    {
        Assert.True(CashShopYaml.IsUnreadable("Body:\n  - Tab: New\n    Items:\n      - Item: Apple\n       Price: 1"));
        Assert.True(CashShopYaml.IsUnreadable("this: is: not: valid: cash: yaml: ["));
        Assert.False(CashShopYaml.IsUnreadable(null));
        Assert.False(CashShopYaml.IsUnreadable("   "));
        Assert.False(CashShopYaml.IsUnreadable(ImportYaml));
    }

    private static IReadOnlySet<string> Known(params string[] names) =>
        new HashSet<string>(names, System.StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Validator_flags_an_unknown_item_as_an_error()
    {
        string yaml = """
            Header:
              Type: ITEM_CASH_DB
              Version: 1
            Body:
              - Tab: New
                Items:
                  - Item: Ghost
                    Price: 100
            """;
        var issues = CashShopValidator.Validate(CashShopYaml.Load(null, yaml), Known("Apple"));

        var error = Assert.Single(issues, i => i.Severity == ValidationSeverity.Error);
        Assert.Equal("CASHSHOP.UNKNOWN_ITEM", error.RuleId);
        Assert.Equal("item_cash", error.DbId);
        Assert.Equal("New", error.Key); // Go To opens the New tab
    }

    [Fact]
    public void Validator_warns_on_duplicate_in_tab_and_zero_price()
    {
        string yaml = """
            Header:
              Type: ITEM_CASH_DB
              Version: 1
            Body:
              - Tab: New
                Items:
                  - Item: Apple
                    Price: 0
                  - Item: Apple
                    Price: 100
            """;
        var issues = CashShopValidator.Validate(CashShopYaml.Load(null, yaml), Known("Apple"));

        Assert.Contains(issues, i => i.RuleId == "CASHSHOP.PRICE_ZERO");
        Assert.Single(issues, i => i.RuleId == "CASHSHOP.DUP_IN_TAB"); // reported once per duplicated name
        Assert.DoesNotContain(issues, i => i.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void Validator_allows_the_same_item_across_different_tabs()
    {
        var issues = CashShopValidator.Validate(CashShopYaml.Load(null, ImportYaml), Known("Apple", "Banana"));

        // Apple is in both New and Sale — that's allowed, so no dup/error from the cross-tab repeat.
        Assert.DoesNotContain(issues, i => i.RuleId == "CASHSHOP.DUP_IN_TAB");
        Assert.DoesNotContain(issues, i => i.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void Check_merges_per_item_rules_for_the_inline_badge()
    {
        Assert.Equal(ValidationSeverity.Error, CashShopValidator.Check("Ghost", 100, 1, Known("Apple")).Severity);
        Assert.Equal(ValidationSeverity.Warning, CashShopValidator.Check("Apple", 0, 1, Known("Apple")).Severity);
        Assert.Equal(ValidationSeverity.Warning, CashShopValidator.Check("Apple", 100, 2, Known("Apple")).Severity);
        Assert.Null(CashShopValidator.Check("Apple", 100, 1, Known("Apple")).Severity);
    }

    private static List<CashItem> Items(params string[] names) => names.Select(n => new CashItem(n, 1)).ToList();

    [Fact]
    public void MovedWithin_moves_an_item_forward_accounting_for_the_lifted_gap()
    {
        var list = Items("A", "B", "C", "D");
        var moved = CashShopOps.MovedWithin(list, list[0], targetIndex: 2); // drop A before C
        Assert.Equal(new[] { "B", "A", "C", "D" }, moved.Select(i => i.Item));
    }

    [Fact]
    public void MovedWithin_moves_an_item_backward()
    {
        var list = Items("A", "B", "C", "D");
        var moved = CashShopOps.MovedWithin(list, list[3], targetIndex: 1); // drop D before B
        Assert.Equal(new[] { "A", "D", "B", "C" }, moved.Select(i => i.Item));
    }

    [Fact]
    public void MovedWithin_is_a_no_op_when_dropped_on_itself()
    {
        var list = Items("A", "B", "C");
        var moved = CashShopOps.MovedWithin(list, list[1], targetIndex: 1);
        Assert.Equal(new[] { "A", "B", "C" }, moved.Select(i => i.Item));
    }

    private static CashShopItemIndex SampleIndex() => new(new[]
    {
        new ItemRef(501, "Red_Potion", "Red Potion"),
        new ItemRef(502, "Orange_Potion", "Orange Potion"),
        new ItemRef(601, "Wing_Of_Fly", "Fly Wing"),
        new ItemRef(909, "Jellopy", "Jellopy"),
    });

    [Fact]
    public void Index_resolves_by_aegis_name_display_name_and_id()
    {
        var idx = SampleIndex();
        Assert.Equal((CashShopItemIndex.ResolveStatus.Resolved, "Red_Potion"), idx.Resolve("Red_Potion")); // aegis
        Assert.Equal((CashShopItemIndex.ResolveStatus.Resolved, "Red_Potion"), idx.Resolve("red_potion")); // case-insensitive
        Assert.Equal((CashShopItemIndex.ResolveStatus.Resolved, "Red_Potion"), idx.Resolve("501"));        // id
        Assert.Equal((CashShopItemIndex.ResolveStatus.Resolved, "Wing_Of_Fly"), idx.Resolve("Fly Wing"));  // display name
    }

    [Fact]
    public void Index_reports_not_found_and_ambiguous_display_names()
    {
        // Two items share the display name "Old Blue Box" (which is no item's Aegis name) -> resolving that Name
        // is ambiguous, but each item is still reachable unambiguously by its unique Aegis name or Id.
        var idx = new CashShopItemIndex(new[]
        {
            new ItemRef(603, "Old_Blue_Box", "Old Blue Box"),
            new ItemRef(617, "Dead_Branch", "Old Blue Box"),
        });
        Assert.Equal(CashShopItemIndex.ResolveStatus.Ambiguous, idx.Resolve("Old Blue Box").Status);
        Assert.Equal((CashShopItemIndex.ResolveStatus.Resolved, "Old_Blue_Box"), idx.Resolve("Old_Blue_Box")); // exact aegis wins
        Assert.Equal((CashShopItemIndex.ResolveStatus.Resolved, "Dead_Branch"), idx.Resolve("617"));            // id wins
        Assert.Equal(CashShopItemIndex.ResolveStatus.NotFound, idx.Resolve("Nonexistent_Thing").Status);
    }

    [Fact]
    public void Index_suggests_across_aegis_name_and_id_ranked()
    {
        var idx = SampleIndex();
        // "Potion" matches both potions by display name + aegis.
        var byWord = idx.Suggest("Potion").Select(s => s.AegisName).ToList();
        Assert.Contains("Red_Potion", byWord);
        Assert.Contains("Orange_Potion", byWord);

        // A numeric query ranks the exact id first.
        Assert.Equal("Wing_Of_Fly", idx.Suggest("601").First().AegisName);

        // A prefix on the aegis name ranks that item first.
        Assert.Equal("Red_Potion", idx.Suggest("Red_").First().AegisName);
    }

    [Fact]
    public void Index_exposes_display_name_and_id_for_a_card()
    {
        var idx = SampleIndex();
        Assert.Equal("Red Potion", idx.DisplayName("Red_Potion"));
        Assert.Equal(501, idx.IdOf("Red_Potion"));
        Assert.True(idx.ContainsAegis("red_potion"));
        Assert.False(idx.ContainsAegis("Ghost_Item"));
    }
}
