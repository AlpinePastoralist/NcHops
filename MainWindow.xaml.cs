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

namespace NCHops;

public partial class MainWindow : Window
{
    private static Brush Fb(byte r, byte g, byte b) { var br = new SolidColorBrush(Color.FromRgb(r, g, b)); br.Freeze(); return br; }
    private static readonly Brush BrushRapid   = Fb(150, 150, 150);
    private static readonly Brush BrushCut     = Fb( 26,  26,  26);
    private static readonly Brush BrushArc     = Fb( 10,  80, 180);
    private static readonly Brush BrushMCode   = Fb(160,  80,   0);
    private static readonly Brush BrushComment = Fb( 60, 130,  60);

    private static readonly Regex GCodeTokenRegex = new(
        "([A-Za-z][+-]?\\d*\\.?\\d*)|(\\s+)|(.+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly DispatcherTimer _refreshTimer;
    private Rect _topRect;
    private Rect _bottomRect;
    private bool _rasterEnabled;
    private double _rasterX = 50.0;
    private double _rasterY = 50.0;
    private ScrollViewer? _gcodeScrollViewer;
    private ScrollViewer? _lineNumbersScrollViewer;
    private readonly ObservableCollection<HistoryEntry> _history = [];
    private readonly List<HistoryEntry> _historyClipboard = [];
    private bool _suppressHistoryRegen;
    private readonly ObservableCollection<Werkzeug> _werkzeuge = [];
    private bool _suppressSave;
    private int  _lastLineCount = -1;

    // G-Code Backing-Field: Canvas/Parser nutzen dies direkt, TextBox wird im Hintergrund gesetzt
    private string _gcodeContent = string.Empty;

    // Parse-Cache: G-Code nur neu parsen wenn Text sich ändert
    private string?         _parsedGCodeText   = null;
    private List<Move>      _cachedTopMoves    = [];
    private List<SideMove>  _cachedSideMoves   = [];
    private List<System.Windows.Point> _cachedDrillPoints = [];
    private static readonly string WerkzeugDatei = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NCHops", "werkzeuge.json");

    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);

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

Loaded += (_, _) => UpdateAll();

