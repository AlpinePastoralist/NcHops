using System;
using System.Collections.Generic;
using System.Linq;

namespace NCHops;

/// <summary>
/// Einfache Demo-Klasse zur Verwendung von TextToLineSegments
/// Zeigt, wie Text in verschiedene Formate konvertiert wird
/// </summary>
public static class TextToSegmentsDemo
{
    /// <summary>
    /// Demonstriert die Konvertierung von Text zu Liniensegmenten
    /// </summary>
    public static void DemoConvertText()
    {
        Console.WriteLine("╔════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║     Text-zu-Liniensegmente Demo                            ║");
        Console.WriteLine("╚════════════════════════════════════════════════════════════╝\n");

        // Demo 1: Buchstabe 'A'
        DemoBuchstabe("A", 48f);

        // Demo 2: Wort 'CNC'
        DemoWort("CNC", 36f);

        // Demo 3: Verschiedene Schriftgrößen
        DemoFontSizes("Hi");

        // Demo 4: G-Code Export
        DemoGCodeExport("TEST");

        // Demo 5: Segment-Statistiken
        DemoStatistics("Text");
    }

    static void DemoBuchstabe(string text, float size)
    {
        Console.WriteLine($"━━━ Demo 1: Buchstabe '{text}' (Größe {size}pt) ━━━\n");

        var geometries = TextToLineSegments.ConvertTextToLineSegments(
            text, "Segoe UI", size, 0, size / 2 + 10
        );

        foreach (var charGeo in geometries)
        {
            Console.WriteLine($"Charakter: '{charGeo.Character}'");
            Console.WriteLine($"  Position: X={charGeo.X:F1}, Y={charGeo.Y:F1}");
            Console.WriteLine($"  Größe: {charGeo.Width:F1} x {charGeo.Height:F1}");
            Console.WriteLine($"  Liniensegmente: {charGeo.LineSegments.Count}");

            if (charGeo.LineSegments.Count > 0)
            {
                Console.WriteLine($"  Erste Segmente:");
                for (int i = 0; i < Math.Min(5, charGeo.LineSegments.Count); i++)
                {
                    var seg = charGeo.LineSegments[i];
                    Console.WriteLine($"    [{i}] ({seg.X1:F1},{seg.Y1:F1}) → ({seg.X2:F1},{seg.Y2:F1})");
                }
                if (charGeo.LineSegments.Count > 5)
                    Console.WriteLine($"    ... und {charGeo.LineSegments.Count - 5} weitere");
            }
        }
        Console.WriteLine();
    }

    static void DemoWort(string text, float size)
    {
        Console.WriteLine($"━━━ Demo 2: Wort '{text}' (Größe {size}pt) ━━━\n");

        var geometries = TextToLineSegments.ConvertTextToLineSegments(
            text, "Segoe UI", size, 0, size / 2 + 10
        );

        Console.WriteLine($"Anzahl Zeichen: {geometries.Count}");
        Console.WriteLine("Segmente pro Buchstabe:");
        foreach (var charGeo in geometries)
        {
            Console.WriteLine($"  '{charGeo.Character}': {charGeo.LineSegments.Count:D3} Segmente");
        }

        int gesamt = TextToLineSegments.ExtractAllLineSegments(geometries).Count;
        Console.WriteLine($"  ─────────────────────");
        Console.WriteLine($"  Gesamt: {gesamt} Segmente\n");
    }

    static void DemoFontSizes(string text)
    {
        Console.WriteLine($"━━━ Demo 3: Verschiedene Schriftgrößen für '{text}' ━━━\n");

        int[] sizes = { 12, 24, 36, 48 };

        Console.WriteLine($"{"Größe (pt)",6} | {"Zeichen",8} | {"Segmente",9} | {"Durchschnitt",10}");
        Console.WriteLine("".PadRight(50, '─'));

        foreach (int size in sizes)
        {
            var geometries = TextToLineSegments.ConvertTextToLineSegments(
                text, "Segoe UI", size, 0, size / 2 + 10
            );

            int gesamt = TextToLineSegments.ExtractAllLineSegments(geometries).Count;
            double durchschnitt = geometries.Count > 0 ? (double)gesamt / geometries.Count : 0;

            Console.WriteLine($"{size,6} | {geometries.Count,8} | {gesamt,9} | {durchschnitt,10:F2}");
        }
        Console.WriteLine();
    }

