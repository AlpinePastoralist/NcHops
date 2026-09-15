using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace NCHops;

/// <summary>
/// Konvertiert Text in Liniensegmente für die CNC-Bearbeitung.
/// Jeder Buchstabe wird als Ensemble von Linien gezeichnet, die exakt
/// an der gleichen Position und Größe wie das Original bleiben.
/// </summary>
public class TextToLineSegments
{
    public class LineSegment
    {
        public float X1 { get; set; }
        public float Y1 { get; set; }
        public float X2 { get; set; }
        public float Y2 { get; set; }

        public LineSegment(float x1, float y1, float x2, float y2)
        {
            X1 = x1;
            Y1 = y1;
            X2 = x2;
            Y2 = y2;
        }

        public override string ToString() => $"Line({X1:F2},{Y1:F2} -> {X2:F2},{Y2:F2})";
    }

    public class CharacterGeometry
    {
        public string Character { get; set; } = "";
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public List<LineSegment> LineSegments { get; set; } = [];
    }

    /// <summary>
    /// Konvertiert Text in Liniensegmente mit voller Formatierungsunterstützung pro Buchstabe.
    /// </summary>
    /// <param name="text">Der zu konvertierende Text</param>
    /// <param name="fontFamily">Schriftfamilie (z.B. "Segoe UI")</param>
    /// <param name="fontSize">Schriftgröße in Punkten</param>
    /// <param name="startX">Startposition X</param>
    /// <param name="startY">Startposition Y (Baseline)</param>
    /// <param name="tolerance">Toleranz für Kurven-zu-Linien-Konvertierung (kleiner = mehr Punkte)</param>
    /// <returns>Liste der Zeichengeometrien mit Liniensegmenten</returns>
    public static List<CharacterGeometry> ConvertTextToLineSegments(
        string text,
        string fontFamily,
        float fontSize,
        float startX,
        float startY,
        float tolerance = 0.5f)
    {
        return ConvertTextToLineSegmentsWithFormat(
            text,
            new List<(int start, int end, CharacterFormat format)>
            {
                (0, text.Length, new CharacterFormat {
                    FontFamily = fontFamily,
                    FontSize = fontSize
                })
            },
            startX, startY, tolerance);
    }

    /// <summary>
    /// Konvertiert Text mit Formatierung pro Abschnitt in Liniensegmente.
    /// Unterstützt verschiedene Schriftarten, Größen und Stile pro Buchstabe.
    /// </summary>
    /// <param name="text">Der zu konvertierende Text</param>
    /// <param name="formats">Liste von (Start, Ende, Format)-Tupeln für Textabschnitte</param>
    /// <param name="startX">Startposition X</param>
    /// <param name="startY">Startposition Y (Baseline)</param>
    /// <param name="tolerance">Toleranz für Kurven-zu-Linien-Konvertierung</param>
    /// <returns>Liste der Zeichengeometrien mit Liniensegmenten</returns>
    public static List<CharacterGeometry> ConvertTextToLineSegmentsWithFormat(
        string text,
        List<(int start, int end, CharacterFormat format)> formats,
        float startX,
        float startY,
        float tolerance = 0.5f)
    {
        var result = new List<CharacterGeometry>();

        if (string.IsNullOrEmpty(text) || formats.Count == 0)
            return result;

        // Erstelle eine Mapping-Liste: Für jeden Character die passende Formatierung
        var charFormats = new CharacterFormat[text.Length];
        foreach (var (start, end, format) in formats)
        {
            for (int i = Math.Max(0, start); i < Math.Min(text.Length, end); i++)
                charFormats[i] = format ?? new CharacterFormat();
        }

        float currentX = startX;
        float baselineY = startY;
        float maxLineHeight = 0;

        // Berechne maximale Line Height für alle Formate
        foreach (var fmt in charFormats)
        {
            if (fmt == null) continue;
            var typeface = SKTypeface.FromFamilyName(fmt.FontFamily);
            using var paint = new SKPaint { Typeface = typeface, TextSize = fmt.FontSize };
            var metrics = paint.FontMetrics;
            float lineHeight = -metrics.Ascent + metrics.Descent;
            maxLineHeight = Math.Max(maxLineHeight, lineHeight);
        }

        for (int charIdx = 0; charIdx < text.Length; charIdx++)
        {
            char c = text[charIdx];

            if (c == '\n')
            {
                currentX = startX;
                baselineY += maxLineHeight;
                continue;
            }

            // Hole das Format für diesen Charakter
            var fmt = charFormats[charIdx] ?? new CharacterFormat();
            var typeface = SKTypeface.FromFamilyName(fmt.FontFamily);

            using var paint = new SKPaint
            {
                Typeface = typeface,
                TextSize = fmt.FontSize,
                IsAntialias = true,
                TextScaleX = fmt.ScaleX,
                TextSkewX = fmt.SkewX
            };

            // Erstelle Charakter-Geometrie
            var charGeo = new CharacterGeometry
            {
                Character = c.ToString(),
                X = currentX,
                Y = baselineY
            };

            // Besorge SKPath für diesen Charakter mit seiner Formatierung
            using var path = paint.GetTextPath(c.ToString(), 0, 0);

            if (path != null && !path.IsEmpty)
            {
                // Konvertiere Path zu Liniensegmenten
                charGeo.LineSegments = ConvertPathToLineSegments(path, currentX, baselineY, tolerance);

                // Berechne Bounding Box
                var bounds = path.Bounds;
                charGeo.Width = bounds.Width;
                charGeo.Height = bounds.Height;
            }

            result.Add(charGeo);

            // Advance zum nächsten Charakter (mit Tracking)
            float charWidth = paint.MeasureText(c.ToString());
            currentX += charWidth + fmt.Tracking;
        }

        return result;
    }

