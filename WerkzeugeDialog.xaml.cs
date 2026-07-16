using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace NCHops;

public partial class WerkzeugeDialog : Window
{
    private const string ClipboardFormat = "NCHopsWerkzeugListe";

    private readonly ObservableCollection<Werkzeug> _werkzeuge;
    private readonly Action _save;
    private readonly bool _waehlModus;

    /// <summary>Im Auswahlmodus (waehlModus=true) das vom Benutzer gewählte Werkzeug, sonst null.</summary>
    public Werkzeug? Ausgewaehlt { get; private set; }

    public WerkzeugeDialog(ObservableCollection<Werkzeug> werkzeuge, Action save, bool waehlModus = false)
    {
        InitializeComponent();
        _werkzeuge  = werkzeuge;
        _save       = save;
        _waehlModus = waehlModus;

        WerkzeugGridFraeser.ItemsSource = new ListCollectionView(_werkzeuge)
        {
            Filter = o => ((Werkzeug)o).Typ == WerkzeugTyp.Fraeser
        };
        WerkzeugGridBohrer.ItemsSource = new ListCollectionView(_werkzeuge)
        {
            Filter = o => ((Werkzeug)o).Typ == WerkzeugTyp.Bohrer
        };

        LstWerkzeuge.ItemsSource = _werkzeuge;
        if (_werkzeuge.Count > 0) LstWerkzeuge.SelectedIndex = 0;

        if (_waehlModus)
        {
            TxtHeaderTitel.Text  = "Werkzeug wählen";
            BtnWaehlen.Visibility = Visibility.Visible;
        }
    }

    private WerkzeugTyp TypFuer(DataGrid grid) =>
        ReferenceEquals(grid, WerkzeugGridBohrer) ? WerkzeugTyp.Bohrer : WerkzeugTyp.Fraeser;

