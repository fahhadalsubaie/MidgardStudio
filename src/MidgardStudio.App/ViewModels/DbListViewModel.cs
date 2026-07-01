using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using MidgardStudio.App.Common;
using MidgardStudio.Core.Model;
using MidgardStudio.Core.Overlay;

namespace MidgardStudio.App.ViewModels;

/// <summary>
/// The master list for one database: the full effective row set plus a debounced, background-filtered
/// snapshot bound to the UI. Sorted ascending by id by default; ID/Name columns are click-sortable.
/// </summary>
public sealed partial class DbListViewModel : ObservableObject
{
    private readonly OverlayTable _table;
    private readonly Func<RecordKey, ImageSource?>? _iconResolver;
    private readonly Func<RecordKey, bool>? _include; // list-membership predicate (e.g. "has a client entry")
    private readonly Func<IEnumerable<RecordRowViewModel>>? _extraRows; // virtual rows not backed by the overlay (client-only items)
    private readonly List<RecordRowViewModel> _all;
    private CancellationTokenSource? _filterCts;

    private string? _sortColumn; // null = default (id ascending, no glyph); "Id"; "Name"
    private bool _sortAscending = true;

    public DbListViewModel(OverlayTable table, Func<RecordKey, ImageSource?>? iconResolver = null,
        Func<RecordKey, bool>? include = null, Func<IEnumerable<RecordRowViewModel>>? extraRows = null)
    {
        _table = table;
        _iconResolver = iconResolver;
        _include = include;
        _extraRows = extraRows;
        _all = table.Effective()
            .Where(r => _include is null || _include(r.Key))
            .Select(r => new RecordRowViewModel(table, r.Key, iconResolver))
            .ToList();
        AppendExtraRows();
        SortAll();
        ApplyFilter(_all);
    }

    /// <summary>Adds the virtual (overlay-less) rows — e.g. client-only items whose server record was deleted —
    /// that aren't already represented, so they show alongside the overlay rows and survive a resync.</summary>
    private void AppendExtraRows()
    {
        if (_extraRows is null) return;
        var have = new HashSet<RecordKey>(_all.Select(r => r.Key));
        foreach (var row in _extraRows())
            if (have.Add(row.Key)) _all.Add(row);
    }

    public RangeObservableCollection<RecordRowViewModel> Rows { get; } = new();

    /// <summary>The currently multi-selected rows (mirrored from the list via <c>ListBehaviors.SelectedItems</c>);
    /// used for bulk "Copy YAML". Empty/one-element for lists that don't enable multi-select.</summary>
    public System.Collections.ObjectModel.ObservableCollection<RecordRowViewModel> SelectedRows { get; } = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private RecordRowViewModel? _selectedRow;

    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>When true, show only customized / overridden entries (hide read-only base rows).</summary>
    [ObservableProperty]
    private bool _overriddenOnly;

    partial void OnOverriddenOnlyChanged(bool value) => FilterNow();

    public int TotalCount => _all.Count;

    private bool Pass(RecordRowViewModel r, string q) =>
        (q.Length == 0 || r.Matches(q)) && (!OverriddenOnly || r.Origin != RecordOrigin.Base);

    /// <summary>Creates a row bound to this list's overlay + icon resolver (for newly added customs).</summary>
    public RecordRowViewModel CreateRow(RecordKey key) => new(_table, key, _iconResolver);

    /// <summary>Header text for a sortable column, including the active sort glyph.</summary>
    public string HeaderText(string key)
    {
        string label = key == "Id" ? "ID" : "Name";
        if (_sortColumn != key) return label;
        return label + (_sortAscending ? "  ▲" : "  ▼");
    }

    /// <summary>The sort glyph (▲/▼) for a column, or empty when it isn't the active sort column.</summary>
    public string SortGlyph(string key) => _sortColumn != key ? string.Empty : (_sortAscending ? "  ▲" : "  ▼");

    /// <summary>Cycles a column's sort: ascending → descending → default (id ascending, no glyph).</summary>
    public void ToggleSort(string key)
    {
        if (_sortColumn != key) { _sortColumn = key; _sortAscending = true; }
        else if (_sortAscending) { _sortAscending = false; }
        else { _sortColumn = null; _sortAscending = true; }

        SortAll();
        FilterNow();
    }

