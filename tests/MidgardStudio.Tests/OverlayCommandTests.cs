using MidgardStudio.Core.Commands;
using MidgardStudio.Core.Model;
using MidgardStudio.Core.Overlay;
using MidgardStudio.Core.Schemas;

namespace MidgardStudio.Tests;

/// <summary>
/// <see cref="AddRecordCommand"/> undo must remove the exact record it added — even if the record's id
/// (and therefore its key) changed after the add, which a key-based undo would miss.
/// </summary>
public class OverlayCommandTests
{
    private static (OverlayTable table, DbRecord rec) NewItem(int id)
    {
        var table = new OverlayTable(ItemDbSchema.Instance, new DbLayer(), new DbLayer(), "x.yml");
        var rec = new DbRecord(ItemDbSchema.Instance);
        rec.SetRaw("Id", id);
        rec.SetRaw("AegisName", "ITEM_" + id);
        rec.SetRaw("Name", "Item " + id);
        return (table, rec);
    }

    [Fact]
    public void Add_then_undo_leaves_the_import_empty()
    {
        var (table, rec) = NewItem(100);
        var cmd = new AddRecordCommand(table, rec);

        cmd.Do();
        Assert.Equal(1, table.ImportCount);
        cmd.Undo();

        Assert.Equal(0, table.ImportCount);
    }

    [Fact]
    public void Undoing_an_add_removes_the_record_even_after_its_id_changed()
    {
        var (table, rec) = NewItem(100);
        var cmd = new AddRecordCommand(table, rec);

        cmd.Do();
        rec.SetRaw("Id", 101); // key is now 101, but it was indexed under 100 — a key-based undo would miss it
        cmd.Undo();

        Assert.Equal(0, table.ImportCount);
    }
}
