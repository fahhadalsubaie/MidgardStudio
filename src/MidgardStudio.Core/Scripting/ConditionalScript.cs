using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MidgardStudio.Core.Lua;

namespace MidgardStudio.Core.Scripting;

/// <summary>The kind of condition a ladder gates on.</summary>
public enum ConditionKind { Refine, Grade }

/// <summary>One tier of an ascending ladder: a threshold plus the bonus statements it ADDS on top of the
/// lower tiers (cumulative). A Refine threshold is a refine level (e.g. 9); a Grade threshold is an
/// ENCHANTGRADE index (1=D, 2=C, 3=B, 4=A).</summary>
public sealed record ConditionTier(int Threshold, IReadOnlyList<string> Bonuses);

/// <summary>An ascending ladder of tiers for one condition type (refine or grade).</summary>
public sealed record ConditionLadder(ConditionKind Kind, IReadOnlyList<ConditionTier> Tiers);

/// <summary>
/// A structured "conditional bonuses" model: zero or more ascending ladders (refine and/or grade). It renders
/// (a) a managed block of idiomatic nested rAthena script and (b) the matching client description sections in
/// the official 4th-job layout (blue "Refine Level +N:" headers, a red "[Bonus by Grade]" section), and it
/// round-trips FROM the exact block it emits — its own format only, so it's reliable, not a general parser.
/// Unconditional bonuses are NOT part of this block; they live in the normal (flat) script and are described by
/// <see cref="ItemAutocomplete"/> as before. The block is delimited so it can be stripped before flat-parsing.
/// </summary>
public sealed record ConditionalScript(IReadOnlyList<ConditionLadder> Ladders)
{
    public const string BeginMarker = "//>> Refine/Grade bonuses (auto)";
    public const string EndMarker = "//<< Refine/Grade bonuses (auto)";

    public bool IsEmpty => Ladders.All(l => l.Tiers.All(t => t.Bonuses.Count == 0));

    // ----- emit -----

    /// <summary>Renders the model as a managed block (marker, each ladder as nested ascending if-blocks,
    /// marker). Empty model → empty string (nothing to insert).</summary>
    public string Emit()
    {
        if (IsEmpty) return string.Empty;
        var sb = new StringBuilder();
        sb.Append(BeginMarker).Append('\n');
        foreach (var ladder in Ladders)
        {
            var tiers = ladder.Tiers.Where(t => t.Bonuses.Any(b => b.Trim().Length > 0)).OrderBy(t => t.Threshold).ToList();
            EmitTiers(sb, ladder.Kind, tiers, 0, 0);
        }
        sb.Append(EndMarker).Append('\n');
        return sb.ToString();
    }

    private static void EmitTiers(StringBuilder sb, ConditionKind kind, IReadOnlyList<ConditionTier> tiers, int index, int depth)
    {
        if (index >= tiers.Count) return;
        string indent = new string('\t', depth);
        string inner = new string('\t', depth + 1);
        sb.Append(indent).Append("if (").Append(Condition(kind, tiers[index].Threshold)).Append(") {\n");
        foreach (var b in tiers[index].Bonuses)
            if (b.Trim().Length > 0) sb.Append(inner).Append(b.Trim()).Append('\n');
        EmitTiers(sb, kind, tiers, index + 1, depth + 1);
        sb.Append(indent).Append("}\n");
    }

    private static string Condition(ConditionKind kind, int threshold) =>
        kind == ConditionKind.Grade
            ? $"getenchantgrade()>={GradeConst(threshold)}"
            : $"getrefine()>={threshold}";

    // ----- describe (official 4th-job description layout) -----

    /// <summary>The client description sections for the conditional bonuses, in the layout the official
    /// itemInfo uses: each refine tier as a blue "Refine Level +N:" header followed by its comma-joined
    /// bonuses; grade tiers under a red "[Bonus by Grade]" header as "[Grade X]: bonuses". Colors are omitted
    /// when <paramref name="useColors"/> is false.</summary>
    public IReadOnlyList<string> Describe(bool useColors = true, Func<string, string?>? resolveSkill = null)
    {
        var lines = new List<string>();

        var refine = Ladders.FirstOrDefault(l => l.Kind == ConditionKind.Refine);
        if (refine is not null)
            foreach (var tier in refine.Tiers.OrderBy(t => t.Threshold))
            {
                var b = DescribeBonuses(tier.Bonuses, resolveSkill);
                if (b.Count == 0) continue;
                lines.Add(Colored($"Refine Level +{tier.Threshold}", "0000FF", useColors) + ":");
                lines.Add(string.Join(", ", b) + ".");
            }

        var grade = Ladders.FirstOrDefault(l => l.Kind == ConditionKind.Grade);
        if (grade is not null)
        {
            var gradeLines = new List<string>();
            foreach (var tier in grade.Tiers.OrderBy(t => t.Threshold))
            {
                var b = DescribeBonuses(tier.Bonuses, resolveSkill);
                if (b.Count == 0) continue;
                gradeLines.Add($"[Grade {GradeLetter(tier.Threshold)}]: " + string.Join(", ", b) + ".");
            }
            if (gradeLines.Count > 0)
            {
                lines.Add(Colored("[Bonus by Grade]", "CC3D3D", useColors));
                lines.AddRange(gradeLines);
            }
        }

        return lines;
    }

