using System.Windows;
using LiveCue.Core.Models;

namespace LiveCue.App;

/// <summary>
/// Standalone monitor management window: lists physical monitors, lets you change a
/// monitor's resolution, and lets you create "virtual monitors" (custom-resolution regions
/// living on top of a real monitor) that cues can target just like a real screen.
/// </summary>
public partial class MonitorManagerWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;

    public MonitorManagerWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
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
