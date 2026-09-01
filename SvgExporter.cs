using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using SkiaSharp;

namespace NCHops;

/// <summary>
/// Exportiert G-Code Pfade und Text-Konturlinien als SVG Datei.
/// SVG kann zum Visualisieren des Werkstücks vor der Bearbeitung verwendet werden.
/// </summary>
public static class SvgExporter
{
    private const string SvgNamespace = "http://www.w3.org/2000/svg";
    private static readonly CultureInfo InvariantCulture = CultureInfo.InvariantCulture;

    /// <summary>
    /// Exportiert Text mit Konturlinien und optional G-Code als SVG
    /// </summary>
    /// <param name="filePath">Ziel-Dateipfad</param>
    /// <param name="textParams">Text-Parameter</param>
    /// <param name="gCode">Optional: G-Code String zur Visualisierung</param>
    /// <param name="workWidth">Werkstück-Breite in mm</param>
    /// <param name="workHeight">Werkstück-Höhe in mm</param>
    public static void ExportTextAndGCode(
        string filePath,
        GraviereParams textParams,
        string? gCode,
        double workWidth,
        double workHeight)
    {
        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", "no"),
            CreateSvgRoot(textParams, gCode, workWidth, workHeight)
        );

        using var writer = XmlWriter.Create(filePath, new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            Encoding = Encoding.UTF8
        });

        doc.WriteTo(writer);
    }

    /// <summary>
    /// Exportiert G-Code als SVG-Pfade (zum Visualisieren der Werkzeugbahn)
    /// </summary>
    public static void ExportGCodePaths(
        string filePath,
        string gCode,
        double workWidth,
        double workHeight,
        string? title = null)
    {
        var paths = ParseGCodePaths(gCode);

        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", "no"),
            CreateGCodeSvgRoot(paths, workWidth, workHeight, title)
        );

        using var writer = XmlWriter.Create(filePath, new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            Encoding = Encoding.UTF8
        });

        doc.WriteTo(writer);
    }

    /// <summary>
    /// Erstellt das SVG Root-Element mit Text-Konturlinien und G-Code
    /// </summary>
    private static XElement CreateSvgRoot(
        GraviereParams textParams,
        string? gCode,
        double workWidth,
        double workHeight)
    {
        double margin = 20;
        double svgWidth = workWidth + margin * 2;
        double svgHeight = workHeight + margin * 2;

        var root = new XElement(XName.Get("svg", SvgNamespace),
            new XAttribute("width", svgWidth.ToString("F2", InvariantCulture)),
            new XAttribute("height", svgHeight.ToString("F2", InvariantCulture)),
            new XAttribute("viewBox", $"0 0 {svgWidth:F2} {svgHeight:F2}"),
            new XAttribute("xmlns", SvgNamespace),
            new XAttribute("xmlns:xlink", "http://www.w3.org/1999/xlink")
        );

        // Titel
        var title = new XElement(XName.Get("title", SvgNamespace),
            $"Text Export: {textParams.Text}");
        root.Add(title);

        // Defs (für Stile und Filter)
        var defs = new XElement(XName.Get("defs", SvgNamespace));
        AddStyleDefs(defs);
        root.Add(defs);

        // Hintergrund (Werkstückgrenzen)
        var rect = new XElement(XName.Get("rect", SvgNamespace),
            new XAttribute("x", margin.ToString("F2", InvariantCulture)),
            new XAttribute("y", margin.ToString("F2", InvariantCulture)),
            new XAttribute("width", workWidth.ToString("F2", InvariantCulture)),
            new XAttribute("height", workHeight.ToString("F2", InvariantCulture)),
            new XAttribute("class", "workpiece")
        );
        root.Add(rect);

        // Gruppe für Text-Konturlinien
        var textGroup = new XElement(XName.Get("g", SvgNamespace),
            new XAttribute("id", "text-contours"),
            new XAttribute("class", "text-layer")
        );

        // Text-Konturlinien konvertieren
        AddTextContours(textGroup, textParams, margin);
        root.Add(textGroup);

        // Wenn G-Code vorhanden: Werkzeugbahn visualisieren
        if (!string.IsNullOrEmpty(gCode))
        {
            var gcodeGroup = new XElement(XName.Get("g", SvgNamespace),
                new XAttribute("id", "gcode-paths"),
                new XAttribute("class", "gcode-layer")
            );

            var paths = ParseGCodePaths(gCode);
            AddGCodePathsToGroup(gcodeGroup, paths, margin);
            root.Add(gcodeGroup);
        }

        // Text-Information hinzufügen
        AddTextInfo(root, textParams, margin);

        return root;
    }

    /// <summary>
    /// Erstellt ein SVG-Dokument nur für G-Code Pfade
    /// </summary>
    private static XElement CreateGCodeSvgRoot(
        List<(List<(double x, double y)> points, string type)> paths,
        double workWidth,
        double workHeight,
        string? title)
    {
        double margin = 20;
        double svgWidth = workWidth + margin * 2;
        double svgHeight = workHeight + margin * 2;

        var root = new XElement(XName.Get("svg", SvgNamespace),
            new XAttribute("width", svgWidth.ToString("F2", InvariantCulture)),
            new XAttribute("height", svgHeight.ToString("F2", InvariantCulture)),
            new XAttribute("viewBox", $"0 0 {svgWidth:F2} {svgHeight:F2}"),
            new XAttribute("xmlns", SvgNamespace)
        );

        var titleElem = new XElement(XName.Get("title", SvgNamespace),
            title ?? "G-Code Visualization");
        root.Add(titleElem);

        var defs = new XElement(XName.Get("defs", SvgNamespace));
        AddStyleDefs(defs);
        root.Add(defs);

        // Werkstück-Grenzen
        var rect = new XElement(XName.Get("rect", SvgNamespace),
            new XAttribute("x", margin.ToString("F2", InvariantCulture)),
            new XAttribute("y", margin.ToString("F2", InvariantCulture)),
            new XAttribute("width", workWidth.ToString("F2", InvariantCulture)),
            new XAttribute("height", workHeight.ToString("F2", InvariantCulture)),
            new XAttribute("class", "workpiece")
        );
        root.Add(rect);

        var pathGroup = new XElement(XName.Get("g", SvgNamespace),
            new XAttribute("id", "gcode-paths")
        );

        AddGCodePathsToGroup(pathGroup, paths, margin);
        root.Add(pathGroup);

        return root;
    }

    /// <summary>
    /// Parst G-Code und extrahiert X,Y Koordinaten als Pfade
    /// </summary>
    private static List<(List<(double x, double y)> points, string type)> ParseGCodePaths(string gCode)
    {
        var paths = new List<(List<(double x, double y)> points, string type)>();
        var currentPath = new List<(double x, double y)>();
        double currentX = 0, currentY = 0;

        var lines = gCode.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // Kommentare überspringen
            if (trimmed.StartsWith("(") || trimmed.StartsWith(";") || string.IsNullOrWhiteSpace(trimmed))
                continue;

            // G00 = Eilgang (schnelle Bewegung) → neue Bahn
            if (trimmed.StartsWith("G00"))
            {
                if (currentPath.Count > 0)
                {
                    paths.Add((currentPath, "cut"));
                    currentPath = new List<(double, double)>();
                }
            }

            // Koordinaten extrahieren (vereinfachte Regex)
            var x = ExtractGCodeValue(trimmed, 'X');
            var y = ExtractGCodeValue(trimmed, 'Y');

            if (x.HasValue)
                currentX = x.Value;
            if (y.HasValue)
                currentY = y.Value;

            // Punkt hinzufügen wenn G01 oder G02/G03 (Schnitt/Bogenfräsung)
            if (trimmed.StartsWith("G01") || trimmed.StartsWith("G02") || trimmed.StartsWith("G03"))
            {
                currentPath.Add((currentX, currentY));
            }
        }

        // Letzte Bahn hinzufügen
        if (currentPath.Count > 0)
            paths.Add((currentPath, "cut"));

        return paths;
    }

    /// <summary>
    /// Extrahiert einen numerischen Wert aus einem G-Code Befehl
    /// z.B. "X10.5 Y20.3 F200" → ExtractGCodeValue(..., 'X') → 10.5
    /// </summary>
    private static double? ExtractGCodeValue(string line, char axis)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            line,
            $@"{axis}([-+]?\d*\.?\d+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );

        if (match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, InvariantCulture, out var value))
            return value;

        return null;
    }

    /// <summary>
    /// Fügt Stil-Definitionen hinzu
    /// </summary>
    private static void AddStyleDefs(XElement defs)
    {
        var style = new XElement(XName.Get("style", SvgNamespace),
            @"
.workpiece {
    fill: none;
    stroke: #333;
    stroke-width: 2;
    stroke-dasharray: 5,5;
}
.text-layer path {
    fill: none;
    stroke: #0066cc;
    stroke-width: 1.5;
    stroke-linecap: round;
    stroke-linejoin: round;
}
.text-layer text {
    font-size: 12px;
    fill: #0066cc;
}
.gcode-layer path {
    fill: none;
    stroke: #cc0000;
    stroke-width: 1;
    opacity: 0.7;
}
.info-text {
    font-size: 10px;
    fill: #666;
    font-family: monospace;
}
.marker {
    fill: #0066cc;
}
.marker-circle {
    fill: none;
    stroke: #0066cc;
    stroke-width: 0.5;
}
"
        );

        defs.Add(style);
    }

    /// <summary>
    /// Konvertiert Text-Parameter in SVG Konturlinien
    /// Dies ist eine vereinfachte Version - für echte Konturlinien
    /// würde man SkiaSharp verwenden um die genauen Kurven zu extrahieren
    /// </summary>
    private static void AddTextContours(XElement group, GraviereParams textParams, double margin)
    {
        // Berechne Text-Bounding-Box
        float x = (float)(textParams.XRel + margin);
        float y = (float)(textParams.YRel + margin);

        // Erstelle SKPaint für Textmessung
        using var paint = new SKPaint
        {
            TextSize = (float)textParams.FontSizeMm,
            Typeface = SKTypeface.FromFamilyName(textParams.FontFamily ?? "Arial")
        };

        // TODO: Erstelle SKPath für Text-Konturlinien
        // Note: AddString is no longer available in current SkiaSharp version
        // using var path = new SKPath();
        // path.AddString(...);

        var pathData = "";  // Placeholder
        if (!string.IsNullOrEmpty(pathData))
        {
            var pathElem = new XElement(XName.Get("path", SvgNamespace),
                new XAttribute("d", pathData),
                new XAttribute("id", "text-path"),
                new XAttribute("fill", "none"),
                new XAttribute("stroke", "#0066cc"),
                new XAttribute("stroke-width", "1")
            );
            group.Add(pathElem);
        }

        // Text-Label hinzufügen
        var textElem = new XElement(XName.Get("text", SvgNamespace),
            new XAttribute("x", x.ToString("F2", InvariantCulture)),
            new XAttribute("y", (y + textParams.FontSizeMm).ToString("F2", InvariantCulture)),
            new XAttribute("font-family", textParams.FontFamily ?? "Arial"),
            new XAttribute("font-size", textParams.FontSizeMm.ToString("F1", InvariantCulture)),
            new XAttribute("fill", "#0066cc"),
            new XAttribute("opacity", "0.7"),
            textParams.Text
        );
        group.Add(textElem);
    }

    /// <summary>
    /// Konvertiert SkiaSharp SKPath zu SVG path data string
    /// </summary>
    private static string ConvertSkPathToSvgPath(SKPath skPath)
    {
        if (skPath == null)
            return string.Empty;

        var sb = new StringBuilder();
        var iterator = skPath.CreateRawIterator();
        SKPoint[] points = new SKPoint[4];

        SKPathVerb verb;
        while ((verb = iterator.Next(points)) != SKPathVerb.Done)
        {
            switch (verb)
            {
                case SKPathVerb.Move:
                    sb.Append($"M {points[0].X:F2} {points[0].Y:F2} ");
                    break;

                case SKPathVerb.Line:
                    sb.Append($"L {points[1].X:F2} {points[1].Y:F2} ");
                    break;

                case SKPathVerb.Quad:
                    sb.Append($"Q {points[1].X:F2} {points[1].Y:F2} {points[2].X:F2} {points[2].Y:F2} ");
                    break;

                case SKPathVerb.Conic:
                    // Für Vereinfachung als Quad behandeln
                    sb.Append($"Q {points[1].X:F2} {points[1].Y:F2} {points[2].X:F2} {points[2].Y:F2} ");
                    break;

                case SKPathVerb.Cubic:
                    sb.Append($"C {points[1].X:F2} {points[1].Y:F2} {points[2].X:F2} {points[2].Y:F2} {points[3].X:F2} {points[3].Y:F2} ");
                    break;

                case SKPathVerb.Close:
                    sb.Append("Z ");
                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Fügt G-Code Pfade zur SVG Gruppe hinzu
    /// </summary>
    private static void AddGCodePathsToGroup(
        XElement group,
        List<(List<(double x, double y)> points, string type)> paths,
        double margin)
    {
        int pathIndex = 0;
        foreach (var (points, type) in paths)
        {
            if (points.Count < 2)
                continue;

            // Konvertiere Punkte zu SVG Path Data
            var sb = new StringBuilder();
            sb.Append($"M {points[0].x + margin:F2} {points[0].y + margin:F2} ");

            for (int i = 1; i < points.Count; i++)
            {
                sb.Append($"L {points[i].x + margin:F2} {points[i].y + margin:F2} ");
            }

            var pathElem = new XElement(XName.Get("path", SvgNamespace),
                new XAttribute("d", sb.ToString()),
                new XAttribute("id", $"gcode-path-{pathIndex}"),
                new XAttribute("class", "gcode-path"),
                new XAttribute("stroke", "#cc0000"),
                new XAttribute("stroke-width", "1"),
                new XAttribute("fill", "none"),
                new XAttribute("opacity", "0.7")
            );

            group.Add(pathElem);
            pathIndex++;
        }
    }

    /// <summary>
    /// Fügt Text-Informationen als Kommentar hinzu
    /// </summary>
    private static void AddTextInfo(XElement root, GraviereParams textParams, double margin)
    {
        var infoGroup = new XElement(XName.Get("g", SvgNamespace),
            new XAttribute("id", "info")
        );

        var infos = new[]
        {
            $"Text: {textParams.Text}",
            $"Font: {textParams.FontFamily}",
            $"Size: {textParams.FontSizeMm:F1} mm",
            $"Position: X={textParams.XRel:F1}, Y={textParams.YRel:F1}",
            $"Field Width: {textParams.TextBreite:F1} mm",
            $"Field Height: {textParams.TextHoehe:F1} mm"
        };

        float infoY = (float)margin;
        float infoX = (float)margin;

        foreach (var info in infos)
        {
            var textElem = new XElement(XName.Get("text", SvgNamespace),
                new XAttribute("x", infoX.ToString("F2", InvariantCulture)),
                new XAttribute("y", infoY.ToString("F2", InvariantCulture)),
                new XAttribute("class", "info-text"),
                info
            );
            infoGroup.Add(textElem);
            infoY += 12;
        }

        root.Add(infoGroup);
    }

    /// <summary>
    /// Exportiert G-Code und Textkonturlinien in eine kombinierte SVG
    /// </summary>
    /// <param name="filePath">Ziel-Dateipfad</param>
    /// <param name="gCode">G-Code String</param>
    /// <param name="textParams">Text-Parameter</param>
    /// <param name="workWidth">Werkstück-Breite</param>
    /// <param name="workHeight">Werkstück-Höhe</param>
    public static void ExportCombined(
        string filePath,
        string gCode,
        GraviereParams textParams,
        double workWidth,
        double workHeight)
    {
        ExportTextAndGCode(filePath, textParams, gCode, workWidth, workHeight);
    }
}
