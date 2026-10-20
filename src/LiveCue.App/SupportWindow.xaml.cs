using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace LiveCue.App;

/// <summary>
/// A small, non-modal "support the app" window shown once per launch (see MainWindow's
/// constructor): a Buy Me a Coffee QR code/link, styled in BMC's own brand colors. "No, grazie"
/// stays disabled for a few seconds - long enough to actually notice the QR code - rather than
/// being instantly dismissible.
/// </summary>
public partial class SupportWindow : Window
{
    private const string SupportUrl = "https://buymeacoffee.com/cin8";
    private const int WaitSeconds = 3;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private int _secondsLeft = WaitSeconds;

    public SupportWindow()
    {
        InitializeComponent();
        UpdateButtonText();
        _timer.Tick += (_, _) =>
        {
            _secondsLeft--;
            if (_secondsLeft <= 0)
            {
                _timer.Stop();
                NoThanksButton.IsEnabled = true;
                NoThanksButton.Content = "No, grazie";
            }
            else
            {
                UpdateButtonText();
            }
        };
        _timer.Start();
    }

    private void UpdateButtonText() => NoThanksButton.Content = $"No, grazie ({_secondsLeft})";

    private void NoThanksButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Link_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(SupportUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            App.LogError(ex);
        }
    }
}
