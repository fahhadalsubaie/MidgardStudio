using Wpf.Ui.Controls;

namespace MidgardStudio.App.ViewModels;

/// <summary>
/// A navigable database section in the shell. In Phase 0 this only carries display metadata;
/// later phases attach the real list/detail editor view models.
/// </summary>
public sealed class DbSectionViewModel
{
    public DbSectionViewModel(string key, string title, SymbolRegular icon, string description, string filePath,
        string? schemaId = null, string category = "Server Databases")
    {
        Key = key;
        Title = title;
        Icon = icon;
        Description = description;
        FilePath = filePath;
        SchemaId = schemaId;
        Category = category;
    }

    public string Key { get; }
    public string Title { get; }
    public SymbolRegular Icon { get; }
    public string Description { get; }

    /// <summary>The nav group this section belongs to (Server Databases / Client / Tools). Drives the
    /// collapsible category grouping in the side nav so it scales as editors are added.</summary>
    public string Category { get; }

    /// <summary>Representative file this section will edit (shown in the placeholder).</summary>
    public string FilePath { get; }

    /// <summary>Schema id for an editable section (null = placeholder until its phase lands).</summary>
    public string? SchemaId { get; }
}
