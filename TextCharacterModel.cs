using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace NCHops;

/// <summary>
/// Speichert Formatierung für ein einzelnes Zeichen (Font, Größe, Farbe, etc.)
/// Mit Value-Type-Semantik für Caching und Vergleich
/// </summary>
public class TextCharacterFormat : IEquatable<TextCharacterFormat>
{
    public string FontFamily { get; set; } = "Segoe UI";
    public float FontSizePt { get; set; } = 12f;
    public SKColor Color { get; set; } = SKColors.Black;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public float BaselineShift { get; set; } = 0f;  // % der FontSize
    public float Tracking { get; set; } = 0f;       // mm Zeichen-Abstand
    public float LineHeight { get; set; } = 0f;     // mm Zeilenabstand (0 = automatisch)

    public override bool Equals(object? obj) => Equals(obj as TextCharacterFormat);

    public bool Equals(TextCharacterFormat? other)
    {
        if (other is null) return false;
        return FontFamily == other.FontFamily
            && Math.Abs(FontSizePt - other.FontSizePt) < 0.01f
            && Color == other.Color
            && Bold == other.Bold
            && Italic == other.Italic
            && Math.Abs(BaselineShift - other.BaselineShift) < 0.01f
            && Math.Abs(Tracking - other.Tracking) < 0.001f
            && Math.Abs(LineHeight - other.LineHeight) < 0.001f;
    }

    public override int GetHashCode()
        => HashCode.Combine(FontFamily, FontSizePt, Color, Bold, Italic);

    public TextCharacterFormat Clone() => new()
    {
        FontFamily = FontFamily,
        FontSizePt = FontSizePt,
        Color = Color,
        Bold = Bold,
        Italic = Italic,
        BaselineShift = BaselineShift,
        Tracking = Tracking,
        LineHeight = LineHeight
    };
}

/// <summary>
/// Ein einzelnes Zeichen mit seiner Formatierung
/// </summary>
public class TextCharacter
{
    public char Value { get; set; }
    public TextCharacterFormat Format { get; set; }

    public TextCharacter(char value = ' ', TextCharacterFormat? format = null)
    {
        Value = value;
        Format = format ?? new TextCharacterFormat();
    }

    public TextCharacter Clone() => new(Value, Format.Clone());
}

/// <summary>
/// Eine "FormattedTextRun" - zusammenhängende Zeichen mit gleicher Formatierung.
/// Wird dynamisch aus TextCharacter[] generiert zur Rendering-Optimierung
/// </summary>
public class FormattedTextRun
{
    public string Text { get; set; } = "";
    public TextCharacterFormat Format { get; set; } = new();

    public FormattedTextRun Clone() => new()
    {
        Text = Text,
        Format = Format.Clone()
    };
}

/// <summary>
/// Zentrale Text-Datenstruktur: Character-basiert + intelligentes Run-Caching
/// </summary>
public class SkiaTextModel
{
    private List<TextCharacter> _characters = [];
    private List<FormattedTextRun> _cachedRuns = [];
    private bool _runsNeedUpdate = true;

    // Typeface-Cache (pro FontFamily, da sehr teuer in SkiaSharp)
    private static Dictionary<string, SKTypeface> _typefaceCache = new();

    public IReadOnlyList<TextCharacter> Characters => _characters.AsReadOnly();
    public int CharacterCount => _characters.Count;

    /// <summary>
    /// Liefert gecachte FormattedTextRuns (aggregiert Zeichen mit gleichem Format)
    /// </summary>
    public List<FormattedTextRun> GetRuns()
    {
        if (_runsNeedUpdate)
            RebuildRuns();
        return _cachedRuns;
    }

    /// <summary>
    /// Lazy-Rebuild: Nur wenn Format sich ändert
    /// </summary>
    private void RebuildRuns()
    {
        _cachedRuns.Clear();
        if (_characters.Count == 0)
            return;

        var currentRun = new FormattedTextRun
        {
            Text = "",
            Format = _characters[0].Format.Clone()
        };

        foreach (var ch in _characters)
        {
            if (ch.Format.Equals(currentRun.Format))
            {
                currentRun.Text += ch.Value;
            }
            else
            {
                if (!string.IsNullOrEmpty(currentRun.Text))
                    _cachedRuns.Add(currentRun);
                currentRun = new FormattedTextRun
                {
                    Text = ch.Value.ToString(),
                    Format = ch.Format.Clone()
                };
            }
        }

        if (!string.IsNullOrEmpty(currentRun.Text))
            _cachedRuns.Add(currentRun);

        _runsNeedUpdate = false;
    }

