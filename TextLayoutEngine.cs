using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace NCHops;

/// <summary>
/// Horizontal-Alignment Optionen (wie InDesign)
/// </summary>
public enum TextHorizontalAlign
{
    Left,
    Center,
    Right,
    Justify // später, optional
}

/// <summary>
/// Vertikal-Alignment Optionen
/// </summary>
public enum TextVerticalAlign
{
    Top,
    Center,
    Bottom
}

/// <summary>
/// Repräsentiert eine Zeile nach Layout-Berechnung
/// </summary>
public class TextLine
{
    public int StartCharIdx { get; set; }
    public int EndCharIdx { get; set; }        // exclusive
    public float Width { get; set; }
    public float Height { get; set; }
    public float Ascent { get; set; }
    public float Descent { get; set; }
    public List<TextCharacter>? Characters { get; set; }  // Cache

    public float VisualHeight => Ascent + Descent;

    /// <summary>
    /// Position dieser Zeile im Container (wird von LayoutEngine berechnet)
    /// </summary>
    public float LineX { get; set; }
    public float LineY { get; set; }
}

/// <summary>
/// Layout-Engine für Text: Zeilenumbruch, Alignment, Metrics
///
/// Ähnlich wie HarfBuzz/LibreLayout, aber vereinfacht für Gravur-Use-Case:
/// - Keine komplexen Features (Ligatures, RTL, etc.)
/// - Fokus auf Zeilenumbruch + 9-Punkt Alignment
/// - Character-Metriken-Caching für Performance
/// </summary>
public class TextLayoutEngine
{
    private SKPaint _paint = new();
    private float _containerWidth;
    private float _containerHeight;
    private List<TextLine> _lines = [];
    private TextHorizontalAlign _halign = TextHorizontalAlign.Left;
    private TextVerticalAlign _valign = TextVerticalAlign.Top;

    // Character-Metriken-Cache: [FontFamily|Size] -> Metriken
    private Dictionary<string, SKFontMetrics> _metricsCache = new();

    public IReadOnlyList<TextLine> Lines => _lines.AsReadOnly();
    public float TotalWidth { get; private set; }
    public float TotalHeight { get; private set; }

    public TextLayoutEngine()
    {
        _paint.IsAntialias = true;
    }

    /// <summary>
    /// Hauptmethode: Layout berechnen für Model + Constraints
    /// </summary>
    /// <param name="model">Text-Datenmodell mit Characters</param>
    /// <param name="containerWidth">Maximale Breite (0 = unbegrenzt, Single-Line)</param>
    /// <param name="containerHeight">Maximale Höhe (0 = unbegrenzt)</param>
    /// <param name="halign">Horizontales Alignment</param>
    /// <param name="valign">Vertikales Alignment</param>
    public void Layout(
        SkiaTextModel model,
        float containerWidth,
        float containerHeight = 0,
        TextHorizontalAlign halign = TextHorizontalAlign.Left,
        TextVerticalAlign valign = TextVerticalAlign.Top)
    {
        _containerWidth = containerWidth;
        _containerHeight = containerHeight;
        _halign = halign;
        _valign = valign;

        _lines.Clear();
        TotalWidth = 0;
        TotalHeight = 0;

        if (model.CharacterCount == 0)
            return;

        // Pass 1: Measure Zeichen und berechne Zeilenumbruch
        MeasureAndWrap(model);

        // Pass 2: Berechne finale Positionen mit Alignment
        PositionLines();
    }

    /// <summary>
    /// Pass 1: Character-weise Messung + Zeilenumbruch
    /// </summary>
    private void MeasureAndWrap(SkiaTextModel model)
    {
        int lineStart = 0;
        float lineWidth = 0;
        float maxAscent = 0;
        float maxDescent = 0;

        for (int i = 0; i < model.CharacterCount; i++)
        {
            var ch = model.Characters[i];

            // Zeilenumbruch-Charakter?
            if (ch.Value == '\n')
            {
                FinishLine(lineStart, i, lineWidth, maxAscent, maxDescent);
                lineStart = i + 1;
                lineWidth = 0;
                maxAscent = maxDescent = 0;
                continue;
            }

            // Charakter-Metriken holen
            var metrics = GetCharMetrics(ch);
            float charWidth = metrics.advanceX;

            // Hard-Wrap prüfen
            bool needsWrap = _containerWidth > 0 && (lineWidth + charWidth > _containerWidth) && lineWidth > 0;

            if (needsWrap)
            {
                FinishLine(lineStart, i, lineWidth, maxAscent, maxDescent);
                lineStart = i;
                lineWidth = charWidth;
                maxAscent = metrics.ascent;
                maxDescent = metrics.descent;
            }
            else
            {
                lineWidth += charWidth;
                maxAscent = Math.Max(maxAscent, metrics.ascent);
                maxDescent = Math.Max(maxDescent, metrics.descent);
            }
        }

        // Letzte Zeile
        if (lineStart < model.CharacterCount)
            FinishLine(lineStart, model.CharacterCount, lineWidth, maxAscent, maxDescent);
    }

    /// <summary>
    /// Hilfsklasse für Character-Metriken
    /// </summary>
    private struct CharMetrics
    {
        public float advanceX;
        public float ascent;
        public float descent;
    }

