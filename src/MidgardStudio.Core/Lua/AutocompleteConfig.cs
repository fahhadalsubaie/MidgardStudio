namespace MidgardStudio.Core.Lua;

/// <summary>
/// User-tunable options for the client-item <b>Autocomplete</b> generator (persisted inside app settings).
/// Controls which lines are produced, the colors used, per-field label wording, and the default texts.
/// Colors are stored as bare <c>RRGGBB</c> hex (no leading <c>^</c>).
/// </summary>
public sealed class AutocompleteConfig
{
    // ---- Which lines to include ----
    public bool IncludeHeal { get; set; } = true;          // Heal/Restore/Cure (usables, from script)
    public bool IncludeScriptBonuses { get; set; } = true; // stat/ATK/DEF/HP… bonus lines parsed from script
    public bool IncludeElement { get; set; } = true;       // weapon element (from script bAtkEle)
    public bool IncludeClass { get; set; } = true;         // "Class: One-Handed Sword"
    public bool IncludeAttack { get; set; } = true;
    public bool IncludeMagicAttack { get; set; } = true;
    public bool IncludeDefense { get; set; } = true;
    public bool IncludeWeaponLevel { get; set; } = true;
    public bool IncludeRequiredLevel { get; set; } = true;
    public bool IncludeJobs { get; set; } = true;
    public bool IncludePosition { get; set; } = true;      // headgear slot(s)
    public bool IncludeWeight { get; set; } = true;
    public bool AlwaysShowWeight { get; set; }             // show "Weight: 0" even at 0 (the Item Forge forces this)
    public bool IncludeSlots { get; set; }                 // off by default (RO shows slots in the name)
    public bool IncludeRefineable { get; set; } = true;    // shows "Unrefineable" when not refineable
    public bool IncludeCompoundOn { get; set; } = true;    // cards
    public bool IncludeDividers { get; set; } = true;      // the ^FFFFFF_^000000 separator lines
    public bool UseColors { get; set; } = true;            // master switch for the ^RRGGBB color codes

    // ---- Behaviour ----
    /// <summary>Replace existing identified name/description; when false, only fills blanks.</summary>
    public bool OverwriteExisting { get; set; } = true;

    /// <summary>Default text written to the unidentified description on autocomplete (blank = leave it).</summary>
    public string DefaultUnidentifiedDescription { get; set; } = string.Empty;

    /// <summary>Text used when an included field has no server value (blank = omit that line).</summary>
    public string MissingValueText { get; set; } = string.Empty;

    // ---- Semantic colors (RRGGBB) ----
    public string ValueColor { get; set; } = "009900";   // numbers: Heal, Weight, levels
    public string LabelColor { get; set; } = "6666CC";   // Class/Jobs/Position value text
    public string AttackColor { get; set; } = "CC0000";  // ATK value
    public string DefenseColor { get; set; } = "0000FF"; // DEF value
    public string SkillColor { get; set; } = "008800";   // skill names in effect lines

    /// <summary>Element name (Fire, Water, …) → hex color used on the "Element:" line.</summary>
    public Dictionary<string, string> ElementColors { get; set; } = DefaultElementColors();

    /// <summary>Per-field label overrides. Key is the canonical field key (see <see cref="LabelKeys"/>).</summary>
    public Dictionary<string, string> Labels { get; set; } = new(StringComparer.Ordinal);

    /// <summary>A deep copy (independent Labels/ElementColors) so a caller can tweak it without touching settings.</summary>
    public AutocompleteConfig Clone()
    {
        var c = (AutocompleteConfig)MemberwiseClone();
        c.Labels = new Dictionary<string, string>(Labels, StringComparer.Ordinal);
        c.ElementColors = new Dictionary<string, string>(ElementColors, StringComparer.Ordinal);
        return c;
    }

    /// <summary>Resolves a label, honoring a user override.</summary>
    public string Label(string key, string fallback) =>
        Labels.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;

    /// <summary>The hex color for an element (falls back to neutral gray).</summary>
    public string ElementColor(string element) =>
        ElementColors.TryGetValue(element, out var v) && !string.IsNullOrWhiteSpace(v) ? v : "777777";

    public static Dictionary<string, string> DefaultElementColors() => new(StringComparer.Ordinal)
    {
        ["Neutral"] = "777777",
        ["Water"] = "0000BB",
        ["Earth"] = "996600",
        ["Fire"] = "FF0000",
        ["Wind"] = "33CC00",
        ["Poison"] = "663399",
        ["Holy"] = "DDAA00",
        ["Dark"] = "660099",
        ["Ghost"] = "6666CC",
        ["Undead"] = "556B2F",
    };