    static void DemoGCodeExport(string text)
    {
        Console.WriteLine($"━━━ Demo 4: G-Code Export für '{text}' ━━━\n");

        var geometries = TextToLineSegments.ConvertTextToLineSegments(
            text, "Segoe UI", 36f, 10, 40
        );

        var segments = TextToLineSegments.ExtractAllLineSegments(geometries);

        string gcode = TextToLineSegments.GenerateGCode(
            segments,
            feedRate: 50,
            safeZ: 5.0f,
            workingZ: -2.0f,
            scale: 1.0f
        );

        var lines = gcode.Split('\n');
        Console.WriteLine($"G-Code Länge: {gcode.Length} Zeichen ({lines.Length} Zeilen)");
        Console.WriteLine("\nVorschau (erste 15 Zeilen):");
        for (int i = 0; i < Math.Min(15, lines.Length); i++)
        {
            if (!string.IsNullOrWhiteSpace(lines[i]))
                Console.WriteLine($"  {lines[i]}");
        }
        if (lines.Length > 15)
            Console.WriteLine($"  ... ({lines.Length - 15} weitere Zeilen)\n");
    }

    static void DemoStatistics(string text)
    {
        Console.WriteLine($"━━━ Demo 5: Statistiken für '{text}' ━━━\n");

        var geometries = TextToLineSegments.ConvertTextToLineSegments(
            text, "Segoe UI", 24f, 0, 30
        );

        var segments = TextToLineSegments.ExtractAllLineSegments(geometries);

        // Berechne Bounding Box
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;

        foreach (var seg in segments)
        {
            minX = Math.Min(minX, Math.Min(seg.X1, seg.X2));
            minY = Math.Min(minY, Math.Min(seg.Y1, seg.Y2));
            maxX = Math.Max(maxX, Math.Max(seg.X1, seg.X2));
            maxY = Math.Max(maxY, Math.Max(seg.Y1, seg.Y2));
        }

        Console.WriteLine($"Text: '{text}'");
        Console.WriteLine($"Schrift: Segoe UI, 24pt");
        Console.WriteLine($"Gesamt Liniensegmente: {segments.Count}");
        Console.WriteLine($"\nBounding Box:");
        Console.WriteLine($"  X: {minX:F2} bis {maxX:F2} (Breite: {maxX - minX:F2})");
        Console.WriteLine($"  Y: {minY:F2} bis {maxY:F2} (Höhe: {maxY - minY:F2})");
        Console.WriteLine($"\nDurchschnittliche Segmentlänge:");

        double avgLength = segments.Count > 0
            ? segments.Sum(s => Math.Sqrt((s.X2 - s.X1) * (s.X2 - s.X1) + (s.Y2 - s.Y1) * (s.Y2 - s.Y1))) / segments.Count
            : 0;

        Console.WriteLine($"  {avgLength:F2} Einheiten\n");
    }

    /// <summary>
    /// Exportiert Segmente als SVG für Visualisierung
    /// </summary>
    public static string ExportSVG(List<TextToLineSegments.LineSegment> segments, float width, float height)
    {
        var svg = new System.Text.StringBuilder();

        svg.AppendLine($"<svg width=\"{width}\" height=\"{height}\" xmlns=\"http://www.w3.org/2000/svg\">");
        svg.AppendLine("  <style>");
        svg.AppendLine("    line { stroke: black; stroke-width: 0.5; }");
        svg.AppendLine("  </style>");

        foreach (var seg in segments)
        {
            svg.AppendLine($"  <line x1=\"{seg.X1:F2}\" y1=\"{seg.Y1:F2}\" x2=\"{seg.X2:F2}\" y2=\"{seg.Y2:F2}\" />");
        }

        svg.AppendLine("</svg>");

        return svg.ToString();
    }

    /// <summary>
    /// Exportiert Segmente als DXF (vereinfachtes Format)
    /// </summary>
    public static string ExportDXF(List<TextToLineSegments.LineSegment> segments, string entityName = "TextContours")
    {
        var dxf = new System.Text.StringBuilder();

        // DXF-Header
        dxf.AppendLine("  0");
        dxf.AppendLine("SECTION");
        dxf.AppendLine("  2");
        dxf.AppendLine("ENTITIES");

        // Liniensegmente als DXF-Linien
        foreach (var seg in segments)
        {
            dxf.AppendLine("  0");
            dxf.AppendLine("LINE");
            dxf.AppendLine("  8");
            dxf.AppendLine(entityName);
            dxf.AppendLine(" 10");
            dxf.AppendLine($"{seg.X1:F4}");
            dxf.AppendLine(" 20");
            dxf.AppendLine($"{seg.Y1:F4}");
            dxf.AppendLine(" 11");
            dxf.AppendLine($"{seg.X2:F4}");
            dxf.AppendLine(" 21");
            dxf.AppendLine($"{seg.Y2:F4}");
        }

        dxf.AppendLine("  0");
        dxf.AppendLine("ENDSEC");

        return dxf.ToString();
    }
}
