using System;
using System.Windows;
using System.Windows.Controls;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace NCHops;

/// <summary>
/// Vereinfachte Demo des Textfeld-Prototypes
/// Zeigt: Character-Modell + LayoutEngine + Multi-Format
/// </summary>
public partial class TextEditorPrototypeWindow : Window
{
    private SkiaTextModel _currentModel;
    private TextHorizontalAlign _currentHAlign = TextHorizontalAlign.Left;
    private int _cursorPos = 0;
    private int _selectionStart = -1;
    private int _selectionEnd = -1;
    private SKElement _editorCanvas;

    public TextEditorPrototypeWindow()
    {
        InitializeComponent();
        InitializeEditor();
    }

    private void InitializeEditor()
    {
        try
        {
            // Modell initialisieren
            _currentModel = new SkiaTextModel();
            _currentModel.SetText(
                "Willkommen zum Textfeld-Prototype!\nZeile 2 mit verschiedener Formatierung.",
                new TextCharacterFormat { FontFamily = "Segoe UI", FontSizePt = 14f, Color = SKColors.White }
            );

            // SKElement erstellen
            _editorCanvas = new SKElement();
            _editorCanvas.PaintSurface += (s, e) => RenderCanvas(e);
            _editorCanvas.PreviewMouseLeftButtonDown += OnCanvasMouseDown;
            _editorCanvas.PreviewMouseMove += OnCanvasMouseMove;
            _editorCanvas.PreviewKeyDown += OnCanvasKeyDown;
            _editorCanvas.Focusable = true;

            // In Border einfügen (FindName braucht noch nicht zu funktionieren)
            // Wir machen es direkt im Loaded-Event
            this.Loaded += (s, e) =>
            {
                var border = (Border)FindName("EditorCanvas");
                if (border != null)
                {
                    border.Child = _editorCanvas;
                    _editorCanvas.Focus();
                }
            };

            UpdateDebugInfo();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Fehler beim Initialisieren: {ex.Message}\n\n{ex.StackTrace}", "Fehler");
            Close();
        }
    }

    private void RenderCanvas(SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(new SKColor(37, 37, 38));

        if (_currentModel.CharacterCount == 0)
            return;

        float width = e.Surface.Canvas.LocalClipBounds.Width;
        float containerWidth = Math.Max(200, width - 20);

        // Layout berechnen
        var layoutEngine = new TextLayoutEngine();
        layoutEngine.Layout(_currentModel, containerWidth, 500, _currentHAlign, TextVerticalAlign.Top);

        float padding = 10f;

        // Zeilen rendern
        foreach (var line in layoutEngine.Lines)
        {
            float lineX = padding + line.LineX;
            float lineY = padding + line.LineY + line.Ascent;

            for (int i = line.StartCharIdx; i < line.EndCharIdx; i++)
            {
                var ch = _currentModel.Characters[i];

                using (var paint = new SKPaint
                {
                    Typeface = SkiaTextModel.GetTypeface(ch.Format.FontFamily, ch.Format.Bold, ch.Format.Italic),
                    TextSize = ch.Format.FontSizePt,
                    Color = ch.Format.Color,
                    IsAntialias = true
                })
                {
                    // Selection-Highlight
                    if (IsCharInSelection(i))
                    {
                        var bounds = layoutEngine.GetCharacterBounds(_currentModel, i);
                        if (!bounds.IsEmpty)
                        {
                            using (var selPaint = new SKPaint { Color = new SKColor(100, 150, 255, 180) })
                            {
                                canvas.DrawRect(bounds, selPaint);
                            }
                        }
                    }

                    canvas.DrawText(ch.Value.ToString(), lineX, lineY, paint);
                    lineX += paint.MeasureText(ch.Value.ToString());
                }
            }
        }

        // Cursor
        var cursorLine = layoutEngine.GetLineForCharacter(_cursorPos);
        if (cursorLine != null)
        {
            float cursorX = padding + cursorLine.LineX;
            for (int i = cursorLine.StartCharIdx; i < _cursorPos; i++)
            {
                var ch = _currentModel.Characters[i];
                using (var paint = new SKPaint { TextSize = ch.Format.FontSizePt })
                {
                    cursorX += paint.MeasureText(ch.Value.ToString());
                }
            }

            float cursorY = padding + cursorLine.LineY;
            float cursorBottom = cursorY + cursorLine.Ascent + cursorLine.Descent;

            using (var cursorPaint = new SKPaint { Color = SKColors.White, StrokeWidth = 2f, IsAntialias = true })
            {
                canvas.DrawLine(cursorX, cursorY, cursorX, cursorBottom, cursorPaint);
            }
        }
    }

    private bool IsCharInSelection(int idx)
    {
        if (_selectionStart < 0 || _selectionEnd < 0) return false;
        int start = Math.Min(_selectionStart, _selectionEnd);
        int end = Math.Max(_selectionStart, _selectionEnd);
        return idx >= start && idx < end;
    }

    private void OnCanvasMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(_editorCanvas);
        var layoutEngine = new TextLayoutEngine();
        layoutEngine.Layout(_currentModel, 300, 500, _currentHAlign, TextVerticalAlign.Top);