    /// <summary>
    /// Text mit einheitlichem Format setzen (z.B. beim Laden)
    /// </summary>
    public void SetText(string text, TextCharacterFormat? format = null)
    {
        _characters.Clear();
        var fmt = format ?? new TextCharacterFormat();
        foreach (char c in text)
            _characters.Add(new TextCharacter(c, fmt.Clone()));
        _runsNeedUpdate = true;
    }

    /// <summary>
    /// Zeichen an Position einfügen
    /// </summary>
    public void InsertChar(int pos, char c, TextCharacterFormat? format = null)
    {
        if (pos < 0 || pos > _characters.Count)
            return;

        var fmt = format;
        if (fmt == null)
        {
            if (_characters.Count > 0)
                fmt = _characters[Math.Min(pos, _characters.Count - 1)].Format.Clone();
            else
                fmt = new TextCharacterFormat();
        }

        _characters.Insert(pos, new TextCharacter(c, fmt));
        _runsNeedUpdate = true;
    }

    /// <summary>
    /// Zeichen bei Position löschen
    /// </summary>
    public void DeleteCharAt(int pos)
    {
        if (pos < 0 || pos >= _characters.Count)
            return;
        _characters.RemoveAt(pos);
        _runsNeedUpdate = true;
    }

    /// <summary>
    /// Formatierung für ein Zeichen oder Range setzen
    /// </summary>
    public void SetFormat(int startPos, int length, TextCharacterFormat format)
    {
        for (int i = startPos; i < startPos + length && i < _characters.Count; i++)
        {
            if (i >= 0)
                _characters[i].Format = format.Clone();
        }
        _runsNeedUpdate = true;
    }

    /// <summary>
    /// Alle Zeichen erhalten ein Format
    /// </summary>
    public void SetFormatAll(TextCharacterFormat format)
    {
        SetFormat(0, _characters.Count, format);
    }

    /// <summary>
    /// Nur die Schriftgröße für alle Zeichen ändern (andere Formatierung erhalten)
    /// </summary>
    public void UpdateFontSizeAll(float newFontSizePt)
    {
        foreach (var ch in _characters)
        {
            ch.Format.FontSizePt = newFontSizePt;
        }
        _runsNeedUpdate = true;
    }

    /// <summary>
    /// Text mit Range extrahieren
    /// </summary>
    public string GetText(int startPos = 0, int length = -1)
    {
        if (length < 0)
            length = _characters.Count - startPos;

        var chars = _characters
            .Skip(startPos)
            .Take(length)
            .Select(c => c.Value);
        return new string(chars.ToArray());
    }

    /// <summary>
    /// Zeichen-Range kopieren (mit Formatierung)
    /// </summary>
    public List<TextCharacter> GetCharacterRange(int startPos, int length)
    {
        return _characters
            .Skip(startPos)
            .Take(length)
            .Select(c => c.Clone())
            .ToList();
    }

    /// <summary>
    /// Zeichen bei Position einfügen
    /// </summary>
    public void InsertRange(int pos, List<TextCharacter> chars)
    {
        _characters.InsertRange(pos, chars.Select(c => c.Clone()));
        _runsNeedUpdate = true;
    }

    /// <summary>
    /// Typeface mit Caching laden
    /// </summary>
    public static SKTypeface GetTypeface(string fontFamily, bool bold = false, bool italic = false)
    {
        string key = $"{fontFamily}|{(bold ? "B" : "")}|{(italic ? "I" : "")}";

        if (_typefaceCache.TryGetValue(key, out var tf))
            return tf;

        // SkiaSharp hat keine einfache Bold/Italic API, daher nur Fontfamily
        tf = SKTypeface.FromFamilyName(fontFamily) ?? SKTypeface.Default;
        _typefaceCache[key] = tf;
        return tf;
    }

    /// <summary>
    /// Model komplett leeren
    /// </summary>
    public void Clear()
    {
        _characters.Clear();
        _cachedRuns.Clear();
        _runsNeedUpdate = true;
    }

    /// <summary>
    /// Model klonen (für Undo/Redo)
    /// </summary>
    public SkiaTextModel Clone()
    {
        var model = new SkiaTextModel();
        model._characters = _characters.Select(c => c.Clone()).ToList();
        model._runsNeedUpdate = true;
        return model;
    }
}
