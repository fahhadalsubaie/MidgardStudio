using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MidgardStudio.Core.Lua;

namespace MidgardStudio.Core.Scripting;

/// <summary>A skill-level sub-gate nested inside a combo tier: extra bonuses when the wearer knows a skill at
/// a given level (<c>if (getskilllv("SKILL")&gt;=LV) { ... }</c>).</summary>
public sealed record ComboSkillGate(string Skill, int Level, IReadOnlyList<string> Bonuses);

/// <summary>One combo tier: a compound condition — a minimum TOTAL refine across the combo's pieces and/or a
/// minimum enchant grade on EVERY piece — that grants bonuses, with optional nested skill-level gates.
/// A grade threshold is an ENCHANTGRADE index (1=D, 2=C, 3=B, 4=A).</summary>
public sealed record ComboTier(int? RefineTotal, int? Grade, IReadOnlyList<string> Bonuses, IReadOnlyList<ComboSkillGate> SkillGates)
{
    public bool HasCondition => RefineTotal is not null || Grade is not null;
}

/// <summary>
/// The conditional-bonus model for an item COMBO (a shared script over 2+ equipped pieces). Unlike the
/// single-item model it gates on cross-piece conditions: a total refine sum (<c>getequiprefinerycnt(EQI_a)+
/// getequiprefinerycnt(EQI_b)…</c>) and a per-piece enchant grade (<c>getenchantgrade(EQI_a)&gt;=…</c>). The
/// pieces' equip slots (<see cref="EqiSlots"/>) are derived by the app from each combo item's Location via
/// <see cref="LocationToEqi"/>. Emits a managed block, previews the effect, and round-trips its own format.
/// </summary>
public sealed record ComboConditionalScript(IReadOnlyList<string> EqiSlots, IReadOnlyList<ComboTier> Tiers, IReadOnlyList<string> Unconditional)
{
    public const string BeginMarker = "//>> Combo bonuses (auto)";
    public const string EndMarker = "//<< Combo bonuses (auto)";

    public bool IsEmpty =>
        Unconditional.All(b => b.Trim().Length == 0)
        && Tiers.All(t => t.Bonuses.All(b => b.Trim().Length == 0) && t.SkillGates.All(g => g.Bonuses.All(b => b.Trim().Length == 0)));

    // ----- emit -----

    /// <summary>Renders the managed block: the unconditional lines, then each tier as a compound-condition
    /// if-block with its nested skill gates. Empty model → empty string.</summary>
    public string Emit()
    {
        if (IsEmpty) return string.Empty;
        var sb = new StringBuilder();
        sb.Append(BeginMarker).Append('\n');
        foreach (var line in Unconditional)
            if (line.Trim().Length > 0) sb.Append(line.Trim()).Append('\n');

        foreach (var tier in Tiers)
        {
            string cond = Condition(tier, EqiSlots);
            if (cond.Length == 0) continue; // a tier with no condition is skipped (invalid)
            var body = tier.Bonuses.Where(b => b.Trim().Length > 0).ToList();
            var gates = tier.SkillGates.Where(g => g.Bonuses.Any(b => b.Trim().Length > 0)).ToList();
            if (body.Count == 0 && gates.Count == 0) continue;

            sb.Append("if (").Append(cond).Append(") {\n");
            foreach (var b in body) sb.Append('\t').Append(b.Trim()).Append('\n');
            foreach (var g in gates)
            {
                sb.Append("\tif (getskilllv(").Append(LuaString.Quote(g.Skill)).Append(")>=").Append(g.Level).Append(") {\n");
                foreach (var b in g.Bonuses)
                    if (b.Trim().Length > 0) sb.Append("\t\t").Append(b.Trim()).Append('\n');
                sb.Append("\t}\n");
            }
            sb.Append("}\n");
        }

        sb.Append(EndMarker).Append('\n');
        return sb.ToString();
    }

    /// <summary>The compound rAthena condition for a tier: the total-refine sum across the pieces and/or the
    /// per-piece grade check, joined with <c>&amp;&amp;</c>. Empty when the tier has no condition or no pieces.</summary>
    private static string Condition(ComboTier tier, IReadOnlyList<string> eqi)
    {
        var slots = eqi.Where(e => e.Trim().Length > 0).ToList();
        if (slots.Count == 0) return string.Empty;
        var parts = new List<string>();
        if (tier.RefineTotal is int rt)
            parts.Add(string.Join("+", slots.Select(e => $"getequiprefinerycnt({e})")) + ">=" + rt);
        if (tier.Grade is int g)
            parts.AddRange(slots.Select(e => $"getenchantgrade({e})>=ENCHANTGRADE_{ConditionalScript.GradeLetter(g)}"));
        return string.Join(" && ", parts);
    }

