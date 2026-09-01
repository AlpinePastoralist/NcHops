using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace NCHops;

/// <summary>
/// Event-Args für Text-Änderungen (kompatibel mit altem SkiaTextEditor)
/// </summary>
public class ImprovedSkiaTextEditorTextChangedEventArgs : EventArgs { }

/// <summary>
/// Verbesserte Text-Editor-Komponente mit:
/// - Character-basiertem Datenmodell
/// - Layout-Engine (Zeilenumbruch, Alignment)
/// - Multi-Format Support
/// - Performance-optimiert
///
/// Prototype für professionelles Textfeld-Werkzeug
/// </summary>
public class ImprovedSkiaTextEditor : SKElement
{
    // ─── Datenmodell & Layout ───────────────────────────────────────
    private SkiaTextModel _model = new();
    private TextLayoutEngine _layoutEngine = new();

    // ─── Cursor & Selection ──────────────────────────────────────────
    private int _cursorPos = 0;
    private int _selectionStart = -1;
    private int _selectionEnd = -1;
    private bool _hasFocus = false;
    private System.Windows.Threading.DispatcherTimer? _cursorBlinkTimer;
    private bool _cursorVisible = true;

    // ─── Rendering ──────────────────────────────────────────────────
    private double _zoom = 1.0;
    private float _scaledPadding = 4f;
    private const float Padding = 4f;
    private TextCharacterFormat _defaultFormat = new();  // Standard-Format für neue Zeichen

    // ─── Layout Settings ────────────────────────────────────────────
    public TextHorizontalAlign HorizontalAlign { get; set; } = TextHorizontalAlign.Left;
    public TextVerticalAlign VerticalAlign { get; set; } = TextVerticalAlign.Top;

    // ─── Events & Properties ────────────────────────────────────────
    public event EventHandler<ImprovedSkiaTextEditorTextChangedEventArgs>? TextChanged;

    public ImprovedSkiaTextEditor()
    {
        Focusable = true;

        // Initialisiere _defaultFormat mit Standard-Werten
        _defaultFormat = new TextCharacterFormat
        {
            FontFamily = "Segoe UI",
            FontSizePt = 12f,
            Color = SKColors.White
        };

        PreviewMouseLeftButtonDown += OnMouseDown;
        PreviewMouseMove += OnMouseMove;
        PreviewMouseLeftButtonUp += OnMouseUp;
        PreviewKeyDown += OnKeyDown;

        GotFocus += (s, e) =>
        {
            _hasFocus = true;
            StartCursorBlink();
            InvalidateVisual();
        };

        LostFocus += (s, e) =>
        {
            _hasFocus = false;
            StopCursorBlink();
            InvalidateVisual();
        };

        PaintSurface += OnPaintSurface;
    }

    /// <summary>
    /// Haupt-Rendering-Methode mit Layout-Engine
    /// </summary>
    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        if (e.Info.Width == 0 || e.Info.Height == 0)
            return;

        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        // ─── Border um das Eingabefeld IMMER zeichnen ──────────────────
        // Die physische Canvas-Größe (e.Info.Width/Height) ist IMMER die Wahrheit
        // Der Rahmen sollte die volle Canvas-Größe in physischen Pixeln ausfüllen
        float dpiScale = 1.0f;  // Canvas coordinates are already in physical pixels

        DrawFieldBorder(canvas, e.Info.Width, e.Info.Height, dpiScale);

        // ─── Früh rückgängig wenn kein Text vorhanden ──────────────────
        if (_model.CharacterCount == 0)
            return;

        // ─── Layout berechnen ───────────────────────────────────────
        // Arbeite direkt mit physischen Pixeln (e.Info.Width/Height sind physische Pixel)
        // Der Text sollte sich von padding bis (width - padding) in physischen Pixeln ausdehnen
        float layoutWidth = e.Info.Width - _scaledPadding * 2;
        float layoutHeight = e.Info.Height - _scaledPadding * 2;

