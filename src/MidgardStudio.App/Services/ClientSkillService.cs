using System.IO;
using System.Linq;
using MidgardStudio.Core.IO;
using MidgardStudio.Core.Lua;
using MidgardStudio.Core.Workspace;

namespace MidgardStudio.App.Services;

/// <summary>
/// Loads and saves the client-side skill text from <c>lua-files/skillinfoz/</c> (skillid + skillinfolist +
/// skilldescript + skilldelaylist). Mirrors <see cref="ClientItemService"/>: an in-memory model, a
/// per-skill content-comparison dirty set (so undo-to-baseline reads clean, no sticky flag), and an
/// in-place splice save that preserves header comments + untouched entries. Edits are encoded as
/// Windows-1252 (the fixed RO client boundary), independent of the profile Display Encoding. The skill tree
/// (skilltreeview) is intentionally out of scope.
/// </summary>
public sealed class ClientSkillService
{
    private readonly WorkspaceSession _session;

    private LuaFileCodec Codec => _session.ClientCodec;

    private ClientSkillTables? _tables;
    private string _skidText = string.Empty, _infoText = string.Empty, _descText = string.Empty, _delayText = string.Empty;
    private HashSet<string> _origSkid = new(StringComparer.Ordinal);

    // Per-file formatted baselines captured at load, so a skill is only re-spliced into a file whose own
    // content actually changed (editing a name doesn't rewrite the 600 KB description file).
    private readonly Dictionary<string, string> _baseInfo = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _baseDesc = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _baseDelay = new(StringComparer.Ordinal);
    private readonly HashSet<string> _dirty = new(StringComparer.Ordinal);

    public ClientSkillService(WorkspaceSession session)
    {
        _session = session;
        _session.WorkspaceReloaded += DropCaches;
        // The dirty set is maintained incrementally by NotifyEdited. An undo/redo of a validation quick-fix
        // can revert a skill to its baseline without that path firing — so reconcile the set against the model
        // on every command-stack change, ensuring IsDirty (and the Save button) settle to the true state.
        // Runs before the shell's own Commands.Changed handler (this service is constructed first), so the
        // Save-button re-query sees the corrected set.
        _session.Commands.Changed += ReconcileDirty;
    }

    /// <summary>Re-evaluates every currently-dirty skill against its baseline; drops those now back to it.</summary>
    private void ReconcileDirty()
    {
        if (_tables is null || _dirty.Count == 0) return;
        foreach (var constant in _dirty.ToList()) NotifyEdited(constant);
    }

    private WorkspacePaths Paths => _session.Paths;
    private string Dir => Path.Combine(Paths.LuaFilesRoot, "skillinfoz");
    private string SkidPath => Path.Combine(Dir, "skillid.lub");
    private string InfoPath => Path.Combine(Dir, "skillinfolist.lub");
    private string DescPath => Path.Combine(Dir, "skilldescript.lub");
    private string DelayPath => Path.Combine(Dir, "skilldelaylist.lub");

    /// <summary>True when the client skill files are present (the tab is hidden/empty otherwise).</summary>
    public bool IsAvailable => File.Exists(SkidPath) && File.Exists(InfoPath);

    /// <summary>The file the next <see cref="Save"/> writes to (for the save summary).</summary>
    public string SaveTargetPath => InfoPath;

    /// <summary>True when any skill's content differs from what's on disk.</summary>
    public bool IsDirty => _dirty.Count > 0;

    private void DropCaches()
    {
        _tables = null;
        _skidText = _infoText = _descText = _delayText = string.Empty;
        _origSkid = new HashSet<string>(StringComparer.Ordinal);
        _baseInfo.Clear(); _baseDesc.Clear(); _baseDelay.Clear(); _dirty.Clear();
    }

    private ClientSkillTables Tables
    {
        get { EnsureLoaded(); return _tables!; }
    }

    private void EnsureLoaded()
    {
        if (_tables is not null) return;

        _skidText = File.Exists(SkidPath) ? Codec.ReadText(SkidPath) : string.Empty;
        _infoText = File.Exists(InfoPath) ? Codec.ReadText(InfoPath) : string.Empty;
        _descText = File.Exists(DescPath) ? Codec.ReadText(DescPath) : string.Empty;
        _delayText = File.Exists(DelayPath) ? Codec.ReadText(DelayPath) : string.Empty;

        _tables = ClientSkillReader.ReadAll(_skidText, _infoText, _descText, _delayText);
        _origSkid = new HashSet<string>(_tables.Skid.Keys, StringComparer.Ordinal);

        _baseInfo.Clear(); _baseDesc.Clear(); _baseDelay.Clear(); _dirty.Clear();
        foreach (var s in _tables.Skills.Values)
        {
            if (InfoContent(s) is { } i) _baseInfo[s.Constant] = i;
            if (DescContent(s) is { } d) _baseDesc[s.Constant] = d;
            if (DelayContent(s) is { } dl) _baseDelay[s.Constant] = dl;
        }
    }

