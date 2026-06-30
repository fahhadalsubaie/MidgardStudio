using System.Text;
using System.Text.RegularExpressions;

namespace MidgardStudio.Core.Lookup;

public sealed record ScriptCommandEntry(string Token, string Hint);

/// <summary>
/// Best-effort catalog of rAthena bonuses and script commands, parsed from docs/item_bonus.txt and
/// docs/script_commands.txt. Feeds the script editor's reference side-panel.
/// </summary>
public sealed class ScriptCommandCatalog
{
    private static readonly Regex BonusRegex = new(@"\bbonus[2-5]?\s+(b[A-Za-z]\w+)", RegexOptions.Compiled);
    private static readonly Regex CommandRegex = new(@"^\*([A-Za-z_]\w+)", RegexOptions.Compiled);

    public IReadOnlyList<ScriptCommandEntry> Entries { get; private set; } = Array.Empty<ScriptCommandEntry>();

    public static ScriptCommandCatalog LoadFromDocs(string docsDirectory)
    {
        var catalog = new ScriptCommandCatalog();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<ScriptCommandEntry>();

        TryParse(Path.Combine(docsDirectory, "item_bonus.txt"), BonusRegex, "bonus", seen, entries);
        TryParse(Path.Combine(docsDirectory, "script_commands.txt"), CommandRegex, "command", seen, entries);

        catalog.Entries = entries;
        return catalog;
    }

    private static void TryParse(string path, Regex regex, string kind, HashSet<string> seen, List<ScriptCommandEntry> entries)
    {
        if (!File.Exists(path)) return;
        try
        {
            foreach (var rawLine in File.ReadLines(path, Encoding.UTF8))
            {
                var match = regex.Match(rawLine);
                if (!match.Success) continue;
                var token = match.Groups[1].Value;
                if (!seen.Add(token)) continue;
                string hint = rawLine.Trim();
                if (hint.Length > 90) hint = hint[..90] + "…";
                entries.Add(new ScriptCommandEntry(token, $"[{kind}] {hint}"));
            }
        }
        catch
        {
            // docs are advisory; ignore parse failures
        }
    }

    public IReadOnlyList<ScriptCommandEntry> Search(string query, int limit = 60)
    {
        if (string.IsNullOrWhiteSpace(query)) return Entries.Take(limit).ToList();
        return Entries
            .Where(e => e.Token.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(limit)
            .ToList();
    }
}
