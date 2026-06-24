namespace MidgardStudio.Core.Workspace;

/// <summary>
/// Which rAthena ruleset is active. The app loads both datasets (db/re and db/pre-re)
/// and toggles the active view at runtime; the import overlay is shared across modes.
/// </summary>
public enum ServerMode
{
    Renewal,
    PreRenewal,
}
