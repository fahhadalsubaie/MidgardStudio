using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace MidgardStudio.Core.Serialization;

/// <summary>
/// Merges a freshly-regenerated db document into an existing import file's text, preserving the original's
/// comments (GPL banner, field docs, commented-out example entries) and replacing only the active top-level
/// <c>Body:</c> sequence. Used by <see cref="YamlDbWriter.WriteFile"/> so a save never wipes a hand-documented
/// import file (e.g. <c>import/mob_avail.yml</c>, which ships as a banner + commented examples with no active
/// entries — a plain regenerate would erase all of it).
/// </summary>
public static class YamlBodyMerge
{
    /// <summary>The text to write: the original's comments + Header preserved, with its active top-level Body
    /// replaced by (or, if it had none, the regenerated Body appended after) the canonical document's Body.
    /// Falls back to <paramref name="canonical"/> when the original is empty or isn't a recognizable rAthena db
    /// file (no top-level <c>Header:</c>) — so app-generated files (no banner, real Body) round-trip unchanged.</summary>
    public static string Merge(string? original, string canonical)
    {
        if (string.IsNullOrWhiteSpace(original)) return canonical;

        var lines = Normalize(original).Split('\n');
        if (IndexOfTopLevelKey(lines, "Header") < 0) return canonical; // not a db file we recognize

        // Refuse to write our schema's Body into a file whose header declares a DIFFERENT db Type — that would
        // leave a self-inconsistent file (header says one db, body follows another). Wrong file for this path
        // (e.g. a MOB_DB document sitting at import/item_db.yml). Absent/empty Type is fine (headerless imports).
        // (audit #8 — the save-side half; the profile pre-check warns about it at load time.)
        var canonLines = Normalize(canonical).Split('\n');
        string? origType = HeaderType(lines);
        string? canonType = HeaderType(canonLines);
        if (origType is { Length: > 0 } && canonType is { Length: > 0 } && !string.Equals(origType, canonType, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"This file's header says Type: {origType}, but the editor is saving {canonType} data — it looks like a " +
                "different database. Your change was NOT saved and the file was left untouched.");

        var canonicalBody = ExtractBody(canonical);
        if (canonicalBody is null) return canonical;

        int bodyStart = IndexOfTopLevelKey(lines, "Body");
        if (bodyStart < 0)
        {
            // No active Body in the original (mob_avail ships a commented "#Body:"). Nothing to add → leave the
            // documented file untouched; otherwise append a real Body, keeping the banner + examples verbatim.
            if (IsEmptyBody(canonicalBody)) return original;
            return original.TrimEnd('\n', '\r') + "\n\n" + EnsureTrailingNewline(canonicalBody);
        }

        // Replace the original's Body block (from "Body:" to the next top-level key / EOF) with the new one —
        // but carry over any comment/blank lines that lived INSIDE the old Body, re-attaching each run to the
        // entry it preceded, so hand notes between entries aren't wiped on save (audit #1: status.yml's
        // Kyoshio notes). Comments for entries the save removed are appended after the Body so nothing is lost.
        int bodyEnd = NextTopLevelKey(lines, bodyStart + 1);
        var (buckets, trailing) = CollectBodyComments(lines, bodyStart + 1, bodyEnd);
        var sb = new StringBuilder();
        for (int i = 0; i < bodyStart; i++) sb.Append(lines[i]).Append('\n');
        sb.Append(EnsureTrailingNewline(ReinsertComments(canonicalBody, buckets, trailing)));
        for (int i = bodyEnd; i < lines.Length; i++) sb.Append(lines[i]).Append('\n');
        return sb.ToString();
    }

    private static bool IsCommentOrBlank(string line)
    {
        var t = line.TrimStart();
        return t.Length == 0 || t[0] == '#';
    }

    /// <summary>The <c>Type:</c> value under the top-level <c>Header:</c> mapping, or null if there's no Header
    /// or no Type. Used to detect a wrong-db file before a save writes a header/body-mismatched document.</summary>
    private static string? HeaderType(string[] lines)
    {
        int h = IndexOfTopLevelKey(lines, "Header");
        if (h < 0) return null;
        for (int i = h + 1; i < lines.Length; i++)
        {
            var l = lines[i];
            if (l.Length > 0 && char.IsLetter(l[0])) break; // reached the next top-level key
            var t = l.Trim();
            if (t.StartsWith("Type:", StringComparison.Ordinal))
                return t.Substring(5).Trim().Trim('"', '\'');
        }
        return null;
    }

    /// <summary>The normalized identity of a Body sequence entry's start line (<c>  - Status: Vipstate</c> ->
    /// <c>Status: Vipstate</c>), used to re-attach comments to the same entry in the regenerated Body. Null if
    /// the line doesn't start a sequence item.</summary>
    private static string? EntryKey(string line)
    {
        var t = line.TrimStart();
        if (!t.StartsWith("- ", StringComparison.Ordinal)) return null;
        return Regex.Replace(t.Substring(2).Trim(), @"\s+", " ");
    }

    /// <summary>Buckets the comment/blank lines inside the old Body by the entry that immediately follows each
    /// run; the leftover run (after the last entry) is returned as trailing comments.</summary>
    private static (Dictionary<string, List<string>> Buckets, List<string> Trailing) CollectBodyComments(
        string[] lines, int start, int end)
    {
        var buckets = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var pending = new List<string>();
        for (int i = start; i < end && i < lines.Length; i++)
        {
            var line = lines[i];
            if (IsCommentOrBlank(line)) { pending.Add(line); continue; }
            var key = EntryKey(line);
            if (key is not null && pending.Count > 0 && !buckets.ContainsKey(key))
            {
                buckets[key] = new List<string>(pending);
                pending.Clear();
            }
            else if (key is not null)
            {
                pending.Clear(); // entry with no (new) leading comments — don't carry stale pending across it
            }
            // field ("other") lines: keep pending so a mid-entry comment still lands on the next entry
        }
        return (buckets, pending);
    }

    /// <summary>Re-emits the regenerated Body with each entry's preserved comments inserted before it; any
    /// comments whose entry no longer exists, plus trailing comments, are appended after the Body.</summary>
    private static string ReinsertComments(string canonicalBody, Dictionary<string, List<string>> buckets, List<string> trailing)
    {
        if (buckets.Count == 0 && trailing.Count == 0) return canonicalBody;

        var lines = Normalize(canonicalBody).Split('\n');
        var sb = new StringBuilder();
        var used = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < lines.Length; i++)
        {
            var key = EntryKey(lines[i]);
            if (key is not null && buckets.TryGetValue(key, out var cmts) && used.Add(key))
                foreach (var c in cmts) sb.Append(c).Append('\n');
            sb.Append(lines[i]);
            if (i < lines.Length - 1) sb.Append('\n');
        }

        var tail = new List<string>(trailing);
        foreach (var kv in buckets)
            if (!used.Contains(kv.Key)) tail.AddRange(kv.Value);
        if (tail.Count > 0)
        {
            if (sb.Length > 0 && sb[sb.Length - 1] != '\n') sb.Append('\n');
            foreach (var c in tail) sb.Append(c).Append('\n');
        }
        return sb.ToString().TrimEnd('\n');
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    private static string EnsureTrailingNewline(string s) => s.EndsWith("\n", StringComparison.Ordinal) ? s : s + "\n";

    /// <summary>Index of the first line that is the top-level mapping key <paramref name="key"/> (column 0, not
    /// a comment, immediately followed by ':'), or -1. A commented "#Body:" is not matched.</summary>
    private static int IndexOfTopLevelKey(string[] lines, string key)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            var l = lines[i];
            if (l.Length > key.Length && l[0] != ' ' && l[0] != '\t' && l[0] != '#'
                && l.StartsWith(key, StringComparison.Ordinal) && l[key.Length] == ':')
                return i;
        }
        return -1;
    }

    /// <summary>The next line (from <paramref name="from"/>) that begins a top-level key — a column-0 letter
    /// followed eventually by ':' — or EOF. Body entries (indented) and comments (#) are skipped, so this lands
    /// on a sibling key like <c>Footer:</c> if present, else EOF (Body is normally last).</summary>
    private static int NextTopLevelKey(string[] lines, int from)
    {
        for (int i = from; i < lines.Length; i++)
        {
            var l = lines[i];
            if (l.Length > 0 && char.IsLetter(l[0]) && l.Contains(':')) return i;
        }
        return lines.Length;
    }

    private static bool IsEmptyBody(string body)
    {
        var t = body.Trim();
        return t is "Body:" or "Body: []" or "Body: {}";
    }

    /// <summary>The Body region of a canonical Header+Body document — from its top-level <c>Body:</c> line to
    /// the end.</summary>
    private static string? ExtractBody(string canonical)
    {
        var lines = Normalize(canonical).Split('\n');
        int idx = IndexOfTopLevelKey(lines, "Body");
        if (idx < 0) return null;
        var sb = new StringBuilder();
        for (int i = idx; i < lines.Length; i++)
        {
            sb.Append(lines[i]);
            if (i < lines.Length - 1) sb.Append('\n');
        }
        return sb.ToString().TrimEnd('\n');
    }
}
