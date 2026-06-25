using System.Text;
using MidgardStudio.Core.Schema;

namespace MidgardStudio.Core.Scripting;

public enum BonusParamKind { Number, Enum, Text, Skill }

/// <summary>A single value-slot of a bonus (e.g. the "race" or the "rate" of <c>bonus2 bAddRace,r,n</c>).
/// <paramref name="Scale"/> lets a newbie type a real percentage that the script stores in finer units —
/// e.g. an auto-cast "chance %" of 5 is written as 50 (rAthena uses 1/10%).</summary>
public sealed record BonusParam(
    string Name, BonusParamKind Kind, EnumSource? Enum = null, string Default = "0", string? Hint = null, int Scale = 1);

/// <summary>A curated rAthena item-bonus definition: its category, family, name, a human label, a
/// plain-English description, and typed params.</summary>
public sealed record BonusDefinition(
    string Category, string Family, string Name, string Display, string Description, IReadOnlyList<BonusParam> Params)
{
    public string Search => $"{Name} {Display} {Description} {Category}".ToLowerInvariant();
}

/// <summary>
/// A curated, plain-English table of the most-used rAthena item bonuses with typed, enum-aware,
/// percentage-friendly parameters — enough for a non-developer to assemble an item's script visually.
/// (item_bonus.txt is prose, so the parameter shapes and descriptions are hand-modelled from it.)
/// </summary>
public static class BonusCatalog
{
    // ----- categories (display order) -----
    private const string CStats = "Core Stats";
    private const string CTrait = "Trait Stats (4th)";
    private const string CHpSp = "HP / SP";
    private const string CAtk = "Attack";
    private const string CDef = "Defense";
    private const string CAcc = "Accuracy & Crit";
    private const string CRace = "vs Race";
    private const string CEle = "vs Element";
    private const string CSize = "vs Size";
    private const string CClass = "vs Class";
    private const string CLeech = "Leech & Drain";
    private const string CAuto = "Auto-Cast";
    private const string CStatus = "Inflict Status";
    private const string CRegen = "Regen";
    private const string CSkill = "Skills & Casting";
    private const string CSurvival = "Survival & Flags";
    private const string CMisc = "Misc";

    public static readonly IReadOnlyList<string> Categories = new[]
    {
        CStats, CTrait, CHpSp, CAtk, CDef, CAcc, CRace, CEle, CSize, CClass,
        CLeech, CAuto, CStatus, CRegen, CSkill, CSurvival, CMisc,
    };

    // ----- enums -----
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

    public static readonly EnumSource Class = EnumSource.Labeled("Class",
        ("Class_Normal", "Normal monsters"), ("Class_Boss", "Boss monsters"),
        ("Class_Guardian", "Guardians"), ("Class_All", "All classes"));

    public static readonly EnumSource Status = EnumSource.Labeled("Status",
        ("Eff_Stun", "Stun"), ("Eff_Freeze", "Freeze"), ("Eff_Stone", "Stone Curse"), ("Eff_Sleep", "Sleep"),
        ("Eff_Curse", "Curse"), ("Eff_Silence", "Silence"), ("Eff_Blind", "Blind"), ("Eff_Poison", "Poison"),
        ("Eff_DPoison", "Deadly Poison"), ("Eff_Bleeding", "Bleeding"), ("Eff_Confusion", "Confusion"),
        ("Eff_Burning", "Burning"), ("Eff_Freezing", "Freezing"), ("Eff_Crystalize", "Crystalize"), ("Eff_Fear", "Fear"));

    // ----- param/definition helpers -----
    private static BonusParam Num(string name, string def = "10", string? hint = null, int scale = 1) =>
        new(name, BonusParamKind.Number, Default: def, Hint: hint, Scale: scale);
    private static BonusParam Pct(string name, string def = "5") =>
        new(name, BonusParamKind.Number, Default: def, Hint: "percent");
    private static BonusParam EnumP(string name, EnumSource src) =>
        new(name, BonusParamKind.Enum, src, src.Values.FirstOrDefault() ?? "");
    private static BonusParam Skill(string name = "skill") =>
        new(name, BonusParamKind.Skill, Default: "\"AL_HEAL\"", Hint: "pick a skill, or type its name in quotes");

    private static BonusDefinition B(string cat, string name, string display, string desc, params BonusParam[] p) =>
        new(cat, "bonus", name, display, desc, p);
    private static BonusDefinition B2(string cat, string name, string display, string desc, params BonusParam[] p) =>
        new(cat, "bonus2", name, display, desc, p);
    private static BonusDefinition B3(string cat, string name, string display, string desc, params BonusParam[] p) =>
        new(cat, "bonus3", name, display, desc, p);

