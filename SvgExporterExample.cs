using System;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace NCHops;

/// <summary>
/// Beispiele für die Verwendung des SvgExporters
/// Diese Klasse zeigt, wie man G-Code und Textkonturlinien als SVG exportiert
/// </summary>
public static class SvgExporterExample
{
    /// <summary>
    /// Beispiel 1: Exportiert Text mit Konturlinien als SVG
    /// </summary>
    public static void ExportTextToSvg()
    {
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

        var saveDialog = new SaveFileDialog
        {
            Filter = "SVG Files (*.svg)|*.svg|All Files (*.*)|*.*",
            DefaultExt = ".svg",
            FileName = "text-export.svg"
        };

        if (saveDialog.ShowDialog() == true)
        {
            SvgExporter.ExportTextAndGCode(
                saveDialog.FileName,
                textParams,
                gCode: null,  // Kein G-Code
                workWidth: 200.0,
                workHeight: 100.0
            );
        }
    }

    /// <summary>
    /// Beispiel 2: Exportiert G-Code als SVG-Pfade
    /// </summary>
    public static void ExportGCodeToSvg()
    {
        string sampleGCode = @"
(Beispiel G-Code)
G00 Z5.0000
G00 X10.0000 Y10.0000
G01 Z-2.0000 F100
G01 X20.0000 F100
G01 Y20.0000 F100
G01 X10.0000 F100
G01 Y10.0000 F100
G00 Z5.0000
";

        var saveDialog = new SaveFileDialog
        {
            Filter = "SVG Files (*.svg)|*.svg|All Files (*.*)|*.*",
            DefaultExt = ".svg",
            FileName = "gcode-export.svg"
        };

        if (saveDialog.ShowDialog() == true)
        {
            SvgExporter.ExportGCodePaths(
                saveDialog.FileName,
                sampleGCode,
                workWidth: 100.0,
                workHeight: 100.0,
                title: "G-Code Visualization"
            );
        }
    }

    /// <summary>
    /// Beispiel 3: Exportiert Text + G-Code kombiniert
    /// </summary>
    public static void ExportCombinedToSvg(GraviereParams textParams, string gCode)
    {
        var saveDialog = new SaveFileDialog
        {
            Filter = "SVG Files (*.svg)|*.svg|All Files (*.*)|*.*",
            DefaultExt = ".svg",
            FileName = $"export-{textParams.Text}.svg"
        };

        if (saveDialog.ShowDialog() == true)
        {
            try
            {
                SvgExporter.ExportCombined(
                    saveDialog.FileName,
                    gCode,
                    textParams,
                    workWidth: 200.0,
                    workHeight: 100.0
                );

                System.Windows.MessageBox.Show(
                    $"SVG erfolgreich exportiert:\n{saveDialog.FileName}",
                    "Export erfolgreich",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information
                );
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Fehler beim Exportieren:\n{ex.Message}",
                    "Export-Fehler",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error
                );
            }
        }
    }

    /// <summary>
    /// Exportiert einen kompletten Gravur-Auftrag:
    /// - Text-Konturlinien
    /// - Werkzeugbahn (G-Code)
    /// - Werkstück-Grenzen
    /// - Metadaten
    /// </summary>
    public static void ExportCompleteJob(GraviereParams textParams, string gCode, double workWidth, double workHeight)
    {
        var saveDialog = new SaveFileDialog
        {
            Filter = "SVG Files (*.svg)|*.svg",
            DefaultExt = ".svg",
            FileName = $"job-{DateTime.Now:yyyy-MM-dd-HHmmss}.svg"
        };

        if (saveDialog.ShowDialog() != true)
            return;

        try
        {
            SvgExporter.ExportCombined(
                saveDialog.FileName,
                gCode,
                textParams,
                workWidth,
                workHeight
            );

            // Optional: Auch als GCode-Datei speichern
            var gCodePath = Path.ChangeExtension(saveDialog.FileName, ".gcode");
            File.WriteAllText(gCodePath, gCode, Encoding.UTF8);

            System.Windows.MessageBox.Show(
                $"Export abgeschlossen:\n" +
                $"  SVG: {saveDialog.FileName}\n" +
                $"  G-Code: {gCodePath}",
                "Export erfolgreich",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information
            );
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Fehler: {ex.Message}",
                "Fehler",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error
            );
        }
    }

    /// <summary>
    /// Batch-Export: Exportiert mehrere Texte als SVG-Dateien
    /// </summary>
    public static void BatchExportTexts(string outputDirectory, params (GraviereParams textParams, string gCode)[] jobs)
    {
        if (!Directory.Exists(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        int successCount = 0;
        int failureCount = 0;

        foreach (var (textParams, gCode) in jobs)
        {
            try
            {
                string filename = Path.Combine(outputDirectory, $"{textParams.Text}.svg");
                SvgExporter.ExportCombined(
                    filename,
                    gCode,
                    textParams,
                    200.0,
                    100.0
                );
                successCount++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fehler bei '{textParams.Text}': {ex.Message}");
                failureCount++;
            }
        }

        Console.WriteLine($"Batch-Export abgeschlossen: {successCount} erfolgreich, {failureCount} Fehler");
    }
}
