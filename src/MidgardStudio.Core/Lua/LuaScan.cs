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
                string ident = s.Substring(start, i - start);
                i--; // the for-loop will advance past the identifier
                if (ident == name)
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
}
