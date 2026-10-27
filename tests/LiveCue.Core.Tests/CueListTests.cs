using LiveCue.Core.Models;

namespace LiveCue.Core.Tests;

public sealed class CueListTests
{
    [Fact]
    public void Move_ToLastPosition_ClearsContinueMode_AsSideEffect()
    {
        // Documents the existing CueList invariant Undo/Redo has to work around: the last cue
        // can never "Continua", enforced automatically on Move/Remove (not Add).
        var list = new CueList();
        var cue = new AudioCue { Number = "1", ContinueMode = ContinueMode.Continue };
        list.Cues.Add(cue);
        list.Cues.Add(new AudioCue { Number = "2" });

        list.Cues.Move(0, 1); // cue becomes the last one

        Assert.Equal(ContinueMode.DoNotContinue, cue.ContinueMode);
    }

    [Fact]
    public void SnapshotAndRestoreContinueModes_UndoesTheSideEffectOfAMove()
    {
        var list = new CueList();
        var first = new AudioCue { Number = "1", ContinueMode = ContinueMode.Continue };
        var second = new AudioCue { Number = "2" };
        list.Cues.Add(first);
        list.Cues.Add(second);

        var snapshot = list.SnapshotContinueModes();
        list.Cues.Move(0, 1); // first becomes last -> its ContinueMode is cleared
        Assert.Equal(ContinueMode.DoNotContinue, first.ContinueMode);

        list.Cues.Move(1, 0); // undo the move: back to the original order
        list.RestoreContinueModes(snapshot);

        Assert.Equal(ContinueMode.Continue, first.ContinueMode);
    }

    [Fact]
    public void SnapshotAndRestoreContinueModes_UndoesTheSideEffectOfARemove()
    {
        var list = new CueList();
        var first = new AudioCue { Number = "1" };
        var second = new AudioCue { Number = "2", ContinueMode = ContinueMode.Continue };
        var third = new AudioCue { Number = "3" }; // last cue, already DoNotContinue
        list.Cues.Add(first);
        list.Cues.Add(second);
        list.Cues.Add(third);

        var snapshot = list.SnapshotContinueModes();
        list.Cues.Remove(third); // second becomes the new last cue -> its ContinueMode is cleared
        Assert.Equal(ContinueMode.DoNotContinue, second.ContinueMode);

        list.Cues.Insert(2, third); // undo the remove: back to the original order
        list.RestoreContinueModes(snapshot);

        Assert.Equal(ContinueMode.Continue, second.ContinueMode);
    }

    [Fact]
    public void SelectNext_ClampsAtLastCue_NeverWraps()
    {
        var list = new CueList();
        list.Cues.Add(new AudioCue { Number = "1" });
        list.Cues.Add(new AudioCue { Number = "2" });
        list.SelectedIndex = 1;

        list.SelectNext();

        Assert.Equal(1, list.SelectedIndex);
    }

    [Fact]
    public void SelectPrevious_ClampsAtFirstCue()
    {
        var list = new CueList();
        list.Cues.Add(new AudioCue { Number = "1" });
        list.Cues.Add(new AudioCue { Number = "2" });
        list.SelectedIndex = 0;

        list.SelectPrevious();

        Assert.Equal(0, list.SelectedIndex);
    }

    [Fact]
    public void SelectNext_FromNoSelection_LandsOnFirstCue()
    {
        var list = new CueList();
        list.Cues.Add(new AudioCue { Number = "1" });
        list.Cues.Add(new AudioCue { Number = "2" });

        list.SelectNext();

        Assert.Equal(0, list.SelectedIndex);
    }

    [Fact]
    public void SelectNextAndPrevious_OnEmptyList_DoNothing()
    {
        var list = new CueList();
        list.SelectNext();
        list.SelectPrevious();
        Assert.Equal(-1, list.SelectedIndex);
    }
}
