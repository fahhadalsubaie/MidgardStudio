using System;
using System.IO;
using MidgardStudio.Core.Commands;
using MidgardStudio.Core.IO;
using MidgardStudio.Core.Lua;
using MidgardStudio.Core.Sprites;
using MidgardStudio.Core.Workspace;

namespace MidgardStudio.App.Services;

/// <summary>
/// Registers a monster's client sprite: appends <c>JT_&lt;NAME&gt; = &lt;mobId&gt;</c> to npcidentity.lub's
/// jobtbl and <c>[jobtbl.JT_&lt;NAME&gt;] = "&lt;sprite&gt;"</c> to jobname.lub's JobNameTable. Registrations
/// are <b>queued in memory</b> and written only on the next Save (so they undo/discard like every other
/// edit), via one atomic transaction. Registered-id / constant lookups reflect the working state
/// (disk ∪ pending) so the validator sees a queued registration as already done.
/// </summary>
public sealed class MobSpriteService : IDirtySource
{
    private readonly WorkspaceSession _session;
    private LuaFileCodec _codec => _session.ClientCodec; // fixed Windows-1252 (the RO client boundary), independent of the profile Display Encoding
    private readonly List<PendingRegistration> _pending = new();

    public MobSpriteService(WorkspaceSession session)
    {
        _session = session;
        // A profile switch points at a different server -> drop queued (now-irrelevant) registrations.
        _session.WorkspaceReloaded += () => { if (_pending.Count > 0) { _pending.Clear(); RaiseDirty(); } };
    }

    private WorkspacePaths Paths => _session.Paths;

    private string DataInfoDir => Path.Combine(Paths.LuaFilesRoot, "datainfo");
    private string NpcIdentityPath => Path.Combine(DataInfoDir, "npcidentity.lub");
    private string JobNamePath => Path.Combine(DataInfoDir, "jobname.lub");

    public bool IsAvailable => File.Exists(NpcIdentityPath) && File.Exists(JobNamePath);

    // ---- unsaved state: one dirty source among several (see CompositeDirtyState) ----
    public bool IsDirty => _pending.Count > 0;
    public event Action? DirtyChanged;
    private void RaiseDirty() => DirtyChanged?.Invoke();

    /// <summary>The file the next <see cref="Save"/> writes to (for the save summary).</summary>
    public string SaveTargetPath => NpcIdentityPath;

    private Dictionary<string, int> DiskConstants() =>
        File.Exists(NpcIdentityPath) ? AccessoryTables.ReadConstants(_codec.ReadText(NpcIdentityPath), "jobtbl") : new();

    /// <summary>The JT_ constant mapped to a mob id in the working state (pending first, else disk), or null.</summary>
    public string? FindConstantForMob(int mobId)
    {
        var pendingHit = _pending.FirstOrDefault(p => p.Id == mobId);
        if (pendingHit is not null) return pendingHit.ConstantName;
        if (!File.Exists(NpcIdentityPath)) return null;
        return DiskConstants().FirstOrDefault(kv => kv.Value == mobId).Key;
    }

    /// <summary>All mob ids registered in the working state (npcidentity.lub jobtbl ∪ pending) — parsed once;
    /// used for a bulk validation check instead of re-reading the file per mob.</summary>
    public HashSet<int> RegisteredMobIds() => SpriteRegistry.RegisteredIds(DiskConstants(), _pending);

    /// <summary>Plans a registration WITHOUT mutating state: reuses an existing JT_ constant for this mob id,
    /// else allocates a fresh one, computed against the working state (disk ∪ pending). The caller commits it
    /// through the undo stack via <see cref="AddPending"/> / <see cref="RemovePending"/>, so it's undoable and
    /// discarded if never saved.</summary>
    public PendingRegistration PlanMob(int mobId, string aegis, string spriteName)
    {
        var disk = DiskConstants();
        string? existing = disk.FirstOrDefault(kv => kv.Value == mobId).Key
            ?? _pending.FirstOrDefault(p => p.Id == mobId)?.ConstantName;
        string constName;
        if (existing is not null) constName = existing;
        else
        {
            string baseName = "JT_" + Sanitize(aegis);
            constName = baseName;
            int suffix = 1;
            while (SpriteRegistry.HasConstant(disk, _pending, constName)) constName = $"{baseName}_{suffix++}";
        }
        return new PendingRegistration(constName, mobId, spriteName);
    }

    public void AddPending(PendingRegistration p) { _pending.Add(p); RaiseDirty(); }

    public void RemovePending(PendingRegistration p) { if (_pending.Remove(p)) RaiseDirty(); }

    /// <summary>True when an identical registration (same mob id + sprite) is already queued — lets the Mob
    /// Sprite tab no-op a duplicate click while still allowing a re-register with a *different* sprite.</summary>
    public bool HasPending(int mobId, string sprite) => _pending.Any(p => p.Id == mobId && p.Sprite == sprite);

    /// <summary>Flushes queued registrations to npcidentity.lub + jobname.lub in one atomic transaction.
    /// The appends throw (fail-loud) on a malformed table BEFORE anything is staged, so a failure leaves the
    /// files untouched and the queue intact for retry; the caller keeps the dirty state.</summary>
    public void Save()
    {
        if (_pending.Count == 0) return;
        string idText = _codec.ReadText(NpcIdentityPath);
        string jobText = _codec.ReadText(JobNamePath);
        var have = new HashSet<string>(AccessoryTables.ReadConstants(idText, "jobtbl").Keys, StringComparer.Ordinal);
        foreach (var p in _pending)
        {
            if (have.Add(p.ConstantName))
                idText = AccessoryTables.AppendConstant(idText, "jobtbl", p.ConstantName, p.Id);
            // Upsert (not append): a re-registered mob already in JobNameTable updates its sprite line in
            // place instead of appending a duplicate key that accumulates on every save (audit #7).
            jobText = AccessoryTables.SetOrAppendName(jobText, "JobNameTable", "jobtbl", p.ConstantName, p.Sprite);
        }
        var tx = new FileTransaction(Path.Combine(Paths.LuaFilesRoot, ".midgard-backup"));
        tx.Stage(NpcIdentityPath, _codec.EncodeText(idText));
        tx.Stage(JobNamePath, _codec.EncodeText(jobText));
        tx.Commit();
        _pending.Clear();
        RaiseDirty();
    }

    private static string Sanitize(string s) =>
        new(s.Select(c => char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : '_').ToArray());
}
