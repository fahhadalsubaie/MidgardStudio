using System.Globalization;
using System.Text.RegularExpressions;
using MidgardStudio.Core.Model;
using MidgardStudio.Core.Scripting;

namespace MidgardStudio.Core.Lua;

/// <summary>
/// Generates authentic, type-aware client item text from a server <c>item_db</c> record, honoring an
/// <see cref="AutocompleteConfig"/>. Branches on item Type, parses the item Script for heal amounts,
/// weapon element and stat bonuses, and emits the color-coded lines the official itemInfo uses
/// (e.g. <c>Heal:^009900 45 - 65^000000 HP</c>, <c>Element:^FF0000 Fire^000000</c>).
/// </summary>
public sealed class ItemAutocomplete
{
    private readonly AutocompleteConfig _c;
    private readonly Func<string, string?>? _skill;

    public ItemAutocomplete(AutocompleteConfig config, Func<string, string?>? resolveSkill = null)
    {
        _c = config;
        _skill = resolveSkill;
    }

    public string DisplayName(DbRecord server) => ServerItemText.DisplayName(server);

    /// <summary>
    /// Builds the full identified description, <b>preserving leading lore/flavor lines</b> from the existing
    /// description and regenerating only the structured property block from server data.
    /// </summary>
    public List<string> BuildDescription(DbRecord server, IEnumerable<string>? existingDescription)
    {
        var flavor = ExtractFlavor(existingDescription);
        var props = IdentifiedDescription(server);
        if (flavor.Count == 0) return props;
        if (props.Count == 0) return flavor;

        var combined = new List<string>(flavor);
        if (_c.IncludeDividers) combined.Add(_c.UseColors ? "^FFFFFF_^000000" : string.Empty);
        combined.AddRange(props);
        return combined;
    }

