# SVG Export Guide - G-Code & Text Contours

## Übersicht

Mit dem neuen `SvgExporter` können Sie:
- **Text-Konturlinien** als SVG visualisieren
- **G-Code Werkzeugbahnen** als SVG-Pfade darstellen
- **Beide kombiniert** in einer SVG-Datei exportieren
- **Werkstück-Grenzen** anzeigen
- **Metadaten** speichern

## Installation & Integration

### 1. Voraussetzungen
Die folgenden NuGet-Pakete müssen bereits installiert sein:
- `SkiaSharp` (für Text-Konturlinien)
- `System.Xml.Linq` (für XML-Verarbeitung)

### 2. Datei-Strukturen
Die neue Funktionalität besteht aus drei Dateien:

| Datei | Beschreibung |
|-------|-------------|
| `SvgExporter.cs` | Hauptklasse mit Export-Funktionen |
| `SvgExporterExample.cs` | Beispiele für verschiedene Use-Cases |
| `SVG_EXPORT_GUIDE.md` | Diese Dokumentation |

## API-Referenz

### Hauptfunktionen

#### 1. Text mit Konturlinien exportieren
```csharp
SvgExporter.ExportTextAndGCode(
    filePath: "output.svg",
    textParams: graviereParams,
    gCode: null,  // oder G-Code String
    workWidth: 200.0,
    workHeight: 100.0
);
```

**Parameter:**
- `filePath` (string): Ziel-Dateipfad für die SVG
- `textParams` (GraviereParams): Text-Parameter
- `gCode` (string | null): Optional: G-Code zur Visualisierung
- `workWidth` (double): Werkstück-Breite in mm
- `workHeight` (double): Werkstück-Höhe in mm

#### 2. Nur G-Code Pfade exportieren
```csharp
SvgExporter.ExportGCodePaths(
    filePath: "gcode.svg",
    gCode: gCodeString,
    workWidth: 200.0,
    workHeight: 100.0,
    title: "Gravuren-Werkzeugbahn"
);
```

#### 3. Text + G-Code kombiniert
```csharp
SvgExporter.ExportCombined(
    filePath: "combined.svg",
    gCode: gCodeString,
    textParams: graviereParams,
    workWidth: 200.0,
    workHeight: 100.0
);
```

## Verwendungsbeispiele

### Beispiel 1: Basic Text Export

```csharp
var textParams = new GraviereParams(
    Text: "Hello",
    FontFamily: "Arial",
    FontSizeMm: 10.0,
    XRel: 10.0,
    YRel: 10.0,
    TextBreite: 100.0,
    TextHoehe: 30.0,
    ZTiefe: -2.0,
    SchneidenWinkel: 60.0,
    FraeserD: 0.3,
    Vorschub: 100.0,
    Drehzahl: 20000.0,
    Bezugspunkt: "Unten links"
);

SvgExporter.ExportTextAndGCode(
    "hello.svg",
    textParams,
    null,
    200.0,
    100.0
);
```

### Beispiel 2: G-Code Visualisierung

```csharp
string gCode = @"
(Gravur)
G00 Z5.0000
G00 X10.0000 Y10.0000
G01 Z-2.0000 F100
G01 X20.0000 F100
G01 Y20.0000 F100
G00 Z5.0000
";

SvgExporter.ExportGCodePaths(
    "toolpath.svg",
    gCode,
    200.0,
    100.0,
    title: "Werkzeugbahn"
);
```

### Beispiel 3: In MainWindow Button integrieren

```csharp
// In MainWindow.xaml Code-Behind:
private void OnExportSvgClick(object sender, RoutedEventArgs e)
{
    var saveDialog = new SaveFileDialog
    {
        Filter = "SVG Files (*.svg)|*.svg",
        DefaultExt = ".svg",
        FileName = $"export-{DateTime.Now:yyyy-MM-dd-HHmmss}.svg"
    };

    if (saveDialog.ShowDialog() != true)
        return;

    try
    {
        // Hier müssen Sie die aktuellen Parameter aus Ihrer UI holen
        SvgExporter.ExportCombined(
            saveDialog.FileName,
            gcodeBoxText,  // G-Code aus Editor
            currentGraviereParams,
            workWidth: 200.0,
            workHeight: 100.0
        );

        MessageBox.Show(
            "SVG erfolgreich exportiert!",
            "Export",
            MessageBoxButton.OK,
            MessageBoxImage.Information
        );
    }
    catch (Exception ex)
    {
        MessageBox.Show(
            $"Fehler: {ex.Message}",
            "Export-Fehler",
            MessageBoxButton.OK,
            MessageBoxImage.Error
        );
    }
}
```

## SVG-Struktur

Die generierte SVG-Datei hat folgende Struktur:

```xml
<svg xmlns="http://www.w3.org/2000/svg" width="240" height="140">
  <defs>
    <style>
      /* Stil-Definitionen */
    </style>
  </defs>
  
  <!-- Werkstück-Grenzen -->
  <rect class="workpiece" x="20" y="20" width="200" height="100"/>
  
  <!-- Text-Konturlinien -->
  <g id="text-contours" class="text-layer">
    <path d="M ... L ... Q ..."/>
    <text>Textlabel</text>
  </g>
  
  <!-- G-Code Pfade -->
  <g id="gcode-paths" class="gcode-layer">
    <path d="M ... L ..."/>
  </g>
  
  <!-- Metadaten -->
  <g id="info">
    <text>Text: ...</text>
    <text>Font: ...</text>
    <!-- etc. -->
  </g>
</svg>
```

