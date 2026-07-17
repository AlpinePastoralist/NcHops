using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace NCHops;

public class TextRun
{
    public string Text = "";
    public string FontFamily = "Segoe UI";
    public float FontSize = 12f;
    public SKColor Color = SKColors.Black;
}

public class SkiaTextEditor : SKElement
{
    private List<TextRun> _content = [new TextRun { Text = "", FontFamily = "Segoe UI", FontSize = 12f, Color = SKColors.White }];
    private int _cursorPos = 0;
    private int _selectionStart = -1;
    private int _selectionEnd = -1;
    private bool _hasFocus = false;
    private System.Windows.Threading.DispatcherTimer? _cursorBlinkTimer;
    private bool _cursorVisible = true;

    private const float Padding = 4f;
    private const float LineHeight = 20f;

    public event EventHandler<SkiaTextEditorTextChangedEventArgs>? TextChanged;

    public SkiaTextEditor()
    {
        Focusable = true;

        PreviewMouseLeftButtonDown += OnMouseDown;
        PreviewMouseMove += OnMouseMove;
        PreviewMouseLeftButtonUp += OnMouseUp;
        PreviewKeyDown += OnKeyDown;
        GotFocus += (s, e) => { _hasFocus = true; StartCursorBlink(); InvalidateVisual(); };
        LostFocus += (s, e) => { _hasFocus = false; StopCursorBlink(); InvalidateVisual(); };

        PaintSurface += OnPaintSurface;
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        if (e.Info.Width == 0 || e.Info.Height == 0) return;

        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        float x = Padding;
        float y = Padding + Padding; // Oben-links, mit Padding

        var textPaint = new SKPaint
        {
            TextSize = 12f,
            IsAntialias = true,
            Color = SKColors.White,
            Typeface = SKTypeface.FromFamilyName("Segoe UI")
        };

        int charIndex = 0;
        float cursorX = x;
        float cursorY = y;

        // Zeichne Text mit Selection-Highlight
        foreach (var run in _content)
        {
            if (string.IsNullOrEmpty(run.Text)) continue;

            textPaint.TextSize = run.FontSize;
            textPaint.Typeface = SKTypeface.FromFamilyName(run.FontFamily);
            textPaint.Color = run.Color;

            for (int i = 0; i < run.Text.Length; i++)
            {
                char c = run.Text[i];
                string charStr = c.ToString();

                if (charIndex == _cursorPos)
                {
                    cursorX = x;
                    cursorY = y - textPaint.TextSize;
                }

                // Selection-Highlight
                if (IsCharInSelection(charIndex))
                {
                    var charWidth = textPaint.MeasureText(charStr);
                    var selectionPaint = new SKPaint { Color = new SKColor(200, 220, 255, 200) };
                    canvas.DrawRect(new SKRect(x, y - textPaint.TextSize + 2, x + charWidth, y + 2), selectionPaint);
                    selectionPaint.Dispose();
                }

                canvas.DrawText(charStr, x, y, textPaint);
                x += textPaint.MeasureText(charStr);

                charIndex++;
            }
        }

        if (charIndex == _cursorPos)
        {
            cursorX = x;
            cursorY = y - textPaint.TextSize;
        }

        // Blinkender Cursor
        if (_hasFocus && _cursorVisible)
        {
            var cursorPaint = new SKPaint { Color = SKColors.White, StrokeWidth = 1f };
            canvas.DrawLine(cursorX, cursorY, cursorX, y + 2, cursorPaint);
            cursorPaint.Dispose();
        }

        textPaint.Dispose();
    }

    private bool IsCharInSelection(int charIndex)
    {
        if (_selectionStart < 0 || _selectionEnd < 0) return false;
        int start = Math.Min(_selectionStart, _selectionEnd);
        int end = Math.Max(_selectionStart, _selectionEnd);
        return charIndex >= start && charIndex < end;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        var pos = e.GetPosition(this);
        _cursorPos = HitTestCursorPosition((float)pos.X, (float)pos.Y);
        _selectionStart = _cursorPos;
        InvalidateVisual();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        var pos = e.GetPosition(this);
        _selectionEnd = HitTestCursorPosition((float)pos.X, (float)pos.Y);
        InvalidateVisual();
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e) { }

