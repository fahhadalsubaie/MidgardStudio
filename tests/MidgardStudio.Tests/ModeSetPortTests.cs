using System;
using System.Collections.Generic;
using System.IO;
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

    [Fact]
    public void Lazy_ModeSet_builds_only_the_active_mode_until_the_other_is_requested()
    {
        var schema = ItemDbSchema.Instance;
        var import = new DbLayer();
        var built = new List<ServerMode>();
        OverlayTable Build(ServerMode m) { built.Add(m); return new OverlayTable(schema, new DbLayer(), import, "x.yml"); }

        var set = new ModeSet(ServerMode.Renewal, Build, import, "x.yml");
        Assert.Equal(new[] { ServerMode.Renewal }, built);    // only the active base parsed up front

        set.For(ServerMode.Renewal);                          // cached — no rebuild
        Assert.Single(built);

        var pre = set.For(ServerMode.PreRenewal);             // the other mode is built on demand
        Assert.Equal(new[] { ServerMode.Renewal, ServerMode.PreRenewal }, built);
        Assert.Same(pre, set.For(ServerMode.PreRenewal));     // now cached
        Assert.Equal(2, built.Count);
    }

    [Fact]
    public void Lazy_ModeSet_ImportFilePath_IsDirty_and_Save_never_build_the_inactive_mode()
    {
        var schema = ItemDbSchema.Instance;
        var import = new DbLayer();
        import.Add(ReItem(9100, 0));                           // one custom record so Save has something to write
        int builds = 0;
        string importPath = Path.Combine(Path.GetTempPath(), "midgard-lazy-" + Guid.NewGuid().ToString("N") + ".yml");
        OverlayTable Build(ServerMode m) { builds++; return new OverlayTable(schema, new DbLayer(), import, importPath); }

        var set = new ModeSet(ServerMode.PreRenewal, Build, import, importPath);
        Assert.Equal(1, builds);                              // only the active (pre-re) base

        Assert.Equal(importPath, set.ImportFilePath);         // path read without building the other mode
        _ = set.IsDirty;                                      // dirty check must not build the other mode
        Assert.Equal(1, builds);

        try
        {
            set.Save();                                       // writes via the already-built overlay
            Assert.Equal(1, builds);                          // still only one mode built
            Assert.True(File.Exists(importPath));
        }
        finally { if (File.Exists(importPath)) File.Delete(importPath); }
    }
}
