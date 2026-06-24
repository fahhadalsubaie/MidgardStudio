using MidgardStudio.Core.Model;
using MidgardStudio.Core.Overlay;
using MidgardStudio.Core.Schemas;
using MidgardStudio.Core.Workspace;

namespace MidgardStudio.Tests;

public class ModeSetPortTests
{
    private static DbRecord ReItem(int id, int magicAttack)
    {
        var r = new DbRecord(ItemDbSchema.Instance);
        r.SetRaw("Id", id);
        r.SetRaw("AegisName", "RE" + id);
        r.SetRaw("Name", "RE " + id);
        r.SetRaw("Type", "Weapon");
        if (magicAttack > 0) r.SetRaw("MagicAttack", magicAttack);
        return r;
    }

    [Fact]
    public void Port_copies_renewal_into_shared_import_and_flags_renewal_only_fields()
    {
        var schema = ItemDbSchema.Instance;
        var reBase = new DbLayer();
        reBase.Add(ReItem(7000, 30));
        var import = new DbLayer();
        var renewal = new OverlayTable(schema, reBase, import, "x.yml");
        var preRenewal = new OverlayTable(schema, new DbLayer(), import, "x.yml");
        var set = new ModeSet(renewal, preRenewal, import);

        var key = RecordKey.Of(7000L);
        Assert.Null(preRenewal.GetEffective(key)); // not served in pre-re yet

        var report = RenewalPortService.PortToPreRenewal(set, key);

        Assert.True(report.Ported);
        Assert.Contains(report.Notes, n => n.Field == "MagicAttack"); // renewal-only flagged
        Assert.NotNull(preRenewal.GetEffective(key));                  // now served via shared import
        Assert.Equal(30, preRenewal.GetEffective(key)!.GetInt("MagicAttack"));
    }

    [Fact]
    public void ModeSet_For_returns_the_matching_overlay()
    {
        var schema = ItemDbSchema.Instance;
        var import = new DbLayer();
        var renewal = new OverlayTable(schema, new DbLayer(), import, "x.yml");
        var preRenewal = new OverlayTable(schema, new DbLayer(), import, "x.yml");
        var set = new ModeSet(renewal, preRenewal, import);

        Assert.Same(renewal, set.For(ServerMode.Renewal));
        Assert.Same(preRenewal, set.For(ServerMode.PreRenewal));
    }
}
