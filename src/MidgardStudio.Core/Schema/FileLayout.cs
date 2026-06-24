using MidgardStudio.Core.Workspace;

namespace MidgardStudio.Core.Schema;

/// <summary>
/// Where a database's data lives on disk, relative to the server db root. Renewal and Pre-Renewal
/// have their own (read-only) base files; the editable import override is a single shared file that
/// applies in both modes. Paths use forward slashes and are normalized per-OS at load time.
/// </summary>
public sealed class FileLayout
{
    public IReadOnlyList<string> RenewalFiles { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> PreRenewalFiles { get; init; } = Array.Empty<string>();

    /// <summary>The db/import/&lt;x&gt;.yml override file (shared across modes).</summary>
    public string ImportFile { get; init; } = string.Empty;

    public IReadOnlyList<string> BaseFiles(ServerMode mode) =>
        mode == ServerMode.Renewal ? RenewalFiles : PreRenewalFiles;

    /// <summary>Common case: one re file, one pre-re file, one import file, all named the same.</summary>
    public static FileLayout Standard(string fileName) => new()
    {
        RenewalFiles = new[] { $"re/{fileName}" },
        PreRenewalFiles = new[] { $"pre-re/{fileName}" },
        ImportFile = $"import/{fileName}",
    };

    /// <summary>For databases that only ship pre-renewal base data (e.g. abra_db).</summary>
    public static FileLayout PreReOnly(string fileName) => new()
    {
        RenewalFiles = Array.Empty<string>(),
        PreRenewalFiles = new[] { $"pre-re/{fileName}" },
        ImportFile = $"import/{fileName}",
    };
}