    // "The text this file would receive, or null when nothing is written" — one definition (Core), shared by
    // the load baseline, the dirty diff, and the save, so they can never disagree. See ClientSkillContent.
    private static string? InfoContent(ClientSkill s) => ClientSkillContent.Info(s);
    private static string? DescContent(ClientSkill s) => ClientSkillContent.Descript(s);
    private static string? DelayContent(ClientSkill s) => ClientSkillContent.Delay(s);

    // ----- read access (list + editor + validation) -----

    /// <summary>All skills, ordered by id then constant (for the master list).</summary>
    public IReadOnlyList<ClientSkill> ListSkills() =>
        Tables.Skills.Values.OrderBy(s => s.Id == 0 ? int.MaxValue : s.Id).ThenBy(s => s.Constant, StringComparer.Ordinal).ToList();

    public ClientSkill? Get(string constant) => Tables.Skills.GetValueOrDefault(constant);

    public bool Exists(string constant) => Tables.Skills.ContainsKey(constant);

    /// <summary>The parsed tables (for the validator). Loads on demand.</summary>
    public ClientSkillTables SnapshotTables() => Tables;

    /// <summary>Constants the user has edited or created this session (the dirty set) — what the
    /// validation panel scopes to in CustomOnly mode and what the save gate checks.</summary>
    public IReadOnlyCollection<string> EditedConstants() => _dirty.ToHashSet(StringComparer.Ordinal);

    public int NextFreeId()
    {
        EnsureLoaded();
        int max = _tables!.Skid.Count == 0 ? 0 : _tables.Skid.Values.Max();
        return Math.Max(max + 1, 1);
    }

    // ----- mutation -----

    /// <summary>Recomputes the dirty state for one skill after the editor mutates it (content comparison
    /// against the per-file baseline, so an undo back to the loaded state clears the flag).</summary>
    public void NotifyEdited(string constant)
    {
        if (_tables is null || !_tables.Skills.TryGetValue(constant, out var s)) { _dirty.Remove(constant); return; }

        bool changed =
            DiffersFrom(_baseInfo, constant, InfoContent(s)) ||
            DiffersFrom(_baseDesc, constant, DescContent(s)) ||
            DiffersFrom(_baseDelay, constant, DelayContent(s)) ||
            IsSessionNew(constant); // a skill WE created this session is dirty until saved

        if (changed) _dirty.Add(constant); else _dirty.Remove(constant);
    }

    /// <summary>True only for a skill created this session (<see cref="AllocateNew"/> added its SKID
    /// constant). NOT true for a skill that was loaded from skillinfolist but is simply missing from
    /// skillid.lub — that's a pre-existing data condition, not a user edit. Keying "brand new" off
    /// <c>!_origSkid.Contains</c> alone latched such skills permanently dirty (undo can't change
    /// <c>_origSkid</c>), leaving the Save button lit after a full undo of a quick-fix.</summary>
    private bool IsSessionNew(string constant) =>
        _tables is not null && _tables.Skid.ContainsKey(constant) && !_origSkid.Contains(constant);

    private static bool DiffersFrom(Dictionary<string, string> baseline, string key, string? current)
    {
        baseline.TryGetValue(key, out var b);
        return (current ?? string.Empty) != (b ?? string.Empty);
    }

    /// <summary>Creates a brand-new custom skill (allocating a fresh SKID id + a unique constant) and adds
    /// it to the in-memory tables. Returns the new skill, already marked dirty.</summary>
    public ClientSkill AllocateNew(int id)
    {
        EnsureLoaded();
        if (id <= 0) id = NextFreeId();
        string constant = $"CUSTOM_{id}";
        int n = id;
        while (_tables!.Skid.ContainsKey(constant) || _tables.Skills.ContainsKey(constant))
            constant = $"CUSTOM_{++n}";

        var skill = new ClientSkill
        {
            Constant = constant,
            Id = id,
            HasInfo = true,
            Aegis = constant,
            SkillName = "New Skill",
            MaxLv = 1,
            HasMaxLv = true,
            HasDescript = true,
            Description = new List<string> { "Custom skill." },
        };
        _tables.Skid[constant] = id;
        _tables.Skills[constant] = skill;
        NotifyEdited(constant);
        return skill;
    }

