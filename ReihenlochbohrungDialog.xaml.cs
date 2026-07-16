using System.Collections.Generic;
using System.Windows;

namespace NCHops;

public partial class ReihenlochbohrungDialog : Window
{
    public ReihenlochbohrungParams? Result { get; private set; }

    public ReihenlochbohrungDialog(double defaultZ, ReihenlochbohrungParams? prefill = null,
                                   IReadOnlyList<Werkzeug>? werkzeuge = null)
    {
        InitializeComponent();
        if (werkzeuge?.Count > 0)
        {
            CbWerkzeug.ItemsSource = werkzeuge;
            if (prefill == null || werkzeuge.Count == 1) CbWerkzeug.SelectedIndex = 0;
            CbWerkzeug.IsEnabled = werkzeuge.Count > 1;
        }
        if (prefill != null)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            TxtStartX.Text    = prefill.StartX.ToString(inv);
            TxtStartY.Text    = prefill.StartY.ToString(inv);
            TxtCountX.Text    = prefill.CountX.ToString(inv);
            TxtCountY.Text    = prefill.CountY.ToString(inv);
            TxtSpacingX.Text  = prefill.SpacingX.ToString(inv);
            TxtSpacingY.Text  = prefill.SpacingY.ToString(inv);
            TxtBohrtiefe.Text = prefill.Bohrtiefe.ToString(inv);
        }
        else
        {
            TxtBohrtiefe.Text = defaultZ.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var w = CbWerkzeug.SelectedItem as Werkzeug;
        Result = new ReihenlochbohrungParams(
            StartX:    double.Parse(TxtStartX.Text,   inv),
            StartY:    double.Parse(TxtStartY.Text,   inv),
            CountX:    int.Parse(TxtCountX.Text,      inv),
            CountY:    int.Parse(TxtCountY.Text,      inv),
            SpacingX:  double.Parse(TxtSpacingX.Text, inv),
            SpacingY:  double.Parse(TxtSpacingY.Text, inv),
            Diameter:   w?.Durchmesser ?? 5,
            Bohrtiefe:  double.Parse(TxtBohrtiefe.Text, inv),
            Zustellung: w?.ZZustellung ?? 10,
            VorschubFz: w?.VorschubFz ?? 500,
            Drehzahl:   w?.Drehzahl ?? 20000
        );
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}

public record ReihenlochbohrungParams(
    double StartX, double StartY,
    int CountX, int CountY,
    double SpacingX, double SpacingY,
    double Diameter, double Bohrtiefe, double Zustellung, double VorschubFz, double Drehzahl);
