namespace LiveCue.Core.Services;

/// <summary>A single reversible edit: a human-readable label plus the steps to redo/undo it.</summary>
public sealed class UndoableAction
{
    public string Description { get; }
    private readonly Action _redo;
    private readonly Action _undo;

    public UndoableAction(string description, Action redo, Action undo)
    {
        Description = description;
        _redo = redo;
        _undo = undo;
    }

    public void Redo() => _redo();
    public void Undo() => _undo();
}

/// <summary>
/// Linear undo/redo history of <see cref="UndoableAction"/>s. Callers perform the edit themselves
/// and then Push it (Push does not re-run the "redo" step - the caller's own code already is that
/// step); Redo only replays it later, after an Undo. Pushing always clears the redo stack, since a
/// fresh edit makes any previously-undone branch unreachable.
/// </summary>
public sealed class UndoManager
{
    private readonly Stack<UndoableAction> _undoStack = new();
    private readonly Stack<UndoableAction> _redoStack = new();

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;
    public string? UndoDescription => _undoStack.Count > 0 ? _undoStack.Peek().Description : null;
    public string? RedoDescription => _redoStack.Count > 0 ? _redoStack.Peek().Description : null;

    /// <summary>Raised after any operation that could change CanUndo/CanRedo/the descriptions.</summary>
    public event Action? Changed;

    public void Push(UndoableAction action)
    {
        _undoStack.Push(action);
        _redoStack.Clear();
        Changed?.Invoke();
    }

    public void Undo()
    {
        if (_undoStack.Count == 0) return;
        var action = _undoStack.Pop();
        action.Undo();
        _redoStack.Push(action);
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (_redoStack.Count == 0) return;
        var action = _redoStack.Pop();
        action.Redo();
        _undoStack.Push(action);
        Changed?.Invoke();
    }

    public void Clear()
    {
        if (_undoStack.Count == 0 && _redoStack.Count == 0) return;
        _undoStack.Clear();
        _redoStack.Clear();
        Changed?.Invoke();
    }
}
