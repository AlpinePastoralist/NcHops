using System.Windows;

namespace NCHops;

public partial class RasterDialog : Window
{
    public double RasterX { get; private set; }
    public double RasterY { get; private set; }

    public RasterDialog(double currentX, double currentY)
    {
        InitializeComponent();
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        TxtRasterX.Text = currentX.ToString(inv);
        TxtRasterY.Text = currentY.ToString(inv);
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (!double.TryParse(TxtRasterX.Text, System.Globalization.NumberStyles.Float, inv, out var rx) || rx <= 0 ||
            !double.TryParse(TxtRasterY.Text, System.Globalization.NumberStyles.Float, inv, out var ry) || ry <= 0)
        {
            MessageBox.Show(this, "Bitte gültige positive Werte für X und Y eingeben.", "Fehler",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        RasterX = rx;
        RasterY = ry;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
