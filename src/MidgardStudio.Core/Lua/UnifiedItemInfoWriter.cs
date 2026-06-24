using System.Text;

namespace MidgardStudio.Core.Lua;

/// <summary>
/// Writes item entries into a unified/base itemInfo.lua (the single <c>tbl</c> table) used by servers
/// that have no separate custom file. Edits are spliced in place: each changed entry replaces its
/// existing <c>[id] = { ... }</c> block, new ids are inserted before the table's closing brace, and
/// every other byte of the file (comments, helper functions, untouched entries) is preserved.
/// </summary>
public sealed class UnifiedItemInfoWriter
{
    public string Splice(string original, IEnumerable<ItemInfoEntry> entries, string tableName = "tbl")
    {
        var list = entries.OrderBy(e => e.Id).ToList();
        if (list.Count == 0) return original;

        int tableOpen = LuaScan.FindTableOpen(original, tableName);
        if (tableOpen < 0)
        {
            // No table present — append a fresh one rather than corrupt the file.
            var fresh = new StringBuilder(original.TrimEnd());
            fresh.Append("\n\n").Append(tableName).Append(" = {\n");
            foreach (var e in list) fresh.Append(ItemInfoWriter.FormatEntry(e));
            fresh.Append("}\n");
            return fresh.ToString();
        }

        var (blocks, tableClose) = LuaScan.ScanIntKeyTables(original, tableOpen);
        if (tableClose < 0)
            throw new InvalidDataException(
                $"Couldn't find the end of the '{tableName}' table in the client item file, so your edit was NOT saved and the file was left untouched. " +
                "The file may have a mismatched brace — open it and check, then save again.");

        string text = original;

        // Replace existing entries, highest offset first so earlier spans stay valid.
        foreach (var e in list.Where(e => blocks.ContainsKey(e.Id)).OrderByDescending(e => blocks[e.Id].BracketStart))
        {
            var block = blocks[e.Id];
            int start = block.BracketStart;
            while (start > 0 && (text[start - 1] == '\t' || text[start - 1] == ' ')) start--; // include line indent
            int commaEnd = block.ValueClose + 1;
            if (commaEnd < text.Length && text[commaEnd] == ',') commaEnd++;
            string replacement = ItemInfoWriter.FormatEntry(e).TrimEnd('\n');
            text = text.Substring(0, start) + replacement + text.Substring(commaEnd);
        }

        // Insert new entries just before the table's closing brace.
        var news = list.Where(e => !blocks.ContainsKey(e.Id)).ToList();
        if (news.Count > 0)
        {
            int open2 = LuaScan.FindTableOpen(text, tableName);
            int close2 = LuaScan.FindMatchingBrace(text, open2);
            if (close2 < 0)
                throw new InvalidDataException(
                    $"Couldn't find the closing brace of the '{tableName}' table to insert new entries, so your edit was NOT saved and the file was left untouched. " +
                    "The file may have a mismatched brace — open it and check, then save again.");

            int p = close2 - 1;
            while (p > open2 && char.IsWhiteSpace(text[p])) p--;
            string sep = (text[p] == '{' || text[p] == ',') ? string.Empty : ",";

            var sb = new StringBuilder();
            sb.Append(sep).Append('\n');
            foreach (var e in news) sb.Append(ItemInfoWriter.FormatEntry(e));
            text = text.Substring(0, close2) + sb + text.Substring(close2);
        }

        return text;
    }
}