        _refreshTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(250), DispatcherPriority.Background,
            (_, _) => UpdateAll(), Dispatcher);
        _refreshTimer.Stop();

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
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool alt  = (Keyboard.Modifiers & ModifierKeys.Alt)     != 0;
        if (ctrl && e.Key == Key.C)       { CopySelectedHistory();   e.Handled = true; }
        else if (ctrl && e.Key == Key.V)  { PasteHistory();          e.Handled = true; }
        else if (e.Key == Key.Delete)     { DeleteSelectedHistory();  e.Handled = true; }
        else if (alt  && e.Key == Key.Up)   { MoveSelectedHistoryUp();   e.Handled = true; }
        else if (alt  && e.Key == Key.Down) { MoveSelectedHistoryDown(); e.Handled = true; }
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
        var sb = new System.Text.StringBuilder();
        var pfadBuffer = new List<PfadPunktParams>();
        double lastStartZ = 0;
        string lastRadiuskorrektur = "Mittig";
        double lastFraeserD = 0;

        void FlushPfad()
        {
            if (pfadBuffer.Count == 0) return;
            var c = GCodeGenerator.PfadFräsen(pfadBuffer, WorkX, WorkY);
            if (!string.IsNullOrEmpty(c)) sb.AppendLine(c);
            pfadBuffer.Clear();
        }

        foreach (var entry in _history)
        {
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
                BohrungParams p           => GCodeGenerator.Bohrung(p, WorkX, WorkY),
                ReihenlochbohrungParams p => GCodeGenerator.Reihenlochbohrung(p),
                UmfahrenParams p          => GCodeGenerator.Umfahren(p, WorkX, WorkY),
                TascheFräsenParams p      => GCodeGenerator.Tasche(p, WorkX, WorkY),
                KreistascheParams p       => GCodeGenerator.Kreistasche(p, WorkX, WorkY),
                _                         => string.Empty
            };
            if (!string.IsNullOrEmpty(code)) sb.AppendLine(code);
        }
        FlushPfad();

        GCodeText = sb.ToString();
        UpdatePfadMenuState();
        UpdateAll();
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
    private void OnWindowKeyDown(object sender, KeyEventArgs e) { }
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

    // ── Canvas: Klick / Drag ─────────────────────────────────────

    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (CbPfadAnzeigen?.IsChecked != true) return;
        if (_pfadCanvasRect.IsEmpty) return;

        var pos = e.GetPosition(DrawCanvas);
        if (!_pfadCanvasRect.Contains(pos)) return;

        // Nähe zu bestehendem Punkt? → Drag starten
        for (int i = 0; i < _pfadPunkte.Count; i++)
        {
            var px = PunktToPx(_pfadPunkte[i]);
            if (Math.Sqrt(Math.Pow(pos.X - px.X, 2) + Math.Pow(pos.Y - px.Y, 2)) < 14)
            {
                _pfadDragIdx = i;
                _pfadHoverIdx = i;
                PfadLvPunkte.SelectedIndex = i;
                DrawCanvas.CaptureMouse();
                DrawCanvas.Cursor = Cursors.SizeAll;
                return;
            }
        }

        // Kein Punkt in der Nähe → neuen Punkt hinzufügen (auf Raster, abs Koords)
        var (absX, absY) = PxToAbsMm(pos);
        double snap = PfadSchritt;
        absX = Math.Round(absX / snap) * snap;
        absY = Math.Round(absY / snap) * snap;

        string defaultBezug = (PfadCbBezug?.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Unten links";
        var (relX, relY) = AbsToRel(defaultBezug, absX, absY, WorkX, WorkY);

        _pfadPunkte.Add(new PfadPunkt
        {
            Nr    = _pfadPunkte.Count + 1,
            X     = relX.ToString("F1", System.Globalization.CultureInfo.InvariantCulture),
            Y     = relY.ToString("F1", System.Globalization.CultureInfo.InvariantCulture),
            Bezug = defaultBezug
        });
        PfadLvPunkte.SelectedIndex = _pfadPunkte.Count - 1;
        PfadLvPunkte.ScrollIntoView(PfadLvPunkte.SelectedItem);
    }

    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_pfadDragIdx >= 0)
        {
            _pfadDragIdx = -1;
            DrawCanvas.ReleaseMouseCapture();
        }
    }

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (CbPfadAnzeigen?.IsChecked != true) return;

        var pos = e.GetPosition(DrawCanvas);

        // ── Drag-Modus ──────────────────────────────────────────
        if (_pfadDragIdx >= 0)
        {
            var (absX, absY) = PxToAbsMm(pos);
            double snap = PfadSchritt;
            absX = Math.Round(absX / snap) * snap;
            absY = Math.Round(absY / snap) * snap;
            string dragBezug = _pfadPunkte[_pfadDragIdx].Bezug;
            var (relX, relY) = AbsToRel(dragBezug, absX, absY, WorkX, WorkY);
            _pfadPunkte[_pfadDragIdx].X = relX.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
            _pfadPunkte[_pfadDragIdx].Y = relY.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);

            UpdateAll();
            return;
        }

        // ── Hover-Erkennung ─────────────────────────────────────
        if (_arrowJustClicked) { _arrowJustClicked = false; return; }
        if (_pfadCanvasRect.IsEmpty) return;

        int newHover = -1;
        for (int i = 0; i < _pfadPunkte.Count; i++)
        {
            var px = PunktToPx(_pfadPunkte[i]);
            if (Math.Sqrt(Math.Pow(pos.X - px.X, 2) + Math.Pow(pos.Y - px.Y, 2)) < 20)
            { newHover = i; break; }
        }

        if (newHover != _pfadHoverIdx)
        {
            _pfadHoverIdx = newHover;
            UpdateAll();
        }

        DrawCanvas.Cursor = newHover >= 0
            ? Cursors.SizeAll
            : (_pfadCanvasRect.Contains(pos) ? Cursors.Cross : Cursors.Arrow);
    }

    private void OnCanvasMouseLeave(object sender, MouseEventArgs e)
    {
        if (_pfadDragIdx >= 0) return; // Drag läuft noch
        DrawCanvas.Cursor = Cursors.Arrow;
        if (_pfadHoverIdx >= 0) { _pfadHoverIdx = -1; UpdateAll(); }
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

            // TextBox nur aktualisieren wenn G-Code Tab aktiv ist
            if (IsGCodeTabActive())
                FlushGCodeBox();
        }
    }

    private bool IsGCodeTabActive() =>
        TabGCode.Visibility == Visibility.Visible &&
        TabGCode.IsSelected;

    private static Brush BrushForLine(string line)
    {
        var t = line.TrimStart();
        if (t.Length == 0) return BrushCut;
        char c = char.ToUpperInvariant(t[0]);
        if (c == ';' || c == '(') return BrushComment;
        if (c == 'M')             return BrushMCode;
        if (c != 'G' || t.Length < 2) return BrushCut;
        char d = t[1];
        if (d == '0' && (t.Length == 2 || !char.IsDigit(t[2]))) return BrushRapid;
        if (d == '0' && t.Length > 2 && t[2] == '0')            return BrushRapid;
        if ((d == '2' || d == '3') && (t.Length == 2 || !char.IsDigit(t[2]))) return BrushArc;
        if (t.Length > 2 && (t[2] == '2' || t[2] == '3') && (t.Length == 3 || !char.IsDigit(t[3])) && d == '0') return BrushArc;
        return BrushCut;
    }

    private void FlushGCodeBox()
    {
        if (!_gcodeBoxDirty) return;
        _gcodeBoxDirty = false;
        var raw   = _gcodeContent.Replace("\r\n", "\n").Split('\n');
        var items = Array.ConvertAll(raw, l => new GCodeLine(l, BrushForLine(l)));
        GCodeBox.ItemsSource = items;
        UpdateLineNumbers(items.Length);
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
        _gcodeScrollViewer = GetDescendantScrollViewer(GCodeBox);
        _lineNumbersScrollViewer = GetDescendantScrollViewer(GCodeLineNumbersBox);
        if (_gcodeScrollViewer != null)
            _gcodeScrollViewer.ScrollChanged += OnGCodeScrollChanged;
    }

    private void OnGCodeScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange != 0)
            _lineNumbersScrollViewer?.ScrollToVerticalOffset(e.VerticalOffset);
    }

    private void UpdateLineNumbers(int lineCount)
    {
        if (lineCount == _lastLineCount) return;
        _lastLineCount = lineCount;
        GCodeLineNumbersBox.ItemsSource = Enumerable.Range(1, lineCount).Select(i => i.ToString()).ToArray();
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
        if (DrawCanvas == null) return;
        EnsureParsed();
        DrawCanvas.Children.Clear();
        DrawWorkpieces();
        DrawGCodeTopView();
        DrawGCodeSideView();
        if (_rasterEnabled)
            DrawRaster();
#if false
        if (CbPfadAnzeigen?.IsChecked == true)
            DrawPfadFräsen();
#endif
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
        if (moves.Count == 0 || _topRect.IsEmpty) return;

        double wx = WorkX, wy = WorkY;
        if (wx <= 0 || wy <= 0) return;

        double scale = Math.Min(_topRect.Width / wx, _topRect.Height / wy);
        Point MmToPx(double x, double y) =>
            new(_topRect.Left + x * scale, _topRect.Bottom - y * scale);

        var cutGeo   = new StreamGeometry();
        var rapidGeo = new StreamGeometry();

        using (var cut   = cutGeo.Open())
        using (var rapid = rapidGeo.Open())
        {
            Point? last = null;

            foreach (var m in moves)
            {
                if (m.Type is MoveType.Rapid or MoveType.Line)
                {
                    var cur = MmToPx(m.X, m.Y);
                    if (last.HasValue)
                    {
                        var ctx = m.Type == MoveType.Rapid ? rapid : cut;
                        ctx.BeginFigure(last.Value, false, false);
                        ctx.LineTo(cur, true, false);
                    }
                    last = cur;
                }
                else
                {
                    double cx = m.X + m.I, cy = m.Y + m.J;
                    double r = Math.Sqrt((m.X - cx) * (m.X - cx) + (m.Y - cy) * (m.Y - cy));
                    if (r == 0) continue;

                    double start = Math.Atan2(m.Y - cy, m.X - cx);
                    double end   = Math.Atan2(m.Ye - cy, m.Xe - cx);

                    bool fullCircle = Math.Abs(m.Xe - m.X) < 1e-6 && Math.Abs(m.Ye - m.Y) < 1e-6;
                    if (fullCircle)
                        end = m.Type == MoveType.ArcCW ? start - 2 * Math.PI : start + 2 * Math.PI;
                    else
                    {
                        if (m.Type == MoveType.ArcCW  && end > start) end -= 2 * Math.PI;
                        if (m.Type == MoveType.ArcCCW && end < start) end += 2 * Math.PI;
                    }

                    int steps = Math.Max(8, (int)(Math.Abs(end - start) / (Math.PI / 36)));
                    var prev = MmToPx(m.X, m.Y);
                    cut.BeginFigure(prev, false, false);
                    for (int i = 1; i <= steps; i++)
                    {
                        double angle = start + (end - start) * i / steps;
                        var cur = MmToPx(cx + r * Math.Cos(angle), cy + r * Math.Sin(angle));
                        cut.LineTo(cur, true, false);
                        prev = cur;
                    }
                    last = MmToPx(m.Xe, m.Ye);
                }
            }
        }

        cutGeo.Freeze();
        rapidGeo.Freeze();

        var rapidDash = new System.Windows.Media.DoubleCollection { 5, 3 };
        rapidDash.Freeze();
        DrawCanvas.Children.Add(new System.Windows.Shapes.Path
        {
            Data = rapidGeo, Stroke = Brushes.Gray,
            StrokeThickness = 2, StrokeDashArray = rapidDash,
            IsHitTestVisible = false
        });
        DrawCanvas.Children.Add(new System.Windows.Shapes.Path
        {
            Data = cutGeo, Stroke = Brushes.Red,
            StrokeThickness = 2, IsHitTestVisible = false
        });

        foreach (var hole in _cachedDrillPoints)
        {
            var center = MmToPx(hole.X, hole.Y);
            var circle = new Ellipse
            {
                Width = 12, Height = 12,
                Fill = Brushes.Yellow, Stroke = Brushes.Black, StrokeThickness = 2
            };
            Canvas.SetLeft(circle, center.X - circle.Width / 2);
            Canvas.SetTop(circle, center.Y - circle.Height / 2);
            DrawCanvas.Children.Add(circle);
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
            Data = rapidGeo, Stroke = Brushes.Gray,
            StrokeThickness = 2, StrokeDashArray = rapidDash,
            IsHitTestVisible = false
        });
        DrawCanvas.Children.Add(new System.Windows.Shapes.Path
        {
            Data = cutGeo, Stroke = Brushes.Red,
            StrokeThickness = 2, IsHitTestVisible = false
        });
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

    private static Line MakeGridLine(double x1, double y1, double x2, double y2, Brush brush) =>
        new() { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = brush, StrokeThickness = 0.7, IsHitTestVisible = false };

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

public sealed class GCodeLine(string text, Brush color)
{
    public string Text  { get; } = text;
    public Brush  Color { get; } = color;
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
