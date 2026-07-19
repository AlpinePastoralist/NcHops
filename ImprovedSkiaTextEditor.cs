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

    // ─── Layout Settings ────────────────────────────────────────────
    public TextHorizontalAlign HorizontalAlign { get; set; } = TextHorizontalAlign.Left;
    public TextVerticalAlign VerticalAlign { get; set; } = TextVerticalAlign.Top;

    // ─── Events & Properties ────────────────────────────────────────
    public event EventHandler<ImprovedSkiaTextEditorTextChangedEventArgs>? TextChanged;

    public ImprovedSkiaTextEditor()
    {
        Focusable = true;

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
        float availableWidth = (float)(Width > 0 ? Width : 400);
        float availableHeight = (float)(Height > 0 ? Height : 100);
        DrawFieldBorder(canvas, availableWidth, availableHeight);

        // ─── Früh rückgängig wenn kein Text vorhanden ──────────────────
        if (_model.CharacterCount == 0)
            return;

        // ─── Layout berechnen ───────────────────────────────────────
        _layoutEngine.Layout(
            _model,
            availableWidth - _scaledPadding * 2,
            availableHeight - _scaledPadding * 2,
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
    /// Zeichnet einen Rahmen um das Eingabefeld herum (innen, damit es nicht überlädt)
    /// </summary>
    private void DrawFieldBorder(SKCanvas canvas, float width, float height)
    {
        float borderWidth = 2.0f;
        float halfStroke = borderWidth / 2.0f;

        using var borderPaint = new SKPaint
        {
            Color = new SKColor(50, 100, 200, 255),  // Blauer Rahmen
            Style = SKPaintStyle.Stroke,
            StrokeWidth = borderWidth,
            IsAntialias = true
        };

        // Rechteck zeichnen: mit Offset nach innen, damit es nicht überlädt
        // StrokeWidth/2 nach innen verschieben
        var borderRect = new SKRect(halfStroke, halfStroke, width - halfStroke, height - halfStroke);
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
        var pos = e.GetPosition(this);
        var contentPos = TransformScreenToContent((float)pos.X, (float)pos.Y);
        _cursorPos = _layoutEngine.HitTestCursorPosition(_model, contentPos.X, contentPos.Y);
        _selectionStart = _cursorPos;
        InvalidateVisual();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        var pos = e.GetPosition(this);
        var contentPos = TransformScreenToContent((float)pos.X, (float)pos.Y);
        _selectionEnd = _layoutEngine.HitTestCursorPosition(_model, contentPos.X, contentPos.Y);
        InvalidateVisual();
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

    private void OnMouseUp(object sender, MouseButtonEventArgs e) { }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (!_hasFocus)
            return;

        if (e.Key == Key.Left)
        {
            _cursorPos = Math.Max(0, _cursorPos - 1);
            _selectionStart = _selectionEnd = -1;
            e.Handled = true;
        }
        else if (e.Key == Key.Right)
        {
            _cursorPos = Math.Min(_model.CharacterCount, _cursorPos + 1);
            _selectionStart = _selectionEnd = -1;
            e.Handled = true;
        }
        else if (e.Key == Key.Home)
        {
            _cursorPos = 0;
            _selectionStart = _selectionEnd = -1;
            e.Handled = true;
        }
        else if (e.Key == Key.End)
        {
            _cursorPos = _model.CharacterCount;
            _selectionStart = _selectionEnd = -1;
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
            _model.InsertChar(_cursorPos, c);
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
}