    private int HitTestCursorPosition(float clickX, float clickY)
    {
        float x = Padding;
        var textPaint = new SKPaint { TextSize = 12f, IsAntialias = true };

        int charIndex = 0;
        foreach (var run in _content)
        {
            if (string.IsNullOrEmpty(run.Text)) continue;

            textPaint.TextSize = run.FontSize;
            textPaint.Typeface = SKTypeface.FromFamilyName(run.FontFamily);

            for (int i = 0; i < run.Text.Length; i++)
            {
                float charWidth = textPaint.MeasureText(run.Text[i].ToString());
                if (clickX < x + charWidth / 2)
                    return charIndex;

                x += charWidth;
                charIndex++;
            }
        }

        textPaint.Dispose();
        return charIndex;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (!_hasFocus) return;

        if (e.Key == Key.Left)
        {
            _cursorPos = Math.Max(0, _cursorPos - 1);
            _selectionStart = _selectionEnd = -1;
            e.Handled = true;
        }
        else if (e.Key == Key.Right)
        {
            int maxPos = _content.Sum(r => r.Text.Length);
            _cursorPos = Math.Min(maxPos, _cursorPos + 1);
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
            _cursorPos = _content.Sum(r => r.Text.Length);
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
                DeleteAt(_cursorPos);
            }
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.A)
        {
            _selectionStart = 0;
            _selectionEnd = _content.Sum(r => r.Text.Length);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C)
        {
            CopySelection();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.V)
        {
            PasteFromClipboard();
            e.Handled = true;
        }

        if (!e.Handled && e.Key != Key.System) return;

        InvalidateVisual();
    }

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        base.OnTextInput(e);

        if (!_hasFocus || string.IsNullOrEmpty(e.Text)) return;

        DeleteSelection();

        string text = e.Text;
        foreach (char c in text)
        {
            InsertCharAt(_cursorPos, c);
            _cursorPos++;
        }

        TextChanged?.Invoke(this, new SkiaTextEditorTextChangedEventArgs());
        InvalidateVisual();
        e.Handled = true;
    }

    private void InsertCharAt(int pos, char c)
    {
        int charCount = 0;
        for (int i = 0; i < _content.Count; i++)
        {
            var run = _content[i];
            if (charCount + run.Text.Length >= pos)
            {
                int offset = pos - charCount;
                run.Text = run.Text.Insert(offset, c.ToString());
                return;
            }
            charCount += run.Text.Length;
        }

        if (_content.Count > 0)
            _content[^1].Text += c;
    }

    private void DeleteAt(int pos)
    {
        int charCount = 0;
        for (int i = 0; i < _content.Count; i++)
        {
            var run = _content[i];
            if (charCount + run.Text.Length > pos)
            {
                int offset = pos - charCount;
                if (offset < run.Text.Length)
                {
                    run.Text = run.Text.Remove(offset, 1);
                }
                return;
            }
            charCount += run.Text.Length;
        }
    }

    private void DeleteSelection()
    {
        if (_selectionStart < 0 || _selectionEnd < 0) return;

        int start = Math.Min(_selectionStart, _selectionEnd);
        int end = Math.Max(_selectionStart, _selectionEnd);

        for (int i = end - 1; i >= start; i--)
            DeleteAt(i);

        _cursorPos = start;
        _selectionStart = _selectionEnd = -1;
        TextChanged?.Invoke(this, new SkiaTextEditorTextChangedEventArgs());
    }

    private void CopySelection()
    {
        if (_selectionStart < 0 || _selectionEnd < 0) return;

        int start = Math.Min(_selectionStart, _selectionEnd);
        int end = Math.Max(_selectionStart, _selectionEnd);

        string selectedText = GetTextRange(start, end);
        Clipboard.SetText(selectedText);
    }

    private void PasteFromClipboard()
    {
        if (!Clipboard.ContainsText()) return;

        string text = Clipboard.GetText();
        DeleteSelection();

        foreach (char c in text)
        {
            InsertCharAt(_cursorPos, c);
            _cursorPos++;
        }

        TextChanged?.Invoke(this, new SkiaTextEditorTextChangedEventArgs());
    }

    private string GetTextRange(int start, int end)
    {
        var result = new System.Text.StringBuilder();
        int charCount = 0;

        foreach (var run in _content)
        {
            for (int i = 0; i < run.Text.Length; i++)
            {
                if (charCount >= start && charCount < end)
                    result.Append(run.Text[i]);
                charCount++;
            }
        }

        return result.ToString();
    }

    private void StartCursorBlink()
    {
        if (_cursorBlinkTimer != null) return;

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
        if (_cursorBlinkTimer == null) return;
        _cursorBlinkTimer.Stop();
        _cursorBlinkTimer = null;
        _cursorVisible = false;
    }

    public string GetText() => string.Concat(_content.Select(r => r.Text));

    public void SetText(string text, string fontFamily = "Segoe UI", float fontSize = 12f)
    {
        _content.Clear();
        _content.Add(new TextRun
        {
            Text = text ?? "",
            FontFamily = fontFamily,
            FontSize = fontSize,
            Color = SKColors.White
        });
        _cursorPos = 0;
        _selectionStart = _selectionEnd = -1;
        InvalidateVisual();
    }
}

public class SkiaTextEditorTextChangedEventArgs : EventArgs { }
