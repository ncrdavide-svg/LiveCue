using System.Windows;
using System.Windows.Threading;
using LiveCue.Core.Models;
using LiveCue.Core.Services;

namespace LiveCue.App;

/// <summary>
/// Standalone monitor management window: lists physical monitors, lets you change a
/// monitor's resolution, and lets you create "virtual monitors" (custom-resolution regions
/// living on top of a real monitor) that cues can target just like a real screen.
/// </summary>
public partial class MonitorManagerWindow : Window
{
    private static readonly TimeSpan IdentifyDuration = TimeSpan.FromSeconds(4);

    private MainViewModel ViewModel => (MainViewModel)DataContext;

    public MonitorManagerWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    /// <summary>
    /// Flashes LiveCue's own "Monitor N" number on every physical screen for a few seconds -
    /// Windows' own Settings numbering can (and, on at least one real machine, does) disagree
    /// with ours, since the two come from different, independently-numbered Windows APIs. This
    /// lets the operator match a physical screen to our "Schermo" picker without needing Windows'
    /// numbers to agree with it at all.
    /// </summary>
    private void IdentifyMonitors_Click(object sender, RoutedEventArgs e)
    {
        var overlays = ViewModel.Displays
            .Select(d => new IdentifyOverlayWindow(new ScreenBounds(d.X, d.Y, d.Width, d.Height), d.DisplayNumber))
            .ToList();

        foreach (var overlay in overlays)
            overlay.Show();

        var timer = new DispatcherTimer { Interval = IdentifyDuration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            foreach (var overlay in overlays)
                overlay.Close();
        };
        timer.Start();
    }

    private void PresetFullHd_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.NewVirtualWidth = 1920;
        ViewModel.NewVirtualHeight = 1080;
    }

    private void PresetHd_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.NewVirtualWidth = 1280;
        ViewModel.NewVirtualHeight = 720;
    }

    private void Preset4K_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.NewVirtualWidth = 3840;
        ViewModel.NewVirtualHeight = 2160;
    }

    private void ScreenTest_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not DisplayInfo display) return;
        var bounds = new ScreenBounds(display.X, display.Y, display.Width, display.Height);
        new ScreenTestWindow(bounds, display.Label).Show();
    }

    private void RenameMonitor_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is DisplayInfo display)
            ViewModel.BeginRenameMonitor(display);
    }

    private void MonitorEnabled_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is DisplayInfo display)
            ViewModel.ToggleMonitorEnabled(display);
    }

    private void EditVirtualDisplay_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is VirtualDisplay virtualDisplay)
            ViewModel.BeginEditVirtualDisplay(virtualDisplay);
    }

    private void RemoveVirtualDisplay_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is VirtualDisplay virtualDisplay)
            ViewModel.RemoveVirtualDisplay(virtualDisplay);
    }
}
