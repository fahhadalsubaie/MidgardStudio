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
        // Reuse the shared string- AND comment-aware scanner so a brace inside a Lua comment can't
        // mis-locate the table's open/close (the old local scanners ignored comments).
        int open = LuaScan.FindTableOpen(text, tableName);
        if (open < 0) return text;

        int close = LuaScan.FindMatchingBrace(text, open);
        if (close < 0) return text;

        // If the last existing entry has no trailing separator, add one — otherwise the new field abuts the
        // previous value with only whitespace between them, which is a Lua syntax error the real client rejects
        // (e.g. skillid.lub's last entry `AT_NATURE_HARMONY = 6607` has no trailing comma). The app's own reader
        // is separator-tolerant, so this only surfaces in-client. Mirrors ExprKeyTableSplicer's insert logic.
        int p = close - 1;
        while (p > open && char.IsWhiteSpace(text[p])) p--;
        string sep = (p <= open || text[p] == '{' || text[p] == ',' || text[p] == ';') ? string.Empty : ",";

        // Insert the line on its own line just before the closing brace.
        string nl = text.Contains("\r\n") ? "\r\n" : "\n";
        return text[..(p + 1)] + sep + text[(p + 1)..close] + line + nl + text[close..];
    }
}
