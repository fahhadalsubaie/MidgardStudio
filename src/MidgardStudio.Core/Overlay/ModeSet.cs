using MidgardStudio.Core.Workspace;

namespace MidgardStudio.Core.Overlay;

/// <summary>
/// The Renewal and Pre-Renewal overlays for one database, sharing a single import layer (rAthena's
/// db/import applies in both modes). Switching mode is a pure view swap — no reload.
/// </summary>
public sealed class ModeSet
{
    public ModeSet(OverlayTable renewal, OverlayTable preRenewal, DbLayer sharedImport)
    {
        Renewal = renewal;
        PreRenewal = preRenewal;
        SharedImport = sharedImport;
    }

    public OverlayTable Renewal { get; }

    public OverlayTable PreRenewal { get; }

    public DbLayer SharedImport { get; }

    public OverlayTable For(ServerMode mode) => mode == ServerMode.Renewal ? Renewal : PreRenewal;

    public bool IsDirty => Renewal.IsDirty || PreRenewal.IsDirty;
}
