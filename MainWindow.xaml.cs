using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;

namespace NCHops;

public partial class MainWindow : Window
{

    private static readonly Regex GCodeTokenRegex = new(
        "([A-Za-z][+-]?\\d*\\.?\\d*)|(\\s+)|(.+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _zoomTimer;       // Debounce: UpdateAll nach Zoom-Geste
    private Rect _topRect;
    private Rect _bottomRect;

    // ── Canvas-Zoom / Pan ────────────────────────────────────────
    private double _zoom      = 1.0;
    private double _panX      = 0.0;
    private double _panY      = 0.0;
    private bool   _isPanning = false;
    private Point  _panStart;   // Startpunkt im Parent-Koordinatensystem
    private Point  _panOrigin;  // _panX/_panY beim Drag-Start

    // ── Aktives Werkzeug ─────────────────────────────────────────
    private enum CanvasTool { Select, Hand, Zoom }
    private CanvasTool _activeTool    = CanvasTool.Select;
    private bool       _isZoomDragging = false;
    private Point      _zoomDragStart;
    private System.Windows.Shapes.Rectangle? _zoomRubberBand;

    // ── G-Code Zeilenmarkierung ───────────────────────────────────
    private int _highlightGCodeLine = -1;   // Caret-Zeile
    private int _mouseHoverLine     = -1;   // Maus-Hover im Editor (Vorrang)
    private int _selectedGCodeLine  = -1;   // Klick auf Werkstück
    private int _selectionSource    =  0;   // 0=Draufsicht, 1=Seitenansicht
    private GCodeLineBackgroundRenderer? _gcodeBgRenderer;
    private readonly DispatcherTimer _hlTimer;   // Debounce 80 ms
    private readonly DispatcherTimer _eigTimer;  // Debounce 400 ms für Eigenschaften-Texteingabe
    private readonly DispatcherTimer _simTimer;  // ~60 fps Animations-Tick

    // Simulation state
    private bool   _simPlaying;
    private bool   _simSliderBusy; // verhindert Rückkopplungsschleife beim Slider-Update
    private bool   _simPathDirty = true;
    private double _simPosMm;
    private double _simTotalMm;
    private double _simSpeedMult = 1.0;
    private DateTime _simLastTick;

    private record struct SimSeg(
        double X0, double Y0, double X1, double Y1,
        double Len, double CumStart,
        bool IsRapid, double FeedMmMin,
        bool IsArc, double Cx, double Cy, double R, double A0, double DA);

    private List<SimSeg> _simSegs = [];
    private CancellationTokenSource? _regenCts;
    private bool _rasterEnabled;
    private double _rasterX = 10.0;
    private double _rasterY = 10.0;
    private readonly ObservableCollection<HistoryEntry> _history = [];
    private readonly List<HistoryEntry> _historyClipboard = [];
    private bool _suppressHistoryRegen;
    private readonly ObservableCollection<Werkzeug> _werkzeuge = [];
    private bool _suppressSave;
    private bool _suppressGCodeUiUpdate;

    // G-Code Backing-Field: Canvas/Parser nutzen dies direkt, TextBox wird im Hintergrund gesetzt
    private string _gcodeContent = string.Empty;

    // Parse-Cache: G-Code nur neu parsen wenn Text sich ändert
    private string?         _parsedGCodeText   = null;
    private List<Move>      _cachedTopMoves    = [];
    private List<SideMove>  _cachedSideMoves   = [];
    private List<DrillHole> _cachedDrillPoints = [];

    // V-Carve: Cache {GraviereParams → Kreisliste} und Ergebnisliste für G-Code
    private readonly Dictionary<GraviereParams, List<GCodeGenerator.VCarveCircle>>
        _vCarveCache = new();
    public List<GCodeGenerator.VCarveCircle> VCarveCenters { get; private set; } = [];
    private GraviereParams? _previewGravParams;
    private static readonly string WerkzeugDatei = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NCHops", "werkzeuge.json");

    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);

#if false // ── Pfad Fräsen (deaktiviert) ─────────────────────────
    private readonly ObservableCollection<PfadPunkt> _pfadPunkte = [];
    private int    _pfadHoverIdx      = -1;
    private int    _pfadDragIdx       = -1;
    private bool   _arrowJustClicked  = false;
    private double _pfadScale         = 1.0;
    private Rect   _pfadCanvasRect    = Rect.Empty;

    private double PfadSchritt => double.TryParse(PfadTxtSchritt?.Text,
        System.Globalization.NumberStyles.Float,
        System.Globalization.CultureInfo.InvariantCulture, out var v) ? Math.Max(0.1, v) : 5.0;

    private Point AbsMmToPx(double absX, double absY) => new(
        _pfadCanvasRect.Left   + absX * _pfadScale,
        _pfadCanvasRect.Bottom - absY * _pfadScale);

    private (double absX, double absY) PxToAbsMm(Point px) => (
        (px.X - _pfadCanvasRect.Left)   / _pfadScale,
        (_pfadCanvasRect.Bottom - px.Y) / _pfadScale);

    private static (double absX, double absY) RelToAbs(
        string bezug, double relX, double relY, double w, double h)
        => bezug switch
        {
            "Unten links"  => (relX,         relY),
            "Oben links"   => (relX,         h - relY),
            "Unten rechts" => (w - relX,     relY),
            "Oben rechts"  => (w - relX,     h - relY),
            "Links Mitte"  => (relX,         h / 2 + relY),
            "Rechts Mitte" => (w - relX,     h / 2 + relY),
            "Oben Mitte"   => (w / 2 + relX, h - relY),
            "Unten Mitte"  => (w / 2 + relX, relY),
            "Mitte"        => (w / 2 + relX, h / 2 + relY),
            _              => (relX, relY)
        };

    private static (double relX, double relY) AbsToRel(
        string bezug, double absX, double absY, double w, double h)
        => bezug switch
        {
            "Unten links"  => (absX,         absY),
            "Oben links"   => (absX,         h - absY),
            "Unten rechts" => (w - absX,     absY),
            "Oben rechts"  => (w - absX,     h - absY),
            "Links Mitte"  => (absX,         absY - h / 2),
            "Rechts Mitte" => (w - absX,     absY - h / 2),
            "Oben Mitte"   => (absX - w / 2, h - absY),
            "Unten Mitte"  => (absX - w / 2, absY),
            "Mitte"        => (absX - w / 2, absY - h / 2),
            _              => (absX, absY)
        };

    private Point PunktToPx(PfadPunkt p)
    {
        double relX = double.Parse(p.X, System.Globalization.CultureInfo.InvariantCulture);
        double relY = double.Parse(p.Y, System.Globalization.CultureInfo.InvariantCulture);
        var (absX, absY) = RelToAbs(p.Bezug, relX, relY, WorkX, WorkY);
        return AbsMmToPx(absX, absY);
    }
#endif

    public MainWindow()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            // Schriftarten-ComboBox in den Eigenschaften befüllen
            var fontNames = System.Windows.Media.Fonts.SystemFontFamilies
                                .Select(f => f.Source)
                                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                                .ToList();
            EigFont.ItemsSource = fontNames;

            DrawCanvas.MouseDown  += OnCanvasMouseDown;
            DrawCanvas.MouseMove  += OnCanvasMouseMove;
            DrawCanvas.MouseUp    += OnCanvasMouseUp;
            DrawCanvas.MouseLeave += OnCanvasMouseLeave;
            DrawCanvas.MouseWheel += OnCanvasMouseWheel;
            // Zoom-Label anklickbar → Reset
            if (TxtZoomLevel is not null)
            {
                var border = (Border)TxtZoomLevel.Parent;
                border.IsHitTestVisible = true;
                border.Cursor           = Cursors.Hand;
                border.ToolTip          = "Klick: Zoom zurücksetzen\nDoppelklick auf Canvas: Zoom zurücksetzen";
                border.MouseLeftButtonDown += (_, _) => ResetZoom();
            }
            UpdateAll();
        };

        _refreshTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(250), DispatcherPriority.Background,
            (_, _) => UpdateAll(), Dispatcher);
        _refreshTimer.Stop();

        _hlTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(80),
            DispatcherPriority.Background, (_, _) =>
            { _hlTimer.Stop(); UpdateAll(); }, Dispatcher);
        _hlTimer.Stop();

        _eigTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(400),
            DispatcherPriority.Background, (_, _) =>
            { _eigTimer.Stop(); UpdatePreviewFromFields(); }, Dispatcher);
        _eigTimer.Stop();

        _simTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16),
            DispatcherPriority.Render, OnSimTick, Dispatcher);
        _simTimer.Stop();

        _zoomTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(120),
            DispatcherPriority.Background, (_, _) =>
            { _zoomTimer.Stop(); UpdateAll(); }, Dispatcher);
        _zoomTimer.Stop();

        HistoryList.ItemsSource = _history;
        _history.CollectionChanged += (_, _) => { if (!_suppressHistoryRegen) RegenerateGCodeFromHistory(); };
        WerkzeugGrid.ItemsSource = _werkzeuge;
        _werkzeuge.CollectionChanged += (_, _) => SaveWerkzeuge();
        LoadWerkzeuge();
#if false
        PfadLvPunkte.ItemsSource = _pfadPunkte;
        _pfadPunkte.CollectionChanged += (_, _) => UpdateAll();
