using System.Collections.Generic;

namespace MidgardStudio.Core.Lua;

/// <summary>
/// String/comment-aware scanning helpers over raw Lua text (brace matching, locating a top-level
/// <c>name = { ... }</c> table, and indexing its <c>[int] = { ... }</c> members). Shared by the
/// official-itemInfo reader (lazy per-id parse of a 7 MB file) and the unified writer (in-place splice).
/// </summary>
public static class LuaScan
{
    /// <summary>One integer-keyed table member: positions of the <c>[</c>, the value's <c>{</c>, and its <c>}</c>.</summary>
    public readonly record struct IntKeyBlock(int BracketStart, int ValueOpen, int ValueClose);

    /// <summary>One expression-keyed table member (<c>[SKID.X] = { ... }</c>): positions of the <c>[</c>,
    /// the value's <c>{</c>, and its <c>}</c>.</summary>
    public readonly record struct ExprKeyBlock(int BracketStart, int ValueOpen, int ValueClose);

    /// <summary>Index of the value-table's opening brace for <c>name = {</c>, or -1.</summary>
    public static int FindTableOpen(string s, string name)
    {
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '"' || c == '\'') { i = SkipString(s, i); continue; }
            if (c == '-' && i + 1 < s.Length && s[i + 1] == '-') { i = SkipComment(s, i) - 1; continue; }

            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
                int len = i - start;
                i--; // the for-loop will advance past the identifier
                // Compare the identifier region in place (no substring) — mirrors LuaTableParser.MatchIdentifierAt.
                if (len == name.Length && string.CompareOrdinal(s, start, name, 0, len) == 0)
                {
                    int k = i + 1;
                    while (k < s.Length && char.IsWhiteSpace(s[k])) k++;
                    if (k < s.Length && s[k] == '=')
                    {
                        k++;
                        while (k < s.Length && char.IsWhiteSpace(s[k])) k++;
                        if (k < s.Length && s[k] == '{') return k;
                    }
                }
            }
        }
        return -1;
    }

    /// <summary>Index of the '}' matching the '{' at <paramref name="open"/>, or -1.</summary>
    public static int FindMatchingBrace(string s, int open)
    {
        int depth = 0;
        for (int i = open; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '"' || c == '\'') { i = SkipString(s, i); continue; }
            if (c == '-' && i + 1 < s.Length && s[i + 1] == '-') { i = SkipComment(s, i) - 1; continue; }
            if (c == '{') depth++;
            else if (c == '}') { depth--; if (depth == 0) return i; }
        }
        return -1;
    }

    /// <summary>
    /// The field separator to insert before a new entry appended just inside a table, given the table's
    /// open and close brace indices. Returns "" if the table is empty or its last entry already ends in a
    /// separator (<c>,</c> or <c>;</c>), else <c>","</c>. Without it a new field abuts the previous value
    /// with only whitespace — a Lua syntax error the strict client rejects (e.g. skillid.lub's last entry
    /// <c>AT_NATURE_HARMONY = 6607</c> has no trailing comma). Shared by every table-append path so the
    /// rule lives, and is tested, in one place.
    /// </summary>
    public static string SeparatorBeforeNewEntry(string s, int open, int close)
    {
        int end = LastValueEnd(s, open, close);
        return (end <= open || s[end - 1] is '{' or ',' or ';') ? string.Empty : ",";
    }

    /// <summary>The index just past the last real value character inside the table (open..close), skipping
    /// trailing whitespace AND trailing comments. A separator/new entry goes here so it lands after the last
    /// value, not after a trailing <c>--</c> comment line (which used to get a stray comma appended — audit #16).
    /// Forward scan reusing <see cref="SkipString"/> / <see cref="SkipComment"/> / <see cref="FindMatchingBrace"/>.</summary>
    public static int LastValueEnd(string s, int open, int close)
    {
        int last = open; // empty table -> right after '{'
        int i = open + 1;
        while (i < close)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '-' && i + 1 < close && s[i + 1] == '-') { i = SkipComment(s, i); continue; }
            if (c == '"' || c == '\'') { i = SkipString(s, i); last = i + 1; i++; continue; }
            if (c == '{')
            {
                int m = FindMatchingBrace(s, i);
                if (m < 0 || m >= close) { last = close; break; }
                last = m + 1; i = m + 1; continue;
            }
            last = i + 1; i++;
        }
        return last;
    }

    /// <summary>Given the index of a quote char, returns the index of the closing quote.</summary>
    public static int SkipString(string s, int i)
    {
        char q = s[i];
        i++;
        while (i < s.Length)
        {
            if (s[i] == '\\') { i += 2; continue; }
            if (s[i] == q) return i;
            i++;
        }
        return s.Length - 1;
    }

    /// <summary>Given the index of the first '-' of a comment, returns the index just past the comment.</summary>
    public static int SkipComment(string s, int i)
    {
        i += 2; // past --
        if (i + 1 < s.Length && s[i] == '[' && s[i + 1] == '[')
        {
            int e = s.IndexOf("]]", i + 2, System.StringComparison.Ordinal);
            return e < 0 ? s.Length : e + 2;
        }
        while (i < s.Length && s[i] != '\n') i++;
        return i;
    }

    /// <summary>Indexes the top-level <c>[int] = { ... }</c> members of the table opened at
    /// <paramref name="tableOpen"/>. Single O(n) pass — no value parsing.</summary>
    public static (Dictionary<int, IntKeyBlock> Blocks, int TableClose) ScanIntKeyTables(string s, int tableOpen)
    {
        var blocks = new Dictionary<int, IntKeyBlock>();
        int end = FindMatchingBrace(s, tableOpen);
        if (end < 0) return (blocks, -1);

        int i = tableOpen + 1;
        while (i < end)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c) || c == ',' || c == ';') { i++; continue; }
            if (c == '-' && i + 1 < s.Length && s[i + 1] == '-') { i = SkipComment(s, i); continue; }

            if (c == '[')
            {
                int bracketStart = i;
                int j = i + 1;
                int keyStart = j;
                while (j < end && s[j] != ']') j++;
                string key = s.Substring(keyStart, j - keyStart).Trim();
                j++; // past ']'
                while (j < end && char.IsWhiteSpace(s[j])) j++;
                if (j < end && s[j] == '=') j++;
                while (j < end && char.IsWhiteSpace(s[j])) j++;

                if (j < end && s[j] == '{')
                {
                    int blockClose = FindMatchingBrace(s, j);
                    if (blockClose < 0) break;
                    if (int.TryParse(key, out int id)) blocks[id] = new IntKeyBlock(bracketStart, j, blockClose);
                    i = blockClose + 1;
                    continue;
                }

                i = j; // non-table value; keep scanning
                continue;
            }

            i++;
        }

        return (blocks, end);
    }

    /// <summary>Indexes the top-level <c>[expr] = { ... }</c> members of the table opened at
    /// <paramref name="tableOpen"/>, keyed by the trimmed raw bracket expression (e.g. <c>SKID.SM_BASH</c>).
    /// Only expression (non-integer) keys are recorded. Single O(n) pass — no value parsing.</summary>
    public static (Dictionary<string, ExprKeyBlock> Blocks, int TableClose) ScanExprKeyTables(string s, int tableOpen)
    {
        var blocks = new Dictionary<string, ExprKeyBlock>(StringComparer.Ordinal);
        int end = FindMatchingBrace(s, tableOpen);
        if (end < 0) return (blocks, -1);

        int i = tableOpen + 1;
        while (i < end)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c) || c == ',' || c == ';') { i++; continue; }
            if (c == '-' && i + 1 < s.Length && s[i + 1] == '-') { i = SkipComment(s, i); continue; }
            if (c == '"' || c == '\'') { i = SkipString(s, i) + 1; continue; }

            if (c == '[')
            {
                int bracketStart = i;
                int j = i + 1;
                int keyStart = j;
                while (j < end && s[j] != ']') j++;
                string key = s.Substring(keyStart, j - keyStart).Trim();
                j++; // past ']'
                while (j < end && char.IsWhiteSpace(s[j])) j++;
                if (j < end && s[j] == '=') j++;
                while (j < end && char.IsWhiteSpace(s[j])) j++;

                if (j < end && s[j] == '{')
                {
                    int blockClose = FindMatchingBrace(s, j);
                    if (blockClose < 0) break;
                    // Record expression keys only; an [int] key here is left to ScanIntKeyTables.
                    if (key.Length > 0 && !int.TryParse(key, out _)) blocks[key] = new ExprKeyBlock(bracketStart, j, blockClose);
                    i = blockClose + 1;
                    continue;
                }

                i = j; // non-table value; keep scanning
                continue;
            }

            i++;
        }

        return (blocks, end);
    }
}
