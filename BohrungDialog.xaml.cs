using System.Windows;
using System.Windows.Controls;

namespace NCHops;

public partial class BohrungDialog : Window
{
    public BohrungParams? Result { get; private set; }

    public BohrungDialog(double defaultZ)
    {
        InitializeComponent();
        TxtBohrtiefe.Text = defaultZ.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var bezugs = new List<string>();
        if (CbUntenLinks.IsChecked == true)  bezugs.Add("Unten links");
        if (CbObenLinks.IsChecked == true)   bezugs.Add("Oben links");
        if (CbUntenRechts.IsChecked == true) bezugs.Add("Unten rechts");
        if (CbObenRechts.IsChecked == true)  bezugs.Add("Oben rechts");
        if (CbLinksMitte.IsChecked == true)  bezugs.Add("Links Mitte");
        if (CbRechtsMitte.IsChecked == true) bezugs.Add("Rechts Mitte");
        if (CbObenMitte.IsChecked == true)   bezugs.Add("Oben Mitte");
        if (CbUntenMitte.IsChecked == true)  bezugs.Add("Unten Mitte");
        if (CbMitteMitte.IsChecked == true)  bezugs.Add("Mitte");

        Result = new BohrungParams(
            XRel: double.Parse(TxtXRel.Text, System.Globalization.CultureInfo.InvariantCulture),
            YRel: double.Parse(TxtYRel.Text, System.Globalization.CultureInfo.InvariantCulture),
            Bohrtiefe: double.Parse(TxtBohrtiefe.Text, System.Globalization.CultureInfo.InvariantCulture),
            Zustellung: double.Parse(TxtZustellung.Text, System.Globalization.CultureInfo.InvariantCulture),
            Durchmesser: double.Parse(TxtDurchmesser.Text, System.Globalization.CultureInfo.InvariantCulture),
            Bezugspunkte: bezugs
        );
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}

public record BohrungParams(
    double XRel, double YRel, double Bohrtiefe, double Zustellung, double Durchmesser,
    List<string> Bezugspunkte);
