using MidgardStudio.Core.Model;

namespace MidgardStudio.App.ViewModels;

/// <summary>One command-palette hit: a record in a database, with how to navigate to it.</summary>
public sealed class PaletteResultViewModel
{
    public PaletteResultViewModel(string schemaId, string database, RecordKey key, string display)
    {
        SchemaId = schemaId;
        Database = database;
        Key = key;
        Display = display;
    }

    public string SchemaId { get; }
    public string Database { get; }
    public RecordKey Key { get; }
    public string Display { get; }
}
