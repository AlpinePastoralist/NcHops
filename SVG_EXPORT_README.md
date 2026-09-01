# 📄 SVG Export Feature für NC Studio

Eine umfassende Lösung zum Exportieren von **G-Code Werkzeugbahnen** und **Text-Konturlinien** als **SVG-Dateien**.

## 🎯 Funktionen

### ✅ Was es macht

- **Text-Konturlinien** als SVG exportieren (mit SkiaSharp)
- **G-Code Pfade** visualisieren (parsed und als Linien gezeichnet)
- **Kombinierte SVG** mit Text + Werkzeugbahn
- **Werkstück-Grenzen** anzeigen
- **Metadaten** speichern (Schriftart, Größe, Werkzeug, etc.)
- **Farbcodierte Visualisierung** (Text = Blau, G-Code = Rot)

### 📋 Unterstützte Formate

- **Input:** G-Code (.gcode, .nc, .txt), Text-Parameter
- **Output:** SVG 1.1 (XML-basiert, überall lesbar)
- **Browser:** Chrome, Firefox, Safari, Edge
- **Anwendungen:** Inkscape, Adobe Illustrator, CorelDRAW
- **Online:** CloudConvert, Convertio, etc.

## 📦 Komponenten

### Neue Dateien

```
SvgExporter.cs (Hauptklasse)
├── ExportTextAndGCode()       → Text + optional G-Code
├── ExportGCodePaths()         → Nur G-Code Pfade
├── ExportCombined()           → Beides kombiniert
├── ParseGCodePaths()          → Parser für G-Code
└── ConvertSkPathToSvgPath()   → SkiaSharp zu SVG

SvgExporterExample.cs (5 Beispiele)
├── ExportTextToSvg()
├── ExportGCodeToSvg()
├── ExportCombinedToSvg()
├── ExportCompleteJob()
└── BatchExportTexts()

SvgExporterTests.cs (11 Unit Tests)
├── Basis-Funktionalität
├── G-Code Parsing
├── Fehlerbehandlung
└── Edge Cases

Dokumentation
├── SVG_EXPORT_README.md        (diese Datei)
├── SVG_EXPORT_GUIDE.md         (detaillierte Anleitung)
└── SVG_EXPORT_INSTALLATION.md  (Setup & Integration)
```

## 🚀 Quick Start

### 1. Installation

```bash
# Kopieren Sie die Dateien in das Projekt
cp SvgExporter.cs /path/to/NcHops/
cp SvgExporterExample.cs /path/to/NcHops/
cp SvgExporterTests.cs /path/to/NcHops/
```

### 2. Basic Usage

```csharp
using NCHops;

// Erstellen Sie Text-Parameter
var textParams = new GraviereParams(
    Text: "Hello",
    FontFamily: "Arial",
    FontSizeMm: 10,
    XRel: 10, YRel: 10,
    TextBreite: 100, TextHoehe: 30,
    ZTiefe: -2,
    SchneidenWinkel: 60,
    FraeserD: 0.3,
    Vorschub: 100,
    Drehzahl: 20000,
    Bezugspunkt: "Unten links"
);

// Exportieren Sie als SVG
SvgExporter.ExportTextAndGCode(
    "output.svg",
    textParams,
    gCode: null,  // oder G-Code String
    workWidth: 200,
    workHeight: 100
);
```

### 3. Mit G-Code

```csharp
string gCode = @"
G00 Z5
G00 X10 Y10
G01 Z-2 F100
G01 X20 Y20 F100
G00 Z5
";

SvgExporter.ExportCombined(
    "output.svg",
    gCode,
    textParams,
    200, 100
);
```

## 📊 SVG Output Beispiel

```xml
<svg xmlns="http://www.w3.org/2000/svg" width="240" height="140">
  <defs>
    <style>/* Styles */</style>
  </defs>
  
  <!-- Werkstück -->
  <rect class="workpiece" x="20" y="20" width="200" height="100"/>
  
  <!-- Text-Konturlinien -->
  <g id="text-contours" class="text-layer">
    <path d="M 30 30 L 40 40 Q 50 50 60 40 Z"/>
    <text>Hello</text>
  </g>
  
  <!-- G-Code Pfade -->
  <g id="gcode-paths" class="gcode-layer">
    <path d="M 10 10 L 20 20 L 30 30"/>
  </g>
  
  <!-- Informationen -->
  <g id="info">
    <text>Text: Hello</text>
    <text>Font: Arial</text>
    <text>Size: 10 pt</text>
  </g>
</svg>
```

## 🔧 API Referenz

### Hauptfunktionen

#### ExportTextAndGCode()
```csharp
public static void ExportTextAndGCode(
    string filePath,
    GraviereParams textParams,
    string? gCode,
    double workWidth,
    double workHeight)
```
Exportiert Text-Konturlinien und optional G-Code Pfade.

#### ExportGCodePaths()
```csharp
public static void ExportGCodePaths(
    string filePath,
    string gCode,
    double workWidth,
    double workHeight,
    string? title = null)
```
Exportiert nur G-Code als Pfade.

#### ExportCombined()
```csharp
public static void ExportCombined(
    string filePath,
    string gCode,
    GraviereParams textParams,
    double workWidth,
    double workHeight)
```
Kombiniert Text und G-Code in einer SVG.

## 📈 Use Cases

### 1. **Preview vor dem Fräsen**
Visualisieren Sie die Werkzeugbahn bevor Sie fräsen.