    /// <summary>
    /// Konvertiert einen SKPath zu einer Liste von Liniensegmenten.
    /// Kurven (Bézier-Kurven) werden in kleine Liniensegmente unterteilt.
    /// </summary>
    private static List<LineSegment> ConvertPathToLineSegments(
        SKPath path,
        float offsetX,
        float offsetY,
        float tolerance)
    {
        var segments = new List<LineSegment>();

        if (path == null || path.IsEmpty)
            return segments;

        using var pathEnum = path.CreateRawIterator();

        SKPathVerb verb;
        Span<SKPoint> points = stackalloc SKPoint[4];
        SKPoint lastPoint = SKPoint.Empty;
        bool firstMove = true;

        while ((verb = pathEnum.Next(points)) != SKPathVerb.Done)
        {
            switch (verb)
            {
                case SKPathVerb.Move:
                    lastPoint = points[0];
                    firstMove = true;
                    break;

                case SKPathVerb.Line:
                    segments.Add(new LineSegment(
                        points[0].X + offsetX,
                        points[0].Y + offsetY,
                        points[1].X + offsetX,
                        points[1].Y + offsetY
                    ));
                    lastPoint = points[1];
                    break;

                case SKPathVerb.Quad:
                    // Quadratische Bézier-Kurve
                    SubdivideCurve(segments, lastPoint, points[1], points[2], offsetX, offsetY, tolerance);
                    lastPoint = points[2];
                    break;

                case SKPathVerb.Cubic:
                    // Kubische Bézier-Kurve
                    SubdivideCubicCurve(segments, lastPoint, points[1], points[2], points[3], offsetX, offsetY, tolerance);
                    lastPoint = points[3];
                    break;

                case SKPathVerb.Close:
                    if (firstMove && lastPoint != SKPoint.Empty)
                    {
                        segments.Add(new LineSegment(
                            lastPoint.X + offsetX,
                            lastPoint.Y + offsetY,
                            lastPoint.X + offsetX,
                            lastPoint.Y + offsetY
                        ));
                    }
                    break;

                case SKPathVerb.Conic:
                    // Konische Kurve (wird selten verwendet)
                    break;
            }
        }

        return segments;
    }

    /// <summary>
    /// Teilt eine quadratische Bézier-Kurve in Liniensegmente auf.
    /// </summary>
    private static void SubdivideCurve(
        List<LineSegment> segments,
        SKPoint p0,
        SKPoint p1,
        SKPoint p2,
        float offsetX,
        float offsetY,
        float tolerance,
        int depth = 0)
    {
        const int maxDepth = 10;

        if (depth >= maxDepth)
        {
            // Zeichne einfache Linie
            segments.Add(new LineSegment(
                p0.X + offsetX,
                p0.Y + offsetY,
                p2.X + offsetX,
                p2.Y + offsetY
            ));
            return;
        }

        // Berechne Kurvenpunkte
        float t = 0.5f;
        float x01 = p0.X + t * (p1.X - p0.X);
        float y01 = p0.Y + t * (p1.Y - p0.Y);
        float x12 = p1.X + t * (p2.X - p1.X);
        float y12 = p1.Y + t * (p2.Y - p1.Y);
        float qx = x01 + t * (x12 - x01);
        float qy = y01 + t * (y12 - y01);

        // Berechne Abstand von Punkt zu Linie
        float dist = DistancePointToLine(qx, qy, p0.X, p0.Y, p2.X, p2.Y);

        if (dist > tolerance)
        {
            // Kurve ist zu krumm, unterteile weiter
            var p01 = new SKPoint(x01, y01);
            var p12 = new SKPoint(x12, y12);
            var q = new SKPoint(qx, qy);

            SubdivideCurve(segments, p0, p01, q, offsetX, offsetY, tolerance, depth + 1);
            SubdivideCurve(segments, q, p12, p2, offsetX, offsetY, tolerance, depth + 1);
        }
        else
        {
            // Kurve ist glatt genug
            segments.Add(new LineSegment(
                p0.X + offsetX,
                p0.Y + offsetY,
                p2.X + offsetX,
                p2.Y + offsetY
            ));
        }
    }

