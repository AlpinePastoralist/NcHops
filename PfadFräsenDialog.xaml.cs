using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace NCHops;

#if false // PfadFräsen (deaktiviert)
public class PfadPunkt : INotifyPropertyChanged
{
    private int    _nr;
    private string _x     = "0";
    private string _y     = "0";
    private string _bezug = "Unten links";

    public int    Nr    { get => _nr;    set { _nr    = value; Notify(); } }
    public string X     { get => _x;     set { _x     = value; Notify(); } }
    public string Y     { get => _y;     set { _y     = value; Notify(); } }
    public string Bezug { get => _bezug; set { _bezug = value; Notify(); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? p = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
}

public partial class PfadFräsenDialog : Window
{
    private readonly ObservableCollection<PfadPunkt> _punkte = [];
    public PfadFräsenParams? Result { get; private set; }

    public PfadFräsenDialog(double workZ)
    {
        InitializeComponent();
        TxtZ.Text = (-Math.Abs(workZ)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        LvPunkte.ItemsSource = _punkte;
        _punkte.CollectionChanged += (_, _) => AktualisierVorschau();
    }

    // ── Punkt hinzufügen ─────────────────────────────────────────

    private void OnHinzufügen(object sender, RoutedEventArgs e) => PunktHinzufügen();

    private void OnPunktKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return) PunktHinzufügen();
    }

    private void PunktHinzufügen()
    {
        if (!double.TryParse(TxtPunktX.Text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var x) ||
            !double.TryParse(TxtPunktY.Text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var y))
        {
            MessageBox.Show("Ungültige X/Y-Werte.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _punkte.Add(new PfadPunkt
        {
            Nr = _punkte.Count + 1,
            X = x.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Y = y.ToString(System.Globalization.CultureInfo.InvariantCulture)
        });

        LvPunkte.SelectedIndex = _punkte.Count - 1;
        LvPunkte.ScrollIntoView(LvPunkte.SelectedItem);
        TxtPunktX.Focus();
        TxtPunktX.SelectAll();
    }

    // ── Reihenfolge & Löschen ────────────────────────────────────

    private void OnHoch(object sender, RoutedEventArgs e)
    {
        int i = LvPunkte.SelectedIndex;
        if (i <= 0) return;
        (_punkte[i], _punkte[i - 1]) = (_punkte[i - 1], _punkte[i]);
        LvPunkte.SelectedIndex = i - 1;
        AktualisiereNummern();
    }

    private void OnRunter(object sender, RoutedEventArgs e)
    {
        int i = LvPunkte.SelectedIndex;
        if (i < 0 || i >= _punkte.Count - 1) return;
        (_punkte[i], _punkte[i + 1]) = (_punkte[i + 1], _punkte[i]);
        LvPunkte.SelectedIndex = i + 1;
        AktualisiereNummern();
    }

    private void OnLöschen(object sender, RoutedEventArgs e)
    {
        int i = LvPunkte.SelectedIndex;
        if (i < 0) return;
        _punkte.RemoveAt(i);
        AktualisiereNummern();
        LvPunkte.SelectedIndex = Math.Min(i, _punkte.Count - 1);
    }

    private void OnAlleLöschen(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Alle Punkte löschen?", "Bestätigung",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            _punkte.Clear();
    }

    private void AktualisiereNummern()
    {
        for (int i = 0; i < _punkte.Count; i++)
            _punkte[i].Nr = i + 1;
        LvPunkte.Items.Refresh();
        AktualisierVorschau();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => AktualisierVorschau();

    // ── Live-Vorschau ────────────────────────────────────────────

    private void OnPreviewSizeChanged(object sender, SizeChangedEventArgs e) => AktualisierVorschau();

    private void AktualisierVorschau()
    {
        PreviewCanvas.Children.Clear();

        if (_punkte.Count == 0)
        {
            TbHinweis.Visibility = Visibility.Visible;
            return;
        }

        TbHinweis.Visibility = Visibility.Collapsed;

        double cw = PreviewCanvas.ActualWidth;
        double ch = PreviewCanvas.ActualHeight;
        if (cw <= 0 || ch <= 0) return;

        // Koordinaten parsen
        var pts = _punkte.Select(p => (
            X: double.Parse(p.X, System.Globalization.CultureInfo.InvariantCulture),
            Y: double.Parse(p.Y, System.Globalization.CultureInfo.InvariantCulture)
        )).ToList();

        if (pts.Count == 0) return;

        // Bounding-Box
        double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X);
        double minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
        double rangeX = maxX - minX, rangeY = maxY - minY;

        // Mindestbereich damit einzelne Punkte sichtbar sind
        if (rangeX < 1) { minX -= 5; maxX += 5; rangeX = maxX - minX; }
        if (rangeY < 1) { minY -= 5; maxY += 5; rangeY = maxY - minY; }

        double pad = 30;
        double scaleX = (cw - 2 * pad) / rangeX;
        double scaleY = (ch - 2 * pad) / rangeY;
        double scale  = Math.Min(scaleX, scaleY);

        System.Windows.Point ToPx(double x, double y) => new(
            pad + (x - minX) * scale,
            ch - pad - (y - minY) * scale   // Y invertieren
        );

        int selectedIdx = LvPunkte.SelectedIndex;

        // Achsenkreuz (Nullpunkt falls im Bereich)
        if (minX <= 0 && 0 <= maxX)
        {
            var p0 = ToPx(0, minY);
            var p1 = ToPx(0, maxY);
            PreviewCanvas.Children.Add(new Line
            {
                X1 = p0.X, Y1 = p0.Y, X2 = p1.X, Y2 = p1.Y,
                Stroke = Brushes.LightGray, StrokeThickness = 1
            });
        }
        if (minY <= 0 && 0 <= maxY)
        {
            var p0 = ToPx(minX, 0);
            var p1 = ToPx(maxX, 0);
            PreviewCanvas.Children.Add(new Line
            {
                X1 = p0.X, Y1 = p0.Y, X2 = p1.X, Y2 = p1.Y,
                Stroke = Brushes.LightGray, StrokeThickness = 1
            });
        }

        // Verbindungslinien
        for (int i = 0; i < pts.Count - 1; i++)
        {
            var a = ToPx(pts[i].X, pts[i].Y);
            var b = ToPx(pts[i + 1].X, pts[i + 1].Y);
            bool isSelected = (i == selectedIdx || i + 1 == selectedIdx);
            PreviewCanvas.Children.Add(new Line
            {
                X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y,
                Stroke = isSelected ? Brushes.OrangeRed : Brushes.Red,
                StrokeThickness = isSelected ? 3 : 2
            });
        }

        // Richtungspfeile (Mitte jeder Linie)
        for (int i = 0; i < pts.Count - 1; i++)
        {
            var a = ToPx(pts[i].X, pts[i].Y);
            var b = ToPx(pts[i + 1].X, pts[i + 1].Y);
            DrawArrow(a, b, Brushes.DarkRed);
        }

        // Punkte zeichnen
        for (int i = 0; i < pts.Count; i++)
        {
            var px = ToPx(pts[i].X, pts[i].Y);
            bool isSelected = i == selectedIdx;
            double r = isSelected ? 7 : 5;

            var dot = new Ellipse
            {
                Width = r * 2, Height = r * 2,
                Fill = i == 0 ? Brushes.Green : (isSelected ? Brushes.Orange : Brushes.Red),
                Stroke = Brushes.White, StrokeThickness = 1.5
            };
            Canvas.SetLeft(dot, px.X - r);
            Canvas.SetTop(dot, px.Y - r);
            PreviewCanvas.Children.Add(dot);

            // Nummer
            var label = new TextBlock
            {
                Text = (i + 1).ToString(),
                FontSize = 10, FontWeight = FontWeights.Bold,
                Foreground = Brushes.DarkBlue
            };
            Canvas.SetLeft(label, px.X + r + 2);
            Canvas.SetTop(label, px.Y - 8);
            PreviewCanvas.Children.Add(label);
        }
    }

    private void DrawArrow(System.Windows.Point a, System.Windows.Point b, Brush brush)
    {
        double mx = (a.X + b.X) / 2, my = (a.Y + b.Y) / 2;
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 10) return;
        dx /= len; dy /= len;

        double arrowSize = 7;
        double ax = mx - dx * arrowSize + dy * arrowSize / 2;
        double ay = my - dy * arrowSize - dx * arrowSize / 2;
        double bx = mx - dx * arrowSize - dy * arrowSize / 2;
        double by = my - dy * arrowSize + dx * arrowSize / 2;

        var poly = new Polygon
        {
            Points = new PointCollection
            {
                new(mx, my), new(ax, ay), new(bx, by)
            },
            Fill = brush
        };
        PreviewCanvas.Children.Add(poly);
    }

    // ── OK / Abbrechen ───────────────────────────────────────────

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (_punkte.Count < 2)
        {
            MessageBox.Show("Mindestens 2 Punkte erforderlich.", "Fehler",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var punkte = _punkte.Select(p => (
            X: double.Parse(p.X, System.Globalization.CultureInfo.InvariantCulture),
            Y: double.Parse(p.Y, System.Globalization.CultureInfo.InvariantCulture)
        )).ToList();

        Result = new PfadFräsenParams(
            Punkte: punkte,
            Z: double.Parse(TxtZ.Text, System.Globalization.CultureInfo.InvariantCulture),
            Zustellung: double.Parse(TxtZustellung.Text, System.Globalization.CultureInfo.InvariantCulture),
            Vorschub: double.Parse(TxtVorschub.Text, System.Globalization.CultureInfo.InvariantCulture),
            Drehzahl: double.Parse(TxtDrehzahl.Text, System.Globalization.CultureInfo.InvariantCulture),
            FraeserD: double.Parse(TxtFraeserD.Text, System.Globalization.CultureInfo.InvariantCulture),
            Bezugspunkt: (CbBezug.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Unten links"
        );
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}

public record PfadFräsenParams(
    List<(double X, double Y)> Punkte,
    double Z,
    double Zustellung,
    double Vorschub,
    double Drehzahl,
    double FraeserD,
    string Bezugspunkt,
    string Seite = "Mitte");
#endif // PfadFräsen Ende
