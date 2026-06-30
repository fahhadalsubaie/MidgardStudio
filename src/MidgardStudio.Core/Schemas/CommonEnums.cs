using MidgardStudio.Core.Schema;

namespace MidgardStudio.Core.Schemas;

/// <summary>Predefined value sets shared by the mob/skill/achievement/group schemas.</summary>
public static class CommonEnums
{
    public static readonly EnumSource MobSize = EnumSource.Static("MobSize", "Small", "Medium", "Large");

    // The two player races are RC_PLAYER_HUMAN / RC_PLAYER_DORAM — there is no RC_PLAYER, so a bare
    // "Player" fails the server's "RC_"+value lookup and silently defaults the mob to Formless.
    public static readonly EnumSource MobRace = EnumSource.Static("MobRace",
        "Formless", "Undead", "Brute", "Plant", "Insect", "Fish", "Demon",
        "Demihuman", "Angel", "Dragon", "Player_Human", "Player_Doram");

    public static readonly EnumSource Element = EnumSource.Static("Element",
        "Neutral", "Water", "Earth", "Fire", "Wind", "Poison", "Holy", "Dark", "Ghost", "Undead");

    public static readonly EnumSource MobClass = EnumSource.Static("MobClass",
        "Normal", "Boss", "Guardian", "Battlefield", "Event");

    // Token names and order mirror the exported MD_* constants. "NoRandomWalk" (MD_NORANDOMWALK) — NOT
    // "NoRandom" (no such constant) — and "NoCast" (MD_NOCAST) must both be present, or the form can't set
    // them and a misspelled flag is dropped on server load.
    public static readonly EnumSource MobModes = EnumSource.Static("MobModes",
        "CanMove", "Looter", "Aggressive", "Assist", "CastSensorIdle", "NoRandomWalk", "NoCast",
        "CanAttack", "CastSensorChase", "ChangeChase", "Angry", "ChangeTargetMelee",
        "ChangeTargetChase", "TargetWeak", "RandomTarget", "IgnoreMelee", "IgnoreMagic",
        "IgnoreRanged", "Mvp", "IgnoreMisc", "KnockbackImmune", "TeleportBlock",
        "FixedItemDrop", "Detector", "StatusImmune", "SkillImmune");

    // The 40 valid (non-deprecated) RC2_* groups, in exported-constant order. "Guardian"/"Battlefield" are
    // deprecated (mapped to CLASS_GUARDIAN/CLASS_BATTLEFIELD, set via the Class field) and are no longer
    // exported, so they're excluded — ticking them would emit a key the server rejects. Tokens match the
    // casing real mob_db.yml uses (e.g. Bio5_Mvp, Glast_Heim_Abyss).
    public static readonly EnumSource RaceGroups = EnumSource.Static("RaceGroups",
        "Goblin", "Kobold", "Orc", "Golem", "Ninja", "Gvg", "Treasure", "Biolab", "Manuk",
        "Splendide", "Scaraba", "Ogh_Atk_Def", "Ogh_Hidden", "Bio5_Swordman_Thief",
        "Bio5_Acolyte_Merchant", "Bio5_Mage_Archer", "Bio5_Mvp", "Clocktower", "Thanatos",
        "Faceworm", "Hearthunter", "Rockridge", "Werner_Lab", "Temple_Demon", "Illusion_Vampire",
        "Malangdo", "Ep172Alpha", "Ep172Beta", "Ep172Bath", "Illusion_Turtle", "Rachel_Sanctuary",
        "Illusion_Luanda", "Illusion_Frozen", "Illusion_Moonlight", "Ep16_Def", "Edda_Arunafeltz",
        "Lasagna", "Glast_Heim_Abyss", "Destroyed_Valkyrie_Realm", "Encroached_Gephenia");

    public static readonly EnumSource AchievementGroup = EnumSource.Static("AchievementGroup",
        "None", "Add_Friend", "Adventure", "Baby", "Battle", "Chatting", "Chatting_Count",
        "Chatting_Create", "Chatting_Dying", "Eat", "Get_Item", "Get_Zeny", "Goal_Achieve",
        "Goal_Level", "Goal_Status", "Job_Change", "Kill_Mob_Class", "Kill_Pc", "Marry",
        "Party", "Refine_Fail", "Refine_Success", "Taming");

    public static readonly EnumSource SkillType = EnumSource.Static("SkillType",
        "None", "Weapon", "Magic", "Misc");

    public static readonly EnumSource SkillTargetType = EnumSource.Static("SkillTargetType",
        "Passive", "Attack", "Ground", "Self", "Support", "Trap");

    public static readonly EnumSource SkillHit = EnumSource.Static("SkillHit",
        "Normal", "Single", "Multi_Hit");

    public static readonly EnumSource SkillElement = EnumSource.Static("SkillElement",
        "Neutral", "Water", "Earth", "Fire", "Wind", "Poison", "Holy", "Dark", "Ghost", "Undead",
        "Weapon", "Endowed", "Random");

