using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace NCHops;

public partial class MainWindow : Window
{
    private static readonly Regex GCodeTokenRegex = new(
        "([A-Za-z][+-]?\\d*\\.?\\d*)|(\\s+)|(.+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly DispatcherTimer _refreshTimer;
    private Rect _topRect;
    private Rect _bottomRect;
    private ScrollViewer? _gcodeScrollViewer;
    private ScrollViewer? _lineNumbersScrollViewer;
    private bool _isUpdatingGCode;
    private bool _suppressGCodeUiUpdate;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateAll();

        _refreshTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(250), DispatcherPriority.Background,
            (_, _) => UpdateAll(), Dispatcher);
        _refreshTimer.Stop();
    }

    // ── Werkstückmaße ────────────────────────────────────────────

    private double WorkX => double.TryParse(TxtX.Text, System.Globalization.NumberStyles.Float,
        System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 800;
    private double WorkY => double.TryParse(TxtY.Text, System.Globalization.NumberStyles.Float,
        System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 400;
    private double WorkZ => double.TryParse(TxtZ.Text, System.Globalization.NumberStyles.Float,
        System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 19;

    // ── Menü ─────────────────────────────────────────────────────

    private void OnSpeichern(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "G-Code speichern",
            Filter = "G-Code (*.nc)|*.nc|Text (*.txt)|*.txt|Alle Dateien (*.*)|*.*",
            DefaultExt = ".nc",
            AddExtension = true,
            OverwritePrompt = true
        };

        if (dlg.ShowDialog(this) != true)
            return;

        try
        {
            var text = GCodeText.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
            File.WriteAllText(dlg.FileName, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Speichern fehlgeschlagen:\n{ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnBeenden(object sender, RoutedEventArgs e) => Close();

    private void OnPlanfraesen(object sender, RoutedEventArgs e)
    {
        var dlg = new PlanfräsenDialog(WorkX, WorkY) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        var gcode = GCodeGenerator.Planfräsen(dlg.Result!);
        PrependGeneratedGCode(gcode);
        UpdateAll();
    }

    private void OnBohrung(object sender, RoutedEventArgs e)
    {
        var dlg = new BohrungDialog(WorkZ + 3) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        var gcode = GCodeGenerator.Bohrung(dlg.Result!, WorkX, WorkY);
        GCodeText = gcode + GCodeText;
        UpdateAll();
    }

    private void OnReihenlochbohrung(object sender, RoutedEventArgs e)
    {
        var dlg = new ReihenlochbohrungDialog(WorkZ + 3) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        var gcode = GCodeGenerator.Reihenlochbohrung(dlg.Result!);
        PrependGeneratedGCode(gcode);
        UpdateAll();
    }

    private void OnUmfahren(object sender, RoutedEventArgs e)
    {
        var dlg = new UmfahrenDialog(WorkZ) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        var gcode = GCodeGenerator.Umfahren(dlg.Result!, WorkX, WorkY);
        PrependGeneratedGCode(gcode);
        UpdateAll();
    }

    private void OnInfo(object sender, RoutedEventArgs e)
        => MessageBox.Show("NC_Hops – G-Code Generator & Visualisierer", "Info");

    // ── Aktualisieren ─────────────────────────────────────────────

    private void OnAktualisieren(object sender, RoutedEventArgs e) => UpdateAll();

    private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e) => UpdateAll();

    private string GCodeText
    {
        get => new TextRange(GCodeBox.Document.ContentStart, GCodeBox.Document.ContentEnd).Text.TrimEnd('\r', '\n');
        //.TrimEnd('\r', '\n')
        set => SetGCodeText(value);
    }

    private void OnGCodeChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingGCode)
            return;
        if (_suppressGCodeUiUpdate)
            return;

        _refreshTimer.Stop();
        _refreshTimer.Start();
        UpdateGCodeEditor();
    }

    private void OnGCodeBoxLoaded(object sender, RoutedEventArgs e)
    {
        // Ensure the text starts at the very top/left so line numbers align visually.
        GCodeBox.Document.PagePadding = new Thickness(0);
        GCodeBox.Document.LineHeight = 24;
        GCodeBox.Document.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;

        GCodeLineNumbersBox.Document.PagePadding = new Thickness(4, 0, 4, 0);
        GCodeLineNumbersBox.Document.LineHeight = 24;
        GCodeLineNumbersBox.Document.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;

        _gcodeScrollViewer = GetDescendantScrollViewer(GCodeBox);
        _lineNumbersScrollViewer = GetDescendantScrollViewer(GCodeLineNumbersBox);
        if (_gcodeScrollViewer != null)
            _gcodeScrollViewer.ScrollChanged += OnGCodeScrollChanged;

        UpdateGCodeEditor();
    }

    private void OnGCodeScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange != 0)
            _lineNumbersScrollViewer?.ScrollToVerticalOffset(e.VerticalOffset);
    }

    private void UpdateGCodeEditor()
    {
        _isUpdatingGCode = true;
        try
        {
            var plainText = GCodeText;
            UpdateLineNumbers(plainText);
            UpdateGCodeHighlighting(plainText);
        }
        finally
        {
            _isUpdatingGCode = false;
        }
    }

    private void SetGCodeText(string text)
    {
        var selectionStart = GCodeBox.CaretPosition;
        var offset = GetTextOffset(GCodeBox.Document.ContentStart, selectionStart);

        GCodeBox.Document.Blocks.Clear();
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
            GCodeBox.Document.Blocks.Add(CreateHighlightedParagraph(line));

        var position = GetTextPositionAtOffset(GCodeBox.Document.ContentStart, offset);
        if (position != null)
            GCodeBox.CaretPosition = position;
    }

    private void UpdateLineNumbers(string text)
    {
        var lineCount = text.Replace("\r\n", "\n").Split('\n').Length;
        GCodeLineNumbersBox.Document.Blocks.Clear();
        for (int i = 1; i <= lineCount; i++)
        {
            var p = new Paragraph(new Run(i.ToString()))
            {
                Margin = new Thickness(0),
                Padding = new Thickness(0),
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                LineHeight = 24,
                TextAlignment = TextAlignment.Right
            };
            GCodeLineNumbersBox.Document.Blocks.Add(p);
        }
    }

    private void PrependGeneratedGCode(string gcode)
    {
        var normalized = gcode.Replace("\r\n", "\n").TrimEnd('\n');
        if (string.IsNullOrEmpty(normalized))
            return;

        var newLines = normalized.Split('\n');

        _suppressGCodeUiUpdate = true;
        try
        {
            var firstNum = GCodeLineNumbersBox.Document.Blocks.FirstBlock;

            var first = GCodeBox.Document.Blocks.FirstBlock;
            // Insert in natural order so the first generated line ends up at the very top.
            for (int i = 0; i < newLines.Length; i++)
            {
                var p = CreateHighlightedParagraph(newLines[i]);
                if (first == null)
                    GCodeBox.Document.Blocks.Add(p);
                else
                    GCodeBox.Document.Blocks.InsertBefore(first, p);
            }

            // Renumber existing lines first, then prepend the new 1..N numbers.
            ShiftLineNumbersDown(newLines.Length);
            for (int n = 1; n <= newLines.Length; n++)
            {
                var p = new Paragraph(new Run(n.ToString()))
                {
                    Margin = new Thickness(0),
                    Padding = new Thickness(0),
                    LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                    LineHeight = 24,
                    TextAlignment = TextAlignment.Right
                };

                if (firstNum == null)
                    GCodeLineNumbersBox.Document.Blocks.Add(p);
                else
                    GCodeLineNumbersBox.Document.Blocks.InsertBefore(firstNum, p);
            }

            GCodeBox.CaretPosition = GCodeBox.Document.ContentStart;
            // RichTextBox may auto-scroll to the bottom after large inserts; force view back to the top on next layout pass.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _gcodeScrollViewer?.ScrollToHome();
                _lineNumbersScrollViewer?.ScrollToHome();
            }), DispatcherPriority.Loaded);
        }
        finally
        {
            // RichTextBox raises TextChanged after this method returns; keep suppression for that callback too.
            Dispatcher.BeginInvoke(new Action(() => { _suppressGCodeUiUpdate = false; }), DispatcherPriority.Background);
        }
    }

    private void ShiftLineNumbersDown(int delta)
    {
        if (delta <= 0)
            return;

        // Don't mutate paragraph inlines while enumerating Blocks (can throw / destabilize the collection).
        var paragraphs = GCodeLineNumbersBox.Document.Blocks.OfType<Paragraph>().ToList();

        int idx = delta + 1;
        foreach (var p in paragraphs)
        {
            p.Inlines.Clear();
            p.Inlines.Add(new Run(idx.ToString()));
            idx++;
        }
    }

    private void UpdateGCodeHighlighting(string text)
    {
        var selectionStart = GCodeBox.Selection.Start;
        var selectionEnd = GCodeBox.Selection.End;
        var startOffset = GetTextOffset(GCodeBox.Document.ContentStart, selectionStart);
        var endOffset = GetTextOffset(GCodeBox.Document.ContentStart, selectionEnd);

        GCodeBox.Document.Blocks.Clear();
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
            GCodeBox.Document.Blocks.Add(CreateHighlightedParagraph(line));

        var startPos = GetTextPositionAtOffset(GCodeBox.Document.ContentStart, startOffset);
        var endPos = GetTextPositionAtOffset(GCodeBox.Document.ContentStart, endOffset);
        if (startPos != null && endPos != null)
            GCodeBox.Selection.Select(startPos, endPos);
    }

    private Paragraph CreateHighlightedParagraph(string line)
    {
        var paragraph = new Paragraph
        {
            Margin = new Thickness(0),
            Padding = new Thickness(0),
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            LineHeight = 24
        };
        if (string.IsNullOrEmpty(line))
        {
            //paragraph.Inlines.Add(new Run(" "));
            return paragraph;
        }

        var commentIndex = line.IndexOf('(');
        string codePart = line;
        string commentPart = string.Empty;
        if (commentIndex >= 0)
        {
            codePart = line.Substring(0, commentIndex);
            commentPart = line.Substring(commentIndex);
        }

        foreach (Match match in GCodeTokenRegex.Matches(codePart))
        {
            var token = match.Value;
            if (string.IsNullOrWhiteSpace(token))
            {
                paragraph.Inlines.Add(new Run(token));
                continue;
            }

            var run = new Run(token);
            var code = char.ToUpperInvariant(token[0]);
            if (code == 'G' || code == 'M')
                run.Foreground = Brushes.DarkBlue;
            else if (code == 'X' || code == 'Y' || code == 'Z' || code == 'I' || code == 'J' || code == 'F' || code == 'A' || code == 'S' || code == 'T' || code == 'R')
                run.Foreground = Brushes.DarkRed;
            else
                run.Foreground = Brushes.Black;

            paragraph.Inlines.Add(run);
        }

        if (!string.IsNullOrEmpty(commentPart))
            paragraph.Inlines.Add(new Run(commentPart) { Foreground = Brushes.Green });

        return paragraph;
    }

    private static int GetTextOffset(TextPointer start, TextPointer position)
    {
        return new TextRange(start, position).Text.Length;
    }

    private static TextPointer? GetTextPositionAtOffset(TextPointer start, int offset)
    {
        var navigator = start;
        int remaining = offset;
        while (navigator != null && remaining > 0)
        {
            if (navigator.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
            {
                string textRun = navigator.GetTextInRun(LogicalDirection.Forward);
                int count = Math.Min(textRun.Length, remaining);
                navigator = navigator.GetPositionAtOffset(count);
                remaining -= count;
            }
            else
            {
                navigator = navigator.GetNextContextPosition(LogicalDirection.Forward);
            }
        }

        return navigator;
    }

    private static ScrollViewer? GetDescendantScrollViewer(DependencyObject element)
    {
        if (element == null)
            return null;

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            var child = VisualTreeHelper.GetChild(element, i);
            if (child is ScrollViewer viewer)
                return viewer;

            var result = GetDescendantScrollViewer(child);
            if (result != null)
                return result;
        }
        return null;
    }

    private void TextBox_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is TextBox textBox && !textBox.IsKeyboardFocusWithin)
        {
            e.Handled = true;
            textBox.Focus();
        }
    }

    private void TextBox_GotKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox textBox)
            textBox.SelectAll();
    }

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
        var moves = GCodeParser.ParseTopView(GCodeText);
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

        foreach (var hole in GCodeParser.ParseDrillPoints(GCodeText))
        {
            var center = MmToPx(hole.X, hole.Y);
            var circle = new Ellipse
            {
                Width = 12,
                Height = 12,
                Fill = Brushes.Yellow,
                Stroke = Brushes.Black,
                StrokeThickness = 2
            };
            Canvas.SetLeft(circle, center.X - circle.Width / 2);
            Canvas.SetTop(circle, center.Y - circle.Height / 2);
            DrawCanvas.Children.Add(circle);
        }
    }

    // ── Seitenansicht G-Code ─────────────────────────────────────

    private void DrawGCodeSideView()
    {
        var moves = GCodeParser.ParseSideView(GCodeText);
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
