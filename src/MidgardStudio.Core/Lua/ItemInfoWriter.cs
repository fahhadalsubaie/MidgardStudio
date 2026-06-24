using System.Text;

namespace MidgardStudio.Core.Lua;

/// <summary>Regenerates itemInfo_C.lua: the template comment plus the tbl_custom and tbl_override
/// tables, formatted like the official client files (tabs, field order, color codes preserved).</summary>
public sealed class ItemInfoWriter
{
    private const string Template =
        "--[[ Template\n" +
        "\t[ID] = {\n" +
        "\t\tunidentifiedDisplayName = \"Unknown Item\",\n" +
        "\t\tunidentifiedResourceName = \"\",\n" +
        "\t\tunidentifiedDescriptionName = { \"\" },\n" +
        "\t\tidentifiedDisplayName = \"Item\",\n" +
        "\t\tidentifiedResourceName = \"\",\n" +
        "\t\tidentifiedDescriptionName = {\n" +
        "\t\t\t\"Line 1\",\n" +
        "\t\t\t\"Line 2\"\n" +
        "\t\t},\n" +
        "\t\tslotCount = 0,\n" +
        "\t\tClassNum = 0,\n" +
        "\t\tcostume = false\n" +
        "\t},\n" +
        "]]\n";

    public string Write(ItemInfoFile file)
    {
        var sb = new StringBuilder();
        sb.Append(Template);
        sb.Append('\n');

        sb.Append("-- Table for Custom Items\n");
        sb.Append("-- NOTE: merged WITHOUT overwriting. If the id already exists in the official\n");
        sb.Append("-- itemInfo.lua, the custom entry is ignored; use tbl_override to replace it.\n");
        sb.Append("tbl_custom = {\n");
        foreach (var entry in file.Custom.Values.OrderBy(e => e.Id))
            WriteEntry(sb, entry);
        sb.Append("}\n\n");

        sb.Append("-- Table for Official Overrides\n");
        sb.Append("-- Entries here overwrite existing item ids and stay flagged as custom.\n");
        sb.Append("tbl_override = {\n");
        foreach (var entry in file.Override.Values.OrderBy(e => e.Id))
            WriteEntry(sb, entry);
        sb.Append("}\n");

        return sb.ToString();
    }

    private static void WriteEntry(StringBuilder sb, ItemInfoEntry e) => sb.Append(FormatEntry(e));

    /// <summary>Formats a single entry as a <c>\t[id] = { ... },\n</c> block (shared with the unified writer).</summary>
    public static string FormatEntry(ItemInfoEntry e)
    {
        var sb = new StringBuilder();
        sb.Append($"\t[{e.Id}] = {{\n");
        sb.Append($"\t\tunidentifiedDisplayName = {Quote(e.UnidentifiedDisplayName)},\n");
        sb.Append($"\t\tunidentifiedResourceName = {Quote(e.UnidentifiedResourceName)},\n");
        sb.Append($"\t\tunidentifiedDescriptionName = {Array(e.UnidentifiedDescription)},\n");
        sb.Append($"\t\tidentifiedDisplayName = {Quote(e.IdentifiedDisplayName)},\n");
        sb.Append($"\t\tidentifiedResourceName = {Quote(e.IdentifiedResourceName)},\n");
        sb.Append($"\t\tidentifiedDescriptionName = {Array(e.IdentifiedDescription)},\n");
        sb.Append($"\t\tslotCount = {e.SlotCount},\n");
        sb.Append($"\t\tClassNum = {e.ClassNum},\n");
        sb.Append($"\t\tcostume = {(e.Costume ? "true" : "false")}");
        if (e.EffectId.HasValue) sb.Append($",\n\t\tEffectID = {e.EffectId.Value}");
        if (e.PackageId.HasValue) sb.Append($",\n\t\tPackageID = {e.PackageId.Value}");
        if (e.Server is not null) sb.Append($",\n\t\tServer = {Quote(e.Server)}");
        sb.Append("\n\t},\n");
        return sb.ToString();
    }

    private static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static string Array(List<string> lines)
    {
        if (lines.Count == 0) return "{ \"\" }";
        if (lines.Count == 1) return "{ " + Quote(lines[0]) + " }";

        var sb = new StringBuilder("{\n");
        for (int i = 0; i < lines.Count; i++)
        {
            sb.Append("\t\t\t").Append(Quote(lines[i]));
            sb.Append(i < lines.Count - 1 ? ",\n" : "\n");
        }
        sb.Append("\t\t}");
        return sb.ToString();
    }
}