    /// <summary>The canonical field keys + their default labels (drives the settings UI and overrides).</summary>
    public static readonly (string Key, string DefaultLabel)[] LabelKeys =
    {
        ("Class", "Class"),
        ("Heal", "Heal"),
        ("Restore", "Restore"),
        ("Cure", "Cure"),
        ("Attack", "Attack"),
        ("MagicAttack", "Magic Attack"),
        ("Defense", "Defense"),
        ("Element", "Element"),
        ("Position", "Position"),
        ("Weight", "Weight"),
        ("WeaponLevel", "Weapon Level"),
        ("ArmorLevel", "Armor Level"),
        ("RequiredLevel", "Level Requirement"),
        ("Slots", "Slots"),
        ("Jobs", "Jobs"),
        ("CompoundOn", "Compound on"),
    };

    /// <summary>The toggle keys + display labels (drives the settings UI).</summary>
    public static readonly (string Key, string Display)[] ToggleKeys =
    {
        ("IncludeHeal", "Healing / cure (from script)"),
        ("IncludeScriptBonuses", "Stat & effect bonuses (from script)"),
        ("IncludeElement", "Weapon element (from script)"),
        ("IncludeClass", "Class line"),
        ("IncludeAttack", "Attack"),
        ("IncludeMagicAttack", "Magic Attack"),
        ("IncludeDefense", "Defense"),
        ("IncludeWeaponLevel", "Weapon / armor level"),
        ("IncludeRequiredLevel", "Required level"),
        ("IncludeJobs", "Applicable jobs"),
        ("IncludePosition", "Headgear position"),
        ("IncludeWeight", "Weight"),
        ("IncludeSlots", "Slots"),
        ("IncludeRefineable", "Unrefineable note"),
        ("IncludeCompoundOn", "Card “Compound on”"),
        ("IncludeDividers", "Divider lines"),
        ("UseColors", "Use color codes"),
    };

    public bool GetToggle(string key) => key switch
    {
        nameof(IncludeHeal) => IncludeHeal,
        nameof(IncludeScriptBonuses) => IncludeScriptBonuses,
        nameof(IncludeElement) => IncludeElement,
        nameof(IncludeClass) => IncludeClass,
        nameof(IncludeAttack) => IncludeAttack,
        nameof(IncludeMagicAttack) => IncludeMagicAttack,
        nameof(IncludeDefense) => IncludeDefense,
        nameof(IncludeWeaponLevel) => IncludeWeaponLevel,
        nameof(IncludeRequiredLevel) => IncludeRequiredLevel,
        nameof(IncludeJobs) => IncludeJobs,
        nameof(IncludePosition) => IncludePosition,
        nameof(IncludeWeight) => IncludeWeight,
        nameof(IncludeSlots) => IncludeSlots,
        nameof(IncludeRefineable) => IncludeRefineable,
        nameof(IncludeCompoundOn) => IncludeCompoundOn,
        nameof(IncludeDividers) => IncludeDividers,
        nameof(UseColors) => UseColors,
        _ => false,
    };

    public void SetToggle(string key, bool value)
    {
        switch (key)
        {
            case nameof(IncludeHeal): IncludeHeal = value; break;
            case nameof(IncludeScriptBonuses): IncludeScriptBonuses = value; break;
            case nameof(IncludeElement): IncludeElement = value; break;
            case nameof(IncludeClass): IncludeClass = value; break;
            case nameof(IncludeAttack): IncludeAttack = value; break;
            case nameof(IncludeMagicAttack): IncludeMagicAttack = value; break;
            case nameof(IncludeDefense): IncludeDefense = value; break;
            case nameof(IncludeWeaponLevel): IncludeWeaponLevel = value; break;
            case nameof(IncludeRequiredLevel): IncludeRequiredLevel = value; break;
            case nameof(IncludeJobs): IncludeJobs = value; break;
            case nameof(IncludePosition): IncludePosition = value; break;
            case nameof(IncludeWeight): IncludeWeight = value; break;
            case nameof(IncludeSlots): IncludeSlots = value; break;
            case nameof(IncludeRefineable): IncludeRefineable = value; break;
            case nameof(IncludeCompoundOn): IncludeCompoundOn = value; break;
            case nameof(IncludeDividers): IncludeDividers = value; break;
            case nameof(UseColors): UseColors = value; break;
        }
    }
}