    // ----- describe (builder preview; combos have no itemInfo of their own) -----

    /// <summary>A readable preview of the combo effect for the builder.</summary>
    public IReadOnlyList<string> Describe(Func<string, string?>? resolveSkill = null)
    {
        var lines = new List<string>();
        lines.AddRange(DescribeBonuses(Unconditional, resolveSkill));
        foreach (var tier in Tiers)
        {
            if (!tier.HasCondition) continue;
            var body = DescribeBonuses(tier.Bonuses, resolveSkill);
            var gates = tier.SkillGates.Where(g => DescribeBonuses(g.Bonuses, resolveSkill).Count > 0).ToList();
            if (body.Count == 0 && gates.Count == 0) continue;

            lines.Add("When " + DescribeCondition(tier) + ":");
            if (body.Count > 0) lines.Add("  " + string.Join(", ", body) + ".");
            foreach (var g in gates)
            {
                string skill = resolveSkill?.Invoke(g.Skill) is { Length: > 0 } d ? d : g.Skill;
                lines.Add($"  If {skill} Lv {g.Level}+: " + string.Join(", ", DescribeBonuses(g.Bonuses, resolveSkill)) + ".");
            }
        }
        return lines;
    }

    private static string DescribeCondition(ComboTier tier)
    {
        var parts = new List<string>();
        if (tier.RefineTotal is int rt) parts.Add($"total refine +{rt}");
        if (tier.Grade is int g) parts.Add($"every piece is Grade {ConditionalScript.GradeLetter(g)}");
        return string.Join(" and ", parts);
    }

    private static IReadOnlyList<string> DescribeBonuses(IReadOnlyList<string> bonusLines, Func<string, string?>? resolveSkill)
    {
        if (bonusLines.Count == 0) return Array.Empty<string>();
        return ItemScriptParser.Parse(string.Join(" ", bonusLines), resolveSkill).Bonuses;
    }

    // ----- block extraction / round-trip (our own emitted format only) -----

