namespace MidgardStudio.Core.Schema;

/// <summary>
/// The shape of a database field. Drives both YAML (de)serialization and the generated UI editor.
/// </summary>
public enum FieldKind
{
    Int,
    Long,
    String,
    Bool,
    Enum,        // single value drawn from an EnumSource
    Flags,       // YAML map&lt;string,bool&gt; over a small known set (e.g. mob Modes, Stack flags)
    BoolMap,     // YAML map&lt;string,bool&gt; over a large/open set (Jobs, Classes, Locations)
    Object,      // nested fixed-shape object (Flags, Trade, Delay, Stack, NoUse)
    ObjectList,  // list of objects (mob Drops, item_group SubGroups, mob_summon Summon, ...)
    Script,      // literal block scalar emitted as `Field: |`
    ScalarList,  // inline list of scalars (item_combos Combo members)
    Reference,   // cross-database reference (AegisName, etc.); rendered as autocomplete
    LevelInt,    // dual-typed: a single int OR a per-skill-level array (skill_db Range/CastTime/HpCost/…)
}