#endif
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
            var text = _gcodeContent.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
            File.WriteAllText(dlg.FileName, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Speichern fehlgeschlagen:\n{ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnFraesenSenden(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_gcodeContent))
        {
            MessageBox.Show(this, "Kein G-Code vorhanden.", "Fräsen",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var downloads = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        downloads = System.IO.Path.Combine(downloads, "Downloads");
        string fileName = DateTime.Now.ToString("yyMMdd-HHmm") + ".nc";
        string filePath = System.IO.Path.Combine(downloads, fileName);

        try
        {
            var text = _gcodeContent.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
            File.WriteAllText(filePath, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Speichern fehlgeschlagen:\n{ex.Message}", "Fehler",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        const string estlcam = @"C:\Program Files (x86)\Estlcam11\estlcam64.exe";
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName  = estlcam,
                Arguments = $"\"{filePath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Estlcam konnte nicht gestartet werden:\n{ex.Message}", "Fehler",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnBeenden(object sender, RoutedEventArgs e) => Close();
    private void OnWindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        WerkzeugGrid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
        WerkzeugGrid.CommitEdit(DataGridEditingUnit.Row,  exitEditingMode: true);
        SaveWerkzeuge();
    }

    private void OnPlanfraesen(object sender, RoutedEventArgs e)
    {
        var dlg = new PlanfräsenDialog(WorkX, WorkY, werkzeuge: _werkzeuge.ToList()) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        var p = dlg.Result!;
        _history.Add(new HistoryEntry("Planfräsen",
            $"{(p.Horizontal ? "Horizontal" : "Vertikal")}, Z={p.Z}, Ø{p.FraeserD}", p));
    }

    private void OnBohrung(object sender, RoutedEventArgs e)
    {
        var dlg = new BohrungDialog(WorkZ + 3, werkzeuge: _werkzeuge.ToList()) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        var p = dlg.Result!;
        _history.Add(new HistoryEntry("Bohrung",
            $"X={p.XRel} Y={p.YRel}, Ø{p.Durchmesser}, Z={p.Bohrtiefe}, {p.Bezugspunkt}", p));
    }

    private void OnReihenlochbohrung(object sender, RoutedEventArgs e)
    {
        var dlg = new ReihenlochbohrungDialog(WorkZ + 3, werkzeuge: _werkzeuge.ToList()) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        var p = dlg.Result!;
        _history.Add(new HistoryEntry("Reihenlochbohrung",
            $"{p.CountX}×{p.CountY}, Ø{p.Diameter}, Z={p.Bohrtiefe}", p));
    }

    private void OnUmfahren(object sender, RoutedEventArgs e)
    {
        var dlg = new UmfahrenDialog(WorkZ, werkzeuge: _werkzeuge.ToList()) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        var p = dlg.Result!;
        _history.Add(new HistoryEntry("Umfahren",
            $"A={p.A}, Ø{p.Diameter}, Z={p.Z}", p));
    }

    private void OnTasche(object sender, RoutedEventArgs e)
    {
        var dlg = new TascheFräsenDialog(-(WorkZ + 3), werkzeuge: _werkzeuge.ToList()) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        var p = dlg.Result!;
        _history.Add(new HistoryEntry("Tasche",
            $"X={p.XRel} Y={p.YRel}, {p.Breite}×{p.Höhe}, Z={p.ZTiefe}, Ø{p.FraeserD}", p));
    }

    private void OnKreistasche(object sender, RoutedEventArgs e)
    {
        var dlg = new KreistascheDialog(-(WorkZ + 3), werkzeuge: _werkzeuge.ToList()) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        var p = dlg.Result!;
        _history.Add(new HistoryEntry("Kreistasche",
            $"X={p.XRel} Y={p.YRel}, Ø{p.Durchmesser}, Z={p.ZTiefe}", p));
    }

    private bool IsPfadAktiv()
    {
        for (int i = _history.Count - 1; i >= 0; i--)
        {
            if (_history[i].Params is PfadPunktParams)
                return true;
            if (_history[i].Params is not null)
                return false;
        }
        return false;
    }

    private void UpdatePfadMenuState()
    {
        MnuPfadPunkt.IsEnabled = IsPfadAktiv();
    }

    private void OnPfadStart(object sender, RoutedEventArgs e)
    {
        var dlg = new PfadPunktDialog("Pfad – Startpunkt", -(WorkZ + 3), isStart: true, werkzeuge: _werkzeuge.ToList()) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        var p = dlg.Result! with { Typ = PfadPunktTyp.Start };
        _history.Add(new HistoryEntry("Pfad Start",
            $"X={p.XRel} Y={p.YRel}, Z={p.ZTiefe}, {p.Bezugspunkt}", p));
        UpdatePfadMenuState();
    }

    private void OnPfadPunkt(object sender, RoutedEventArgs e)
    {
        var dlg = new PfadPunktDialog("Pfad – Punkt", -(WorkZ + 3), werkzeuge: _werkzeuge.ToList()) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        var p = dlg.Result! with { Typ = PfadPunktTyp.Punkt };
        _history.Add(new HistoryEntry("Pfad Punkt",
            $"X={p.XRel} Y={p.YRel}, {p.Bezugspunkt}", p, level: 1));
    }

    // ── Gravieren ─────────────────────────────────────────────────
    private void OnGravieren      (object sender, RoutedEventArgs e) => OpenGravierenDialog();
    private void OnVCarve         (object sender, RoutedEventArgs e) => OpenGravierenDialog(isVCarve: true);
    private void OnVCarveRaster   (object sender, RoutedEventArgs e) => OpenGravierenDialog(isVCarveRaster: true);
    private void OnTextfeldTasche (object sender, RoutedEventArgs e) => OpenGravierenDialog(isTasche: true);

    private void OpenGravierenDialog(bool isVCarve = false, bool isTasche = false, bool isVCarveRaster = false)
    {
        string title = isTasche       ? "Gravieren – Textfeld A Tasche"
                     : isVCarveRaster ? "Gravieren – Textfeld A carve (Raster)"
                     : isVCarve       ? "Gravieren – Textfeld A carve"
                     : "Gravieren – Textfeld A umriss";
        var dlg = new GravierenDialog(werkzeuge: _werkzeuge.ToList(), workX: WorkX, workY: WorkY)
                      { Owner = this, Title = title };
        if (dlg.ShowDialog() != true) return;
        var p = dlg.Result! with
        {
            IsVCarve       = isVCarve,
            IsTasche       = isTasche,
            IsVCarveRaster = isVCarveRaster
        };
        string label = isTasche       ? "Textfeld-Tasche"
                     : isVCarveRaster ? "V-Carve Raster"
                     : isVCarve       ? "V-Carve"
                     : "Gravieren";
        // G-Code noch nicht berechnen – erst nach "G-Code berechnen"
        _suppressHistoryRegen = true;
        try { _history.Add(new HistoryEntry(label,
            $"\"{p.Text.Replace('\n', ' ')}\" {p.FontFamily} {p.FontSizeMm} mm", p)); }
        finally { _suppressHistoryRegen = false; }

        // Eintrag selektieren → UpdateEigenschaften() füllt Felder + ResetGCodeButton()
        HistoryList.SelectedItem  = _history[^1];
        TabEigenschaften.IsSelected = true;

        // Preview einschalten, Button als ausstehend markieren
        _previewGravParams = p;
        BtnGCodeBerechnen.Background = new SolidColorBrush(Color.FromRgb(0xC8, 0xA0, 0x30));
        BtnGCodeBerechnen.Content    = "● G-Code berechnen";
        UpdateAll();
    }

    // ── Eigenschaften-Tab ─────────────────────────────────────────
    private bool _eigSuppressUpdate;

    private void UpdateEigenschaften()
    {
        ResetGCodeButton(); // Ausstehende Änderungen des vorherigen Eintrags verwerfen
        var entry = HistoryList.SelectedItem as HistoryEntry;
        if (entry?.Params is GraviereParams p)
        {
            // Visibility nur ändern wenn kein Apply läuft (sonst verliert EigText den Fokus)
            if (!_eigSuppressUpdate)
            {
                TbEigKein.Visibility    = Visibility.Collapsed;
                PnlGravieren.Visibility = Visibility.Visible;
            }

            var inv = System.Globalization.CultureInfo.InvariantCulture;
            static void Set(TextBox tb, string val)
            { if (!tb.IsKeyboardFocused) tb.Text = val; }

            _eigSuppressUpdate = true;
            Set(EigText,            p.Text);
            if (!EigFont.IsKeyboardFocused)
            {
                var match = (EigFont.ItemsSource as IEnumerable<string>)?
                    .FirstOrDefault(f => f.Equals(p.FontFamily, StringComparison.OrdinalIgnoreCase));
                if (match != null) EigFont.SelectedItem = match;
                else               EigFont.Text         = p.FontFamily;
            }
            Set(EigTextBreite,      p.TextBreite.ToString(inv));
            Set(EigTextHoehe,       p.TextHoehe.ToString(inv));
            Set(EigFontSize,        p.FontSizeMm.ToString(inv));
            LblEigTiefe.Text = "Max. Tiefe (mm):";
            Set(EigTiefe,           p.ZTiefe.ToString(inv));
            Set(EigSchneidenWinkel, p.SchneidenWinkel.ToString(inv));
            Set(EigVorschub,        p.Vorschub.ToString(inv));
            Set(EigDrehzahl,        p.Drehzahl.ToString(inv));
            Set(EigVereinfachung,   p.VereinfachungMm.ToString(inv));
            EigAusrLinks.IsChecked  = p.Ausrichtung == "Links"  || string.IsNullOrEmpty(p.Ausrichtung);
            EigAusrMitte.IsChecked  = p.Ausrichtung == "Mitte";
            EigAusrRechts.IsChecked = p.Ausrichtung == "Rechts";
            _eigSuppressUpdate = false;

            TbEigInfo.Text = $"Pos: X={p.XRel} Y={p.YRel}  Bezug: {p.Bezugspunkt}";
        }
        else if (!_eigSuppressUpdate)
        {
            TbEigKein.Visibility    = Visibility.Visible;
            PnlGravieren.Visibility = Visibility.Collapsed;
        }
    }

    private void ApplyEigenschaften()
    {
        if (_eigSuppressUpdate) return;
        var entry = HistoryList.SelectedItem as HistoryEntry;
        if (entry?.Params is not GraviereParams p) return;
        int idx = _history.IndexOf(entry);
        if (idx < 0) return;

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var sty = System.Globalization.NumberStyles.Float;
        static string Norm(string s) => s.Replace(',', '.');
        if (!double.TryParse(Norm(EigTextBreite.Text),      sty, inv, out var tw)) return;
        if (!double.TryParse(Norm(EigTextHoehe.Text),       sty, inv, out var th)) return;
        if (!double.TryParse(Norm(EigFontSize.Text),        sty, inv, out var fs)  || fs <= 0) return;
        if (!double.TryParse(Norm(EigTiefe.Text),           sty, inv, out var zt) || zt <= 0) return;
        if (!double.TryParse(Norm(EigSchneidenWinkel.Text), sty, inv, out var sw) || sw <= 0 || sw >= 180) return;
        if (!double.TryParse(Norm(EigVorschub.Text),        sty, inv, out var vf)) return;
        if (!double.TryParse(Norm(EigDrehzahl.Text),        sty, inv, out var dr)) return;
        if (!double.TryParse(Norm(EigVereinfachung.Text),   sty, inv, out var ve) || ve < 0) ve = p.VereinfachungMm;

        double halfRad = sw / 2.0 * Math.PI / 180.0;
        double effW    = 2.0 * zt * Math.Tan(halfRad);

        string ausrichtung = EigAusrRechts.IsChecked == true ? "Rechts"
                           : EigAusrMitte.IsChecked  == true ? "Mitte"
                           : "Links";

        // SelectedItem hat Vorrang vor Text — bei editierbarem ComboBox kann
        // Text beim SelectionChanged-Event noch nicht aktualisiert sein.
        string fontFamily = (EigFont.SelectedItem as string)
                          ?? EigFont.Text.Trim();
        if (string.IsNullOrWhiteSpace(fontFamily)) return;

        var np = p with
        {
            Text            = EigText.Text,
            FontFamily      = fontFamily,
            FontSizeMm      = fs,
            TextBreite      = tw,
            TextHoehe       = th,
            ZTiefe           = zt,
            SchneidenWinkel  = sw,
            Vorschub         = vf,
            Drehzahl         = dr,
            Ausrichtung      = ausrichtung,
            VereinfachungMm  = ve
        };

        TbEigInfo.Text = $"Pos: X={np.XRel} Y={np.YRel}  Bezug: {np.Bezugspunkt}" +
                         $"  →  Schnittbreite: {effW:F3} mm";
        string lbl2 = np.IsTasche       ? "Textfeld-Tasche"
                    : np.IsVCarveRaster ? "V-Carve Raster"
                    : np.IsVCarve       ? "V-Carve"
                    : "Gravieren";
        // _eigSuppressUpdate + _suppressHistoryRegen: verhindert Panel-Flicker und doppeltes Regenerieren
        // während der ObservableCollection Replace-Event HistoryList.SelectionChanged auslöst
        _eigSuppressUpdate    = true;
        _suppressHistoryRegen = true;
        try { _history[idx] = new HistoryEntry(lbl2,
            $"\"{np.Text.Replace('\n', ' ')}\" {np.FontFamily} {np.FontSizeMm} mm", np); }
        finally { _suppressHistoryRegen = false; _eigSuppressUpdate = false; }

        RegenerateGCodeFromHistory();
        HistoryList.SelectedIndex = idx;
    }

    private void OnEigTextDirty(object sender, TextChangedEventArgs e) => UpdatePreviewFromFields();

    private void UpdatePreviewFromFields()
    {
        if (_eigSuppressUpdate) return;
        BtnGCodeBerechnen.Background = new SolidColorBrush(Color.FromRgb(0xC8, 0xA0, 0x30));
        BtnGCodeBerechnen.Content    = "● G-Code berechnen";

        var entry = HistoryList.SelectedItem as HistoryEntry;
        if (entry?.Params is GraviereParams gp)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var sty = System.Globalization.NumberStyles.Float;
            static string Norm(string s) => s.Replace(',', '.');

            string fontFamily = (EigFont.SelectedItem as string) ?? EigFont.Text.Trim();
            if (string.IsNullOrWhiteSpace(fontFamily)) fontFamily = gp.FontFamily;
            double fs = double.TryParse(Norm(EigFontSize.Text),      sty, inv, out var v1) && v1 > 0 ? v1 : gp.FontSizeMm;
            double tw = double.TryParse(Norm(EigTextBreite.Text),     sty, inv, out var v2)           ? v2 : gp.TextBreite;
            double th = double.TryParse(Norm(EigTextHoehe.Text),      sty, inv, out var v3)           ? v3 : gp.TextHoehe;
            double ve = double.TryParse(Norm(EigVereinfachung.Text),  sty, inv, out var v4) && v4 >= 0 ? v4 : gp.VereinfachungMm;
            string ausr = EigAusrRechts.IsChecked == true ? "Rechts"
                        : EigAusrMitte.IsChecked  == true ? "Mitte" : "Links";

            _previewGravParams = gp with
            {
                Text            = EigText.Text,
                FontFamily      = fontFamily,
                FontSizeMm      = fs,
                TextBreite      = tw,
                TextHoehe       = th,
                Ausrichtung     = ausr,
                VereinfachungMm = ve,
            };
        }
        UpdateAll();
    }

    private void OnGCodeBerechnen(object sender, RoutedEventArgs e)
    {
        ResetGCodeButton();
        ApplyEigenschaften();
    }

    private void ResetGCodeButton()
    {
        BtnGCodeBerechnen.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
        BtnGCodeBerechnen.Content = "G-Code berechnen";
        _previewGravParams = null;
    }

    // Numerische Felder → Debounce → Preview (kein G-Code)
    private void OnEigSizeChanged(object sender, TextChangedEventArgs e)          => RestartEigTimer();
    // Auswahl-Events → sofort Preview (kein G-Code)
    private void OnEigFontChanged(object sender, SelectionChangedEventArgs e)     => UpdatePreviewFromFields();
    private void OnEigFontKeyUp(object sender, KeyEventArgs e)                    => RestartEigTimer();
    private void RestartEigTimer() { _eigTimer.Stop(); _eigTimer.Start(); }
    private void OnEigAusrichtungChanged(object sender, RoutedEventArgs e)        => UpdatePreviewFromFields();
    private void OnHistorySelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateEigenschaften();

    // ── Verlauf: Doppelklick → Bearbeiten ───────────────────────
    private void OnHistoryDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var item = (e.OriginalSource as DependencyObject)?.FindVisualParent<ListBoxItem>();
        if (item?.DataContext is not HistoryEntry entry) return;
        EditHistoryEntry(entry);
    }

    // ── Verlauf: Tastatur (Ctrl+C, Ctrl+V, Del, Alt+↑↓) ────────
    private void OnHistoryKeyDown(object sender, KeyEventArgs e)
    {
        // Bei gedrücktem Alt liefert WPF e.Key == Key.System; der echte Key steckt in e.SystemKey
        var key  = e.Key == Key.System ? e.SystemKey : e.Key;
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool alt  = (Keyboard.Modifiers & ModifierKeys.Alt)     != 0;
        if      (ctrl && key == Key.C)    { CopySelectedHistory();    e.Handled = true; }
        else if (ctrl && key == Key.V)    { PasteHistory();           e.Handled = true; }
        else if (key == Key.Delete)       { DeleteSelectedHistory();  e.Handled = true; }
        else if (alt  && key == Key.Up)   { MoveSelectedHistoryUp();  e.Handled = true; }
        else if (alt  && key == Key.Down) { MoveSelectedHistoryDown(); e.Handled = true; }
    }

    // ── Verlauf: Clipboard ───────────────────────────────────────
    private void CopySelectedHistory()
    {
        _historyClipboard.Clear();
        _historyClipboard.AddRange(HistoryList.SelectedItems.Cast<HistoryEntry>()
            .OrderBy(_history.IndexOf));
    }

    private void PasteHistory()
    {
        if (_historyClipboard.Count == 0) return;
        int insertIdx = HistoryList.SelectedItems.Count > 0
            ? HistoryList.SelectedItems.Cast<HistoryEntry>().Max(_history.IndexOf)
            : _history.Count - 1;
        _suppressHistoryRegen = true;
        try
        {
            for (int i = 0; i < _historyClipboard.Count; i++)
            {
                var src = _historyClipboard[i];
                _history.Insert(insertIdx + 1 + i,
                    new HistoryEntry(src.Label, src.Details, src.Params, src.Level));
            }
        }
        finally { _suppressHistoryRegen = false; RegenerateGCodeFromHistory(); }
    }

    private void DeleteSelectedHistory()
    {
        var toDelete = HistoryList.SelectedItems.Cast<HistoryEntry>().ToList();
        _suppressHistoryRegen = true;
        try { foreach (var e in toDelete) _history.Remove(e); }
        finally { _suppressHistoryRegen = false; RegenerateGCodeFromHistory(); }
    }

    // ── Verlauf: Reihenfolge ─────────────────────────────────────
    private void OnHistoryMoveUp(object sender, RoutedEventArgs e)   => MoveSelectedHistoryUp();
    private void OnHistoryMoveDown(object sender, RoutedEventArgs e) => MoveSelectedHistoryDown();

    private void MoveSelectedHistoryUp()
    {
        var entries = HistoryList.SelectedItems.Cast<HistoryEntry>()
            .OrderBy(_history.IndexOf).ToList();
        if (entries.Count == 0) return;
        _suppressHistoryRegen = true;
        try
        {
            foreach (var entry in entries)
            {
                int idx = _history.IndexOf(entry);
                if (idx > 0) _history.Move(idx, idx - 1);
            }
        }
        finally { _suppressHistoryRegen = false; RegenerateGCodeFromHistory(); }
        RestoreSelection(entries);
    }

    private void MoveSelectedHistoryDown()
    {
        var entries = HistoryList.SelectedItems.Cast<HistoryEntry>()
            .OrderByDescending(_history.IndexOf).ToList();
        if (entries.Count == 0) return;
        _suppressHistoryRegen = true;
        try
        {
            foreach (var entry in entries)
            {
                int idx = _history.IndexOf(entry);
                if (idx < _history.Count - 1) _history.Move(idx, idx + 1);
            }
        }
        finally { _suppressHistoryRegen = false; RegenerateGCodeFromHistory(); }
        RestoreSelection(entries);
    }

    private void RestoreSelection(IEnumerable<HistoryEntry> entries)
    {
        HistoryList.SelectedItems.Clear();
        foreach (var e in entries) HistoryList.SelectedItems.Add(e);
    }

    private void EditHistoryEntry(HistoryEntry entry)
    {
        int idx = _history.IndexOf(entry);
        switch (entry.Params)
        {
            case PlanfräsenParams p:
            {
                var dlg = new PlanfräsenDialog(WorkX, WorkY, p, werkzeuge: _werkzeuge.ToList()) { Owner = this };
                if (dlg.ShowDialog() != true) return;
                var np = dlg.Result!;
                _history[idx] = new HistoryEntry("Planfräsen",
                    $"{(np.Horizontal ? "Horizontal" : "Vertikal")}, Z={np.Z}, Ø{np.FraeserD}", np);
                break;
            }
            case BohrungParams p:
            {
                var dlg = new BohrungDialog(WorkZ + 3, p, werkzeuge: _werkzeuge.ToList()) { Owner = this };
                if (dlg.ShowDialog() != true) return;
                var np = dlg.Result!;
                _history[idx] = new HistoryEntry("Bohrung",
                    $"X={np.XRel} Y={np.YRel}, Ø{np.Durchmesser}, Z={np.Bohrtiefe}, {np.Bezugspunkt}", np);
                break;
            }
            case ReihenlochbohrungParams p:
            {
                var dlg = new ReihenlochbohrungDialog(WorkZ + 3, p, werkzeuge: _werkzeuge.ToList()) { Owner = this };
                if (dlg.ShowDialog() != true) return;
                var np = dlg.Result!;
                _history[idx] = new HistoryEntry("Reihenlochbohrung",
                    $"{np.CountX}×{np.CountY}, Ø{np.Diameter}, Z={np.Bohrtiefe}", np);
                break;
            }
            case UmfahrenParams p:
            {
                var dlg = new UmfahrenDialog(WorkZ, p, werkzeuge: _werkzeuge.ToList()) { Owner = this };
                if (dlg.ShowDialog() != true) return;
                var np = dlg.Result!;
                _history[idx] = new HistoryEntry("Umfahren",
                    $"A={np.A}, Ø{np.Diameter}, Z={np.Z}", np);
                break;
            }
            case TascheFräsenParams p:
            {
                var dlg = new TascheFräsenDialog(-(WorkZ + 3), p, werkzeuge: _werkzeuge.ToList()) { Owner = this };
                if (dlg.ShowDialog() != true) return;
                var np = dlg.Result!;
                _history[idx] = new HistoryEntry("Tasche",
                    $"X={np.XRel} Y={np.YRel}, {np.Breite}×{np.Höhe}, Z={np.ZTiefe}, Ø{np.FraeserD}", np);
                break;
            }
            case KreistascheParams p:
            {
                var dlg = new KreistascheDialog(-(WorkZ + 3), p, werkzeuge: _werkzeuge.ToList()) { Owner = this };
                if (dlg.ShowDialog() != true) return;
                var np = dlg.Result!;
                _history[idx] = new HistoryEntry("Kreistasche",
                    $"X={np.XRel} Y={np.YRel}, Ø{np.Durchmesser}, Z={np.ZTiefe}", np);
                break;
            }
            case PfadPunktParams p:
            {
                string title = p.Typ == PfadPunktTyp.Start ? "Pfad – Startpunkt" : "Pfad – Punkt";
                var dlg = new PfadPunktDialog(title, -(WorkZ + 3), isStart: p.Typ == PfadPunktTyp.Start, p, werkzeuge: _werkzeuge.ToList()) { Owner = this };
                if (dlg.ShowDialog() != true) return;
                var np = dlg.Result! with { Typ = p.Typ };
                int lvl = np.Typ == PfadPunktTyp.Start ? 0 : 1;
                string det = np.Typ == PfadPunktTyp.Start
                    ? $"X={np.XRel} Y={np.YRel}, Z={np.ZTiefe}, {np.Bezugspunkt}"
                    : $"X={np.XRel} Y={np.YRel}, {np.Bezugspunkt}";
                _history[idx] = new HistoryEntry(np.Typ == PfadPunktTyp.Start ? "Pfad Start" : "Pfad Punkt", det, np, lvl);
                break;
            }
            case GraviereParams p:
            {
                string dlgTitle = p.IsTasche       ? "Gravieren – Textfeld A Tasche"
                                : p.IsVCarveRaster ? "Gravieren – Textfeld A carve (Raster)"
                                : p.IsVCarve       ? "Gravieren – Textfeld A carve"
                                : "Gravieren – Textfeld A umriss";
                var dlg = new GravierenDialog(p, werkzeuge: _werkzeuge.ToList())
                              { Owner = this, Title = dlgTitle };
                if (dlg.ShowDialog() != true) return;
                var np = dlg.Result! with
                {
                    Text            = p.Text,
                    FontFamily      = p.FontFamily,
                    FontSizeMm      = p.FontSizeMm,
                    TextBreite      = p.TextBreite,
                    TextHoehe       = p.TextHoehe,
                    ZTiefe          = p.ZTiefe,
                    SchneidenWinkel = p.SchneidenWinkel,
                    Vorschub        = p.Vorschub,
                    Drehzahl        = p.Drehzahl,
                    Ausrichtung     = p.Ausrichtung,
                    IsVCarve        = p.IsVCarve,
                    IsTasche        = p.IsTasche,
                    IsVCarveRaster  = p.IsVCarveRaster
                };
                string lbl = np.IsTasche       ? "Textfeld-Tasche"
                           : np.IsVCarveRaster ? "V-Carve Raster"
                           : np.IsVCarve       ? "V-Carve"
                           : "Gravieren";
                _history[idx] = new HistoryEntry(lbl,
                    $"\"{np.Text.Replace('\n', ' ')}\" {np.FontFamily} {np.FontSizeMm} mm", np);
                break;
            }
        }
        RegenerateGCodeFromHistory();
    }

    private void OnHistoryRightClick(object sender, MouseButtonEventArgs e)
    {
        var item = (e.OriginalSource as DependencyObject)?.FindVisualParent<ListBoxItem>();
        if (item == null) return;
        e.Handled = true;

        // Angeklicktes Element selektieren falls noch nicht in der Auswahl
        if (item.DataContext is HistoryEntry clicked && !HistoryList.SelectedItems.Contains(clicked))
        {
            HistoryList.SelectedItems.Clear();
            HistoryList.SelectedItems.Add(clicked);
        }

        int n = HistoryList.SelectedItems.Count;
        if (n == 0) return;
        string nStr = n == 1 ? "Eintrag" : $"{n} Einträge";

        var cm = new ContextMenu();
        var miCopy   = new MenuItem { Header = $"{nStr} kopieren (Ctrl+C)" };
        miCopy.Click += (_, _) => CopySelectedHistory();
        var miPaste  = new MenuItem { Header = "Einfügen (Ctrl+V)", IsEnabled = _historyClipboard.Count > 0 };
        miPaste.Click += (_, _) => PasteHistory();
        var miUp     = new MenuItem { Header = $"{nStr} nach oben (Alt+↑)" };
        miUp.Click   += (_, _) => MoveSelectedHistoryUp();
        var miDown   = new MenuItem { Header = $"{nStr} nach unten (Alt+↓)" };
        miDown.Click += (_, _) => MoveSelectedHistoryDown();
        var miDel    = new MenuItem { Header = $"{nStr} löschen (Del)" };
        miDel.Click  += (_, _) => DeleteSelectedHistory();

        cm.Items.Add(miCopy);
        cm.Items.Add(miPaste);
        cm.Items.Add(new Separator());
        cm.Items.Add(miUp);
        cm.Items.Add(miDown);
        cm.Items.Add(new Separator());
        cm.Items.Add(miDel);
        item.ContextMenu = cm;
        item.ContextMenu.IsOpen = true;
    }

    private void RegenerateGCodeFromHistory()
    {
        // Laufende Berechnung abbrechen (z. B. vorheriger Tastendruck noch in Arbeit)
        _regenCts?.Cancel();
        var cts = _regenCts = new CancellationTokenSource();

        // Snapshot auf UI-Thread – keine Zugriffe auf UI-Objekte im Hintergrund-Thread nötig
        var historySnap = _history.ToList();
        double workX = WorkX, workY = WorkY;

        // STA-Thread: WPF-Geometrie (FormattedText, PathGeometry) erfordert STA
        var t = new Thread(() =>
        {
            var sb          = new System.Text.StringBuilder();
            var pfadBuffer  = new List<PfadPunktParams>();
            double lastStartZ          = 0;
            string lastRadiuskorrektur = "Mittig";
            double lastFraeserD        = 0;

            void FlushPfad()
            {
                if (pfadBuffer.Count == 0) return;
                var c = GCodeGenerator.PfadFräsen(pfadBuffer, workX, workY);
                if (!string.IsNullOrEmpty(c)) sb.AppendLine(c);
                pfadBuffer.Clear();
            }

            foreach (var entry in historySnap)
            {
                if (cts.IsCancellationRequested) return;

                if (entry.Params is PfadPunktParams pfad)
                {
                    if (pfad.Typ == PfadPunktTyp.Start)
                    {
                        FlushPfad();
                        lastStartZ          = pfad.ZTiefe;
                        lastRadiuskorrektur = pfad.Radiuskorrektur;
                        lastFraeserD        = pfad.FraeserD;
                        pfadBuffer.Add(pfad);
                    }
                    else
                    {
                        pfadBuffer.Add(pfad with {
                            ZTiefe          = lastStartZ,
                            Radiuskorrektur = lastRadiuskorrektur,
                            FraeserD        = lastFraeserD
                        });
                    }
                    continue;
                }

                FlushPfad();
                string code = entry.Params switch
                {
                    PlanfräsenParams p        => GCodeGenerator.Planfräsen(p),
                    BohrungParams p           => GCodeGenerator.Bohrung(p, workX, workY),
                    ReihenlochbohrungParams p => GCodeGenerator.Reihenlochbohrung(p),
                    UmfahrenParams p          => GCodeGenerator.Umfahren(p, workX, workY),
                    TascheFräsenParams p      => GCodeGenerator.Tasche(p, workX, workY),
                    KreistascheParams p       => GCodeGenerator.Kreistasche(p, workX, workY),
                    GraviereParams p when p.IsTasche       => GCodeGenerator.TextfeldTasche(p, workX, workY),
                    GraviereParams p when p.IsVCarveRaster => GCodeGenerator.VCarveRaster(p, workX, workY),
                    GraviereParams p when p.IsVCarve       => GCodeGenerator.VCarve(p, workX, workY),
                    GraviereParams p                       => GCodeGenerator.Gravieren(p, workX, workY),
                    _                         => string.Empty
                };
                if (!string.IsNullOrEmpty(code)) sb.AppendLine(code);
            }
            FlushPfad();

            if (cts.IsCancellationRequested) return;
            var result = sb.ToString();

            Dispatcher.InvokeAsync(() =>
            {
                if (cts.IsCancellationRequested) return;
                _vCarveCache.Clear();
                GCodeText = result;
                UpdatePfadMenuState();
                UpdateAll();
            });
        });
        t.SetApartmentState(ApartmentState.STA);
        t.IsBackground = true;
        t.Start();
    }

#if false // ── Pfad Fräsen Panel ────────────────────────────────────────

    private void OnPfadAnzeigenChanged(object sender, RoutedEventArgs e)
    {
        PfadPanel.Visibility = CbPfadAnzeigen.IsChecked == true
            ? Visibility.Visible : Visibility.Collapsed;
        if (CbPfadAnzeigen.IsChecked == true)
            PfadTxtZ.Text = (-Math.Abs(WorkZ)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        UpdateAll();
    }

    private void OnPfadParamChanged(object sender, SelectionChangedEventArgs e) => UpdateAll();

    private void OnPfadPunktKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return) PfadPunktHinzufügen();
    }

    private void OnPfadHinzufügen(object sender, RoutedEventArgs e) => PfadPunktHinzufügen();

    private void PfadPunktHinzufügen()
    {
        if (!double.TryParse(PfadTxtX.Text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var x) ||
            !double.TryParse(PfadTxtY.Text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var y))
            return;

        string bezug = (PfadCbBezug?.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Unten links";
        _pfadPunkte.Add(new PfadPunkt
        {
            Nr    = _pfadPunkte.Count + 1,
            X     = x.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Y     = y.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Bezug = bezug
        });
        PfadLvPunkte.SelectedIndex = _pfadPunkte.Count - 1;
        PfadLvPunkte.ScrollIntoView(PfadLvPunkte.SelectedItem);
        PfadTxtX.Focus(); PfadTxtX.SelectAll();
    }

    private void OnPfadHoch(object sender, RoutedEventArgs e)
    {
        int i = PfadLvPunkte.SelectedIndex;
        if (i <= 0) return;
        (_pfadPunkte[i], _pfadPunkte[i - 1]) = (_pfadPunkte[i - 1], _pfadPunkte[i]);
        PfadLvPunkte.SelectedIndex = i - 1;
        PfadAktualisiereNummern();
    }

    private void OnPfadRunter(object sender, RoutedEventArgs e)
    {
        int i = PfadLvPunkte.SelectedIndex;
        if (i < 0 || i >= _pfadPunkte.Count - 1) return;
        (_pfadPunkte[i], _pfadPunkte[i + 1]) = (_pfadPunkte[i + 1], _pfadPunkte[i]);
        PfadLvPunkte.SelectedIndex = i + 1;
        PfadAktualisiereNummern();
    }

    private void OnPfadLöschen(object sender, RoutedEventArgs e)
    {
        int i = PfadLvPunkte.SelectedIndex;
        if (i < 0) return;
        _pfadPunkte.RemoveAt(i);
        PfadAktualisiereNummern();
        PfadLvPunkte.SelectedIndex = Math.Min(i, _pfadPunkte.Count - 1);
    }

#endif // OnWindowKeyDown aus #if false herausgenommen
    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is System.Windows.Controls.TextBox
            || e.OriginalSource is ICSharpCode.AvalonEdit.Editing.TextArea) return;
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        switch (e.Key)
        {
            case Key.H when !ctrl: SetActiveTool(_activeTool == CanvasTool.Hand ? CanvasTool.Select : CanvasTool.Hand); e.Handled = true; break;
            case Key.Z when !ctrl: SetActiveTool(_activeTool == CanvasTool.Zoom ? CanvasTool.Select : CanvasTool.Zoom); e.Handled = true; break;
            case Key.Escape:       SetActiveTool(CanvasTool.Select); e.Handled = true; break;
            case Key.D0 or Key.NumPad0 when ctrl: ZoomTo100();    e.Handled = true; break;
            case Key.D1 or Key.NumPad1 when ctrl: ZoomTo1to1();   e.Handled = true; break;
        }
    }
