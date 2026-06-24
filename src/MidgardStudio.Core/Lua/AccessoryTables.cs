namespace MidgardStudio.Core.Lua;

/// <summary>
/// Reads and appends to the headgear sprite tables: accessoryid.lub (ACCESSORY_IDs constants) and
/// accname.lub (AccNameTable mapping constant -&gt; sprite file). Robe tables share the same shape.
/// </summary>
public static class AccessoryTables
{
    /// <summary>Parses ACCESSORY_IDs: constant name -> numeric id.</summary>
    public static Dictionary<string, int> ReadConstants(string accessoryIdText, string tableName = "ACCESSORY_IDs")
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        var table = new LuaTableParser(accessoryIdText).ParseNamedTable(tableName);
        if (table is null) return result;
        foreach (var (name, value) in table.NameKeys)
            if (value is double d) result[name] = (int)d;
        return result;
    }

    /// <summary>Parses AccNameTable: constant name (from [ACCESSORY_IDs.X]) -> sprite file name.</summary>
    public static Dictionary<string, string> ReadNames(string accNameText, string tableName = "AccNameTable")
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var table = new LuaTableParser(accNameText).ParseNamedTable(tableName);
        if (table is null) return result;
        foreach (var (key, value) in table.ExprKeys)
        {
            int dot = key.LastIndexOf('.');
            string name = dot >= 0 ? key[(dot + 1)..] : key;
            if (value is string s) result[name] = s;
        }
        return result;
    }

    public static int NextFreeId(IReadOnlyDictionary<string, int> constants) =>
        constants.Count == 0 ? 1 : constants.Values.Max() + 1;

    /// <summary>Appends a constant <c>NAME = id,</c> inside the named table.</summary>
    public static string AppendConstant(string text, string tableName, string constantName, int id) =>
        InsertBeforeTableClose(text, tableName, $"\t{constantName} = {id},");

    /// <summary>Appends a <c>[ACCESSORY_IDs.NAME] = "sprite",</c> mapping inside the named table.</summary>
    public static string AppendName(string text, string tableName, string idsTableName, string constantName, string sprite) =>
        InsertBeforeTableClose(text, tableName, $"\t[{idsTableName}.{constantName}] = \"{sprite}\",");

    private static string InsertBeforeTableClose(string text, string tableName, string line)
    {
        int open = FindTableOpenBrace(text, tableName);
        if (open < 0) return text;

        int close = MatchBrace(text, open);
        if (close < 0) return text;

        // Insert the line on its own line just before the closing brace.
        string nl = text.Contains("\r\n") ? "\r\n" : "\n";
        return text[..close] + line + nl + text[close..];
    }

    private static int FindTableOpenBrace(string text, string tableName)
    {
        int i = 0;
        while (i < text.Length)
        {
            int idx = text.IndexOf(tableName, i, StringComparison.Ordinal);
            if (idx < 0) return -1;
            // ensure standalone token
            bool leftOk = idx == 0 || !(char.IsLetterOrDigit(text[idx - 1]) || text[idx - 1] == '_' || text[idx - 1] == '.');
            int after = idx + tableName.Length;
            int j = after;
            while (j < text.Length && char.IsWhiteSpace(text[j])) j++;
            if (leftOk && j < text.Length && text[j] == '=')
            {
                int k = j + 1;
                while (k < text.Length && char.IsWhiteSpace(text[k])) k++;
                if (k < text.Length && text[k] == '{') return k;
            }
            i = idx + tableName.Length;
        }
        return -1;
    }

    private static int MatchBrace(string text, int openIndex)
    {
        int depth = 0;
        for (int i = openIndex; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '{') depth++;
            else if (c == '}') { depth--; if (depth == 0) return i; }
            else if (c == '"' || c == '\'')
            {
                char q = c;
                i++;
                while (i < text.Length && text[i] != q) { if (text[i] == '\\') i++; i++; }
            }
        }
        return -1;
    }
}