    public static readonly EnumSource SkillFlags = EnumSource.Static("SkillFlags",
        "IsQuest", "IsEnsemble", "IsTrap", "TargetSelf", "NoTargetSelf", "PartyOnly", "GuildOnly",
        "NoTargetEnemy", "IsSong", "IsChorus", "IsSpirit", "IsGuild", "IsWedding", "IsNpc",
        "IgnoreLandProtector", "IgnoreGtb", "IgnoreAutoGuard", "IgnoreCicada", "IgnoreHovering",
        "IgnoreKagehumi", "IgnoreWugBite", "AllowWhenHidden", "AllowWhenPerforming", "AllowOnWarg",
        "AllowOnMado", "DisableNearNpc", "TargetTrap", "TargetEmperium", "TargetHidden", "TargetManHole",
        "IgnoreBgReduction", "IgnoreGvgReduction", "IgnoreNonCritAtkBonus", "IncreaseDanceWithWugDamage",
        "ShowScale", "Toggleable", "IsAutoShadowSpell", "AlterRangeVulture", "AlterRangeSnakeEye",
        "AlterRangeShadowJump", "AlterRangeRadius", "AlterRangeResearchTrap");

    public static readonly EnumSource SkillDamageFlags = EnumSource.Static("SkillDamageFlags",
        "NoDamage", "Splash", "SplashSplit", "IgnoreAtkCard", "IgnoreElement", "IgnoreDefense",
        "IgnoreFlee", "IgnoreDefCard", "Critical", "IgnoreLongCard", "SimpleDefense");

    public static readonly EnumSource SkillCastFlags = EnumSource.Static("SkillCastFlags",
        "IgnoreDex", "IgnoreStatus", "IgnoreItemBonus");

    public static readonly EnumSource SkillCopySkill = EnumSource.Static("SkillCopySkill",
        "Plagiarism", "Reproduce");

    /// <summary>Requirement types a copied skill can drop (CopyFlags.RemoveRequirement).</summary>
    public static readonly EnumSource SkillRemoveRequirement = EnumSource.Static("SkillRemoveRequirement",
        "HpCost", "SpCost", "ApCost", "HpRateCost", "SpRateCost", "ApRateCost", "MaxHpTrigger",
        "ZenyCost", "Weapon", "Ammo", "State", "Status", "SpiritSphereCost", "ItemCost", "Equipment");

    /// <summary>NPC types that block casting when near (NoNearNPC.Type).</summary>
    public static readonly EnumSource SkillNoNearNpc = EnumSource.Static("SkillNoNearNpc",
        "WarpPortal", "Shop", "NPC", "Tomb");

    /// <summary>Required player state to cast (Requires.State).</summary>
    public static readonly EnumSource SkillState = EnumSource.Static("SkillState",
        "None", "Cart", "Falcon", "Peco", "Riding", "Ridingdragon", "Ridingwug", "Wug", "Mado",
        "Water", "Elementalspirit", "Elementalspirit2", "Sunstance", "Moonstance", "Starstance",
        "Universestance", "MoveEnable", "Shield", "Hidden", "RecoverWeightRate");

    /// <summary>Weapon types a skill can require (Requires.Weapon map; "All" clears the requirement).</summary>
    public static readonly EnumSource SkillWeapon = EnumSource.Static("SkillWeapon",
        "All", "1hSword", "2hSword", "1hSpear", "2hSpear", "1hAxe", "2hAxe", "Mace", "2hMace",
        "Dagger", "Bow", "Cbow", "Staff", "Knuckle", "Musical", "Whip", "Book", "Katar", "Revolver",
        "Rifle", "Gatling", "Shotgun", "Grenade", "Huuma", "Fist", "Shield",
        "Double_DD", "Double_SS", "Double_AA", "Double_DS", "Double_DA", "Double_SA");

    /// <summary>Ammo types a skill can require (Requires.Ammo map; "None" clears the requirement).</summary>
    public static readonly EnumSource SkillAmmo = EnumSource.Static("SkillAmmo",
        "None", "Arrow", "Dagger", "Bullet", "Shell", "Grenade", "Shuriken", "Kunai",
        "Cannonball", "Throwweapon");

    /// <summary>Who a ground skill unit affects (Unit.Target).</summary>
    public static readonly EnumSource SkillUnitTarget = EnumSource.Static("SkillUnitTarget",
        "All", "Ally", "Enemy", "Friend", "Guild", "GuildAlly", "Neutral", "NoEnemy", "NoGuild",
        "NoParty", "Party", "SameGuild", "Self", "WoS");

    /// <summary>Behaviour flags for a ground skill unit (Unit.Flag).</summary>
    public static readonly EnumSource SkillUnitFlag = EnumSource.Static("SkillUnitFlag",
        "NoEnemy", "NoReiteration", "NoFootSet", "NoOverlap", "PathCheck", "NoPc", "NoMob", "Skill",
        "Dance", "Ensemble", "Song", "DualMode", "NoKnockback", "RangedSingleUnit", "CrazyWeedImmune",
        "RemovedByFireRain", "KnockbackGroup", "HiddenTrap");
}