#if false

    private void OnPfadAlleLöschen(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Alle Punkte löschen?", "Bestätigung",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            _pfadPunkte.Clear();
    }

    private void OnPfadSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateAll();

    private void OnPfadCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        // Nach Commit der Zelle Zeichnung aktualisieren
        Dispatcher.BeginInvoke(UpdateAll, System.Windows.Threading.DispatcherPriority.Background);
    }

    // ── Pfeilklick: Punkt verschieben + Maus nachführen ──────────

    private void OnPfadArrowClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is not FrameworkElement el) return;
        var parts = (el.Tag as string ?? "").Split(':');
        if (parts.Length < 2 || !int.TryParse(parts[0], out int idx)) return;
        var dir = parts[1];
        if (idx < 0 || idx >= _pfadPunkte.Count) return;

        double relX = double.Parse(_pfadPunkte[idx].X, System.Globalization.CultureInfo.InvariantCulture);
        double relY = double.Parse(_pfadPunkte[idx].Y, System.Globalization.CultureInfo.InvariantCulture);
        string bezug = _pfadPunkte[idx].Bezug;
        double s = PfadSchritt;

        // Verschiebung in absoluten Koords (oben = +absY, unten = -absY)
        var (absX, absY) = RelToAbs(bezug, relX, relY, WorkX, WorkY);
        (absX, absY) = dir switch
        {
            "U" => (absX,     absY + s),
            "D" => (absX,     absY - s),
            "L" => (absX - s, absY),
            "R" => (absX + s, absY),
            _   => (absX, absY)
        };

        // Zurück in relative Koords für diesen Bezug
        var (newRelX, newRelY) = AbsToRel(bezug, absX, absY, WorkX, WorkY);
        _pfadPunkte[idx].X = newRelX.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        _pfadPunkte[idx].Y = newRelY.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        _pfadHoverIdx = idx;
        _arrowJustClicked = true;

        // Cursor um genau den Pixel-Betrag des Schritts verschieben
        // → bleibt relativ zur Klickposition, kein Voreilung
        var clickPos = e.GetPosition(DrawCanvas);
        double stepPx = s * _pfadScale;
        (double cdx, double cdy) = dir switch
        {
            "U" => (0.0,    -stepPx),
            "D" => (0.0,     stepPx),
            "L" => (-stepPx, 0.0),
            _   => (stepPx,  0.0)
        };
        var screen = DrawCanvas.PointToScreen(new Point(clickPos.X + cdx, clickPos.Y + cdy));
        SetCursorPos((int)screen.X, (int)screen.Y);

        PfadLvPunkte.Items.Refresh();
        UpdateAll();
    }

    private void PfadAktualisiereNummern()
    {
        for (int i = 0; i < _pfadPunkte.Count; i++) _pfadPunkte[i].Nr = i + 1;
        PfadLvPunkte.Items.Refresh();
        UpdateAll();
    }

    private void OnPfadGCodeErzeugen(object sender, RoutedEventArgs e)
    {
        if (_pfadPunkte.Count < 2)
        {
            MessageBox.Show("Mindestens 2 Punkte erforderlich.", "Hinweis",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var p = BuildPfadParams();
        if (p == null) return;
        var gcode = GCodeGenerator.PfadFräsen(p, WorkX, WorkY);
        PrependGeneratedGCode(gcode);
        UpdateAll();
    }

    private PfadFräsenParams? BuildPfadParams()
    {
        if (_pfadPunkte.Count == 0) return null;
        if (!double.TryParse(PfadTxtZ.Text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var z)) return null;
        if (!double.TryParse(PfadTxtZustellung.Text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var zu)) return null;
        if (!double.TryParse(PfadTxtVorschub.Text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v)) return null;
        if (!double.TryParse(PfadTxtDrehzahl.Text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var d)) return null;
        if (!double.TryParse(PfadTxtFraeserD.Text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var fd)) return null;

        double wx = WorkX, wy = WorkY;
        // Jeden Punkt in absolute Werkstück-Koords umrechnen (per-Punkt-Bezug)
        var punkte = _pfadPunkte.Select(p =>
        {
            double relX = double.Parse(p.X, System.Globalization.CultureInfo.InvariantCulture);
            double relY = double.Parse(p.Y, System.Globalization.CultureInfo.InvariantCulture);
            var (absX, absY) = RelToAbs(p.Bezug, relX, relY, wx, wy);
            return (X: absX, Y: absY);
        }).ToList();

        string seite = (PfadCbSeite.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Mitte";
        return new PfadFräsenParams(punkte, z, zu, v, d, fd, "absolut", seite);
    }
#endif // Pfad Fräsen Panel Ende

    // ── Canvas: Zoom / Pan ───────────────────────────────────────

    private void ApplyCanvasTransform()
    {
        var grp = new TransformGroup();
        grp.Children.Add(new ScaleTransform(_zoom, _zoom));
        grp.Children.Add(new TranslateTransform(_panX, _panY));
        DrawCanvas.RenderTransform = grp;
        if (TxtZoomLevel is not null)
            TxtZoomLevel.Text = $"{_zoom * 100:F0} %";
    }

    private void ResetZoom()
    {
        _zoom = 1.0; _panX = 0.0; _panY = 0.0;
        ApplyCanvasTransform();
    }

    // Ctrl+0: WPF-Zoom auf 100 % zurücksetzen (Ansicht wie beim Programmstart)
    private void ZoomTo100()
    {
        ResetZoom();
        UpdateAll();
    }

    // Ctrl+1: Originalmaßstab – 1 mm auf dem Bildschirm = 1 mm in Wirklichkeit
    private void ZoomTo1to1()
    {
        if (_topRect.IsEmpty || WorkX <= 0) return;
        // baseScale = WPF-DIPs pro mm bei zoom=1.0
        double baseScale = _topRect.Width / WorkX;

        // Physische Pixeldichte des Monitors (MDT_RAW_DPI = 2)
        // Formel: zoom = physDpi / (25.4 mm/in × baseScale × m11)
        //   m11  = WPF-DIPs → physische Pixel (entspricht dem Windows-Skalierungsfaktor)
        //   physDpi = tatsächliche Pixel pro Zoll des Monitors
        double physDpi = 96.0;
        try
        {
            var helper  = new System.Windows.Interop.WindowInteropHelper(this);
            var monitor = MonitorFromWindow(helper.Handle, 2 /* MONITOR_DEFAULTTONEAREST */);
            if (GetDpiForMonitor(monitor, 2 /* MDT_RAW_DPI */, out uint dx, out _) == 0)
                physDpi = dx;
        }
        catch { }

        var src  = PresentationSource.FromVisual(this);
        double m11 = src?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;

        double newZoom = Math.Clamp(physDpi / (25.4 * baseScale * m11), 0.05, 200.0);

        // Draufsicht-Werkstück mittig auf dem Canvas halten
        double cx  = DrawCanvas.ActualWidth  / 2;
        double cy  = DrawCanvas.ActualHeight / 2;
        double wCx = _topRect.Left + _topRect.Width  / 2;
        double wCy = _topRect.Top  + _topRect.Height / 2;
        _zoom = newZoom;
        _panX = cx - wCx * _zoom;
        _panY = cy - wCy * _zoom;
        ApplyCanvasTransform();
        UpdateAll();
    }

    // ── Werkzeugpalette ──────────────────────────────────────────

    private void SetActiveTool(CanvasTool tool)
    {
        _activeTool = tool;
        var active   = new System.Windows.Media.SolidColorBrush(
                           System.Windows.Media.Color.FromArgb(0xCC, 0xDD, 0xD0, 0xB0));
        var inactive = System.Windows.Media.Brushes.Transparent;
        BtnToolHand.Background = tool == CanvasTool.Hand ? active : inactive;
        BtnToolZoom.Background = tool == CanvasTool.Zoom ? active : inactive;
        DrawCanvas.Cursor = tool switch
        {
            CanvasTool.Hand => Cursors.Hand,
            CanvasTool.Zoom => Cursors.Cross,
            _               => Cursors.Arrow,
        };
    }

    private void OnToolHand(object sender, RoutedEventArgs e)
        => SetActiveTool(_activeTool == CanvasTool.Hand ? CanvasTool.Select : CanvasTool.Hand);

    private void OnToolZoom(object sender, RoutedEventArgs e)
        => SetActiveTool(_activeTool == CanvasTool.Zoom ? CanvasTool.Select : CanvasTool.Zoom);

    private void OnZoom100(object sender, RoutedEventArgs e)   => ZoomTo100();
    private void OnZoom1to1(object sender, RoutedEventArgs e)  => ZoomTo1to1();

    private void UpdateZoomRubberBand(Point p1, Point p2)
    {
        if (_zoomRubberBand == null)
        {
            _zoomRubberBand = new System.Windows.Shapes.Rectangle
            {
                Stroke            = System.Windows.Media.Brushes.White,
                StrokeThickness   = 1.5,
                StrokeDashArray   = new System.Windows.Media.DoubleCollection { 5, 3 },
                Fill              = new System.Windows.Media.SolidColorBrush(
                                        System.Windows.Media.Color.FromArgb(30, 80, 140, 255)),
                IsHitTestVisible  = false,
            };
        }
        double x = Math.Min(p1.X, p2.X);
        double y = Math.Min(p1.Y, p2.Y);
        _zoomRubberBand.Width  = Math.Abs(p2.X - p1.X);
        _zoomRubberBand.Height = Math.Abs(p2.Y - p1.Y);
        System.Windows.Controls.Canvas.SetLeft(_zoomRubberBand, x);
        System.Windows.Controls.Canvas.SetTop (_zoomRubberBand, y);
        if (!SimToolCanvas.Children.Contains(_zoomRubberBand))
            SimToolCanvas.Children.Add(_zoomRubberBand);
    }

    private void ClearZoomRubberBand()
    {
        if (_zoomRubberBand != null)
            SimToolCanvas.Children.Remove(_zoomRubberBand);
    }

    private void ZoomToRect(Point parentP1, Point parentP2)
    {
        // Parent-Koordinaten → DrawCanvas-lokale Koordinaten (vor Transform)
        double lx1 = (parentP1.X - _panX) / _zoom;
        double ly1 = (parentP1.Y - _panY) / _zoom;
        double lx2 = (parentP2.X - _panX) / _zoom;
        double ly2 = (parentP2.Y - _panY) / _zoom;
        double localW = Math.Abs(lx2 - lx1);
        double localH = Math.Abs(ly2 - ly1);
        if (localW < 1 || localH < 1) return;

        double cw = DrawCanvas.ActualWidth;
        double ch = DrawCanvas.ActualHeight;
        double newZoom = Math.Clamp(Math.Min(cw / localW, ch / localH), 0.05, 200.0);

        double centerX = (Math.Min(lx1, lx2) + localW / 2);
        double centerY = (Math.Min(ly1, ly2) + localH / 2);
        _zoom = newZoom;
        _panX = cw / 2 - centerX * _zoom;
        _panY = ch / 2 - centerY * _zoom;
        ApplyCanvasTransform();
        UpdateAll();
    }

    private void OnCanvasMouseWheel(object sender, MouseWheelEventArgs e)
    {
        double factor  = e.Delta > 0 ? 1.18 : 1.0 / 1.18;
        double newZoom = Math.Clamp(_zoom * factor, 0.05, 200.0);

        // Zoom auf Mauszeiger ausrichten:
        // screen = local * zoom + pan  →  pan_new = pan + local * (zoom - newZoom)
        var local = e.GetPosition(DrawCanvas); // lokale Canvas-Koordinaten (vor Transform)
        _panX += local.X * (_zoom - newZoom);
        _panY += local.Y * (_zoom - newZoom);
        _zoom  = newZoom;

        // Nur Transform setzen (GPU) — UpdateAll erst 120 ms nach letztem Rad-Ereignis
        ApplyCanvasTransform();
        _zoomTimer.Stop();
        _zoomTimer.Start();
        e.Handled = true;
    }

    // ── Canvas: Klick / Drag ─────────────────────────────────────

    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Zoom-Werkzeug
        if (_activeTool == CanvasTool.Zoom)
        {
            // Rechtsklick / Alt+Links → sofort herauszoomen
            if (e.ChangedButton == MouseButton.Right ||
                (e.ChangedButton == MouseButton.Left && (Keyboard.Modifiers & ModifierKeys.Alt) != 0))
            {
                double newZoom = Math.Clamp(_zoom * 0.5, 0.05, 200.0);
                var local = e.GetPosition(DrawCanvas);
                _panX += local.X * (_zoom - newZoom);
                _panY += local.Y * (_zoom - newZoom);
                _zoom  = newZoom;
                ApplyCanvasTransform();
                UpdateAll();
                e.Handled = true;
                return;
            }
            // Linksklick → Drag-Tracking starten (Gummiband oder Klick-Zoom bei MouseUp)
            if (e.ChangedButton == MouseButton.Left)
            {
                _zoomDragStart  = e.GetPosition((UIElement)DrawCanvas.Parent);
                _isZoomDragging = false;
                DrawCanvas.CaptureMouse();
                e.Handled = true;
                return;
            }
        }

        // Doppelklick setzt Zoom zurück (nur ohne aktives Werkzeug)
        if (_activeTool == CanvasTool.Select &&
            e.ChangedButton == MouseButton.Left && e.ClickCount == 2)
        {
            ResetZoom();
            return;
        }

        // Pan starten: Rechtsklick immer, Linksklick beim Hand-Werkzeug
        bool startPan = e.ChangedButton == MouseButton.Right
                        || (e.ChangedButton == MouseButton.Left && _activeTool == CanvasTool.Hand);
        if (!startPan) return;
        _isPanning = true;
        _panStart  = e.GetPosition((UIElement)DrawCanvas.Parent);
        _panOrigin = new Point(_panX, _panY);
        DrawCanvas.CaptureMouse();
        DrawCanvas.Cursor = Cursors.SizeAll;
        e.Handled = true;
    }

    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            // Zoom-Werkzeug: Drag beendet oder Klick
            if (_activeTool == CanvasTool.Zoom && DrawCanvas.IsMouseCaptured)
            {
                DrawCanvas.ReleaseMouseCapture();
                if (_isZoomDragging)
                {
                    ClearZoomRubberBand();
                    ZoomToRect(_zoomDragStart, e.GetPosition((UIElement)DrawCanvas.Parent));
                }
                else
                {
                    // Kurzer Klick → 2× hineinzoomen auf Klickposition
                    double newZoom = Math.Clamp(_zoom * 2.0, 0.05, 200.0);
                    var local = e.GetPosition(DrawCanvas);
                    _panX += local.X * (_zoom - newZoom);
                    _panY += local.Y * (_zoom - newZoom);
                    _zoom  = newZoom;
                    ApplyCanvasTransform();
                    UpdateAll();
                }
                _isZoomDragging = false;
                e.Handled = true;
                return;
            }

            if (_isPanning && _activeTool == CanvasTool.Hand)
            {
                _isPanning = false;
                DrawCanvas.ReleaseMouseCapture();
                DrawCanvas.Cursor = Cursors.Hand;
                return;
            }
            // Linksklick auf leere Fläche → Auswahl aufheben
            if (e.ClickCount == 1 && _selectedGCodeLine >= 0 && !_isPanning && e.OriginalSource == DrawCanvas)
            {
                SetSelectedGCodeLine(-1);
                UpdateAll();
            }
            return;
        }
        if (e.ChangedButton != MouseButton.Right || !_isPanning) return;
        _isPanning = false;
        DrawCanvas.ReleaseMouseCapture();
        DrawCanvas.Cursor = _activeTool switch
        {
            CanvasTool.Hand => Cursors.Hand,
            CanvasTool.Zoom => Cursors.Cross,
            _               => Cursors.Arrow,
        };
    }

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        // Zoom-Werkzeug: Gummiband-Rechteck aufziehen
        if (_activeTool == CanvasTool.Zoom && DrawCanvas.IsMouseCaptured && !_isPanning)
        {
            var pos   = e.GetPosition((UIElement)DrawCanvas.Parent);
            var delta = pos - _zoomDragStart;
            if (!_isZoomDragging && (Math.Abs(delta.X) > 4 || Math.Abs(delta.Y) > 4))
                _isZoomDragging = true;
            if (_isZoomDragging)
                UpdateZoomRubberBand(_zoomDragStart, pos);
            return;
        }

        if (!_isPanning) return;
        var panPos = e.GetPosition((UIElement)DrawCanvas.Parent);
        _panX = _panOrigin.X + (panPos.X - _panStart.X);
        _panY = _panOrigin.Y + (panPos.Y - _panStart.Y);
        ApplyCanvasTransform();
    }

    private void OnCanvasMouseLeave(object sender, MouseEventArgs e)
    {
        if (_isZoomDragging)
        {
            _isZoomDragging = false;
            ClearZoomRubberBand();
            DrawCanvas.ReleaseMouseCapture();
            return;
        }
        if (!_isPanning) return;
        _isPanning = false;
        DrawCanvas.ReleaseMouseCapture();
        DrawCanvas.Cursor = _activeTool switch
        {
            CanvasTool.Hand => Cursors.Hand,
            CanvasTool.Zoom => Cursors.Cross,
            _               => Cursors.Arrow,
        };
    }

    private void OnInfo(object sender, RoutedEventArgs e)
    {
        var dlg = new Window
        {
            Title           = "Info",
            SizeToContent   = SizeToContent.WidthAndHeight,
            ResizeMode      = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner           = this,
            Background      = new System.Windows.Media.SolidColorBrush(
                                  (System.Windows.Media.Color)System.Windows.Media.ColorConverter
                                  .ConvertFromString("#d8e0ea")),
        };

        // Grössten Frame aus der ICO-Datei laden
        var decoder = new System.Windows.Media.Imaging.IconBitmapDecoder(
            new Uri("pack://application:,,,/icon.ico"),
            System.Windows.Media.Imaging.BitmapCreateOptions.None,
            System.Windows.Media.Imaging.BitmapCacheOption.Default);
        var bestFrame = decoder.Frames
            .OrderByDescending(f => f.PixelWidth)
            .First();

        var img = new System.Windows.Controls.Image
        {
            Width   = 128,
            Height  = 128,
            Margin  = new Thickness(0, 0, 0, 12),
            HorizontalAlignment = HorizontalAlignment.Center,
            Source  = bestFrame,
        };
        System.Windows.Media.RenderOptions.SetBitmapScalingMode(
            img, System.Windows.Media.BitmapScalingMode.HighQuality);

        var txt = new System.Windows.Controls.TextBlock
        {
            Text            = "NC Studio – G-Code Generator & Visualisierer\n\n" +
                              "NC Studio ist ein CNC-Hilfsprogramm für die Holzbearbeitung und das Fräsen. " +
                              "Es unterstützt bei der Erstellung und Visualisierung von G-Code für CNC-Maschinen.\n\n" +
                              "G-Code ist eine weit verbreitete Programmiersprache zur Steuerung von CNC-Maschinen. " +
                              "Er beschreibt Bewegungen, Geschwindigkeiten und Werkzeugbefehle in einer Abfolge von Befehlen – " +
                              "zum Beispiel bestimmt G0 eine schnelle Leerfahrt und G1 eine gefräste Linie mit definiertem Vorschub.\n\n" +
                              "\"NC\" steht für Numerical Control (Numerische Steuerung), " +
                              "die technologische Grundlage moderner CNC-Maschinen-Steuerung.\n\n" +
                              "Entwickler: Joel Suter",
            TextAlignment   = TextAlignment.Center,
            Foreground      = new System.Windows.Media.SolidColorBrush(
                                  (System.Windows.Media.Color)System.Windows.Media.ColorConverter
                                  .ConvertFromString("#1a1a1a")),
            FontSize        = 13,
            Margin          = new Thickness(0, 0, 0, 16),
            MaxWidth        = 480,
            TextWrapping    = System.Windows.TextWrapping.Wrap,
        };

        var btn = new System.Windows.Controls.Button
        {
            Content             = "OK",
            Width               = 80,
            HorizontalAlignment = HorizontalAlignment.Center,
            Background          = new System.Windows.Media.SolidColorBrush(
                                      (System.Windows.Media.Color)System.Windows.Media.ColorConverter
                                      .ConvertFromString("#DDD0B0")),
            Foreground          = System.Windows.Media.Brushes.White,
            BorderThickness     = new Thickness(0),
            Padding             = new Thickness(8, 4, 8, 4),
        };
        btn.Click += (_, __) => dlg.Close();

        var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(32, 24, 32, 24) };
        panel.Children.Add(img);
        panel.Children.Add(txt);
        panel.Children.Add(btn);
        dlg.Content = panel;
        dlg.ShowDialog();
    }

    // ── Aktualisieren ─────────────────────────────────────────────

    private void OnAktualisieren(object sender, RoutedEventArgs e) => UpdateAll();

    private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e) => UpdateAll();

    private bool _gcodeBoxDirty = false;

    private string GCodeText
    {
        get => _gcodeContent;
        set
        {
            if (_gcodeContent == value) return;
            _gcodeContent    = value;
            _parsedGCodeText = null;
            _gcodeBoxDirty   = true;
            _simPathDirty    = true;

            // TextBox nur aktualisieren wenn G-Code Tab aktiv ist
            if (IsGCodeTabActive())
                FlushGCodeBox();
        }
    }

    private bool IsGCodeTabActive() =>
        TabGCode.Visibility == Visibility.Visible &&
        TabGCode.IsSelected;


    private void FlushGCodeBox()
    {
        if (!_gcodeBoxDirty) return;
        _gcodeBoxDirty = false;

        _suppressGCodeUiUpdate = true;
        try { GCodeBox.Text = _gcodeContent; }
        finally { _suppressGCodeUiUpdate = false; }
    }

    private void OnGCodeTextChanged(object? sender, EventArgs e)
    {
        if (_suppressGCodeUiUpdate) return;
        _gcodeContent    = GCodeBox.Text;
        _parsedGCodeText = null;
        _gcodeBoxDirty   = false;
        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    private void EnsureParsed()
    {
        if (_gcodeContent == _parsedGCodeText) return;
        _parsedGCodeText   = _gcodeContent;
        _cachedTopMoves    = GCodeParser.ParseTopView(_gcodeContent);
        _cachedSideMoves   = GCodeParser.ParseSideView(_gcodeContent);
        _cachedDrillPoints = GCodeParser.ParseDrillPoints(_gcodeContent);
    }

    private void OnGCodeBoxLoaded(object sender, RoutedEventArgs e)
    {
        // Guard: Loaded feuert beim Tab-Wechsel erneut — keine Doppel-Registrierung
        if (GCodeBox.TextArea.TextView.LineTransformers.OfType<GCodeColorizer>().Any()) return;

        GCodeBox.TextChanged += OnGCodeTextChanged;
        GCodeBox.TextArea.TextView.LineTransformers.Add(new GCodeColorizer());
        _gcodeBgRenderer = new GCodeLineBackgroundRenderer();
        GCodeBox.TextArea.TextView.BackgroundRenderers.Add(_gcodeBgRenderer);
        GCodeBox.TextArea.TextView.MouseHover        += OnGCodeMouseHover;
        GCodeBox.TextArea.TextView.MouseHoverStopped += OnGCodeMouseHoverStopped;
        GCodeBox.TextArea.LeftMargins.Add(new GCodeArrowMargin());
        GCodeBox.TextArea.Caret.PositionChanged      += OnGCodeCaretMoved;
        GCodeBox.TextArea.TextView.MouseMove              += OnGCodeEditorMouseMove;
        GCodeBox.TextArea.TextView.MouseLeave             += OnGCodeEditorMouseLeave;
        GCodeBox.TextArea.TextView.MouseLeftButtonDown    += OnGCodeEditorClick;
        GCodeBox.TextArea.PreviewKeyDown                  += OnGCodeEditorKeyDown;
    }

    private ToolTip? _gCodeToolTip;

    private void OnGCodeMouseHover(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var tv  = GCodeBox.TextArea.TextView;
        var pos = tv.GetPositionFloor(e.GetPosition(tv) + tv.ScrollOffset);
        if (pos is null) return;

        int offset  = GCodeBox.Document.GetOffset(pos.Value.Location);
        var docLine = GCodeBox.Document.GetLineByOffset(offset);
        var lineText = GCodeBox.Document.GetText(docLine);
        int col = offset - docLine.Offset;

        var (tipTitle, tipDesc) = GCodeTooltip.GetTooltip(lineText, col);
        if (tipDesc is null) return;

        if (_gCodeToolTip is null)
        {
            _gCodeToolTip = new ToolTip
            {
                Placement       = System.Windows.Controls.Primitives.PlacementMode.Mouse,
                Background      = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                HasDropShadow   = false,
                Padding         = new Thickness(0)
            };
        }
        _gCodeToolTip.Content = BuildTooltipBubble(tipTitle, tipDesc);
        _gCodeToolTip.IsOpen  = true;
        e.Handled = true;
    }

    private void OnGCodeMouseHoverStopped(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_gCodeToolTip is not null) _gCodeToolTip.IsOpen = false;
    }

    // ── G-Code → Canvas Zeilenmarkierung ────────────────────────

    private void OnGCodeCaretMoved(object? sender, EventArgs e)
    {
        int ln = GCodeBox.TextArea.Caret.Line;
        if (ln == _highlightGCodeLine) return;
        _highlightGCodeLine = ln;
        if (_mouseHoverLine < 1) { _hlTimer.Stop(); _hlTimer.Start(); }
    }

    private void OnGCodeEditorMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var tv  = GCodeBox.TextArea.TextView;
        var vp  = tv.GetPositionFloor(e.GetPosition(tv) + tv.ScrollOffset);
        int ln  = vp?.Line ?? -1;
        if (ln == _mouseHoverLine) return;
        _mouseHoverLine = ln;
        // Editor sofort aktualisieren (kein Debounce nötig)
        if (_gcodeBgRenderer != null)
        {
            _gcodeBgRenderer.HoverLine = ln;
            GCodeBox.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        }
        // Canvas via Debounce (80 ms)
        _hlTimer.Stop(); _hlTimer.Start();
    }

    private void OnGCodeEditorMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_mouseHoverLine < 0) return;
        _mouseHoverLine = -1;
        if (_gcodeBgRenderer != null)
        {
            _gcodeBgRenderer.HoverLine = -1;
            GCodeBox.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        }
        _hlTimer.Stop(); _hlTimer.Start();
    }

    private void OnGCodeEditorKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_selectedGCodeLine < 1) return;
        int delta = e.Key switch
        {
            Key.Up   => -1,
            Key.Down => +1,
            _        => 0
        };
        if (delta == 0) return;

        int max  = GCodeBox.Document.LineCount;
        int next = Math.Clamp(_selectedGCodeLine + delta, 1, max);
        if (next == _selectedGCodeLine) return;

        SetSelectedGCodeLine(next);
        // Caret mitbewegen damit die Zeile sichtbar bleibt
        GCodeBox.TextArea.Caret.Line   = next;
        GCodeBox.TextArea.Caret.Column = 1;
        GCodeBox.TextArea.Caret.BringCaretToView();
        UpdateAll();
        e.Handled = true;   // Standard-Caret-Bewegung unterdrücken
    }

    private void OnGCodeEditorClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var tv = GCodeBox.TextArea.TextView;
        var vp = tv.GetPositionFloor(e.GetPosition(tv) + tv.ScrollOffset);
        int ln = vp?.Line ?? -1;
        if (ln < 1) return;
        // Toggle: nochmal klicken hebt Selektion auf
        SetSelectedGCodeLine(_selectedGCodeLine == ln ? -1 : ln);
        UpdateAll();
    }

    private void DrawGCodeHighlight()
    {
        // Alte "hl"-Elemente aufräumen
        for (int i = DrawCanvas.Children.Count - 1; i >= 0; i--)
            if (DrawCanvas.Children[i] is FrameworkElement fe && fe.Tag is "hl")
                DrawCanvas.Children.RemoveAt(i);

        // Zoom-invarianter Locator-Ring für selektierte Form
        if (_selectedGCodeLine >= 1 && _topRect.Width > 0)
            DrawSelectionLocator();
    }

    // Konvertierung G-Code-mm → Canvas-Pixel (gleiche Formel wie lokales MmToPx in DrawGCodeTopView)
    private Point TopMmToPx(double x, double y)
    {
        double wx = WorkX, wy = WorkY;
        if (wx <= 0 || wy <= 0) return default;
        double scale = Math.Min(_topRect.Width / wx, _topRect.Height / wy);
        return new(_topRect.Left + x * scale, _topRect.Bottom - y * scale);
    }

    private void DrawSelectionLocator()
    {
        // Tatsächliche Bildschirmgrösse = Canvas-Pixel × _zoom (RenderTransform skaliert den Canvas)
        const double VisibleThreshold = 6.0;   // Bildschirm-Pixel

        Point? pt = null;
        bool   showRing = false;

        var hole = _cachedDrillPoints.FirstOrDefault(h => h.LineNumber == _selectedGCodeLine);
        if (hole != null)
        {
            // Bohrpunkte sind immer als 10-px-Kreis gerendert → immer sichtbar, kein Ring nötig
            pt = TopMmToPx(hole.X, hole.Y);
            showRing = false;
        }
        else
        {
            int idx  = _cachedTopMoves.FindIndex(m => m.LineNumber == _selectedGCodeLine);
            var move = idx >= 0 ? _cachedTopMoves[idx] : null;
            if (move != null)
            {
                if (move.Type is MoveType.ArcCW or MoveType.ArcCCW)
                {
                    double wx = WorkX, wy = WorkY;
                    double scale = (wx > 0 && wy > 0)
                        ? Math.Min(_topRect.Width / wx, _topRect.Height / wy) : 1;
                    double arScreenPx = Math.Sqrt(move.I * move.I + move.J * move.J) * scale * _zoom;
                    showRing = arScreenPx < VisibleThreshold;
                    pt = TopMmToPx((move.X + move.Xe) / 2.0, (move.Y + move.Ye) / 2.0);
                }
                else
                {
                    // Startpunkt = Endpunkt des vorherigen Moves
                    Point startPt = idx > 0
                        ? TopMmToPx(_cachedTopMoves[idx - 1].X, _cachedTopMoves[idx - 1].Y)
                        : TopMmToPx(0, 0);
                    Point endPt = TopMmToPx(move.X, move.Y);
                    double lenScreenPx = Math.Sqrt(
                        Math.Pow(endPt.X - startPt.X, 2) +
                        Math.Pow(endPt.Y - startPt.Y, 2)) * _zoom;
                    showRing = lenScreenPx < VisibleThreshold;
                    pt = endPt;
                }
            }
        }

        if (!showRing || pt == null) return;
        double px = pt.Value.X, py = pt.Value.Y;

        // Ring und Ticks in fester Screengrösse (zoom-invariant)
        const double R    = 20;   // Ring-Radius in Pixel
        const double tick = 7;    // Tick-Länge ausserhalb des Rings
        const double gap  = 4;    // Lücke zwischen Ring und Tick
        var gold = new SolidColorBrush(Color.FromRgb(255, 215, 0));

        var ring = new Ellipse
        {
            Width=R*2, Height=R*2,
            Stroke=gold, StrokeThickness=1.8,
            Fill=Brushes.Transparent,
            Tag="hl", IsHitTestVisible=false
        };
        Canvas.SetLeft(ring, px - R); Canvas.SetTop(ring, py - R);
        DrawCanvas.Children.Add(ring);

        void Tick(double x1, double y1, double x2, double y2) =>
            DrawCanvas.Children.Add(new Line
            {
                X1=x1, Y1=y1, X2=x2, Y2=y2,
                Stroke=gold, StrokeThickness=1.5,
                StrokeStartLineCap=PenLineCap.Round, StrokeEndLineCap=PenLineCap.Round,
                Tag="hl", IsHitTestVisible=false
            });

        Tick(px - R - gap - tick, py,  px - R - gap, py);
        Tick(px + R + gap,        py,  px + R + gap + tick, py);
        Tick(px, py - R - gap - tick,  px, py - R - gap);
        Tick(px, py + R + gap,         px, py + R + gap + tick);
    }

    // ── Klick auf Werkstück-Form ──────────────────────────────────

    private void SetSelectedGCodeLine(int ln)
    {
        _selectedGCodeLine = ln;
        // Beim Deselektieren auch den Caret-Highlight auf dem Canvas löschen —
        // sonst bleibt die Form durch _highlightGCodeLine (= Caret) halb sichtbar.
        if (ln < 0) _highlightGCodeLine = -1;
        if (_gcodeBgRenderer != null)
        {
            _gcodeBgRenderer.SelectedLine = ln;
            GCodeBox.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        }
    }

    private void OnTopViewFormClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    { _selectionSource = 0; OnWorkpieceFormClick(sender, e); }

    private void OnSideViewFormClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    { _selectionSource = 1; OnWorkpieceFormClick(sender, e); }

    private void OnWorkpieceFormClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;   // kein Pan starten
        if (sender is not FrameworkElement el || el.Tag is not int ln) return;
        SetSelectedGCodeLine(_selectedGCodeLine == ln ? -1 : ln);   // toggle
        UpdateAll();   // Form neu zeichnen (mit neuer Farbe)

        // G-Code Editor scrollen und Zeile markieren
        if (_selectedGCodeLine >= 1 && GCodeBox.Document.LineCount >= _selectedGCodeLine)
        {
            GCodeBox.TextArea.Caret.Line   = _selectedGCodeLine;
            GCodeBox.TextArea.Caret.Column = 1;
            GCodeBox.TextArea.Caret.BringCaretToView();
        }
    }

    // Mini-Interpreter: gibt (vorherige X/Y, aktuelle X/Y, Bewegungstyp) für die Ziellinie zurück
    private static readonly Regex RxHlX = new(@"(?<![A-Za-z])X([+-]?[\d.]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxHlY = new(@"(?<![A-Za-z])Y([+-]?[\d.]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxHlG = new(@"\bG(0{1,2}|1|00|01|02|03|2|3)\b",  RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static (double px, double py, double cx, double cy, string moveType)?
        GetGCodePosAtLine(string gcode, int targetLine)
    {
        var inv   = System.Globalization.CultureInfo.InvariantCulture;
        var style = System.Globalization.NumberStyles.Float;
        double x = 0, y = 0;
        bool abs = true;
        string modal = "G1";   // modaler Bewegungsbefehl (Standard G1)
        int ln = 0;

        foreach (var raw in gcode.Split('\n'))
        {
            ln++;
            var line = raw.Trim();
            int ci = line.IndexOf('('); if (ci >= 0) line = line[..ci].Trim();
            ci = line.IndexOf(';');     if (ci >= 0) line = line[..ci].Trim();

            if (string.IsNullOrEmpty(line))
            { if (ln == targetLine) return null; continue; }

            if (Regex.IsMatch(line, @"\bG90\b", RegexOptions.IgnoreCase)) abs = true;
            if (Regex.IsMatch(line, @"\bG91\b", RegexOptions.IgnoreCase)) abs = false;

            // Bewegungsbefehl dieser Zeile ermitteln
            string? lineMove = null;
            foreach (Match mg in RxHlG.Matches(line))
            {
                var n = mg.Groups[1].Value.TrimStart('0');
                if (n == "" ) n = "0";
                lineMove = n switch { "0" => "G0", "1" => "G1", "2" => "G2", "3" => "G3", _ => null };
                if (lineMove != null) { modal = lineMove; break; }
            }

            var mx = RxHlX.Match(line);
            var my = RxHlY.Match(line);
            double vx = 0, vy = 0;
            bool hasX = mx.Success && double.TryParse(mx.Groups[1].Value, style, inv, out vx);
            bool hasY = my.Success && double.TryParse(my.Groups[1].Value, style, inv, out vy);

            double nx = hasX ? (abs ? vx : x + vx) : x;
            double ny = hasY ? (abs ? vy : y + vy) : y;

            if (ln == targetLine)
            {
                string mt = (hasX || hasY) ? modal : (lineMove ?? "");
                return (x, y, nx, ny, mt);
            }

            if (hasX || hasY) { x = nx; y = ny; }
        }
        return null;
    }

    private static UIElement BuildTooltipBubble(string? title, string desc)
    {
        var panel = new StackPanel { Margin = new Thickness(10, 7, 10, 8) };
        if (!string.IsNullOrEmpty(title))
        {
            panel.Children.Add(new TextBlock
            {
                Text       = title,
                FontWeight = FontWeights.Bold,
                FontSize   = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(220, 170, 70)),
                Margin     = new Thickness(0, 0, 0, 4)
            });
        }
        panel.Children.Add(new TextBlock
        {
            Text         = desc,
            FontSize     = 12,
            Foreground   = new SolidColorBrush(Color.FromRgb(230, 230, 225)),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth     = 340
        });

        return new Border
        {
            Background      = new SolidColorBrush(Color.FromRgb(32, 32, 38)),
            BorderBrush     = new SolidColorBrush(Color.FromRgb(100, 100, 115)),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(7),
            Child           = panel,
            Effect          = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color       = Colors.Black,
                Opacity     = 0.55,
                BlurRadius  = 10,
                ShadowDepth = 4
            }
        };
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
        if (DrawCanvas == null) return;
        EnsureParsed();
        DrawCanvas.Children.Clear();
        WatermarkCanvas.Children.Clear();
        DrawIconWatermark(new Rect(0, 0, WatermarkCanvas.ActualWidth, WatermarkCanvas.ActualHeight));
        DrawWorkpieces();
        DrawGCodeTopView();
        DrawGCodeSideView();
        if (_rasterEnabled)
            DrawRaster();
#if false
        if (CbPfadAnzeigen?.IsChecked == true)
            DrawPfadFräsen();
#endif
        DrawGCodeHighlight();   // Hover/Caret-Markierung (über allem)
    }

    private void DrawWorkpieces()
    {
        double cw = DrawCanvas.ActualWidth;
        double ch = DrawCanvas.ActualHeight;
        if (cw <= 0 || ch <= 0) return;

        double wx = WorkX, wy = WorkY, wz = WorkZ;
        if (wx <= 0 || wy <= 0 || wz <= 0) return;

        double minGap = 40;
        double scale  = Math.Min((cw * 0.82) / wx, (ch * 0.82) / (wy + wz));
        scale = Math.Min(scale, 1.0);
        double w  = wx * scale;
        double h1 = wy * scale;
        double h2 = wz * scale;

        double x0  = (cw - w) / 2;
        double gap = Math.Max((ch - h1 - h2) / 3, minGap * 0.5);

        _topRect    = new Rect(x0, gap,              w, h1);
        _bottomRect = new Rect(x0, gap * 2 + h1,     w, h2);

        DrawCanvas.Children.Add(MakeWoodRect(_topRect));
        DrawCanvas.Children.Add(MakeWoodRect(_bottomRect));
    }

    private void DrawIconWatermark(Rect target)
    {
        if (target.IsEmpty || target.Width < 20 || target.Height < 20) return;

        const double SVG = 256.0;
        double scX = target.Width  / SVG;
        double scY = target.Height / SVG;
        double S(double v) => v * Math.Min(scX, scY);
        double X(double x) => target.Left + x * scX;
        double Y(double y) => target.Top  + y * scY;
        Point  P(double x, double y) => new(X(x), Y(y));

        var c = Brushes.Black;
        var wm = new Canvas { IsHitTestVisible = false, Opacity = 0.06 };

        void Rect(double x, double y, double w, double h, double rx, bool filled = true)
        {
            var r = new System.Windows.Shapes.Rectangle {
                Width=w*scX, Height=h*scY, RadiusX=S(rx), RadiusY=S(rx),
                Fill=filled ? c : Brushes.Transparent, Stroke=c, StrokeThickness=S(filled ? 0 : 1.5) };
            Canvas.SetLeft(r, X(x)); Canvas.SetTop(r, Y(y));
            wm.Children.Add(r);
        }
        void Poly(params (double x, double y)[] pts) =>
            wm.Children.Add(new System.Windows.Shapes.Polygon {
                Fill=c, Points=new PointCollection(pts.Select(p => P(p.x, p.y))) });
        void Line(double x1, double y1, double x2, double y2, double w, bool dash=false)
        {
            var ln = new System.Windows.Shapes.Line {
                X1=X(x1), Y1=Y(y1), X2=X(x2), Y2=Y(y2),
                Stroke=c, StrokeThickness=S(w),
                StrokeStartLineCap=PenLineCap.Round, StrokeEndLineCap=PenLineCap.Round };
            if (dash) ln.StrokeDashArray = new System.Windows.Media.DoubleCollection {4,3};
            wm.Children.Add(ln);
        }
        void Path(string d, double w)
        {
            var el = new System.Windows.Shapes.Path {
                Data=System.Windows.Media.Geometry.Parse(d),
                Stroke=c, StrokeThickness=S(w), Fill=Brushes.Transparent,
                RenderTransform=new System.Windows.Media.ScaleTransform(scX, scY) };
            Canvas.SetLeft(el, target.Left); Canvas.SetTop(el, target.Top);
            wm.Children.Add(el);
        }
        void Dot(double cx, double cy, double r)
        {
            var e = new System.Windows.Shapes.Ellipse { Width=r*2*scX, Height=r*2*scY, Fill=c };
            Canvas.SetLeft(e, X(cx)-r*scX); Canvas.SetTop(e, Y(cy)-r*scY);
            wm.Children.Add(e);
        }
        void Text(double x, double y, string t, double sz)
        {
            var tb = new TextBlock { Text=t, FontSize=S(sz), Foreground=c,
                FontFamily=new System.Windows.Media.FontFamily("Consolas"), FontWeight=FontWeights.Bold };
            Canvas.SetLeft(tb, X(x)); Canvas.SetTop(tb, Y(y)-S(sz));
            wm.Children.Add(tb);
        }

        // ── Fräsbahnpfeil (Draufsicht, oben) ────────────────────────────────
        Line(24,18, 232,18, 1.5, dash:true);
        Poly((225,18),(215,13),(215,23));            // Pfeilspitze rechts
        Text(16, 22, "G01", 13);

        // ── Werkzeughalter / Flansch ─────────────────────────────────────────
        Rect(90, 28, 76, 34, 8);                    // Spindelgehäuse
        // Spannzange: trapezförmig
        Poly((90,62),(100,62),(116,82),(140,82),(156,62),(166,62),(162,62),(148,86),(108,86),(94,62));

        // ── Schaft (Zylinder mit Spiralschneiden) ────────────────────────────
        Rect(110, 86, 36, 90, 2);
        // linke Spiralschneide
        Path("M111,88 C105,100 118,108 111,120 C104,132 118,140 111,152 C104,162 112,170 111,176", 2);
        // rechte Spiralschneide
        Path("M145,88 C151,100 138,108 145,120 C152,132 138,140 145,152 C152,162 144,170 145,176", 2);

        // ── Schneidspitze (flacher Fräser) ───────────────────────────────────
        Rect(110, 176, 36, 4, 0);

        // ── Holzoberfläche ───────────────────────────────────────────────────
        Line(24, 184, 232, 184, 3);

        // ── Holzkörper ───────────────────────────────────────────────────────
        Rect(24, 184, 208, 60, 3, false);

        // ── Holzmaserung (wellige Linien) ────────────────────────────────────
        Path("M24,196 Q56,191 88,196 Q120,201 152,196 Q184,191 232,196", 1);
        Path("M24,210 Q56,205 88,210 Q120,215 152,210 Q184,205 232,210", 1);
        Path("M24,224 Q56,219 88,224 Q120,229 152,224 Q184,219 232,224", 1);
        Path("M24,238 Q56,233 88,238 Q120,243 152,238 Q184,233 232,238", 1);

        // ── Gefräste Nut (Querschnitt) ───────────────────────────────────────
        Line(110, 184, 110, 200, 2);     // linke Nutwand
        Line(146, 184, 146, 200, 2);     // rechte Nutwand
        Line(110, 200, 146, 200, 2);     // Nutboden

        // ── Späne / Holzfasern am Kontaktpunkt ───────────────────────────────
        Line(104, 182, 96,  170, 1.8);
        Line(100, 183, 91,  173, 1.4);
        Line( 95, 182, 87,  175, 1.2);
        Line(152, 182, 160, 170, 1.8);
        Line(156, 183, 165, 173, 1.4);
        Line(161, 182, 169, 175, 1.2);
        Dot(88, 168, 3);
        Dot(82, 174, 2.2);
        Dot(168, 168, 3);
        Dot(174, 174, 2.2);

        // ── X/Y/Z-Achsenbeschriftungen ───────────────────────────────────────
        Line(24, 250, 200, 250, 2);
        Poly((200,250),(190,245),(190,255));         // X-Pfeil
        Text(204, 255, "X", 15);
        Line(18, 184, 18, 50, 2);
        Poly((18,48),(13,58),(23,58));               // Z-Pfeil
        Text(21, 58, "Z", 15);

        WatermarkCanvas.Children.Add(wm);
    }

    private void AddWood3DBlock(Rect r, Point vp, double depthFactor)
    {
        var tl = new Point(r.Left,  r.Top);
        var tr = new Point(r.Right, r.Top);
        var br = new Point(r.Right, r.Bottom);
        var bl = new Point(r.Left,  r.Bottom);

        // Perspektivische Projektion: Punkte laufen auf Fluchtpunkt zu
        Point ToVP(Point p) => new(
            p.X + (vp.X - p.X) * depthFactor,
            p.Y + (vp.Y - p.Y) * depthFactor);

        var tlo = ToVP(tl);
        var tro = ToVP(tr);
        var bro = ToVP(br);
        var blo = ToVP(bl);

        var border = new SolidColorBrush(Color.FromRgb(0xD4, 0xC8, 0xA8));

        void AddFace(PointCollection pts, byte shadowAlpha)
        {
            DrawCanvas.Children.Add(new Polygon { Points = pts, Fill = GetWoodBrush() });
            DrawCanvas.Children.Add(new Polygon
            {
                Points           = pts,
                Fill             = new SolidColorBrush(Color.FromArgb(shadowAlpha, 0, 0, 0)),
                Stroke           = border,
                StrokeThickness  = 0.1,
                IsHitTestVisible = false,
            });
        }

        // Obere Fläche (VP liegt oberhalb → sichtbar)
        if (vp.Y < r.Top)
            AddFace(new PointCollection { tl, tr, tro, tlo }, 70);

        // Rechte Seitenfläche (VP liegt rechts → sichtbar)
        if (vp.X > r.Right)
            AddFace(new PointCollection { tr, tro, bro, br }, 130);

        // Vorderfläche – volle Textur
        DrawCanvas.Children.Add(MakeWoodRect(r));
    }

    private static ImageBrush? _woodBrush;
    private static ImageBrush GetWoodBrush()
    {
        if (_woodBrush is null)
        {
            var imgPath = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "maple.png");
            var bmp = System.IO.File.Exists(imgPath)
                ? new System.Windows.Media.Imaging.BitmapImage(new Uri(imgPath))
                : (System.Windows.Media.Imaging.BitmapSource)CreateMapleTexture(512, 512);
            _woodBrush = new ImageBrush(bmp)
            {
                Stretch       = Stretch.UniformToFill,
                TileMode      = TileMode.Tile,
                ViewportUnits = BrushMappingMode.RelativeToBoundingBox,
                Viewport      = new Rect(0, 0, 1, 1),
            };
        }
        return _woodBrush;
    }

    private static System.Windows.Media.Imaging.BitmapSource CreateMapleTexture(int w, int h)
    {
        var rng    = new Random(13);
        var pixels = new byte[w * h * 4];

        // Mehrere unabhängige Rausch-Gitter für fraktales Warp
        const int gw = 64, gh = 64;
        var grid1 = new double[gw * gh];
        var grid2 = new double[gw * gh];
        var grid3 = new double[gw * gh];
        for (int i = 0; i < gw * gh; i++)
        {
            grid1[i] = rng.NextDouble();
            grid2[i] = rng.NextDouble();
            grid3[i] = rng.NextDouble();
        }

        double Bilinear(double[] g, double gx, double gy)
        {
            gx = ((gx % gw) + gw) % gw;
            gy = ((gy % gh) + gh) % gh;
            int x0 = (int)gx, y0 = (int)gy;
            int x1 = (x0 + 1) % gw, y1 = (y0 + 1) % gh;
            double fx = gx - x0, fy = gy - y0;
            return g[y0 * gw + x0] * (1 - fx) * (1 - fy)
                 + g[y0 * gw + x1] * fx        * (1 - fy)
                 + g[y1 * gw + x0] * (1 - fx)  * fy
                 + g[y1 * gw + x1] * fx         * fy;
        }

        // Fraktales Rauschen: 4 Oktaven aufaddiert
        double Fractal(double[] g, double gx, double gy)
        {
            double v = 0, amp = 0.5, freq = 1, sum = 0;
            for (int o = 0; o < 4; o++)
            {
                v   += Bilinear(g, gx * freq, gy * freq) * amp;
                sum += amp;
                amp  *= 0.55;
                freq *= 2.1;
            }
            return v / sum;
        }

        const double twoPi = 2 * Math.PI;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                double gx = x * gw / (double)w;
                double gy = y * gh / (double)h;

                // Fraktales Warp in y-Richtung (organische Jahresring-Biegung)
                double warpY = (Fractal(grid1, gx, gy) - 0.5) * 55.0;
                // Leichtes Warp in x für minimale Neigung der Maserung
                double warpX = (Fractal(grid2, gx + 10, gy + 10) - 0.5) * 8.0;

                double yW = y + warpY;
                double xW = x + warpX;

                // Jahresringe: Hauptperiode + Feinstruktur
                double ring = Math.Sin(yW * twoPi / 70.0 + xW * 0.003)
                            + 0.25 * Math.Sin(yW * twoPi / 22.0 + xW * 0.006);

                // Schmale dunkle Linien, breite helle Flächen (typisch Ahorn)
                double t = (Math.Sin(ring * 1.8) + 1.0) / 2.0;
                t = Math.Pow(t, 2.2);
                t = Math.Clamp(t, 0.0, 1.0);

                // Oberflächenvariation (Maserglanz, Poren)
                double surface = (Fractal(grid3, gx * 0.7, gy * 0.7) - 0.5) * 0.12;

                double tt = Math.Clamp(t + surface, 0.0, 1.0);

                // Ahorn-Palette: sehr hell #F6F0DF → warmes Honigbraun #C8A050
                byte r = (byte)(246 - tt * 30);
                byte g = (byte)(240 - tt * 64);
                byte b = (byte)(223 - tt * 143);

                int pi = (y * w + x) * 4;
                pixels[pi]     = b;
                pixels[pi + 1] = g;
                pixels[pi + 2] = r;
                pixels[pi + 3] = 255;
            }
        }

        var bmp = new System.Windows.Media.Imaging.WriteableBitmap(w, h, 96, 96,
            System.Windows.Media.PixelFormats.Bgra32, null);
        bmp.WritePixels(new Int32Rect(0, 0, w, h), pixels, w * 4, 0);
        bmp.Freeze();
        return bmp;
    }

    private static UIElement MakeWoodRect(Rect r)
    {
        var rect = new Rectangle
        {
            Width           = r.Width,
            Height          = r.Height,
            Fill            = GetWoodBrush(),
            Stroke          = new SolidColorBrush(Color.FromRgb(0xE0, 0xD4, 0xB8)),
            StrokeThickness = 0.1,
        };
        Canvas.SetLeft(rect, r.Left);
        Canvas.SetTop(rect,  r.Top);
        return rect;
    }

    /// <summary>
    /// Prozedurale Holztextur via Fraktalem Rauschen + Jahresring-Sinus.
    /// </summary>

    // ── Draufsicht G-Code ────────────────────────────────────────

    private void DrawGCodeTopView()
    {
        var moves = _cachedTopMoves;
        if (_topRect.IsEmpty) return;

        double wx = WorkX, wy = WorkY;
        if (wx <= 0 || wy <= 0) return;

        double scale = Math.Min(_topRect.Width / wx, _topRect.Height / wy);
        Point MmToPx(double x, double y) =>
            new(_topRect.Left + x * scale, _topRect.Bottom - y * scale);

        if (moves.Count > 0)
        {

        var rapidDash = new System.Windows.Media.DoubleCollection { 5, 3 };
        rapidDash.Freeze();

        // Aktive Hover/Caret-Zeile (Maus hat Vorrang vor Cursor)
        int activeLine = _mouseHoverLine >= 1 ? _mouseHoverLine : _highlightGCodeLine;

        // ── Pass 1: Alle normalen Moves ──
        // Simulations-Modus: Schnittbreite als Strichstärke (nach Werkzeugdurchmesser / -winkel / Tiefe)
        // Normal-Modus:      feste 2 px Linienstärke

        // Rapid-Moves immer als dünne gestrichelte Linie (beide Modi)
        var rapidGeo = new StreamGeometry();
        using (var rap = rapidGeo.Open())
        {
            Point? last = null;
            foreach (var m in moves)
            {
                bool skip = m.LineNumber == _selectedGCodeLine || m.LineNumber == activeLine ||
                            (_selectionSource == 1 && _selectedGCodeLine >= 1 && m.LineNumber > 0 && m.Type != MoveType.Rapid && Math.Abs(m.LineNumber - _selectedGCodeLine) <= 3);
                var cur = m.Type == MoveType.Rapid ? MmToPx(m.X, m.Y)
                        : (m.Type is MoveType.ArcCW or MoveType.ArcCCW ? MmToPx(m.Xe, m.Ye)
                        : MmToPx(m.X, m.Y));
                if (m.Type == MoveType.Rapid && last.HasValue && !skip)
                {
                    rap.BeginFigure(last.Value, false, false);
                    rap.LineTo(cur, true, false);
                }
                last = cur;
            }
        }
        rapidGeo.Freeze();
        double lineThick = 1.5 / _zoom;
        DrawCanvas.Children.Add(new System.Windows.Shapes.Path { Data=rapidGeo, Stroke=new SolidColorBrush(Color.FromRgb(160,160,160)), StrokeThickness=lineThick, StrokeDashArray=rapidDash, IsHitTestVisible=false });

        if (_showFraesbreite)
        {
            double borderThick = 2.0 / _zoom;
            var borderBrush = new SolidColorBrush(Color.FromArgb(130, 50, 50, 50));
            borderBrush.Freeze();
            var fillBrush = new SolidColorBrush(Color.FromArgb(35, 150, 150, 150));
            fillBrush.Freeze();
            Point? last2 = null;

            foreach (var m in moves)
            {
                bool skip = m.LineNumber == _selectedGCodeLine || m.LineNumber == activeLine ||
                            (_selectionSource == 1 && _selectedGCodeLine >= 1 && m.LineNumber > 0 && m.Type != MoveType.Rapid && Math.Abs(m.LineNumber - _selectedGCodeLine) <= 3);
                var endPt = m.Type is MoveType.ArcCW or MoveType.ArcCCW ? MmToPx(m.Xe, m.Ye) : MmToPx(m.X, m.Y);
                if (m.Type == MoveType.Rapid) { last2 = endPt; continue; }
                if (skip || m.ToolWidthMm <= 0) { last2 = endPt; continue; }

                var startPt = last2 ?? MmToPx(m.X, m.Y);
                double toolPx = Math.Max(lineThick * 3, m.ToolWidthMm * scale);

                var geo = new StreamGeometry();
                var ctx = geo.Open();
                ctx.BeginFigure(startPt, false, false);
                if (m.Type == MoveType.Line)
                {
                    ctx.LineTo(endPt, true, false);
                }
                else
                {
                    double ccx = m.X + m.I, ccy = m.Y + m.J;
                    double ar = Math.Sqrt((m.X - ccx) * (m.X - ccx) + (m.Y - ccy) * (m.Y - ccy));
                    if (ar > 0)
                    {
                        bool cw = m.Type == MoveType.ArcCW;
                        var sweep = cw ? SweepDirection.Clockwise : SweepDirection.Counterclockwise;
                        double arPx = ar * scale;
                        var sp3 = MmToPx(m.X, m.Y);
                        var ep3 = MmToPx(m.Xe, m.Ye);
                        bool full = Math.Abs(m.Xe - m.X) < 1e-6 && Math.Abs(m.Ye - m.Y) < 1e-6;
                        if (full)
                        {
                            double sA = Math.Atan2(m.Y - ccy, m.X - ccx);
                            var midPx = MmToPx(ccx + ar * Math.Cos(sA + Math.PI), ccy + ar * Math.Sin(sA + Math.PI));
                            ctx.ArcTo(midPx, new System.Windows.Size(arPx, arPx), 0, false, sweep, true, false);
                            ctx.ArcTo(sp3,   new System.Windows.Size(arPx, arPx), 0, false, sweep, true, false);
                        }
                        else
                        {
                            double sA = Math.Atan2(m.Y - ccy, m.X - ccx), eA = Math.Atan2(m.Ye - ccy, m.Xe - ccx);
                            if (cw && eA > sA) eA -= 2 * Math.PI;
                            if (!cw && eA < sA) eA += 2 * Math.PI;
                            ctx.ArcTo(ep3, new System.Windows.Size(arPx, arPx), 0, Math.Abs(eA - sA) > Math.PI, sweep, true, false);
                        }
                    }
                }
                ((IDisposable)ctx).Dispose();
                geo.Freeze();

                // Border zuerst (breiter), dann Fill darüber — so ist der Fill jeder Bahn sichtbar
                DrawCanvas.Children.Add(new System.Windows.Shapes.Path
                {
                    Data = geo, Stroke = borderBrush, StrokeThickness = toolPx + borderThick,
                    StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false
                });
                DrawCanvas.Children.Add(new System.Windows.Shapes.Path
                {
                    Data = geo, Stroke = fillBrush, StrokeThickness = toolPx,
                    StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false
                });

                last2 = endPt;
            }

            // Rote Mittellinie on top (wie Normal-Modus)
            var cutGeoSim = new StreamGeometry();
            using (var cut = cutGeoSim.Open())
            {
                Point? lastS = null;
                foreach (var m in moves)
                {
                    bool skip = m.LineNumber == _selectedGCodeLine || m.LineNumber == activeLine ||
                            (_selectionSource == 1 && _selectedGCodeLine >= 1 && m.LineNumber > 0 && m.Type != MoveType.Rapid && Math.Abs(m.LineNumber - _selectedGCodeLine) <= 3);
                    if (m.Type is MoveType.Rapid or MoveType.Line)
                    {
                        var cur = MmToPx(m.X, m.Y);
                        if (m.Type == MoveType.Line && lastS.HasValue && !skip)
                        { cut.BeginFigure(lastS.Value, false, false); cut.LineTo(cur, true, false); }
                        lastS = cur;
                    }
                    else
                    {
                        double ccx=m.X+m.I, ccy=m.Y+m.J, ar=Math.Sqrt((m.X-ccx)*(m.X-ccx)+(m.Y-ccy)*(m.Y-ccy));
                        if (ar > 0 && !skip)
                        {
                            bool cw = m.Type == MoveType.ArcCW;
                            var sweep = cw ? SweepDirection.Clockwise : SweepDirection.Counterclockwise;
                            double arPx = ar*scale;
                            var sp = MmToPx(m.X, m.Y); var ep = MmToPx(m.Xe, m.Ye);
                            bool full = Math.Abs(m.Xe-m.X)<1e-6 && Math.Abs(m.Ye-m.Y)<1e-6;
                            if (full)
                            {
                                double sA=Math.Atan2(m.Y-ccy,m.X-ccx);
                                var mp=MmToPx(ccx+ar*Math.Cos(sA+Math.PI),ccy+ar*Math.Sin(sA+Math.PI));
                                cut.BeginFigure(sp,false,false); cut.ArcTo(mp,new System.Windows.Size(arPx,arPx),0,false,sweep,true,false);
                                cut.ArcTo(sp,new System.Windows.Size(arPx,arPx),0,false,sweep,true,false);
                            }
                            else
                            {
                                double sA=Math.Atan2(m.Y-ccy,m.X-ccx),eA=Math.Atan2(m.Ye-ccy,m.Xe-ccx);
                                if(cw&&eA>sA)eA-=2*Math.PI; if(!cw&&eA<sA)eA+=2*Math.PI;
                                cut.BeginFigure(sp,false,false); cut.ArcTo(ep,new System.Windows.Size(arPx,arPx),0,Math.Abs(eA-sA)>Math.PI,sweep,true,false);
                            }
                        }
                        lastS = MmToPx(m.Xe, m.Ye);
                    }
                }
            }
            cutGeoSim.Freeze();
            DrawCanvas.Children.Add(new System.Windows.Shapes.Path { Data=cutGeoSim, Stroke=new SolidColorBrush(Color.FromRgb(200,30,30)), StrokeThickness=lineThick, IsHitTestVisible=false });
        }
        else
        {
            var cutGeo = new StreamGeometry();
            using (var cut = cutGeo.Open())
            {
                Point? last = null;
                foreach (var m in moves)
                {
                    bool skip = m.LineNumber == _selectedGCodeLine || m.LineNumber == activeLine ||
                            (_selectionSource == 1 && _selectedGCodeLine >= 1 && m.LineNumber > 0 && m.Type != MoveType.Rapid && Math.Abs(m.LineNumber - _selectedGCodeLine) <= 3);
                    if (m.Type is MoveType.Rapid or MoveType.Line)
                    {
                        var cur = MmToPx(m.X, m.Y);
                        if (m.Type == MoveType.Line && last.HasValue && !skip)
                        {
                            cut.BeginFigure(last.Value, false, false);
                            cut.LineTo(cur, true, false);
                        }
                        last = cur;
                    }
                    else
                    {
                        double ccx = m.X+m.I, ccy = m.Y+m.J;
                        double ar  = Math.Sqrt((m.X-ccx)*(m.X-ccx)+(m.Y-ccy)*(m.Y-ccy));
                        if (ar > 0 && !skip)
                        {
                            bool cw   = m.Type == MoveType.ArcCW;
                            var sweep = cw ? SweepDirection.Clockwise : SweepDirection.Counterclockwise;
                            double arPx   = ar * scale;
                            var startPx   = MmToPx(m.X,  m.Y);
                            var endPx     = MmToPx(m.Xe, m.Ye);
                            bool full = Math.Abs(m.Xe-m.X)<1e-6 && Math.Abs(m.Ye-m.Y)<1e-6;
                            if (full)
                            {
                                double sA = Math.Atan2(m.Y-ccy, m.X-ccx);
                                var midPx = MmToPx(ccx + ar*Math.Cos(sA+Math.PI), ccy + ar*Math.Sin(sA+Math.PI));
                                cut.BeginFigure(startPx, false, false);
                                cut.ArcTo(midPx,   new System.Windows.Size(arPx,arPx), 0, false, sweep, true, false);
                                cut.ArcTo(startPx, new System.Windows.Size(arPx,arPx), 0, false, sweep, true, false);
                            }
                            else
                            {
                                double sA=Math.Atan2(m.Y-ccy,m.X-ccx), eA=Math.Atan2(m.Ye-ccy,m.Xe-ccx);
                                if (cw&&eA>sA) eA-=2*Math.PI; if (!cw&&eA<sA) eA+=2*Math.PI;
                                bool large = Math.Abs(eA-sA) > Math.PI;
                                cut.BeginFigure(startPx, false, false);
                                cut.ArcTo(endPx, new System.Windows.Size(arPx,arPx), 0, large, sweep, true, false);
                            }
                        }
                        last = MmToPx(m.Xe, m.Ye);
                    }
                }
            }
            cutGeo.Freeze();
            DrawCanvas.Children.Add(new System.Windows.Shapes.Path { Data=cutGeo, Stroke=new SolidColorBrush(Color.FromRgb(200,30,30)), StrokeThickness=lineThick, IsHitTestVisible=false });
        }

        // ── Pass 2: Aktiver und selektierter Move farbig als Einzel-Pfad ──
        void DrawColoredMove(Move m, double fromX, double fromY, Brush stroke, double thick, bool dashed)
        {
            System.Windows.Shapes.Path? el = null;
            if (m.Type is MoveType.Rapid or MoveType.Line)
            {
                var geo = new StreamGeometry();
                using (var ctx = geo.Open()) { ctx.BeginFigure(MmToPx(fromX,fromY),false,false); ctx.LineTo(MmToPx(m.X,m.Y),true,false); }
                geo.Freeze();
                el = new System.Windows.Shapes.Path { Data=geo, Stroke=stroke, StrokeThickness=thick, StrokeDashArray=dashed?rapidDash:null };
            }
            else
            {
                double ccx=m.X+m.I, ccy=m.Y+m.J, ar=Math.Sqrt((m.X-ccx)*(m.X-ccx)+(m.Y-ccy)*(m.Y-ccy));
                if (ar>0)
                {
                    bool cw = m.Type == MoveType.ArcCW;
                    var sweep = cw ? SweepDirection.Clockwise : SweepDirection.Counterclockwise;
                    double arPx = ar * scale;
                    var startPx = MmToPx(m.X, m.Y);
                    var endPx   = MmToPx(m.Xe, m.Ye);
                    bool full = Math.Abs(m.Xe-m.X)<1e-6&&Math.Abs(m.Ye-m.Y)<1e-6;
                    // StreamGeometry statt PathGeometry → identisches Rendering wie Pass 1
                    var geo2 = new StreamGeometry();
                    using (var ctx2 = geo2.Open())
                    {
                        if (full)
                        {
                            double sA = Math.Atan2(m.Y-ccy, m.X-ccx);
                            var midPx = MmToPx(ccx + ar*Math.Cos(sA+Math.PI), ccy + ar*Math.Sin(sA+Math.PI));
                            ctx2.BeginFigure(startPx, false, false);
                            ctx2.ArcTo(midPx,   new System.Windows.Size(arPx,arPx), 0, false, sweep, true, false);
                            ctx2.ArcTo(startPx, new System.Windows.Size(arPx,arPx), 0, false, sweep, true, false);
                        }
                        else
                        {
                            double sA=Math.Atan2(m.Y-ccy,m.X-ccx), eA=Math.Atan2(m.Ye-ccy,m.Xe-ccx);
                            if (cw&&eA>sA) eA-=2*Math.PI; if (!cw&&eA<sA) eA+=2*Math.PI;
                            bool large = Math.Abs(eA-sA) > Math.PI;
                            ctx2.BeginFigure(startPx, false, false);
                            ctx2.ArcTo(endPx, new System.Windows.Size(arPx,arPx), 0, large, sweep, true, false);
                        }
                    }
                    geo2.Freeze();
                    el = new System.Windows.Shapes.Path { Data=geo2, Stroke=stroke, StrokeThickness=thick };
                }
            }
            if (el!=null) DrawCanvas.Children.Add(el);
        }

        // ── Pass 3: Transparente Klickflächen über alle Moves ──
        // Klick-Breite = 14 Bildschirmpixel unabhängig vom Zoom
        double hitThick = Math.Max(2.0, 14.0 / _zoom);
        double lx = 0, ly = 0;
        foreach (var m in moves)
        {
            double fromX = lx, fromY = ly;
            bool sel    = m.LineNumber == _selectedGCodeLine;
            bool active = !sel && m.LineNumber == activeLine;
            bool nearby = !sel && !active && _selectionSource == 1 && _selectedGCodeLine >= 1 && m.LineNumber > 0 &&
                          Math.Abs(m.LineNumber - _selectedGCodeLine) <= 3 && m.Type != MoveType.Rapid;

            if (sel)
                DrawColoredMove(m, fromX, fromY, new SolidColorBrush(Color.FromRgb(255, 215,  0)), 2.0 / _zoom, m.Type==MoveType.Rapid);
            else if (active)
                DrawColoredMove(m, fromX, fromY, new SolidColorBrush(Color.FromRgb(255, 235, 80)), 2.0 / _zoom, m.Type==MoveType.Rapid);
            else if (nearby)
                DrawColoredMove(m, fromX, fromY, new SolidColorBrush(Color.FromArgb(140, 255, 215, 0)), 1.5 / _zoom, false);

            // Klickfläche
            System.Windows.Shapes.Path? hitEl = null;
            if (m.Type is MoveType.Rapid or MoveType.Line)
            {
                var geo = new StreamGeometry();
                using (var ctx = geo.Open()) { ctx.BeginFigure(MmToPx(fromX,fromY),false,false); ctx.LineTo(MmToPx(m.X,m.Y),true,false); }
                geo.Freeze();
                hitEl = new System.Windows.Shapes.Path { Data=geo, Stroke=Brushes.Transparent, StrokeThickness=hitThick, Cursor=Cursors.Hand, Tag=m.LineNumber };
                lx=m.X; ly=m.Y;
            }
            else
            {
                double ccx=m.X+m.I, ccy=m.Y+m.J, ar=Math.Sqrt((m.X-ccx)*(m.X-ccx)+(m.Y-ccy)*(m.Y-ccy));
                if (ar>0)
                {
                    bool cw = m.Type == MoveType.ArcCW;
                    var sweep = cw ? SweepDirection.Counterclockwise : SweepDirection.Clockwise;
                    double arPx = ar * scale;
                    var startPx = MmToPx(m.X, m.Y);
                    var endPx   = MmToPx(m.Xe, m.Ye);
                    bool full = Math.Abs(m.Xe-m.X)<1e-6&&Math.Abs(m.Ye-m.Y)<1e-6;
                    var pg = new PathGeometry();
                    if (full)
                    {
                        double sA = Math.Atan2(m.Y-ccy, m.X-ccx);
                        var midPx = MmToPx(ccx + ar*Math.Cos(sA+Math.PI), ccy + ar*Math.Sin(sA+Math.PI));
                        var fig = new PathFigure { StartPoint=startPx, IsFilled=false };
                        fig.Segments.Add(new ArcSegment(midPx,   new System.Windows.Size(arPx,arPx), 0, false, sweep, true));
                        fig.Segments.Add(new ArcSegment(startPx, new System.Windows.Size(arPx,arPx), 0, false, sweep, true));
                        pg.Figures.Add(fig);
                    }
                    else
                    {
                        double sA=Math.Atan2(m.Y-ccy,m.X-ccx), eA=Math.Atan2(m.Ye-ccy,m.Xe-ccx);
                        if (cw&&eA>sA) eA-=2*Math.PI; if (!cw&&eA<sA) eA+=2*Math.PI;
                        bool large = Math.Abs(eA-sA) > Math.PI;
                        var fig = new PathFigure { StartPoint=startPx, IsFilled=false };
                        fig.Segments.Add(new ArcSegment(endPx, new System.Windows.Size(arPx,arPx), 0, large, sweep, true));
                        pg.Figures.Add(fig);
                    }
                    hitEl = new System.Windows.Shapes.Path { Data=pg, Stroke=Brushes.Transparent, StrokeThickness=hitThick, Cursor=Cursors.Hand, Tag=m.LineNumber };
                }
                lx=m.Xe; ly=m.Ye;
            }
            if (hitEl!=null) { hitEl.MouseLeftButtonDown+=OnTopViewFormClick; DrawCanvas.Children.Add(hitEl); }
        }

        foreach (var hole in _cachedDrillPoints)
        {
            var center = MmToPx(hole.X, hole.Y);
            bool selHole = hole.LineNumber == _selectedGCodeLine;
            // Bohrpunkt: zoom-invariant (6px Bildschirmgrösse)
            double dotR = 3.0 / _zoom;
            var circle = new Ellipse
            {
                Width           = dotR * 2, Height = dotR * 2,
                Fill            = selHole ? new SolidColorBrush(Color.FromRgb(255,215,0))
                                          : new SolidColorBrush(Color.FromRgb(0,140,255)),
                Stroke          = Brushes.White,
                StrokeThickness = 1.0 / _zoom,
                Cursor          = Cursors.Hand,
                Tag             = hole.LineNumber
            };
            circle.MouseLeftButtonDown += OnTopViewFormClick;
            Canvas.SetLeft(circle, center.X - dotR);
            Canvas.SetTop (circle, center.Y - dotR);
            DrawCanvas.Children.Add(circle);
        }

        } // end if (moves.Count > 0)

        // ── Gravieren: Buchstaben-Konturen grau anzeigen (Tasche + V-Carve) ─
        foreach (var entry in _history)
        {
            if (entry.Params is not GraviereParams gp || (!gp.IsTasche && !gp.IsVCarve && !gp.IsVCarveRaster)) continue;
            // Preview: aktuellen Eingabetext live anzeigen ohne G-Code neu zu berechnen
            var displayGp = (entry == HistoryList.SelectedItem && _previewGravParams != null)
                            ? _previewGravParams : gp;
            var tctx = GCodeGenerator.BuildTextGeo(displayGp, wx, wy);
            if (tctx.Flat.Bounds.IsEmpty) continue;

            double ts = tctx.Scale, tmH = tctx.MultiH;
            Point ToPx(double fx, double fy) =>
                MmToPx(tctx.Ox + fx * ts, tctx.Oy + tctx.YOffset + (tmH - fy) * ts);

            var outGeo = new StreamGeometry();
            using (var og = outGeo.Open())
            {
                foreach (var fig in tctx.Flat.Figures)
                {
                    if (!fig.IsClosed) continue;
                    var sp = ToPx(fig.StartPoint.X, fig.StartPoint.Y);
                    og.BeginFigure(sp, false, false);
                    foreach (var seg in fig.Segments)
                    {
                        var segPts = seg switch
                        {
                            System.Windows.Media.PolyLineSegment pls =>
                                pls.Points.Select(q => ToPx(q.X, q.Y)).ToList(),
                            System.Windows.Media.LineSegment ls =>
                                new List<System.Windows.Point> { ToPx(ls.Point.X, ls.Point.Y) },
                            _ => new List<System.Windows.Point>()
                        };
                        if (segPts.Count > 0) og.PolyLineTo(segPts, true, false);
                    }
                    og.LineTo(sp, true, false);
                }
            }
            DrawCanvas.Children.Add(new System.Windows.Shapes.Path
            {
                Data            = outGeo,
                Stroke          = new SolidColorBrush(Color.FromArgb(200, 80, 80, 80)),
                StrokeThickness = 1.0 / _zoom,
                IsHitTestVisible = false
            });
        }

        // ── V-Carve: einbeschriebene Kreise (blau, nur bei aktivierter Visualisierung) ──
        bool showVCarve = MnuVCarveVisualisieren.IsChecked == true;
        var allVCarveCenters = new List<GCodeGenerator.VCarveCircle>();
        foreach (var entry in _history)
        {
            if (entry.Params is not GraviereParams gp || !gp.IsVCarve) continue;
            if (!showVCarve) continue;

            // Cache: nur einmal pro Parameterset berechnen
            if (!_vCarveCache.TryGetValue(gp, out var circles))
            {
                double step = Math.Clamp(gp.FontSizeMm / 200.0, 0.025, 0.1);
                circles = GCodeGenerator.ResampleVCarveCircles(
                              GCodeGenerator.ComputeVCarveCircles(gp, wx, wy, step),
                              simplifyMm: gp.VereinfachungMm);
                _vCarveCache[gp] = circles;
            }
            allVCarveCenters.AddRange(circles);

            if (circles.Count == 0) continue;

            // Alle Kreise als eine einzige StreamGeometry (performant)
            var circleGeo = new StreamGeometry();
            using (var sg = circleGeo.Open())
            {
                foreach (var c in circles)
                {
                    double rPx = c.R * scale;
                    if (rPx < 0.01) continue;
                    var ctr = MmToPx(c.X, c.Y);
                    var left  = new System.Windows.Point(ctr.X - rPx, ctr.Y);
                    var right = new System.Windows.Point(ctr.X + rPx, ctr.Y);
                    var sz    = new System.Windows.Size(rPx, rPx);
                    sg.BeginFigure(right, true, true);
                    sg.ArcTo(left,  sz, 0, false, System.Windows.Media.SweepDirection.Clockwise, true, false);
                    sg.ArcTo(right, sz, 0, false, System.Windows.Media.SweepDirection.Clockwise, true, false);
                }
            }
            circleGeo.Freeze();
            DrawCanvas.Children.Add(new System.Windows.Shapes.Path
            {
                Data             = circleGeo,
                Stroke           = new SolidColorBrush(Color.FromRgb(0, 80, 210)),
                StrokeThickness  = 0.8 / _zoom,
                Fill             = System.Windows.Media.Brushes.Transparent,
                IsHitTestVisible = false
            });
        }
        VCarveCenters = allVCarveCenters;

        // ── Gravieren-Textfelder (grau gepunktet) ─────────────────
        foreach (var entry in _history)
        {
            if (entry.Params is not GraviereParams gp) continue;
            double fh = gp.TextHoehe > 0 ? gp.TextHoehe : gp.FontSizeMm;
            if (gp.TextBreite <= 0 || fh <= 0) continue;

            var (ox, oy) = GCodeGenerator.ConvertBezugspunkt(
                gp.Bezugspunkt, gp.XRel, gp.YRel, WorkX, WorkY);

            var tl = MmToPx(ox,                 oy + fh);
            var br = MmToPx(ox + gp.TextBreite, oy);
            double pw = br.X - tl.X;
            double ph = br.Y - tl.Y;
            if (pw < 1 || ph < 1) continue;

            var rect = new System.Windows.Shapes.Rectangle
            {
                Width           = pw,
                Height          = ph,
                Stroke          = Brushes.Gray,
                StrokeThickness = 1.0,
                StrokeDashArray = new DoubleCollection([4, 3]),
                Fill            = Brushes.Transparent
            };
            Canvas.SetLeft(rect, tl.X);
            Canvas.SetTop(rect,  tl.Y);
            DrawCanvas.Children.Add(rect);
        }
    }

    // ── Seitenansicht G-Code ─────────────────────────────────────

    private void DrawGCodeSideView()
    {
        var moves = _cachedSideMoves;
        if (moves.Count == 0 || _bottomRect.IsEmpty) return;

        double wx = WorkX, wz = WorkZ;
        if (wx <= 0 || wz <= 0) return;

        double scale = Math.Min(_bottomRect.Width / wx, _bottomRect.Height / wz);
        Point MmToPx(double x, double z) =>
            new(_bottomRect.Left + x * scale, _bottomRect.Top + (-z) * scale);

        double thick = 1.5 / _zoom;

        // ── Sichtbare Linien ──────────────────────────────────────
        var cutGeo   = new StreamGeometry();
        var rapidGeo = new StreamGeometry();

        using (var cut   = cutGeo.Open())
        using (var rapid = rapidGeo.Open())
        {
            Point? last = null;
            foreach (var m in moves)
            {
                var cur = MmToPx(m.X, m.Z);
                if (last.HasValue)
                {
                    var ctx = m.Cmd == "G0" ? rapid : cut;
                    ctx.BeginFigure(last.Value, false, false);
                    ctx.LineTo(cur, true, false);
                }
                last = cur;
            }
        }

        cutGeo.Freeze();
        rapidGeo.Freeze();

        var rapidDash = new System.Windows.Media.DoubleCollection { 5, 3 };
        rapidDash.Freeze();
        DrawCanvas.Children.Add(new System.Windows.Shapes.Path
        {
            Data = rapidGeo, Stroke = new SolidColorBrush(Color.FromRgb(160, 160, 160)),
            StrokeThickness = thick, StrokeDashArray = rapidDash,
            IsHitTestVisible = false
        });

        int activeLine = _mouseHoverLine >= 1 ? _mouseHoverLine : _highlightGCodeLine;

        // Erst rote Schnittlinien, dann gelbe Hervorhebungen darüber
        DrawCanvas.Children.Add(new System.Windows.Shapes.Path
        {
            Data = cutGeo, Stroke = new SolidColorBrush(Color.FromRgb(200, 30, 30)),
            StrokeThickness = thick, IsHitTestVisible = false
        });

        // Selektierter / aktiver Move hervorheben (on top of red)
        Point? prevPt = null;
        foreach (var m in moves)
        {
            var cur = MmToPx(m.X, m.Z);
            if (prevPt.HasValue && m.LineNumber > 0)
            {
                bool sel    = m.LineNumber == _selectedGCodeLine;
                bool active = !sel && m.LineNumber == activeLine;
                bool nearby = !sel && !active && _selectionSource == 0 && _selectedGCodeLine >= 1 && m.LineNumber > 0 &&
                              Math.Abs(m.LineNumber - _selectedGCodeLine) <= 3 && m.Cmd != "G0";
                if (sel || active || nearby)
                {
                    var hl = new StreamGeometry();
                    using (var c = hl.Open()) { c.BeginFigure(prevPt.Value, false, false); c.LineTo(cur, true, false); }
                    hl.Freeze();
                    var hlColor = sel    ? Color.FromRgb(255, 215,   0)
                                : active ? Color.FromRgb(255, 235,  80)
                                         : Color.FromArgb(200, 255, 215, 0);
                    DrawCanvas.Children.Add(new System.Windows.Shapes.Path
                    {
                        Data            = hl,
                        Stroke          = new SolidColorBrush(hlColor),
                        StrokeThickness = sel ? 2.5 / _zoom : 2.0 / _zoom,
                        IsHitTestVisible = false
                    });
                }
            }
            prevPt = cur;
        }

        // ── Klickflächen ──────────────────────────────────────────
        double hitThick = Math.Max(2.0, 14.0 / _zoom);
        prevPt = null;
        foreach (var m in moves)
        {
            var cur = MmToPx(m.X, m.Z);
            if (prevPt.HasValue && m.LineNumber > 0 && m.Cmd != "G0")
            {
                var geo = new StreamGeometry();
                using (var c = geo.Open()) { c.BeginFigure(prevPt.Value, false, false); c.LineTo(cur, true, false); }
                geo.Freeze();
                var hitEl = new System.Windows.Shapes.Path
                {
                    Data            = geo,
                    Stroke          = Brushes.Transparent,
                    StrokeThickness = hitThick,
                    Cursor          = Cursors.Hand,
                    Tag             = m.LineNumber
                };
                hitEl.MouseLeftButtonDown += OnSideViewFormClick;
                DrawCanvas.Children.Add(hitEl);
            }
            prevPt = cur;
        }
    }

    // ── Pfad Fräsen zeichnen ─────────────────────────────────────

