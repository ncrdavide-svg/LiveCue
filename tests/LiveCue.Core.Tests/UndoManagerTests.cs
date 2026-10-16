using LiveCue.Core.Services;
using Xunit;

namespace LiveCue.Core.Tests;

public class UndoManagerTests
{
    [Fact]
    public void Undo_RevertsPushedAction()
    {
        var value = 2;
        var undo = new UndoManager();
        undo.Push(new UndoableAction("set 2", () => value = 2, () => value = 1));

        undo.Undo();

        Assert.Equal(1, value);
        Assert.False(undo.CanUndo);
        Assert.True(undo.CanRedo);
    }

    [Fact]
    public void Redo_ReappliesUndoneAction()
    {
        var value = 2;
        var undo = new UndoManager();
        undo.Push(new UndoableAction("set 2", () => value = 2, () => value = 1));
        undo.Undo();

        undo.Redo();

        Assert.Equal(2, value);
        Assert.True(undo.CanUndo);
        Assert.False(undo.CanRedo);
    }

    [Fact]
    public void Push_AfterUndo_ClearsRedoStack()
    {
        var undo = new UndoManager();
        undo.Push(new UndoableAction("a", () => { }, () => { }));
        undo.Undo();
        Assert.True(undo.CanRedo);

        undo.Push(new UndoableAction("b", () => { }, () => { }));

        Assert.False(undo.CanRedo);
    }

    [Fact]
    public void Undo_OnEmptyStack_DoesNothing()
    {
        var undo = new UndoManager();

        undo.Undo();

        Assert.False(undo.CanUndo);
        Assert.False(undo.CanRedo);
    }

    [Fact]
    public void Redo_OnEmptyStack_DoesNothing()
    {
        var undo = new UndoManager();

        undo.Redo();

        Assert.False(undo.CanUndo);
        Assert.False(undo.CanRedo);
    }

    [Fact]
    public void Clear_RemovesAllHistory()
    {
        var undo = new UndoManager();
        undo.Push(new UndoableAction("a", () => { }, () => { }));
        undo.Undo();

        undo.Clear();

        Assert.False(undo.CanUndo);
        Assert.False(undo.CanRedo);
    }

    [Fact]
    public void MultipleActions_UndoInReverseOrder()
    {
        var log = new List<string>();
        var undo = new UndoManager();
        undo.Push(new UndoableAction("a", () => log.Add("do a"), () => log.Add("undo a")));
        undo.Push(new UndoableAction("b", () => log.Add("do b"), () => log.Add("undo b")));

        undo.Undo();
        undo.Undo();

        Assert.Equal(new[] { "undo b", "undo a" }, log);
    }

    [Fact]
    public void Descriptions_ReflectTopOfEachStack()
    {
        var undo = new UndoManager();
        undo.Push(new UndoableAction("a", () => { }, () => { }));
        undo.Push(new UndoableAction("b", () => { }, () => { }));

        Assert.Equal("b", undo.UndoDescription);
        Assert.Null(undo.RedoDescription);

        undo.Undo();

        Assert.Equal("a", undo.UndoDescription);
        Assert.Equal("b", undo.RedoDescription);
    }
}
