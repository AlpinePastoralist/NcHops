using System.Collections.Generic;
using System.Windows;

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
            if (prefill == null || werkzeuge.Count == 1) CbWerkzeug.SelectedIndex = 0;
            CbWerkzeug.IsEnabled = werkzeuge.Count > 1;
        }
        if (prefill != null)
        {
            TxtXRel.Text      = prefill.XRel.ToString(inv);
            TxtYRel.Text      = prefill.YRel.ToString(inv);
            TxtBohrtiefe.Text = prefill.Bohrtiefe.ToString(inv);
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
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var w = CbWerkzeug.SelectedItem as Werkzeug;
        Result = new BohrungParams(
            XRel:        double.Parse(TxtXRel.Text,      inv),
            YRel:        double.Parse(TxtYRel.Text,      inv),
            Bohrtiefe:   double.Parse(TxtBohrtiefe.Text, inv),
            Zustellung:  w?.ZZustellung ?? 5,
            Durchmesser: w?.Durchmesser ?? 10,
            VorschubFz:  w?.VorschubFz ?? 500,
            Drehzahl:    w?.Drehzahl ?? 20000,
            Bezugspunkt: GetBezug()
        );
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}

public record BohrungParams(
    double XRel, double YRel, double Bohrtiefe, double Zustellung, double Durchmesser,
    double VorschubFz, double Drehzahl, string Bezugspunkt);