#if false // DrawPfadFräsen + DrawHoverArrows
    private void DrawPfadFräsen()
    {
        if (_topRect.IsEmpty) return;

        double wx = WorkX, wy = WorkY;
        if (wx <= 0 || wy <= 0) return;

        // State immer setzen — auch ohne Punkte, damit Klicken sofort funktioniert
        _pfadScale      = Math.Min(_topRect.Width / wx, _topRect.Height / wy);
        _pfadCanvasRect = _topRect;

        if (_pfadPunkte.Count == 0) return;

        int selIdx = PfadLvPunkte.SelectedIndex;

        // Linien mit Richtungspfeil-Mitte
        for (int i = 0; i < _pfadPunkte.Count - 1; i++)
        {
            var a = PunktToPx(_pfadPunkte[i]);
            var b = PunktToPx(_pfadPunkte[i + 1]);
            bool hi = (i == selIdx || i + 1 == selIdx);
            DrawCanvas.Children.Add(new Line
            {
                X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y,
                Stroke = hi ? Brushes.OrangeRed : Brushes.DarkViolet,
                StrokeThickness = hi ? 3 : 2
            });

            // Kleiner Richtungspfeil in der Linienmitte
            double mx = (a.X + b.X) / 2, my = (a.Y + b.Y) / 2;
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len > 20)
            {
                dx /= len; dy /= len;
                double sz = 6;
                DrawCanvas.Children.Add(new Polygon
                {
                    Points = new PointCollection
                    {
                        new(mx, my),
                        new(mx - dx * sz + dy * sz / 2, my - dy * sz - dx * sz / 2),
                        new(mx - dx * sz - dy * sz / 2, my - dy * sz + dx * sz / 2)
                    },
                    Fill = hi ? Brushes.OrangeRed : Brushes.DarkViolet,
                    IsHitTestVisible = false
                });
            }
        }

        // Punkte + Nummern
        for (int i = 0; i < _pfadPunkte.Count; i++)
        {
            var px = PunktToPx(_pfadPunkte[i]);
            bool sel  = i == selIdx;
            bool hov  = i == _pfadHoverIdx;
            double r  = (sel || hov) ? 7 : 5;

            var dot = new Ellipse
            {
                Width = r * 2, Height = r * 2,
                Fill = i == 0 ? Brushes.LimeGreen : (hov ? Brushes.Orange : (sel ? Brushes.Gold : Brushes.DarkViolet)),
                Stroke = Brushes.White, StrokeThickness = 1.5,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(dot, px.X - r);
            Canvas.SetTop(dot, px.Y - r);
            DrawCanvas.Children.Add(dot);

            var lbl = new TextBlock
            {
                Text = (i + 1).ToString(),
                FontSize = 10, FontWeight = FontWeights.Bold,
                Foreground = Brushes.DarkViolet,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(lbl, px.X + r + 2);
            Canvas.SetTop(lbl, px.Y - 8);
            DrawCanvas.Children.Add(lbl);

            // Hover-Pfeile
            if (hov)
                DrawHoverArrows(px, i);
        }
    }

    private void DrawHoverArrows(Point center, int punktIdx)
    {
        double dist = 24;   // px vom Mittelpunkt
        double sz   = 9;    // Pfeilgröße

        (double dx, double dy, string dir)[] dirs =
        [
            ( 0,   -dist, "U"),   // ↑
            ( 0,    dist, "D"),   // ↓
            (-dist, 0,    "L"),   // ←
            ( dist, 0,    "R"),   // →
        ];

        foreach (var (dx, dy, dir) in dirs)
        {
            var tip = new Point(center.X + dx, center.Y + dy);

            // Pfeil-Dreieck
            PointCollection pts = dir switch
            {
                "U" => [new(tip.X, tip.Y), new(tip.X - sz/2, tip.Y + sz), new(tip.X + sz/2, tip.Y + sz)],
                "D" => [new(tip.X, tip.Y), new(tip.X - sz/2, tip.Y - sz), new(tip.X + sz/2, tip.Y - sz)],
                "L" => [new(tip.X, tip.Y), new(tip.X + sz, tip.Y - sz/2), new(tip.X + sz, tip.Y + sz/2)],
                _   => [new(tip.X, tip.Y), new(tip.X - sz, tip.Y - sz/2), new(tip.X - sz, tip.Y + sz/2)],
            };

            // Transparenter Klickbereich (größer als der Pfeil)
            var hitArea = new Ellipse
            {
                Width = 22, Height = 22,
                Fill = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Tag = $"{punktIdx}:{dir}"
            };
            hitArea.MouseLeftButtonDown += OnPfadArrowClick;
            Canvas.SetLeft(hitArea, tip.X - 11);
            Canvas.SetTop(hitArea,  tip.Y - 11);
            DrawCanvas.Children.Add(hitArea);

            var arrow = new Polygon
            {
                Points = pts,
                Fill = Brushes.DarkOrange,
                Stroke = Brushes.White, StrokeThickness = 1,
                Cursor = Cursors.Hand,
                Tag = $"{punktIdx}:{dir}"
            };
            arrow.MouseLeftButtonDown += OnPfadArrowClick;
            DrawCanvas.Children.Add(arrow);
        }
    }
#endif // DrawPfadFräsen + DrawHoverArrows Ende

    // ── Hilfsmethode ─────────────────────────────────────────────

    private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsGCodeTabActive())
            FlushGCodeBox();
    }

    private void OnGCodeAnzeigen(object sender, RoutedEventArgs e)
    {
        TabGCode.Visibility = MnuGCodeAnzeigen.IsChecked ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnWerkzeugeAnzeigen(object sender, RoutedEventArgs e)
    {
        TabWerkzeuge.Visibility = MnuWerkzeugeAnzeigen.IsChecked ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool _showFraesbreite = false;

    private void OnFraesbreiteAnzeigen(object sender, RoutedEventArgs e)
    {
        _showFraesbreite = MnuFraesbreite.IsChecked;
        UpdateAll();
    }

    private void OnVCarveVisualisierenChanged(object sender, RoutedEventArgs e)
    {
        // Cache leeren → neue Schrittweite wird beim ersten Einschalten sofort wirksam
        _vCarveCache.Clear();
        UpdateAll();
    }

    private void OnWerkzeugCellsChanged(object sender, SelectedCellsChangedEventArgs e)
    {
        if (WerkzeugGrid.SelectedCells.Count > 0)
            WerkzeugGrid.BeginEdit();
    }

    private void OnWerkzeugCellPreparing(object sender, DataGridPreparingCellForEditEventArgs e)
    {
        if (e.EditingElement is TextBox tb)
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input,
                () => { tb.SelectAll(); tb.Focus(); });
    }

    private void OnWerkzeugKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;

        var item       = WerkzeugGrid.CurrentCell.Item;
        var currentCol = WerkzeugGrid.CurrentCell.Column;

        WerkzeugGrid.CommitEdit(DataGridEditingUnit.Cell, true);

        var editableCols = WerkzeugGrid.Columns
            .Where(c => c.Visibility == Visibility.Visible && !c.IsReadOnly)
            .OrderBy(c => c.DisplayIndex)
            .ToList();

        if (editableCols.Count == 0 || currentCol == null) return;

        int idx  = editableCols.IndexOf(currentCol);
        int next = idx < 0 ? 0 : (idx + 1) % editableCols.Count;

        WerkzeugGrid.CurrentCell = new DataGridCellInfo(item, editableCols[next]);
        WerkzeugGrid.BeginEdit();
    }

    private void LoadWerkzeuge()
    {
        _suppressSave = true;
        try
        {
            _werkzeuge.Clear();
            if (File.Exists(WerkzeugDatei))
            {
                var list = JsonSerializer.Deserialize<List<Werkzeug>>(File.ReadAllText(WerkzeugDatei));
                if (list != null)
                    foreach (var w in list)
                        _werkzeuge.Add(w);
            }
            if (_werkzeuge.Count == 0)
                _werkzeuge.Add(new Werkzeug
                {
                    Nr = 1, Name = "Werkzeug 1",
                    Durchmesser = 10, Schneidenwinkel = 180, ZZustellung = 4,
                    Eintauchwinkel = 90, VorschubFxy = 3000, VorschubFz = 2000,
                    Drehzahl = 18000, RaeumzustellungXY = 75,
                });
        }
        catch { /* korrupte Datei ignorieren */ }
        finally { _suppressSave = false; }
    }

    private void SaveWerkzeuge()
    {
        if (_suppressSave) return;
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(WerkzeugDatei)!);
            var json = JsonSerializer.Serialize(_werkzeuge.ToList(),
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(WerkzeugDatei, json);
        }
        catch { }
    }

    private void OnWerkzeugInitializingNewItem(object sender, InitializingNewItemEventArgs e)
    {
        if (e.NewItem is not Werkzeug w) return;
        int nr = (_werkzeuge.Count > 0 ? _werkzeuge.Max(x => x.Nr) : 0) + 1;
        var tmpl = _werkzeuge.LastOrDefault();
        w.Nr = nr;
        if (tmpl != null)
        {
            w.Name              = tmpl.Name;
            w.Durchmesser       = tmpl.Durchmesser;
            w.Schneidenwinkel   = tmpl.Schneidenwinkel;
            w.ZZustellung       = tmpl.ZZustellung;
            w.Eintauchwinkel    = tmpl.Eintauchwinkel;
            w.VorschubFxy       = tmpl.VorschubFxy;
            w.VorschubFz        = tmpl.VorschubFz;
            w.Drehzahl          = tmpl.Drehzahl;
            w.RaeumzustellungXY = tmpl.RaeumzustellungXY;
        }
        else
        {
            w.Durchmesser = 10; w.Schneidenwinkel = 180; w.ZZustellung = 4;
            w.Eintauchwinkel = 90; w.VorschubFxy = 3000; w.VorschubFz = 2000;
            w.Drehzahl = 18000; w.RaeumzustellungXY = 75;
        }
    }

    private void OnWerkzeugCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Commit)
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, SaveWerkzeuge);
    }

    private void OnWerkzeugLoeschen(object sender, RoutedEventArgs e)
    {
        if (WerkzeugGrid.SelectedItem is Werkzeug w)
            _werkzeuge.Remove(w);
    }

    private void OnRasterEinblenden(object sender, RoutedEventArgs e)
    {
        if (MnuRaster.IsChecked)
        {
            var dlg = new RasterDialog(_rasterX, _rasterY) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                _rasterX = dlg.RasterX;
                _rasterY = dlg.RasterY;
                _rasterEnabled = true;
            }
            else
            {
                MnuRaster.IsChecked = false;
                _rasterEnabled = false;
            }
        }
        else
        {
            _rasterEnabled = false;
        }
        UpdateAll();
    }

    private void DrawRaster()
    {
        if (_topRect.IsEmpty) return;
        double cw = DrawCanvas.ActualWidth;
        double ch = DrawCanvas.ActualHeight;
        double wx = WorkX, wy = WorkY;
        if (wx <= 0 || wy <= 0) return;

        double scale = Math.Min(_topRect.Width / wx, _topRect.Height / wy);
        double stepX = _rasterX * scale;
        double stepY = _rasterY * scale;
        if (stepX < 1 || stepY < 1) return;

        // Ursprung (0,0) des Werkstück-Koordinatensystems in Canvas-Pixeln
        double ox = _topRect.Left;
        double oy = _topRect.Bottom;

        var pen = new SolidColorBrush(Color.FromArgb(55, 30, 90, 200));
        pen.Freeze();

        // Vertikale Linien (X-Abstand), von Ursprung nach rechts und links
        for (double px = ox; px <= cw; px += stepX)
            DrawCanvas.Children.Add(MakeGridLine(px, 0, px, ch, pen));
        for (double px = ox - stepX; px >= 0; px -= stepX)
            DrawCanvas.Children.Add(MakeGridLine(px, 0, px, ch, pen));

        // Horizontale Linien (Y-Abstand), von Ursprung nach oben und unten
        for (double py = oy; py >= 0; py -= stepY)
            DrawCanvas.Children.Add(MakeGridLine(0, py, cw, py, pen));
        for (double py = oy + stepY; py <= ch; py += stepY)
            DrawCanvas.Children.Add(MakeGridLine(0, py, cw, py, pen));
    }

    private Line MakeGridLine(double x1, double y1, double x2, double y2, Brush brush) =>
        new() { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = brush, StrokeThickness = 0.5 / _zoom, IsHitTestVisible = false };

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

    // ── G-Code Simulation ───────────────────────────────────────────────────

    private void BuildSimPath()
    {
        _simSegs.Clear();
        _simTotalMm = 0;
        var moves = GCodeParser.ParseTopView(_gcodeContent);
        double prevX = 0, prevY = 0;

        foreach (var m in moves)
        {
            bool   rapid = m.Type == MoveType.Rapid;
            double feed  = rapid ? 3000 : (m.FeedRate > 0 ? m.FeedRate : 800);

            if (m.Type is MoveType.Rapid or MoveType.Line)
            {
                double dx = m.X - prevX, dy = m.Y - prevY;
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len > 1e-9)
                {
                    _simSegs.Add(new SimSeg(prevX, prevY, m.X, m.Y, len, _simTotalMm,
                        rapid, feed, false, 0, 0, 0, 0, 0));
                    _simTotalMm += len;
                }
                prevX = m.X; prevY = m.Y;
            }
            else // Arc
            {
                double cx = m.X + m.I, cy = m.Y + m.J;
                double r  = Math.Sqrt(m.I * m.I + m.J * m.J);
                if (r > 1e-9)
                {
                    double a0 = Math.Atan2(m.Y - cy, m.X - cx);
                    double a1 = Math.Atan2(m.Ye - cy, m.Xe - cx);
                    bool   cw = m.Type == MoveType.ArcCW;
                    double da = a1 - a0;
                    bool full = Math.Abs(m.Xe - m.X) < 1e-6 && Math.Abs(m.Ye - m.Y) < 1e-6;
                    if (full) da = cw ? -2 * Math.PI : 2 * Math.PI;
                    else
                    {
                        if ( cw && da > 0) da -= 2 * Math.PI;
                        if (!cw && da < 0) da += 2 * Math.PI;
                    }
                    double len = Math.Abs(da) * r;
                    _simSegs.Add(new SimSeg(m.X, m.Y, m.Xe, m.Ye, len, _simTotalMm,
                        false, feed, true, cx, cy, r, a0, da));
                    _simTotalMm += len;
                }
                prevX = m.Xe; prevY = m.Ye;
            }
        }

        _simPathDirty = false;
        _simSliderBusy = true;
        SimSlider.Maximum = _simTotalMm > 0 ? _simTotalMm : 1;
        SimSlider.Value   = 0;
        _simSliderBusy = false;
        _simPosMm = 0;
        TxtSimPos.Text = "0 mm";
    }

    private (double x, double y, bool rapid, double feed) SimInterp(double posMm)
    {
        if (_simSegs.Count == 0) return (0, 0, false, 800);
        posMm = Math.Clamp(posMm, 0, _simTotalMm);

        int lo = 0, hi = _simSegs.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (_simSegs[mid].CumStart <= posMm) lo = mid; else hi = mid - 1;
        }
        var s = _simSegs[lo];
        double t = s.Len > 1e-12 ? Math.Clamp((posMm - s.CumStart) / s.Len, 0, 1) : 1.0;

        double x, y;
        if (!s.IsArc)
        {
            x = s.X0 + t * (s.X1 - s.X0);
            y = s.Y0 + t * (s.Y1 - s.Y0);
        }
        else
        {
            double a = s.A0 + t * s.DA;
            x = s.Cx + s.R * Math.Cos(a);
            y = s.Cy + s.R * Math.Sin(a);
        }
        return (x, y, s.IsRapid, s.FeedMmMin);
    }

    private void DrawSimTool(double xMm, double yMm, bool rapid)
    {
        SimToolCanvas.Children.Clear();
        if (_topRect.IsEmpty || WorkX <= 0 || WorkY <= 0) return;

        double scale  = Math.Min(_topRect.Width / WorkX, _topRect.Height / WorkY);
        // DrawCanvas-lokale Koordinaten → Bildschirmkoordinaten (Zoom + Pan)
        double px     = (_topRect.Left   + xMm * scale) * _zoom + _panX;
        double py     = (_topRect.Bottom - yMm * scale) * _zoom + _panY;
        double rPx    = Math.Max(6, 2.0 * scale * _zoom);

        var color  = rapid ? Color.FromArgb(200, 80, 80, 255) : Color.FromArgb(200, 255, 100, 0);
        var stroke = new SolidColorBrush(color);
        var fill   = new SolidColorBrush(Color.FromArgb(50, color.R, color.G, color.B));

        var circle = new System.Windows.Shapes.Ellipse
        {
            Width = rPx * 2, Height = rPx * 2,
            Stroke = stroke, StrokeThickness = 2,
            Fill   = fill
        };
        Canvas.SetLeft(circle, px - rPx);
        Canvas.SetTop(circle,  py - rPx);
        SimToolCanvas.Children.Add(circle);
    }

    private void OnSimPlay(object sender, RoutedEventArgs e)
    {
        if (_simPathDirty) BuildSimPath();
        if (_simTotalMm <= 0) return;

        if (_simPosMm >= _simTotalMm - 1e-6) _simPosMm = 0;

        _simPlaying = !_simPlaying;
        BtnSimPlay.Content = _simPlaying ? "⏸" : "▶";

        if (_simPlaying)
        {
            _simLastTick = DateTime.UtcNow;
            _simTimer.Start();
        }
        else _simTimer.Stop();
    }

    private void OnSimStop(object sender, RoutedEventArgs e)
    {
        _simPlaying = false;
        _simTimer.Stop();
        _simPosMm          = 0;
        BtnSimPlay.Content = "▶";
        _simSliderBusy     = true;
        SimSlider.Value    = 0;
        _simSliderBusy     = false;
        TxtSimPos.Text     = "0 mm";
        SimToolCanvas.Children.Clear();
    }

    private void OnSimSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_simSliderBusy) return;
        _simPosMm = SimSlider.Value;
        TxtSimPos.Text = $"{_simPosMm:F0} mm";
        var (x, y, rapid, _) = SimInterp(_simPosMm);
        DrawSimTool(x, y, rapid);
    }

    private void OnSimSliderMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_simPlaying)
        {
            _simPlaying = false;
            _simTimer.Stop();
            BtnSimPlay.Content = "▶";
        }
    }

    private void OnSimSliderMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e) { }

    private void OnSimSpeedChanged(object sender, SelectionChangedEventArgs e)
    {
        _simSpeedMult = CbSimSpeed.SelectedIndex switch
        {
            1 => 2.0, 2 => 5.0, 3 => 10.0, _ => 1.0
        };
    }

    private void OnSimTick(object? sender, EventArgs e)
    {
        var now       = DateTime.UtcNow;
        double elMs   = Math.Min((now - _simLastTick).TotalMilliseconds, 100);
        _simLastTick  = now;

        var (_, _, rapid, feedMmMin) = SimInterp(_simPosMm);
        double effFeed = rapid ? Math.Max(feedMmMin, 3000) : Math.Max(feedMmMin, 100);
        double deltaMm = effFeed / 60000.0 * elMs * _simSpeedMult;

        _simPosMm = Math.Min(_simPosMm + deltaMm, _simTotalMm);

        _simSliderBusy  = true;
        SimSlider.Value = _simPosMm;
        _simSliderBusy  = false;
        TxtSimPos.Text  = $"{_simPosMm:F0} mm";

        var (x, y, isRapid, _) = SimInterp(_simPosMm);
        DrawSimTool(x, y, isRapid);

        if (_simPosMm >= _simTotalMm)
        {
            _simPlaying        = false;
            _simTimer.Stop();
            BtnSimPlay.Content = "▶";
        }
    }
}

