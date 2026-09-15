using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace NCHops;

/// <summary>
/// Tests für TextToLineSegments-Konvertierung
/// </summary>
class TestTextToLineSegments
{
    static void Main()
    {
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║        Text-zu-Liniensegmente Konvertierungs-Tests          ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝\n");

        // Test 1: Einfacher Text
        TestSimpleText();

        // Test 2: Text mit verschiedenen Schriftgrößen
        TestDifferentFontSizes();

        // Test 3: G-Code-Generierung
        TestGCodeGeneration();

        Console.WriteLine("\n✓ Alle Tests abgeschlossen!");
    }

    static void TestSimpleText()
    {
        Console.WriteLine("Test 1: Einfache Textkonvertierung\n");

        string text = "A";
        var geometries = TextToLineSegments.ConvertTextToLineSegments(
            text: text,
            fontFamily: "Segoe UI",
            fontSize: 48f,
            startX: 0,
            startY: 50,
            tolerance: 0.3f
        );

        Console.WriteLine($"  Text: \"{text}\"");
        Console.WriteLine($"  Font: Segoe UI, 48pt");
        Console.WriteLine($"  Zeichen erkannt: {geometries.Count}");

        int totalSegments = 0;
        foreach (var charGeo in geometries)
        {
            Console.WriteLine($"    '{charGeo.Character}': {charGeo.LineSegments.Count} Liniensegmente");
            totalSegments += charGeo.LineSegments.Count;

            // Zeige erste 3 Segmente
            for (int i = 0; i < Math.Min(3, charGeo.LineSegments.Count); i++)
            {
                var seg = charGeo.LineSegments[i];
                Console.WriteLine($"      [{i}] ({seg.X1:F1},{seg.Y1:F1}) → ({seg.X2:F1},{seg.Y2:F1})");
            }

            if (charGeo.LineSegments.Count > 3)
                Console.WriteLine($"      ... und {charGeo.LineSegments.Count - 3} weitere");
        }

        Console.WriteLine($"  Gesamt Liniensegmente: {totalSegments}\n");
    }

    static void TestDifferentFontSizes()
    {
        Console.WriteLine("Test 2: Verschiedene Schriftgrößen\n");

        string text = "Hi";
        int[] fontSizes = { 12, 24, 48 };

        foreach (int fontSize in fontSizes)
        {
            var geometries = TextToLineSegments.ConvertTextToLineSegments(
                text: text,
                fontFamily: "Segoe UI",
                fontSize: fontSize,
                startX: 0,
                startY: fontSize,
                tolerance: 0.3f
            );

            int totalSegments = TextToLineSegments.ExtractAllLineSegments(geometries).Count;
            Console.WriteLine($"  Font-Größe {fontSize}pt: {totalSegments} Liniensegmente für \"{text}\"");
        }

        Console.WriteLine();
    }

    static void TestGCodeGeneration()
    {
        Console.WriteLine("Test 3: G-Code Generierung\n");

        string text = "CNC";
        var geometries = TextToLineSegments.ConvertTextToLineSegments(
            text: text,
            fontFamily: "Segoe UI",
            fontSize: 36f,
            startX: 10,
            startY: 40,
            tolerance: 0.5f
        );

        var allSegments = TextToLineSegments.ExtractAllLineSegments(geometries);

        string gcode = TextToLineSegments.GenerateGCode(
            segments: allSegments,
            feedRate: 50,
            safeZ: 5.0f,
            workingZ: -2.0f,
            scale: 1.0f
        );

        Console.WriteLine($"  Text: \"{text}\"");
        Console.WriteLine($"  Gesamt Liniensegmente: {allSegments.Count}");
        Console.WriteLine($"\n  G-Code Vorschau (erste 30 Zeilen):\n");

        var lines = gcode.Split('\n');
        for (int i = 0; i < Math.Min(30, lines.Length); i++)
        {
            if (!string.IsNullOrWhiteSpace(lines[i]))
                Console.WriteLine($"    {lines[i]}");
        }

        if (lines.Length > 30)
            Console.WriteLine($"    ... ({lines.Length - 30} weitere Zeilen)\n");
    }
}
