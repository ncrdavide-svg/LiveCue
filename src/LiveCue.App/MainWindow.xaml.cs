using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using LiveCue.Core.Models;

namespace LiveCue.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private const string CueRowFormat = "CueRow";

    // ESC as a system-wide hotkey for PANIC: a cue's topmost video output window can end up
    // covering the operator's own screen, and LibVLC's native video surface can grab OS keyboard
    // focus on its own (independent of any WPF-level focus/activation setting on that window) -
    // so the in-window Escape KeyBinding alone isn't reliable. RegisterHotKey fires regardless of
    // which window currently has focus, which is what a panic button needs to guarantee.
    private const int PanicHotKeyId = 0x5A1C;
    private const uint ModNoRepeat = 0x4000;
    private const uint VkEscape = 0x1B;
    private const int WmHotKey = 0x0312;

    private MainViewModel ViewModel => (MainViewModel)DataContext;
    private MonitorManagerWindow? _monitorWindow;
    private MonitorPreviewWindow? _monitorPreviewWindow;
    private Point _dragStartPoint;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        Closing += (_, e) =>
        {
            if (!ViewModel.ConfirmDiscardChanges())
                e.Cancel = true;
        };

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(hwnd)?.AddHook(PanicHotKeyWndProc);
            RegisterHotKey(hwnd, PanicHotKeyId, ModNoRepeat, VkEscape);
        };
        Closed += (_, _) => UnregisterHotKey(new WindowInteropHelper(this).Handle, PanicHotKeyId);

        PreviewKeyDown += MainWindow_PreviewKeyDown;
    }

    /// <summary>
    /// Space = GO, like QLab. A plain WPF-level shortcut is enough here (unlike PANIC/ESC): a
    /// video cue's output window can never take keyboard focus (WS_EX_NOACTIVATE), so the main
    /// window's own key handling always sees it. It's a fallback, not an override: anywhere the
    /// operator could be typing a literal space (a cue's Name/Notes/Target, an open combo box, a
    /// focused checkbox/button already using Space for its own purpose) lets it through untouched,
    /// so naming a cue "Scene 2" never fires one.
    /// </summary>
    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space) return;
        if (Keyboard.FocusedElement is TextBox or ComboBox or ButtonBase) return;
        if (Keyboard.FocusedElement is DataGridCell { IsEditing: true }) return;

        e.Handled = true;
        ViewModel.GoCommand.Execute(null);
    }

    private IntPtr PanicHotKeyWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotKey && wParam.ToInt32() == PanicHotKeyId)
        {
            ViewModel.PanicCommand.Execute(null);
            handled = true;
        }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private void OpenMonitorManager_Click(object sender, RoutedEventArgs e)
    {
        if (_monitorWindow is null)
        {
            _monitorWindow = new MonitorManagerWindow(ViewModel) { Owner = this };
            _monitorWindow.Closed += (_, _) => _monitorWindow = null;
        }

        _monitorWindow.Show();
        _monitorWindow.Activate();
    }

    private void OpenMonitorPreview_Click(object sender, RoutedEventArgs e)
    {
        if (_monitorPreviewWindow is null)
        {
            _monitorPreviewWindow = new MonitorPreviewWindow(ViewModel) { Owner = this };
            _monitorPreviewWindow.Closed += (_, _) => _monitorPreviewWindow = null;
        }

        _monitorPreviewWindow.Show();
        _monitorPreviewWindow.Activate();
    }

    // --- Reordering cues by dragging the row's grip handle ---

    private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        _dragStartPoint = e.GetPosition(null);

    private void DragHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (sender is not FrameworkElement handle || handle.DataContext is not Cue cue) return;

        var position = e.GetPosition(null);
        if (Math.Abs(position.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        DragDrop.DoDragDrop(handle, new DataObject(CueRowFormat, cue), DragDropEffects.Move);
    }

    // --- Drag/drop target: reordering cues (from the grip handle above) and dropping files (from Explorer) ---

    private void CueGrid_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(CueRowFormat) ? DragDropEffects.Move
            : e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void CueGrid_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(CueRowFormat))
        {
            ReorderCue(e);
            return;
        }

        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var paths = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        foreach (var path in paths)
            ViewModel.AddCueFromFile(path);
    }

    private void ReorderCue(DragEventArgs e)
    {
        if (e.Data.GetData(CueRowFormat) is not Cue draggedCue) return;

        var cues = ViewModel.CueList.Cues;
        var oldIndex = cues.IndexOf(draggedCue);
        if (oldIndex < 0) return;

        var targetRow = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        int newIndex;
        if (targetRow?.Item is Cue targetCue && targetCue != draggedCue)
        {
            newIndex = cues.IndexOf(targetCue);
            // Dropping on the bottom half of a row inserts after it, not before.
            if (e.GetPosition(targetRow).Y > targetRow.ActualHeight / 2)
                newIndex++;
            if (newIndex > oldIndex) newIndex--; // account for the shift once the dragged item is removed
        }
        else
        {
            // Dropped below the last row (or on itself): send it to the end of the list.
            newIndex = cues.Count - 1;
        }

        newIndex = Math.Clamp(newIndex, 0, cues.Count - 1);
        if (newIndex != oldIndex)
            ViewModel.MoveCue(oldIndex, newIndex);
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T target) return target;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
