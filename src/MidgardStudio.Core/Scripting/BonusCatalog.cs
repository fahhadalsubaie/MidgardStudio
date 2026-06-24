using System.Text;
using MidgardStudio.Core.Schema;

namespace MidgardStudio.Core.Scripting;

public enum BonusParamKind { Number, Enum, Text }

/// <summary>A single value-slot of a bonus (e.g. the "race" or the "rate" of bonus2 bAddRace,r,n).</summary>
public sealed record BonusParam(string Name, BonusParamKind Kind, EnumSource? Enum = null, string Default = "0");

/// <summary>A curated rAthena item-bonus definition: its family, name, a human label, and typed params.</summary>
public sealed record BonusDefinition(string Family, string Name, string Display, IReadOnlyList<BonusParam> Params)
{
    public string Search => $"{Name} {Display}".ToLowerInvariant();
}

/// <summary>
/// A curated table of the most-used rAthena item bonuses with typed, enum-aware parameters — enough to
/// build the common ones visually. (item_bonus.txt is prose, so the parameter shapes are hand-modelled.)
/// </summary>
public static class BonusCatalog
{
    public static readonly EnumSource Race = EnumSource.Labeled("Race",
        ("RC_Formless", "Formless"), ("RC_Undead", "Undead"), ("RC_Brute", "Brute / Beast"),
        ("RC_Plant", "Plant"), ("RC_Insect", "Insect"), ("RC_Fish", "Fish"), ("RC_Demon", "Demon"),
        ("RC_DemiHuman", "Demi-Human"), ("RC_Angel", "Angel"), ("RC_Dragon", "Dragon"),
        ("RC_Player_Human", "Player (Human)"), ("RC_Player_Doram", "Player (Doram)"), ("RC_All", "All races"));

    public static readonly EnumSource Element = EnumSource.Labeled("Element",
        ("Ele_Neutral", "Neutral"), ("Ele_Water", "Water"), ("Ele_Earth", "Earth"), ("Ele_Fire", "Fire"),
        ("Ele_Wind", "Wind"), ("Ele_Poison", "Poison"), ("Ele_Holy", "Holy"), ("Ele_Dark", "Dark"),
        ("Ele_Ghost", "Ghost"), ("Ele_Undead", "Undead"), ("Ele_All", "All elements"));

    public static readonly EnumSource Size = EnumSource.Labeled("Size",
        ("Size_Small", "Small"), ("Size_Medium", "Medium"), ("Size_Large", "Large"), ("Size_All", "All sizes"));

    private static BonusParam Num(string name, string def = "10") => new(name, BonusParamKind.Number, Default: def);
    private static BonusParam EnumP(string name, EnumSource src) => new(name, BonusParamKind.Enum, src, src.Values.FirstOrDefault() ?? "");
    private static BonusParam Txt(string name, string def = "") => new(name, BonusParamKind.Text, Default: def);

    private static BonusDefinition B(string name, string display, params BonusParam[] p) => new("bonus", name, display, p);
    private static BonusDefinition B2(string name, string display, params BonusParam[] p) => new("bonus2", name, display, p);
    private static BonusDefinition B3(string name, string display, params BonusParam[] p) => new("bonus3", name, display, p);

