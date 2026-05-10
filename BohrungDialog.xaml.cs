using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace NCHops;

public partial class BohrungDialog : Window
{
    public BohrungParams? Result { get; private set; }

    public BohrungDialog(double defaultZ, BohrungParams? prefill = null,
                         IReadOnlyList<Werkzeug>? werkzeuge = null)
    {
        InitializeComponent();
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (werkzeuge?.Count > 0)
        {
            CbWerkzeug.ItemsSource = werkzeuge;
            if (prefill == null) CbWerkzeug.SelectedIndex = 0;
        }
        if (prefill != null)
        {
            TxtXRel.Text        = prefill.XRel.ToString(inv);
            TxtYRel.Text        = prefill.YRel.ToString(inv);
            TxtBohrtiefe.Text   = prefill.Bohrtiefe.ToString(inv);
            TxtZustellung.Text  = prefill.Zustellung.ToString(inv);
            TxtDurchmesser.Text = prefill.Durchmesser.ToString(inv);
            SetBezug(prefill.Bezugspunkt);
        }
        else
        {
            TxtBohrtiefe.Text = defaultZ.ToString(inv);
        }
    }

    private void SetBezug(string bezug)
    {
        RbObenLinks.IsChecked   = bezug == "Oben links";
        RbObenMitte.IsChecked   = bezug == "Oben Mitte";
        RbObenRechts.IsChecked  = bezug == "Oben rechts";
        RbLinksMitte.IsChecked  = bezug == "Links Mitte";
        RbMitte.IsChecked       = bezug == "Mitte";
        RbRechtsMitte.IsChecked = bezug == "Rechts Mitte";
        RbUntenLinks.IsChecked  = bezug == "Unten links";
        RbUntenMitte.IsChecked  = bezug == "Unten Mitte";
        RbUntenRechts.IsChecked = bezug == "Unten rechts";
    }

    private string GetBezug()
    {
        if (RbObenLinks.IsChecked   == true) return "Oben links";
        if (RbObenMitte.IsChecked   == true) return "Oben Mitte";
        if (RbObenRechts.IsChecked  == true) return "Oben rechts";
        if (RbLinksMitte.IsChecked  == true) return "Links Mitte";
        if (RbMitte.IsChecked       == true) return "Mitte";
        if (RbRechtsMitte.IsChecked == true) return "Rechts Mitte";
        if (RbUntenMitte.IsChecked  == true) return "Unten Mitte";
        if (RbUntenRechts.IsChecked == true) return "Unten rechts";
        return "Unten links";
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        Result = new BohrungParams(
            XRel:        double.Parse(TxtXRel.Text,        System.Globalization.CultureInfo.InvariantCulture),
            YRel:        double.Parse(TxtYRel.Text,        System.Globalization.CultureInfo.InvariantCulture),
            Bohrtiefe:   double.Parse(TxtBohrtiefe.Text,   System.Globalization.CultureInfo.InvariantCulture),
            Zustellung:  double.Parse(TxtZustellung.Text,  System.Globalization.CultureInfo.InvariantCulture),
            Durchmesser: double.Parse(TxtDurchmesser.Text, System.Globalization.CultureInfo.InvariantCulture),
            Bezugspunkt: GetBezug()
        );
        DialogResult = true;
    }

    private void OnWerkzeugSelected(object sender, SelectionChangedEventArgs e)
    {
        if (CbWerkzeug.SelectedItem is Werkzeug w)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            TxtDurchmesser.Text = w.Durchmesser.ToString(inv);
            TxtZustellung.Text  = w.ZZustellung.ToString(inv);
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}

public record BohrungParams(
    double XRel, double YRel, double Bohrtiefe, double Zustellung, double Durchmesser,
    string Bezugspunkt);
