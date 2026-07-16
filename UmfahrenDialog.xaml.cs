using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls; // ComboBoxItem

namespace NCHops;

public partial class UmfahrenDialog : Window
{
    public UmfahrenParams? Result { get; private set; }

    public UmfahrenDialog(double workThickness, UmfahrenParams? prefill = null,
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
            TxtA.Text      = prefill.A.ToString(inv);
            TxtRadius.Text = prefill.Radius.ToString(inv);
            TxtZ.Text      = prefill.Z.ToString(inv);
            foreach (System.Windows.Controls.ComboBoxItem item in CbStartSide.Items)
                if (item.Content.ToString() == prefill.StartSide) { CbStartSide.SelectedItem = item; break; }
            foreach (System.Windows.Controls.ComboBoxItem item in CbDirection.Items)
                if (item.Content.ToString() == prefill.Direction) { CbDirection.SelectedItem = item; break; }
            ChkMehrfach.IsChecked = prefill.MehrfachZustellung;
        }
        else
        {
            TxtZ.Text = (-workThickness - 3).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var w = CbWerkzeug.SelectedItem as Werkzeug;
        bool mehrfach = ChkMehrfach.IsChecked == true;
        Result = new UmfahrenParams(
            A: double.Parse(TxtA.Text, inv),
            StartSide: (CbStartSide.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "oben",
            Direction: (CbDirection.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "gegenlauf",
            Radius: double.Parse(TxtRadius.Text, inv),
            Z: double.Parse(TxtZ.Text, inv),
            Diameter:    w?.Durchmesser ?? 10,
            Drehzahl:    w?.Drehzahl ?? 18000,
            VorschubFxy: w?.VorschubFxy ?? 3000,
            VorschubFz:  w?.VorschubFz ?? 500,
            MehrfachZustellung: mehrfach,
            ZZustellung: mehrfach ? (w?.ZZustellung ?? 0) : 0
        );
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}

public record UmfahrenParams(double A, string StartSide, string Direction, double Radius, double Z,
    double Diameter, double Drehzahl, double VorschubFxy, double VorschubFz,
    bool MehrfachZustellung = false, double ZZustellung = 0);