public sealed class GCodeColorizer : DocumentColorizingTransformer
{
    private static Brush Fb(byte r, byte g, byte b)
    {
        var br = new SolidColorBrush(Color.FromRgb(r, g, b));
        br.Freeze();
        return br;
    }

    private static readonly Brush BrRapid   = Fb(150, 150, 150); // G00
    private static readonly Brush BrCut     = Fb( 26,  26,  26); // G01
    private static readonly Brush BrArc     = Fb( 10,  80, 180); // G02/G03
    private static readonly Brush BrMCode   = Fb(160,  80,   0); // M
    private static readonly Brush BrComment = Fb( 60, 130,  60); // Kommentar

    private static Brush LineColor(string line)
    {
        var t = line.TrimStart();
        if (t.Length == 0) return BrCut;
        char c = char.ToUpperInvariant(t[0]);
        if (c == ';' || c == '(') return BrComment;
        if (c == 'M')             return BrMCode;
        if (c != 'G' || t.Length < 2) return BrCut;
        char d = t[1];
        if (d == '0' && (t.Length == 2 || !char.IsDigit(t[2]))) return BrRapid;
        if (d == '0' && t.Length > 2 && t[2] == '0')            return BrRapid;
        if ((d == '2' || d == '3') && (t.Length == 2 || !char.IsDigit(t[2]))) return BrArc;
        if (t.Length > 2 && (t[2] == '2' || t[2] == '3') && (t.Length == 3 || !char.IsDigit(t[3])) && d == '0') return BrArc;
        return BrCut;
    }

