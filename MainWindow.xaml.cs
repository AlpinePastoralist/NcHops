using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace NCHops;

public partial class MainWindow : Window
{
    private Rect _topRect;
    private Rect _bottomRect;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateAll();
    }

    // ── Werkstückmaße ────────────────────────────────────────────

    private double WorkX => double.TryParse(TxtX.Text, System.Globalization.NumberStyles.Float,
        System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 800;
    private double WorkY => double.TryParse(TxtY.Text, System.Globalization.NumberStyles.Float,
        System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 400;
    private double WorkZ => double.TryParse(TxtZ.Text, System.Globalization.NumberStyles.Float,
        System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 19;

    // ── Menü ─────────────────────────────────────────────────────

    private void OnSpeichern(object sender, RoutedEventArgs e) { }

    private void OnBeenden(object sender, RoutedEventArgs e) => Close();

    private void OnPlanfraesen(object sender, RoutedEventArgs e)
    {
        var dlg = new PlanfräsenDialog(WorkX, WorkY) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        var gcode = GCodeGenerator.Planfräsen(dlg.Result!);
        GCodeBox.Text = gcode + GCodeBox.Text;
        UpdateAll();
    }

    private void OnBohrung(object sender, RoutedEventArgs e)
    {
        var dlg = new BohrungDialog(WorkZ) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        var gcode = GCodeGenerator.Bohrung(dlg.Result!, WorkX, WorkY);
        GCodeBox.Text = gcode + GCodeBox.Text;
        UpdateAll();
    }

    private void OnInfo(object sender, RoutedEventArgs e)
        => MessageBox.Show("NC_Hops – G-Code Generator & Visualisierer", "Info");

    // ── Aktualisieren ─────────────────────────────────────────────

    private void OnAktualisieren(object sender, RoutedEventArgs e) => UpdateAll();

    private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e) => UpdateAll();

    private void OnGCodeChanged(object sender, TextChangedEventArgs e) => UpdateAll();

    // ── Zeichnen ─────────────────────────────────────────────────

    private void UpdateAll()
    {
        DrawCanvas.Children.Clear();
        DrawWorkpieces();
        DrawGCodeTopView();
        DrawGCodeSideView();
    }

    private void DrawWorkpieces()
    {
        double cw = DrawCanvas.ActualWidth;
        double ch = DrawCanvas.ActualHeight;
        if (cw <= 0 || ch <= 0) return;

        double wx = WorkX, wy = WorkY, wz = WorkZ;
        if (wx <= 0 || wy <= 0 || wz <= 0) return;

        double minGap = 40;
        double scaleW = (cw * 0.9) / wx;
        double scale = Math.Min(scaleW, 1.0);
        double w = wx * scale, h1 = wy * scale, h2 = wz * scale;

        double needed = h1 + h2 + 3 * minGap;
        double gap;
        if (needed > ch)
        {
            double sh = ch / needed;
            w *= sh; h1 *= sh; h2 *= sh;
            gap = minGap * sh;
        }
        else
        {
            gap = (ch - h1 - h2) / 3;
        }

        double x0 = (cw - w) / 2;

        _topRect    = new Rect(x0, gap,          w, h1);
        _bottomRect = new Rect(x0, gap * 2 + h1, w, h2);

        DrawCanvas.Children.Add(MakeRect(_topRect,    Brushes.SkyBlue));
        DrawCanvas.Children.Add(MakeRect(_bottomRect, Brushes.LightGreen));
    }

    private static Rectangle MakeRect(Rect r, Brush fill)
    {
        var rect = new Rectangle { Width = r.Width, Height = r.Height, Fill = fill };
        Canvas.SetLeft(rect, r.Left);
        Canvas.SetTop(rect, r.Top);
        return rect;
    }

    // ── Draufsicht G-Code ────────────────────────────────────────

    private void DrawGCodeTopView()
    {
        var moves = GCodeParser.ParseTopView(GCodeBox.Text);
        if (moves.Count == 0 || _topRect.IsEmpty) return;

        double wx = WorkX, wy = WorkY;
        if (wx <= 0 || wy <= 0) return;

        double scale = Math.Min(_topRect.Width / wx, _topRect.Height / wy);
        Point MmToPx(double x, double y) =>
            new(_topRect.Left + x * scale, _topRect.Bottom - y * scale);

        Point? last = null;

        foreach (var m in moves)
        {
            if (m.Type is MoveType.Rapid or MoveType.Line)
            {
                var cur = MmToPx(m.X, m.Y);
                if (last.HasValue)
                    DrawCanvas.Children.Add(MakeLine(last.Value, cur,
                        m.Type == MoveType.Rapid ? Brushes.Gray : Brushes.Red,
                        m.Type == MoveType.Rapid));
                last = cur;
            }
            else
            {
                double cx = m.X + m.I, cy = m.Y + m.J;
                double r = Math.Sqrt((m.X - cx) * (m.X - cx) + (m.Y - cy) * (m.Y - cy));
                if (r == 0) continue;

                double start = Math.Atan2(m.Y - cy, m.X - cx);
                double end   = Math.Atan2(m.Ye - cy, m.Xe - cx);

                if (m.Type == MoveType.ArcCW  && end > start) end -= 2 * Math.PI;
                if (m.Type == MoveType.ArcCCW && end < start) end += 2 * Math.PI;

                int steps = Math.Max(8, (int)(Math.Abs(end - start) / (Math.PI / 36)));
                var prev = MmToPx(m.X, m.Y);

                for (int i = 1; i <= steps; i++)
                {
                    double angle = start + (end - start) * i / steps;
                    var cur = MmToPx(cx + r * Math.Cos(angle), cy + r * Math.Sin(angle));
                    DrawCanvas.Children.Add(MakeLine(prev, cur, Brushes.Red, false));
                    prev = cur;
                }
                last = MmToPx(m.Xe, m.Ye);
            }
        }
    }

    // ── Seitenansicht G-Code ─────────────────────────────────────

    private void DrawGCodeSideView()
    {
        var moves = GCodeParser.ParseSideView(GCodeBox.Text);
        if (moves.Count == 0 || _bottomRect.IsEmpty) return;

        double wx = WorkX, wz = WorkZ;
        if (wx <= 0 || wz <= 0) return;

        double scale = Math.Min(_bottomRect.Width / wx, _bottomRect.Height / wz);
        Point MmToPx(double x, double z) =>
            new(_bottomRect.Left + x * scale, _bottomRect.Top + (-z) * scale);

        Point? last = null;

        foreach (var m in moves)
        {
            var cur = MmToPx(m.X, m.Z);
            if (last.HasValue)
                DrawCanvas.Children.Add(MakeLine(last.Value, cur,
                    m.Cmd == "G0" ? Brushes.Gray : Brushes.Red,
                    m.Cmd == "G0"));
            last = cur;
        }
    }

    // ── Hilfsmethode ─────────────────────────────────────────────

    private static Line MakeLine(Point a, Point b, Brush brush, bool dashed)
    {
        var line = new Line
        {
            X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y,
            Stroke = brush, StrokeThickness = 2
        };
        if (dashed)
            line.StrokeDashArray = new System.Windows.Media.DoubleCollection { 5, 3 };
        return line;
    }
}
