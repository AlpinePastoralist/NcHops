using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

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
            if (prefill == null) CbWerkzeug.SelectedIndex = 0;
        }
        if (prefill != null)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            RbHorizontal.IsChecked = prefill.Horizontal;
            RbVertikal.IsChecked   = !prefill.Horizontal;
            TxtX0.Text      = prefill.X0.ToString(inv);
            TxtY0.Text      = prefill.Y0.ToString(inv);
            TxtX1.Text      = prefill.X1.ToString(inv);
            TxtY1.Text      = prefill.Y1.ToString(inv);
            TxtZ.Text       = prefill.Z.ToString(inv);
            TxtFraeserD.Text= prefill.FraeserD.ToString(inv);
            TxtFaktor.Text  = prefill.Faktor.ToString(inv);
            TxtVorschub.Text= prefill.Vorschub.ToString(inv);
            TxtDrehzahl.Text= prefill.Drehzahl.ToString(inv);
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
        Result = new PlanfräsenParams(
            X0: double.Parse(TxtX0.Text, System.Globalization.CultureInfo.InvariantCulture),
            Y0: double.Parse(TxtY0.Text, System.Globalization.CultureInfo.InvariantCulture),
            X1: double.Parse(TxtX1.Text, System.Globalization.CultureInfo.InvariantCulture),
            Y1: double.Parse(TxtY1.Text, System.Globalization.CultureInfo.InvariantCulture),
            Z: double.Parse(TxtZ.Text, System.Globalization.CultureInfo.InvariantCulture),
            FraeserD: double.Parse(TxtFraeserD.Text, System.Globalization.CultureInfo.InvariantCulture),
            Faktor: double.Parse(TxtFaktor.Text, System.Globalization.CultureInfo.InvariantCulture),
            Vorschub: double.Parse(TxtVorschub.Text, System.Globalization.CultureInfo.InvariantCulture),
            Drehzahl: double.Parse(TxtDrehzahl.Text, System.Globalization.CultureInfo.InvariantCulture),
            Horizontal: RbHorizontal.IsChecked == true
        );
        DialogResult = true;
    }

    private void OnWerkzeugSelected(object sender, SelectionChangedEventArgs e)
    {
        if (CbWerkzeug.SelectedItem is Werkzeug w)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            TxtFraeserD.Text = w.Durchmesser.ToString(inv);
            TxtFaktor.Text   = (w.RaeumzustellungXY / 100.0).ToString(inv);
            TxtVorschub.Text = w.VorschubFxy.ToString(inv);
            TxtDrehzahl.Text = w.Drehzahl.ToString(inv);
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}

public record PlanfräsenParams(
    double X0, double Y0, double X1, double Y1,
    double Z, double FraeserD, double Faktor,
    double Vorschub, double Drehzahl, bool Horizontal);