    protected override void ColorizeLine(ICSharpCode.AvalonEdit.Document.DocumentLine line)
    {
        var text  = CurrentContext.Document.GetText(line);
        var brush = LineColor(text);
        ChangeLinePart(line.Offset, line.EndOffset, el =>
            el.TextRunProperties.SetForegroundBrush(brush));
    }
}

// Zeilenhintergrund: G00-Zeilen leicht grau, M30-Zeile zartes Rot
public sealed class GCodeLineBackgroundRenderer : IBackgroundRenderer
{
    private static Brush MkBg(byte r, byte g, byte b, byte a)
    {
        var br = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        br.Freeze();
        return br;
    }

    private static readonly Brush BgG00 = MkBg(  0,   0,   0,   0); // kein Hintergrund für G00
    private static readonly Brush BgM30 = MkBg(220, 100,   0,  25); // zartes Orange für M-Ende
    private static readonly Brush BgSel = MkBg(255, 210,   0, 140); // kräftiges Gold für Werkstück-Selektion
    private static readonly Brush BgHov = MkBg(255, 230,  60,  70); // helleres Gold für Hover
    private static readonly Pen   PenSel;

    static GCodeLineBackgroundRenderer()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(255, 180, 0)), 1.5);
        pen.Freeze();
        PenSel = pen;
    }

    private static readonly Regex RxG00 = new(@"\bG0*0\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxM30 = new(@"\bM30\b",  RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Von Werkstück-Klick gesetzte selektierte Zeile (1-basiert, -1 = keine)</summary>
    public int SelectedLine { get; set; } = -1;
    /// <summary>Maus-Hover-Zeile im Editor (1-basiert, -1 = keine)</summary>
    public int HoverLine    { get; set; } = -1;

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (textView?.Document is null) return;
        textView.EnsureVisualLines();
        foreach (var vl in textView.VisualLines)
        {
            var docLine = vl.FirstDocumentLine;
            var text    = textView.Document.GetText(docLine);

            // Werkstück-Selektion hat Vorrang (überschreibt G00/M30-Farbe)
            if (docLine.LineNumber == SelectedLine)
            {
                foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, docLine))
                    drawingContext.DrawRectangle(BgSel, PenSel,
                        new Rect(0, rect.Y, textView.ActualWidth, rect.Height));
                continue;
            }
            // Hover-Zeile (Maus im Editor)
            if (docLine.LineNumber == HoverLine)
            {
                foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, docLine))
                    drawingContext.DrawRectangle(BgHov, null,
                        new Rect(0, rect.Y, textView.ActualWidth, rect.Height));
                continue;
            }

            Brush? bg = null;
            if (RxG00.IsMatch(text)) bg = BgG00;
            if (RxM30.IsMatch(text)) bg = BgM30;  // M30 überschreibt G00
            if (bg is null) continue;

            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, docLine))
                drawingContext.DrawRectangle(bg, null,
                    new Rect(0, rect.Y, textView.ActualWidth, rect.Height));
        }
    }
}

