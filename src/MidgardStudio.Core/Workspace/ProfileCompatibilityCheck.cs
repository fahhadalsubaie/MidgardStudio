using System.Text;
using System.Text.RegularExpressions;
using MidgardStudio.Core.Lua;
using MidgardStudio.Core.Schema;

namespace MidgardStudio.Core.Workspace;

public enum CompatSeverity { Info, Warning, Blocker }

/// <summary>One compatibility finding about a profile's data file.</summary>
public sealed record CompatFinding(string File, CompatSeverity Severity, string Message);

/// <summary>
/// A read-only, headless pre-flight run BEFORE a profile is loaded, to catch files in a format this build
/// can't safely round-trip — so the user is warned instead of discovering it as a confusing error (or a
/// silent mis-route) at save time. It never writes and is wrapped so it can't crash the load. Signals:
/// server import <c>Header.Type</c> mismatch (wrong db for the path — would write a header/body-mismatched
/// file), server <c>Header.Version</c> drift (older/newer rAthena), a client file whose expected table is
/// missing/renamed (the editor would fail or mis-route on save), and an itemInfo_C with extra <c>tbl_*</c>
/// tables (edits route to tbl_custom). See wiki/gotchas/write-safety.md.
/// </summary>
public static class ProfileCompatibilityCheck
{
    public static IReadOnlyList<CompatFinding> Run(WorkspacePaths paths, IReadOnlyList<DbSchema> schemas, ServerMode mode, int codepage)
    {
        var findings = new List<CompatFinding>();
        try
        {
            CheckServerYaml(paths, schemas, mode, findings);
            CheckClientFiles(paths, codepage <= 0 ? 1252 : codepage, findings);
        }
        catch { /* a pre-check must never block the app from opening */ }
        return findings;
    }

    // ---- server YAML ----

    private static void CheckServerYaml(WorkspacePaths paths, IReadOnlyList<DbSchema> schemas, ServerMode mode, List<CompatFinding> findings)
    {
        if (string.IsNullOrWhiteSpace(paths.ServerDbRoot) || schemas is null) return;

        foreach (var schema in schemas)
        {
            // The import file is what we WRITE — a present, mismatched Type would make a save corrupt it.
            ProbeYamlHeader(Resolve(paths.ServerDbRoot, schema.Layout.ImportFile), schema, findings, isImport: true);

            // The first base file with a header tells us the server's db version (old/new rAthena).
            foreach (var rel in schema.Layout.BaseFiles(mode))
                if (ProbeYamlHeader(Resolve(paths.ServerDbRoot, rel), schema, findings, isImport: false))
                    break;
        }
    }

    /// <summary>Reads a file's <c>Header</c> (if present) and flags a Type/Version mismatch. Returns true if
    /// the file existed and carried a Header.</summary>
    private static bool ProbeYamlHeader(string path, DbSchema schema, List<CompatFinding> findings, bool isImport)
    {
        if (!File.Exists(path)) return false;
        var (type, version) = ReadYamlHeader(ReadHead(path));
        string name = Path.GetFileName(path);

        if (!string.IsNullOrEmpty(type) && !string.Equals(type, schema.HeaderType, StringComparison.Ordinal))
        {
            findings.Add(new CompatFinding(name, CompatSeverity.Blocker,
                $"{name} declares Type: {type}, but this database expects {schema.HeaderType} — it looks like a different db. " +
                (isImport ? "Saving would write a header/body-mismatched file." : "It won't load correctly against this schema.")));
            return true; // type is already decisive; don't also nag about version
        }

        if (version is int v && v != schema.HeaderVersion)
            findings.Add(new CompatFinding(name, CompatSeverity.Warning,
                $"{name} is Version {v}; the editor models version {schema.HeaderVersion}. " +
                "Unknown fields are preserved, but newer fields may be blank and some shapes may differ."));

        return type is not null || version is not null;
    }

    /// <summary>Parses just the <c>Type:</c> / <c>Version:</c> under a top-level <c>Header:</c>.</summary>
    private static (string? Type, int? Version) ReadYamlHeader(string head)
    {
        var lines = head.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        int h = -1;
        for (int i = 0; i < lines.Length; i++)
            if (lines[i].StartsWith("Header:", StringComparison.Ordinal)) { h = i; break; }
        if (h < 0) return (null, null);

        string? type = null; int? version = null;
        for (int i = h + 1; i < lines.Length; i++)
        {
            var l = lines[i];
            if (l.Length > 0 && char.IsLetter(l[0])) break; // next top-level key
            var t = l.Trim();
            if (t.StartsWith("Type:", StringComparison.Ordinal)) type = t.Substring(5).Trim().Trim('"', '\'');
            else if (t.StartsWith("Version:", StringComparison.Ordinal) && int.TryParse(t.Substring(8).Trim(), out var ver)) version = ver;
        }
        return (type, version);
    }