### 2. **Dokumentation**
Exportieren Sie SVGs zur Dokumentation Ihrer Projekte.

### 3. **Kundenpräsentation**
Zeigen Sie Kunden die genaue Gravur in hoher Qualität.

### 4. **CAM Integration**
Importieren Sie SVGs in andere CAM-Programme (Fusion 360, SolidCAM, etc.)

### 5. **Batch Processing**
Exportieren Sie mehrere Gravuren automatisch.

## 🎨 Styling & Anpassung

### Standardfarben

| Element | Farbe | RGB |
|---------|-------|-----|
| Werkstück | #333 | Grau |
| Text | #0066cc | Blau |
| G-Code | #cc0000 | Rot |
| Info | #666 | Hellgrau |

### Ändern Sie die Farben

In `SvgExporter.cs` -> `AddStyleDefs()`:

```csharp
.text-layer path {
    stroke: #00AA00;  /* Grün statt Blau */
}
.gcode-layer path {
    stroke: #0000FF;  /* Blau statt Rot */
}
```

## 🧪 Testing

### Unit Tests ausführen

```bash
dotnet test SvgExporterTests.cs
```

### Erwartetes Ergebnis

```
11 passed (5.234s)
```

### Tests decken ab:

- ✅ Basis-Funktionalität
- ✅ G-Code Parsing
- ✅ Fehlerbehandlung
- ✅ Edge Cases
- ✅ UTF-8 Encoding
- ✅ Große/kleine Dimensionen

## 📚 Dokumentation

| Datei | Inhalt |
|-------|--------|
| **SVG_EXPORT_README.md** | Übersicht (Sie lesen sie gerade) |
| **SVG_EXPORT_GUIDE.md** | Detaillierte Anleitung mit Beispielen |
| **SVG_EXPORT_INSTALLATION.md** | Setup, Integration, Debugging |
| **SvgExporterExample.cs** | 5 praktische Codebeispiele |
| **SvgExporterTests.cs** | 11 Unit Tests |

## ⚙️ Konfiguration

### Margin (Rand)

```csharp
double margin = 20;  // Standard: 20mm
```

### Werkstück-Größe

```csharp
SvgExporter.ExportTextAndGCode(
    "output.svg",
    textParams,
    gCode,
    workWidth: 300,   // Ihre Werkstückbreite
    workHeight: 200   // Ihre Werkstückhöhe
);
```

### Schriftart & Größe

```csharp
var textParams = new GraviereParams(
    FontFamily: "Times New Roman",  // Andere Schriftart
    FontSizeMm: 15,                 // Größer
    // ...
);
```

## 🐛 Troubleshooting

### Problem: SVG zeigt nichts an

**Lösung:** 
- Überprüfen Sie, dass der Viewer SVG unterstützt
- Öffnen Sie mit Chrome/Firefox/Inkscape
- Prüfen Sie die Konsolenausgabe auf Fehler

### Problem: Text-Konturlinien leer

**Lösung:**
- Schriftart muss auf System installiert sein
- FontSize muss > 0 sein
- Text darf nicht leer sein

### Problem: G-Code Pfade fehlen

**Lösung:**
- G-Code Format muss korrekt sein
- X/Y Werte müssen vorhanden sein
- Prüfen Sie die Margin-Größe

## 📝 Lizenz

Dieses Feature ist Teil von NC Studio und folgt der gleichen Lizenz.

## 🔄 Version

**Version:** 1.0  
**Status:** Production Ready  
**Getestet:** Windows 10, .NET 6+  

## 🎉 Features Roadmap

### ✅ v1.0 (Aktuell)
- Text Export
- G-Code Parse & Visualisierung
- Kombinierter Export
- SVG Styling
- Unit Tests

### 🔜 v1.1 (Geplant)
- DXF Export
- PDF Export (WebGL)
- 3D-Vorschau
- Animierte Werkzeugbahn
- Custom Color Schemes

### 🔜 v2.0
- Multi-file Export
- Nested Text/Pfade
- Simulation
- Real-time Preview

## 🤝 Integration in UI

### Menü-Option hinzufügen

```xml
<!-- MainWindow.xaml -->
<MenuItem Header="Datei">
    <MenuItem Header="Als SVG exportieren..." 
              Click="OnExportSvgClick"/>
</MenuItem>
```

### Button in Toolbar

```xml
<Button Content="📄 SVG Export" 
        Click="OnExportSvgClick"
        ToolTip="Exportiert G-Code und Text als SVG"/>
```

## 📞 Support

Bei Fragen oder Problemen:

1. Lesen Sie die Dokumentation (`SVG_EXPORT_GUIDE.md`)
2. Schauen Sie sich die Beispiele an (`SvgExporterExample.cs`)
3. Führen Sie die Tests aus (`SvgExporterTests.cs`)
4. Überprüfen Sie das Debugging-Kapitel

## 🙏 Credits

Entwickelt für NC Studio
- SkiaSharp für Text-Konturlinien
- System.Xml.Linq für XML-Verarbeitung
- xUnit für Unit Testing

---

**Viel Spaß beim Exportieren! 🎉**

Für mehr Informationen siehe:
- 📖 [SVG Export Guide](SVG_EXPORT_GUIDE.md)
- 🚀 [Installation & Setup](SVG_EXPORT_INSTALLATION.md)
- 💻 [Code Beispiele](SvgExporterExample.cs)
- 🧪 [Unit Tests](SvgExporterTests.cs)
