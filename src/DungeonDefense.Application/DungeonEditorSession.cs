using DungeonDefense.Core;

namespace DungeonDefense.Application;

public sealed class DungeonEditorSession
{
    private readonly Stack<DungeonState> _undo = new();
    private readonly Stack<DungeonState> _redo = new();

    public DungeonEditorSession(DungeonState initial) => Current = initial.Clone();
    public DungeonState Current { get; private set; }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public EditResult Apply(Func<DungeonState, EditResult> edit)
    {
        var result = edit(Current);
        if (!result.Success) return result;
        ReplaceCurrent(result.State);
        return result;
    }

    public void ReplaceCurrent(DungeonState next)
    {
        _undo.Push(Current.Clone());
        _redo.Clear();
        Current = next.Clone();
    }

    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        _redo.Push(Current.Clone());
        Current = _undo.Pop();
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0) return false;
        _undo.Push(Current.Clone());
        Current = _redo.Pop();
        return true;
    }
}
