using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
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
    public RelayCommand SaveProjectCommand { get; }
    public RelayCommand LoadProjectCommand { get; }
    public RelayCommand NewProjectCommand { get; }

    private readonly MediaPlayerCuePlayer _player;
    private readonly SystemMonitor _systemMonitor = new();
    private readonly DispatcherTimer _systemMonitorTimer;
    private string? _currentProjectPath;

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
        SaveProjectCommand = new RelayCommand(SaveProject);
        LoadProjectCommand = new RelayCommand(LoadProject);
        NewProjectCommand = new RelayCommand(NewProject);

        Displays.CollectionChanged += (_, _) => OnPropertyChanged(nameof(DisplayTargets));
        VirtualDisplays.CollectionChanged += (_, _) => OnPropertyChanged(nameof(DisplayTargets));

        RefreshDisplays();

        _systemMonitorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _systemMonitorTimer.Tick += (_, _) => UpdateSystemStats();
        _systemMonitorTimer.Start();
        UpdateSystemStats();
    }

    private void UpdateSystemStats()
    {
        var stats = _systemMonitor.Sample();
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

        if (!string.IsNullOrEmpty(targetId))
        {
            var resolved = DisplayManager.ResolveBounds(targetId, livePhysical, VirtualDisplays);
            if (resolved is not null) return resolved;
        }

        // No target set, or the target display/monitor is gone: fall back to the primary display.
        var primary = livePhysical.FirstOrDefault(d => d.IsPrimary) ?? livePhysical.FirstOrDefault();
        return primary is null ? null : new ScreenBounds(primary.X, primary.Y, primary.Width, primary.Height);
    }

    private void RefreshDisplays()
    {
        var previouslySelectedDevice = SelectedDisplayForResolution?.DeviceName;

        Displays.Clear();
        foreach (var display in DisplayManager.GetDisplays())
            Displays.Add(display);

        SelectedDisplayForResolution = Displays.FirstOrDefault(d => d.DeviceName == previouslySelectedDevice)
            ?? Displays.FirstOrDefault();
        NewVirtualHostDisplay ??= Displays.FirstOrDefault();
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

    private void NewProject()
    {
        Engine.Panic();
        CueList.Cues.Clear();
        CueList.Name = "Cue List 1";
        CueList.SelectedIndex = -1;
        _currentProjectPath = null;
        StatusMessage = "Nuovo progetto.";
    }

    private void SaveProject()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Salva progetto",
            Filter = "Progetto LiveCue|*.livecue|Tutti i file|*.*",
            FileName = _currentProjectPath is null ? "Progetto.livecue" : Path.GetFileName(_currentProjectPath),
        };
        if (dialog.ShowDialog() != true) return;

        ProjectFile.Save(CueList, dialog.FileName);
        _currentProjectPath = dialog.FileName;
        StatusMessage = $"Progetto salvato in {dialog.FileName}";
    }

    private void LoadProject()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Apri progetto",
            Filter = "Progetto LiveCue|*.livecue|Tutti i file|*.*",
        };
        if (dialog.ShowDialog() != true) return;

        ProjectFile.Load(CueList, dialog.FileName);
        _currentProjectPath = dialog.FileName;
        StatusMessage = $"Progetto caricato da {dialog.FileName}";

        // Older project files may predate the Duration field; backfill it for those cues.
        foreach (var cue in CueList.Cues)
        {
            var filePath = cue switch { VideoCue v => v.FilePath, AudioCue a => a.FilePath, _ => null };
            if (cue.Duration is null && filePath is not null)
                _ = _player.LoadDurationAsync(cue, filePath);
        }
    }

    private string NextCueNumber() => (CueList.Cues.Count + 1).ToString();

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