    private static readonly Regex TierIf = new(@"^\s*if\s*\((.+)\)\s*\{\s*$", RegexOptions.Compiled);
    private static readonly Regex SkillIf = new(@"^\s*if\s*\(\s*getskilllv\(\s*""([^""]*)""\s*\)\s*>=\s*(\d+)\s*\)\s*\{\s*$", RegexOptions.Compiled);
    private static readonly Regex CloseBrace = new(@"^\s*\}\s*$", RegexOptions.Compiled);
    private static readonly Regex RefinePart = new(@"getequiprefinerycnt\([^)]*\)(?:\s*\+\s*getequiprefinerycnt\([^)]*\))*\s*>=\s*(\d+)", RegexOptions.Compiled);
    private static readonly Regex GradePart = new(@"getenchantgrade\([^)]*\)\s*>=\s*ENCHANTGRADE_([A-Z]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex EqiPart = new(@"getequiprefinerycnt\(\s*(EQI_\w+)\s*\)", RegexOptions.Compiled);

    /// <summary>Returns the script without the managed block (markers + content); unchanged when absent.</summary>
    public static string StripManagedBlock(string? script)
    {
        if (string.IsNullOrEmpty(script)) return script ?? string.Empty;
        var all = script.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        int begin = Array.FindIndex(all, l => l.Trim() == BeginMarker);
        int end = Array.FindIndex(all, l => l.Trim() == EndMarker);
        if (begin < 0 || end < 0 || end < begin) return script;
        return string.Join("\n", all.Take(begin).Concat(all.Skip(end + 1)));
    }

    /// <summary>Parses the managed block back into the model (tiers, gates, unconditional, and the pieces read
    /// from the first refine sum), or null when there's no block. Parses only the format <see cref="Emit"/>
    /// produces. The app re-supplies <see cref="EqiSlots"/> from the combo's current pieces on re-emit.</summary>
    public static ComboConditionalScript? TryParse(string? script)
    {
        if (string.IsNullOrEmpty(script)) return null;
        var all = script.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        int begin = Array.FindIndex(all, l => l.Trim() == BeginMarker);
        int end = Array.FindIndex(all, l => l.Trim() == EndMarker);
        if (begin < 0 || end < 0 || end <= begin) return null;

        var body = all.Skip(begin + 1).Take(end - begin - 1).Where(l => l.Trim().Length > 0).ToList();
        var unconditional = new List<string>();
        var tiers = new List<ComboTier>();
        var eqi = new List<string>();

        int i = 0;
        while (i < body.Count)
        {
            var tierMatch = TierIf.Match(body[i]);
            if (tierMatch.Success && !SkillIf.IsMatch(body[i]))
            {
                var (tier, slots, next) = ParseTier(body, i, tierMatch.Groups[1].Value);
                if (tier is not null) tiers.Add(tier);
                foreach (var s in slots) if (!eqi.Contains(s)) eqi.Add(s);
                i = next;
            }
            else
            {
                if (!CloseBrace.IsMatch(body[i])) unconditional.Add(body[i].Trim());
                i++;
            }
        }
        return new ComboConditionalScript(eqi, tiers, unconditional);
    }

    private static (ComboTier? Tier, IReadOnlyList<string> Slots, int Next) ParseTier(IReadOnlyList<string> body, int start, string condition)
    {
        int? refineTotal = RefinePart.Match(condition) is { Success: true } rm ? int.Parse(rm.Groups[1].Value) : null;
        int? grade = GradePart.Match(condition) is { Success: true } gm ? GradeIndex(gm.Groups[1].Value) : null;
        var slots = EqiPart.Matches(condition).Select(m => m.Groups[1].Value).Distinct().ToList();

        var bonuses = new List<string>();
        var gates = new List<ComboSkillGate>();
        int i = start + 1;
        while (i < body.Count)
        {
            var skillMatch = SkillIf.Match(body[i]);
            if (skillMatch.Success)
            {
                var gateBonuses = new List<string>();
                i++;
                while (i < body.Count && !CloseBrace.IsMatch(body[i])) { gateBonuses.Add(body[i].Trim()); i++; }
                if (i < body.Count) i++; // consume the gate's closing brace
                gates.Add(new ComboSkillGate(skillMatch.Groups[1].Value, int.Parse(skillMatch.Groups[2].Value), gateBonuses));
            }
            else if (CloseBrace.IsMatch(body[i]))
            {
                i++; // tier closed
                break;
            }
            else
            {
                bonuses.Add(body[i].Trim());
                i++;
            }
        }
        return (new ComboTier(refineTotal, grade, bonuses, gates), slots, i);
    }

    // ----- Location -> EQI equip-slot map (rAthena; verified vs doc/script_commands.txt getequipid list) -----

    private static readonly Dictionary<string, string> LocationEqi = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Head_Top"] = "EQI_HEAD_TOP",
        ["Head_Mid"] = "EQI_HEAD_MID",
        ["Head_Low"] = "EQI_HEAD_LOW",
        ["Armor"] = "EQI_ARMOR",
        ["Right_Hand"] = "EQI_HAND_R",
        ["Left_Hand"] = "EQI_HAND_L",
        ["Both_Hand"] = "EQI_HAND_R",       // a two-handed weapon equips in the right-hand slot
        ["Garment"] = "EQI_GARMENT",
        ["Shoes"] = "EQI_SHOES",
        ["Right_Accessory"] = "EQI_ACC_R",
        ["Left_Accessory"] = "EQI_ACC_L",
        ["Both_Accessory"] = "EQI_ACC_R",   // either slot; default to the right accessory
        ["Costume_Head_Top"] = "EQI_COSTUME_HEAD_TOP",
        ["Costume_Head_Mid"] = "EQI_COSTUME_HEAD_MID",
        ["Costume_Head_Low"] = "EQI_COSTUME_HEAD_LOW",
        ["Costume_Garment"] = "EQI_COSTUME_GARMENT",
        ["Ammo"] = "EQI_AMMO",
        ["Shadow_Armor"] = "EQI_SHADOW_ARMOR",
        ["Shadow_Weapon"] = "EQI_SHADOW_WEAPON",
        ["Shadow_Shield"] = "EQI_SHADOW_SHIELD",
        ["Shadow_Shoes"] = "EQI_SHADOW_SHOES",
        ["Shadow_Right_Accessory"] = "EQI_SHADOW_ACC_R",
        ["Shadow_Left_Accessory"] = "EQI_SHADOW_ACC_L",
    };

    /// <summary>The EQI equip-slot constant for an item <c>Location</c> token (e.g. "Head_Top" → "EQI_HEAD_TOP"),
    /// or null for an unmappable location. Used to build the combo's per-piece refine/grade checks.</summary>
    public static string? LocationToEqi(string? location) =>
        location is not null && LocationEqi.TryGetValue(location.Trim(), out var e) ? e : null;

    private static int GradeIndex(string s) => s.ToUpperInvariant() switch { "D" => 1, "C" => 2, "B" => 3, "A" => 4, _ => 1 };
}