        _layoutEngine.Layout(
            _model,
            Math.Max(layoutWidth, 1),  // Ensure minimum width
            Math.Max(layoutHeight, 1),
            HorizontalAlign,
            VerticalAlign
        );

        // ─── Text rendern (nach Zeilen) ─────────────────────────────
        using var textPaint = new SKPaint
        {
            TextSize = 12f,
            IsAntialias = true,
            Typeface = SKTypeface.Default
        };

        foreach (var line in _layoutEngine.Lines)
        {
            float lineX = _scaledPadding + line.LineX;
            float lineY = _scaledPadding + line.LineY + line.Ascent;

            // Character-weise rendern (für Selection-Highlight)
            for (int i = line.StartCharIdx; i < line.EndCharIdx; i++)
            {
                var ch = _model.Characters[i];

                // Formatting anwenden
                textPaint.Typeface = SkiaTextModel.GetTypeface(
                    ch.Format.FontFamily,
                    ch.Format.Bold,
                    ch.Format.Italic
                );
                textPaint.TextSize = ch.Format.FontSizePt;
                textPaint.Color = ch.Format.Color;

                // Selection-Highlight
                if (IsCharInSelection(i))
                {
                    var bounds = _layoutEngine.GetCharacterBounds(_model, i);
                    if (!bounds.IsEmpty)
                    {
                        // Bounds sind im Content-Space, wir müssen Padding hinzufügen für Screen-Koordinaten
                        var screenBounds = new SKRect(
                            bounds.Left + _scaledPadding,
                            bounds.Top + _scaledPadding,
                            bounds.Right + _scaledPadding,
                            bounds.Bottom + _scaledPadding
                        );

                        using var selectionPaint = new SKPaint
                        {
                            Color = new SKColor(100, 150, 255, 180),
                            Style = SKPaintStyle.Fill
                        };
                        canvas.DrawRect(screenBounds, selectionPaint);
                    }
                }

                // Charakter rendern
                canvas.DrawText(ch.Value.ToString(), lineX, lineY, textPaint);

                // Advance für nächstes Zeichen
                float charWidth = textPaint.MeasureText(ch.Value.ToString()) + ch.Format.Tracking;
                lineX += charWidth;
            }
        }

