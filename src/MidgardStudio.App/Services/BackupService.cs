using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using MidgardStudio.Core.Backup;
using MidgardStudio.Core.IO;

namespace MidgardStudio.App.Services;

/// <summary>One on-disk backup snapshot — a thin App wrapper over the Core <see cref="BackupManifest"/> plus
/// its folder path, with display helpers the Backup Manager binds to.</summary>
public sealed class BackupEntry
{
    public BackupEntry(BackupManifest manifest, string folderPath)
    {
        Manifest = manifest;
        FolderPath = folderPath;
    }

    public BackupManifest Manifest { get; }

    /// <summary>Absolute path to the snapshot folder (runtime; not persisted).</summary>
    public string FolderPath { get; }

    public string Label => Manifest.Label;
    public string Note => Manifest.Note;
    public bool Pinned => Manifest.Pinned;
    public DateTime TimestampUtc => Manifest.TimestampUtc;

    public DateTime When => Manifest.TimestampUtc.ToLocalTime();
    public string WhenText => When.ToString("yyyy-MM-dd  HH:mm:ss");
    public string DayText => When.ToString("dddd, dd MMM yyyy");
    public int FileCount => Manifest.Files.Count;
    public string SizeText => HumanSize(Manifest.TotalBytes);
    public string SummaryText => $"{WhenText}  ·  {FileCount} files  ·  {SizeText}";

    /// <summary>Coarse relative age for the list (recomputed on each Refresh; not live-updating).</summary>
    public string RelativeText
    {
        get
        {
            var d = DateTime.Now - When;
            if (d.TotalSeconds < 60) return "just now";
            if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes}m ago";
            if (d.TotalHours < 24) return $"{(int)d.TotalHours}h ago";
            if (d.TotalDays < 30) return $"{(int)d.TotalDays}d ago";
            return When.ToString("dd MMM yyyy");
        }
    }

    /// <summary>The snapshot's file paths (relative), for the details list.</summary>
    public IReadOnlyList<string> Files => Manifest.Files.Select(f => f.Path).ToList();

    private static string HumanSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double v = bytes; int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return u == 0 ? $"{bytes} B" : $"{v:0.0} {units[u]}";
    }
}

/// <summary>
/// Dated snapshots of the editable data (server <c>import/</c> YAML + client itemInfo + <c>skillinfoz</c> +
/// <c>datainfo</c>). A snapshot is taken after each manual save and before each restore. Per ADR-0003 each
/// snapshot is a complete, self-contained restore point, but a file byte-identical to the previous snapshot
/// (same per-file SHA-256, recorded in the manifest) is <b>hardlinked</b> instead of re-copied, so unchanged
/// files cost ~0 extra disk. Snapshots live under
/// <c>Documents\Midgard Studio\Backups\&lt;profile&gt;\&lt;timestamp&gt;\</c>.
/// </summary>
public sealed class BackupService
{
    private readonly WorkspaceSession _session;
    private readonly AppSettingsService _settings;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>The highest snapshot <c>Manifest.Version</c> this build can fully restore. CreateBackup writes
    /// version 2; bump this whenever a new snapshot group is added so an OLDER build refuses a forward-
    /// incompatible snapshot instead of silently restoring it only in part (audit #19).</summary>
    private const int KnownMaxSnapshotVersion = 2;

    public BackupService(WorkspaceSession session, AppSettingsService settings)
    {
        _session = session;
        _settings = settings;
        MigrateLegacyBackups();
    }

    public string RootDir { get; } = MidgardStudio.Core.AppPaths.BackupsDir;

