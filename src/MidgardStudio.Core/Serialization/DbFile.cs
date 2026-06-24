using MidgardStudio.Core.Model;

namespace MidgardStudio.Core.Serialization;

/// <summary>
/// An in-memory representation of one rAthena YAML database file: the Header (Type + Version)
/// and the Body records. Import files have no Footer, so it is not modeled here.
/// </summary>
public sealed class DbFile
{
    public string HeaderType { get; set; } = string.Empty;

    public int HeaderVersion { get; set; }

    public List<DbRecord> Records { get; } = new();
}
