namespace MidgardStudio.Core.Schema;

/// <summary>
/// Which ruleset(s) a field applies to. Renewal-only fields are greyed in Pre-Renewal mode
/// and flagged by the Renewal-to-Pre-Renewal port report.
/// </summary>
public enum RenewalScope
{
    Both,
    RenewalOnly,
    PreRenewalOnly,
}
