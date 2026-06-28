using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MidgardStudio.App.Services;
using MidgardStudio.Core.CashShop;
using MidgardStudio.Core.Commands;
using MidgardStudio.Core.Validation;

namespace MidgardStudio.App.ViewModels;

/// <summary>
/// Tools ▸ Cash Shop Manager. Edits the server cash shop (<c>item_cash_db</c>) through a left tab rail (the
/// nine fixed tabs, with live item counts) and a per-tab item list: add an item by Aegis name + price, edit a
/// price inline, remove an item — all routed through the global undo stack and written to import on Save.
/// Inline badges mirror the cash-shop validator (unknown item / dup-in-tab / price 0).
/// </summary>
public sealed partial class CashShopManagerViewModel : ObservableObject
{
    private readonly CashShopService _service;
    private readonly EditCommandStack _commands;
    private readonly ClientItemService _clientItems;
    private readonly GrfImageService _images;

    public CashShopManagerViewModel(CashShopService service, EditCommandStack commands,
        ClientItemService clientItems, GrfImageService images)
    {
        _service = service;
        _commands = commands;
        _clientItems = clientItems;
        _images = images;
        foreach (var tab in CashShopData.Tabs) Tabs.Add(new CashTabViewModel(tab));
        SelectedTab = Tabs.FirstOrDefault();
        RefreshCounts();
        RebuildItems();
    }

    /// <summary>The nine fixed tabs (with live counts) shown in the rail.</summary>
    public ObservableCollection<CashTabViewModel> Tabs { get; } = new();

    /// <summary>Items of the selected tab (base rows first, read-only; then editable custom rows).</summary>
    public ObservableCollection<CashItemRowViewModel> Items { get; } = new();

    [ObservableProperty]
    private CashTabViewModel? _selectedTab;

    [ObservableProperty]
    private string _newItemName = string.Empty;

    [ObservableProperty]
    private string _newItemPriceText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>Whether the autocomplete suggestion popup is shown.</summary>
    [ObservableProperty]
    private bool _isSuggestOpen;

    private bool _suppressSuggest;

    /// <summary>Live item_db autocomplete matches for the current add-box text (across Name / Aegis name / Id),
    /// each with its GRF icon for at-a-glance identification.</summary>
    public IReadOnlyList<CashSuggestionViewModel> ItemSuggestions =>
        _service.Suggest(NewItemName).Select(s => new CashSuggestionViewModel(s, ResolveIcon)).ToList();

    public bool HasNoItems => Items.Count == 0;

    partial void OnSelectedTabChanged(CashTabViewModel? value) => RebuildItems();

    partial void OnNewItemNameChanged(string value)
    {
        OnPropertyChanged(nameof(ItemSuggestions));
        if (_suppressSuggest) return;
        IsSuggestOpen = !string.IsNullOrWhiteSpace(value) && ItemSuggestions.Count > 0;
    }

    /// <summary>Fills the add box with a chosen suggestion's canonical Aegis name (without reopening the
    /// popup) and closes it.</summary>
    public void PickSuggestion(CashSuggestionViewModel suggestion)
    {
        _suppressSuggest = true;
        NewItemName = suggestion.AegisName;
        _suppressSuggest = false;
        IsSuggestOpen = false;
    }

    public void CloseSuggestions() => IsSuggestOpen = false;

    private CashShopTab Tab => SelectedTab?.Tab ?? CashShopTab.New;

    [RelayCommand]
    private void AddItem()
    {
        if (SelectedTab is null) return;
        // Each token may be a display Name, an Aegis name, or a numeric Id; bulk-add splits on commas. Resolve
        // every token to its canonical Aegis name (what item_cash.yml stores) and gate insertion on a real
        // item — the server silently drops unknown cash entries, so a typo must never reach the shop.
        var tokens = (NewItemName ?? string.Empty)
            .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) { StatusMessage = "Enter an item name, Aegis name, or ID to add."; return; }

