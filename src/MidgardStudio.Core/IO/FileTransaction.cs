using System.Runtime.ExceptionServices;

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
                        if (File.Exists(backup)) File.Copy(backup, path, overwrite: true);
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

    private string BackupPathFor(string path) =>
        Path.Combine(_backupDir, Path.GetFileName(path) + ".bak");
}