// Richtungspfeil-Spalte: zeigt pro Zeile die Bewegungsrichtung des Fräskopfes
// Richtung = Vektor vom Standort der Vorzeile zum Ziel der aktuellen Zeile
public sealed class GCodeArrowMargin : AbstractMargin
{
    private const double ColW = 34;

    private List<LineData>? _cachedLines;  // wird in OnRender befüllt
    private ToolTip?        _tip;
    private int             _lastTipLine = -1;

    public GCodeArrowMargin()
    {
        Width      = ColW;
        MouseMove  += OnMarginMouseMove;
        MouseLeave += OnMarginMouseLeave;
    }

    protected override void OnTextViewChanged(TextView? oldTextView, TextView? newTextView)
    {
        if (oldTextView != null)
        {
            oldTextView.VisualLinesChanged    -= OnViewChanged;
            oldTextView.ScrollOffsetChanged   -= OnViewChanged;
        }
        base.OnTextViewChanged(oldTextView, newTextView);
        if (newTextView != null)
        {
            newTextView.VisualLinesChanged    += OnViewChanged;
            newTextView.ScrollOffsetChanged   += OnViewChanged;
        }
    }

    private void OnViewChanged(object? sender, EventArgs e) => InvalidateVisual();

    protected override Size MeasureOverride(Size availableSize) => new(ColW, 0);

