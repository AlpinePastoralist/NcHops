# Text-zu-Liniensegmente Konvertierung

## Übersicht

Die neue `TextToLineSegments`-Klasse konvertiert Text in Vektorliniensegmente für die CNC-Bearbeitung. Jeder Buchstabe wird als Ensemble von Linien gezeichnet, die **exakt an der gleichen Position und Größe** wie das Original bleiben.

## Funktionsweise

### Konvertierungsprozess

1. **Text-zu-Pfad**: Text wird mittels SkiaSharp in `SKPath`-Objekte konvertiert
2. **Kurven-Unterteilung**: Bézier-Kurven werden in kleine Liniensegmente unterteilt
   - Quadratische Kurven (Quad)
   - Kubische Kurven (Cubic)
   - Konische Kurven (Conic)
3. **Offset-Anwendung**: Liniensegmente werden mit X/Y-Offsets versetzt
4. **Export**: Segmente können als G-Code, Liste oder anderes exportiert werden

### Toleranz-Handling

Die `tolerance`-Parameter steuert die Kurven-Auflösung:
- **Kleine Toleranz** (z.B. 0.1): Mehr Liniensegmente, glattere Kurven
- **Große Toleranz** (z.B. 1.0): Weniger Liniensegmente, schneller

Empfohlen: `0.3 - 0.5` für gute Balance zwischen Genauigkeit und Performance.

## API-Übersicht

### Hauptfunktion: ConvertTextToLineSegments

```csharp
var geometries = TextToLineSegments.ConvertTextToLineSegments(
    text: "Hallo",           // Der zu konvertierende Text
    fontFamily: "Segoe UI",  // Schriftfamilie
    fontSize: 48f,           // Größe in Punkten
    startX: 0,              // Startposition X
    startY: 50,             // Startposition Y (Baseline)
    tolerance: 0.3f         // Kurven-Toleranz
);
```

**Rückgabe**: Liste von `CharacterGeometry`-Objekten

### CharacterGeometry-Struktur

```csharp
public class CharacterGeometry
{
    public string Character { get; set; }              // Der Buchstabe ('A', 'B', etc.)
    public float X { get; set; }                       // Position X
    public float Y { get; set; }                       // Position Y
    public float Width { get; set; }                   // Breite
    public float Height { get; set; }                  // Höhe
    public List<LineSegment> LineSegments { get; set; } // Die Liniensegmente
}
```

### LineSegment-Struktur

```csharp
public class LineSegment
{
    public float X1 { get; set; }  // Start-X
    public float Y1 { get; set; }  // Start-Y
    public float X2 { get; set; }  // End-X
    public float Y2 { get; set; }  // End-Y
}
```

## Verwendungsbeispiele

### Beispiel 1: Einfache Konvertierung

```csharp
var geometries = TextToLineSegments.ConvertTextToLineSegments(
    "ABC",
    "Arial",
    36f,
    0, 40
);

foreach (var charGeo in geometries)
{
    Console.WriteLine($"'{charGeo.Character}': {charGeo.LineSegments.Count} Segmente");
}
```

### Beispiel 2: Alle Segmente extrahieren

```csharp
var geometries = TextToLineSegments.ConvertTextToLineSegments(
    "Test",
    "Verdana",
    24f,
    10, 30
);

var allSegments = TextToLineSegments.ExtractAllLineSegments(geometries);
Console.WriteLine($"Gesamt: {allSegments.Count} Liniensegmente");
```

### Beispiel 3: G-Code generieren

```csharp
var geometries = TextToLineSegments.ConvertTextToLineSegments(
    "CNC",
    "Segoe UI",
    48f,
    0, 50
);

var segments = TextToLineSegments.ExtractAllLineSegments(geometries);

string gcode = TextToLineSegments.GenerateGCode(
    segments,
    feedRate: 50,      // mm/min
    safeZ: 5.0f,       // Sichere Z-Höhe
    workingZ: -2.0f,   // Arbeits-Z-Tiefe
    scale: 1.0f        // Skalierungsfaktor
);

File.WriteAllText("text_contours.gcode", gcode);
```

## Integration in TextEditorPropertiesWindow

Ein neuer Button wurde hinzugefügt:

### UI-Button

```xaml
<Button x:Name="ConvertToLineSegmentsBtn" 
        Content="Text zu Liniensegmenten" 
        Click="OnConvertToLineSegments" />
```

### Event-Handler

```csharp
private void OnConvertToLineSegments(object sender, RoutedEventArgs e)
{
    string text = _textEditor.GetText();
    
    var lineSegments = TextToLineSegments.ConvertTextToLineSegments(
        text: text,
        fontFamily: "Segoe UI",
        fontSize: 14f,
        startX: 0,
        startY: 20,
        tolerance: 0.3f
    );
    
    int totalSegments = TextToLineSegments.ExtractAllLineSegments(lineSegments).Count;
    ExportInfoText.Text = $"✓ Konvertiert!\n{lineSegments.Count} Zeichen\n{totalSegments} Segmente";
}
```

## Technische Details

### Kurven-Unterteilung

**Quadratische Bézier-Kurve** (2 Kontrollpunkte):
- Verwendung des De Casteljau-Algorithmus
- Rekursive Unterteilung bei Bedarf (max. 10 Tiefe)

**Kubische Bézier-Kurve** (3 Kontrollpunkte):
- Standard-Unterteilungsmethode
- Abstand-Check mit `DistancePointToLine()`

### Toleranz-Berechnung

```
Abstand = Distanz vom Kurvenpunkt zur Geraden
wenn Abstand > Toleranz:
    unterteile Kurve weiter
sonst:
    verwende gerade Linie
```

### G-Code-Generierung

Für jedes Liniensegment:
1. Schnelles Verfahren zum Start-Punkt: `G00 X{x} Y{y}`
2. Lineares Verfahren zum End-Punkt: `G01 X{x} Y{y} F{feedrate}`

## Performance-Tipps

| Einstellung | Effekt | Empfehlung |
|-------------|--------|------------|
| Größere Schrift | Weniger Segmente | Für Details groß halten |
| Kleinere Toleranz | Mehr Segmente | 0.3 - 0.5 optimal |
| Einfache Schrift | Weniger Segmente | Verwende Standard-Fonts |
| Komplexe Glyphen | Mehr Segmente | Sans-Serif besser als Serif |

## Bekannte Einschränkungen

1. **Schlagschatten/Effekte**: Nicht unterstützt (nur Base-Glyphen)
2. **Variable Fonts**: Werden als Standard-Weight behandelt
3. **Ligaturen**: Müssen manuell erweitert werden
4. **CJK-Text**: Funktioniert, aber sehr viele Segmente

## Zukünftige Erweiterungen

- [ ] Höhen-Offset für Grat-Fräsung
- [ ] Outline-Dicke (für gravierte Linien)
- [ ] Automatische Schnittoptimierung (Pen-Lifting)
- [ ] Export zu verschiedenen Formaten (DXF, SVG)
- [ ] Font-Rendering-Optionen (anti-aliasing, etc.)

## Beispiel: Text im TextEditor konvertieren

1. Öffnen Sie **TextEditorPropertiesWindow**
2. Geben Sie Text im Textfeld ein
3. Klicken Sie auf **"Text zu Liniensegmenten"**
4. Die Statuszeile zeigt: `✓ Konvertiert! X Zeichen, Y Segmente`
5. Die Liniensegmente sind jetzt für weitere Bearbeitung verfügbar