    /// <summary>Builds just the structured property block (no flavor) as ordered Lua lines.</summary>
    public List<string> IdentifiedDescription(DbRecord server)
    {
        var lines = new List<string>();
        string type = server.GetString("Type") ?? "Etc";
        var locations = server.GetSet("Locations");
        string? script = server.GetScript("Script")?.Text;
        // Flat bonuses parse the script WITHOUT the managed conditional block, so a tier's bonuses aren't
        // double-counted as flat; the block is described separately (below) in the official 4th-job layout.
        var insights = ItemScriptParser.Parse(ConditionalScript.StripManagedBlock(script), _skill);
        var conditional = ConditionalScript.TryParse(script);

        if (IsUsable(type)) BuildUsable(server, insights, conditional, lines);
        else if (string.Equals(type, "Card", StringComparison.Ordinal)) BuildCard(server, locations, insights, conditional, lines);
        else BuildEquip(server, type, locations, insights, conditional, lines);

        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    private static bool IsUsable(string type) =>
        type is "Healing" or "Usable" or "DelayConsume";

    // ---- Usables (potions, etc.) ----
    private void BuildUsable(DbRecord server, ScriptInsights ins, ConditionalScript? conditional, List<string> lines)
    {
        if (_c.IncludeClass)
            lines.Add(ClassLine(ServerItemText.CategoryLabel(server.GetString("Type") ?? "Usable", null, null), usable: true));

        if (_c.IncludeHeal)
        {
            foreach (var h in ins.Heals)
                lines.Add(Line("Heal", "Heal", h.Amount, _c.ValueColor, h.Unit));
            if (ins.Cures.Count > 0)
                lines.Add(Line("Cure", "Cure", string.Join(", ", ins.Cures), _c.ValueColor));
        }

        // Other script effects (skill casts, stat bonuses) — e.g. Infinite Flywing's "Casts Level 1 Teleport."
        if (_c.IncludeScriptBonuses) BonusLines(ins, conditional, lines);

        WeightLine(server, lines);
    }

    // ---- Cards ----
    private void BuildCard(DbRecord server, ISet<string>? locations, ScriptInsights ins, ConditionalScript? conditional, List<string> lines)
    {
        if (_c.IncludeScriptBonuses) BonusLines(ins, conditional, lines);
        if (_c.IncludeClass) lines.Add(ClassLine("Card", usable: false));
        if (_c.IncludeCompoundOn)
        {
            string target = ServerItemText.CompoundTarget(locations);
            lines.Add(Line("CompoundOn", "Compound on", target, CompoundColor(target)));
        }
        WeightLine(server, lines);
    }

    // ---- Equipment (weapons, armor, headgear, …) ----
    private void BuildEquip(DbRecord server, string type, ISet<string>? locations, ScriptInsights ins, ConditionalScript? conditional, List<string> lines)
    {
        if (_c.IncludeScriptBonuses) BonusLines(ins, conditional, lines);

        if (_c.IncludeClass)
            lines.Add(ClassLine(ServerItemText.CategoryLabel(type, server.GetString("SubType"), locations), usable: false));

        bool isWeapon = string.Equals(type, "Weapon", StringComparison.Ordinal);

        if (isWeapon) MaybeLine(lines, _c.IncludeAttack, "Attack", "Attack", Num(server, "Attack"), _c.AttackColor);
        MaybeLine(lines, _c.IncludeMagicAttack, "MagicAttack", "Magic Attack", Num(server, "MagicAttack"), _c.AttackColor);
        MaybeLine(lines, _c.IncludeDefense, "Defense", "Defense", Num(server, "Defense"), _c.DefenseColor);

        if (_c.IncludePosition && ServerItemText.IsHeadgear(locations) && ServerItemText.PositionLabel(locations) is { } pos)
            lines.Add(Line("Position", "Position", pos, _c.LabelColor));

        WeightLine(server, lines);

        if (_c.IncludeElement && isWeapon)
        {
            if (ins.Element is { } el)
                lines.Add(Line("Element", "Element", el, _c.ElementColor(el)));
            else if (_c.MissingValueText.Length > 0)
                lines.Add(Line("Element", "Element", _c.MissingValueText, _c.ElementColor("Neutral")));
        }

        if (_c.IncludeWeaponLevel)
        {
            if (isWeapon) MaybeLine(lines, true, "WeaponLevel", "Weapon Level", Num(server, "WeaponLevel"), _c.ValueColor);
            else if (string.Equals(type, "Armor", StringComparison.Ordinal))
                MaybeLine(lines, true, "ArmorLevel", "Armor Level", Num(server, "ArmorLevel"), _c.ValueColor);
        }

        MaybeLine(lines, _c.IncludeRequiredLevel, "RequiredLevel", "Level Requirement", Num(server, "EquipLevelMin"), _c.ValueColor);
        MaybeLine(lines, _c.IncludeSlots, "Slots", "Slots", Num(server, "Slots"), _c.ValueColor);

        // Refinement only applies to weapons / armor / shadow gear — never potions, cards, ammo, etc.
        if (_c.IncludeRefineable && IsRefinableType(type) && !server.GetBool("Refineable"))
            lines.Add(_c.UseColors ? "^880000Unrefineable^000000" : "Unrefineable");

        // Who can use it: the class tier (Classes) merged with the jobs ("Transcendent Archer"). Only shown
        // when the item is actually restricted — an all-jobs/all-classes item gets no line.
        if (_c.IncludeJobs && ServerItemText.ClassLabel(server.GetSet("Classes"), server.GetSet("Jobs")) is { } applicable)
            lines.Add(Line("Jobs", "Jobs", applicable, _c.LabelColor));
    }

    // ---- shared building blocks ----

    private void BonusLines(ScriptInsights ins, ConditionalScript? conditional, List<string> lines)
    {
        lines.AddRange(ins.Bonuses);

        // Conditional (refine/grade) bonuses from the managed block, rendered in the official 4th-job layout.
        var condLines = conditional?.Describe(_c.UseColors, _skill) ?? (IReadOnlyList<string>)System.Array.Empty<string>();
        if (condLines.Count > 0)
        {
            if (_c.IncludeDividers && lines.Count > 0) lines.Add(_c.UseColors ? "^FFFFFF_^000000" : string.Empty);
            lines.AddRange(condLines);
        }

        // "Has a special effect." only when there's genuinely nothing describable — no flat bonuses AND no
        // conditional block (else a conditional-only item would wrongly show the generic line).
        if (ins.HasComplex && ins.Bonuses.Count == 0 && condLines.Count == 0)
            lines.Add("Has a special effect.");
    }

    private void WeightLine(DbRecord server, List<string> lines)
    {
        int w = server.GetInt("Weight");
        if (!_c.IncludeWeight) return;
        if (w > 0)
            lines.Add(Line("Weight", "Weight", (w / 10.0).ToString("0.#", CultureInfo.InvariantCulture), _c.ValueColor));
        else if (_c.AlwaysShowWeight)
            lines.Add(Line("Weight", "Weight", "0", _c.ValueColor)); // weight 0 shown verbatim (rAthena weight is ×10)
        else if (_c.MissingValueText.Length > 0)
            lines.Add(Line("Weight", "Weight", _c.MissingValueText, _c.ValueColor));
    }

    private string ClassLine(string value, bool usable) =>
        Line("Class", "Class", value, usable ? _c.DefenseColor : _c.LabelColor);

    /// <summary>Adds a labelled line; if the value is empty, uses the fallback text or omits the line.</summary>
    private void MaybeLine(List<string> lines, bool include, string labelKey, string defaultLabel, string value, string color, string suffix = "")
    {
        if (!include) return;
        if (value.Length == 0)
        {
            if (_c.MissingValueText.Length == 0) return;
            value = _c.MissingValueText;
        }
        lines.Add(Line(labelKey, defaultLabel, value, color, suffix));
    }

    private string Line(string labelKey, string defaultLabel, string value, string color, string suffix = "")
    {
        string label = _c.Label(labelKey, defaultLabel);
        string body = _c.UseColors ? $"{label}:^{color} {value}^000000" : $"{label}: {value}";
        return suffix.Length > 0 ? body + " " + suffix : body;
    }

    private static string CompoundColor(string target) => target switch
    {
        "Weapon" => "FF0000",
        "Armor" => "00CC33",
        "Headgear" => "6600FF",
        _ => "6666CC",
    };

    private static string Num(DbRecord server, string field)
    {
        int v = server.GetInt(field);
        return v > 0 ? v.ToString(CultureInfo.InvariantCulture) : string.Empty;
    }

    private static bool IsRefinableType(string type) => type is "Weapon" or "Armor" or "ShadowGear";

    /// <summary>The leading lore/flavor lines (prose) up to the first structured property line.</summary>
    public static List<string> ExtractFlavor(IEnumerable<string>? existing)
    {
        var flavor = new List<string>();
        if (existing is null) return flavor;
        foreach (var raw in existing)
        {
            var line = raw ?? string.Empty;
            if (IsStructured(line)) break;
            flavor.Add(line);
        }
        while (flavor.Count > 0 && flavor[^1].Trim().Length == 0) flavor.RemoveAt(flavor.Count - 1);
        return flavor;
    }

    private static readonly Regex KnownLabel = new(
        @"^(Class|Type|Attack|Magic\s*Attack|M\.?Atk|Defense|DEF|MDEF|Element|Property|Position|Weight|Weapon\s*Level|Armor\s*Level|Level\s*Requirement|Required\s*Level|Jobs?|Compound\s*on|Slots?|Heal|Restore|Cure)\b.*:",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex StatBonus = new(@"^[A-Za-z][\w .'/]*\s[+\-]\d+%?$", RegexOptions.Compiled);

    /// <summary>True when a line looks like a generated/structured property rather than free prose.</summary>
    private static bool IsStructured(string line)
    {
        var t = line.Trim();
        if (t.Length == 0) return false;                    // blank lines inside leading flavor are allowed
        if (t.StartsWith('^')) return true;                 // color-coded property line or divider
        if (KnownLabel.IsMatch(t)) return true;             // "Class:", "Type:", "Weight:" …
        if (StatBonus.IsMatch(t)) return true;              // "STR +2", "Matk +15%"
        if (t.StartsWith("Unrefineable", StringComparison.OrdinalIgnoreCase)) return true;
        if (t.StartsWith("Unbreakable", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
