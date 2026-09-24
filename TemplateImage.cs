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
    public bool FlipHorizontal { get; set; }
    public bool FlipVertical { get; set; }

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

            // Seitenverhältnis beibehalten: max 100 mm in einer Dimension
            if (Bitmap != null)
            {
                double aspectRatio = (double)Bitmap.Width / Bitmap.Height;
                const double maxSize = 100.0;  // max 100 mm

                if (aspectRatio >= 1.0)
                {
                    // Breiter als hoch
                    Width = maxSize;
                    Height = maxSize / aspectRatio;
                }
                else
                {
                    // Höher als breit
                    Width = maxSize * aspectRatio;
                    Height = maxSize;
                }

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

        // Rotation rückgängig machen für Größenberechnung
        double radians = -Rotation * Math.PI / 180.0;
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);

        // Neue Position in nicht-rotierte lokale Koordinaten transformieren (relativ zum Mittelpunkt)
        double localNewX = (newX - X) * cos - (newY - Y) * sin;
        double localNewY = (newX - X) * sin + (newY - Y) * cos;

        // Alte Ankerpunkt-Position auch transformieren
        var oldPt = AnchorPoints[anchorIdx];
        double localOldX = (oldPt.x - X) * cos - (oldPt.y - Y) * sin;
        double localOldY = (oldPt.x - X) * sin + (oldPt.y - Y) * cos;

        double deltaX = localNewX - localOldX;
        double deltaY = localNewY - localOldY;

        // Index in 3x3 Grid: 0=TL,1=TM,2=TR, 3=ML,4=MM,5=MR, 6=BL,7=BM,8=BR
        int row = anchorIdx / 3;  // 0,1,2
        int col = anchorIdx % 3;  // 0,1,2

        if (anchorIdx == 4)  // Mittelpunkt: einfach verschieben
        {
            // Bei Mittelpunkt: in Weltkoordinaten verschieben
            double worldDeltaX = deltaX * cos + deltaY * sin;
            double worldDeltaY = -deltaX * sin + deltaY * cos;
            X += worldDeltaX;
            Y += worldDeltaY;
        }
        else  // Ecke/Kante: ändern Größe und Position
        {
            double newLeft   = -Width / 2;
            double newRight  = Width / 2;
            double newTop    = Height / 2;
            double newBottom = -Height / 2;

            bool isCorner = (row == 0 || row == 2) && (col == 0 || col == 2);
            bool isHorizontalSide = (row == 1) && (col == 0 || col == 2);
            bool isVerticalSide = (row == 0 || row == 2) && (col == 1);

            double aspectRatio = Width / Height;

            // Update bounds basierend auf Änderung der lokalen Koordinaten
            if (col == 0)       newLeft   += deltaX;  // Linke Seite
            else if (col == 2)  newRight  += deltaX;  // Rechte Seite

            if (row == 0)       newTop    += deltaY;  // Obere Seite
            else if (row == 2)  newBottom += deltaY;  // Untere Seite

            double newWidth = newRight - newLeft;
            double newHeight = newTop - newBottom;

            // Bei Ecken: Seitenverhältnis beibehalten
            if (isCorner)
            {
                // Berechne Skalierungsfaktoren für beide Dimensionen
                double scaleX = newWidth / Width;
                double scaleY = newHeight / Height;

                // Verwende den Durchschnitt für stabile Skalierung (verhindert Flackern)
                double scale = (scaleX + scaleY) / 2;

                // Wende die Skalierung auf beide Dimensionen an (behält Seitenverhältnis)
                newWidth = Width * scale;
                newHeight = Height * scale;

                // Neuberechne bounds mit Ankerpunkt als Referenz
                if (col == 0)       newLeft = newRight - newWidth;
                else if (col == 2)  newRight = newLeft + newWidth;
                else                { newLeft = X - newWidth / 2; newRight = X + newWidth / 2; }

                if (row == 0)       newTop = newBottom + newHeight;
                else if (row == 2)  newBottom = newTop - newHeight;
                else                { newBottom = -newHeight / 2; newTop = newHeight / 2; }
            }

            // Minimum Größe erzwingen
            if (newWidth > 10 && newHeight > 10)
            {
                Width = newWidth;
                Height = newHeight;
                // Grenzen sind in lokalen Koordinaten, Mittelpunkt ist bei (0, 0)
                // Nur Weltkoordinaten speichern (X, Y bleiben unverändert in Bezug auf lokale Breite/Höhe)
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

    public void RotateCW()
    {
        Rotation = (Rotation + 90) % 360;
        UpdateAnchorPoints();
    }

    public void RotateCCW()
    {
        Rotation = (Rotation - 90 + 360) % 360;
        UpdateAnchorPoints();
    }

    public void FlipH()
    {
        FlipHorizontal = !FlipHorizontal;
        UpdateAnchorPoints();
    }

    public void FlipV()
    {
        FlipVertical = !FlipVertical;
        UpdateAnchorPoints();
    }
}