    /// <summary>
    /// Character-Metriken holen (mit Caching)
    /// </summary>
    private CharMetrics GetCharMetrics(TextCharacter ch)
    {
        _paint.TextSize = ch.Format.FontSizePt;
        _paint.Typeface = SkiaTextModel.GetTypeface(ch.Format.FontFamily, ch.Format.Bold, ch.Format.Italic);

        // Advance (Character-Breite)
        float advance = _paint.MeasureText(ch.Value.ToString());

        // Font-Metriken
        var metricsKey = $"{ch.Format.FontFamily}|{ch.Format.FontSizePt}";
        if (!_metricsCache.TryGetValue(metricsKey, out var fm))
        {
            fm = _paint.FontMetrics;
            _metricsCache[metricsKey] = fm;
        }

        return new CharMetrics
        {
            advanceX = advance + ch.Format.Tracking,
            ascent = -fm.Ascent,
            descent = fm.Descent
        };
    }

    /// <summary>
    /// Zeile abschließen und zu _lines hinzufügen
    /// </summary>
    private void FinishLine(int startIdx, int endIdx, float width, float ascent, float descent)
    {
        if (startIdx >= endIdx)
            return;

        _lines.Add(new TextLine
        {
            StartCharIdx = startIdx,
            EndCharIdx = endIdx,
            Width = width,
            Ascent = ascent,
            Descent = descent
        });

        TotalWidth = Math.Max(TotalWidth, width);
        TotalHeight += ascent + descent;
    }

    /// <summary>
    /// Pass 2: Berechne X/Y Positionen der Zeilen basierend auf Alignment
    /// </summary>
    private void PositionLines()
    {
        float y = 0;

        // Vertikal: Falls Top, starten wir bei 0
        // Falls Center/Bottom, müssen wir offset berechnen
        float verticalOffset = 0;
        if (_containerHeight > 0)
        {
            if (_valign == TextVerticalAlign.Center)
                verticalOffset = (_containerHeight - TotalHeight) / 2;
            else if (_valign == TextVerticalAlign.Bottom)
                verticalOffset = _containerHeight - TotalHeight;
        }

        foreach (var line in _lines)
        {
            // Horizontal
            float x = 0;
            if (_halign == TextHorizontalAlign.Center)
                x = (_containerWidth - line.Width) / 2;
            else if (_halign == TextHorizontalAlign.Right)
                x = _containerWidth - line.Width;

            // Vertikal
            line.LineX = x;
            line.LineY = y + verticalOffset;
            y += line.Ascent + line.Descent;
        }
    }

    /// <summary>
    /// Hit-Test: Finde Character-Index bei gegebener (x, y)
    /// Wird für Mouse-Click→Cursor-Position verwendet
    /// </summary>
    public int HitTestCursorPosition(SkiaTextModel model, float clickX, float clickY)
    {
        foreach (var line in _lines)
        {
            // In dieser Zeile?
            if (clickY < line.LineY || clickY > line.LineY + line.Ascent + line.Descent)
                continue;

            // Character suchen in dieser Zeile
            float charX = line.LineX;
            for (int i = line.StartCharIdx; i < line.EndCharIdx; i++)
            {
                var ch = model.Characters[i];
                var metrics = GetCharMetrics(ch);

                if (clickX < charX + metrics.advanceX / 2)
                    return i;

                charX += metrics.advanceX;
            }

            // Hinter letztem Zeichen
            return line.EndCharIdx;
        }

        // Außerhalb aller Zeilen → Ende des Textes
        return model.CharacterCount;
    }

    /// <summary>
    /// Hole Zeile für einen Character-Index
    /// </summary>
    public TextLine? GetLineForCharacter(int charIdx)
    {
        return _lines.FirstOrDefault(l => charIdx >= l.StartCharIdx && charIdx < l.EndCharIdx);
    }

    /// <summary>
    /// Berechne Y-Position (Zeile-Nummer) für Cursor
    /// </summary>
    public (int lineIndex, int columnInLine) GetCursorLineColumn(int charIdx)
    {
        for (int lineIdx = 0; lineIdx < _lines.Count; lineIdx++)
        {
            var line = _lines[lineIdx];
            if (charIdx >= line.StartCharIdx && charIdx < line.EndCharIdx)
            {
                return (lineIdx, charIdx - line.StartCharIdx);
            }
        }

        // Am Ende
        if (_lines.Count > 0)
            return (_lines.Count - 1, _lines[^1].EndCharIdx - _lines[^1].StartCharIdx);

        return (0, 0);
    }

    /// <summary>
    /// Liefert Bounding-Box für Character-Auswahl (zur Visualisierung)
    /// </summary>
    public SKRect GetCharacterBounds(SkiaTextModel model, int charIdx)
    {
        var line = GetLineForCharacter(charIdx);
        if (line == null)
            return SKRect.Empty;

        float charX = line.LineX;
        for (int i = line.StartCharIdx; i < charIdx; i++)
        {
            var metrics = GetCharMetrics(model.Characters[i]);
            charX += metrics.advanceX;
        }

        var chMetrics = GetCharMetrics(model.Characters[charIdx]);

        return new SKRect(
            charX,
            line.LineY,
            charX + chMetrics.advanceX,
            line.LineY + line.Ascent + line.Descent
        );
    }

    /// <summary>
    /// Debug: Alle Zeilen und deren Positionen anzeigen
    /// </summary>
    public override string ToString()
    {
        return $"TextLayout: {_lines.Count} lines, {TotalWidth:F1}×{TotalHeight:F1}, " +
               $"HAlign={_halign}, VAlign={_valign}";
    }
}
