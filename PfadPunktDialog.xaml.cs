using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace NCHops;

public partial class PfadPunktDialog : Window
{
    public PfadPunktParams? Result { get; private set; }

    public PfadPunktDialog(string title, double defaultZ, bool isStart = false, PfadPunktParams? prefill = null,
                           IReadOnlyList<Werkzeug>? werkzeuge = null)
    {
        InitializeComponent();
        Title = title;
        if (werkzeuge?.Count > 0)
        {
            CbWerkzeug.ItemsSource = werkzeuge;
            if (prefill == null) CbWerkzeug.SelectedIndex = 0;
        }
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (!isStart)
        {
            LblZ.Visibility          = Visibility.Collapsed;
            TxtZ.Visibility          = Visibility.Collapsed;
            LblZustellung.Visibility = Visibility.Collapsed;
            TxtZustellung.Visibility = Visibility.Collapsed;
            LblFraeserD.Visibility        = Visibility.Collapsed;
            TxtFraeserD.Visibility        = Visibility.Collapsed;
            LblDrehzahl.Visibility        = Visibility.Collapsed;
            TxtDrehzahl.Visibility        = Visibility.Collapsed;
            LblVorschub.Visibility        = Visibility.Collapsed;
            TxtVorschub.Visibility        = Visibility.Collapsed;
            LblRadiuskorrektur.Visibility = Visibility.Collapsed;
            CbRadiuskorrektur.Visibility  = Visibility.Collapsed;
            RbLetzterPunkt.Visibility     = Visibility.Visible;
        }
        if (prefill != null)
        {
            TxtXRel.Text       = prefill.XRel.ToString(inv);
            TxtYRel.Text       = prefill.YRel.ToString(inv);
            TxtZ.Text          = prefill.ZTiefe.ToString(inv);
            TxtZustellung.Text = prefill.ZZustellung.ToString(inv);
            TxtFraeserD.Text   = prefill.FraeserD.ToString(inv);
            TxtDrehzahl.Text   = prefill.Drehzahl.ToString(inv);
            TxtVorschub.Text   = prefill.Vorschub.ToString(inv);
            CbRadiuskorrektur.SelectedIndex = prefill.Radiuskorrektur switch
            {
                "Links"  => 0,
                "Rechts" => 2,
                _        => 1
            };
            SetBezug(prefill.Bezugspunkt);
        }
        else
        {
            TxtZ.Text = defaultZ.ToString(inv);
            if (!isStart)
                RbLetzterPunkt.IsChecked = true;
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
        RbLetzterPunkt.IsChecked = bezug == "Letzter Punkt";
    }

    private string GetBezug()
    {
        if (RbLetzterPunkt.IsChecked == true) return "Letzter Punkt";
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
        bool vis = TxtZ.Visibility == Visibility.Visible;
        string radiuskorrektur = CbRadiuskorrektur.SelectedIndex switch { 0 => "Links", 2 => "Rechts", _ => "Mittig" };
        Result = new PfadPunktParams(
            XRel:             double.Parse(TxtXRel.Text, inv),
            YRel:             double.Parse(TxtYRel.Text, inv),
            ZTiefe:           vis ? double.Parse(TxtZ.Text,          inv) : 0,
            ZZustellung:      vis ? double.Parse(TxtZustellung.Text, inv) : 5,
            FraeserD:         vis ? double.Parse(TxtFraeserD.Text,   inv) : 0,
            Drehzahl:         vis ? double.Parse(TxtDrehzahl.Text,   inv) : 18000,
            Vorschub:         vis ? double.Parse(TxtVorschub.Text,   inv) : 1000,
            Radiuskorrektur:  vis ? radiuskorrektur : "Mittig",
            Bezugspunkt:      GetBezug(),
            Typ:              PfadPunktTyp.Start // wird vom Aufrufer überschrieben
        );
        DialogResult = true;
    }

    private void OnWerkzeugSelected(object sender, SelectionChangedEventArgs e)
    {
        if (CbWerkzeug.SelectedItem is Werkzeug w && TxtFraeserD.Visibility == Visibility.Visible)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            TxtFraeserD.Text   = w.Durchmesser.ToString(inv);
            TxtZustellung.Text = w.ZZustellung.ToString(inv);
            TxtDrehzahl.Text   = w.Drehzahl.ToString(inv);
            TxtVorschub.Text   = w.VorschubFxy.ToString(inv);
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}

public enum PfadPunktTyp { Start, Punkt }

public record PfadPunktParams(
    double XRel, double YRel, double ZTiefe, double ZZustellung,
    double FraeserD, double Drehzahl, double Vorschub,
    string Radiuskorrektur, string Bezugspunkt, PfadPunktTyp Typ);
