using MidgardStudio.Core.Lua;
using MidgardStudio.Core.Model;
using MidgardStudio.Core.Validation;
using MidgardStudio.Core.Validation.Validators;
using MidgardStudio.Grf;

namespace MidgardStudio.App.Services;

/// <summary>
/// The workspace-wide validation orchestrator. Runs the headless Core <see cref="ValidationEngine"/>
/// across every registered database (server-side correctness + cross-references), then layers the
/// App-only cross-file rules that need the client/GRF/sprite services. All findings carry a stable
/// RuleId and, where possible, a one-click quick-fix.
/// </summary>
public sealed class WorkspaceValidator
{
    private readonly WorkspaceSession _session;
    private readonly SchemaRegistry _schemas;
    private readonly ReferenceIndex _references;
    private readonly ClientItemService _client;
    private readonly ClientSkillService _clientSkills;
    private readonly MobSpriteService _mobSprite;
    private readonly SpriteLinkService _sprite;
    private readonly GrfService _grf;

    public WorkspaceValidator(WorkspaceSession session, SchemaRegistry schemas, ReferenceIndex references,
        ClientItemService client, ClientSkillService clientSkills, MobSpriteService mobSprite, SpriteLinkService sprite, GrfService grf)
    {
        _session = session;
        _schemas = schemas;
        _references = references;
        _client = client;
        _clientSkills = clientSkills;
        _mobSprite = mobSprite;
        _sprite = sprite;
        _grf = grf;
    }

    /// <summary>A validation context bound to the current mode and the cached reference index. Reused by
    /// the live per-record path (so editing validates against the same references the panel uses).</summary>
    public ValidationContext CurrentContext() => ValidationContext.Create(_references, _session.Mode);

    /// <summary>Runs the full workspace scan and returns a structured report. Safe to call off the UI thread.</summary>
    public ValidationReport Validate(ValidationScope scope = ValidationScope.CustomOnly)
    {
        // The reference index self-invalidates on edit / undo / reload / mode change, so consecutive scans
        // with no edits in between reuse the cached name sets instead of rebuilding every run.
        var ctx = CurrentContext();
        var issues = new List<ValidationIssue>();

        foreach (var schema in _schemas.All)
        {
            if (schema.IsNested) continue;
            try
            {
                var overlay = _session.GetActiveOverlay(schema);
                issues.AddRange(_session.Validation.ValidateOverlay(overlay, scope, ctx));
            }
            catch { /* a database that can't be loaded in this workspace is skipped, not fatal */ }
        }

        ValidateItemClientFiles(issues, scope);
        ValidateMobClientFiles(issues, scope);
        ValidateClientSkills(issues, scope);

        return new ValidationReport(issues);
    }

    /// <summary>Validates only the given databases (plus their client files). Used by the save gate, which
    /// only cares about what is about to be written — far cheaper than a whole-workspace scan.</summary>
    public ValidationReport ValidateDatabases(IEnumerable<string> dbIds, ValidationScope scope = ValidationScope.CustomOnly)
    {
        var ctx = CurrentContext();
        var issues = new List<ValidationIssue>();
        var wanted = new HashSet<string>(dbIds, StringComparer.Ordinal);

        foreach (var schema in _schemas.All)
        {
            if (schema.IsNested || !wanted.Contains(schema.Id)) continue;
            try
            {
                var overlay = _session.GetActiveOverlay(schema);
                issues.AddRange(_session.Validation.ValidateOverlay(overlay, scope, ctx));
            }
            catch { /* unloadable db skipped */ }
        }

        if (wanted.Contains("item_db")) ValidateItemClientFiles(issues, scope);
        if (wanted.Contains("mob_db")) ValidateMobClientFiles(issues, scope);
        if (wanted.Contains("skill_db")) ValidateClientSkills(issues, scope);

        return new ValidationReport(issues);
    }

