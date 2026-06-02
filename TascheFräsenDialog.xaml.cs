using System.Collections.Generic;
using System.Windows;

namespace NCHops;

public partial class TascheFräsenDialog : Window
{
    private readonly double _defaultZ;

    public TascheFräsenParams? Result { get; private set; }

    public TascheFräsenDialog(double defaultZ, TascheFräsenParams? prefill = null,
                               IReadOnlyList<Werkzeug>? werkzeuge = null)
    {
        InitializeComponent();
        _defaultZ = defaultZ;
        if (werkzeuge?.Count > 0)
        {
            CbWerkzeug.ItemsSource = werkzeuge;
            if (prefill == null) CbWerkzeug.SelectedIndex = 0;
        }
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (prefill != null)
        {
            TxtXRel.Text   = prefill.XRel.ToString(inv);
            TxtYRel.Text   = prefill.YRel.ToString(inv);
            TxtBreite.Text = prefill.Breite.ToString(inv);
            TxtHöhe.Text   = prefill.Höhe.ToString(inv);
            TxtZTiefe.Text = prefill.ZTiefe.ToString(inv);
            SetBezug(prefill.Bezugspunkt);
        }
        else
        {
            TxtZTiefe.Text = _defaultZ.ToString(inv);
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
        Result = new TascheFräsenParams(
            XRel:           double.Parse(TxtXRel.Text,   inv),
            YRel:           double.Parse(TxtYRel.Text,   inv),
            Breite:         double.Parse(TxtBreite.Text, inv),
            Höhe:           double.Parse(TxtHöhe.Text,   inv),
            ZTiefe:         double.Parse(TxtZTiefe.Text, inv),
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

public record TascheFräsenParams(
    double XRel, double YRel, double Breite, double Höhe,
    double ZTiefe, double ZZustellung,
    double FraeserD, double Faktor,
    double Vorschub, double VorschubFz, double Drehzahl,
    string Bezugspunkt,
    double Eintauchwinkel = 90,
    double Verrundung = 0);
