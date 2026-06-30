using MidgardStudio.Core.Commands;
using MidgardStudio.Core.Lookup;
using MidgardStudio.Core.Model;
using MidgardStudio.Core.Overlay;
using MidgardStudio.Core.Schemas;
using MidgardStudio.Core.Validation;
using MidgardStudio.Core.Workspace;

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

    // Regression: the validation Quick Fix routes each reversible fix through the command stack as a
    // ListMutateCommand(apply, revert). The Save button is gated on EditCommandStack.IsModified, so applying
    // several fixes and then fully undoing them MUST return IsModified to false — otherwise the Save button
    // (and the modified indicator) stay lit even though the model is back to its saved state.
    [Fact]
    public void QuickFix_style_commands_clear_modified_after_full_undo()
    {
        var stack = new EditCommandStack();
        var model = new[] { 5, 5, 5 };   // three "records", each fixed (set to 1) independently

        for (int i = 0; i < model.Length; i++)
        {
            int idx = i, old = model[idx];
            stack.Execute(new ListMutateCommand($"fix {idx}", () => model[idx] = 1, () => model[idx] = old));
        }
        Assert.True(stack.IsModified);

        while (stack.CanUndo) stack.Undo();

        Assert.False(stack.IsModified);              // Save gate clears
        Assert.Equal(new[] { 5, 5, 5 }, model);      // model fully reverted
    }

    // Regression for "do a fix then revert it, repeatedly": after each undo there must be nothing modified,
    // no matter how many apply/undo cycles run (the user reported 3+ cycles sticking the Save button).
    [Fact]
    public void QuickFix_apply_then_revert_cycles_leave_no_residual_modified()
    {
        var stack = new EditCommandStack();
        int value = 5;

        for (int cycle = 0; cycle < 5; cycle++)
        {
            int old = value;
            stack.Execute(new ListMutateCommand("fix", () => value = 1, () => value = old));
            Assert.True(stack.IsModified);

            stack.Undo();
            Assert.False(stack.IsModified);          // each revert clears the Save gate
            Assert.Equal(5, value);
        }
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
        var ctx = ValidationContext.Create(new InMemoryReferenceIndex(), ServerMode.Renewal);
        var issues = ValidationEngine.CreateDefault().ValidateOverlay(overlay, ValidationScope.CustomOnly, ctx).ToList();

        Assert.Contains(issues, i => i.Field == "AegisName" && i.Severity == ValidationSeverity.Error);
    }

    // A stand-in for a content-signature dirty source (e.g. ClientItemService): dirty when its entry set
    // differs from the set captured at construction (the "saved" baseline).
    private sealed class FakeClientText : IDirtySource
    {
        private readonly HashSet<int> _entries = new();
        private readonly string _saved;
        public FakeClientText() => _saved = Sig();
        private string Sig() => string.Join(",", _entries.OrderBy(x => x));
        public void Add(int id) { _entries.Add(id); DirtyChanged?.Invoke(); }
        public void Remove(int id) { _entries.Remove(id); DirtyChanged?.Invoke(); }
        public bool IsDirty => Sig() != _saved;
        public event Action? DirtyChanged;
    }

    // Regression for the cross-file orphan (the BLOCKER): an item Add/Forge seeds a client-text entry. If that
    // seed isn't on the undo stack, undoing the add leaves the client entry behind — the client dirty source
    // stays dirty, the Save button never clears, and a phantom client entry is written for a removed item. The
    // fix puts the seed in the SAME batch as the AddRecordCommand, so undoing the add clears EVERY dirty source.
    [Fact]
    public void Add_with_batched_client_seed_clears_all_dirty_sources_after_undo()
    {
        var overlay = new OverlayTable(ItemDbSchema.Instance, new DbLayer(), new DbLayer(), "x.yml");
        var stack = new EditCommandStack();
        var client = new FakeClientText();
        var dirty = new CompositeDirtyState(stack, client);
        Assert.False(dirty.IsDirty);

        var rec = NewItem(99006);
        using (stack.BeginBatch("Add item"))
        {
            stack.Execute(new AddRecordCommand(overlay, rec));
            stack.Execute(new ListMutateCommand("seed client", () => client.Add(99006), () => client.Remove(99006)));
        }
        Assert.True(dirty.IsDirty);
        Assert.Equal(1, overlay.ImportCount);

        stack.Undo(); // one step reverts BOTH the overlay add and the client seed
        Assert.False(dirty.IsDirty);    // no orphan client entry -> Save gate clears
        Assert.Equal(0, overlay.ImportCount);

        stack.Redo();
        Assert.True(dirty.IsDirty);     // redo re-applies both
        Assert.Equal(1, overlay.ImportCount);
    }
}
