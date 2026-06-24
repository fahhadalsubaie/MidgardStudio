namespace MidgardStudio.Core.Lua;

/// <summary>One client itemInfo entry (the data the player sees: names, descriptions, slots, view).</summary>
public sealed class ItemInfoEntry
{
    public int Id { get; set; }
    public string UnidentifiedDisplayName { get; set; } = string.Empty;
    public string UnidentifiedResourceName { get; set; } = string.Empty;
    public List<string> UnidentifiedDescription { get; set; } = new();
    public string IdentifiedDisplayName { get; set; } = string.Empty;
    public string IdentifiedResourceName { get; set; } = string.Empty;
    public List<string> IdentifiedDescription { get; set; } = new();
    public int SlotCount { get; set; }
    public int ClassNum { get; set; }
    public bool Costume { get; set; }

    // Optional fields seen in some clients.
    public int? EffectId { get; set; }
    public int? PackageId { get; set; }
    public string? Server { get; set; }

    /// <summary>A deep copy (so a base entry can be edited without mutating the cached base table).</summary>
    public ItemInfoEntry Clone() => new()
    {
        Id = Id,
        UnidentifiedDisplayName = UnidentifiedDisplayName,
        UnidentifiedResourceName = UnidentifiedResourceName,
        UnidentifiedDescription = new List<string>(UnidentifiedDescription),
        IdentifiedDisplayName = IdentifiedDisplayName,
        IdentifiedResourceName = IdentifiedResourceName,
        IdentifiedDescription = new List<string>(IdentifiedDescription),
        SlotCount = SlotCount,
        ClassNum = ClassNum,
        Costume = Costume,
        EffectId = EffectId,
        PackageId = PackageId,
        Server = Server,
    };
}

/// <summary>The editable client item tables: new customs and official overrides.</summary>
public sealed class ItemInfoFile
{
    public Dictionary<int, ItemInfoEntry> Custom { get; } = new();
    public Dictionary<int, ItemInfoEntry> Override { get; } = new();
}
