using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace NCHops;

public partial class NutFräsenDialog : Window
{
    private readonly double _defaultZ;
    private bool _breiteAutoFill = true;

    public NutParams? Result { get; private set; }

    public NutFräsenDialog(double defaultZ, double defaultLaenge,
                            NutParams? prefill = null,
                            IReadOnlyList<Werkzeug>? werkzeuge = null)
    {
        InitializeComponent();
        _defaultZ = defaultZ;
        var inv = System.Globalization.CultureInfo.InvariantCulture;

        if (werkzeuge?.Count > 0)
        {
            CbWerkzeug.ItemsSource = werkzeuge;
            if (prefill == null) CbWerkzeug.SelectedIndex = 0;
        }

        if (prefill != null)
        {
            TxtXRel.Text   = prefill.XRel.ToString(inv);
            TxtYRel.Text   = prefill.YRel.ToString(inv);
            TxtLänge.Text  = prefill.Länge.ToString(inv);
            TxtBreite.Text = prefill.Breite.ToString(inv);
            TxtZTiefe.Text = prefill.ZTiefe.ToString(inv);
            SetBezug(prefill.Bezugspunkt);
            _breiteAutoFill = false;

            if (werkzeuge != null)
            {
                foreach (var w in werkzeuge)
                {
                    if (Math.Abs(w.Durchmesser - prefill.FraeserD) < 0.01)
                    {
                        CbWerkzeug.SelectedItem = w;
                        break;
                    }
                }
            }
        }
        else
        {
            TxtLänge.Text  = defaultLaenge.ToString(inv);
            TxtZTiefe.Text = defaultZ.ToString(inv);
            // Breite is filled by OnWerkzeugChanged via SelectedIndex = 0
        }

        TxtBreite.TextChanged += (_, _) => _breiteAutoFill = false;
    }

    private void OnWerkzeugChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_breiteAutoFill && CbWerkzeug.SelectedItem is Werkzeug w)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            TxtBreite.Text = w.Durchmesser.ToString(inv);
            _breiteAutoFill = true; // keep auto-fill active after our own write
        }
    }

    private void SetBezug(string bezug)
    {
        RbObenLinks.IsChecked    = bezug == "Oben links";
        RbObenMitte.IsChecked    = bezug == "Oben Mitte";
        RbObenRechts.IsChecked   = bezug == "Oben rechts";
        RbLinksMitte.IsChecked   = bezug == "Links Mitte";
        RbMitte.IsChecked        = bezug == "Mitte";
        RbRechtsMitte.IsChecked  = bezug == "Rechts Mitte";
        RbUntenLinks.IsChecked   = bezug == "Unten links";
        RbUntenMitte.IsChecked   = bezug == "Unten Mitte";
        RbUntenRechts.IsChecked  = bezug == "Unten rechts";
    }

    private string GetBezug()
    {
        if (RbObenLinks.IsChecked    == true) return "Oben links";
        if (RbObenMitte.IsChecked    == true) return "Oben Mitte";
        if (RbObenRechts.IsChecked   == true) return "Oben rechts";
        if (RbLinksMitte.IsChecked   == true) return "Links Mitte";
        if (RbMitte.IsChecked        == true) return "Mitte";
        if (RbRechtsMitte.IsChecked  == true) return "Rechts Mitte";
        if (RbUntenMitte.IsChecked   == true) return "Unten Mitte";
        if (RbUntenRechts.IsChecked  == true) return "Unten rechts";
        return "Unten links";
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var w = CbWerkzeug.SelectedItem as Werkzeug;
        Result = new NutParams(
            XRel:           double.Parse(TxtXRel.Text,    inv),
            YRel:           double.Parse(TxtYRel.Text,    inv),
            Länge:          double.Parse(TxtLänge.Text,   inv),
            Breite:         double.Parse(TxtBreite.Text,  inv),
            ZTiefe:         double.Parse(TxtZTiefe.Text,  inv),
            ZZustellung:    w?.ZZustellung ?? 5,
            FraeserD:       w?.Durchmesser ?? 10,
            Faktor:         w != null ? w.RaeumzustellungXY / 100.0 : 0.75,
            Vorschub:       w?.VorschubFxy ?? 3000,
            VorschubFz:     w?.VorschubFz ?? 500,
            Drehzahl:       w?.Drehzahl ?? 18000,
            Bezugspunkt:    GetBezug(),
            Eintauchwinkel: w?.Eintauchwinkel ?? 90
        );
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}

public record NutParams(
    double XRel, double YRel, double Länge, double Breite,
    double ZTiefe, double ZZustellung,
    double FraeserD, double Faktor,
    double Vorschub, double VorschubFz, double Drehzahl,
    string Bezugspunkt,
    double Eintauchwinkel = 90);
