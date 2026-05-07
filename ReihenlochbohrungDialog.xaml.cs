using System.Windows;

namespace NCHops;

public partial class ReihenlochbohrungDialog : Window
{
    public ReihenlochbohrungParams? Result { get; private set; }

    public ReihenlochbohrungDialog(double defaultZ, ReihenlochbohrungParams? prefill = null)
    {
        InitializeComponent();
        if (prefill != null)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            TxtStartX.Text    = prefill.StartX.ToString(inv);
            TxtStartY.Text    = prefill.StartY.ToString(inv);
            TxtCountX.Text    = prefill.CountX.ToString(inv);
            TxtCountY.Text    = prefill.CountY.ToString(inv);
            TxtSpacingX.Text  = prefill.SpacingX.ToString(inv);
            TxtSpacingY.Text  = prefill.SpacingY.ToString(inv);
            TxtDiameter.Text  = prefill.Diameter.ToString(inv);
            TxtBohrtiefe.Text = prefill.Bohrtiefe.ToString(inv);
            TxtZustellung.Text= prefill.Zustellung.ToString(inv);
        }
        else
        {
            TxtBohrtiefe.Text = defaultZ.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        Result = new ReihenlochbohrungParams(
            StartX: double.Parse(TxtStartX.Text, System.Globalization.CultureInfo.InvariantCulture),
            StartY: double.Parse(TxtStartY.Text, System.Globalization.CultureInfo.InvariantCulture),
            CountX: int.Parse(TxtCountX.Text, System.Globalization.CultureInfo.InvariantCulture),
            CountY: int.Parse(TxtCountY.Text, System.Globalization.CultureInfo.InvariantCulture),
            SpacingX: double.Parse(TxtSpacingX.Text, System.Globalization.CultureInfo.InvariantCulture),
            SpacingY: double.Parse(TxtSpacingY.Text, System.Globalization.CultureInfo.InvariantCulture),
            Diameter: double.Parse(TxtDiameter.Text, System.Globalization.CultureInfo.InvariantCulture),
            Bohrtiefe: double.Parse(TxtBohrtiefe.Text, System.Globalization.CultureInfo.InvariantCulture),
            Zustellung: double.Parse(TxtZustellung.Text, System.Globalization.CultureInfo.InvariantCulture)
        );
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}

public record ReihenlochbohrungParams(
    double StartX, double StartY,
    int CountX, int CountY,
    double SpacingX, double SpacingY,
    double Diameter, double Bohrtiefe, double Zustellung);