    /// <summary>Searches the full row set (used by the command palette).</summary>
    public IEnumerable<RecordRowViewModel> Search(string query, int limit)
    {
        string q = query.Trim();
        if (q.Length == 0) yield break;
        int count = 0;
        foreach (var row in _all)
        {
            if (!row.Matches(q)) continue;
            yield return row;
            if (++count >= limit) yield break;
        }
    }

    public void SelectByKey(RecordKey key)
    {
        var row = _all.FirstOrDefault(r => r.Key.Equals(key));
        if (row is null) return;
        if (!Rows.Contains(row)) Rows.ReplaceAll(_all);
        SelectedRow = row;
    }

    public void AddRow(RecordRowViewModel row)
    {
        _all.Add(row);
        SortAll();
        FilterNow();
    }

    /// <summary>Reconciles the master row set with the overlay's effective records after an undo/redo.</summary>
    public void SyncWithOverlay()
    {
        var prevKey = SelectedRow?.Key;

        var byKey = new Dictionary<RecordKey, RecordRowViewModel>();
        foreach (var row in _all) byKey[row.Key] = row;

        _all.Clear();
        foreach (var rec in _table.Effective())
        {
            if (_include is not null && !_include(rec.Key)) continue; // keep the list's membership filter (e.g. client-only rows)
            if (byKey.TryGetValue(rec.Key, out var row))
                row.Refresh(); // overlay changed (undo / redo / restore / delete) — re-read the origin pill + cached text
            else
                row = new RecordRowViewModel(_table, rec.Key, _iconResolver);
            _all.Add(row);
        }
        AppendExtraRows(); // re-add client-only rows (their server record isn't in the overlay)

        SortAll();
        FilterNow();
        SelectedRow = prevKey is { } key ? _all.FirstOrDefault(r => r.Key.Equals(key)) : null;
    }

    partial void OnSearchTextChanged(string value) => _ = RunFilterAsync(value);

    private void SortAll()
    {
        Comparison<RecordRowViewModel> cmp = (_sortColumn, _sortAscending) switch
        {
            ("Name", true) => CompareName,
            ("Name", false) => (a, b) => CompareName(b, a),
            ("Id", false) => (a, b) => CompareId(b, a),
            _ => CompareId, // default + Id ascending
        };
        _all.Sort(cmp);
    }

    private static int CompareId(RecordRowViewModel a, RecordRowViewModel b) =>
        !a.Key.IsString && !b.Key.IsString
            ? a.Key.AsInt.CompareTo(b.Key.AsInt)
            : string.Compare(a.Key.AsString, b.Key.AsString, StringComparison.OrdinalIgnoreCase);

    private static int CompareName(RecordRowViewModel a, RecordRowViewModel b)
    {
        int c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        return c != 0 ? c : CompareId(a, b);
    }

    private void FilterNow()
    {
        string q = SearchText.Trim();
        var matched = _all.Where(r => Pass(r, q)).ToList();
        ApplyFilter(matched);
    }

    private async Task RunFilterAsync(string query)
    {
        _filterCts?.Cancel();
        var cts = new CancellationTokenSource();
        _filterCts = cts;

        try
        {
            await Task.Delay(140, cts.Token); // debounce
            string q = query.Trim();
            // Snapshot on the UI thread (we're back on it after the debounce) so the background filter can't
            // race a concurrent edit/add/delete/sort mutating _all and throw "collection modified".
            var snapshot = _all.ToArray();
            List<RecordRowViewModel> matched = await Task.Run(() => snapshot.Where(r => Pass(r, q)).ToList(), cts.Token);

            if (cts.Token.IsCancellationRequested) return;

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (!cts.Token.IsCancellationRequested)
                    ApplyFilter(matched);
            });
        }
        catch (TaskCanceledException)
        {
            // superseded by a newer keystroke — ignore
        }
        catch (InvalidOperationException)
        {
            // a concurrent list mutation raced the filter despite the snapshot — the next keystroke re-runs it
        }
    }

    private void ApplyFilter(IReadOnlyList<RecordRowViewModel> matched)
    {
        Rows.ReplaceAll(matched);
        StatusText = matched.Count == _all.Count
            ? $"{_all.Count:N0} entries"
            : $"{matched.Count:N0} of {_all.Count:N0} entries";
    }
}
