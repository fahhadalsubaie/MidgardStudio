using MidgardStudio.Core.Workspace;

namespace MidgardStudio.Core.Overlay;

/// <summary>
/// The Renewal and Pre-Renewal overlays for one database, sharing a single import layer (rAthena's
/// db/import applies in both modes). Switching mode is a pure view swap.
///
/// Built lazily: only the ACTIVE mode's base is parsed up front; the other mode's base is parsed on
/// first <see cref="For"/>. Most sessions never switch mode, so the inactive base (roughly the active
/// base's size again) is never loaded — re/ and pre-re/ are no longer both resident at startup.
/// </summary>
public sealed class ModeSet
{
    private readonly Func<ServerMode, OverlayTable>? _build;
    private readonly Dictionary<ServerMode, OverlayTable> _built = new();
    private readonly object _lock = new();

    /// <summary>Eager: both overlays already built. Used by direct construction (tests) and any caller
    /// that wants both modes resident.</summary>
    public ModeSet(OverlayTable renewal, OverlayTable preRenewal, DbLayer sharedImport)
    {
        _built[ServerMode.Renewal] = renewal;
        _built[ServerMode.PreRenewal] = preRenewal;
        SharedImport = sharedImport;
        ImportFilePath = renewal.ImportFilePath; // both overlays share the one import file
    }

    /// <summary>Lazy: parses only <paramref name="activeMode"/>'s base now; the other mode is built by
    /// <paramref name="build"/> on first <see cref="For"/>. <paramref name="build"/> must reuse the same
    /// shared import layer so edits are visible across modes and saved once.</summary>
    public ModeSet(ServerMode activeMode, Func<ServerMode, OverlayTable> build, DbLayer sharedImport, string importFilePath)
    {
        _build = build;
        SharedImport = sharedImport;
        ImportFilePath = importFilePath;
        _built[activeMode] = build(activeMode);
    }

    public DbLayer SharedImport { get; }

    /// <summary>The single import file both modes write to (mode-independent) — read it without forcing
    /// the inactive mode to be built.</summary>
    public string ImportFilePath { get; }

    public OverlayTable Renewal => For(ServerMode.Renewal);

    public OverlayTable PreRenewal => For(ServerMode.PreRenewal);

    public OverlayTable For(ServerMode mode)
    {
        lock (_lock)
        {
            if (_built.TryGetValue(mode, out var existing)) return existing;
            if (_build is null)
                throw new InvalidOperationException($"ModeSet has no builder for {mode}.");
            var built = _build(mode);
            _built[mode] = built;
            return built;
        }
    }

    /// <summary>Dirty if any BUILT overlay has unsaved edits. An un-built mode has never been reached,
    /// so it can hold no edits — checking it (and thereby parsing its base) would be both wrong-headed
    /// and would defeat the lazy load.</summary>
    public bool IsDirty
    {
        get { lock (_lock) { return _built.Values.Any(t => t.IsDirty); } }
    }

    /// <summary>Writes the shared import file once. Both overlays write the same single import layer, so
    /// saving through any already-built overlay (there is always the eagerly-built active one) suffices —
    /// no need to build the inactive mode just to save.</summary>
    public void Save()
    {
        lock (_lock) { _built.Values.First().Save(); }
    }
}
