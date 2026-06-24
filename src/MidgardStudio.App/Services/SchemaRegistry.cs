using MidgardStudio.Core.Schema;
using MidgardStudio.Core.Schemas;

namespace MidgardStudio.App.Services;

/// <summary>Maps database ids to their schemas. Schemas are registered here as they come online.</summary>
public sealed class SchemaRegistry
{
    private readonly Dictionary<string, DbSchema> _byId = new(StringComparer.Ordinal);

    public SchemaRegistry()
    {
        Register(ItemDbSchema.Instance);
        Register(MobDbSchema.Instance);
        Register(MobAvailSchema.Instance);
        Register(PetDbSchema.Instance);
        Register(ItemComboSchema.Instance);
        Register(ItemGroupSchema.Instance);
        Register(SkillDbSchema.Instance);
        Register(AchievementDbSchema.Instance);
        Register(AbraDbSchema.Instance);
        Register(MobSummonSchema.Instance);
    }

    public void Register(DbSchema schema) => _byId[schema.Id] = schema;

    public DbSchema? Get(string id) => _byId.GetValueOrDefault(id);

    public bool Has(string id) => _byId.ContainsKey(id);

    public IReadOnlyCollection<DbSchema> All => _byId.Values;
}