    private void OnWerkzeugCellPreparing(object sender, DataGridPreparingCellForEditEventArgs e)
    {
        if (e.EditingElement is TextBox tb)
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input,
                () => { tb.SelectAll(); tb.Focus(); });
    }

    private void OnWerkzeugKeyDown(object sender, KeyEventArgs e)
    {
        // Zelle wird gerade bearbeitet: normales Text-Editing (inkl. Kopieren/Einfügen) zulassen
        if (Keyboard.FocusedElement is TextBox) return;

        var grid = (DataGrid)sender;

        if (e.Key == Key.Enter)
        {
            e.Handled = true;

            var item       = grid.CurrentCell.Item;
            var currentCol = grid.CurrentCell.Column;

            grid.CommitEdit(DataGridEditingUnit.Cell, true);

            var editableCols = grid.Columns
                .Where(c => c.Visibility == Visibility.Visible && !c.IsReadOnly)
                .OrderBy(c => c.DisplayIndex)
                .ToList();

            if (editableCols.Count == 0 || currentCol == null) return;

            int idx  = editableCols.IndexOf(currentCol);
            int next = idx < 0 ? 0 : (idx + 1) % editableCols.Count;

            grid.CurrentCell = new DataGridCellInfo(item, editableCols[next]);
            grid.BeginEdit();
        }
        else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            CopySelectedWerkzeuge(grid);
            e.Handled = true;
        }
        else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
        {
            PasteWerkzeuge(TypFuer(grid));
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            DeleteSelectedWerkzeuge(grid);
            e.Handled = true;
        }
    }

    private void CopySelectedWerkzeuge(DataGrid grid)
    {
        var items = grid.SelectedItems.OfType<Werkzeug>().ToList();
        if (items.Count == 0) return;

        var data = new DataObject();
        data.SetData(ClipboardFormat, JsonSerializer.Serialize(items));
        Clipboard.SetDataObject(data);
    }

    private void PasteWerkzeuge(WerkzeugTyp typ)
    {
        if (!Clipboard.ContainsData(ClipboardFormat)) return;
        if (Clipboard.GetData(ClipboardFormat) is not string json) return;

        List<Werkzeug>? copied;
        try { copied = JsonSerializer.Deserialize<List<Werkzeug>>(json); }
        catch { return; }
        if (copied == null || copied.Count == 0) return;

        int nextNr = (_werkzeuge.Count > 0 ? _werkzeuge.Max(w => w.Nr) : 0) + 1;
        foreach (var w in copied)
        {
            w.Nr        = nextNr++;
            w.Typ       = typ;
            w.Geaendert = DateTime.Now;
            _werkzeuge.Add(w);
        }
    }

    private void DeleteSelectedWerkzeuge(DataGrid grid)
    {
        foreach (var w in grid.SelectedItems.OfType<Werkzeug>().ToList())
            _werkzeuge.Remove(w);
    }

    private void OnWerkzeugInitializingNewItem(object sender, InitializingNewItemEventArgs e)
    {
        if (e.NewItem is not Werkzeug w) return;
        var grid = (DataGrid)sender;
        var typ  = TypFuer(grid);

        int nr = (_werkzeuge.Count > 0 ? _werkzeuge.Max(x => x.Nr) : 0) + 1;
        var tmpl = _werkzeuge.LastOrDefault(x => x.Typ == typ && !ReferenceEquals(x, w));
        w.Nr  = nr;
        w.Typ = typ;
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
            w.Eintauchwinkel = 20; w.VorschubFxy = 3000; w.VorschubFz = 2000;
            w.Drehzahl = 18000; w.RaeumzustellungXY = 75;
        }
    }

    private void OnWerkzeugCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;

        var item = e.Row.Item as Werkzeug;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
        {
            if (item != null) item.Geaendert = DateTime.Now;
            _save();
        });
    }

    private void OnWerkzeugGridRightClick(object sender, MouseButtonEventArgs e)
    {
        var grid = (DataGrid)sender;
        var dep  = (DependencyObject)e.OriginalSource;
        while (dep != null && dep is not DataGridRow)
            dep = VisualTreeHelper.GetParent(dep);

        if (dep is DataGridRow row && row.Item is Werkzeug w)
            grid.SelectedItem = w;
    }

    private void OnZeileHoch(object sender, RoutedEventArgs e)   => VerschiebeZeile(sender, -1);
    private void OnZeileRunter(object sender, RoutedEventArgs e) => VerschiebeZeile(sender, +1);

    private void VerschiebeZeile(object sender, int delta)
    {
        if (sender is not MenuItem { Parent: ContextMenu { PlacementTarget: DataGrid grid } }) return;
        if (grid.SelectedItem is not Werkzeug w) return;

        var typ    = w.Typ;
        var gruppe = _werkzeuge.Where(x => x.Typ == typ).ToList();
        int idx    = gruppe.IndexOf(w);
        int ziel   = idx + delta;
        if (ziel < 0 || ziel >= gruppe.Count) return;

        var other = gruppe[ziel];
        int i1 = _werkzeuge.IndexOf(w);
        int i2 = _werkzeuge.IndexOf(other);
        _werkzeuge.Move(i1, i2);

        int nr = 1;
        foreach (var x in _werkzeuge.Where(x => x.Typ == typ))
            x.Nr = nr++;

        grid.SelectedItem = w;
        _save();
    }

    private void OnEinzelSelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshBildVorschau();

    private void OnEinzelFeldGeaendert(object sender, RoutedEventArgs e)
    {
        if (LstWerkzeuge.SelectedItem is Werkzeug w) w.Geaendert = DateTime.Now;
        _save();
    }

    private static string WerkzeugBilderOrdner => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NCHops", "WerkzeugBilder");

    private void OnBildWaehlen(object sender, RoutedEventArgs e)
    {
        if (LstWerkzeuge.SelectedItem is not Werkzeug w) return;

        var dlg = new OpenFileDialog
        {
            Title  = "Bild auswählen",
            Filter = "Bilder|*.png;*.jpg;*.jpeg;*.bmp;*.gif"
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            Directory.CreateDirectory(WerkzeugBilderOrdner);
            var ziel = Path.Combine(WerkzeugBilderOrdner, $"{Guid.NewGuid():N}{Path.GetExtension(dlg.FileName)}");
            File.Copy(dlg.FileName, ziel, overwrite: true);

            var alt = w.BildPfad;
            w.BildPfad  = ziel;
            w.Geaendert = DateTime.Now;
            RefreshBildVorschau();
            _save();

            if (!string.IsNullOrEmpty(alt) && File.Exists(alt))
                try { File.Delete(alt); } catch { /* egal */ }
        }
        catch
        {
            MessageBox.Show(this, "Bild konnte nicht geladen werden.", "Fehler",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnBildEntfernen(object sender, RoutedEventArgs e)
    {
        if (LstWerkzeuge.SelectedItem is not Werkzeug w) return;

        if (!string.IsNullOrEmpty(w.BildPfad) && File.Exists(w.BildPfad))
            try { File.Delete(w.BildPfad); } catch { /* egal */ }

        w.BildPfad  = null;
        w.Geaendert = DateTime.Now;
        RefreshBildVorschau();
        _save();
    }

    private void RefreshBildVorschau()
    {
        ImgVorschau.Source = null;
        if (LstWerkzeuge.SelectedItem is not Werkzeug w) return;
        if (string.IsNullOrEmpty(w.BildPfad) || !File.Exists(w.BildPfad)) return;

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource   = new Uri(w.BildPfad, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            ImgVorschau.Source = bmp;
        }
        catch { /* Bilddatei defekt/unlesbar */ }
    }

    private void OnSchliessen(object sender, RoutedEventArgs e)
    {
        WerkzeugGridFraeser.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
        WerkzeugGridFraeser.CommitEdit(DataGridEditingUnit.Row,  exitEditingMode: true);
        WerkzeugGridBohrer.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
        WerkzeugGridBohrer.CommitEdit(DataGridEditingUnit.Row,  exitEditingMode: true);
        Close();
    }

    private Werkzeug? AktuelleAuswahl() => TabWerkzeuge.SelectedIndex switch
    {
        0 => WerkzeugGridFraeser.SelectedItem as Werkzeug,
        1 => WerkzeugGridBohrer.SelectedItem as Werkzeug,
        _ => LstWerkzeuge.SelectedItem as Werkzeug,
    };

    private void WaehleUndSchliesse(Werkzeug w)
    {
        Ausgewaehlt   = w;
        DialogResult  = true;
        Close();
    }

    private void OnWaehlen(object sender, RoutedEventArgs e)
    {
        if (AktuelleAuswahl() is Werkzeug w) WaehleUndSchliesse(w);
    }

    private void OnWerkzeugGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (!_waehlModus) return;
        if (sender is not DataGrid grid) return;

        var dep = (DependencyObject)e.OriginalSource;
        while (dep != null && dep is not DataGridRow)
            dep = VisualTreeHelper.GetParent(dep);
        if (dep is not DataGridRow) return;

        if (grid.SelectedItem is Werkzeug w) WaehleUndSchliesse(w);
    }

    private void OnEinzelListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (!_waehlModus) return;
        if (LstWerkzeuge.SelectedItem is Werkzeug w) WaehleUndSchliesse(w);
    }
}
