# 🚀 SVG Export - Quick Reference Card

## Die 3 Hauptfunktionen

### 1️⃣ Text exportieren
```csharp
SvgExporter.ExportTextAndGCode(
    "out.svg",              // Dateiname
    textParams,             // GraviereParams
    null,                   // kein G-Code
    200, 100                // Werkstück-Größe
);
```

### 2️⃣ G-Code exportieren
```csharp
SvgExporter.ExportGCodePaths(
    "out.svg",              // Dateiname
    gCodeString,            // G-Code
    200, 100,               // Werkstück-Größe
    "Titel"                 // Optional
);
```

### 3️⃣ Kombiniert exportieren
```csharp
SvgExporter.ExportCombined(
    "out.svg",              // Dateiname
    gCodeString,            // G-Code
    textParams,             // Text-Parameter
    200, 100                // Werkstück-Größe
);
```

## Text-Parameter erstellen

```csharp
var textParams = new GraviereParams(
    Text: "Hello",                    // 📝 Text
    FontFamily: "Arial",              // 🔤 Schriftart
    FontSizeMm: 10,                   // 📏 Größe
    XRel: 10, YRel: 10,              // 📍 Position
    TextBreite: 100, TextHoehe: 30,   // 📐 Box
    ZTiefe: -2,                       // 🔻 Tiefe
    SchneidenWinkel: 60,              // 🎯 Angle
    FraeserD: 0.3,                    // 🔧 Tool
    Vorschub: 100,                    // ⚡ Feed
    Drehzahl: 20000,                  // 🔄 Speed
    Bezugspunkt: "Unten links"        // 📌 Anchor
);
```

## Häufige Fehler & Lösungen

| Problem | Lösung |
|---------|--------|
| SVG ist leer | Text/FontSize/Dims überprüfen |
| Schriftart error | Font auf System installiert? |
| Datei nicht erstellt | Pfad/Permissions überprüfen |
| G-Code wird ignoriert | Gcode != null setzen |

## Farben im Output

| Element | Farbe | Code |
|---------|-------|------|
| Text | 🔵 Blau | `#0066cc` |
| G-Code | 🔴 Rot | `#cc0000` |
| Werkstück | ⚫ Grau | `#333` |

## Unit Tests

```bash
# Alle Tests
dotnet test

# Nur SVG Tests
dotnet test --filter "SvgExporter"

# Mit Details
dotnet test -v d
```

## SVG öffnen mit

- 💻 **Browser:** Chrome, Firefox, Safari, Edge
- 🎨 **Design:** Inkscape (kostenlos), Illustrator
- 📋 **Online:** cloudconvert.com
- 🔧 **CAM:** Fusion 360, SolidCAM, etc.

## Speicherpfade

```csharp
// Desktop
var path = Path.Combine(
    Environment.GetFolderPath(
        Environment.SpecialFolder.Desktop),
    "export.svg"
);

// Dokumente
var path = Path.Combine(
    Environment.GetFolderPath(
        Environment.SpecialFolder.MyDocuments),
    "export.svg"
);

// Projektverzeichnis
var path = "export.svg";  // Relativ zum Projekt
```

## Save-Dialog Integration

```csharp
var dlg = new SaveFileDialog
{
    Filter = "SVG Files (*.svg)|*.svg|All|*.*",
    DefaultExt = ".svg",
    FileName = $"export-{DateTime.Now:yyyy-MM-dd}.svg"
};

if (dlg.ShowDialog() == true)
{
    SvgExporter.ExportCombined(
        dlg.FileName, gCode, textParams, 200, 100);
}
```

## G-Code Format

```gcode
; Kommentar
G00 Z5          ; Eilgang
G00 X10 Y10     ; Punkt anfahren
G01 Z-2 F100    ; Schnitt
G01 X20 Y20     ; Linie
G02 X30 I5 J0   ; Bogen CW
G03 X40 I5 J0   ; Bogen CCW
G00 Z5          ; Hochfahren
```

## Batch-Export

```csharp
var jobs = new[] {
    ("Text1", gcode1),
    ("Text2", gcode2),
    ("Text3", gcode3),
};

SvgExporterExample.BatchExportTexts(
    "C:\\exports", jobs);
```

## Performance

| Operation | Zeit |
|-----------|------|
| Text-Export | ~100ms |
| GCode-Parse | ~50ms |
| SVG-Render | ~200ms |
| **Total** | ~**350ms** |

## Namespace

```csharp
using NCHops;
using System.IO;
using Microsoft.Win32;
```

## Margin ändern

```csharp
// In SvgExporter.cs ändern:
double margin = 30;  // Statt 20
```

## Typische Werkstück-Größen

```csharp
// Klein
workWidth: 50, workHeight: 50

// Mittel (Standard)
workWidth: 200, workHeight: 100

// Groß
workWidth: 500, workHeight: 300

// Format A4
workWidth: 210, workHeight: 297
```

## Debugging

```csharp
try
{
    SvgExporter.ExportCombined(...);
    Debug.WriteLine("✓ Export erfolgreich");
}
catch (FileNotFoundException ex)
{
    Debug.WriteLine($"✗ Fehler: {ex.Message}");
}
```

## Checkliste vor Export

- ✅ Text nicht leer
- ✅ Schriftart verfügbar
- ✅ FontSize > 0
- ✅ Werkstück-Größe > 0
- ✅ Zielverzeichnis existiert
- ✅ Schreibberechtigung vorhanden

## Weiterführende Links

- 📖 [Vollständiges Guide](SVG_EXPORT_GUIDE.md)
- 🚀 [Installation](SVG_EXPORT_INSTALLATION.md)
- 📖 [README](SVG_EXPORT_README.md)
- 💻 [Code Beispiele](SvgExporterExample.cs)

## Tastaturkürzel (optional)

```csharp
// Ctrl+Shift+E für SVG Export
PreviewKeyDown += (s, e) =>
{
    if (e.Key == Key.E && 
        Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
    {
        OnExportSvgClick();
    }
};
```

---

**🎯 Tipp:** Speichern Sie diese Karte als Lesezeichen für schnellen Zugriff!

```
SvgExporter.ExportCombined(file, gcode, textParams, 200, 100);
```

**Das ist alles, was Sie brauchen! 🎉**
