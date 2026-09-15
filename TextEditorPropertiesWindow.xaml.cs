using System.Windows;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace NCHops;

public partial class TextEditorPropertiesWindow : Window
{
    private SkiaTextEditor? _textEditor;

    public TextEditorPropertiesWindow()
    {
        InitializeComponent();

        // Erstelle und konfiguriere Text Editor
        _textEditor = new SkiaTextEditor();
        _textEditor.SetText("Hallo Welt!\nDie Schrift kann gefüllt\noder als Kontur gerendert werden.", "Segoe UI", 14f);

        // Platziere Editor im ContentControl-Container
        TextEditorControl.Content = _textEditor;

        // Default: Fill-Modus
        FillRadio.IsChecked = true;

        UpdateDebugInfo();
    }

    private void OnStyleChanged(object sender, RoutedEventArgs e)
    {
        if (_textEditor == null) return;

        if (FillRadio.IsChecked == true)
            _textEditor.TextStyle = SKPaintStyle.Fill;
        else if (StrokeRadio.IsChecked == true)
            _textEditor.TextStyle = SKPaintStyle.Stroke;

        UpdateDebugInfo();
    }

    private void OnColorWhiteClick(object sender, RoutedEventArgs e)
    {
        if (_textEditor == null) return;
        _textEditor.TextColor = SKColors.White;
        UpdateDebugInfo();
    }

    private void OnColorBlackClick(object sender, RoutedEventArgs e)
    {
        if (_textEditor == null) return;
        _textEditor.TextColor = SKColors.Black;
        UpdateDebugInfo();
    }

    private void OnColorRedClick(object sender, RoutedEventArgs e)
    {
        if (_textEditor == null) return;
        _textEditor.TextColor = new SKColor(255, 100, 100);
        UpdateDebugInfo();
    }

    private void OnStrokeWidthChanged(object sender, RoutedEventArgs e)
    {
        if (_textEditor == null) return;

        float width = (float)StrokeWidthSlider.Value;
        _textEditor.StrokeWidth = width;
        StrokeWidthLabel.Text = width.ToString("F2");

        UpdateDebugInfo();
    }

    private void UpdateDebugInfo()
    {
        if (_textEditor == null) return;

        string style = _textEditor.TextStyle == SKPaintStyle.Fill ? "Fill" : "Stroke";
        string color = _textEditor.TextColor.ToString();
        float width = _textEditor.StrokeWidth;

        DebugText.Text = $"Style: {style}\nColor: {color}\nStroke Width: {width:F2}";
    }

    private void OnConvertToLineSegments(object sender, RoutedEventArgs e)
    {
        if (_textEditor == null) return;

        string text = _textEditor.GetText();
        if (string.IsNullOrWhiteSpace(text))
        {
            ExportInfoText.Text = "Textfeld ist leer!";
            return;
        }

        try
        {
            // Konvertiere Text zu Liniensegmenten
            var lineSegments = TextToLineSegments.ConvertTextToLineSegments(
                text: text,
                fontFamily: "Segoe UI",
                fontSize: 14f,
                startX: 0,
                startY: 20,
                tolerance: 0.3f
            );

            int totalSegments = TextToLineSegments.ExtractAllLineSegments(lineSegments).Count;

            ExportInfoText.Text = $"✓ Konvertiert!\n{lineSegments.Count} Zeichen\n{totalSegments} Liniensegmente";

            // Speichere die Liniensegmente zwischenzeitlich
            System.Diagnostics.Debug.WriteLine($"Converted {lineSegments.Count} characters to {totalSegments} line segments");

            // Optional: Zeige die Segmente in der Debug-Konsole
            foreach (var charGeo in lineSegments.Take(3))
            {
                System.Diagnostics.Debug.WriteLine($"  '{charGeo.Character}': {charGeo.LineSegments.Count} segments at ({charGeo.X:F1},{charGeo.Y:F1})");
            }
        }
        catch (Exception ex)
        {
            ExportInfoText.Text = $"Fehler: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"ERROR in OnConvertToLineSegments: {ex}");
        }
    }
}