    public void Remove(string constant)
    {
        EnsureLoaded();
        _tables!.Skills.Remove(constant);
        // Note: the SKID constant + on-disk blocks are left in place (the splicer only adds/replaces);
        // removing a brand-new unsaved skill simply drops it before it is ever written.
        if (!_origSkid.Contains(constant)) _tables.Skid.Remove(constant);
        NotifyEdited(constant);
    }

    // ----- save -----

    public void Save()
    {
        if (!IsDirty || _tables is null) return;

        var infoEntries = new List<(string, string)>();
        var descEntries = new List<(string, string)>();
        var delayEntries = new List<(string, string)>();
        var newConstants = new List<(string Constant, int Id)>();

        foreach (var c in _dirty)
        {
            if (!_tables.Skills.TryGetValue(c, out var s)) continue;
            string key = $"SKID.{c}";

            if (InfoContent(s) is { } i && DiffersFrom(_baseInfo, c, i)) infoEntries.Add((key, i));
            if (DescContent(s) is { } d && DiffersFrom(_baseDesc, c, d)) descEntries.Add((key, d));
            if (DelayContent(s) is { } dl && DiffersFrom(_baseDelay, c, dl)) delayEntries.Add((key, dl));

            if (IsSessionNew(c)) newConstants.Add((c, s.Id)); // only write a SKID entry for skills we created
        }

        // Read fresh on-disk text so a concurrent external edit isn't clobbered by a stale buffer.
        string skidText = File.Exists(SkidPath) ? Codec.ReadText(SkidPath) : _skidText;
        string infoText = File.Exists(InfoPath) ? Codec.ReadText(InfoPath) : _infoText;
        string descText = File.Exists(DescPath) ? Codec.ReadText(DescPath) : _descText;
        string delayText = File.Exists(DelayPath) ? Codec.ReadText(DelayPath) : _delayText;

        // Append new SKID constants FIRST so _NeedSkillList forward references resolve in-client.
        foreach (var (constant, id) in newConstants)
            skidText = AccessoryTables.AppendConstant(skidText, "SKID", constant, id);

        if (infoEntries.Count > 0) infoText = ExprKeyTableSplicer.Splice(infoText, "SKILL_INFO_LIST", infoEntries);
        if (descEntries.Count > 0) descText = ExprKeyTableSplicer.Splice(descText, "SKILL_DESCRIPT", descEntries);
        if (delayEntries.Count > 0) delayText = ExprKeyTableSplicer.Splice(delayText, "SKILL_DELAY_LIST", delayEntries);

        // Stage every touched file into ONE transaction (per-file backup + all-or-nothing rollback).
        var tx = new FileTransaction(Path.Combine(Dir, ".midgard-backup"));
        if (newConstants.Count > 0) tx.Stage(SkidPath, Codec.EncodeText(skidText));
        if (infoEntries.Count > 0) tx.Stage(InfoPath, Codec.EncodeText(infoText));
        if (descEntries.Count > 0) tx.Stage(DescPath, Codec.EncodeText(descText));
        if (delayEntries.Count > 0) tx.Stage(DelayPath, Codec.EncodeText(delayText));
        tx.Commit();

        // Re-baseline so the just-saved state reads clean.
        _skidText = skidText; _infoText = infoText; _descText = descText; _delayText = delayText;
        _origSkid = new HashSet<string>(_tables.Skid.Keys, StringComparer.Ordinal);
        foreach (var c in _dirty.ToList())
        {
            if (!_tables.Skills.TryGetValue(c, out var s)) { _baseInfo.Remove(c); _baseDesc.Remove(c); _baseDelay.Remove(c); continue; }
            Rebaseline(_baseInfo, c, InfoContent(s));
            Rebaseline(_baseDesc, c, DescContent(s));
            Rebaseline(_baseDelay, c, DelayContent(s));
        }
        _dirty.Clear();
    }

    /// <summary>Sets (or clears) a per-file baseline to match the just-saved content.</summary>
    private static void Rebaseline(Dictionary<string, string> baseline, string key, string? content)
    {
        if (content is null) baseline.Remove(key); else baseline[key] = content;
    }
}