    public static readonly IReadOnlyList<BonusDefinition> All = new List<BonusDefinition>
    {
        // ===== Core stats =====
        B(CStats, "bStr", "STR + n", "Raises Strength (melee damage, weight limit).", Num("amount")),
        B(CStats, "bAgi", "AGI + n", "Raises Agility (attack speed, flee).", Num("amount")),
        B(CStats, "bVit", "VIT + n", "Raises Vitality (HP, soft defence, status resist).", Num("amount")),
        B(CStats, "bInt", "INT + n", "Raises Intelligence (magic damage, SP).", Num("amount")),
        B(CStats, "bDex", "DEX + n", "Raises Dexterity (hit, cast time, ranged damage).", Num("amount")),
        B(CStats, "bLuk", "LUK + n", "Raises Luck (crit, perfect dodge, status resist).", Num("amount")),
        B(CStats, "bAllStats", "All stats + n", "Raises STR, AGI, VIT, INT, DEX and LUK together.", Num("amount", "1")),

        // ===== Trait stats (4th job) =====
        B(CTrait, "bPow", "POW + n", "Raises Power (4th-job trait — physical mastery).", Num("amount", "1")),
        B(CTrait, "bSta", "STA + n", "Raises Stamina (4th-job trait — resistance).", Num("amount", "1")),
        B(CTrait, "bWis", "WIS + n", "Raises Wisdom (4th-job trait — magic resistance).", Num("amount", "1")),
        B(CTrait, "bSpl", "SPL + n", "Raises Spell (4th-job trait — magic mastery).", Num("amount", "1")),
        B(CTrait, "bCon", "CON + n", "Raises Concentration (4th-job trait — accuracy/speed).", Num("amount", "1")),
        B(CTrait, "bCrt", "CRT + n", "Raises Creative (4th-job trait — crit/heal).", Num("amount", "1")),
        B(CTrait, "bAllTraitStats", "All traits + n", "Raises POW, STA, WIS, SPL, CON and CRT together.", Num("amount", "1")),

        // ===== HP / SP =====
        B(CHpSp, "bMaxHP", "Max HP + n", "Flat increase to maximum HP.", Num("amount", "100")),
        B(CHpSp, "bMaxSP", "Max SP + n", "Flat increase to maximum SP.", Num("amount", "50")),
        B(CHpSp, "bMaxHPrate", "Max HP + n%", "Increases maximum HP by a percentage.", Pct("percent")),
        B(CHpSp, "bMaxSPrate", "Max SP + n%", "Increases maximum SP by a percentage.", Pct("percent")),
        B(CHpSp, "bHPrecovRate", "HP recovery + n%", "Boosts natural HP regeneration.", Pct("percent")),
        B(CHpSp, "bSPrecovRate", "SP recovery + n%", "Boosts natural SP regeneration.", Pct("percent")),
        B(CHpSp, "bUseSPrate", "SP cost ± n%", "Changes SP consumed by skills (use a negative value to reduce).", Num("percent", "-10")),

        // ===== Attack =====
        B(CAtk, "bAtk", "ATK + n", "Flat physical attack power.", Num("amount")),
        B(CAtk, "bAtkRate", "ATK + n%", "Percentage physical attack power.", Pct("percent")),
        B(CAtk, "bBaseAtk", "Base ATK + n", "Adds to base attack power.", Num("amount")),
        B(CAtk, "bMatk", "MATK + n", "Flat magic attack power.", Num("amount")),
        B(CAtk, "bMatkRate", "MATK + n%", "Percentage magic attack power.", Pct("percent")),
        B(CAtk, "bWeaponAtkRate", "Weapon ATK + n%", "Increases the weapon's own ATK by a percentage.", Pct("percent")),
        B(CAtk, "bAtkRange", "Attack range + n", "Extends physical attack range (cells).", Num("amount", "1")),
        B(CAtk, "bCritAtkRate", "Critical damage + n%", "Increases the damage dealt by critical hits.", Pct("percent")),

        // ===== Defense =====
        B(CDef, "bDef", "DEF + n", "Flat equipment defence (reduces ranged/percentage damage).", Num("amount")),
        B(CDef, "bDef2Rate", "Soft DEF + n%", "Percentage VIT-based soft defence.", Pct("percent")),
        B(CDef, "bMdef", "MDEF + n", "Flat magic defence.", Num("amount")),
        B(CDef, "bRes", "RES + n", "Status resistance vs physical (4th-job stat).", Num("amount")),
        B(CDef, "bMRes", "MRES + n", "Status resistance vs magic (4th-job stat).", Num("amount")),
        B(CDef, "bNearAtkDef", "Resist melee + n%", "Reduces damage taken from melee attacks.", Pct("percent")),
        B(CDef, "bLongAtkDef", "Resist ranged + n%", "Reduces damage taken from ranged attacks.", Pct("percent")),
        B(CDef, "bMagicAtkDef", "Resist magic + n%", "Reduces damage taken from magic.", Pct("percent")),
        B(CDef, "bUnbreakableArmor", "Armor cannot break", "Protects the armor from breaking."),

        // ===== Accuracy & crit / speed =====
        B(CAcc, "bHit", "HIT + n", "Improves accuracy.", Num("amount")),
        B(CAcc, "bFlee", "FLEE + n", "Improves dodge against melee/ranged.", Num("amount")),
        B(CAcc, "bFlee2", "Perfect dodge + n", "Adds lucky-dodge chance (LUK based).", Num("amount", "1")),
        B(CAcc, "bCritical", "CRIT + n", "Adds critical rate (in tenths in-game, shows as +n).", Num("amount")),
        B(CAcc, "bCriticalRate", "Critical chance + n%", "Increases critical hit chance.", Pct("percent")),
        B(CAcc, "bAspd", "ASPD + n", "Flat attack-speed bonus (big effect — use small numbers).", Num("amount", "1")),
        B(CAcc, "bAspdRate", "ASPD + n%", "Percentage attack speed.", Pct("percent")),
        B(CAcc, "bSpeedRate", "Move speed + n%", "Increases movement speed (highest one applies).", Num("percent", "25")),

        // ===== vs Race =====
        B2(CRace, "bAddRace", "+n% phys. damage vs race", "More physical damage to the chosen monster race.", EnumP("race", Race), Pct("percent")),
        B2(CRace, "bMagicAddRace", "+n% magic damage vs race", "More magic damage to the chosen race.", EnumP("race", Race), Pct("percent")),
        B2(CRace, "bSubRace", "-n% damage from race", "Reduces damage taken from the chosen race.", EnumP("race", Race), Pct("percent")),
        B2(CRace, "bCriticalAddRace", "+n crit vs race", "Extra critical rate against a race.", EnumP("race", Race), Num("amount")),
        B2(CRace, "bIgnoreDefRaceRate", "Ignore n% DEF of race", "Pierces a percentage of the race's defence.", EnumP("race", Race), Pct("percent")),
        B2(CRace, "bExpAddRace", "+n% EXP from race", "Bonus experience when killing the race.", EnumP("race", Race), Pct("percent")),
        B2(CRace, "bHPDrainValueRace", "Heal n HP hitting race", "Restores flat HP per normal hit on the race.", EnumP("race", Race), Num("amount", "5")),

        // ===== vs Element =====
        B2(CEle, "bAddEle", "+n% phys. damage vs element", "More physical damage to a defensive element.", EnumP("element", Element), Pct("percent")),
        B2(CEle, "bMagicAddEle", "+n% magic damage vs element", "More magic damage to an element.", EnumP("element", Element), Pct("percent")),
        B2(CEle, "bSubEle", "-n% damage from element", "Reduces damage taken from an element.", EnumP("element", Element), Pct("percent")),

        // ===== vs Size =====
        B2(CSize, "bAddSize", "+n% phys. damage vs size", "More physical damage to a monster size.", EnumP("size", Size), Pct("percent")),
        B2(CSize, "bMagicAddSize", "+n% magic damage vs size", "More magic damage to a size.", EnumP("size", Size), Pct("percent")),
        B2(CSize, "bSubSize", "-n% damage from size", "Reduces damage taken from a size.", EnumP("size", Size), Pct("percent")),

        // ===== vs Class =====
        B2(CClass, "bAddClass", "+n% phys. damage vs class", "More physical damage vs Normal/Boss/etc.", EnumP("class", Class), Pct("percent")),
        B2(CClass, "bMagicAddClass", "+n% magic damage vs class", "More magic damage vs a class.", EnumP("class", Class), Pct("percent")),
        B2(CClass, "bSubClass", "-n% damage from class", "Reduces damage taken from a class.", EnumP("class", Class), Pct("percent")),

        // ===== Leech & drain (chance is in 1/10%, modelled as a real %) =====
        B2(CLeech, "bHPDrainRate", "Leech HP on hit", "Chance to recover a % of damage dealt as HP.",
            Num("chance", "5", "trigger chance %", scale: 10), Pct("percent")),
        B2(CLeech, "bSPDrainRate", "Leech SP on hit", "Chance to recover a % of damage dealt as SP.",
            Num("chance", "5", "trigger chance %", scale: 10), Pct("percent")),
        B(CLeech, "bHPDrainValue", "Heal n HP per hit", "Restores flat HP on every normal attack.", Num("amount", "5")),
        B(CLeech, "bSPDrainValue", "Restore n SP per hit", "Restores flat SP on every normal attack.", Num("amount", "1")),

        // ===== Auto-cast (chance is in 1/10%, modelled as a real %) =====
        B3(CAuto, "bAutoSpell", "Auto-cast skill when attacking", "Chance to cast a skill when you attack.",
            Skill(), Num("level", "1", "skill level"), Num("chance", "5", "trigger chance %", scale: 10)),
        B3(CAuto, "bAutoSpellWhenHit", "Auto-cast skill when hit", "Chance to cast a skill when you are hit.",
            Skill(), Num("level", "1", "skill level"), Num("chance", "5", "trigger chance %", scale: 10)),

        // ===== Inflict status (chance is in 1/100%, modelled as a real %) =====
        B2(CStatus, "bAddEff", "Inflict status when attacking", "Chance to inflict a status on the target when you attack.",
            EnumP("status", Status), Num("chance", "5", "trigger chance %", scale: 100)),
        B2(CStatus, "bAddEff2", "Inflict status on self", "Chance to inflict a status on yourself when attacking (e.g. cursed gear).",
            EnumP("status", Status), Num("chance", "5", "trigger chance %", scale: 100)),
        B2(CStatus, "bAddEffWhenHit", "Inflict status when hit", "Chance to inflict a status on the attacker when you are hit.",
            EnumP("status", Status), Num("chance", "5", "trigger chance %", scale: 100)),
        B2(CStatus, "bComaRace", "Coma chance vs race", "Chance to instantly drop a race's HP to 1 when attacking.",
            EnumP("race", Race), Num("chance", "1", "trigger chance %", scale: 100)),

        // ===== Regen =====
        B2(CRegen, "bHPRegenRate", "Gain n HP every t ms", "Periodic flat HP recovery.", Num("amount", "10"), Num("interval_ms", "10000")),
        B2(CRegen, "bSPRegenRate", "Gain n SP every t ms", "Periodic flat SP recovery.", Num("amount", "5"), Num("interval_ms", "10000")),

        // ===== Skills & casting =====
        B2(CSkill, "bSkillAtk", "Skill damage + n%", "Increases damage of one specific skill.", Skill(), Pct("percent")),
        B2(CSkill, "bSkillUseSP", "Skill SP cost - n", "Reduces SP cost of one skill by a flat amount.", Skill(), Num("amount", "5")),
        B(CSkill, "bHealPower", "Healing done + n%", "Increases the heal amount of your healing skills.", Pct("percent")),
        B(CSkill, "bAddItemHealRate", "Healing items + n%", "Increases HP recovered from healing items.", Pct("percent")),
        B(CSkill, "bVariableCastrate", "Variable cast ± n%", "Changes variable cast time (negative = faster).", Num("percent", "-10")),
        B(CSkill, "bFixedCastrate", "Fixed cast ± n%", "Changes fixed cast time (negative = faster).", Num("percent", "-10")),
        B(CSkill, "bNoCastCancel", "Cast not interrupted", "Your casting is not cancelled when you are hit."),

        // ===== Survival & flags =====
        B(CSurvival, "bNoKnockback", "Immune to knockback", "You cannot be pushed back."),
        B(CSurvival, "bPerfectHide", "Perfect hide", "Hidden even from Insect/Demon monsters."),
        B(CSurvival, "bUnbreakableWeapon", "Weapon cannot break", "Protects the weapon from breaking."),
        B(CSurvival, "bUnbreakableHelm", "Headgear cannot break", "Protects the headgear from breaking."),
        B(CSurvival, "bUnbreakableShield", "Shield cannot break", "Protects the shield from breaking."),
        B(CSurvival, "bRestartFullRecover", "Full HP/SP on revive", "When you resurrect, HP and SP are fully restored."),

        // ===== Misc =====
        B(CMisc, "bAddMaxWeight", "Weight limit + n", "Increases carry weight (in units of 0.1).", Num("amount", "2000")),
        B2(CMisc, "bGetZenyNum", "Zeny on kill", "Chance to gain 1~x zeny when killing a monster.", Num("max_zeny", "100"), Pct("chance_pct")),
        B2(CMisc, "bDropAddRace", "+n% drop rate vs race", "Increases item drop rate from a race.", EnumP("race", Race), Pct("percent")),
    };

    /// <summary>Formats a complete bonus statement, applying each param's display scale
    /// (e.g. a 5% auto-cast chance becomes <c>50</c>): <c>bonus3 bAutoSpell,"MG_FIREBOLT",1,50;</c>.</summary>
    public static string Format(BonusDefinition def, IReadOnlyList<string> values)
    {
        var sb = new StringBuilder(def.Family).Append(' ').Append(def.Name);
        for (int i = 0; i < def.Params.Count; i++)
        {
            var p = def.Params[i];
            string raw = i < values.Count && !string.IsNullOrWhiteSpace(values[i]) ? values[i].Trim() : p.Default;
            if (p.Kind == BonusParamKind.Number && p.Scale != 1 && int.TryParse(raw, out var n))
                raw = (n * p.Scale).ToString();
            sb.Append(',').Append(raw);
        }
        return sb.Append(';').ToString();
    }
}
