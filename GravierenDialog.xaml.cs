using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace NCHops;

public record GraviereParams(
    string Text,
    string FontFamily,
    double FontSizeMm,        // line height in mm
    double XRel,
    double YRel,
    double TextBreite,        // text field width  in mm (0 = unconstrained)
    double TextHoehe,         // text field height in mm (0 = unconstrained)
    double ZTiefe,
    double SchneidenWinkel,   // full included tip angle in degrees (e.g. 60, 90, 120)
    double FraeserD,          // tool diameter — caps effective width (flat mill = diameter)
    double Vorschub,
    double Drehzahl,
    string Bezugspunkt,
    string Ausrichtung    = "Links",    // "Links" | "Mitte" | "Rechts"
    bool   IsVCarve        = false,     // true = V-Carve (Medialachse), false = Umriss
    bool   IsTasche        = false,     // true = Tasche pro Buchstabe
    double VereinfachungMm = 1.0,       // Spitzentoleranz: Umkehrpunkte kollabieren (0 = aus)
    double SampleStepMm    = 0,         // Abtastschrittweite mm (0 = auto: FontSizeMm/300, clamp 0.02–0.1)
    int    WerkzeugNr      = 0);        // Werkzeug-Nr aus Werkzeugliste (0 = keines)

public partial class GravierenDialog : Window
{
    public GraviereParams? Result { get; private set; }

    public GravierenDialog(GraviereParams? prefill = null,
                           IReadOnlyList<Werkzeug>? werkzeuge = null,
                           double workX = 0, double workY = 0)
    {
        InitializeComponent();

        if (werkzeuge?.Count > 0)
        {
            var vFräser = werkzeuge.Where(w => w.Schneidenwinkel < 180.0).ToList();
            CbWerkzeug.ItemsSource = vFräser;

            var defaultTool = vFräser.FirstOrDefault(
                w => w.Name.IndexOf("Gravierstichel", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? vFräser.FirstOrDefault();
            CbWerkzeug.SelectedItem = defaultTool;
        }

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (prefill != null)
        {
            TxtBreite.Text = prefill.TextBreite.ToString(inv);
            TxtHoehe.Text  = prefill.TextHoehe.ToString(inv);
            TxtX.Text      = prefill.XRel.ToString(inv);
            TxtY.Text      = prefill.YRel.ToString(inv);
            SetBezug(prefill.Bezugspunkt);

            if (werkzeuge != null)
            {
                var match = werkzeuge.FirstOrDefault(
                    w => Math.Abs(w.Drehzahl - prefill.Drehzahl) < 1);
                if (match != null) CbWerkzeug.SelectedItem = match;
            }
        }
        else
        {
            TxtX.Text      = "0";
            TxtY.Text      = "0";
            TxtBreite.Text = workX > 0 ? workX.ToString(inv) : "100";
            TxtHoehe.Text  = "30";
        }
    }

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
    }

    private string GetBezug()
    {
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
        var sty = System.Globalization.NumberStyles.Float;

        static string Norm(string s) => s.Replace(',', '.');

        if (!double.TryParse(Norm(TxtBreite.Text), sty, inv, out var breite) || breite < 0 ||
            !double.TryParse(Norm(TxtHoehe.Text),  sty, inv, out var hoehe)  || hoehe  < 0 ||
            (breite == 0 && hoehe == 0))
        {
            MessageBox.Show("Ungültige Textfeld-Grösse.", "Fehler",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!double.TryParse(Norm(TxtX.Text), sty, inv, out var x) ||
            !double.TryParse(Norm(TxtY.Text), sty, inv, out var y))
        {
            MessageBox.Show("Ungültige X/Y-Werte.", "Fehler",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var w = CbWerkzeug.SelectedItem as Werkzeug;

        // Text, Schriftart und Ausrichtung werden in den Eigenschaften gesetzt
        var bezug = GetBezug();
        var ausrichtung = bezug switch
        {
            _ when bezug.Contains("Rechts")                      => "Rechts",
            "Mitte" or "Oben Mitte" or "Unten Mitte"             => "Mitte",
            _                                                     => "Links"
        };

        Result = new GraviereParams(
            Text:            "Text",
            FontFamily:      "Arial",
            FontSizeMm:      hoehe,
            XRel:            x,
            YRel:            y,
            TextBreite:      breite,
            TextHoehe:       hoehe,
            ZTiefe:          w?.ZZustellung > 0 ? w.ZZustellung : 3.0,
            SchneidenWinkel: w?.Schneidenwinkel ?? 60,
            FraeserD:        w?.Durchmesser     ?? 1.0,
            Vorschub:        w?.VorschubFxy ?? 800,
            Drehzahl:        w?.Drehzahl    ?? 20000,
            Bezugspunkt:     bezug,
            Ausrichtung:     ausrichtung);

        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