        var resolved = new List<string>();
        var unknown = new List<string>();
        var ambiguous = new List<string>();
        foreach (var token in tokens)
        {
            var (status, aegis) = _service.Resolve(token);
            if (status == CashShopItemIndex.ResolveStatus.Resolved && aegis is not null)
            {
                if (!resolved.Contains(aegis, StringComparer.OrdinalIgnoreCase)) resolved.Add(aegis);
            }
            else if (status == CashShopItemIndex.ResolveStatus.Ambiguous) ambiguous.Add(token);
            else unknown.Add(token);
        }

        if (resolved.Count == 0)
        {
            StatusMessage = ambiguous.Count > 0
                ? $"'{ambiguous[0]}' matches several items — pick the exact one from the suggestions, or use its Aegis name or ID."
                : (unknown.Count == 1
                    ? $"'{unknown[0]}' is not an item in item_db — pick one from the suggestions."
                    : $"None of those match an item in item_db: '{string.Join("', '", unknown)}'.");
            return;
        }

        long price = long.TryParse((NewItemPriceText ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ? p : 0;
        var tab = SelectedTab.Tab;
        var list = _service.Data.Custom(tab);
        var added = resolved.Select(n => new CashItem(n, price)).ToList();
        _commands.Execute(new ListMutateCommand(
            added.Count == 1 ? $"Add cash item {added[0].Item} to {tab}" : $"Add {added.Count} cash items to {tab}",
            () => list.AddRange(added),
            () => { foreach (var it in added) list.Remove(it); }));

        NewItemName = string.Empty;
        NewItemPriceText = string.Empty; // back to empty so the "Price" placeholder shows for the next add
        IsSuggestOpen = false;
        RebuildItems();
        RefreshCounts();

        var skipped = unknown.Concat(ambiguous).ToList();
        StatusMessage = skipped.Count > 0
            ? $"Added {added.Count} item(s) to {tab}; skipped: '{string.Join("', '", skipped)}'."
            : (added.Count == 1 ? $"Added '{added[0].Item}' to {tab}." : $"Added {added.Count} items to {tab}.");
    }

    /// <summary>Reorders a custom item within the selected tab (drag-drop). Undoable as one step.</summary>
    public void MoveWithinTab(CashItemRowViewModel row, int targetIndex)
    {
        if (row.IsBase || SelectedTab is null) return;
        var list = _service.Data.Custom(SelectedTab.Tab);
        if (!list.Contains(row.Model)) return;

        var before = list.ToList();
        var after = CashShopOps.MovedWithin(list, row.Model, targetIndex);
        if (before.SequenceEqual(after)) return;

        _commands.Execute(new ListMutateCommand($"Reorder cash items in {SelectedTab.Tab}",
            () => { list.Clear(); list.AddRange(after); },
            () => { list.Clear(); list.AddRange(before); }));
        RebuildItems();
    }

    /// <summary>Recategorizes a custom item into another tab (drag onto the rail, or the move-to menu). Undoable.</summary>
    public void MoveItemToTab(CashItemRowViewModel row, CashShopTab target)
    {
        if (row.IsBase || SelectedTab is null || SelectedTab.Tab == target) return;
        var src = _service.Data.Custom(SelectedTab.Tab);
        var dst = _service.Data.Custom(target);
        var item = row.Model;
        if (!src.Contains(item)) return;

        var srcBefore = src.ToList();
        var dstBefore = dst.ToList();
        var srcAfter = src.Where(i => !ReferenceEquals(i, item)).ToList();
        var dstAfter = dst.Concat(new[] { item }).ToList();

        _commands.Execute(new ListMutateCommand($"Move {item.Item} to {target}",
            () => { src.Clear(); src.AddRange(srcAfter); dst.Clear(); dst.AddRange(dstAfter); },
            () => { src.Clear(); src.AddRange(srcBefore); dst.Clear(); dst.AddRange(dstBefore); }));
        RebuildItems();
        RefreshCounts();
        StatusMessage = $"Moved '{item.Item}' to {target}.";
    }

    [RelayCommand]
    private void RemoveItem(CashItemRowViewModel? row)
    {
        if (row is null || row.IsBase || SelectedTab is null) return;
        var tab = SelectedTab.Tab;
        var list = _service.Data.Custom(tab);
        int idx = list.IndexOf(row.Model);
        if (idx < 0) return;

        var item = row.Model;
        _commands.Execute(new ListMutateCommand($"Remove cash item {item.Item} from {tab}",
            () => list.Remove(item),
            () => list.Insert(Math.Min(idx, list.Count), item)));

        RebuildItems();
        RefreshCounts();
        StatusMessage = $"Removed '{item.Item}' from {tab}.";
    }

    private void CommitPrice(CashItemRowViewModel row, long newPrice)
    {
        var item = row.Model;
        long old = item.Price;
        if (newPrice == old) return;
        _commands.Execute(new ListMutateCommand($"Set price of {item.Item} to {newPrice}",
            () => item.Price = newPrice,
            () => item.Price = old));
        RefreshValidation();
    }

    private void RebuildItems()
    {
        Items.Clear();
        if (SelectedTab is not null)
        {
            var data = _service.Data;
            foreach (var b in data.Base(SelectedTab.Tab)) Items.Add(new CashItemRowViewModel(b, isBase: true, CommitPrice, ResolveIcon, _service.DisplayName, _service.ItemId));
            foreach (var c in data.Custom(SelectedTab.Tab)) Items.Add(new CashItemRowViewModel(c, isBase: false, CommitPrice, ResolveIcon, _service.DisplayName, _service.ItemId));
        }
        RefreshValidation();
        OnPropertyChanged(nameof(HasNoItems));
    }

    /// <summary>Resolves a cash item's GRF icon: Aegis name → item id → client resource name → GRF icon.
    /// Returns null (placeholder shown) when the item, its client text, or the GRF isn't available.</summary>
    private ImageSource? ResolveIcon(string aegisName)
    {
        try
        {
            if (_service.ItemId(aegisName) is not { } id) return null;
            return _images.ItemIcon(_clientItems.GetOrCreate(id).IdentifiedResourceName);
        }
        catch { return null; }
    }

    private void RefreshCounts()
    {
        var data = _service.Data;
        foreach (var t in Tabs) t.Count = data.Count(t.Tab);
    }

    private void RefreshValidation()
    {
        var known = _service.KnownItems();
        var counts = Items.GroupBy(r => r.Item, StringComparer.OrdinalIgnoreCase)
                          .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        foreach (var row in Items)
        {
            var (severity, message) = CashShopValidator.Check(row.Item, row.Model.Price, counts[row.Item], known);
            row.Issue = severity;
            row.IssueTooltip = message;
        }
    }

    /// <summary>Selects a tab by name (used by validation "Go To").</summary>
    public void SelectTab(string tabName)
    {
        if (Enum.TryParse<CashShopTab>(tabName, ignoreCase: true, out var tab))
            SelectedTab = Tabs.FirstOrDefault(t => t.Tab == tab) ?? SelectedTab;
    }

    /// <summary>Re-syncs the lists + counts after an external change (undo/redo, reopen).</summary>
    public void RefreshAfterChange()
    {
        RebuildItems();
        RefreshCounts();
    }
}

/// <summary>One tab in the rail: the fixed tab + its live effective item count.</summary>
public sealed partial class CashTabViewModel : ObservableObject
{
    public CashTabViewModel(CashShopTab tab) => Tab = tab;

