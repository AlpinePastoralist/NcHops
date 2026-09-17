using System;
using System.IO;
using System.Windows.Media.Imaging;
using SkiaSharp;

namespace NCHops;

/// <summary>
/// Repräsentiert ein Vorlagenbild mit 9 Ankerpunkten für Transformation
/// </summary>
public class TemplateImage
{
    public string FilePath { get; set; }
    public SKBitmap? Bitmap { get; set; }

    // Position in mm (Werkstück-Koordinaten)
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double Rotation { get; set; }  // Rotation in Grad
    public double Opacity { get; set; } = 0.5;  // 0.0 bis 1.0

    // 9 Ankerpunkte (Ecken + Mittelpunkte):
    // 0=TL, 1=TM, 2=TR, 3=ML, 4=MM, 5=MR, 6=BL, 7=BM, 8=BR
    public (double x, double y)[] AnchorPoints { get; private set; } = new (double, double)[9];

    public TemplateImage(string filePath)
    {
        FilePath = filePath;
        Load();
    }

    private void Load()
    {
        if (File.Exists(FilePath))
        {
            var data = File.ReadAllBytes(FilePath);
            Bitmap = SKBitmap.Decode(data);

            // Standard-Größe: 100 x 100 mm
            if (Bitmap != null)
            {
                Width = 100;
                Height = 100;
                X = 50;
                Y = 50;
            }
        }
    }

    /// <summary>
    /// Aktualisiert die 9 Ankerpunkte basierend auf Größe, Position und Rotation
    /// </summary>
    public void UpdateAnchorPoints()
    {
        var points = new (double x, double y)[9];

        // Relative Positionen für die 9 Punkte (0..1)
        var relPosX = new[] { 0.0, 0.5, 1.0, 0.0, 0.5, 1.0, 0.0, 0.5, 1.0 };
        var relPosY = new[] { 1.0, 1.0, 1.0, 0.5, 0.5, 0.5, 0.0, 0.0, 0.0 };

        for (int i = 0; i < 9; i++)
        {
            var localX = -Width / 2 + relPosX[i] * Width;
            var localY = -Height / 2 + relPosY[i] * Height;

            // Rotation anwenden
            var radians = Rotation * Math.PI / 180.0;
            var cos = Math.Cos(radians);
            var sin = Math.Sin(radians);

            var rotX = localX * cos - localY * sin;
            var rotY = localX * sin + localY * cos;

            points[i] = (X + rotX, Y + rotY);
        }

        AnchorPoints = points;
    }

    /// <summary>
    /// Verschiebt das Bild basierend auf Ankerpunkt-Veränderung
    /// </summary>
    public void ResizeFromAnchor(int anchorIdx, double newX, double newY)
    {
        if (anchorIdx < 0 || anchorIdx > 8) return;

        var oldPt = AnchorPoints[anchorIdx];
        double deltaX = newX - oldPt.x;
        double deltaY = newY - oldPt.y;

        // Index in 3x3 Grid: 0=TL,1=TM,2=TR, 3=ML,4=MM,5=MR, 6=BL,7=BM,8=BR
        int row = anchorIdx / 3;  // 0,1,2
        int col = anchorIdx % 3;  // 0,1,2

        if (anchorIdx == 4)  // Mittelpunkt: einfach verschieben
        {
            X += deltaX;
            Y += deltaY;
        }
        else  // Ecke/Kante: ändern Größe
        {
            double newWidth = Width;
            double newHeight = Height;

            // Horizontale Größenänderung
            if (col == 0)       newWidth += deltaX;  // Links
            else if (col == 2)  newWidth -= deltaX;  // Rechts

            // Vertikale Größenänderung
            if (row == 0)       newHeight -= deltaY;  // Oben
            else if (row == 2)  newHeight += deltaY;  // Unten

            // Minimum Größe erzwingen
            if (newWidth > 10 && newHeight > 10)
            {
                Width = newWidth;
                Height = newHeight;
            }
        }

        UpdateAnchorPoints();
    }

    public void Move(double deltaX, double deltaY)
    {
        X += deltaX;
        Y += deltaY;
        UpdateAnchorPoints();
    }

    public int GetClosestAnchorPoint(double screenX, double screenY, double tolerance)
    {
        int closest = -1;
        double minDist = tolerance;

        for (int i = 0; i < 9; i++)
        {
            var dx = AnchorPoints[i].x - screenX;
            var dy = AnchorPoints[i].y - screenY;
            var dist = Math.Sqrt(dx * dx + dy * dy);

            if (dist < minDist)
            {
                minDist = dist;
                closest = i;
            }
        }

        return closest;
    }
}
