using System.Globalization;
using System.Text.RegularExpressions;

namespace MidgardStudio.Core.Lua;

/// <summary>One recovery line parsed from a heal script (e.g. amount "45 - 65", unit "HP").</summary>
public readonly record struct HealLine(string Amount, string Unit);

/// <summary>Structured, human-readable facts extracted from an item's rAthena <c>Script</c>.</summary>
public sealed class ScriptInsights
{
    public List<HealLine> Heals { get; } = new();
    public List<string> Cures { get; } = new();
    public string? Element { get; set; }
    public List<string> Bonuses { get; } = new();
    /// <summary>True when the script contained statements too complex to read safely (autobonus, conditionals, refine…).</summary>
    public bool HasComplex { get; set; }
}

/// <summary>
/// Parses common rAthena item-script statements into readable description fragments for Autocomplete.
/// Deliberately conservative: anything it doesn't recognise (autobonus, conditionals, getrefine, skill grants,
/// variables) is flagged via <see cref="ScriptInsights.HasComplex"/> rather than guessed at.
/// </summary>
public static class ItemScriptParser
{
    static ItemScriptParser()
    {
        // This parser matches ~30 distinct inline patterns — more than the process-wide static Regex cache
        // default (15) — so without this each call recompiled evicted patterns. Raising the cache keeps every
        // pattern compiled-once-and-reused, fixing the thrash with no risk of mis-transcribing a pattern.
        if (Regex.CacheSize < 64) Regex.CacheSize = 64;
    }