    // ---- client lua/lub ----

    private static void CheckClientFiles(WorkspacePaths paths, int codepage, List<CompatFinding> findings)
    {
        var codec = new LuaFileCodec(codepage);

        ProbeLuaTable(paths.ItemInfoPath, codec, "tbl", "base item client text", findings);
        ProbeItemInfoCustom(paths.ItemInfoCustomPath, codec, findings);

        if (string.IsNullOrWhiteSpace(paths.LuaFilesRoot)) return;
        string skz = Path.Combine(paths.LuaFilesRoot, "skillinfoz");
        ProbeLuaTable(Path.Combine(skz, "skillid.lub"), codec, "SKID", "client skills", findings);
        ProbeLuaTable(Path.Combine(skz, "skillinfolist.lub"), codec, "SKILL_INFO_LIST", "client skills", findings);
        ProbeLuaTable(Path.Combine(skz, "skilldescript.lub"), codec, "SKILL_DESCRIPT", "client skills", findings);
        ProbeLuaTable(Path.Combine(skz, "skilldelaylist.lub"), codec, "SKILL_DELAY_LIST", "client skills", findings);

        string di = Path.Combine(paths.LuaFilesRoot, "datainfo");
        ProbeLuaTable(Path.Combine(di, "accessoryid.lub"), codec, "ACCESSORY_IDs", "headgear sprites", findings);
        ProbeLuaTable(Path.Combine(di, "accname.lub"), codec, "AccNameTable", "headgear sprites", findings);
        ProbeLuaTable(Path.Combine(di, "npcidentity.lub"), codec, "jobtbl", "mob sprites", findings);
        ProbeLuaTable(Path.Combine(di, "jobname.lub"), codec, "JobNameTable", "mob sprites", findings);
    }

    /// <summary>Warns when a client file EXISTS and is non-empty but its expected table can't be located — the
    /// writer would either throw or mis-route on save (an old/renamed/unsupported client format). An absent
    /// file is fine (the feature is simply unavailable, e.g. the table is packed in a GRF).</summary>
    private static void ProbeLuaTable(string? path, LuaFileCodec codec, string table, string feature, List<CompatFinding> findings)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        string text = Read(path, codec);
        if (string.IsNullOrWhiteSpace(text)) return;
        if (LuaScan.FindTableOpen(text, table) < 0)
            findings.Add(new CompatFinding(Path.GetFileName(path), CompatSeverity.Warning,
                $"{Path.GetFileName(path)} is present but its '{table}' table is missing or renamed — the {feature} editor " +
                "can't safely write to it (old/unsupported client format)."));
    }

    private static readonly Regex TopLevelTbl = new(@"(?m)^(tbl_[A-Za-z0-9_]+)\s*=\s*\{", RegexOptions.Compiled);

    private static void ProbeItemInfoCustom(string? path, LuaFileCodec codec, List<CompatFinding> findings)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        string text = Read(path, codec);
        if (string.IsNullOrWhiteSpace(text)) return;

        string name = Path.GetFileName(path);
        bool hasCustom = LuaScan.FindTableOpen(text, "tbl_custom") >= 0;
        bool hasOverride = LuaScan.FindTableOpen(text, "tbl_override") >= 0;

        var extras = new List<string>();
        foreach (Match m in TopLevelTbl.Matches(text))
        {
            var t = m.Groups[1].Value;
            if (t is not ("tbl_custom" or "tbl_override") && !extras.Contains(t)) extras.Add(t);
        }
        if (extras.Count > 0)
            findings.Add(new CompatFinding(name, CompatSeverity.Warning,
                $"{name} defines extra tables ({string.Join(", ", extras)}). Edits are written to tbl_custom — items in " +
                "those tables aren't editable here and may end up duplicated."));
        else if (!hasCustom && !hasOverride)
            findings.Add(new CompatFinding(name, CompatSeverity.Warning,
                $"{name} has neither tbl_custom nor tbl_override — the editor will append a new tbl_custom on save."));
    }

    // ---- io helpers (read-only, best-effort) ----

    private static string Resolve(string root, string relative) =>
        Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));

    private static string Read(string path, LuaFileCodec codec)
    {
        try { return codec.ReadText(path); } catch { return string.Empty; }
    }

    /// <summary>Reads only the first chunk of a (possibly multi-MB base) file — the Header is always at the top.</summary>
    private static string ReadHead(string path, int maxBytes = 8192)
    {
        try
        {
            using var fs = File.OpenRead(path);
            int n = (int)Math.Min(fs.Length, maxBytes);
            var buf = new byte[n];
            int read = fs.Read(buf, 0, n);
            return Encoding.UTF8.GetString(buf, 0, read); // headers are ASCII
        }
        catch { return string.Empty; }
    }
}
