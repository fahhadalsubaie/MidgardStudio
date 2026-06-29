using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;

namespace MidgardStudio.Core.IO;

/// <summary>
/// Stages writes to several files and commits them all-or-nothing: each existing target is backed up,
/// written to a temp file, then atomically swapped in. If any write fails, files that were already
/// written are rolled back — overwritten files are restored from their backup and newly-created files
/// are removed — and the original error is rethrown. Leftover temp files are cleaned up on failure.
/// </summary>
public sealed class FileTransaction
{
    private readonly List<(string Path, byte[] Bytes)> _writes = new();
    private readonly string _backupDir;

    public FileTransaction(string backupDir) => _backupDir = backupDir;

    public IReadOnlyList<string> StagedPaths => _writes.Select(w => w.Path).ToList();

    public void Stage(string path, byte[] bytes) => _writes.Add((path, bytes));

    public void Commit()
    {
        Directory.CreateDirectory(_backupDir);
        var written = new List<(string Path, bool WasNew)>();

        try
        {
            foreach (var (path, bytes) in _writes)
            {
                bool existed = File.Exists(path);
                if (existed)
                    File.Copy(path, BackupPathFor(path), overwrite: true);

                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var tmp = path + ".tmp";
                try
                {
                    File.WriteAllBytes(tmp, bytes);
                    if (existed) File.Replace(tmp, path, null);
                    else File.Move(tmp, path);
                }
                catch
                {
                    TryDelete(tmp); // don't leave a half-written .tmp next to the real file
                    throw;
                }

                written.Add((path, !existed));
            }
        }
        catch (Exception ex)
        {
            // Roll back everything already written; keep going even if one restore fails.
            foreach (var (path, wasNew) in written)
            {
                try
                {
                    if (wasNew) TryDelete(path);
                    else
                    {
                        var backup = BackupPathFor(path);
                        if (File.Exists(backup)) AtomicCopy(backup, path); // atomic so a crash mid-rollback can't truncate (audit #17)
                    }
                }
                catch { /* best effort — restore the rest */ }
            }

            ExceptionDispatchInfo.Capture(ex).Throw(); // surface the real cause, not a rollback error
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }

    /// <summary>Copies <paramref name="src"/> over <paramref name="dest"/> atomically (temp + Replace/Move) so a
    /// crash mid-copy can't leave <paramref name="dest"/> truncated. Shared by the rollback path and
    /// <c>BackupService.TryRollback</c> (audit #17).</summary>
    public static void AtomicCopy(string src, string dest)
    {
        var tmp = dest + ".roll.tmp";
        try
        {
            File.Copy(src, tmp, overwrite: true);
            if (File.Exists(dest)) File.Replace(tmp, dest, null);
            else File.Move(tmp, dest);
        }
        finally { TryDelete(tmp); }
    }

    // Backup name is unique per full path, so two staged files that share a leaf name (different folders)
    // can never collide in the shared backup dir and cause a rollback to restore from the wrong backup.
    private string BackupPathFor(string path)
    {
        var full = Path.GetFullPath(path);
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(full)))[..8];
        return Path.Combine(_backupDir, Path.GetFileName(path) + "." + hash + ".bak");
    }
}
