using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using LiveCue.Core.Models;
using LiveCue.Core.Services;
using Microsoft.Win32;

namespace LiveCue.App;

/// <summary>A (Value, Label) pair for the "Continue" combo box — ContinueMode shown with Italian labels.</summary>
public record ContinueModeOption(ContinueMode Value, string Label)
{
    public override string ToString() => Label;
}

public class MainViewModel : INotifyPropertyChanged
{
    private static readonly string[] VideoExtensions = { ".mp4", ".mov", ".mkv", ".avi", ".webm", ".wmv", ".m4v" };
    private static readonly string[] AudioExtensions = { ".mp3", ".wav", ".aac", ".flac", ".m4a", ".ogg", ".wma" };

    public static readonly ContinueModeOption[] ContinueModeOptions =
    {
        new(ContinueMode.DoNotContinue, "Non continua"),
        new(ContinueMode.Continue, "Continua"),
        new(ContinueMode.StartTogether, "Parti insieme"),
    };

    public CueList CueList { get; } = new();
    public ObservableCollection<DisplayInfo> Displays { get; } = new();
    public ObservableCollection<VirtualDisplay> VirtualDisplays { get; } = new();
    public ObservableCollection<DisplayMode> AvailableModes { get; } = new();
    public CueEngine Engine { get; }
    public MediaPlayerCuePlayer Player => _player;

    /// <summary>Physical + virtual displays combined, for the cue-list "Schermo" picker.</summary>
    public IEnumerable<object> DisplayTargets => Displays.Cast<object>().Concat(VirtualDisplays.Cast<object>());

    public RelayCommand GoCommand { get; }
    public RelayCommand PanicCommand { get; }
    public RelayCommand AddVideoCueCommand { get; }
    public RelayCommand AddAudioCueCommand { get; }
    public RelayCommand RemoveSelectedCueCommand { get; }
    public RelayCommand RefreshDisplaysCommand { get; }
    public RelayCommand ApplyResolutionCommand { get; }
    public RelayCommand CreateVirtualDisplayCommand { get; }
    public RelayCommand CancelEditVirtualDisplayCommand { get; }
    public RelayCommand ApplyMonitorRenameCommand { get; }
    public RelayCommand CancelMonitorRenameCommand { get; }
    public RelayCommand SaveProjectCommand { get; }
    public RelayCommand LoadProjectCommand { get; }
    public RelayCommand NewProjectCommand { get; }

    private readonly MediaPlayerCuePlayer _player;
    private readonly SystemMonitor _systemMonitor = new();
    private readonly DispatcherTimer _systemMonitorTimer;
    private bool _isSamplingSystemStats;
    private string? _currentProjectPath;

    /// <summary>User-given display names, keyed by DisplayInfo.DeviceName — Windows' own EDID name
    /// (e.g. "Generic PnP Monitor") is often useless for telling two monitors apart at a glance.</summary>
    private readonly Dictionary<string, string> _monitorNameOverrides = new();
    private DisplayInfo? _renamingDisplay;

    private DisplayInfo? _selectedDisplayForResolution;
    public DisplayInfo? SelectedDisplayForResolution
    {
        get => _selectedDisplayForResolution;
        set
        {
            if (!SetField(ref _selectedDisplayForResolution, value)) return;
            RefreshAvailableModes();
        }
    }

    private DisplayMode? _selectedMode;
    public DisplayMode? SelectedMode
    {
        get => _selectedMode;
        set => SetField(ref _selectedMode, value);
    }

    private string _newVirtualDisplayName = "Virtuale 1";
    public string NewVirtualDisplayName
    {
        get => _newVirtualDisplayName;
        set => SetField(ref _newVirtualDisplayName, value);
    }

    private DisplayInfo? _newVirtualHostDisplay;
    public DisplayInfo? NewVirtualHostDisplay
    {
        get => _newVirtualHostDisplay;
        set => SetField(ref _newVirtualHostDisplay, value);
    }

    private int _newVirtualWidth = 1920;
    public int NewVirtualWidth
    {
        get => _newVirtualWidth;
        set => SetField(ref _newVirtualWidth, value);
    }

    private int _newVirtualHeight = 1080;
    public int NewVirtualHeight
    {
        get => _newVirtualHeight;
        set => SetField(ref _newVirtualHeight, value);
    }

    private int _newVirtualOffsetX;
    public int NewVirtualOffsetX
    {
        get => _newVirtualOffsetX;
        set => SetField(ref _newVirtualOffsetX, value);
    }

    private int _newVirtualOffsetY;
    public int NewVirtualOffsetY
    {
        get => _newVirtualOffsetY;
        set => SetField(ref _newVirtualOffsetY, value);
    }

