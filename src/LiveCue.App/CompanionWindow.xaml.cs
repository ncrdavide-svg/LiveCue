using System.Windows;

namespace LiveCue.App;

/// <summary>
/// Standalone window for the Companion/Stream Deck remote control: port the HTTP server listens
/// on, status, and the "Esporta per Companion..." button that writes a ready-to-import
/// .companionconfig page (see CompanionExport). The server itself is always running (started in
/// MainViewModel's constructor) - this window is only for configuring/exporting it, not for
/// turning it on and off.
/// </summary>
public partial class CompanionWindow : Window
{
    public CompanionWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
