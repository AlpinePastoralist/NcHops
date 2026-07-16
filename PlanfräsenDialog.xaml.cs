using System.Collections.Generic;
using System.Windows;

namespace NCHops;

public partial class PlanfräsenDialog : Window
{
    private readonly double _defaultX;
    private readonly double _defaultY;

    public PlanfräsenParams? Result { get; private set; }

    public PlanfräsenDialog(double defaultX, double defaultY, PlanfräsenParams? prefill = null,
                            IReadOnlyList<Werkzeug>? werkzeuge = null)
    {
        InitializeComponent();
        _defaultX = defaultX;
        _defaultY = defaultY;
        if (werkzeuge?.Count > 0)
        {
            CbWerkzeug.ItemsSource = werkzeuge;
            if (prefill == null || werkzeuge.Count == 1) CbWerkzeug.SelectedIndex = 0;
            CbWerkzeug.IsEnabled = werkzeuge.Count > 1;
        }
        if (prefill != null)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            RbHorizontal.IsChecked = prefill.Horizontal;
            RbVertikal.IsChecked   = !prefill.Horizontal;
            TxtX0.Text = prefill.X0.ToString(inv);
            TxtY0.Text = prefill.Y0.ToString(inv);
            TxtX1.Text = prefill.X1.ToString(inv);
            TxtY1.Text = prefill.Y1.ToString(inv);
            TxtZ.Text  = prefill.Z.ToString(inv);
        }
        else
        {
            TxtX1.Text = (_defaultX + 35).ToString(System.Globalization.CultureInfo.InvariantCulture);
            TxtY1.Text = (_defaultY + 35).ToString(System.Globalization.CultureInfo.InvariantCulture);
            UpdateOrientationDefaults();
        }
    }

    private void OnOrientationChanged(object sender, RoutedEventArgs e)
    {
        UpdateOrientationDefaults();
    }

    private void UpdateOrientationDefaults()
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (RbHorizontal.IsChecked == true)
        {
            TxtX0.Text = "-35";
            TxtY0.Text = "0";
            TxtY1.Text = _defaultY.ToString(inv);
            TxtX1.Text = (_defaultX + 35).ToString(inv);
        }
        else
        {
            TxtX0.Text = "0";
            TxtY0.Text = "-35";
            TxtX1.Text = _defaultX.ToString(inv);
            TxtY1.Text = (_defaultY + 35).ToString(inv);
        }
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var w = CbWerkzeug.SelectedItem as Werkzeug;
        Result = new PlanfräsenParams(
            X0: double.Parse(TxtX0.Text, inv),
            Y0: double.Parse(TxtY0.Text, inv),
            X1: double.Parse(TxtX1.Text, inv),
            Y1: double.Parse(TxtY1.Text, inv),
            Z: double.Parse(TxtZ.Text, inv),
            FraeserD: w?.Durchmesser ?? 10,
            Faktor: w != null ? w.RaeumzustellungXY / 100.0 : 0.75,
            Vorschub:   w?.VorschubFxy ?? 3000,
            VorschubFz: w?.VorschubFz ?? 500,
            Drehzahl:   w?.Drehzahl ?? 18000,
            Horizontal: RbHorizontal.IsChecked == true
        );
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}

public record PlanfräsenParams(
    double X0, double Y0, double X1, double Y1,
    double Z, double FraeserD, double Faktor,
    double Vorschub, double VorschubFz, double Drehzahl, bool Horizontal);
