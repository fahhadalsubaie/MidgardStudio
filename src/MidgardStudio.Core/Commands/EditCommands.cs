using MidgardStudio.Core.Model;
using MidgardStudio.Core.Overlay;

namespace MidgardStudio.Core.Commands;

/// <summary>Sets a single field on a record (undo restores the previous value).</summary>
public sealed class SetFieldCommand : IEditCommand
{
    private readonly DbRecord _record;
    private readonly string _field;
    private readonly object? _oldValue;
    private readonly object? _newValue;

    public SetFieldCommand(DbRecord record, string field, object? newValue)
    {
        _record = record;
        _field = field;
        _oldValue = record.Get(field);
        _newValue = newValue;
        Description = $"{record.Schema.DisplayName}: edit {field}";
    }

    public string Description { get; }

    public void Do() => _record.Set(_field, _newValue);

    public void Undo() => _record.Set(_field, _oldValue);
}

/// <summary>Adds a new custom record to the import layer (undo removes it).</summary>
public sealed class AddRecordCommand : IEditCommand
{
    private readonly OverlayTable _table;
    private readonly DbRecord _record;

    public AddRecordCommand(OverlayTable table, DbRecord record)
    {
        _table = table;
        _record = record;
        Description = $"Add {record.Schema.DisplayName} {record.Key}";
    }

    public string Description { get; }

    public void Do() => _table.AddCustom(_record);

    public void Undo() => _table.RevertToCore(_record.Key);
}

/// <summary>A generic reversible mutation defined by two delegates (used for list add/remove/reorder).</summary>
public sealed class ListMutateCommand : IEditCommand
{
    private readonly Action _do;
    private readonly Action _undo;

    public ListMutateCommand(string description, Action doAction, Action undoAction)
    {
        Description = description;
        _do = doAction;
        _undo = undoAction;
    }

    public string Description { get; }

    public void Do() => _do();

    public void Undo() => _undo();
}

/// <summary>Removes an import record (deleting a custom entry, or reverting an override to core).</summary>
public sealed class RemoveImportCommand : IEditCommand
{
    private readonly OverlayTable _table;
    private readonly DbRecord _record;

    public RemoveImportCommand(OverlayTable table, DbRecord importRecord)
    {
        _table = table;
        _record = importRecord;
        Description = $"Remove {importRecord.Schema.DisplayName} {importRecord.Key}";
    }

    public string Description { get; }

    public void Do() => _table.RevertToCore(_record.Key);

    public void Undo() => _table.AddCustom(_record);
}