    private VirtualDisplay? _editingVirtualDisplay;

    private bool _isEditingVirtualDisplay;
    public bool IsEditingVirtualDisplay
    {
        get => _isEditingVirtualDisplay;
        private set => SetField(ref _isEditingVirtualDisplay, value);
    }

    private string _virtualDisplayFormButtonText = "Crea monitor virtuale";
    public string VirtualDisplayFormButtonText
    {
        get => _virtualDisplayFormButtonText;
        private set => SetField(ref _virtualDisplayFormButtonText, value);
    }

    private bool _isRenamingMonitor;
    public bool IsRenamingMonitor
    {
        get => _isRenamingMonitor;
        private set => SetField(ref _isRenamingMonitor, value);
    }

    private string _renameMonitorText = "";
    public string RenameMonitorText
    {
        get => _renameMonitorText;
        set => SetField(ref _renameMonitorText, value);
    }

    private string _statusMessage = "";
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    private string _systemStatusText = "";
    public string SystemStatusText
    {
        get => _systemStatusText;
        set => SetField(ref _systemStatusText, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel()
    {
        _player = new MediaPlayerCuePlayer(App.VlcInstance!, ResolveBounds);

        _player.CueFailed += (cue, reason) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            StatusMessage = $"Cue {cue.Number} \"{cue.Name}\" non eseguita: {reason}");

        Engine = new CueEngine(CueList, _player);

        GoCommand = new RelayCommand(Engine.GoAsync);
        PanicCommand = new RelayCommand(Panic);
        AddVideoCueCommand = new RelayCommand(AddVideoCue);
        AddAudioCueCommand = new RelayCommand(AddAudioCue);
        RemoveSelectedCueCommand = new RelayCommand(RemoveSelectedCue, () => CueList.SelectedCue is not null);
        RefreshDisplaysCommand = new RelayCommand(RefreshDisplays);
        ApplyResolutionCommand = new RelayCommand(ApplyResolution,
            () => SelectedDisplayForResolution is not null && SelectedMode is not null);
        CreateVirtualDisplayCommand = new RelayCommand(CreateVirtualDisplay,
            () => NewVirtualHostDisplay is not null && NewVirtualWidth > 0 && NewVirtualHeight > 0);
        CancelEditVirtualDisplayCommand = new RelayCommand(CancelEditVirtualDisplay);
        ApplyMonitorRenameCommand = new RelayCommand(ApplyMonitorRename, () => _renamingDisplay is not null);
        CancelMonitorRenameCommand = new RelayCommand(CancelRenameMonitor);
        SaveProjectCommand = new RelayCommand(() => SaveProject());
        LoadProjectCommand = new RelayCommand(LoadProject);
        NewProjectCommand = new RelayCommand(NewProject);

        Displays.CollectionChanged += (_, _) => OnPropertyChanged(nameof(DisplayTargets));
        VirtualDisplays.CollectionChanged += (_, _) => OnPropertyChanged(nameof(DisplayTargets));

        RefreshDisplays();
        StartChangeTracking();

        _systemMonitorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _systemMonitorTimer.Tick += (_, _) => UpdateSystemStats();
        _systemMonitorTimer.Start();
        UpdateSystemStats();
    }

    private async void UpdateSystemStats()
    {
        // Sampling runs on a worker thread: refreshing the GPU performance counters can take
        // over half a second, which on the UI thread froze the whole app (GO included) every
        // few seconds. One sample at a time, since SystemMonitor keeps state between samples.
        if (_isSamplingSystemStats) return;
        _isSamplingSystemStats = true;
        SystemStats stats;
        try
        {
            stats = await Task.Run(_systemMonitor.Sample);
        }
        catch
        {
            return; // stats are informational only; never let them disturb the show
        }
        finally
        {
            _isSamplingSystemStats = false;
        }

        var gpuText = stats.GpuAvailable ? $"{stats.GpuPercent:F0}%" : "n/d";
        SystemStatusText =
            $"CPU {stats.CpuPercent:F0}%  |  RAM {stats.RamUsedGb:F1}/{stats.RamTotalGb:F1} GB ({stats.RamPercent:F0}%)  |  GPU {gpuText}  |  LiveCue: CPU {stats.AppCpuPercent:F0}% RAM {stats.AppRamMb:F0} MB";
    }

    /// <summary>Panic button (ESC): stops every cue and all playback immediately.</summary>
    private void Panic()
    {
        Engine.Panic();
        StatusMessage = "PANIC — tutto fermato.";
    }

    private ScreenBounds? ResolveBounds(string? targetId)
    {
        // Always re-query the OS here instead of using the cached Displays collection: that
        // collection is only refreshed on startup or on "Aggiorna elenco monitor", so if a
        // resolution changed since then (ours or Windows'), using it would send the cue to the
        // wrong position/size even though the correct monitor was selected.
        var livePhysical = DisplayManager.GetDisplays();

        // When this returns null the player reports the cue as not played (CueFailed).
        var bounds = DisplayManager.ResolveOutputBounds(targetId, livePhysical, VirtualDisplays, out var warning);
        if (bounds is not null && warning is not null)
            StatusMessage = $"Attenzione: {warning}.";
        return bounds;
    }

    private void RefreshDisplays()
    {
        var previouslySelectedDevice = SelectedDisplayForResolution?.DeviceName;

        Displays.Clear();
        foreach (var display in DisplayManager.GetDisplays())
        {
            // Windows' own EDID name ("Generic PnP Monitor"...) is often useless for telling two
            // monitors apart at a glance; a renamed display keeps its custom name across refreshes.
            var named = _monitorNameOverrides.TryGetValue(display.DeviceName, out var customName)
                ? display with { MonitorName = customName }
                : display;
            Displays.Add(named);
        }

        SelectedDisplayForResolution = Displays.FirstOrDefault(d => d.DeviceName == previouslySelectedDevice)
            ?? Displays.FirstOrDefault();
        NewVirtualHostDisplay ??= Displays.FirstOrDefault();
    }

    /// <summary>Loads a monitor's current name into the rename form so it can be changed and saved.</summary>
    public void BeginRenameMonitor(DisplayInfo display)
    {
        _renamingDisplay = display;
        RenameMonitorText = display.MonitorName;
        IsRenamingMonitor = true;
    }

    private void ApplyMonitorRename()
    {
        if (_renamingDisplay is null) return;

        if (string.IsNullOrWhiteSpace(RenameMonitorText))
            _monitorNameOverrides.Remove(_renamingDisplay.DeviceName);
        else
            _monitorNameOverrides[_renamingDisplay.DeviceName] = RenameMonitorText.Trim();

        CancelRenameMonitor();
        RefreshDisplays();
    }

    private void CancelRenameMonitor()
    {
        _renamingDisplay = null;
        RenameMonitorText = "";
        IsRenamingMonitor = false;
    }

    private void RefreshAvailableModes()
    {
        AvailableModes.Clear();
        if (SelectedDisplayForResolution is null) return;

        foreach (var mode in DisplayManager.GetAvailableModes(SelectedDisplayForResolution.DeviceName))
            AvailableModes.Add(mode);

        SelectedMode = AvailableModes.FirstOrDefault(m =>
            m.Width == SelectedDisplayForResolution.Width && m.Height == SelectedDisplayForResolution.Height);
    }

    private void ApplyResolution()
    {
        if (SelectedDisplayForResolution is null || SelectedMode is null) return;

        var ok = DisplayManager.ChangeResolution(
            SelectedDisplayForResolution.DeviceName,
            SelectedMode.Width,
            SelectedMode.Height,
            SelectedMode.RefreshRate,
            SelectedMode.BitsPerPixel);

        StatusMessage = ok
            ? $"Risoluzione cambiata in {SelectedMode}."
            : "Cambio risoluzione non riuscito (modalità non supportata dal monitor/driver).";

        RefreshDisplays();
    }

    /// <summary>Loads a virtual display's current values into the form so it can be edited and saved back in place.</summary>
    public void BeginEditVirtualDisplay(VirtualDisplay virtualDisplay)
    {
        _editingVirtualDisplay = virtualDisplay;
        NewVirtualDisplayName = virtualDisplay.Name;
        NewVirtualHostDisplay = Displays.FirstOrDefault(d => d.DeviceName == virtualDisplay.HostDeviceName) ?? NewVirtualHostDisplay;
        NewVirtualWidth = virtualDisplay.Width;
        NewVirtualHeight = virtualDisplay.Height;
        NewVirtualOffsetX = virtualDisplay.OffsetX;
        NewVirtualOffsetY = virtualDisplay.OffsetY;
        IsEditingVirtualDisplay = true;
        VirtualDisplayFormButtonText = "Salva modifiche";
    }

    public void RemoveVirtualDisplay(VirtualDisplay virtualDisplay)
    {
        if (ReferenceEquals(_editingVirtualDisplay, virtualDisplay))
            CancelEditVirtualDisplay();

        VirtualDisplays.Remove(virtualDisplay);
    }

    private void CancelEditVirtualDisplay()
    {
        _editingVirtualDisplay = null;
        IsEditingVirtualDisplay = false;
        VirtualDisplayFormButtonText = "Crea monitor virtuale";
        NewVirtualDisplayName = $"Virtuale {VirtualDisplays.Count + 1}";
    }

    private void CreateVirtualDisplay()
    {
        if (NewVirtualHostDisplay is null) return;

        var name = string.IsNullOrWhiteSpace(NewVirtualDisplayName) ? $"Virtuale {VirtualDisplays.Count + 1}" : NewVirtualDisplayName;

        if (_editingVirtualDisplay is { } editing)
        {
            // Same Id, mutated in place: cues already targeting this virtual display stay linked to it.
            editing.Name = name;
            editing.HostDeviceName = NewVirtualHostDisplay.DeviceName;
            editing.OffsetX = NewVirtualOffsetX;
            editing.OffsetY = NewVirtualOffsetY;
            editing.Width = NewVirtualWidth;
            editing.Height = NewVirtualHeight;
            CancelEditVirtualDisplay();
            return;
        }

        VirtualDisplays.Add(new VirtualDisplay
        {
            Name = name,
            HostDeviceName = NewVirtualHostDisplay.DeviceName,
            OffsetX = NewVirtualOffsetX,
            OffsetY = NewVirtualOffsetY,
            Width = NewVirtualWidth,
            Height = NewVirtualHeight,
        });

        NewVirtualDisplayName = $"Virtuale {VirtualDisplays.Count + 1}";
    }

    private void AddVideoCue()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Scegli un file video",
            Filter = "File video|*.mp4;*.mov;*.mkv;*.avi;*.webm;*.wmv|Tutti i file|*.*",
        };
        if (dialog.ShowDialog() != true) return;
        AddCueFromFile(dialog.FileName);
    }

    private void AddAudioCue()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Scegli un file audio",
            Filter = "File audio|*.mp3;*.wav;*.aac;*.flac;*.m4a;*.ogg|Tutti i file|*.*",
        };
        if (dialog.ShowDialog() != true) return;
        AddCueFromFile(dialog.FileName);
    }

    /// <summary>Adds a cue inferring Audio/Video from the file extension. Used by file dialogs and drag&amp;drop.</summary>
    public void AddCueFromFile(string filePath)
    {
        // Make sure the default "Schermo" assignment (and its label) reflects the monitor
        // layout as it is right now, not a snapshot from whenever Displays was last refreshed.
        RefreshDisplays();

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        Cue cue;

        if (VideoExtensions.Contains(ext))
        {
            cue = new VideoCue
            {
                Number = NextCueNumber(),
                Name = Path.GetFileNameWithoutExtension(filePath),
                FilePath = filePath,
                // Prefer the first non-primary (external) display, if there is one.
                TargetDisplayId = (Displays.FirstOrDefault(d => !d.IsPrimary) ?? Displays.FirstOrDefault())?.Id,
            };
        }
        else if (AudioExtensions.Contains(ext))
        {
            cue = new AudioCue
            {
                Number = NextCueNumber(),
                Name = Path.GetFileNameWithoutExtension(filePath),
                FilePath = filePath,
            };
        }
        else
        {
            StatusMessage = $"Formato non riconosciuto: {Path.GetFileName(filePath)}";
            return;
        }

        CueList.Cues.Add(cue);
        CueList.SelectedIndex = CueList.Cues.Count - 1;
        _ = _player.LoadDurationAsync(cue, filePath);
    }

    private void RemoveSelectedCue()
    {
        if (CueList.SelectedCue is { } cue)
            CueList.Cues.Remove(cue);
    }

    // --- Unsaved-changes tracking ---

    private bool _isDirty;
    /// <summary>True when the show has changes not yet saved to its project file.</summary>
    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (SetField(ref _isDirty, value))
                OnPropertyChanged(nameof(WindowTitle));
        }
    }

    public string WindowTitle =>
        $"LiveCue - {(_currentProjectPath is null ? "Nuovo progetto" : Path.GetFileNameWithoutExtension(_currentProjectPath))}{(IsDirty ? " *" : "")}";

    // Live/derived values that change on their own and are not part of the saved show.
    private static readonly HashSet<string> UntrackedCueProperties = new() { nameof(Cue.Duration), nameof(Cue.RemainingTime) };

    private readonly HashSet<Cue> _trackedCues = new();
    private readonly HashSet<VirtualDisplay> _trackedVirtualDisplays = new();

    private void StartChangeTracking()
    {
        CueList.Cues.CollectionChanged += (_, _) =>
        {
            SyncTracked(_trackedCues, CueList.Cues, OnCuePropertyChanged);
            IsDirty = true;
        };
        VirtualDisplays.CollectionChanged += (_, _) =>
        {
            SyncTracked(_trackedVirtualDisplays, VirtualDisplays, OnVirtualDisplayPropertyChanged);
            IsDirty = true;
        };
    }

    /// <summary>Keeps exactly the current items subscribed (handles Clear/Reset, where the removed items aren't reported).</summary>
    private static void SyncTracked<T>(HashSet<T> tracked, IEnumerable<T> current, PropertyChangedEventHandler handler) where T : INotifyPropertyChanged
    {
        var currentSet = current.ToHashSet();
        foreach (var item in tracked.Except(currentSet).ToList())
        {
            item.PropertyChanged -= handler;
            tracked.Remove(item);
        }
        foreach (var item in currentSet.Where(tracked.Add))
            item.PropertyChanged += handler;
    }

    private void OnCuePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!UntrackedCueProperties.Contains(e.PropertyName ?? "")) IsDirty = true;
    }

    private void OnVirtualDisplayPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(VirtualDisplay.Label)) IsDirty = true;
    }

    private void SetCurrentProject(string? path)
    {
        _currentProjectPath = path;
        IsDirty = false;
        OnPropertyChanged(nameof(WindowTitle));
    }

    /// <summary>
    /// Asks whether to save unsaved changes before they'd be lost (New, Open, closing the app).
    /// </summary>
    /// <returns>false if the user cancelled, or the save failed/was cancelled: abort the operation.</returns>
    public bool ConfirmDiscardChanges()
    {
        if (!IsDirty) return true;

        var answer = MessageBox.Show(
            "Ci sono modifiche non salvate. Vuoi salvarle prima di continuare?",
            "LiveCue", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        return answer switch
        {
            MessageBoxResult.Yes => SaveProject(),
            MessageBoxResult.No => true,
            _ => false,
        };
    }

    // --- Project commands ---

    private void NewProject()
    {
        if (!ConfirmDiscardChanges()) return;

        Engine.Panic();
        CueList.Cues.Clear();
        CueList.Name = "Cue List 1";
        CueList.SelectedIndex = -1;
        SetCurrentProject(null);
        StatusMessage = "Nuovo progetto.";
    }

    /// <returns>true if the project was saved.</returns>
    private bool SaveProject()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Salva progetto",
            Filter = "Progetto LiveCue|*.livecue|Tutti i file|*.*",
            FileName = _currentProjectPath is null ? "Progetto.livecue" : Path.GetFileName(_currentProjectPath),
        };
        if (dialog.ShowDialog() != true) return false;

        try
        {
            ProjectFile.Save(CueList, VirtualDisplays, dialog.FileName);
        }
        catch (Exception ex)
        {
            App.LogError(ex);
            MessageBox.Show($"Impossibile salvare il progetto:\n{ex.Message}", "LiveCue", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }

        SetCurrentProject(dialog.FileName);
        StatusMessage = $"Progetto salvato in {dialog.FileName}";
        return true;
    }

    private void LoadProject()
    {
        if (!ConfirmDiscardChanges()) return;

        var dialog = new OpenFileDialog
        {
            Title = "Apri progetto",
            Filter = "Progetto LiveCue|*.livecue|Tutti i file|*.*",
        };
        if (dialog.ShowDialog() != true) return;

        CancelEditVirtualDisplay();
        try
        {
            ProjectFile.Load(CueList, VirtualDisplays, dialog.FileName);
        }
        catch (Exception ex)
        {
            // Load only replaces the current show once the file has been fully read and parsed,
            // so a corrupt file leaves what was open untouched.
            App.LogError(ex);
            MessageBox.Show($"Impossibile aprire il progetto (file danneggiato o non valido):\n{ex.Message}",
                "LiveCue", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        SetCurrentProject(dialog.FileName);
        StatusMessage = $"Progetto caricato da {dialog.FileName}";

        var missing = CueList.Cues.OfType<MediaCue>().Count(c => !File.Exists(c.FilePath));
        if (missing > 0)
            StatusMessage += $" — ATTENZIONE: {missing} file multimediali non trovati.";

        // Older project files may predate the Duration field; backfill it for those cues.
        foreach (var cue in CueList.Cues)
        {
            if (cue.Duration is null && cue is MediaCue { FilePath.Length: > 0 } mediaCue)
                _ = _player.LoadDurationAsync(cue, mediaCue.FilePath);
        }
    }

    private string NextCueNumber() => CueList.NextCueNumber();

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName!);
        return true;
    }
}
