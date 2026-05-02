using System.Windows;

namespace NCHops;

public partial class PlanfräsenDialog : Window
{
    public PlanfräsenParams? Result { get; private set; }

    public PlanfräsenDialog(double defaultX, double defaultY)
    {
        InitializeComponent();
        TxtX1.Text = (defaultX + 35).ToString();
        TxtY1.Text = (defaultY + 35).ToString();
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
