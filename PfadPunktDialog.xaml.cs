using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls; // ComboBoxItem

namespace NCHops;

public partial class PfadPunktDialog : Window
{
    public PfadPunktParams? Result { get; private set; }

    public PfadPunktDialog(string title, double defaultZ, bool isStart = false, PfadPunktParams? prefill = null,
                           IReadOnlyList<Werkzeug>? werkzeuge = null, bool isBogen = false)
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
            LblZ.Visibility               = Visibility.Collapsed;
            TxtZ.Visibility               = Visibility.Collapsed;
            LblRadiuskorrektur.Visibility  = Visibility.Collapsed;
            CbRadiuskorrektur.Visibility   = Visibility.Collapsed;
            RbLetzterPunkt.Visibility      = Visibility.Visible;
        }
        if (isBogen)
        {
            LblBogenModus.Visibility = Visibility.Visible;
            CbBogenModus.Visibility  = Visibility.Visible;
            LblXMid.Visibility       = Visibility.Visible;
            TxtXMid.Visibility       = Visibility.Visible;
        }
        if (prefill != null)
        {
            TxtXRel.Text = prefill.XRel.ToString(inv);
            TxtYRel.Text = prefill.YRel.ToString(inv);
            TxtZ.Text    = prefill.ZTiefe.ToString(inv);
            CbRadiuskorrektur.SelectedIndex = prefill.Radiuskorrektur switch
            {
                "Links"  => 0,
                "Rechts" => 2,
                _        => 1
            };
            SetBezug(prefill.Bezugspunkt);
            if (isBogen)
            {
                // Modus setzen — triggert OnBogenModusChanged → passt Labels/Sichtbarkeit an
                CbBogenModus.SelectedIndex = prefill.BogenModus switch
                {
                    "Radius"    => 1,
                    "Pfeilhöhe" => 2,
                    _           => 0
                };
                TxtXMid.Text = prefill.XMid.ToString(inv);
                TxtYMid.Text = prefill.YMid.ToString(inv);
            }
        }
        else
        {
            TxtZ.Text = defaultZ.ToString(inv);
            if (!isStart)
                RbLetzterPunkt.IsChecked = true;
            if (isBogen)
                CbBogenModus.SelectedIndex = 2; // Pfeilhöhe als Standard
        }
    }

    private void OnBogenModusChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (CbBogenModus == null || LblXMid == null) return;
        string modus = GetBogenModus();
        bool isMitte = modus == "Bogenmitte";
        LblXMid.Content     = modus switch
        {
            "Radius"    => "Radius (mm):",
            "Pfeilhöhe" => "Pfeilhöhe (mm):",
            _           => "Bogen X-Mitte:"
        };
        // Bezugspunkt nur für Bogenmitte relevant (Radius/Pfeilhöhe sind absolute Werte)
        LblBezugHinweis.Visibility = isMitte ? Visibility.Collapsed : Visibility.Visible;
        LblYMid.Visibility  = isMitte ? Visibility.Visible : Visibility.Collapsed;
        TxtYMid.Visibility  = isMitte ? Visibility.Visible : Visibility.Collapsed;
    }

    private string GetBogenModus() => CbBogenModus.SelectedIndex switch
    {
        1 => "Radius",
        2 => "Pfeilhöhe",
        _ => "Bogenmitte"
    };

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
        bool vis    = TxtZ.Visibility  == Visibility.Visible;
        bool hasMid = TxtXMid.Visibility == Visibility.Visible;
        bool isMitte = GetBogenModus() == "Bogenmitte";
        var w = CbWerkzeug.SelectedItem as Werkzeug;
        string radiuskorrektur = CbRadiuskorrektur.SelectedIndex switch { 0 => "Links", 2 => "Rechts", _ => "Mittig" };
        Result = new PfadPunktParams(
            XRel:            double.Parse(TxtXRel.Text, inv),
            YRel:            double.Parse(TxtYRel.Text, inv),
            ZTiefe:          vis ? double.Parse(TxtZ.Text, inv) : 0,
            ZZustellung:     w?.ZZustellung ?? 5,
            FraeserD:        w?.Durchmesser ?? 10,
            Drehzahl:        w?.Drehzahl ?? 18000,
            Vorschub:        w?.VorschubFxy ?? 3000,
            VorschubFz:      w?.VorschubFz ?? 500,
            Radiuskorrektur: vis ? radiuskorrektur : "Mittig",
            Bezugspunkt:     GetBezug(),
            Typ:             PfadPunktTyp.Start, // wird vom Aufrufer überschrieben
            Eintauchwinkel:  w?.Eintauchwinkel ?? 90,
            XMid:            hasMid ? double.Parse(TxtXMid.Text, inv) : 0,
            YMid:            hasMid && isMitte ? double.Parse(TxtYMid.Text, inv) : 0,
            BogenModus:      hasMid ? GetBogenModus() : "Bogenmitte"
        );
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}

public enum PfadPunktTyp { Start, Linie, Bogen }

public record PfadPunktParams(
    double XRel, double YRel, double ZTiefe, double ZZustellung,
    double FraeserD, double Drehzahl, double Vorschub, double VorschubFz,
    string Radiuskorrektur, string Bezugspunkt, PfadPunktTyp Typ,
    double Verrundung = 0, double Eintauchwinkel = 90,
    double XMid = 0, double YMid = 0,
    string BogenModus = "Pfeilhöhe"); // "Bogenmitte" | "Radius" | "Pfeilhöhe"
