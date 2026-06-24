using MidgardStudio.Core.Commands;
using MidgardStudio.Core.Model;
using MidgardStudio.Core.Overlay;
using MidgardStudio.Core.Schemas;
using MidgardStudio.Core.Validation;

namespace MidgardStudio.Tests;

public class CommandStackTests
{
    private static DbRecord NewItem(int id)
    {
        var r = new DbRecord(ItemDbSchema.Instance);
        r.SetRaw("Id", id);
        r.SetRaw("AegisName", "X" + id);
        r.SetRaw("Name", "Item " + id);
        r.SetRaw("Type", "Etc");
        return r;
    }

    [Fact]
    public void SetField_undo_redo_restores_values()
    {
        var stack = new EditCommandStack();
        var rec = NewItem(1);

        stack.Execute(new SetFieldCommand(rec, "Buy", 500));
        Assert.Equal(500, rec.GetInt("Buy"));

        stack.Undo();
        Assert.Equal(0, rec.GetInt("Buy"));

        stack.Redo();
        Assert.Equal(500, rec.GetInt("Buy"));
    }

    [Fact]
    public void Batch_groups_into_single_undo_step()
    {
        var stack = new EditCommandStack();
        var rec = NewItem(2);

        using (stack.BeginBatch("edit"))
        {
            stack.Execute(new SetFieldCommand(rec, "Buy", 100));
            stack.Execute(new SetFieldCommand(rec, "Weight", 50));
        }

        Assert.Equal(100, rec.GetInt("Buy"));
        Assert.Equal(50, rec.GetInt("Weight"));

        stack.Undo(); // reverts both in one step
        Assert.Equal(0, rec.GetInt("Buy"));
        Assert.Equal(0, rec.GetInt("Weight"));
    }

    [Fact]
    public void Saved_marker_tracks_modification_state()
    {
        var stack = new EditCommandStack();
        var rec = NewItem(3);

        Assert.False(stack.IsModified);
        stack.Execute(new SetFieldCommand(rec, "Buy", 1));
        Assert.True(stack.IsModified);
        stack.MarkSaved();
        Assert.False(stack.IsModified);
        stack.Undo();
        Assert.True(stack.IsModified);
    }

    [Fact]
    public void Add_and_remove_commands_round_trip_through_overlay()
    {
        var schema = ItemDbSchema.Instance;
        var overlay = new OverlayTable(schema, new DbLayer(), new DbLayer(), "x.yml");
        var stack = new EditCommandStack();
        var rec = NewItem(99003);

        stack.Execute(new AddRecordCommand(overlay, rec));
        Assert.Equal(1, overlay.ImportCount);

        stack.Undo();
        Assert.Equal(0, overlay.ImportCount);

        stack.Redo();
        Assert.Equal(1, overlay.ImportCount);
    }

    [Fact]
    public void Validator_flags_missing_aegis_name()
    {
        var schema = ItemDbSchema.Instance;
        var importLayer = new DbLayer();
        var bad = new DbRecord(schema);
        bad.SetRaw("Id", 99004);
        bad.SetRaw("Name", string.Empty);
        importLayer.Add(bad);

        var overlay = new OverlayTable(schema, new DbLayer(), importLayer, "x.yml");
        var issues = new ValidationService().Register(new ItemValidator()).Validate(overlay);

        Assert.Contains(issues, i => i.Field == "AegisName" && i.Severity == ValidationSeverity.Error);
    }
}