        // ─── Cursor ─────────────────────────────────────────────────
        if (_hasFocus && _cursorVisible && _layoutEngine.Lines.Count > 0)
            DrawCursor(canvas);
    }

    /// <summary>
    /// Zeichnet einen Rahmen um das Eingabefeld herum (äußere Kante mit DPI-Skalierung)
    /// </summary>
    private void DrawFieldBorder(SKCanvas canvas, float width, float height, float dpiScale = 1.0f)
    {
        float borderWidth = 1.0f;

        using var borderPaint = new SKPaint
        {
            Color = new SKColor(50, 100, 200, 255),  // Blauer Rahmen
            Style = SKPaintStyle.Stroke,
            StrokeWidth = borderWidth,
            IsAntialias = true
        };

        // Rechteck zeichnen: äußere Kante (0, 0) bis (width*dpiScale, height*dpiScale)
        // Multipliziere mit dpiScale, um die unterschiedliche Auflösung zu berücksichtigen
        // (physische Pixel vs. logische WPF-Pixel)
        var borderRect = new SKRect(0, 0, width * dpiScale, height * dpiScale);
        canvas.DrawRect(borderRect, borderPaint);
    }

    /// <summary>
    /// Zeichne blinkenden Cursor
    /// </summary>
    private void DrawCursor(SKCanvas canvas)
    {
        var (lineIdx, colInLine) = _layoutEngine.GetCursorLineColumn(_cursorPos);
        if (lineIdx >= _layoutEngine.Lines.Count)
            return;

        var line = _layoutEngine.Lines[lineIdx];

        // Character-Position in der Zeile berechnen (mit korrekter Messung)
        float cursorX = line.LineX;
        for (int i = line.StartCharIdx; i < line.StartCharIdx + colInLine; i++)
        {
            if (i < _model.CharacterCount)
            {
                var ch = _model.Characters[i];
                using var paint = new SKPaint
                {
                    Typeface = SkiaTextModel.GetTypeface(ch.Format.FontFamily, ch.Format.Bold, ch.Format.Italic),
                    TextSize = ch.Format.FontSizePt
                };
                cursorX += paint.MeasureText(ch.Value.ToString());
            }
        }

        // Screen-Koordinaten (mit Padding)
        float screenCursorX = _scaledPadding + cursorX;
        float screenCursorY = _scaledPadding + line.LineY;
        float screenCursorBottom = _scaledPadding + line.LineY + line.Ascent + line.Descent;

        using var cursorPaint = new SKPaint { Color = SKColors.White, StrokeWidth = 2f };
        canvas.DrawLine(screenCursorX, screenCursorY, screenCursorX, screenCursorBottom, cursorPaint);
    }

    private bool IsCharInSelection(int charIndex)
    {
        if (_selectionStart < 0 || _selectionEnd < 0)
            return false;

        int start = Math.Min(_selectionStart, _selectionEnd);
        int end = Math.Max(_selectionStart, _selectionEnd);
        return charIndex >= start && charIndex < end;
    }

    // ─── Maus & Keyboard Input ──────────────────────────────────────

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        Focus();

        // Berechne DPI-Skalierung (Logical zu Physical Pixels)
        var src = PresentationSource.FromVisual(this);
        double dpiScale = src?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;

        // Berechne Layout mit physischen Pixel-Abmessungen
        float layoutWidth = (float)(Width * dpiScale) - _scaledPadding * 2;
        float layoutHeight = (float)(Height * dpiScale) - _scaledPadding * 2;
        _layoutEngine.Layout(
            _model,
            Math.Max(layoutWidth, 1),
            Math.Max(layoutHeight, 1),
            HorizontalAlign,
            VerticalAlign
        );

        // Konvertiere Mouse-Koordinaten von WPF Logical zu Physical Pixels
        var pos = e.GetPosition(this);
        float physicalX = (float)(pos.X * dpiScale);
        float physicalY = (float)(pos.Y * dpiScale);
        var contentPos = TransformScreenToContent(physicalX, physicalY);
        _cursorPos = _layoutEngine.HitTestCursorPosition(_model, contentPos.X, contentPos.Y);

        // Initialisiere Selection für Drag: Start und End auf aktuelle Position
        // Während OnMouseMove wird _selectionEnd aktualisiert
        _selectionStart = _cursorPos;
        _selectionEnd = _cursorPos;
        InvalidateVisual();

        // Event als behandelt markieren, damit OnCanvasMouseDown nicht aufgerufen wird
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        // Berechne DPI-Skalierung (Logical zu Physical Pixels)
        var src = PresentationSource.FromVisual(this);
        double dpiScale = src?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;

        // Berechne Layout mit physischen Pixel-Abmessungen
        float layoutWidth = (float)(Width * dpiScale) - _scaledPadding * 2;
        float layoutHeight = (float)(Height * dpiScale) - _scaledPadding * 2;
        _layoutEngine.Layout(
            _model,
            Math.Max(layoutWidth, 1),
            Math.Max(layoutHeight, 1),
            HorizontalAlign,
            VerticalAlign
        );

        // Konvertiere Mouse-Koordinaten von WPF Logical zu Physical Pixels
        var pos = e.GetPosition(this);
        float physicalX = (float)(pos.X * dpiScale);
        float physicalY = (float)(pos.Y * dpiScale);
        var contentPos = TransformScreenToContent(physicalX, physicalY);
        _selectionEnd = _layoutEngine.HitTestCursorPosition(_model, contentPos.X, contentPos.Y);
        InvalidateVisual();

        // Event als behandelt markieren
        e.Handled = true;
    }

    /// <summary>
    /// Transformiere Screen-Koordinaten (WPF) in Content-Koordinaten (Layout-Engine)
    /// Screen-Koordinaten sind relativ zum SKElement.
    /// Content-Koordinaten sind relativ zur Padding-Box (0,0 = top-left of content area).
    /// </summary>
    private (float X, float Y) TransformScreenToContent(float screenX, float screenY)
    {
        // Einfach: Padding abziehen
        float contentX = screenX - _scaledPadding;
        float contentY = screenY - _scaledPadding;
        return (contentX, contentY);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        // Event als behandelt markieren
        e.Handled = true;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (!_hasFocus)
            return;

        bool isShiftPressed = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        if (e.Key == Key.Left)
        {
            if (isShiftPressed)
            {
                // Shift+Left: Selection erweitern/verkleinern
                if (_selectionStart < 0) _selectionStart = _cursorPos;
                _cursorPos = Math.Max(0, _cursorPos - 1);
                _selectionEnd = _cursorPos;
                // Wenn Selection leer ist, deselektieren
                if (_selectionStart == _selectionEnd) _selectionStart = _selectionEnd = -1;
            }
            else
            {
                // Normal Left: Cursor bewegen, Selection löschen
                _cursorPos = Math.Max(0, _cursorPos - 1);
                _selectionStart = _selectionEnd = -1;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Right)
        {
            if (isShiftPressed)
            {
                // Shift+Right: Selection erweitern/verkleinern
                if (_selectionStart < 0) _selectionStart = _cursorPos;
                _cursorPos = Math.Min(_model.CharacterCount, _cursorPos + 1);
                _selectionEnd = _cursorPos;
                // Wenn Selection leer ist, deselektieren
                if (_selectionStart == _selectionEnd) _selectionStart = _selectionEnd = -1;
            }
            else
            {
                // Normal Right: Cursor bewegen, Selection löschen
                _cursorPos = Math.Min(_model.CharacterCount, _cursorPos + 1);
                _selectionStart = _selectionEnd = -1;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            MoveCursorUp();
            if (!isShiftPressed) _selectionStart = _selectionEnd = -1;
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            MoveCursorDown();
            if (!isShiftPressed) _selectionStart = _selectionEnd = -1;
            e.Handled = true;
        }
        else if (e.Key == Key.Home)
        {
            if (isShiftPressed)
            {
                // Shift+Home: Selection bis Anfang der Zeile
                if (_selectionStart < 0) _selectionStart = _cursorPos;
                _cursorPos = 0;
                _selectionEnd = _cursorPos;
                if (_selectionStart == _selectionEnd) _selectionStart = _selectionEnd = -1;
            }
            else
            {
                _cursorPos = 0;
                _selectionStart = _selectionEnd = -1;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.End)
        {
            if (isShiftPressed)
            {
                // Shift+End: Selection bis Textende
                if (_selectionStart < 0) _selectionStart = _cursorPos;
                _cursorPos = _model.CharacterCount;
                _selectionEnd = _cursorPos;
                if (_selectionStart == _selectionEnd) _selectionStart = _selectionEnd = -1;
            }
            else
            {
                _cursorPos = _model.CharacterCount;
                _selectionStart = _selectionEnd = -1;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            DeleteSelection();
            e.Handled = true;
        }
        else if (e.Key == Key.Back)
        {
            if (_cursorPos > 0)
            {
                _cursorPos--;
                _model.DeleteCharAt(_cursorPos);
                TextChanged?.Invoke(this, new ImprovedSkiaTextEditorTextChangedEventArgs());
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Return)
        {
            // Enter als Zeilenumbruch
            DeleteSelection();
            _model.InsertChar(_cursorPos, '\n', _defaultFormat.Clone());
            _cursorPos++;
            TextChanged?.Invoke(this, new ImprovedSkiaTextEditorTextChangedEventArgs());
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.A)
        {
            _selectionStart = 0;
            _selectionEnd = _model.CharacterCount;
            e.Handled = true;
        }

        if (!e.Handled && e.Key != Key.System)
            return;

        InvalidateVisual();
    }

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        base.OnTextInput(e);

        if (!_hasFocus || string.IsNullOrEmpty(e.Text))
            return;

        DeleteSelection();

        foreach (char c in e.Text)
        {
            // Verwende _defaultFormat für neue Zeichen, damit sie die richtige Schriftgröße haben
            _model.InsertChar(_cursorPos, c, _defaultFormat.Clone());
            _cursorPos++;
        }

        TextChanged?.Invoke(this, new ImprovedSkiaTextEditorTextChangedEventArgs());
        InvalidateVisual();
        e.Handled = true;
    }

    private void DeleteSelection()
    {
        if (_selectionStart < 0 || _selectionEnd < 0)
            return;

        int start = Math.Min(_selectionStart, _selectionEnd);
        int end = Math.Max(_selectionStart, _selectionEnd);

        for (int i = end - 1; i >= start; i--)
            _model.DeleteCharAt(i);

        _cursorPos = start;
        _selectionStart = _selectionEnd = -1;
        TextChanged?.Invoke(this, new ImprovedSkiaTextEditorTextChangedEventArgs());
    }

    /// <summary>
    /// Cursor eine Zeile nach oben bewegen (oder am Anfang bleiben)
    /// </summary>
    private void MoveCursorUp()
    {
        var (lineIdx, colInLine) = _layoutEngine.GetCursorLineColumn(_cursorPos);

        if (lineIdx <= 0)
        {
            // Bereits in der ersten Zeile → zum Anfang
            _cursorPos = 0;
            return;
        }

        // Zur vorherigen Zeile wechseln
        var prevLine = _layoutEngine.Lines[lineIdx - 1];

        // Versuche, die gleiche Spalte in der vorherigen Zeile zu erreichen
        int targetCol = Math.Min(colInLine, prevLine.EndCharIdx - prevLine.StartCharIdx);
        _cursorPos = prevLine.StartCharIdx + targetCol;
    }

    /// <summary>
    /// Cursor eine Zeile nach unten bewegen (oder am Ende bleiben)
    /// </summary>
    private void MoveCursorDown()
    {
        var (lineIdx, colInLine) = _layoutEngine.GetCursorLineColumn(_cursorPos);

        if (lineIdx >= _layoutEngine.Lines.Count - 1)
        {
            // Bereits in der letzten Zeile → zum Ende
            _cursorPos = _model.CharacterCount;
            return;
        }

        // Zur nächsten Zeile wechseln
        var nextLine = _layoutEngine.Lines[lineIdx + 1];

        // Versuche, die gleiche Spalte in der nächsten Zeile zu erreichen
        int targetCol = Math.Min(colInLine, nextLine.EndCharIdx - nextLine.StartCharIdx);
        _cursorPos = nextLine.StartCharIdx + targetCol;
    }

    private void StartCursorBlink()
    {
        if (_cursorBlinkTimer != null)
            return;

        _cursorBlinkTimer = new System.Windows.Threading.DispatcherTimer();
        _cursorBlinkTimer.Interval = TimeSpan.FromMilliseconds(500);
        _cursorBlinkTimer.Tick += (s, e) =>
        {
            _cursorVisible = !_cursorVisible;
            InvalidateVisual();
        };
        _cursorBlinkTimer.Start();
    }

    private void StopCursorBlink()
    {
        if (_cursorBlinkTimer == null)
            return;

        _cursorBlinkTimer.Stop();
        _cursorBlinkTimer = null;
        _cursorVisible = false;
    }

    // ─── Public API (kompatibel mit altem Editor) ───────────────────

    public string GetText() => _model.GetText();

    public void SetText(
        string text,
        string fontFamily = "Segoe UI",
        float fontSize = 12f,
        double zoom = 1.0)
    {
        _zoom = zoom;
        _scaledPadding = (float)(Padding * _zoom);

        var format = new TextCharacterFormat
        {
            FontFamily = fontFamily,
            FontSizePt = fontSize,
            Color = SKColors.White
        };

        // Speichere das Format als Standard für neue Zeichen
        _defaultFormat = format.Clone();

        _model.SetText(text, format);
        _cursorPos = 0;
        _selectionStart = _selectionEnd = -1;
        InvalidateVisual();
    }

    /// <summary>
    /// Formatierung für Selected Text ändern
    /// </summary>
    public void SetSelectedFormat(TextCharacterFormat format)
    {
        if (_selectionStart < 0 || _selectionEnd < 0)
            return;

        int start = Math.Min(_selectionStart, _selectionEnd);
        int end = Math.Max(_selectionStart, _selectionEnd);

        _model.SetFormat(start, end - start, format);
        TextChanged?.Invoke(this, new ImprovedSkiaTextEditorTextChangedEventArgs());
        InvalidateVisual();
    }

    /// <summary>
    /// Liefert das komplette Datenmodell (für Export/Speichern)
    /// </summary>
    public SkiaTextModel GetModel() => _model.Clone();

    /// <summary>
    /// Datenmodell setzen (z.B. beim Laden)
    /// </summary>
    public void SetModel(SkiaTextModel model, double zoom = 1.0)
    {
        _model = model.Clone();
        _zoom = zoom;
        _scaledPadding = (float)(Padding * _zoom);
        _cursorPos = 0;
        _selectionStart = _selectionEnd = -1;
        InvalidateVisual();
    }

    /// <summary>
    /// Aktualisiert die Schriftgröße für alle Zeichen, OHNE den Cursor zu resetzen.
    /// Dies wird verwendet, wenn _dpiScale berechnet wurde und wir die Schriftgröße korrigieren müssen.
    /// </summary>
    public void UpdateFontSize(float newFontSize)
    {
        _model.UpdateFontSizeAll(newFontSize);

        // Aktualisiere auch _defaultFormat, damit neue Zeichen die neue Größe bekommen
        _defaultFormat.FontSizePt = newFontSize;

        InvalidateVisual();
    }

    /// <summary>
    /// Setzt die Cursor-Position basierend auf Screen-Koordinaten (WPF-Pixel im Element)
    /// </summary>
    public void SetCursorAtPosition(double screenX, double screenY)
    {
        // screenX/Y sind in WPF Logical Pixels von GetPosition()
        // Konvertiere zu Physical Pixels für das Layout
        var src = PresentationSource.FromVisual(this);
        double dpiScale = src?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;

        float physicalX = (float)(screenX * dpiScale);
        float physicalY = (float)(screenY * dpiScale);
        var contentPos = TransformScreenToContent(physicalX, physicalY);
        _cursorPos = _layoutEngine.HitTestCursorPosition(_model, contentPos.X, contentPos.Y);
        _selectionStart = _selectionEnd = -1;
        InvalidateVisual();
    }

    /// <summary>
    /// Aktualisiert die Formatierung aller Zeichen, OHNE den Cursor-Zustand zu verändern
    /// Dies ist für die Live-Aktualisierung von Schriftart/Größe gedacht
    /// </summary>
    public void UpdateCharacterFormat(TextCharacterFormat format)
    {
        if (_model == null || _model.CharacterCount == 0) return;

        for (int i = 0; i < _model.CharacterCount; i++)
        {
            var ch = _model.Characters[i];
            ch.Format.FontFamily = format.FontFamily;
            ch.Format.FontSizePt = format.FontSizePt;
            ch.Format.Tracking = format.Tracking;
            ch.Format.LineHeight = format.LineHeight;
            // Weitere Eigenschaften können hier auch aktualisiert werden, falls nötig
        }

        // Aktualisiere auch das Standard-Format für neue Zeichen
        _defaultFormat = format.Clone();

        InvalidateVisual();
    }
}