    private static string Colored(string text, string color, bool useColors) => useColors ? $"^{color}{text}^000000" : text;

    private static IReadOnlyList<string> DescribeBonuses(IReadOnlyList<string> bonusLines, Func<string, string?>? resolveSkill)
    {
        if (bonusLines.Count == 0) return Array.Empty<string>();
        // Reuse the (modern-aware) item-script parser so each tier's bonus lines render exactly like flat
        // bonuses; join with spaces since the parser splits on ';' after flattening whitespace.
        return ItemScriptParser.Parse(string.Join(" ", bonusLines), resolveSkill).Bonuses;
    }

    // ----- block extraction / round-trip parse (our own emitted format only) -----

    private static readonly Regex RefineIf = new(@"^\s*if\s*\(\s*getrefine\(\)\s*>=\s*(\d+)\s*\)\s*\{\s*$", RegexOptions.Compiled);
    private static readonly Regex GradeIf = new(@"^\s*if\s*\(\s*getenchantgrade\(\)\s*>=\s*ENCHANTGRADE_([A-Z]+)\s*\)\s*\{\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CloseBrace = new(@"^\s*\}\s*$", RegexOptions.Compiled);

    /// <summary>Returns <paramref name="script"/> with the managed block (markers + content) removed, so the
    /// flat description generator never double-counts a tier's bonuses. Unchanged when there's no block.</summary>
    public static string StripManagedBlock(string? script)
    {
        if (string.IsNullOrEmpty(script)) return script ?? string.Empty;
        var all = script.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        int begin = Array.FindIndex(all, l => l.Trim() == BeginMarker);
        int end = Array.FindIndex(all, l => l.Trim() == EndMarker);
        if (begin < 0 || end < 0 || end < begin) return script;
        return string.Join("\n", all.Take(begin).Concat(all.Skip(end + 1)));
    }

    /// <summary>Finds the managed block in a full script and parses it back into the model, or null when the
    /// script has no managed block. Parses ONLY the regular, one-statement-per-line format <see cref="Emit"/>
    /// produces — reliable by construction, not a general rAthena parser.</summary>
    public static ConditionalScript? TryParse(string? script)
    {
        if (string.IsNullOrEmpty(script)) return null;
        var all = script.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        int begin = Array.FindIndex(all, l => l.Trim() == BeginMarker);
        int end = Array.FindIndex(all, l => l.Trim() == EndMarker);
        if (begin < 0 || end < 0 || end <= begin) return null;

        var body = all.Skip(begin + 1).Take(end - begin - 1).Where(l => l.Trim().Length > 0).ToList();
        var ladders = new List<ConditionLadder>();
        int i = 0;
        while (i < body.Count)
        {
            if (RefineIf.IsMatch(body[i]) || GradeIf.IsMatch(body[i]))
            {
                var (ladder, next) = ParseLadder(body, i);
                if (ladder.Tiers.Count > 0) ladders.Add(ladder);
                i = next;
            }
            else
            {
                i++; // stray line inside the block (shouldn't happen with our emit) — skip
            }
        }
        return new ConditionalScript(ladders);
    }

    private static (ConditionLadder Ladder, int Next) ParseLadder(IReadOnlyList<string> body, int start)
    {
        var tiers = new List<(int Threshold, List<string> Bonuses)>();
        ConditionKind kind = ConditionKind.Refine;
        int depth = 0;
        int i = start;
        while (i < body.Count)
        {
            var rm = RefineIf.Match(body[i]);
            var gm = GradeIf.Match(body[i]);
            if (rm.Success || gm.Success)
            {
                if (tiers.Count == 0) kind = gm.Success ? ConditionKind.Grade : ConditionKind.Refine;
                int threshold = gm.Success ? GradeIndex(gm.Groups[1].Value) : int.Parse(rm.Groups[1].Value);
                tiers.Add((threshold, new List<string>()));
                depth++;
                i++;
            }
            else if (CloseBrace.IsMatch(body[i]))
            {
                depth--;
                i++;
                if (depth == 0) break; // this ladder's outermost if just closed
            }
            else
            {
                if (tiers.Count > 0) tiers[^1].Bonuses.Add(body[i].Trim()); // deepest open tier owns the line
                i++;
            }
        }
        var frozen = tiers.Select(t => new ConditionTier(t.Threshold, t.Bonuses)).ToList();
        return (new ConditionLadder(kind, frozen), i);
    }

    // ----- grade helpers (ENCHANTGRADE order: NONE=0, D=1, C=2, B=3, A=4) -----

    private static string GradeConst(int t) => "ENCHANTGRADE_" + GradeLetter(t);

    public static string GradeLetter(int t) => t switch { 1 => "D", 2 => "C", 3 => "B", 4 => "A", _ => "D" };

    private static int GradeIndex(string s) => s.ToUpperInvariant() switch
    {
        "D" => 1, "C" => 2, "B" => 3, "A" => 4, "NONE" => 0, _ => 1,
    };
}