    public CashShopTab Tab { get; }
    public string Name => Tab.ToString();

    [ObservableProperty]
    private int _count;
}

/// <summary>One row in the item list — wraps a <see cref="CashItem"/>. Base rows are read-only; custom rows
/// commit a price edit through the undo stack on focus-loss.</summary>
public sealed partial class CashItemRowViewModel : ObservableObject
{
    private readonly Action<CashItemRowViewModel, long> _commitPrice;
    private readonly Func<string, ImageSource?> _iconResolver;
    private readonly Func<string, string?> _nameResolver;
    private readonly Func<string, int?> _idResolver;
    private bool _applying;
    private bool _iconResolved;
    private ImageSource? _icon;
    private bool _nameResolved;
    private string? _displayName;
    private bool _idResolved;
    private int? _id;

    public CashItemRowViewModel(CashItem model, bool isBase, Action<CashItemRowViewModel, long> commitPrice,
        Func<string, ImageSource?> iconResolver, Func<string, string?> nameResolver, Func<string, int?> idResolver)
    {
        Model = model;
        IsBase = isBase;
        _commitPrice = commitPrice;
        _iconResolver = iconResolver;
        _nameResolver = nameResolver;
        _idResolver = idResolver;
    }

    public CashItem Model { get; }
    public string Item => Model.Item;
    public bool IsBase { get; }
    public bool IsEditable => !IsBase;

