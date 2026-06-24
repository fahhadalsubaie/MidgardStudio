using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MidgardStudio.Core.IO;

namespace MidgardStudio.App.Services;

/// <summary>One on-disk backup snapshot (a timestamped folder + manifest).</summary>
public sealed class BackupEntry
{
    public DateTime TimestampUtc { get; set; }
    public string Label { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public List<string> Files { get; set; } = new();
    public long TotalBytes { get; set; }

    /// <summary>Absolute path to the snapshot folder (set when listing; not persisted).</summary>
    [JsonIgnore] public string FolderPath { get; set; } = string.Empty;

    [JsonIgnore] public DateTime When => TimestampUtc.ToLocalTime();
    [JsonIgnore] public string WhenText => When.ToString("yyyy-MM-dd  HH:mm:ss");
    [JsonIgnore] public string DayText => When.ToString("dddd, dd MMM yyyy");
    [JsonIgnore] public int FileCount => Files.Count;
    [JsonIgnore] public string SizeText => HumanSize(TotalBytes);
    [JsonIgnore] public string SummaryText => $"{WhenText}  ·  {FileCount} files  ·  {SizeText}";

    private static string HumanSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double v = bytes; int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return u == 0 ? $"{bytes} B" : $"{v:0.0} {units[u]}";
    }
}

/// <summary>
/// Dated snapshots of the editable data (the server <c>import/</c> YAML + the client itemInfo file).
/// A backup is taken automatically after every save with a self-describing label, and can be made
/// manually. Restores replace the current files with the snapshot, taking a safety backup first.
/// Snapshots live under <c>%APPDATA%\Midgard Studio\backups\&lt;profile&gt;\&lt;timestamp&gt;\</c>.
/// </summary>
public sealed class BackupService
{
    private readonly WorkspaceSession _session;
    private readonly AppSettingsService _settings;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public BackupService(WorkspaceSession session, AppSettingsService settings)
    {
        _session = session;
        _settings = settings;
    }

    public string RootDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Midgard Studio", "backups");

    private string ProfileDir => Path.Combine(RootDir, Sanitize(_session.Config.Name));

    private string ImportDir => Path.Combine(_session.Paths.ServerDbRoot, "import");

    /// <summary>The single client itemInfo file the app writes to (custom file, else the unified base).</summary>
    private string? ItemInfoWriteTarget()
    {
        var p = _session.Paths;
        if (!string.IsNullOrWhiteSpace(p.ItemInfoCustomPath)) return p.ItemInfoCustomPath;
        if (!string.IsNullOrWhiteSpace(p.ItemInfoPath)) return p.ItemInfoPath;
        if (!string.IsNullOrWhiteSpace(p.SystemEnRoot)) return Path.Combine(p.SystemEnRoot, "itemInfo_C.lua");
        return null;
    }

