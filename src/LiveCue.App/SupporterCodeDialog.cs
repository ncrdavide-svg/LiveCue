// Same file in every app of the suite; the .NET 9 apps have nullable annotations on, this code doesn't use them.
#if NETCOREAPP
#nullable disable
#endif
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LiveCue.App
{
    /// <summary>
    /// "Ho un codice sostenitore" from the support window: the supporter pastes the code they
    /// received, and it is saved in %APPDATA%\LIVE for every app of the suite (see SupporterCode).
    /// Built in code, not XAML: it is the same file in every WPF app of the suite.
    /// </summary>
    public static class SupporterCodeDialog
    {
        /// <summary>True if a valid code was saved.</summary>
        public static bool Ask(Window owner)
        {
            var ink = new SolidColorBrush(Color.FromRgb(0x0D, 0x0C, 0x22));
            var box = new TextBox
            {
                AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 90, Margin = new Thickness(0, 8, 0, 14),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Consolas"), FontSize = 12,
            };
            var ok = new Button
            {
                Content = "Attiva", Padding = new Thickness(18, 6, 18, 6), Margin = new Thickness(0, 0, 8, 0),
                Background = ink, Foreground = Brushes.White, BorderThickness = new Thickness(0),
            };
            var cancel = new Button { Content = "Annulla", IsCancel = true, Padding = new Thickness(18, 6, 18, 6) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            var panel = new StackPanel { Margin = new Thickness(18) };
            panel.Children.Add(new TextBlock
            {
                Text = "Incolla il codice sostenitore che hai ricevuto (va bene anche tutto il testo del file sostenitore.txt).",
                TextWrapping = TextWrapping.Wrap, Foreground = ink,
            });
            panel.Children.Add(box);
            panel.Children.Add(buttons);
            var dialog = new Window
            {
                Title = "Codice sostenitore", Owner = owner, Width = 440, SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false, FontFamily = new FontFamily("Segoe UI"), Content = panel,
                Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xDD, 0x00)),
            };
            ok.Click += (s, e) =>
            {
                string name;
                try
                {
                    name = SupporterCode.Save(box.Text);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(dialog, "Non riesco a salvare il codice: " + ex.Message, dialog.Title,
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (name == null)
                {
                    MessageBox.Show(dialog, "Questo codice non \u00e8 valido. Controlla di averlo copiato tutto, firma compresa.",
                        dialog.Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                MessageBox.Show(dialog, "Grazie, " + name + "!\n\nDa ora la finestra del caff\u00e8 non comparir\u00e0 pi\u00f9, " +
                    "in tutte le app della suite LIVE.", dialog.Title, MessageBoxButton.OK, MessageBoxImage.Information);
                dialog.DialogResult = true;
            };
            box.Focus();
            return dialog.ShowDialog() == true;
        }
    }
}
