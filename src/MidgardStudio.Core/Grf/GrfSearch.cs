namespace MidgardStudio.Core.Grf;

/// <summary>Pure match predicates for the GRF Browser's file filter and full-content search.</summary>
public static class GrfSearch
{
    /// <summary>Case-insensitive substring match on an entry name; an empty query matches everything (no filter).</summary>
    public static bool NameMatches(string name, string query)
        => string.IsNullOrEmpty(query) || name.Contains(query, StringComparison.OrdinalIgnoreCase);

    /// <summary>Case-insensitive substring match in decoded file text; an empty query matches nothing.</summary>
    public static bool ContentMatches(string decodedText, string query)
        => !string.IsNullOrEmpty(query) && decodedText.Contains(query, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether an extension (with leading dot, any case) is a text type worth grepping.</summary>
    public static bool IsTextExtension(string ext) => TextExtensions.Contains(ext);

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".lua", ".lub", ".txt", ".xml", ".ini", ".inf", ".conf", ".log", ".json", ".csv", ".tsv",
        ".ezv", ".lst", ".js", ".c", ".cpp", ".h", ".bat", ".scp", ".layout", ".font", ".imageset",
        ".integrity", ".yml", ".yaml",
    };
}
