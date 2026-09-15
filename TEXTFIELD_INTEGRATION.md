# Text-zu-Liniensegmente Integration ins Textfeld-Werkzeug (VCarveTextSk)

## Übersicht

Die `TextToLineSegments`-Klasse mit Formatierungsunterstützung (`CharacterFormat`) kann jetzt direkt ins **VCarveTextSk-Werkzeug** (Textfeld-Werkzeug) in MainWindow integriert werden.

## Verfügbare Klassen

### TextToLineSegments.cs
- `ConvertTextToLineSegments()` - Einfache Konvertierung ohne Formatierung
- `ConvertTextToLineSegmentsWithFormat()` - Formatierte Konvertierung mit verschiedenen Stilen pro Abschnitt

### CharacterFormat.cs
Definiert die Formatierung für Textabschnitte:
```csharp
public class CharacterFormat
{
    public string FontFamily { get; set; } = "Segoe UI";
    public float FontSize { get; set; } = 12f;
    public SKColor Color { get; set; } = SKColors.Black;
    public bool Bold { get; set; } = false;
    public bool Italic { get; set; } = false;
    public float Tracking { get; set; } = 0f;
    public float ScaleX { get; set; } = 1.0f;
    public float BaselineOffsetY { get; set; } = 0f;
    // ... weitere Eigenschaften
}
```

## Integration in MainWindow VCarveTextSk-Werkzeug

### Schritt 1: Text-zu-Linien-Button hinzufügen

Im VCarveTextSk-Werkzeug (wenn Text platziert wurde) einen Button hinzufügen:
```csharp
"Text zu Liniensegmenten konvertieren"
```

### Schritt 2: Conversion durchführen

```csharp
// Hole Text aus dem Textfeld-Werkzeug
string textContent = GetCurrentTextContent();
string fontFamily = GetCurrentFontFamily();  // z.B. "Segoe UI"
float fontSize = GetCurrentFontSize();       // z.B. 24.0f

// Konvertiere zu Liniensegmenten
var geometries = TextToLineSegments.ConvertTextToLineSegments(
    text: textContent,
    fontFamily: fontFamily,
    fontSize: fontSize,
    startX: currentX,
    startY: currentY,
    tolerance: 0.3f  // Kurven-Genauigkeit
);

// Extrahiere alle Segmente
var allSegments = TextToLineSegments.ExtractAllLineSegments(geometries);
```

### Schritt 3: Geometrie ins Projekt übernehmen

```csharp
// Konvertiere Liniensegmente zu Projekt-Geometrie
var pathGeometry = ConvertSegmentsToPathGeometry(allSegments);

// Füge zur Zeichnungsgeometrie hinzu
_geoObjects.Add(new DrawingObject 
{
    Geometry = pathGeometry,
    Type = ObjectType.Path,
    // ... weitere Eigenschaften
});

// Aktualisiere Canvas
DrawSkia?.InvalidateVisual();
```

## Formatierte Text-Konvertierung

### Beispiel: Titel mit verschiedenen Schriftgrößen

```csharp
string text = "Mein Projekt";

var formats = new List<(int start, int end, CharacterFormat format)>
{
    // "Mein " - groß
    (0, 5, new CharacterFormat { 
        FontFamily = "Segoe UI", 
        FontSize = 36f 
    }),
    // "Projekt" - normal
    (5, 12, new CharacterFormat { 
        FontFamily = "Segoe UI", 
        FontSize = 24f 
    }),
};

var geometries = TextToLineSegments.ConvertTextToLineSegmentsWithFormat(
    text, formats, 0, 40, 0.3f
);
```

### Beispiel: Wissenschaftliche Formel

```csharp
string formula = "E = mc2";

var formats = new List<(int start, int end, CharacterFormat format)>
{
    (0, 4, new CharacterFormat { FontSize = 32f }),           // "E = m"
    (4, 5, new CharacterFormat { FontSize = 32f, Italic = true }), // "c"
    (5, 6, new CharacterFormat {                               // "2" Superscript
        FontSize = 16f,
        BaselineOffsetY = -8f  // Nach oben versetzt
    }),
};

var geometries = TextToLineSegments.ConvertTextToLineSegmentsWithFormat(
    formula, formats, 0, 40, 0.3f
);
```

## Integration in DrawSkia-Rendering

Um Liniensegmente live in der Canvas zu visualisieren:

```csharp
// In DrawSkia.OnPaintSurface() oder ähnlich:
foreach (var segment in lineSegments)
{
    canvas.DrawLine(
        segment.X1, segment.Y1,
        segment.X2, segment.Y2,
        strokePaint
    );
}
```

## G-Code Export

Die konvertierten Liniensegmente können direkt zu G-Code exportiert werden:

```csharp
string gcode = TextToLineSegments.GenerateGCode(
    segments,
    feedRate: 50,
    safeZ: 5.0f,
    workingZ: -2.0f,
    scale: 1.0f
);

File.WriteAllText("text_output.gcode", gcode);
```

## Toleranz-Einstellungen

Die `tolerance`-Parameter bei der Konvertierung steuert die Kurvenauflösung:

```
tolerance = 0.1   → Sehr glatt, viele Segmente, langsamer
tolerance = 0.3   → Empfohlen, gutes Gleichgewicht
tolerance = 0.5   → Weniger Segmente, schneller
tolerance = 1.0   → Grob, sehr wenige Segmente
```

## Workflow im Textfeld-Werkzeug

1. **Text eingeben**: Benutzer gibt Text im Textfeld ein (oder wählt existierenden Text)
2. **Formatierung wählen**: Optional Schriftart, Größe, Stil wählen
3. **"Zu Linien konvertieren" klicken**: Startet die Konvertierung
4. **Ergebnis visualisieren**: Liniensegmente werden in der Canvas angezeigt
5. **Bearbeiten**: Segmente wie normale Pfade bearbeiten
6. **Exportieren**: Als G-Code oder in andere Formate exportieren

## Tipps für die Implementierung

### Performance-Optimierung
- Für große Texte: `tolerance` erhöhen (z.B. 0.5-1.0)
- Komplexe Schriftarten (Serif) können viele Segmente erzeugen
- Sans-Serif Schriften sind effizienter

### Formatierungsflexibilität
- Unterstütze Rich-Text-Editor im Textfeld (verschiedene Formate pro Abschnitt)
- Ermögliche Speichern/Laden von Formatierungsinformationen
- Erlauben Sie Superscript/Subscript via UI

### Benutzerfreundlichkeit
- Zeige Segmentanzahl vor/nach Konvertierung
- Ermögliche Undo/Redo für Konvertierung
- Visuelles Feedback während der Konvertierung

## Zusammenfassung

Die Text-zu-Liniensegmente-Konvertierung ist jetzt vollständig formatierungsgerecht und kann direkt ins MainWindow Textfeld-Werkzeug integriert werden. Mit der `CharacterFormat`-Klasse können Benutzer:

✓ Verschiedene Schriftarten pro Abschnitt verwenden
✓ Unterschiedliche Schriftgrößen mischen
✓ Superscript/Subscript-Effekte erzeugen
✓ Buchstabenabstand und Skalierung anpassen
✓ Farben für Visualisierung verwenden

Alles unter Beibehaltung der exakten Position und Größe! 🎨

