namespace MidgardStudio.Core.Lookup;

/// <summary>Supplies autocomplete candidates for cross-database reference fields (e.g. item AegisNames).</summary>
public interface IReferenceResolver
{
    IReadOnlyList<string> Search(string referenceDb, string query, int limit = 40);
}