    public static readonly IReadOnlyList<BonusDefinition> All = new List<BonusDefinition>
    {
        // --- core stats ---
        B("bStr", "STR + n", Num("amount")),
        B("bAgi", "AGI + n", Num("amount")),
        B("bVit", "VIT + n", Num("amount")),
        B("bInt", "INT + n", Num("amount")),
        B("bDex", "DEX + n", Num("amount")),
        B("bLuk", "LUK + n", Num("amount")),
        B("bAllStats", "All stats + n", Num("amount")),

        // --- HP / SP ---
        B("bMaxHP", "Max HP + n", Num("amount", "100")),
        B("bMaxSP", "Max SP + n", Num("amount", "50")),
        B("bMaxHPrate", "Max HP + n%", Num("percent")),
        B("bMaxSPrate", "Max SP + n%", Num("percent")),
        B("bHPrecovRate", "HP recovery + n%", Num("percent")),
        B("bSPrecovRate", "SP recovery + n%", Num("percent")),

        // --- offense ---
        B("bAtk", "ATK + n", Num("amount")),
        B("bAtkRate", "ATK + n%", Num("percent")),
        B("bMatk", "MATK + n", Num("amount")),
        B("bMatkRate", "MATK + n%", Num("percent")),
        B("bBaseAtk", "Base ATK + n", Num("amount")),
        B("bHit", "HIT + n", Num("amount")),
        B("bCritical", "CRIT + n", Num("amount")),
        B("bCriticalRate", "Critical chance + n%", Num("percent")),
        B("bAspd", "ASPD + n", Num("amount", "1")),
        B("bAspdRate", "ASPD + n%", Num("percent")),
        B("bSpeedRate", "Movement speed + n%", Num("percent", "25")),

        // --- defense ---
        B("bDef", "DEF + n", Num("amount")),
        B("bDef2Rate", "Soft DEF + n%", Num("percent")),
        B("bMdef", "MDEF + n", Num("amount")),
        B("bFlee", "FLEE + n", Num("amount")),
        B("bFlee2", "Perfect dodge + n", Num("amount")),
        B("bNearAtkDef", "Resist melee damage + n%", Num("percent")),
        B("bLongAtkDef", "Resist ranged damage + n%", Num("percent")),
        B("bMagicAtkDef", "Resist magic damage + n%", Num("percent")),

        // --- vs race / element / size ---
        B2("bAddRace", "+n% physical damage vs race", EnumP("race", Race), Num("percent")),
        B2("bMagicAddRace", "+n% magic damage vs race", EnumP("race", Race), Num("percent")),
        B2("bSubRace", "-n% damage taken from race", EnumP("race", Race), Num("percent")),
        B2("bCriticalAddRace", "+n crit vs race", EnumP("race", Race), Num("amount")),
        B2("bIgnoreDefRaceRate", "Ignore n% DEF of race", EnumP("race", Race), Num("percent")),
        B2("bExpAddRace", "+n% EXP from race", EnumP("race", Race), Num("percent")),
        B2("bAddEle", "+n% physical damage vs element", EnumP("element", Element), Num("percent")),
        B2("bMagicAddEle", "+n% magic damage vs element", EnumP("element", Element), Num("percent")),
        B2("bSubEle", "-n% damage taken from element", EnumP("element", Element), Num("percent")),
        B2("bAddSize", "+n% physical damage vs size", EnumP("size", Size), Num("percent")),
        B2("bMagicAddSize", "+n% magic damage vs size", EnumP("size", Size), Num("percent")),
        B2("bSubSize", "-n% damage taken from size", EnumP("size", Size), Num("percent")),

        // --- leech / triggers ---
        B2("bHPDrainRate", "HP leech: n/10% chance, +x% of damage", Num("chance_x10", "50"), Num("percent")),
        B2("bSPDrainRate", "SP leech: n/10% chance, +x% of damage", Num("chance_x10", "50"), Num("percent")),
        B3("bAutoSpell", "Auto-cast skill (skill, level, n/10% on attack)", Txt("skill"), Num("level", "1"), Num("chance_x10", "50")),
        B3("bAutoSpellWhenHit", "Auto-cast when hit (skill, level, n/10%)", Txt("skill"), Num("level", "1"), Num("chance_x10", "50")),

        // --- flags (no parameters) ---
        B("bNoCastCancel", "Cast not interrupted when hit"),
        B("bUnbreakableHelm", "Headgear cannot break"),
        B("bUnbreakableArmor", "Armor cannot break"),
        B("bUnbreakableWeapon", "Weapon cannot break"),
        B("bUnbreakableShield", "Shield cannot break"),
        B("bPerfectHide", "Hidden from Insect/Demon monsters"),
        B("bNoKnockback", "Immune to knockback"),
    };

    /// <summary>Formats a complete bonus statement: e.g. <c>bonus2 bAddRace,RC_DemiHuman,5;</c>.</summary>
    public static string Format(BonusDefinition def, IReadOnlyList<string> values)
    {
        var sb = new StringBuilder(def.Family).Append(' ').Append(def.Name);
        for (int i = 0; i < def.Params.Count; i++)
        {
            string v = i < values.Count ? values[i] : def.Params[i].Default;
            sb.Append(',').Append(string.IsNullOrWhiteSpace(v) ? def.Params[i].Default : v.Trim());
        }
        return sb.Append(';').ToString();
    }
}
