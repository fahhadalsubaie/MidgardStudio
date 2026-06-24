using System;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using MidgardStudio.Core.Model;
using MidgardStudio.Core.Overlay;

namespace MidgardStudio.App.ViewModels;

/// <summary>
/// A row in the database list. Reads through the overlay by key so it always reflects the effective
/// record (import wins over base) and the live origin, even after an override is created.
/// </summary>
public sealed class RecordRowViewModel : ObservableObject
{
    private readonly OverlayTable _table;
    private readonly string _keyField;
    private readonly string _displayField;
    private readonly Func<RecordKey, ImageSource?>? _iconResolver;
    private ImageSource? _icon;
    private bool _iconResolved;

    // Searchable text is stable between keystrokes, so resolve it through the overlay once and cache it.
    // The filter pass then does plain string compares instead of dictionary lookups + a fresh
    // Key.ToString() allocation per row on every keystroke. Caches are reset in Refresh().
    // NOTE: Origin is deliberately NOT cached — it changes when an override is created/reverted, and a
    // stale cache would leave the "Overridden" chip showing after a restore-to-default until reload.
    private string? _keyText, _aegisName, _name, _typeText;

    public RecordRowViewModel(OverlayTable table, RecordKey key, Func<RecordKey, ImageSource?>? iconResolver = null)
    {
        _table = table;
        Key = key;
        _iconResolver = iconResolver;
        _keyField = table.Schema.KeyField?.Name ?? "Id";
        _displayField = table.Schema.DisplayField?.Name ?? "Name";
    }

    public RecordKey Key { get; }

    /// <summary>Lazily-resolved list icon (items only); null for databases without one.</summary>
    public ImageSource? Icon
    {
        get
        {
            if (!_iconResolved) { _iconResolved = true; _icon = _iconResolver?.Invoke(Key); }
            return _icon;
        }
    }

    public DbRecord Record => _table.GetEffective(Key)!;

    public string KeyText => _keyText ??= Key.ToString();

    public string AegisName => _aegisName ??= Record.GetString("AegisName") ?? string.Empty;

    public string Name => _name ??= Record.GetString(_displayField) ?? string.Empty;

    public string TypeText => _typeText ??= Record.GetString("Type") ?? string.Empty;

    public RecordOrigin Origin => _table.OriginOf(Key);

    public bool Matches(string lowerQuery) =>
        KeyText.Contains(lowerQuery, StringComparison.OrdinalIgnoreCase)
        || AegisName.Contains(lowerQuery, StringComparison.OrdinalIgnoreCase)
        || Name.Contains(lowerQuery, StringComparison.OrdinalIgnoreCase)
        || TypeText.Contains(lowerQuery, StringComparison.OrdinalIgnoreCase);

    /// <summary>Refresh all bindings (after an edit/override changes the effective record).</summary>
    public void Refresh()
    {
        _iconResolved = false; // re-resolve the icon (resource name may have changed)
        _keyText = _aegisName = _name = _typeText = null; // drop cached text so it re-reads the overlay
        OnPropertyChanged(string.Empty);
    }
}
