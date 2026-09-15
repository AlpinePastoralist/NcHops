using System;
using System.Collections.Generic;

namespace NCHops;

/// <summary>
/// Beispiele für die Verwendung von formatiertem Text in TextToLineSegments
/// </summary>
public static class FormattedTextExample
{
    /// <summary>
    /// Beispiel 1: Text mit verschiedenen Schriftgrößen
    /// </summary>
    public static void Example1_DifferentSizes()
    {
        string text = "A B C";

        var formats = new List<(int start, int end, CharacterFormat format)>
        {
            // 'A' - 48pt
            (0, 1, new CharacterFormat { FontFamily = "Segoe UI", FontSize = 48f }),
            // ' ' - 12pt
            (1, 2, new CharacterFormat { FontFamily = "Segoe UI", FontSize = 12f }),
            // 'B' - 36pt
            (2, 3, new CharacterFormat { FontFamily = "Segoe UI", FontSize = 36f }),
            // ' ' - 12pt
            (3, 4, new CharacterFormat { FontFamily = "Segoe UI", FontSize = 12f }),
            // 'C' - 24pt
            (4, 5, new CharacterFormat { FontFamily = "Segoe UI", FontSize = 24f }),
        };

        var geometries = TextToLineSegments.ConvertTextToLineSegmentsWithFormat(
            text, formats, 0, 50, 0.3f
        );

        Console.WriteLine($"Unterschiedliche Größen: {geometries.Count} Zeichen konvertiert");
        foreach (var geo in geometries)
            Console.WriteLine($"  '{geo.Character}': {geo.LineSegments.Count} Segmente");
    }

    /// <summary>
    /// Beispiel 2: Text mit verschiedenen Schriftarten
    /// </summary>
    public static void Example2_DifferentFonts()
    {
        string text = "Arial Times";

        var formats = new List<(int start, int end, CharacterFormat format)>
        {
            // "Arial" - Arial Schrift
            (0, 5, new CharacterFormat { FontFamily = "Arial", FontSize = 24f }),
            // " " - Default
            (5, 6, new CharacterFormat { FontFamily = "Segoe UI", FontSize = 24f }),
            // "Times" - Times New Roman
            (6, 11, new CharacterFormat { FontFamily = "Times New Roman", FontSize = 24f }),
        };

        var geometries = TextToLineSegments.ConvertTextToLineSegmentsWithFormat(
            text, formats, 0, 30, 0.3f
        );

        Console.WriteLine($"\nVerschiedene Schriftarten: {geometries.Count} Zeichen");
        foreach (var geo in geometries)
            Console.WriteLine($"  '{geo.Character}': {geo.LineSegments.Count} Segmente");
    }

    /// <summary>
    /// Beispiel 3: Text mit Basis-Versatz (Superscript/Subscript)
    /// </summary>
    public static void Example3_Superscript()
    {
        string text = "H2O";

        var formats = new List<(int start, int end, CharacterFormat format)>
        {
            // 'H' - Normal
            (0, 1, new CharacterFormat {
                FontFamily = "Segoe UI",
                FontSize = 24f
            }),
            // '2' - Subscript (50% Größe, nach unten versetzt)
            (1, 2, new CharacterFormat {
                FontFamily = "Segoe UI",
                FontSize = 12f,
                BaselineOffsetY = 4f  // Nach unten versetzt
            }),
            // 'O' - Normal
            (2, 3, new CharacterFormat {
                FontFamily = "Segoe UI",
                FontSize = 24f
            }),
        };

        var geometries = TextToLineSegments.ConvertTextToLineSegmentsWithFormat(
            text, formats, 0, 30, 0.3f
        );

        Console.WriteLine($"\nSuperscript/Subscript: {geometries.Count} Zeichen");
    }

    /// <summary>
    /// Beispiel 4: Text mit Buchstabenabstand (Tracking)
    /// </summary>
    public static void Example4_Tracking()
    {
        string text = "ABCD";

        var formats = new List<(int start, int end, CharacterFormat format)>
        {
            // Normales Spacing
            (0, 2, new CharacterFormat {
                FontFamily = "Segoe UI",
                FontSize = 24f,
                Tracking = 0f  // Normal
            }),
            // Großer Abstand
            (2, 4, new CharacterFormat {
                FontFamily = "Segoe UI",
                FontSize = 24f,
                Tracking = 5f  // 5 Einheiten Abstand pro Buchstabe
            }),
        };

        var geometries = TextToLineSegments.ConvertTextToLineSegmentsWithFormat(
            text, formats, 0, 30, 0.3f
        );

        Console.WriteLine($"\nTracking-Beispiel: {geometries.Count} Zeichen");
    }

