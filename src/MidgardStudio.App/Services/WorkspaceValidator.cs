using MidgardStudio.Core.Lua;
using MidgardStudio.Core.Model;
using MidgardStudio.Core.Validation;
using MidgardStudio.Core.Sprites;
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
    private readonly CashShopService _cashShop;

    public WorkspaceValidator(WorkspaceSession session, SchemaRegistry schemas, ReferenceIndex references,
        ClientItemService client, ClientSkillService clientSkills, MobSpriteService mobSprite, SpriteLinkService sprite,
        GrfService grf, CashShopService cashShop)
    {
        _session = session;
        _schemas = schemas;
        _references = references;
        _client = client;
        _clientSkills = clientSkills;
        _mobSprite = mobSprite;
        _sprite = sprite;
        _grf = grf;
        _cashShop = cashShop;
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
        ValidateCashShop(issues);

        return new ValidationReport(issues);
    }

    /// <summary>Cash-shop findings (unknown item / dup-in-tab / price 0). Bespoke db (no schema), so it runs
    /// outside the schema loop; cheap when the shop is empty.</summary>
    private void ValidateCashShop(List<ValidationIssue> issues)
    {
        try { issues.AddRange(_cashShop.Validate()); }
        catch { /* item_cash unreadable in this workspace — skip, not fatal */ }
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
        if (wanted.Contains(MidgardStudio.Core.CashShop.CashShopValidator.DbId)) ValidateCashShop(issues);

        return new ValidationReport(issues);
    }

    private void ValidateItemClientFiles(List<ValidationIssue> issues, ValidationScope scope)
    {
        if (_schemas.Get("item_db") is not { } schema) return;
        var overlay = _session.GetActiveOverlay(schema);
        // The rules + their quick-fixes live in Core (ItemClientFileValidator), reached through ports — the
        // live client/GRF/sprite services are wrapped by the adapters below.
        issues.AddRange(ItemClientFileValidator.Validate(overlay.Effective(), scope,
            new ClientItemProbe(_client), new GrfIconProbe(_grf), new AccessoryMapProbe(_sprite), new ClientItemEditor(_client)));
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
                Category = "Client Mobs", // DbId stays mob_db so "Go to" opens the Monsters list (no Client Mobs tab)
                Fix = string.IsNullOrEmpty(aegis) ? null : MakeMobSpriteFix(id, aegis),
            });
        }
    }

    /// <summary>The mob-sprite quick-fix as a reversible registration: Apply queues it (undoable, written on
    /// Save), Revert removes the queued entry. Captures the pending entry so undo pulls exactly it.</summary>
    private QuickFix MakeMobSpriteFix(int id, string aegis)
    {
        PendingRegistration? pending = null;
        return new QuickFix("Register mob sprite",
            () => { pending = _mobSprite.PlanMob(id, aegis, aegis); _mobSprite.AddPending(pending); },
            () => { if (pending is not null) _mobSprite.RemovePending(pending); });
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
                    Fix = new QuickFix($"Set client MaxLv to {sv.MaxLevel}", () => SetClientSkillMaxLv(s.Constant, sv.MaxLevel), () => SetClientSkillMaxLv(s.Constant, oldMaxLv)) { Automatic = true },
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

    // ---- Core port adapters: the only place the item cross-file rules touch live client/GRF/sprite services ----

    private sealed class ClientItemProbe(ClientItemService client) : IClientItemProbe
    {
        public bool Exists(int id) => client.Exists(id);
        public ClientItemFacts? Get(int id)
        {
            if (!client.Exists(id)) return null;
            var e = client.GetOrCreate(id);
            return new ClientItemFacts(e.SlotCount, e.ClassNum, e.IdentifiedResourceName);
        }
    }

    private sealed class GrfIconProbe(GrfService grf) : IGrfIconProbe
    {
        public bool IsConfigured => grf.IsConfigured;
        public bool IconExists(string resourceName) => grf.Exists(GrfAssetPaths.ItemIcon(resourceName));
    }

    private sealed class AccessoryMapProbe(SpriteLinkService sprite) : IAccessoryMapProbe
    {
        public bool IsAvailable => sprite.IsAvailable;
        public IReadOnlySet<int> MappedViewIds() => sprite.MappedViewIds();
    }

    private sealed class ClientItemEditor(ClientItemService client) : IClientItemEditor
    {
        public void SetSlots(int id, int slots) { var e = client.GetOrCreate(id); e.SlotCount = slots; client.Upsert(e); }
        public void SetClassNum(int id, int classNum) { var e = client.GetOrCreate(id); e.ClassNum = classNum; client.Upsert(e); }

        public void CreateText(int id, string name, int slots, int classNum)
        {
            var e = client.GetOrCreate(id);
            e.IdentifiedDisplayName = name;
            e.UnidentifiedDisplayName = name;
            e.SlotCount = slots;
            e.ClassNum = classNum;
            if (e.IdentifiedDescription.Count == 0) e.IdentifiedDescription.Add(name);
            client.Upsert(e);
        }

        public void Remove(int id) => client.Remove(id);
    }
}