    /// <summary>
    /// Teilt eine kubische Bézier-Kurve in Liniensegmente auf.
    /// </summary>
    private static void SubdivideCubicCurve(
        List<LineSegment> segments,
        SKPoint p0,
        SKPoint p1,
        SKPoint p2,
        SKPoint p3,
        float offsetX,
        float offsetY,
        float tolerance,
        int depth = 0)
    {
        const int maxDepth = 10;

        if (depth >= maxDepth)
        {
            segments.Add(new LineSegment(
                p0.X + offsetX,
                p0.Y + offsetY,
                p3.X + offsetX,
                p3.Y + offsetY
            ));
            return;
        }

        // De Casteljau-Algorithmus
        float t = 0.5f;

        float p01x = p0.X + t * (p1.X - p0.X);
        float p01y = p0.Y + t * (p1.Y - p0.Y);
        float p12x = p1.X + t * (p2.X - p1.X);
        float p12y = p1.Y + t * (p2.Y - p1.Y);
        float p23x = p2.X + t * (p3.X - p2.X);
        float p23y = p2.Y + t * (p3.Y - p2.Y);

        float p012x = p01x + t * (p12x - p01x);
        float p012y = p01y + t * (p12y - p01y);
        float p123x = p12x + t * (p23x - p12x);
        float p123y = p12y + t * (p23y - p12y);

        float qx = p012x + t * (p123x - p012x);
        float qy = p012y + t * (p123y - p012y);

        // Berechne Abstand
        float dist = DistancePointToLine(qx, qy, p0.X, p0.Y, p3.X, p3.Y);

        if (dist > tolerance)
        {
            var q = new SKPoint(qx, qy);
            var p01 = new SKPoint(p01x, p01y);
            var p12 = new SKPoint(p12x, p12y);
            var p23 = new SKPoint(p23x, p23y);
            var p012 = new SKPoint(p012x, p012y);
            var p123 = new SKPoint(p123x, p123y);

            SubdivideCubicCurve(segments, p0, p01, p012, q, offsetX, offsetY, tolerance, depth + 1);
            SubdivideCubicCurve(segments, q, p123, p23, p3, offsetX, offsetY, tolerance, depth + 1);
        }
        else
        {
            segments.Add(new LineSegment(
                p0.X + offsetX,
                p0.Y + offsetY,
                p3.X + offsetX,
                p3.Y + offsetY
            ));
        }
    }

    /// <summary>
    /// Berechnet den Abstand eines Punktes zu einer Linie.
    /// </summary>
    private static float DistancePointToLine(float px, float py, float x1, float y1, float x2, float y2)
    {
        float dx = x2 - x1;
        float dy = y2 - y1;
        float len2 = dx * dx + dy * dy;

        if (len2 < 0.0001f)
            return (float)Math.Sqrt((px - x1) * (px - x1) + (py - y1) * (py - y1));

        float t = ((px - x1) * dx + (py - y1) * dy) / len2;
        t = Math.Max(0, Math.Min(1, t));

        float nx = x1 + t * dx;
        float ny = y1 + t * dy;

        return (float)Math.Sqrt((px - nx) * (px - nx) + (py - ny) * (py - ny));
    }

    /// <summary>
    /// Exportiert die Liniensegmente als Liste aus der CharacterGeometry.
    /// </summary>
    public static List<LineSegment> ExtractAllLineSegments(IEnumerable<CharacterGeometry> geometries)
    {
        return geometries.SelectMany(g => g.LineSegments).ToList();
    }

    /// <summary>
    /// Generiert G-Code für die Liniensegmente (für CNC-Maschinen).
    /// </summary>
    public static string GenerateGCode(
        List<LineSegment> segments,
        float feedRate = 100,
        float safeZ = 5.0f,
        float workingZ = -2.0f,
        float scale = 1.0f)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("(Text-Kontur als Liniensegmente)");
        sb.AppendLine($"(Anzahl Segmente: {segments.Count})");
        sb.AppendLine($"G00 Z{safeZ:F4}");
        sb.AppendLine();

        bool isFirstSegment = true;

        foreach (var seg in segments)
        {
            float x1 = seg.X1 * scale;
            float y1 = seg.Y1 * scale;
            float x2 = seg.X2 * scale;
            float y2 = seg.Y2 * scale;

            if (isFirstSegment)
            {
                sb.AppendLine($"G00 X{x1:F4} Y{y1:F4}");
                sb.AppendLine($"G01 Z{workingZ:F4} F{feedRate:F0}");
                isFirstSegment = false;
            }

            sb.AppendLine($"G01 X{x2:F4} Y{y2:F4} F{feedRate:F0}");
        }

        sb.AppendLine();
        sb.AppendLine($"G00 Z{safeZ:F4}");

        return sb.ToString();
    }
}