    private void ValidateItemClientFiles(List<ValidationIssue> issues, ValidationScope scope)
    {
        if (_schemas.Get("item_db") is not { } schema) return;
        var overlay = _session.GetActiveOverlay(schema);

        // Parse the accessory view map ONCE for the whole pass (HasView re-read + re-parsed the lua per item).
        var mappedViews = _sprite.IsAvailable ? _sprite.MappedViewIds() : new HashSet<int>();

        foreach (var rec in overlay.Effective())
        {
            if (scope == ValidationScope.CustomOnly && rec.Origin == RecordOrigin.Base) continue;
            int id = rec.GetInt("Id");
            string key = id.ToString();

            if (!_client.Exists(id))
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Warning, "item_db", key, "client",
                    "Item doesn't exist in Client Items (itemInfo.lua / itemInfo_C.lua) — it will show no name or description in-game.")
                {
                    RuleId = "XFILE.ITEM_NO_CLIENTTEXT",
                    Fix = new QuickFix("Create client text", () => CreateClientText(rec), () => _client.Remove(id)),
                });
                continue;
            }

            var entry = _client.GetOrCreate(id);

            int slots = rec.GetInt("Slots");
            if (entry.SlotCount != slots)
            {
                int oldSlots = entry.SlotCount;
                issues.Add(new ValidationIssue(ValidationSeverity.Warning, "item_db", key, "SlotCount",
                    $"Slots count mismatch — Server [{slots}], Client [{entry.SlotCount}].")
                {
                    RuleId = "XFILE.SLOTCOUNT_MISMATCH",
                    Fix = new QuickFix($"Set client slots to {slots}", () => SetClientSlot(id, slots), () => SetClientSlot(id, oldSlots)),
                });
            }

            var loc = rec.GetSet("Locations");
            bool isHeadgear = loc is not null
                && (loc.Contains("Head_Top") || loc.Contains("Head_Mid") || loc.Contains("Head_Low")
                    || loc.Contains("Costume_Head_Top") || loc.Contains("Costume_Head_Mid") || loc.Contains("Costume_Head_Low"));
            bool isGarment = loc is not null && (loc.Contains("Garment") || loc.Contains("Costume_Garment"));

            // Server View only drives the broadcast worn sprite for headgear/costume-head and garment, where it
            // must equal the client ClassNum. For weapons (sprite comes from SubType), accessories, cards and
            // generic items the two are independent — so don't flag a mismatch there.
            int view = rec.GetInt("View");
            if ((isHeadgear || isGarment) && entry.ClassNum != view)
            {
                int oldView = entry.ClassNum;
                issues.Add(new ValidationIssue(ValidationSeverity.Warning, "item_db", key, "ClassNum",
                    $"View / ClassNum mismatch — Server View [{view}], Client ClassNum [{entry.ClassNum}]. " +
                    "For headgear and garments these must match or the equipped sprite won't appear.")
                {
                    RuleId = "XFILE.CLASSNUM_MISMATCH",
                    Fix = new QuickFix($"Set client ClassNum to {view}", () => SetClientClassNum(id, view), () => SetClientClassNum(id, oldView)),
                });
            }

            if (_grf.IsConfigured && !string.IsNullOrEmpty(entry.IdentifiedResourceName)
                && !_grf.Exists(GrfAssetPaths.ItemIcon(entry.IdentifiedResourceName)))
                issues.Add(new ValidationIssue(ValidationSeverity.Warning, "item_db", key, "icon",
                    $"Inventory icon '{entry.IdentifiedResourceName}.bmp' not found in the configured GRF.")
                { RuleId = "XFILE.ICON_MISSING" });

            if (isHeadgear && view > 0 && _sprite.IsAvailable && !mappedViews.Contains(view))
                issues.Add(new ValidationIssue(ValidationSeverity.Warning, "item_db", key, "View",
                    $"Headgear View {view} is not mapped in accessoryid.lub / accname.lub — the sprite won't show.")
                { RuleId = "XFILE.HEADGEAR_NO_ACCMAP" });
        }
    }

    private void ValidateMobClientFiles(List<ValidationIssue> issues, ValidationScope scope)
    {
        if (_schemas.Get("mob_db") is not { } schema || !_mobSprite.IsAvailable) return;
        var overlay = _session.GetActiveOverlay(schema);

        // Parse the registered mob ids ONCE (this used to re-read + re-parse a 142 KB lua file per mob).
        var registered = _mobSprite.RegisteredMobIds();

        foreach (var rec in overlay.Effective())
        {
            if (scope == ValidationScope.CustomOnly && rec.Origin == RecordOrigin.Base) continue;
            int id = rec.GetInt("Id");
            if (registered.Contains(id)) continue;

            string aegis = rec.GetString("AegisName") ?? string.Empty;
            issues.Add(new ValidationIssue(ValidationSeverity.Warning, "mob_db", id.ToString(), "sprite",
                "Custom mob is not registered in npcidentity.lub — the client will fail to load its sprite.")
            {
                RuleId = "XFILE.MOB_NOT_REGISTERED",
                Fix = string.IsNullOrEmpty(aegis) ? null
                    : new QuickFix("Register mob sprite", () => _mobSprite.RegisterMob(id, aegis, aegis)),
            });
        }
    }

    /// <summary>Client skill validation: the Core internal-consistency rules plus a cross-check against the
    /// server skill_db (orphan SKID id, max-level mismatch, aegis mismatch). In CustomOnly scope only the
    /// user's edited/created skills are checked (so the panel isn't flooded by the official translation's
    /// pre-existing quirks); a full scan checks everything.</summary>
    private void ValidateClientSkills(List<ValidationIssue> issues, ValidationScope scope)
    {
        if (!_clientSkills.IsAvailable) return;
        if (scope == ValidationScope.CustomOnly && !_clientSkills.IsDirty) return; // nothing the user touched — skip the load

        var tables = _clientSkills.SnapshotTables();
        var edited = _clientSkills.EditedConstants();
        bool InScope(string constant) => scope != ValidationScope.CustomOnly || edited.Contains(constant);

        foreach (var issue in ClientSkillValidator.Validate(tables))
            if (InScope(issue.Key))
                issues.Add(issue);

        // Cross-check against the server skill_db (loaded anyway).
        if (_schemas.Get("skill_db") is not { } schema) return;
        var server = new Dictionary<int, (string Name, int MaxLevel)>();
        try
        {
            foreach (var r in _session.GetActiveOverlay(schema).Effective())
            {
                int sid = r.GetInt("Id");
                if (sid != 0 && !server.ContainsKey(sid)) server[sid] = (r.GetString("Name") ?? string.Empty, r.GetInt("MaxLevel"));
            }
        }
        catch { return; }

        foreach (var s in tables.Skills.Values)
        {
            if (!s.HasInfo || !InScope(s.Constant)) continue;
            string key = s.Constant;

            if (!server.TryGetValue(s.Id, out var sv))
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Warning, ClientSkillValidator.DbId, key, "skill_db",
                    $"No server skill_db entry with id {s.Id} — this client skill has no server-side definition.")
                { RuleId = "XFILE.CSKILL_NOT_IN_SKILLDB" });
                continue;
            }

            if (sv.MaxLevel > 0 && s.MaxLv != sv.MaxLevel)
            {
                int oldMaxLv = s.MaxLv;
                issues.Add(new ValidationIssue(ValidationSeverity.Warning, ClientSkillValidator.DbId, key, "MaxLv",
                    $"Max level mismatch — Server [{sv.MaxLevel}], Client [{s.MaxLv}].")
                {
                    RuleId = "XFILE.CSKILL_MAXLV_MISMATCH",
                    Fix = new QuickFix($"Set client MaxLv to {sv.MaxLevel}", () => SetClientSkillMaxLv(s.Constant, sv.MaxLevel), () => SetClientSkillMaxLv(s.Constant, oldMaxLv)),
                });
            }

            if (!string.IsNullOrEmpty(sv.Name) && !string.Equals(sv.Name, s.Constant, StringComparison.Ordinal))
                issues.Add(new ValidationIssue(ValidationSeverity.Warning, ClientSkillValidator.DbId, key, "Name",
                    $"Aegis mismatch — server skill #{s.Id} is '{sv.Name}', client SKID is '{s.Constant}'.")
                { RuleId = "XFILE.CSKILL_NAME_MISMATCH" });
        }
    }

    private void SetClientSkillMaxLv(string constant, int maxLv)
    {
        if (_clientSkills.Get(constant) is { } s)
        {
            s.MaxLv = maxLv;
            s.HasMaxLv = true; // a cross-check fix writes an explicit MaxLv, so mark it present
            _clientSkills.NotifyEdited(constant);
        }
    }

    // ---- quick-fix helpers (UI thread) ----

    private void CreateClientText(DbRecord rec)
    {
        int id = rec.GetInt("Id");
        var entry = _client.GetOrCreate(id);
        string? name = rec.GetString("Name");
        if (string.IsNullOrWhiteSpace(name)) name = rec.GetString("AegisName") ?? $"Item {id}";
        entry.IdentifiedDisplayName = name;
        entry.UnidentifiedDisplayName = name;
        entry.SlotCount = rec.GetInt("Slots");
        entry.ClassNum = rec.GetInt("View");
        if (entry.IdentifiedDescription.Count == 0) entry.IdentifiedDescription.Add(name);
        _client.Upsert(entry);
    }

    private void SetClientSlot(int id, int slots)
    {
        var entry = _client.GetOrCreate(id);
        entry.SlotCount = slots;
        _client.Upsert(entry);
    }

    private void SetClientClassNum(int id, int view)
    {
        var entry = _client.GetOrCreate(id);
        entry.ClassNum = view;
        _client.Upsert(entry);
    }
}