    /// <summary>One-time move of pre-1.0 snapshots from %APPDATA%\Midgard Studio\backups to the new Documents
    /// location, so existing backups aren't orphaned. Best-effort: old backups simply stay put on failure.</summary>
    private static void MigrateLegacyBackups()
    {
        try
        {
            string legacy = MidgardStudio.Core.AppPaths.LegacyRoamingBackupsDir;
            string current = MidgardStudio.Core.AppPaths.BackupsDir;
            if (Directory.Exists(legacy) && !Directory.Exists(current))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(current)!);
                Directory.Move(legacy, current);
            }
        }
        catch { /* best effort */ }
    }

    private string ProfileDir => Path.Combine(RootDir, Sanitize(_session.Config.Name));

    private string ImportDir => Path.Combine(_session.Paths.ServerDbRoot, "import");

    // The client files the app's writers touch — snapshotted alongside import/ + itemInfo so a Restore rolls
    // back everything the app can change. Derived from _session.Paths, mirroring ClientSkillService's
    // skillinfoz/* and SpriteLinkService/MobSpriteService's datainfo/* write targets.
    private string SkillInfoDir => Path.Combine(_session.Paths.LuaFilesRoot, "skillinfoz");
    private string SpriteDataDir => Path.Combine(_session.Paths.LuaFilesRoot, "datainfo");
    private static readonly string[] SkillFiles = { "skillid.lub", "skillinfolist.lub", "skilldescript.lub", "skilldelaylist.lub" };
    private static readonly string[] SpriteFiles = { "accessoryid.lub", "accname.lub", "accname_eng.lub", "npcidentity.lub", "jobname.lub" };

    /// <summary>The single client itemInfo file the app writes to (custom file, else the unified base).</summary>
    private string? ItemInfoWriteTarget()
    {
        var p = _session.Paths;
        if (!string.IsNullOrWhiteSpace(p.ItemInfoCustomPath)) return p.ItemInfoCustomPath;
        if (!string.IsNullOrWhiteSpace(p.ItemInfoPath)) return p.ItemInfoPath;
        if (!string.IsNullOrWhiteSpace(p.SystemEnRoot)) return Path.Combine(p.SystemEnRoot, "itemInfo_C.lua");
        return null;
    }

    private static string Sha256Hex(byte[] data) => Convert.ToHexString(SHA256.HashData(data));

    /// <summary>The live editable files to snapshot, as (absolute source, snapshot-relative path).</summary>
    private List<(string Abs, string Rel)> GatherSources()
    {
        var sources = new List<(string, string)>();
        if (Directory.Exists(ImportDir))
            foreach (var f in Directory.GetFiles(ImportDir, "*.yml", SearchOption.AllDirectories))
                sources.Add((f, "import/" + Path.GetRelativePath(ImportDir, f).Replace('\\', '/')));

        var itemInfo = ItemInfoWriteTarget();
        if (itemInfo is not null && File.Exists(itemInfo))
            sources.Add((itemInfo, "SystemEN/" + Path.GetFileName(itemInfo))); // itemInfo_C.lua's default home

        foreach (var name in SkillFiles)
        {
            string p = Path.Combine(SkillInfoDir, name);
            if (File.Exists(p)) sources.Add((p, "skillinfoz/" + name));
        }
        foreach (var name in SpriteFiles)
        {
            string p = Path.Combine(SpriteDataDir, name);
            if (File.Exists(p)) sources.Add((p, "datainfo/" + name));
        }
        return sources;
    }

    /// <summary>Snapshots the current editable data. Unchanged files (same SHA-256 as the previous snapshot)
    /// are hardlinked; changed/new files are copied. Returns null when there is nothing to back up.</summary>
    public BackupEntry? CreateBackup(string label, string note = "")
    {
        if (!Directory.Exists(ImportDir)) return null;

        var sources = GatherSources();
        if (sources.Count == 0) return null;

        // Hash every source file (byte-exact dedup key + integrity checksum + diff signal).
        var current = new List<(string Path, long Bytes, string Sha256)>(sources.Count);
        var sourceByRel = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (abs, rel) in sources)
        {
            byte[] bytes = File.ReadAllBytes(abs);
            current.Add((rel, bytes.LongLength, Sha256Hex(bytes)));
            sourceByRel[rel] = abs;
        }

        // Plan against the most recent existing snapshot (newest first): link unchanged, copy the rest.
        var previous = List().FirstOrDefault();
        var plan = BackupPlanner.Plan(current, previous?.Manifest);

        var now = DateTime.Now;
        string stamp = now.ToString("yyyyMMdd-HHmmss-fff");
        string dest = Path.Combine(ProfileDir, stamp);
        for (int n = 2; Directory.Exists(dest); n++) // never merge two sub-second backups into one folder
            dest = Path.Combine(ProfileDir, $"{stamp}-{n}");
        Directory.CreateDirectory(dest);

        var files = new List<BackupFile>(plan.Count);
        long totalBytes = 0;
        foreach (var p in plan)
        {
            string target = Path.Combine(dest, p.Path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (p.Action == BackupFileAction.Link && previous?.FolderPath is { } prevFolder)
            {
                string blob = Path.Combine(prevFolder, p.Path.Replace('/', Path.DirectorySeparatorChar));
                FileLink.HardLinkOrCopy(target, blob, sourceByRel[p.Path]); // hardlink to prev blob, else copy live
            }
            else
            {
                File.Copy(sourceByRel[p.Path], target, overwrite: true);
            }
            files.Add(new BackupFile(p.Path, p.Bytes, p.Sha256));
            totalBytes += p.Bytes;
        }

        var manifest = new BackupManifest
        {
            Version = 2,
            TimestampUtc = now.ToUniversalTime(),
            Label = label,
            Note = note,
            Pinned = false,
            EncodingCodepage = _session.ClientCodepage,
            Ruleset = _session.Mode.ToString(),
            Files = files,
            TotalBytes = totalBytes,
        };

        if (!WriteManifest(dest, manifest))
        {
            try { Directory.Delete(dest, true); } catch { /* best effort */ }
            throw new IOException("Failed to write the backup manifest, so the snapshot was discarded.");
        }

        Prune();
        return new BackupEntry(manifest, dest);
    }

    /// <summary>Writes manifest.json atomically (tmp + move) so a crash can't leave a folder whose manifest
    /// points at half-written data (List() treats a folder without a manifest as not-a-backup).</summary>
    private static bool WriteManifest(string dest, BackupManifest manifest)
    {
        string manifestPath = Path.Combine(dest, "manifest.json");
        string manifestTmp = manifestPath + ".tmp";
        try
        {
            File.WriteAllText(manifestTmp, JsonSerializer.Serialize(manifest, JsonOptions));
            File.Move(manifestTmp, manifestPath, overwrite: true); // overwrite: SetPinned rewrites an existing manifest
            return true;
        }
        catch
        {
            try { if (File.Exists(manifestTmp)) File.Delete(manifestTmp); } catch { /* best effort */ }
            return false;
        }
    }

    /// <summary>Sets (or clears) the pinned flag on a snapshot, rewriting its manifest. Pinned snapshots are
    /// exempt from retention pruning.</summary>
    public void SetPinned(BackupEntry entry, bool pinned)
    {
        if (entry.Manifest.Pinned == pinned) return;
        entry.Manifest.Pinned = pinned;
        WriteManifest(entry.FolderPath, entry.Manifest);
    }

    // ---- slice 3: restore preview + encoding ----

    /// <summary>Hashes the current live files and diffs them against the snapshot — i.e. what a restore would
    /// change (overwrite / add / remove). Pure result from <c>Core.Backup.BackupDiff</c>.</summary>
    public IReadOnlyList<BackupChange> PreviewRestore(BackupEntry entry)
    {
        var current = new List<(string Path, string Sha256)>();
        foreach (var (abs, rel) in GatherSources())
            current.Add((rel, Sha256Hex(File.ReadAllBytes(abs))));
        return BackupDiff.Compare(entry.Manifest, current);
    }

    /// <summary>True when the snapshot's stamped Display Encoding is known and differs from the current profile's.</summary>
    public bool EncodingDiffers(BackupEntry entry) =>
        entry.Manifest.EncodingCodepage != 0 && entry.Manifest.EncodingCodepage != _session.ClientCodepage;

    public int CurrentCodepage => _session.ClientCodepage;

    public static string EncodingLabel(int codepage) => Common.EncodingCatalog.Label(codepage);

    // ---- slice 5: integrity + what-changed ----

    /// <summary>Re-hashes a snapshot's files and checks them against the manifest. Mismatch/Missing = corruption;
    /// Unknown = a legacy file with no recorded hash.</summary>
    public (int Ok, int Mismatch, int Unknown, int Missing) Verify(BackupEntry entry)
    {
        int ok = 0, mismatch = 0, unknown = 0, missing = 0;
        foreach (var f in entry.Manifest.Files)
        {
            string path = Path.Combine(entry.FolderPath, f.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) { missing++; continue; }
            if (f.Sha256 is null) { unknown++; continue; }
            if (Sha256Hex(File.ReadAllBytes(path)) == f.Sha256) ok++; else mismatch++;
        }
        return (ok, mismatch, unknown, missing);
    }

    /// <summary>Per-file "changed (new/modified) vs carried-over" relative to the chronologically previous
    /// snapshot — drives the details list's badges. A legacy file, or one with no previous, reads as changed.</summary>
    public IReadOnlyDictionary<string, bool> ChangedSincePrevious(BackupEntry entry)
    {
        var all = List(); // newest first
        int idx = -1;
        for (int i = 0; i < all.Count; i++) if (all[i].FolderPath == entry.FolderPath) { idx = i; break; }
        var prev = idx >= 0 && idx + 1 < all.Count ? all[idx + 1] : null; // next-older snapshot

        var prevByPath = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (prev is not null) foreach (var f in prev.Manifest.Files) prevByPath[f.Path] = f.Sha256;

        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in entry.Manifest.Files)
            result[f.Path] = f.Sha256 is null
                || !prevByPath.TryGetValue(f.Path, out var ps) || ps is null || ps != f.Sha256;
        return result;
    }

    // ---- slice 4: export / import ----

    /// <summary>Exports a snapshot as a self-contained .zip (materializes hardlinked blobs into the archive).</summary>
    public void Export(BackupEntry entry, string zipPath)
    {
        if (File.Exists(zipPath)) File.Delete(zipPath);
        System.IO.Compression.ZipFile.CreateFromDirectory(entry.FolderPath, zipPath);
    }

    /// <summary>Imports a .zip as a snapshot in the active profile: verifies each file against the manifest's
    /// SHA-256 (rejecting a corrupt/incomplete archive), then registers it under the profile's Backups dir with
    /// timestamp-collision suffixing. Extraction happens on the same volume as the destination so the final
    /// move is a rename.</summary>
    public BackupEntry Import(string zipPath)
    {
        Directory.CreateDirectory(ProfileDir);
        string temp = Path.Combine(ProfileDir, ".import-" + Guid.NewGuid().ToString("N"));
        try
        {
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, temp);

            string manifestPath = Path.Combine(temp, "manifest.json");
            if (!File.Exists(manifestPath))
                throw new InvalidDataException("That .zip isn't a Midgard backup — it has no manifest.json.");
            var m = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(manifestPath), JsonOptions)
                ?? throw new InvalidDataException("The backup manifest in that .zip is unreadable.");

            foreach (var f in m.Files)
            {
                string p = Path.Combine(temp, f.Path.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(p))
                    throw new InvalidDataException($"The archive is incomplete — missing {f.Path}.");
                if (f.Sha256 is not null && Sha256Hex(File.ReadAllBytes(p)) != f.Sha256)
                    throw new InvalidDataException($"The archive is corrupt — {f.Path} failed its checksum.");
            }

            string stamp = m.TimestampUtc.ToLocalTime().ToString("yyyyMMdd-HHmmss-fff");
            string dest = Path.Combine(ProfileDir, stamp);
            for (int n = 2; Directory.Exists(dest); n++) dest = Path.Combine(ProfileDir, $"{stamp}-{n}");
            Directory.Move(temp, dest);
            return new BackupEntry(m, dest);
        }
        catch
        {
            try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch { /* best effort */ }
            throw;
        }
    }

    /// <summary>Keeps the newest N UNPINNED snapshots (per <see cref="AppSettings.BackupRetention"/>) plus all
    /// pinned ones, deleting the rest. Deleting a folder unlinks its files; shared (hardlinked) blobs persist
    /// while any other snapshot still references them, so pruning never breaks another snapshot.</summary>
    private void Prune()
    {
        int keep = Math.Max(1, _settings.Settings.BackupRetention);
        var refs = List()
            .Select(e => new SnapshotRef(e.FolderPath, e.Manifest.TimestampUtc, e.Manifest.Pinned))
            .ToList();
        foreach (var folder in RetentionPolicy.SelectForPrune(refs, keep))
        {
            try { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
            catch { /* best effort */ }
        }
    }

    /// <summary>All snapshots for the active profile, newest first.</summary>
    public IReadOnlyList<BackupEntry> List()
    {
        if (!Directory.Exists(ProfileDir)) return Array.Empty<BackupEntry>();

        var list = new List<BackupEntry>();
        foreach (var dir in Directory.GetDirectories(ProfileDir))
        {
            string manifest = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifest)) continue;
            try
            {
                var m = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(manifest), JsonOptions);
                if (m is null) continue;
                list.Add(new BackupEntry(m, dir));
            }
            catch { /* skip a corrupt manifest */ }
        }
        return list.OrderByDescending(e => e.Manifest.TimestampUtc).ToList();
    }

    /// <summary>
    /// Replaces the current editable files with the snapshot. A safety backup of the current state is taken
    /// first; the writes go through a <see cref="FileTransaction"/> (atomic per file, rolled back on failure);
    /// an empty/corrupt snapshot is rejected before anything is touched; and if the restore fails partway the
    /// live data is automatically rolled back to the safety backup. Folder-based, so it restores legacy and
    /// dedup snapshots identically (a hardlinked snapshot file reads like any other file).
    /// </summary>
    public void Restore(BackupEntry entry)
    {
        if (!Directory.Exists(entry.FolderPath))
            throw new InvalidDataException("This backup's folder no longer exists on disk, so nothing was restored.");

        // A snapshot from a NEWER build may carry groups this version can't restore; refuse rather than
        // restore it partially while reporting success (audit #19).
        if (entry.Manifest.Version > KnownMaxSnapshotVersion)
            throw new InvalidDataException(
                $"This backup was created by a newer version of Midgard Studio (snapshot v{entry.Manifest.Version}); " +
                "update the app to restore it safely — nothing was changed.");

        string importSrc = Path.Combine(entry.FolderPath, "import");
        // New snapshots store the itemInfo file under "SystemEN/" (its default home); older ones used "client/".
        string clientSrc = Path.Combine(entry.FolderPath, "SystemEN");
        if (!Directory.Exists(clientSrc)) clientSrc = Path.Combine(entry.FolderPath, "client");
        string? writeTarget = ItemInfoWriteTarget();

        var snapImport = Directory.Exists(importSrc)
            ? Directory.GetFiles(importSrc, "*.yml", SearchOption.AllDirectories)
            : Array.Empty<string>();

        string? clientFile = null;
        if (Directory.Exists(clientSrc) && writeTarget is not null)
        {
            string named = Path.Combine(clientSrc, Path.GetFileName(writeTarget));
            if (File.Exists(named)) clientFile = named;
        }

        string[] snapSkill = SnapFiles(entry.FolderPath, "skillinfoz");
        string[] snapSprite = SnapFiles(entry.FolderPath, "datainfo");

        if (snapImport.Length == 0 && clientFile is null && snapSkill.Length == 0 && snapSprite.Length == 0)
            throw new InvalidDataException(
                "This backup is empty or unreadable, so nothing was restored — your current data is untouched.");

        // Read every snapshot file into memory and verify it against the manifest's SHA-256 BEFORE we touch
        // anything. This (a) keeps a corrupt snapshot (bit-rot / hand-edited folder) from being written over the
        // user's live data, and (b) makes the safety backup's Prune() below harmless even if it would otherwise
        // delete THIS snapshot's folder (a near-oldest unpinned snapshot at a low retention cap) — the bytes are
        // already in hand by then.
        var shaByRel = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var mf in entry.Manifest.Files) shaByRel[mf.Path] = mf.Sha256;
        var staged = new List<(string Target, byte[] Bytes)>();
        void Materialize(string snapFile, string target, string rel)
        {
            byte[] bytes = File.ReadAllBytes(snapFile);
            if (shaByRel.TryGetValue(rel, out var sha) && sha is not null && Sha256Hex(bytes) != sha)
                throw new InvalidDataException(
                    $"This backup failed its integrity check — \"{rel}\" doesn't match its recorded checksum, so " +
                    "nothing was restored and your current data is untouched. The backup may be corrupted; try a different restore point.");
            staged.Add((target, bytes));
        }
        foreach (var f in snapImport)
            Materialize(f, Path.Combine(ImportDir, Path.GetRelativePath(importSrc, f)),
                "import/" + Path.GetRelativePath(importSrc, f).Replace('\\', '/'));
        if (clientFile is not null && writeTarget is not null)
            Materialize(clientFile, writeTarget, "SystemEN/" + Path.GetFileName(clientFile));
        foreach (var f in snapSkill)
            Materialize(f, Path.Combine(SkillInfoDir, Path.GetFileName(f)), "skillinfoz/" + Path.GetFileName(f));
        foreach (var f in snapSprite)
            Materialize(f, Path.Combine(SpriteDataDir, Path.GetFileName(f)), "datainfo/" + Path.GetFileName(f));

        // Safety snapshot of the CURRENT state before we change anything (covers files this restore removes).
        var safety = CreateBackup("Auto-backup before restore", $"Taken automatically before restoring \"{entry.Label}\".");

        try
        {
            var tx = new FileTransaction(Path.Combine(ImportDir, ".midgard-backup"));
            foreach (var (target, bytes) in staged) tx.Stage(target, bytes);
            Directory.CreateDirectory(ImportDir);
            tx.Commit();

            // Make import/ match the snapshot by removing live files it doesn't contain (only when the snapshot
            // actually has import files — a client-only snapshot must not wipe server data).
            if (snapImport.Length > 0 && Directory.Exists(ImportDir))
            {
                var keep = new HashSet<string>(
                    snapImport.Select(f => Path.GetFullPath(Path.Combine(ImportDir, Path.GetRelativePath(importSrc, f)))),
                    StringComparer.OrdinalIgnoreCase);
                foreach (var live in Directory.GetFiles(ImportDir, "*.yml", SearchOption.AllDirectories))
                    if (!keep.Contains(Path.GetFullPath(live)))
                        File.Delete(live);
            }
        }
        catch
        {
            if (safety is not null) TryRollback(safety);
            throw;
        }
    }

    /// <summary>Best-effort re-application of a (safety) snapshot over the live files after a failed restore.</summary>
    private void TryRollback(BackupEntry safety)
    {
        try
        {
            string importSrc = Path.Combine(safety.FolderPath, "import");
            if (Directory.Exists(importSrc))
                foreach (var f in Directory.GetFiles(importSrc, "*.yml", SearchOption.AllDirectories))
                {
                    string target = Path.Combine(ImportDir, Path.GetRelativePath(importSrc, f));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    MidgardStudio.Core.IO.FileTransaction.AtomicCopy(f, target); // atomic rollback restore (audit #17)
                }

            // Also remove any live import file the (committed) restore newly ADDED that the safety snapshot
            // doesn't have, so rollback returns import/ to exactly the safety state, not safety + leftovers.
            if (Directory.Exists(importSrc) && Directory.Exists(ImportDir))
            {
                var keep = new HashSet<string>(
                    Directory.GetFiles(importSrc, "*.yml", SearchOption.AllDirectories)
                        .Select(f => Path.GetFullPath(Path.Combine(ImportDir, Path.GetRelativePath(importSrc, f)))),
                    StringComparer.OrdinalIgnoreCase);
                foreach (var live in Directory.GetFiles(ImportDir, "*.yml", SearchOption.AllDirectories))
                    if (!keep.Contains(Path.GetFullPath(live)))
                        try { File.Delete(live); } catch { /* best effort */ }
            }

            string clientSrc = Path.Combine(safety.FolderPath, "SystemEN");
            if (!Directory.Exists(clientSrc)) clientSrc = Path.Combine(safety.FolderPath, "client");
            string? writeTarget = ItemInfoWriteTarget();
            if (Directory.Exists(clientSrc) && writeTarget is not null)
            {
                string named = Path.Combine(clientSrc, Path.GetFileName(writeTarget));
                if (File.Exists(named))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(writeTarget)!);
                    MidgardStudio.Core.IO.FileTransaction.AtomicCopy(named, writeTarget); // atomic rollback restore (audit #17)
                }
            }

            // Roll the client skill + sprite files back too (they're in the safety snapshot).
            void RestoreSet(string sub, string liveDir)
            {
                foreach (var f in SnapFiles(safety.FolderPath, sub))
                {
                    string target = Path.Combine(liveDir, Path.GetFileName(f));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    MidgardStudio.Core.IO.FileTransaction.AtomicCopy(f, target); // atomic rollback restore (audit #17)
                }
            }
            RestoreSet("skillinfoz", SkillInfoDir);
            RestoreSet("datainfo", SpriteDataDir);
        }
        catch { /* best effort; the caller rethrows the original failure */ }
    }

    /// <summary>The files stored in a snapshot's subfolder (skillinfoz / datainfo), or empty.</summary>
    private static string[] SnapFiles(string folder, string sub)
    {
        string dir = Path.Combine(folder, sub);
        return Directory.Exists(dir) ? Directory.GetFiles(dir) : Array.Empty<string>();
    }

    public void Delete(BackupEntry entry)
    {
        try { if (Directory.Exists(entry.FolderPath)) Directory.Delete(entry.FolderPath, true); }
        catch { /* best effort */ }
    }

    private static string Sanitize(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Default";
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }
}