        _cursorPos = layoutEngine.HitTestCursorPosition(_currentModel, (float)pos.X - 10, (float)pos.Y - 10);
        _selectionStart = _cursorPos;
        _selectionEnd = -1;
        _editorCanvas?.InvalidateVisual();
        UpdateDebugInfo();
    }

    private void OnCanvasMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;

        var pos = e.GetPosition(_editorCanvas);
        var layoutEngine = new TextLayoutEngine();
        layoutEngine.Layout(_currentModel, 300, 500, _currentHAlign, TextVerticalAlign.Top);

        _selectionEnd = layoutEngine.HitTestCursorPosition(_currentModel, (float)pos.X - 10, (float)pos.Y - 10);
        _editorCanvas?.InvalidateVisual();
    }

    private void OnCanvasKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Back && _cursorPos > 0)
        {
            _cursorPos--;
            _currentModel.DeleteCharAt(_cursorPos);
            _selectionStart = _selectionEnd = -1;
        }
        else if (e.Key == System.Windows.Input.Key.Delete && _selectionStart >= 0 && _selectionEnd >= 0)
        {
            int start = Math.Min(_selectionStart, _selectionEnd);
            int end = Math.Max(_selectionStart, _selectionEnd);
            for (int i = end - 1; i >= start; i--) _currentModel.DeleteCharAt(i);
            _cursorPos = start;
            _selectionStart = _selectionEnd = -1;
        }
        else if (e.Key == System.Windows.Input.Key.Left)
        {
            _cursorPos = Math.Max(0, _cursorPos - 1);
            _selectionStart = _selectionEnd = -1;
        }
        else if (e.Key == System.Windows.Input.Key.Right)
        {
            _cursorPos = Math.Min(_currentModel.CharacterCount, _cursorPos + 1);
            _selectionStart = _selectionEnd = -1;
        }
        else return;

        _editorCanvas?.InvalidateVisual();
        UpdateDebugInfo();
        e.Handled = true;
    }

    protected override void OnTextInput(System.Windows.Input.TextCompositionEventArgs e)
    {
        base.OnTextInput(e);
        if (string.IsNullOrEmpty(e.Text)) return;

        if (_selectionStart >= 0 && _selectionEnd >= 0)
        {
            int start = Math.Min(_selectionStart, _selectionEnd);
            int end = Math.Max(_selectionStart, _selectionEnd);
            for (int i = end - 1; i >= start; i--) _currentModel.DeleteCharAt(i);
            _cursorPos = start;
            _selectionStart = _selectionEnd = -1;
        }

        foreach (char c in e.Text)
        {
            _currentModel.InsertChar(_cursorPos, c);
            _cursorPos++;
        }

        _editorCanvas?.InvalidateVisual();
        UpdateDebugInfo();
        e.Handled = true;
    }

    private void OnFormatChanged(object sender, RoutedEventArgs e)
    {
        if (_selectionStart < 0 || _selectionEnd < 0) return;
        if (_currentModel == null || _editorCanvas == null) return;

        try
        {
            int start = Math.Min(_selectionStart, _selectionEnd);
            int end = Math.Max(_selectionStart, _selectionEnd);

            var cmbFont = (ComboBox)FindName("CmbFontFamily");
            var sldSize = (Slider)FindName("SldFontSize");
            var chkBold = (CheckBox)FindName("ChkBold");
            var chkItalic = (CheckBox)FindName("ChkItalic");

            string font = (cmbFont?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Segoe UI";
            float size = (float)(sldSize?.Value ?? 12f);
            bool bold = chkBold?.IsChecked == true;
            bool italic = chkItalic?.IsChecked == true;

            var format = new TextCharacterFormat { FontFamily = font, FontSizePt = size, Bold = bold, Italic = italic, Color = SKColors.White };
            _currentModel.SetFormat(start, end - start, format);

            _editorCanvas.InvalidateVisual();
        }
        catch { /* Fehler bei Format-Änderung */ }
    }

    private void OnColorClick(object sender, RoutedEventArgs e)
    {
        if (_selectionStart < 0 || _selectionEnd < 0) return;

        int start = Math.Min(_selectionStart, _selectionEnd);
        int end = Math.Max(_selectionStart, _selectionEnd);

        SKColor color = (sender as Button)?.Content?.ToString() switch
        {
            "Weiß" => SKColors.White,
            "Schwarz" => SKColors.Black,
            _ => SKColors.White
        };

        for (int i = start; i < end; i++)
            _currentModel.Characters[i].Format.Color = color;

        _editorCanvas?.InvalidateVisual();
    }

    private void OnAlignLeft(object sender, RoutedEventArgs e) { _currentHAlign = TextHorizontalAlign.Left; _editorCanvas?.InvalidateVisual(); }
    private void OnAlignCenter(object sender, RoutedEventArgs e) { _currentHAlign = TextHorizontalAlign.Center; _editorCanvas?.InvalidateVisual(); }
    private void OnAlignRight(object sender, RoutedEventArgs e) { _currentHAlign = TextHorizontalAlign.Right; _editorCanvas?.InvalidateVisual(); }

    private void OnLayoutChanged(object sender, RoutedEventArgs e) => _editorCanvas?.InvalidateVisual();

    private void OnExport(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Exportierter Text:\n\n" + _currentModel.GetText() + "\n\nZeichen: " + _currentModel.CharacterCount, "Export");
    }

    private void UpdateDebugInfo()
    {
        try
        {
            var txtInfo = (TextBox)FindName("TxtInfo");
            if (txtInfo != null)
                txtInfo.Text = $"Zeichen: {_currentModel.CharacterCount}\nCursor: {_cursorPos}\nSelection: {_selectionStart}-{_selectionEnd}\nAlign: {_currentHAlign}";
        }
        catch { /* Ignorieren wenn nicht gefunden */ }
    }
}
