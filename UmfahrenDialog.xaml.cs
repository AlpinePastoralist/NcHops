using System.Windows;
using System.Windows.Controls;

namespace NCHops;

public partial class UmfahrenDialog : Window
{
    public UmfahrenParams? Result { get; private set; }

    public UmfahrenDialog(double workThickness, UmfahrenParams? prefill = null)
    {
        InitializeComponent();
        if (prefill != null)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            TxtA.Text        = prefill.A.ToString(inv);
            TxtRadius.Text   = prefill.Radius.ToString(inv);
            TxtZ.Text        = prefill.Z.ToString(inv);
            TxtDiameter.Text = prefill.Diameter.ToString(inv);
            TxtDrehzahl.Text = prefill.Drehzahl.ToString(inv);
            foreach (System.Windows.Controls.ComboBoxItem item in CbStartSide.Items)
                if (item.Content.ToString() == prefill.StartSide) { CbStartSide.SelectedItem = item; break; }
            foreach (System.Windows.Controls.ComboBoxItem item in CbDirection.Items)
                if (item.Content.ToString() == prefill.Direction) { CbDirection.SelectedItem = item; break; }
        }
        else
        {
            TxtZ.Text        = (-workThickness - 3).ToString(System.Globalization.CultureInfo.InvariantCulture);
            TxtDiameter.Text = "10";
            TxtDrehzahl.Text = "18000";
        }
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        Result = new UmfahrenParams(
            A: double.Parse(TxtA.Text, System.Globalization.CultureInfo.InvariantCulture),
            StartSide: (CbStartSide.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "oben",
            Direction: (CbDirection.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "gegenlauf",
            Radius: double.Parse(TxtRadius.Text, System.Globalization.CultureInfo.InvariantCulture),
            Z: double.Parse(TxtZ.Text, System.Globalization.CultureInfo.InvariantCulture),
            Diameter: double.Parse(TxtDiameter.Text, System.Globalization.CultureInfo.InvariantCulture),
            Drehzahl: double.Parse(TxtDrehzahl.Text, System.Globalization.CultureInfo.InvariantCulture)
        );
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}

public record UmfahrenParams(double A, string StartSide, string Direction, double Radius, double Z, double Diameter, double Drehzahl);
