using System.Configuration;
using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NCHops;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public App()
    {
        // Global Exception Handler für unbehandelte Exceptions
        DispatcherUnhandledException += (s, e) =>
        {
            System.Diagnostics.Debug.WriteLine($"UNHANDLED EXCEPTION: {e.Exception}");
            System.Diagnostics.Debug.WriteLine($"StackTrace: {e.Exception.StackTrace}");

            MessageBox.Show(
                $"Ein kritischer Fehler ist aufgetreten:\n\n{e.Exception.GetType().Name}\n{e.Exception.Message}\n\nStackTrace:\n{e.Exception.StackTrace}",
                "Kritischer Fehler",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            e.Handled = true;  // Verhindere, dass die App abstürzt
        };
    }

    private void TextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is TextBox textBox && !textBox.IsKeyboardFocusWithin)
        {
            e.Handled = true;
            textBox.Focus();
        }
    }

    private void TextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox textBox)
            textBox.SelectAll();
    }
}

