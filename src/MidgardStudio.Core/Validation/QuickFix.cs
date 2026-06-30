namespace MidgardStudio.Core.Validation;

/// <summary>
/// A one-click remedy for a validation issue. <see cref="Apply"/> mutates the model to resolve the
/// finding; the caller re-validates afterwards. Pure-data fixes are built in Core; fixes that need
/// client/GRF services are built in the App layer (capturing the service in the delegate).
/// <see cref="Revert"/> (when provided) is the inverse, so the App can run the fix through the undo
/// stack — giving it undo/redo and lighting the Save indicator like any other edit.
/// </summary>
public sealed record QuickFix(string Title, Action Apply, Action? Revert = null)
{
    /// <summary>
    /// True only when this is the single, unambiguous correct resolution — the value comes from an invariant
    /// or another source of truth (e.g. client ClassNum := server View, name := the SKID key, clear a field
    /// that doesn't apply), never a guessed default the user should choose (clamp/trim/"set to 1"/pad). Only
    /// Automatic fixes get a one-click "Fix" button and are applied by "Fix All"; heuristic fixes leave this
    /// false and are resolved by hand. Default false — a fix must prove it's deterministic to opt in.
    /// </summary>
    public bool Automatic { get; init; }

    /// <summary>The per-row action button's label. Default "Fix"; a fix that creates a record uses "Create".</summary>
    public string ButtonLabel { get; init; } = "Fix";

    /// <summary>When true, applying the fix then navigates to the affected record so the user completes and
    /// confirms it (e.g. a client entry created from server data that still needs a real description). Such a
    /// fix gets the one-click button but is NOT batch-applied by "Fix All" — it needs review.</summary>
    public bool ReviewAfter { get; init; }
}