    private static readonly Regex Complex = new(
        @"autobonus|getrefine|callfunc|\bif\b|\belse\b|\bfor\b|\bwhile\b|\.@|\bset\b|\bgetitem\b|\bskilleffect\b|\bpetloot\b|\bhealpercent\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <param name="resolveSkill">Maps a skill AegisName (e.g. "MG_FIREBOLT") to its display name ("Fire Bolt").</param>
    public static ScriptInsights Parse(string? script, Func<string, string?>? resolveSkill = null)
    {
        var result = new ScriptInsights();
        if (string.IsNullOrWhiteSpace(script)) return result;

        script = StripComments(script); // before the newline flatten below, so a // comment can't swallow the next line

        if (Complex.IsMatch(script)) result.HasComplex = true;

        foreach (var raw in script.Replace("\r", " ").Replace("\n", " ").Split(';'))
        {
            var s = Regex.Replace(raw, @"\s+", " ").Trim();
            if (s.Length == 0) continue;
            if (!TryParse(s, result, resolveSkill) && !LooksIgnorable(s))
                result.HasComplex = true; // an effect we couldn't read confidently
        }

        return result;
    }

    // Strip /* */ block comments then // line comments (each only to its own end-of-line) BEFORE Parse flattens
    // newlines into spaces — otherwise a "// note" would merge with and silently swallow the next statement.
    // ponytail: not string-literal-aware, but this only feeds the generated description text (never the saved
    // Script), and item scripts don't put "//" inside quotes — so a worst case is one omitted description line.
    private static string StripComments(string script)
    {
        script = Regex.Replace(script, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        return Regex.Replace(script, @"//[^\n]*", " ");
    }

    private static bool LooksIgnorable(string s) =>
        s.StartsWith("//", StringComparison.Ordinal) || s.StartsWith("/*", StringComparison.Ordinal) || s == "{" || s == "}";

    private static string Skill(string aegis, Func<string, string?>? resolve)
    {
        var d = resolve?.Invoke(aegis);
        if (!string.IsNullOrWhiteSpace(d)) return d!;
        // Fallback: strip the class prefix and title-case (e.g. AL_TELEPORT → Teleport).
        int us = aegis.IndexOf('_');
        var body = us >= 0 && us < aegis.Length - 1 ? aegis[(us + 1)..] : aegis;
        return Capitalize(body.Replace('_', ' '));
    }

    private static bool TryParse(string s, ScriptInsights r, Func<string, string?>? resolve)
    {
        Match m;

        // Skill grants / casts / autocasts — resolved to readable names.
        if ((m = Regex.Match(s, @"^itemskill\s+""?([A-Za-z][\w]*)""?,(\d+)")).Success)
        {
            r.Bonuses.Add($"Casts Level {m.Groups[2].Value} {Skill(m.Groups[1].Value, resolve)}.");
            return true;
        }
        if ((m = Regex.Match(s, @"^skill\s+""?([A-Za-z][\w]*)""?,(\d+)$")).Success)
        {
            r.Bonuses.Add($"Enables Level {m.Groups[2].Value} {Skill(m.Groups[1].Value, resolve)}.");
            return true;
        }
        if ((m = Regex.Match(s, @"^bonus[345]\s+bAutoSpell(WhenHit)?,""?([A-Za-z][\w]*)""?,(\d+),")).Success)
        {
            bool whenHit = m.Groups[1].Value.Length > 0;
            string skill = Skill(m.Groups[2].Value, resolve);
            r.Bonuses.Add(whenHit
                ? $"When hit, has a chance to autocast Level {m.Groups[3].Value} {skill}."
                : $"Has a chance to autocast Level {m.Groups[3].Value} {skill}.");
            return true;
        }

        // itemheal <hp>,<sp>;  — each side is rand(a,b) or a literal number; 0 means none.
        // Match each side explicitly so the top-level comma isn't confused with the one inside rand().
        if ((m = Regex.Match(s, @"^itemheal\s+(rand\(\d+,\s*\d+\)|\d+),\s*(rand\(\d+,\s*\d+\)|\d+)$")).Success)
        {
            AddHeal(m.Groups[1].Value, "HP", r);
            AddHeal(m.Groups[2].Value, "SP", r);
            return true;
        }
        // percentheal <hp%>,<sp%>;
        if ((m = Regex.Match(s, @"^percentheal\s+(\d+),(\d+)$")).Success)
        {
            if (m.Groups[1].Value != "0") r.Heals.Add(new HealLine(m.Groups[1].Value + "%", "HP"));
            if (m.Groups[2].Value != "0") r.Heals.Add(new HealLine(m.Groups[2].Value + "%", "SP"));
            return true;
        }
        // sc_end SC_<STATUS>;  (a cure)
        if ((m = Regex.Match(s, @"^sc_end\s+SC_(\w+)$")).Success)
        {
            r.Cures.Add(Status(m.Groups[1].Value));
            return true;
        }
        // bonus bAtkEle,Ele_<X>;  (weapon element)
        if ((m = Regex.Match(s, @"^bonus\s+bAtkEle,Ele_(\w+)$")).Success)
        {
            r.Element = Capitalize(m.Groups[1].Value);
            return true;
        }
        // bonus b<Stat>,<n>;
        if ((m = Regex.Match(s, @"^bonus\s+b(Str|Agi|Vit|Int|Dex|Luk),([+-]?\d+)$")).Success)
        {
            r.Bonuses.Add($"{m.Groups[1].Value.ToUpperInvariant()} {Signed(m.Groups[2].Value)}");
            return true;
        }
        if ((m = Regex.Match(s, @"^bonus\s+bAllStats,([+-]?\d+)$")).Success)
        {
            r.Bonuses.Add($"All Stats {Signed(m.Groups[1].Value)}");
            return true;
        }
        // simple "<label> +n" / "+n%" bonuses
        foreach (var (rx, label, pct) in SimpleBonuses)
        {
            if ((m = Regex.Match(s, rx)).Success)
            {
                r.Bonuses.Add($"{label} {Signed(m.Groups[1].Value)}{(pct ? "%" : string.Empty)}");
                return true;
            }
        }
        // flags
        if (Regex.IsMatch(s, @"^bonus\s+bUnbreakableWeapon$")) { r.Bonuses.Add("Unbreakable Weapon"); return true; }
        if (Regex.IsMatch(s, @"^bonus\s+bUnbreakableArmor$")) { r.Bonuses.Add("Unbreakable Armor"); return true; }
        // bonus2 bAddRace/bSubRace,RC_<X>,<n>;
        if ((m = Regex.Match(s, @"^bonus2\s+b(Add|Sub)Race,RC_(\w+),(\d+)$")).Success)
        {
            bool add = m.Groups[1].Value == "Add";
            r.Bonuses.Add(add
                ? $"Damage vs {Race(m.Groups[2].Value)}: +{m.Groups[3].Value}%"
                : $"Damage from {Race(m.Groups[2].Value)}: -{m.Groups[3].Value}%");
            return true;
        }
        // bonus2 bAddEle/bSubEle,Ele_<X>,<n>;
        if ((m = Regex.Match(s, @"^bonus2\s+b(Add|Sub)Ele,Ele_(\w+),(\d+)$")).Success)
        {
            bool add = m.Groups[1].Value == "Add";
            r.Bonuses.Add(add
                ? $"Damage vs {Capitalize(m.Groups[2].Value)}: +{m.Groups[3].Value}%"
                : $"Damage from {Capitalize(m.Groups[2].Value)}: -{m.Groups[3].Value}%");
            return true;
        }
        return false;
    }

    private static readonly (string Rx, string Label, bool Pct)[] SimpleBonuses =
    {
        (@"^bonus\s+bAtk,([+-]?\d+)$", "ATK", false),
        (@"^bonus\s+bMatk,([+-]?\d+)$", "MATK", false),
        (@"^bonus\s+bDef,([+-]?\d+)$", "DEF", false),
        (@"^bonus\s+bMdef,([+-]?\d+)$", "MDEF", false),
        (@"^bonus\s+bMaxHPrate,([+-]?\d+)$", "Max HP", true),
        (@"^bonus\s+bMaxSPrate,([+-]?\d+)$", "Max SP", true),
        (@"^bonus\s+bMaxHP,([+-]?\d+)$", "Max HP", false),
        (@"^bonus\s+bMaxSP,([+-]?\d+)$", "Max SP", false),
        (@"^bonus\s+bHit,([+-]?\d+)$", "Hit", false),
        (@"^bonus\s+bFlee2,([+-]?\d+)$", "Perfect Dodge", false),
        (@"^bonus\s+bFlee,([+-]?\d+)$", "Flee", false),
        (@"^bonus\s+bCritical,([+-]?\d+)$", "Critical", false),
        (@"^bonus\s+bAspdRate,([+-]?\d+)$", "ASPD", true),
        (@"^bonus\s+bHealPower,([+-]?\d+)$", "Healing Power", true),
        (@"^bonus\s+bSpeedRate,([+-]?\d+)$", "Movement Speed", true),
    };

    private static void AddHeal(string part, string unit, ScriptInsights r)
    {
        part = part.Trim();
        if (part == "0") return;
        var rand = Regex.Match(part, @"^rand\((\d+),\s*(\d+)\)$");
        if (rand.Success) { r.Heals.Add(new HealLine($"{rand.Groups[1].Value} - {rand.Groups[2].Value}", unit)); return; }
        if (Regex.IsMatch(part, @"^\d+$")) r.Heals.Add(new HealLine(part, unit));
        // non-numeric (variable/expression) — leave for the complex flag
        else r.HasComplex = true;
    }

    private static string Signed(string n) =>
        int.TryParse(n, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? (v >= 0 ? "+" + v.ToString(CultureInfo.InvariantCulture) : v.ToString(CultureInfo.InvariantCulture))
            : n;

    private static string Capitalize(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();

    private static string Race(string rc) => rc switch
    {
        "DemiHuman" => "Demi-Human",
        "Player_Human" or "Player" or "Player_Doram" => "Players",
        "All" => "all races",
        _ => Capitalize(rc),
    };

    private static string Status(string sc) => sc switch
    {
        "POISON" => "Poison",
        "DPOISON" => "Deadly Poison",
        "BLIND" => "Blind",
        "SILENCE" => "Silence",
        "CURSE" => "Curse",
        "CONFUSION" => "Confuse",
        "STONE" => "Petrify",
        "FREEZE" => "Frozen",
        "STUN" => "Stun",
        "SLEEP" => "Sleep",
        "BLEEDING" => "Bleeding",
        "ILLUSION" => "Illusion",
        "HALLUCINATION" => "Hallucination",
        _ => Capitalize(sc.Replace('_', ' ')),
    };
}
