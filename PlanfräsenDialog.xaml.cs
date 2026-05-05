using System.Windows;

namespace NCHops;

public partial class PlanfräsenDialog : Window
{
    private readonly double _defaultX;
    private readonly double _defaultY;

    public PlanfräsenParams? Result { get; private set; }

    public PlanfräsenDialog(double defaultX, double defaultY)
    {
        InitializeComponent();
        _defaultX = defaultX;
        _defaultY = defaultY;
        TxtX1.Text = (_defaultX + 35).ToString(System.Globalization.CultureInfo.InvariantCulture);
        TxtY1.Text = (_defaultY + 35).ToString(System.Globalization.CultureInfo.InvariantCulture);
        UpdateOrientationDefaults();
    }

    private void OnOrientationChanged(object sender, RoutedEventArgs e)
    {
        UpdateOrientationDefaults();
    }

    private void UpdateOrientationDefaults()
    {
        if (RbHorizontal.IsChecked == true)
        {
            TxtY1.Text = _defaultY.ToString(System.Globalization.CultureInfo.InvariantCulture);
            TxtX1.Text = (_defaultX + 35).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        else
        {
            TxtX1.Text = _defaultX.ToString(System.Globalization.CultureInfo.InvariantCulture);
            TxtY1.Text = (_defaultY + 35).ToString(System.Globalization.CultureInfo.InvariantCulture);
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

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}

public record PlanfräsenParams(
    double X0, double Y0, double X1, double Y1,
    double Z, double FraeserD, double Faktor,
    double Vorschub, double Drehzahl, bool Horizontal);