### Farben und Stile

| Element | Farbe | Stil |
|---------|-------|------|
| Werkstück-Grenzen | #333 (grau) | Gestrichelt |
| Text-Konturlinien | #0066cc (blau) | Durchgezogen |
| G-Code Pfade | #cc0000 (rot) | Halbdurchsichtig |
| Text-Label | #0066cc (blau) | Beschriftung |
| Info-Text | #666 (hellgrau) | Kleine Schrift |

## G-Code Parsing

Der Export-Funktion parst automatisch den G-Code und extrahiert:

- **G00**: Eilgang (schnelle Bewegung) → neue Werkzeugbahn
- **G01**: Linearer Schnitt → wird als Linie gezeichnet
- **G02/G03**: Bogenschnitte → werden als Linienfolge angenähert
- **X/Y/Z Werte**: Koordinaten werden extrahiert und konvertiert

### Unterstützte G-Code Befehle

```
G00 X10 Y10       # Eilgang
G01 X20 Y20 F100  # Linearer Schnitt mit Vorschub
G02 X30 Y30 I5 J0 # Bogenschnitt (CW)
G03 X40 Y40 I5 J0 # Bogenschnitt (CCW)
Z-2.0             # Tiefe
```

## Text-Konturlinien

Die Text-Konturlinien werden mit SkiaSharp generiert:

1. **Font-Rendering**: Der Text wird mit der angegebenen Schriftart gerendert
2. **Path-Extraktion**: Die Konturlinien werden als Bezier-Kurven extrahiert
3. **SVG-Konvertierung**: Die Kurven werden in SVG Path-Format konvertiert

### Unterstützte Kurven-Typen

- **M** (Move): Startpunkt
- **L** (Line): Gerade Linie
- **Q** (Quadratic Bézier): Quadratische Kurve
- **C** (Cubic Bézier): Kubische Kurve
- **Z** (Close): Pfad schließen

## Konfiguration & Anpassung

### Custom Styles

Sie können die SVG-Stile anpassen, indem Sie `SvgExporter.AddStyleDefs` modifizieren:

```csharp
.text-layer path {
    stroke: #0066cc;        /* Farbe ändern */
    stroke-width: 2;         /* Dicke ändern */
    stroke-linecap: round;
    stroke-linejoin: round;
}
```

### Margin anpassen

Der Standard-Margin beträgt 20mm. Sie können dies ändern:

```csharp
double margin = 30;  // Statt 20
```

## Performance-Tipps

1. **Große Dateien**: Für sehr große G-Code Dateien (>10MB) reduzieren Sie die Anzahl der Punkte
2. **Text-Konturlinien**: SkiaSharp kann bei sehr großen Fonts langsam sein
3. **Batch-Export**: Verwenden Sie die Batch-Export Funktion für mehrere Dateien

## Fehlerbehandlung

```csharp
try
{
    SvgExporter.ExportCombined(...);
}
catch (FileNotFoundException)
{
    MessageBox.Show("Datei nicht gefunden");
}
catch (UnauthorizedAccessException)
{
    MessageBox.Show("Keine Berechtigung zum Schreiben");
}
catch (Exception ex)
{
    MessageBox.Show($"Fehler: {ex.Message}");
}
```

## Integration in UI

### Menü-Item hinzufügen

In `MainWindow.xaml`:
```xml
<MenuItem Header="Export">
    <MenuItem Header="Als SVG exportieren..." Click="OnExportSvgClick"/>
</MenuItem>
```

### Toolbar-Button

```xml
<Button 
    Content="📄 SVG Export" 
    Click="OnExportSvgClick"
    ToolTip="Exportiert G-Code und Text-Konturlinien als SVG"/>
```

## Troubleshooting

### Problem: SVG wird nicht angezeigt
**Lösung**: Überprüfen Sie, ob der Viewer SVG unterstützt (Chrome, Firefox, Inkscape, Adobe Illustrator)

### Problem: Text-Konturlinien sind leer
**Lösung**: Stellen Sie sicher, dass:
- Die Schriftart auf dem System installiert ist
- FontSizeMm > 0
- Text nicht leer ist

### Problem: G-Code Pfade sind leer
**Lösung**: Überprüfen Sie:
- G-Code Format ist korrekt
- X/Y Werte sind vorhanden
- Margin ist nicht größer als Werkstück

## Weitere Ressourcen

- [SVG Spezifikation](https://developer.mozilla.org/en-US/docs/Web/SVG)
- [G-Code Referenz](https://en.wikipedia.org/wiki/G-code)
- [SkiaSharp Dokumentation](https://learn.microsoft.com/en-us/dotnet/api/skiasharp)

## Lizenz & Verwendung

Diese Klasse ist Teil von NC Studio und folgt derselben Lizenz.

## Changelog

### Version 1.0
- ✅ Text-Konturlinien Export
- ✅ G-Code Pfad-Visualisierung
- ✅ Kombinierter Export
- ✅ SVG-Styling
- ✅ Metadaten-Export

### Geplante Features
- 🔜 Druckoptimierte Ausgabe
- 🔜 DXF Export (Alternative zu SVG)
- 🔜 3D Preview (als WebGL)
- 🔜 Animierte Werkzeugbahn