    /// <summary>The item's display Name (from item_db), resolved lazily — null/empty for an unknown item, in
    /// which case the card falls back to showing just the Aegis name.</summary>
    public string? DisplayName
    {
        get
        {
            if (!_nameResolved) { _displayName = _nameResolver(Item); _nameResolved = true; }
            return _displayName;
        }
    }

    public bool HasDisplayName => !string.IsNullOrEmpty(DisplayName);

    private int? ItemId
    {
        get
        {
            if (!_idResolved) { _id = _idResolver(Item); _idResolved = true; }
            return _id;
        }
    }

    /// <summary>The card's primary line: the item's display Name, or the Aegis name as a fallback when item_db
    /// has no name for it (rare; unknown items are rejected on add).</summary>
    public string Title => HasDisplayName ? DisplayName! : Item;

    /// <summary>The item id shown under the name (<c>#501</c>) so visually-identical items stay distinguishable.
    /// Empty when item_db has no such item.</summary>
    public string IdLabel => ItemId is { } id ? $"#{id}" : string.Empty;

    public bool HasId => ItemId is not null;

    /// <summary>The item's GRF icon, resolved lazily on first bind (so virtualized off-screen cards don't pay
    /// for a GRF decode). Null when unresolvable — the card shows a placeholder glyph.</summary>
    public ImageSource? Icon
    {
        get
        {
            if (!_iconResolved) { _icon = _iconResolver(Item); _iconResolved = true; }
            return _icon;
        }
    }

    public bool HasNoIcon => Icon is null;

    /// <summary>Editable price text — bound TwoWay with LostFocus, so committing once per edit (not per
    /// keystroke) pushes a single undoable command. Invalid input snaps back to the current value.</summary>
    public string PriceText
    {
        get => Model.Price.ToString(CultureInfo.InvariantCulture);
        set
        {
            if (_applying || IsBase) { OnPropertyChanged(); return; }
            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var price))
                _commitPrice(this, price);
            OnPropertyChanged(); // reflect the canonical value (commit may have changed it; invalid input reverts)
        }
    }

    /// <summary>Re-reads the price after a command-driven change (undo/redo) without re-triggering the setter.</summary>
    public void RefreshPrice()
    {
        _applying = true;
        OnPropertyChanged(nameof(PriceText));
        _applying = false;
    }

    /// <summary>Inline validation: null = ok, Warning, or Error (drives the row badge colour).</summary>
    [ObservableProperty]
    private ValidationSeverity? _issue;

    [ObservableProperty]
    private string? _issueTooltip;

    public bool HasIssue => Issue is not null;

    partial void OnIssueChanged(ValidationSeverity? value) => OnPropertyChanged(nameof(HasIssue));
}

/// <summary>One autocomplete suggestion row in the add box — the resolved item plus its GRF icon (resolved
/// lazily) so users can identify the item visually.</summary>
public sealed class CashSuggestionViewModel
{
    private readonly Func<string, ImageSource?> _iconResolver;
    private bool _iconResolved;
    private ImageSource? _icon;

    public CashSuggestionViewModel(ItemSuggestion suggestion, Func<string, ImageSource?> iconResolver)
    {
        Suggestion = suggestion;
        _iconResolver = iconResolver;
    }

    public ItemSuggestion Suggestion { get; }
    public string AegisName => Suggestion.AegisName;
    public string Name => Suggestion.Name;
    public int Id => Suggestion.Id;

    public ImageSource? Icon
    {
        get
        {
            if (!_iconResolved) { _icon = _iconResolver(AegisName); _iconResolved = true; }
            return _icon;
        }
    }

    public bool HasNoIcon => Icon is null;
}
