namespace MidgardStudio.Core.Commands;

/// <summary>
/// Global undo/redo stack. Edits during an open batch are grouped into one undo step. A saved-marker
/// tracks whether anything changed since the last save without clearing history.
/// </summary>
public sealed class EditCommandStack
{
    private readonly Stack<IEditCommand> _undo = new();
    private readonly Stack<IEditCommand> _redo = new();
    private CompositeCommand? _batch;
    private int _savedDepth;

    public event Action? Changed;

    /// <summary>Raised after an Undo or Redo so views can re-sync against the mutated overlay
    /// (rows that were added/removed, fields that were reverted).</summary>
    public event Action? UndoRedoPerformed;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>True when the undo depth differs from the last save point.</summary>
    public bool IsModified => _undo.Count != _savedDepth;

    public string? NextUndoDescription => _undo.Count > 0 ? _undo.Peek().Description : null;

    public string? NextRedoDescription => _redo.Count > 0 ? _redo.Peek().Description : null;

    /// <summary>Runs a command (Do) and records it for undo. If a batch is open, it's grouped into it.</summary>
    public void Execute(IEditCommand command)
    {
        command.Do();
        if (_batch is not null)
        {
            _batch.Add(command);
            return;
        }

        _undo.Push(command);
        _redo.Clear();
        OnChanged();
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        var command = _undo.Pop();
        command.Undo();
        _redo.Push(command);
        OnChanged();
        UndoRedoPerformed?.Invoke();
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        var command = _redo.Pop();
        command.Do();
        _undo.Push(command);
        OnChanged();
        UndoRedoPerformed?.Invoke();
    }

    /// <summary>Opens a batch; dispose the returned scope to commit it as a single undo step.</summary>
    public IDisposable BeginBatch(string description)
    {
        _batch = new CompositeCommand(description);
        return new BatchScope(this);
    }

    private void EndBatch()
    {
        var batch = _batch;
        _batch = null;
        if (batch is { Count: > 0 })
        {
            _undo.Push(batch);
            _redo.Clear();
            OnChanged();
        }
    }

    public void MarkSaved()
    {
        _savedDepth = _undo.Count;
        OnChanged();
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _savedDepth = 0;
        OnChanged();
    }

    private void OnChanged() => Changed?.Invoke();

    private sealed class BatchScope : IDisposable
    {
        private readonly EditCommandStack _stack;
        private bool _disposed;

        public BatchScope(EditCommandStack stack) => _stack = stack;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _stack.EndBatch();
        }
    }
}