    // ── Zeichenressourcen ─────────────────────────────────────────────────────
    private static Pen MkPen(byte r, byte g, byte b, double thick, bool dash = false)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(r, g, b)), thick);
        if (dash) pen.DashStyle = DashStyles.Dash;
        pen.Freeze();
        return pen;
    }
    private static Brush MkBr(byte r, byte g, byte b)
    { var br = new SolidColorBrush(Color.FromRgb(r, g, b)); br.Freeze(); return br; }

    // Eilgang: dünner gestrichelter grauer Pfeil
    private static readonly Pen   PenRapid  = MkPen(170, 170, 170, 1.2, dash: true);
    private static readonly Brush BrRapid   = MkBr(170, 170, 170);
    // Linearbewegung: kräftiger schwarzer Pfeil
    private static readonly Pen   PenLinear = MkPen( 30,  30,  30, 1.8);
    private static readonly Brush BrLinear  = MkBr( 30,  30,  30);
    // Bogen: blau
    private static readonly Brush BrArc     = MkBr( 30,  90, 210);
    // M-Codes: rot
    private static readonly Brush BrM       = MkBr(200,  20,  20);
    // Hintergrund der Spalte (passend zu AvalonEdit-Zeilennummer-Farbe)
    private static readonly Brush BgCol     = MkBr(240, 240, 236);
    private static readonly Pen   PenSep    = new(MkBr(200, 200, 195), 1) { };

    private static readonly Typeface Tf = new("Segoe UI Symbol");

    // ── G-Code Mini-Interpreter ───────────────────────────────────────────────
    private static readonly Regex RxG = new(@"(?<![.\d])G(\d+(?:\.\d+)?)(?!\d)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxM = new(@"(?<![.\d])M(\d+)(?!\d)",            RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxX = new(@"(?<![A-Za-z])X([+-]?[\d.]+)",       RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxY = new(@"(?<![A-Za-z])Y([+-]?[\d.]+)",       RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxZ = new(@"(?<![A-Za-z])Z([+-]?[\d.]+)",       RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private enum GCmd { None, Rapid, Linear, ArcCW, ArcCCW, MCode, End }

    // DX/DY/DZ = Differenz vom vorherigen Standort zum Ziel dieser Zeile
    private record LineData(GCmd Cmd, double DX, double DY, double DZ);

    private static List<LineData> ParseDocument(ICSharpCode.AvalonEdit.Document.TextDocument doc)
    {
        var inv    = System.Globalization.CultureInfo.InvariantCulture;
        var result = new List<LineData>(doc.LineCount);
        double x = 0, y = 0, z = 0;
        bool abs   = true;
        int  modal = 1; // modaler G-Code (Standard: G01)

        for (int ln = 1; ln <= doc.LineCount; ln++)
        {
            var seg  = doc.GetLineByNumber(ln);
            var text = doc.GetText(seg).Trim();

            if (string.IsNullOrEmpty(text) || text.StartsWith(";") || text.StartsWith("("))
            { result.Add(new LineData(GCmd.None, 0, 0, 0)); continue; }

            int  gMode   = modal;
            bool hasMove = false;
            bool hasMEnd = false;
            bool hasM    = false;

            foreach (Match mg in RxG.Matches(text))
            {
                if (!double.TryParse(mg.Groups[1].Value, System.Globalization.NumberStyles.Float, inv, out double gn)) continue;
                switch ((int)gn)
                {
                    case 0:  gMode = 0; hasMove = true; break;
                    case 1:  gMode = 1; hasMove = true; break;
                    case 2:  gMode = 2; hasMove = true; break;
                    case 3:  gMode = 3; hasMove = true; break;
                    case 90: abs = true;  break;
                    case 91: abs = false; break;
                }
            }
            modal = gMode;

            foreach (Match mm in RxM.Matches(text))
            {
                hasM = true;
                if (int.TryParse(mm.Groups[1].Value, out int mn) && mn == 30) hasMEnd = true;
            }

            // Zielkoordinaten dieser Zeile ermitteln
            double nx = x, ny = y, nz = z;
            var mx = RxX.Match(text); var my = RxY.Match(text); var mz = RxZ.Match(text);
            if (mx.Success && double.TryParse(mx.Groups[1].Value, System.Globalization.NumberStyles.Float, inv, out double vx))
            { nx = abs ? vx : x + vx; hasMove = true; }
            if (my.Success && double.TryParse(my.Groups[1].Value, System.Globalization.NumberStyles.Float, inv, out double vy))
            { ny = abs ? vy : y + vy; hasMove = true; }
            if (mz.Success && double.TryParse(mz.Groups[1].Value, System.Globalization.NumberStyles.Float, inv, out double vz))
            { nz = abs ? vz : z + vz; hasMove = true; }

            // Richtungsvektor: Standort (x,y,z) → Ziel (nx,ny,nz)
            double dx = nx - x, dy = ny - y, dz = nz - z;
            x = nx; y = ny; z = nz;

            GCmd cmd = GCmd.None;
            if      (hasMEnd)  cmd = GCmd.End;
            else if (hasMove)  cmd = gMode switch { 0 => GCmd.Rapid, 2 => GCmd.ArcCW, 3 => GCmd.ArcCCW, _ => GCmd.Linear };
            else if (hasM)     cmd = GCmd.MCode;

            result.Add(new LineData(cmd, dx, dy, dz));
        }
        return result;
    }

    // ── Rendering ─────────────────────────────────────────────────────────────
    protected override void OnRender(DrawingContext dc)
    {
        var tv = TextView;
        if (tv is null) return;

        dc.DrawRectangle(BgCol, null, new Rect(0, 0, ColW, ActualHeight));
        dc.DrawLine(PenSep, new Point(ColW - 0.5, 0), new Point(ColW - 0.5, ActualHeight));

        var doc = tv.Document;
        if (doc is null) return;

        double ppd   = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var    lines = ParseDocument(doc);
        _cachedLines = lines;            // für MouseMove-Tooltip merken
        double cx    = ColW / 2.0;

        foreach (var vl in tv.VisualLines)
        {
            int idx = vl.FirstDocumentLine.LineNumber - 1;
            if (idx < 0 || idx >= lines.Count) continue;

            double top = vl.GetTextLineVisualYPosition(vl.TextLines[0], VisualYPosition.TextTop) - tv.VerticalOffset;
            double h   = vl.Height;
            double cy  = top + h / 2;

            // Pfeilgröße: gut sichtbar, aber nicht zu wuchtig
            double sz = Math.Clamp(h * 0.60, 7, 13);

            DrawSymbol(dc, lines[idx], cx, cy, sz, ppd);
        }
    }

    private static void DrawSymbol(DrawingContext dc, LineData ld, double cx, double cy, double sz, double ppd)
    {
        switch (ld.Cmd)
        {
            case GCmd.Rapid:
                // Eilgang: gestrichelter grauer Pfeil in Richtung des Deltas
                DrawArrow(dc, PenRapid, BrRapid, cx, cy, sz, ld.DX, ld.DY, ld.DZ);
                break;
            case GCmd.Linear:
                // Linearbewegung: kräftiger schwarzer Richtungspfeil
                DrawArrow(dc, PenLinear, BrLinear, cx, cy, sz, ld.DX, ld.DY, ld.DZ);
                break;
            case GCmd.ArcCW:
                DrawGlyph(dc, "↻", cx, cy, sz * 1.5, BrArc, ppd);
                break;
            case GCmd.ArcCCW:
                DrawGlyph(dc, "↺", cx, cy, sz * 1.5, BrArc, ppd);
                break;
            case GCmd.MCode:
                DrawGlyph(dc, "●", cx, cy, sz * 1.1, BrM, ppd);
                break;
            case GCmd.End:
                DrawGlyph(dc, "■", cx, cy, sz * 1.1, BrM, ppd);
                break;
        }
    }

    // Richtungspfeil: zeigt vom alten Standort (Vorzeile) zum neuen Ziel (diese Zeile).
    // dx/dy/dz = Differenz im Maschinen-Koordinatensystem.
    // WPF-Y ist invertiert zu CNC-Y (positive CNC-Y = Bildschirm-oben).
    private static void DrawArrow(DrawingContext dc, Pen pen, Brush headBrush,
        double cx, double cy, double sz, double dx, double dy, double dz)
    {
        // Winkel im WPF-Koordinatensystem: dy negieren, weil Y-Achse invertiert
        double angle;
        if (Math.Abs(dx) > 0.001 || Math.Abs(dy) > 0.001)
            angle = Math.Atan2(-dy, dx);
        else if (Math.Abs(dz) > 0.001)
            angle = dz < 0 ? Math.PI / 2 : -Math.PI / 2; // Z-: eintauchen (↓); Z+: abheben (↑)
        else
            return; // kein Delta → kein Pfeil

        // Pfeilschaft
        double half = sz * 0.68;
        double x0 = cx - Math.Cos(angle) * half;
        double y0 = cy - Math.Sin(angle) * half;
        double x1 = cx + Math.Cos(angle) * half;
        double y1 = cy + Math.Sin(angle) * half;
        dc.DrawLine(pen, new Point(x0, y0), new Point(x1, y1));

        // Pfeilspitze (gefülltes Dreieck)
        double hl = sz * 0.58;   // Länge der Spitze
        double hw = 0.58;        // halber Öffnungswinkel (rad)
        double a1 = angle + Math.PI - hw;
        double a2 = angle + Math.PI + hw;
        var tip = new Point(x1, y1);
        var p1  = new Point(x1 + Math.Cos(a1) * hl, y1 + Math.Sin(a1) * hl);
        var p2  = new Point(x1 + Math.Cos(a2) * hl, y1 + Math.Sin(a2) * hl);

        var fig = new PathFigure { StartPoint = tip, IsClosed = true, IsFilled = true };
        fig.Segments.Add(new LineSegment(p1, true));
        fig.Segments.Add(new LineSegment(p2, true));
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        dc.DrawGeometry(headBrush, null, geo);
    }

    private static void DrawGlyph(DrawingContext dc, string glyph, double cx, double cy,
        double sz, Brush brush, double ppd)
    {
        var ft = new FormattedText(glyph,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, Tf, sz, brush, ppd);
        dc.DrawText(ft, new Point(cx - ft.Width / 2, cy - ft.Height / 2));
    }

    // ── Tooltip ───────────────────────────────────────────────────────────────
    private void OnMarginMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var tv = TextView;
        if (tv is null || _cachedLines is null) return;

        double mouseY = e.GetPosition(this).Y;
        LineData? ld  = null;
        int       ln  = 0;
        foreach (var vl in tv.VisualLines)
        {
            double top = vl.GetTextLineVisualYPosition(vl.TextLines[0], VisualYPosition.TextTop)
                         - tv.VerticalOffset;
            if (mouseY >= top && mouseY < top + vl.Height)
            {
                int idx = vl.FirstDocumentLine.LineNumber - 1;
                if (idx >= 0 && idx < _cachedLines.Count)
                { ld = _cachedLines[idx]; ln = idx + 1; }
                break;
            }
        }

        if (ld is null || ld.Cmd == GCmd.None)
        { if (_tip is not null) _tip.IsOpen = false; return; }

        // Inhalt nur neu aufbauen wenn sich die Zeile geändert hat
        if (_tip is null)
        {
            _tip = new ToolTip
            {
                Placement       = System.Windows.Controls.Primitives.PlacementMode.Mouse,
                Background      = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                HasDropShadow   = false,
                Padding         = new Thickness(0)
            };
        }
        if (ln != _lastTipLine)
        {
            _tip.Content  = BuildArrowTooltip(ld, ln);
            _lastTipLine  = ln;
        }
        _tip.IsOpen = true;
    }

    private void OnMarginMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_tip is not null) _tip.IsOpen = false;
        _lastTipLine = -1;
    }

    private static UIElement BuildArrowTooltip(LineData ld, int lineNum)
    {
        // ── Pfeil-Canvas (linke Hälfte) ──────────────────────────────────────
        const double C = 80;
        var canvas = new Canvas
        {
            Width      = C,
            Height     = C,
            Background = new SolidColorBrush(Color.FromRgb(28, 28, 34))
        };

        bool xyMove = Math.Abs(ld.DX) > 0.001 || Math.Abs(ld.DY) > 0.001;
        bool zMove  = Math.Abs(ld.DZ) > 0.001;

        if (ld.Cmd is GCmd.Rapid or GCmd.Linear)
        {
            double angle;
            if (xyMove)     angle = Math.Atan2(-ld.DY, ld.DX);
            else if (zMove) angle = ld.DZ < 0 ? Math.PI / 2 : -Math.PI / 2;
            else            angle = 0;

            double cx = C / 2, cy = C / 2, half = C * 0.34;
            double x0 = cx - Math.Cos(angle) * half, y0 = cy - Math.Sin(angle) * half;
            double x1 = cx + Math.Cos(angle) * half, y1 = cy + Math.Sin(angle) * half;

            bool   isRapid = ld.Cmd == GCmd.Rapid;
            var    col     = isRapid ? Color.FromRgb(150, 150, 150) : Color.FromRgb(230, 230, 225);
            var    stroke  = new SolidColorBrush(col);

            // Pfeilschaft
            var shaft = new System.Windows.Shapes.Line
            {
                X1 = x0, Y1 = y0, X2 = x1, Y2 = y1,
                Stroke          = stroke,
                StrokeThickness = isRapid ? 1.8 : 2.8,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap   = PenLineCap.Round
            };
            if (isRapid) shaft.StrokeDashArray = new System.Windows.Media.DoubleCollection { 4, 2 };
            canvas.Children.Add(shaft);

            // Startpunkt
            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = 6, Height = 6, Fill = stroke
            };
            Canvas.SetLeft(dot, x0 - 3); Canvas.SetTop(dot, y0 - 3);
            canvas.Children.Add(dot);

            // Pfeilspitze
            double hl = C * 0.17, hw = 0.44;
            var head = new System.Windows.Shapes.Polygon
            {
                Points = new PointCollection
                {
                    new(x1, y1),
                    new(x1 + Math.Cos(angle + Math.PI - hw) * hl,
                        y1 + Math.Sin(angle + Math.PI - hw) * hl),
                    new(x1 + Math.Cos(angle + Math.PI + hw) * hl,
                        y1 + Math.Sin(angle + Math.PI + hw) * hl)
                },
                Fill = stroke
            };
            canvas.Children.Add(head);

            // Koordinatenkreuz — volle Breite, deutlich sichtbar
            var dimBrush = new SolidColorBrush(Color.FromArgb(110, 140, 140, 170));
            canvas.Children.Add(new System.Windows.Shapes.Line
            { X1=2, Y1=C/2, X2=C-2, Y2=C/2, Stroke=dimBrush, StrokeThickness=1.0 });
            canvas.Children.Add(new System.Windows.Shapes.Line
            { X1=C/2, Y1=2, X2=C/2, Y2=C-2, Stroke=dimBrush, StrokeThickness=1.0 });
            // Achsenbeschriftung
            var axFont = new System.Windows.Media.FontFamily("Segoe UI");
            var lblX = new TextBlock { Text="X", FontSize=9, Foreground=dimBrush, FontFamily=axFont };
            Canvas.SetLeft(lblX, C-10); Canvas.SetTop(lblX, C/2+2); canvas.Children.Add(lblX);
            var lblY = new TextBlock { Text="Y", FontSize=9, Foreground=dimBrush, FontFamily=axFont };
            Canvas.SetLeft(lblY, C/2+3); Canvas.SetTop(lblY, 2); canvas.Children.Add(lblY);
        }
        else
        {
            // Bogensymbol oder M-Code
            var sym = ld.Cmd switch
            {
                GCmd.ArcCW  => "↻",
                GCmd.ArcCCW => "↺",
                GCmd.End    => "■",
                _           => "●"
            };
            var symColor = ld.Cmd is GCmd.ArcCW or GCmd.ArcCCW
                ? Color.FromRgb(80, 140, 230)
                : Color.FromRgb(210, 50, 50);
            var tb = new TextBlock
            {
                Text       = sym,
                FontSize   = 36,
                Foreground = new SolidColorBrush(symColor)
            };
            Canvas.SetLeft(tb, C / 2 - 20); Canvas.SetTop(tb, C / 2 - 26);
            canvas.Children.Add(tb);
        }

        // ── Infotext (rechte Hälfte) ──────────────────────────────────────────
        // Z-only G01: Eintauchen oder Herausziehen, kein XY-Vorschub
        bool zOnly = ld.Cmd == GCmd.Linear && !xyMove && zMove;

        var (cmdTitle, cmdDesc) = ld.Cmd switch
        {
            GCmd.Rapid  => ("G00 — Eilgang",
                            "Schnelle Positionierfahrt,\nkein Materialabtrag."),
            GCmd.Linear when zOnly && ld.DZ < 0
                        => ("G01 — Eintauchen ↓",
                            "Werkzeug senkrecht in\ndas Material eintauchen."),
            GCmd.Linear when zOnly && ld.DZ > 0
                        => ("G01 — Herausziehen ↑",
                            "Werkzeug senkrecht aus\ndem Material herausziehen."),
            GCmd.Linear => ("G01 — Linearbewegung",
                            "Gerades Fräsen entlang\ndes Richtungsvektors."),
            GCmd.ArcCW  => ("G02 — Bogen ↻",
                            "Kreisbogen im\nUhrzeigersinn."),
            GCmd.ArcCCW => ("G03 — Bogen ↺",
                            "Kreisbogen gegen\nden Uhrzeigersinn."),
            GCmd.MCode  => ("M-Code",
                            "Maschinenfunktion\n(Spindel / Kühlmittel …)"),
            GCmd.End    => ("M30 — Programmende",
                            "Programm beendet,\nRücksprung zum Anfang."),
            _           => ("", "")
        };

        var info = new StackPanel { Margin = new Thickness(10, 8, 12, 9), MinWidth = 160 };

        info.Children.Add(new TextBlock
        {
            Text       = $"Zeile {lineNum}",
            FontSize   = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(130, 130, 125)),
            Margin     = new Thickness(0, 0, 0, 2)
        });
        info.Children.Add(new TextBlock
        {
            Text       = cmdTitle,
            FontWeight = FontWeights.Bold,
            FontSize   = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(220, 170, 70)),
            Margin     = new Thickness(0, 0, 0, 4)
        });
        info.Children.Add(new TextBlock
        {
            Text         = cmdDesc,
            FontSize     = 11,
            Foreground   = new SolidColorBrush(Color.FromRgb(195, 195, 190)),
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(0, 0, 0, 6)
        });

        // Delta-Werte
        if (ld.Cmd is GCmd.Rapid or GCmd.Linear)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            void AddDelta(string lbl, double v)
            {
                if (Math.Abs(v) < 0.001) return;
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 0) };
                row.Children.Add(new TextBlock
                {
                    Text = lbl, Width = 28, FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(130, 130, 125))
                });
                row.Children.Add(new TextBlock
                {
                    Text = $"{v:+0.###;-0.###} mm",
                    FontFamily = new FontFamily("Consolas"), FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(210, 210, 200))
                });
                info.Children.Add(row);
            }
            AddDelta("ΔX:", ld.DX);
            AddDelta("ΔY:", ld.DY);
            AddDelta("ΔZ:", ld.DZ);

            double dist = Math.Sqrt(ld.DX * ld.DX + ld.DY * ld.DY);
            if (dist > 0.001)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
                row.Children.Add(new TextBlock
                {
                    Text = "↔", Width = 28, FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(130, 130, 125))
                });
                row.Children.Add(new TextBlock
                {
                    Text = $"{dist:0.###} mm",
                    FontFamily = new FontFamily("Consolas"), FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(140, 180, 210))
                });
                info.Children.Add(row);
            }

            // Kompassrichtung
            if (xyMove)
            {
                double deg = Math.Atan2(-ld.DY, ld.DX) * 180 / Math.PI;
                if (deg < 0) deg += 360;
                string compass = deg switch
                {
                    >= 337.5 or < 22.5   => "→  Ost",
                    >= 22.5  and < 67.5  => "↗  Nordost",
                    >= 67.5  and < 112.5 => "↑  Nord",
                    >= 112.5 and < 157.5 => "↖  Nordwest",
                    >= 157.5 and < 202.5 => "←  West",
                    >= 202.5 and < 247.5 => "↙  Südwest",
                    >= 247.5 and < 292.5 => "↓  Süd",
                    _                    => "↘  Südost"
                };
                info.Children.Add(new TextBlock
                {
                    Text     = $"{compass}  ({deg:0}°)",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(140, 170, 200)),
                    Margin   = new Thickness(0, 4, 0, 0)
                });
            }
        }

        // Canvas + Text nebeneinander
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(canvas);
        content.Children.Add(info);

        return new Border
        {
            Background      = new SolidColorBrush(Color.FromRgb(32, 32, 38)),
            BorderBrush     = new SolidColorBrush(Color.FromRgb(100, 100, 115)),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(7),
            Child           = content,
            Effect          = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black, Opacity = 0.6, BlurRadius = 10, ShadowDepth = 4
            }
        };
    }
}

public static class GCodeTooltip
{
    private static readonly Regex TokenRx = new(
        @"(?<c>;[^\r\n]*|\([^)]*\))|(?<g>G\d+(?:\.\d+)?)|(?<m>M\d+)|(?<f>F[+-]?[\d.]+)|(?<s>S[+-]?[\d.]+)|(?<xyz>[XYZ][+-]?[\d.]+)|(?<ijk>[IJK][+-]?[\d.]+)|(?<ln>N\d+)|(?<other>[A-EHLO-RT-W][+-]?[\d.]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Gibt (Titel, Beschreibung) für das Token an Position col zurück.</summary>
    public static (string? Title, string? Desc) GetTooltip(string lineText, int col)
    {
        foreach (Match m in TokenRx.Matches(lineText))
        {
            if (col >= m.Index && col < m.Index + m.Length)
                return Explain(m);
        }
        return (null, null);
    }

    private static (string? Title, string? Desc) Explain(Match m)
    {
        if (m.Groups["c"].Success)
            return ("Kommentar", "Wird von der Maschine vollständig ignoriert.");
        if (m.Groups["g"].Success) return ExplainG(m.Value);
        if (m.Groups["m"].Success) return ExplainM(m.Value);
        if (m.Groups["f"].Success)
            return ("F — Vorschubgeschwindigkeit", $"Werkzeugbewegung mit {m.Value[1..]} mm/min.");
        if (m.Groups["s"].Success)
            return ("S — Spindeldrehzahl", $"Spindel dreht mit {m.Value[1..]} U/min.");
        if (m.Groups["xyz"].Success) return ExplainXYZ(m.Value);
        if (m.Groups["ijk"].Success) return ExplainIJK(m.Value);
        if (m.Groups["ln"].Success)
            return ("N — Zeilennummer", $"Zeilennummer {m.Value[1..]}. Dient der Orientierung im Programm.");
        return (null, null);
    }

    private static (string, string) ExplainG(string tok)
    {
        return tok.ToUpperInvariant() switch
        {
            "G0"  or "G00" => ("G00 — Eilgang",
                "Schnelle Positionierfahrt ohne Fräsen. Das Werkzeug bewegt sich mit maximaler Geschwindigkeit zum Zielpunkt."),
            "G1"  or "G01" => ("G01 — Linearbewegung",
                "Gerades Fräsen mit dem eingestellten Vorschub F. Erzeugt eine gerade Linie im Material."),
            "G2"  or "G02" => ("G02 — Kreisbogen (Uhrzeigersinn)",
                "Fräst einen Kreisbogen im Uhrzeigersinn. Mittelpunkt wird mit I/J angegeben."),
            "G3"  or "G03" => ("G03 — Kreisbogen (Gegenuhrzeigersinn)",
                "Fräst einen Kreisbogen gegen den Uhrzeigersinn. Mittelpunkt wird mit I/J angegeben."),
            "G17"          => ("G17 — XY-Ebene",
                "Wählt die XY-Ebene als aktive Arbeitsebene. Standardeinstellung beim Fräsen."),
            "G20"          => ("G20 — Maßeinheit: Zoll",
                "Alle Koordinaten werden in Zoll (inch) interpretiert."),
            "G21"          => ("G21 — Maßeinheit: Millimeter",
                "Alle Koordinaten werden in Millimetern interpretiert."),
            "G28"          => ("G28 — Referenzpunkt anfahren",
                "Maschine fährt zum Maschinen-Nullpunkt (Referenzpunkt)."),
            "G40"          => ("G40 — Radiuskorrektur AUS",
                "Hebt eine aktive Werkzeug-Radiuskorrektur (G41/G42) auf."),
            "G41"          => ("G41 — Radiuskorrektur links",
                "Werkzeugmittelpunkt fährt links vom programmierten Pfad. Ausgleich für Frästerdurchmesser."),
            "G42"          => ("G42 — Radiuskorrektur rechts",
                "Werkzeugmittelpunkt fährt rechts vom programmierten Pfad. Ausgleich für Frästerdurchmesser."),
            "G54"          => ("G54 — Werkstück-Nullpunkt 1",
                "Aktiviert den ersten Werkstück-Koordinatenursprung (Nullpunktversatz 1)."),
            "G90"          => ("G90 — Absolute Koordinaten",
                "Alle Koordinaten beziehen sich auf den Werkstück-Nullpunkt."),
            "G91"          => ("G91 — Inkrementale Koordinaten",
                "Alle Koordinaten sind relativ zur aktuellen Werkzeugposition angegeben."),
            "G94"          => ("G94 — Vorschub mm/min",
                "Vorschubgeschwindigkeit F wird in Millimetern pro Minute interpretiert."),
            var u          => ($"{u} — G-Code", "Geometrischer Befehl für Bewegung oder Maschinenmodus.")
        };
    }

    private static (string, string) ExplainM(string tok)
    {
        return tok.ToUpperInvariant() switch
        {
            "M3"  or "M03" => ("M03 — Spindel EIN ↻",
                "Schaltet die Frässpindel im Uhrzeigersinn ein. Drehzahl wird mit S angegeben."),
            "M4"  or "M04" => ("M04 — Spindel EIN ↺",
                "Schaltet die Frässpindel gegen den Uhrzeigersinn ein."),
            "M5"  or "M05" => ("M05 — Spindel AUS",
                "Hält die Frässpindel an. Sollte vor Werkzeugwechsel und Programmende aufgerufen werden."),
            "M6"  or "M06" => ("M06 — Werkzeugwechsel",
                "Löst einen Werkzeugwechsel aus. Werkzeugnummer wird mit T angegeben."),
            "M8"  or "M08" => ("M08 — Kühlmittel EIN",
                "Schaltet die Kühlmittelzufuhr ein."),
            "M9"  or "M09" => ("M09 — Kühlmittel AUS",
                "Schaltet die Kühlmittelzufuhr aus."),
            "M30"          => ("M30 — Programmende",
                "Beendet das G-Code-Programm und setzt die Maschine auf den Programmanfang zurück."),
            var u          => ($"{u} — M-Code", "Maschinenfunktion (Hilfsfunktion, kein Bewegungsbefehl).")
        };
    }

    private static (string, string) ExplainXYZ(string tok)
    {
        var axis = char.ToUpperInvariant(tok[0]);
        var val  = tok[1..];
        return axis switch
        {
            'X' => ("X — Längsachse",   $"Zielposition X = {val} mm (links/rechts)."),
            'Y' => ("Y — Querachse",    $"Zielposition Y = {val} mm (vorne/hinten)."),
            'Z' => ("Z — Vertikalachse", $"Zielposition Z = {val} mm. Negative Werte bedeuten Eintauchen ins Material."),
            _   => (tok, "")
        };
    }

    private static (string, string) ExplainIJK(string tok)
    {
        var axis = char.ToUpperInvariant(tok[0]);
        var val  = tok[1..];
        return axis switch
        {
            'I' => ("I — Bogenmittelpunkt X", $"Abstand {val} mm in X-Richtung vom Startpunkt zum Bogenmittelpunkt."),
            'J' => ("J — Bogenmittelpunkt Y", $"Abstand {val} mm in Y-Richtung vom Startpunkt zum Bogenmittelpunkt."),
            'K' => ("K — Bogenmittelpunkt Z", $"Abstand {val} mm in Z-Richtung vom Startpunkt zum Bogenmittelpunkt."),
            _   => (tok, "")
        };
    }
}

public class Werkzeug
{
    public int    Nr                { get; set; }
    public string Name              { get; set; } = "";
    public string DisplayName       => $"{Nr}. {Name}";
    public double Durchmesser       { get; set; }
    public double Schneidenwinkel   { get; set; }
    public double ZZustellung       { get; set; }
    public double Eintauchwinkel    { get; set; }
    public double VorschubFxy       { get; set; }
    public double VorschubFz        { get; set; }
    public double Drehzahl          { get; set; }
    public double RaeumzustellungXY { get; set; } = 75.0;
}

public class HistoryEntry(string label, string details, object p, int level = 0)
{
    public string Label   { get; } = label;
    public string Details { get; } = details;
    public object Params  { get; } = p;
    public int    Level   { get; } = level;
}

public static class VisualTreeHelperExtensions
{
    public static T? FindVisualParent<T>(this DependencyObject obj) where T : DependencyObject
    {
        var current = VisualTreeHelper.GetParent(obj);
        while (current != null)
        {
            if (current is T t) return t;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
