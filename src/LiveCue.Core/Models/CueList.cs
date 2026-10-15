using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace LiveCue.Core.Models;

public class CueList : INotifyPropertyChanged
{
    public string Name { get; set; } = "Cue List 1";
    public ObservableCollection<Cue> Cues { get; } = new();

    private int _selectedIndex = -1;
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (_selectedIndex == value) return;
            _selectedIndex = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedIndex)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedCue)));
        }
    }

    public Cue? SelectedCue => SelectedIndex >= 0 && SelectedIndex < Cues.Count
        ? Cues[SelectedIndex]
        : null;

    public event PropertyChangedEventHandler? PropertyChanged;

    public CueList()
    {
        // "Continue"/"start together" only make sense when there's a next cue to chain into -
        // the last cue in the list can never have one, so keep that invariant enforced here
        // rather than relying on every call site (UI, project load, ...) to remember it.
        // Adds are skipped: appending never turns an existing cue into the last one, and
        // enforcing it on every Add would wipe the ContinueMode of each cue while a project
        // is being loaded one cue at a time (each one is briefly "the last" as it's added).
        Cues.CollectionChanged += (_, e) =>
        {
            if (e.Action != NotifyCollectionChangedAction.Add)
                EnforceLastCueDoesNotContinue();
        };
    }

    private void EnforceLastCueDoesNotContinue()
    {
        if (Cues.Count == 0) return;
        var last = Cues[^1];
        if (last.ContinueMode != ContinueMode.DoNotContinue)
            last.ContinueMode = ContinueMode.DoNotContinue;
    }

    /// <summary>
    /// Number for a newly added cue: one more than the highest whole number in use. Using
    /// "count + 1" instead produced duplicates after a deletion (1, 3 → new cue "3").
    /// Non-numeric numbers ("2A", "intro") are ignored.
    /// </summary>
    public string NextCueNumber()
    {
        var highest = Cues
            .Select(c => int.TryParse(c.Number, out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();
        return (highest + 1).ToString();
    }

    public Cue? NextCue()
    {
        var next = SelectedIndex + 1;
        return next < Cues.Count ? Cues[next] : null;
    }
}
