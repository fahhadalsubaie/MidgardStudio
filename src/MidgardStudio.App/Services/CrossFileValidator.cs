using MidgardStudio.Core.Model;
using MidgardStudio.Core.Validation;
using MidgardStudio.Grf;

namespace MidgardStudio.App.Services;

/// <summary>
/// Checks consistency across the server and client files for custom/overridden entries: missing
/// client text, slot/view mismatches, sprites/icons absent from the GRF, and unregistered mob sprites.
/// All findings are advisory.
/// </summary>
public sealed class CrossFileValidator
{
    private readonly WorkspaceSession _session;
    private readonly SchemaRegistry _schemas;
    private readonly ClientItemService _client;
    private readonly MobSpriteService _mobSprite;
    private readonly GrfService _grf;

    public CrossFileValidator(WorkspaceSession session, SchemaRegistry schemas,
        ClientItemService client, MobSpriteService mobSprite, GrfService grf)
    {
        _session = session;
        _schemas = schemas;
        _client = client;
        _mobSprite = mobSprite;
        _grf = grf;
    }

    public IReadOnlyList<ValidationIssue> Validate()
    {
        var issues = new List<ValidationIssue>();
        ValidateItems(issues);
        ValidateMobs(issues);
        return issues;
    }

    private void ValidateItems(List<ValidationIssue> issues)
    {
        if (_schemas.Get("item_db") is not { } schema) return;
        var overlay = _session.GetActiveOverlay(schema);

        foreach (var rec in overlay.Effective().Where(r => r.Origin != RecordOrigin.Base))
        {
            int id = rec.GetInt("Id");
            string key = id.ToString();

            if (!_client.Has(id))
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Warning, "item_db", key, "client",
                    "Custom item has no client text (itemInfo) — it will show no name/description in-game."));
                continue;
            }

            var entry = _client.GetOrCreate(id);
            if (entry.SlotCount != rec.GetInt("Slots"))
                issues.Add(new ValidationIssue(ValidationSeverity.Warning, "item_db", key, "SlotCount",
                    $"Client slotCount ({entry.SlotCount}) != server Slots ({rec.GetInt("Slots")})."));

            if (entry.ClassNum != rec.GetInt("View"))
                issues.Add(new ValidationIssue(ValidationSeverity.Warning, "item_db", key, "ClassNum",
                    $"Client ClassNum ({entry.ClassNum}) != server View ({rec.GetInt("View")})."));

            if (_grf.IsConfigured && !string.IsNullOrEmpty(entry.IdentifiedResourceName)
                && !_grf.Exists(GrfAssetPaths.ItemIcon(entry.IdentifiedResourceName)))
                issues.Add(new ValidationIssue(ValidationSeverity.Warning, "item_db", key, "icon",
                    $"Inventory icon '{entry.IdentifiedResourceName}.bmp' not found in the configured GRF."));
        }
    }

    private void ValidateMobs(List<ValidationIssue> issues)
    {
        if (_schemas.Get("mob_db") is not { } schema || !_mobSprite.IsAvailable) return;
        var overlay = _session.GetActiveOverlay(schema);

        foreach (var rec in overlay.Effective().Where(r => r.Origin != RecordOrigin.Base))
        {
            int id = rec.GetInt("Id");
            if (_mobSprite.FindConstantForMob(id) is null)
                issues.Add(new ValidationIssue(ValidationSeverity.Warning, "mob_db", id.ToString(), "sprite",
                    "Custom mob is not registered in npcidentity.lub — the client will fail to load its sprite."));
        }
    }
}
