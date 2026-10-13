using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LiveCue.Core.Models;

namespace LiveCue.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private const string CueRowFormat = "CueRow";

    private MainViewModel ViewModel => (MainViewModel)DataContext;
    private MonitorManagerWindow? _monitorWindow;
    private MonitorPreviewWindow? _monitorPreviewWindow;
    private Point _dragStartPoint;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }

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
            cues.Move(oldIndex, newIndex);
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