    /// <summary>
    /// Beispiel 5: Text mit Skalierung
    /// </summary>
    public static void Example5_Scaling()
    {
        string text = "Wide";

        var formats = new List<(int start, int end, CharacterFormat format)>
        {
            // Normal
            (0, 2, new CharacterFormat {
                FontFamily = "Segoe UI",
                FontSize = 24f,
                ScaleX = 1.0f  // Normal
            }),
            // Verdoppelt in der Breite
            (2, 4, new CharacterFormat {
                FontFamily = "Segoe UI",
                FontSize = 24f,
                ScaleX = 1.5f  // 50% breiter
            }),
        };

        var geometries = TextToLineSegments.ConvertTextToLineSegmentsWithFormat(
            text, formats, 0, 30, 0.3f
        );

        Console.WriteLine($"\nSkalierungs-Beispiel: {geometries.Count} Zeichen");
    }

    /// <summary>
    /// Beispiel 6: Rainbow-Text (verschiedene Farben, für später verwendbar)
    /// </summary>
    public static void Example6_MultiColor()
    {
        string text = "COLOR";

        var colors = new[]
        {
            SkiaSharp.SKColors.Red,
            SkiaSharp.SKColors.Orange,
            SkiaSharp.SKColors.Yellow,
            SkiaSharp.SKColors.Green,
            SkiaSharp.SKColors.Blue,
        };

        var formats = new List<(int start, int end, CharacterFormat format)>();
        for (int i = 0; i < text.Length; i++)
        {
            formats.Add((i, i + 1, new CharacterFormat
            {
                FontFamily = "Segoe UI",
                FontSize = 24f,
                Color = colors[i]
            }));
        }

        var geometries = TextToLineSegments.ConvertTextToLineSegmentsWithFormat(
            text, formats, 0, 30, 0.3f
        );

        Console.WriteLine($"\nMulti-Color-Beispiel: {geometries.Count} Zeichen");
    }

    /// <summary>
    /// Komplexes Beispiel: Wissenschaftliche Formel mit verschiedenen Formaten
    /// </summary>
    public static void ExampleComplex_ScientificFormula()
    {
        string text = "E = mc2";  // c2 soll Superscript sein

        var formats = new List<(int start, int end, CharacterFormat format)>
        {
            // 'E' - Normal, groß
            (0, 1, new CharacterFormat { FontFamily = "Segoe UI", FontSize = 32f }),
            // ' = m' - Normal
            (1, 4, new CharacterFormat { FontFamily = "Segoe UI", FontSize = 32f }),
            // 'c' - Kursiv
            (4, 5, new CharacterFormat {
                FontFamily = "Segoe UI",
                FontSize = 32f,
                Italic = true
            }),
            // '2' - Superscript (klein, nach oben)
            (5, 6, new CharacterFormat {
                FontFamily = "Segoe UI",
                FontSize = 16f,
                BaselineOffsetY = -8f  // Nach oben versetzt
            }),
        };

        var geometries = TextToLineSegments.ConvertTextToLineSegmentsWithFormat(
            text, formats, 0, 40, 0.3f
        );

        Console.WriteLine($"\nWissenschaftliche Formel: {geometries.Count} Zeichen");
        foreach (var geo in geometries)
            Console.WriteLine($"  '{geo.Character}': {geo.LineSegments.Count} Segmente");
    }

    /// <summary>
    /// Führe alle Beispiele aus
    /// </summary>
    public static void RunAllExamples()
    {
        Console.WriteLine("╔════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║     Formatierte Text-zu-Liniensegmente Beispiele          ║");
        Console.WriteLine("╚════════════════════════════════════════════════════════════╝\n");

        try
        {
            Example1_DifferentSizes();
            Example2_DifferentFonts();
            Example3_Superscript();
            Example4_Tracking();
            Example5_Scaling();
            Example6_MultiColor();
            ExampleComplex_ScientificFormula();

            Console.WriteLine("\n✓ Alle Beispiele erfolgreich ausgeführt!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n✗ Fehler: {ex.Message}");
        }
    }
}