    /// <summary>Snapshots the current editable data. Returns null when there is nothing to back up.</summary>
    public BackupEntry? CreateBackup(string label, string note = "")
    {
        if (!Directory.Exists(ImportDir)) return null;

        var now = DateTime.Now;
        string stamp = now.ToString("yyyyMMdd-HHmmss-fff");
        string dest = Path.Combine(ProfileDir, stamp);
        for (int n = 2; Directory.Exists(dest); n++) // never merge two sub-second backups into one folder
            dest = Path.Combine(ProfileDir, $"{stamp}-{n}");
        Directory.CreateDirectory(dest);

        var files = new List<string>();
        long bytes = 0;

        foreach (var f in Directory.GetFiles(ImportDir, "*.yml", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(ImportDir, f);
            string target = Path.Combine(dest, "import", rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(f, target, true);
            files.Add("import/" + rel.Replace('\\', '/'));
            bytes += new FileInfo(f).Length;
        }

        var itemInfo = ItemInfoWriteTarget();
        if (itemInfo is not null && File.Exists(itemInfo))
        {
            string name = Path.GetFileName(itemInfo);
            string target = Path.Combine(dest, "client", name);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(itemInfo, target, true);
            files.Add("client/" + name);
            bytes += new FileInfo(itemInfo).Length;
        }

        if (files.Count == 0)
        {
            try { Directory.Delete(dest, true); } catch { /* best effort */ }
            return null;
        }

        var entry = new BackupEntry
        {
            TimestampUtc = now.ToUniversalTime(),
            Label = label,
            Note = note,
            Files = files,
            TotalBytes = bytes,
            FolderPath = dest,
        };
        // Write the manifest atomically and last, so a crash can't leave a manifest that points at
        // half-copied data (List() treats a folder without a manifest as not-a-backup).
        string manifestPath = Path.Combine(dest, "manifest.json");
        string manifestTmp = manifestPath + ".tmp";
        File.WriteAllText(manifestTmp, JsonSerializer.Serialize(entry, JsonOptions));
        File.Move(manifestTmp, manifestPath);

        Prune();
        return entry;
    }

    /// <summary>Keeps the newest N snapshots for the active profile (per <see cref="AppSettings.BackupRetention"/>),
    /// deleting the oldest beyond the cap so the backups folder can't grow without bound.</summary>
    private void Prune()
    {
        int keep = Math.Max(1, _settings.Settings.BackupRetention);
        foreach (var old in List().Skip(keep))
        {
            try { if (Directory.Exists(old.FolderPath)) Directory.Delete(old.FolderPath, true); }
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
                var entry = JsonSerializer.Deserialize<BackupEntry>(File.ReadAllText(manifest));
                if (entry is null) continue;
                entry.FolderPath = dir;
                list.Add(entry);
            }
            catch { /* skip a corrupt manifest */ }
        }
        return list.OrderByDescending(e => e.TimestampUtc).ToList();
    }

    /// <summary>
    /// Replaces the current editable files with the snapshot. A safety backup of the current state is
    /// taken first; the writes go through a <see cref="FileTransaction"/> (atomic per file, rolled back
    /// on failure); an empty/corrupt snapshot is rejected before anything is touched; and if the restore
    /// fails partway the live data is automatically rolled back to the safety backup.
    /// </summary>
    public void Restore(BackupEntry entry)
    {
        if (!Directory.Exists(entry.FolderPath))
            throw new InvalidDataException("This backup's folder no longer exists on disk, so nothing was restored.");

        string importSrc = Path.Combine(entry.FolderPath, "import");
        string clientSrc = Path.Combine(entry.FolderPath, "client");
        string? writeTarget = ItemInfoWriteTarget();

        var snapImport = Directory.Exists(importSrc)
            ? Directory.GetFiles(importSrc, "*.yml", SearchOption.AllDirectories)
            : Array.Empty<string>();

        // Only restore the client file when the snapshot holds the exact expected filename (no copying an
        // arbitrary first file over the live target).
        string? clientFile = null;
        if (Directory.Exists(clientSrc) && writeTarget is not null)
        {
            string named = Path.Combine(clientSrc, Path.GetFileName(writeTarget));
            if (File.Exists(named)) clientFile = named;
        }

        if (snapImport.Length == 0 && clientFile is null)
            throw new InvalidDataException(
                "This backup is empty or unreadable, so nothing was restored — your current data is untouched.");

        // Safety snapshot of the CURRENT state before we change anything (covers files this restore removes).
        var safety = CreateBackup("Auto-backup before restore", $"Taken automatically before restoring \"{entry.Label}\".");

        try
        {
            // Overwrite each snapshot file atomically (per-file backup + temp + swap + rollback).
            var tx = new FileTransaction(Path.Combine(ImportDir, ".midgard-backup"));
            foreach (var f in snapImport)
                tx.Stage(Path.Combine(ImportDir, Path.GetRelativePath(importSrc, f)), File.ReadAllBytes(f));
            if (clientFile is not null && writeTarget is not null)
                tx.Stage(writeTarget, File.ReadAllBytes(clientFile));
            Directory.CreateDirectory(ImportDir);
            tx.Commit();

            // Make import/ match the snapshot by removing live files it doesn't contain (only when the
            // snapshot actually has import files — a client-only snapshot must not wipe server data).
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
            // Roll the live data back to the safety snapshot we just took, then surface the original error.
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
                    File.Copy(f, target, true);
                }

            string clientSrc = Path.Combine(safety.FolderPath, "client");
            string? writeTarget = ItemInfoWriteTarget();
            if (Directory.Exists(clientSrc) && writeTarget is not null)
            {
                string named = Path.Combine(clientSrc, Path.GetFileName(writeTarget));
                if (File.Exists(named))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(writeTarget)!);
                    File.Copy(named, writeTarget, true);
                }
            }
        }
        catch { /* best effort; the caller rethrows the original failure */ }
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
