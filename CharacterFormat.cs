using SkiaSharp;

namespace NCHops;

/// <summary>
/// Definiert die Formatierung eines einzelnen Buchstabens oder Textabschnitts.
/// Unterstützt Schriftart, Größe, Stil, Farbe und weitere Eigenschaften.
/// </summary>
public class CharacterFormat
{
    /// <summary>
    /// Schriftfamilie (z.B. "Arial", "Segoe UI", "Times New Roman")
    /// </summary>
    public string FontFamily { get; set; } = "Segoe UI";

    /// <summary>
    /// Schriftgröße in Punkten
    /// </summary>
    public float FontSize { get; set; } = 12f;

    /// <summary>
    /// Buchstabenfarbe
    /// </summary>
    public SKColor Color { get; set; } = SKColors.Black;

    /// <summary>
    /// Ist der Text fett (Bold)?
    /// </summary>
    public bool Bold { get; set; } = false;

    /// <summary>
    /// Ist der Text kursiv (Italic)?
    /// </summary>
    public bool Italic { get; set; } = false;

    /// <summary>
    /// Ist der Text unterstrichen?
    /// </summary>
    public bool Underline { get; set; } = false;

    /// <summary>
    /// Ist der Text durchgestrichen?
    /// </summary>
    public bool Strikethrough { get; set; } = false;

    /// <summary>
    /// Buchstabenabstand (Tracking) in Einheiten
    /// </summary>
    public float Tracking { get; set; } = 0f;

    /// <summary>
    /// Zeilenabstand Multiplier
    /// </summary>
    public float LineHeight { get; set; } = 1.0f;

    /// <summary>
    /// Horizontale Skalierung (1.0 = normal, 0.5 = halb, 2.0 = doppelt)
    /// </summary>
    public float ScaleX { get; set; } = 1.0f;

    /// <summary>
    /// Schräge (Skew) für pseudo-kursiv Effekt
    /// </summary>
    public float SkewX { get; set; } = 0f;

    /// <summary>
    /// Basis-Versatz in X-Richtung (für High/Low-Text)
    /// </summary>
    public float BaselineOffsetX { get; set; } = 0f;

    /// <summary>
    /// Basis-Versatz in Y-Richtung (für Superscript/Subscript)
    /// </summary>
    public float BaselineOffsetY { get; set; } = 0f;

    /// <summary>
    /// Rotationswinkel in Grad
    /// </summary>
    public float RotationDegrees { get; set; } = 0f;

    /// <summary>
    /// Opazität (0.0 = transparent, 1.0 = opak)
    /// </summary>
    public float Opacity { get; set; } = 1.0f;

    /// <summary>
    /// Erstellt eine tiefe Kopie dieser Formatierung
    /// </summary>
    public CharacterFormat Clone()
    {
        return new CharacterFormat
        {
            FontFamily = FontFamily,
            FontSize = FontSize,
            Color = Color,
            Bold = Bold,
            Italic = Italic,
            Underline = Underline,
            Strikethrough = Strikethrough,
            Tracking = Tracking,
            LineHeight = LineHeight,
            ScaleX = ScaleX,
            SkewX = SkewX,
            BaselineOffsetX = BaselineOffsetX,
            BaselineOffsetY = BaselineOffsetY,
            RotationDegrees = RotationDegrees,
            Opacity = Opacity
        };
    }

    /// <summary>
    /// Vergleicht zwei Formate auf Gleichheit
    /// </summary>
    public bool Equals(CharacterFormat other)
    {
        if (other == null) return false;
        return FontFamily == other.FontFamily
            && FontSize == other.FontSize
            && Color == other.Color
            && Bold == other.Bold
            && Italic == other.Italic
            && Underline == other.Underline
            && Strikethrough == other.Strikethrough
            && Tracking == other.Tracking
            && LineHeight == other.LineHeight
            && ScaleX == other.ScaleX
            && SkewX == other.SkewX
            && BaselineOffsetX == other.BaselineOffsetX
            && BaselineOffsetY == other.BaselineOffsetY
            && RotationDegrees == other.RotationDegrees
            && Opacity == other.Opacity;
    }

    public override string ToString()
    {
        return $"{FontFamily} {FontSize}pt {(Bold ? "Bold " : "")}{(Italic ? "Italic " : "")}";
    }
}
