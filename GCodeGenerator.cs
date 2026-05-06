using System.Globalization;
using System.Text;

namespace NCHops;

public static class GCodeGenerator
{
    private static string F(double v) => v.ToString("F4", CultureInfo.InvariantCulture);

    public static string Planfräsen(PlanfräsenParams p)
    {
        double schritt = p.FraeserD * p.Faktor;
        var sb = new StringBuilder();
        sb.AppendLine("(Benötigte Werkzeuge:)");
        sb.AppendLine("(planfräser)");
        sb.AppendLine();
        sb.AppendLine($"M03 S{p.Drehzahl}");
        sb.AppendLine("G00 Z5.0000");
        sb.AppendLine();
        sb.AppendLine($"G00 X{F(p.X0)} Y{F(p.Y0)}");
        sb.AppendLine($"G01 Z{F(p.Z)} F{p.Vorschub} S{p.Drehzahl}");

        if (p.Horizontal)
        {
            double y = p.Y0;
            bool hin = true;
            while (y < p.Y1)
            {
                sb.AppendLine(hin ? $"G01 X{F(p.X1)}" : $"G01 X{F(p.X0)}");
                y += schritt;
                sb.AppendLine($"G01 Y{F(y)}");
                if (y >= p.Y1)
                {
                    hin = !hin;
                    sb.AppendLine(hin ? $"G01 X{F(p.X1)}" : $"G01 X{F(p.X0)}");
                    break;
                }
                hin = !hin;
            }
        }
        else
        {
            double x = p.X0;
            bool hin = true;
            while (x < p.X1)
            {
                sb.AppendLine(hin ? $"G01 Y{F(p.Y1)}" : $"G01 Y{F(p.Y0)}");
                x += schritt;
                sb.AppendLine($"G01 X{F(x)}");
                if (x >= p.X1)
                {
                    hin = !hin;
                    sb.AppendLine(hin ? $"G01 Y{F(p.Y1)}" : $"G01 Y{F(p.Y0)}");
                    break;
                }
                hin = !hin;
            }
        }

        sb.AppendLine();
        sb.AppendLine("G00 Z5.0000");
        sb.AppendLine("M05");
        return sb.ToString();
    }

    public static string Bohrung(BohrungParams p, double workW, double workH)
    {
        var sb = new StringBuilder();
        sb.AppendLine("(Mehrfach-Bohrungen)");
        sb.AppendLine($"(D={p.Durchmesser})");
        sb.AppendLine("G00 Z5.0000");

        var zDepth = -Math.Abs(p.Bohrtiefe);
        var zStep = Math.Abs(p.Zustellung);
        foreach (var ref_ in p.Bezugspunkte)
        {
            var (x, y) = ConvertBezugspunkt(ref_, p.XRel, p.YRel, workW, workH);
            sb.AppendLine($"(Bohrung Bezugspunkt: {ref_})");
            sb.AppendLine($"G00 X{F(x)} Y{F(y)}");

            double currentZ = 0;
            while (currentZ > zDepth)
            {
                currentZ = Math.Max(zDepth, currentZ - zStep);
                sb.AppendLine($"G01 Z{F(currentZ)} F300");
                sb.AppendLine("G00 Z5.0000");
            }
        }

        sb.AppendLine("M05");
        return sb.ToString();
    }

    public static string Reihenlochbohrung(ReihenlochbohrungParams p)
    {
        var sb = new StringBuilder();
        sb.AppendLine("(Reihenlochbohrung)");
        sb.AppendLine($"(Start: X={p.StartX} Y={p.StartY})");
        sb.AppendLine($"(Anzahl X={p.CountX} Y={p.CountY})");
        sb.AppendLine($"(Abstand X={p.SpacingX} Y={p.SpacingY})");
        sb.AppendLine($"(D={p.Diameter})");
        sb.AppendLine($"(Bohrtiefe={p.Bohrtiefe})");
        sb.AppendLine($"(Zustellung={p.Zustellung})");
        sb.AppendLine("G00 Z5.0000");

        var depth = -Math.Abs(p.Bohrtiefe);
        var step = Math.Abs(p.Zustellung);
        for (int iy = 0; iy < p.CountY; iy++)
        {
            for (int ix = 0; ix < p.CountX; ix++)
            {
                var x = p.StartX + ix * p.SpacingX;
                var y = p.StartY + iy * p.SpacingY;
                sb.AppendLine($"(Bohrung: X={x} Y={y})");
                sb.AppendLine($"G00 X{F(x)} Y{F(y)}");

                double currentZ = 0;
                while (currentZ > depth)
                {
                    currentZ = Math.Max(depth, currentZ - step);
                    sb.AppendLine($"G01 Z{F(currentZ)} F300");
                    sb.AppendLine("G00 Z5.0000");
                }
            }
        }

        sb.AppendLine("M05");
        return sb.ToString();
    }

    public static string Umfahren(UmfahrenParams p, double workW, double workH)
    {
        var sb = new StringBuilder();
        sb.AppendLine("(Umfahren)");
        sb.AppendLine($"(A={p.A})");
        sb.AppendLine($"(D={p.Diameter})");
        sb.AppendLine($"(Fertigradius={p.Radius})");
        sb.AppendLine($"(Z={p.Z})");
        sb.AppendLine($"(Startseite={p.StartSide})");
        sb.AppendLine($"(Drehzahl={p.Drehzahl})");
        sb.AppendLine($"(Fräsrichtung={p.Direction})");
        sb.AppendLine($"M03 S{p.Drehzahl}");
        sb.AppendLine("G00 Z5.0000");

        var toolOffset = p.Diameter / 2.0;
        var effectiveA = p.A + toolOffset;
        double x0 = -effectiveA;
        double y0 = -effectiveA;
        double x1 = workW + effectiveA;
        double y1 = workH + effectiveA;
        if (x1 <= x0 || y1 <= y0)
        {
            sb.AppendLine("(Ungültige Umfahrungsbreite)");
            sb.AppendLine("M05");
            return sb.ToString();
        }

        var z = p.Z;
        var requestedFinishedRadius = Math.Max(0, p.Radius);
        var halfW = (x1 - x0) / 2.0;
        var halfH = (y1 - y0) / 2.0;
        var r = Math.Min(requestedFinishedRadius + toolOffset, Math.Min(halfW, halfH));

        var startSide = p.StartSide?.ToLowerInvariant() ?? string.Empty;
        var direction = p.Direction?.ToLowerInvariant() ?? "gegenlauf";
        var gegenlauf = direction == "gegenlauf";
        double startX, startY;
        double approachX, approachY;
        double exitX, exitY;
        double leadIn = 50.0;
        double arcI = 0, arcJ = 0;
        double exitI = 0, exitJ = 0;
        string entryArcCmd, exitArcCmd;

        switch (startSide)
        {
            case "oben":
                startX = (x0 + x1) / 2.0;
                startY = y1;
                if (gegenlauf)
                {
                    entryArcCmd = "G02";
                    exitArcCmd = "G02";
                    approachX = startX + leadIn;
                    approachY = startY + leadIn;
                    exitX = startX - leadIn;
                    exitY = startY + leadIn;
                    arcI = -leadIn;
                    arcJ = 0;
                    exitI = 0;
                    exitJ = leadIn;
                }
                else
                {
                    entryArcCmd = "G03";
                    exitArcCmd = "G03";
                    approachX = startX - leadIn;
                    approachY = startY + leadIn;
                    exitX = startX + leadIn;
                    exitY = startY + leadIn;
                    arcI = leadIn;
                    arcJ = 0;
                    exitI = 0;
                    exitJ = leadIn;
                }
                break;
            case "rechts":
                startX = x1;
                startY = (y0 + y1) / 2.0;
                if (gegenlauf)
                {
                    entryArcCmd = "G02";
                    exitArcCmd = "G02";
                    approachX = startX + leadIn;
                    approachY = startY - leadIn;
                    exitX = startX + leadIn;
                    exitY = startY + leadIn;
                    arcI = 0;
                    arcJ = leadIn;
                    exitI = leadIn;
                    exitJ = 0;
                }
                else
                {
                    entryArcCmd = "G03";
                    exitArcCmd = "G03";
                    approachX = startX + leadIn;
                    approachY = startY + leadIn;
                    exitX = startX + leadIn;
                    exitY = startY - leadIn;
                    arcI = 0;
                    arcJ = -leadIn;
                    exitI = leadIn;
                    exitJ = 0;
                }
                break;
            case "unten":
                startX = (x0 + x1) / 2.0;
                startY = y0;
                if (gegenlauf)
                {
                    entryArcCmd = "G02";
                    exitArcCmd = "G02";
                    approachX = startX - leadIn;
                    approachY = startY - leadIn;
                    exitX = startX + leadIn;
                    exitY = startY - leadIn;
                    arcI = leadIn;
                    arcJ = 0;
                    exitI = 0;
                    exitJ = -leadIn;
                }
                else
                {
                    entryArcCmd = "G03";
                    exitArcCmd = "G03";
                    approachX = startX + leadIn;
                    approachY = startY - leadIn;
                    exitX = startX - leadIn;
                    exitY = startY - leadIn;
                    arcI = -leadIn;
                    arcJ = 0;
                    exitI = 0;
                    exitJ = -leadIn;
                }
                break;
            case "links":
                startX = x0;
                startY = (y0 + y1) / 2.0;
                if (gegenlauf)
                {
                    entryArcCmd = "G02";
                    exitArcCmd = "G02";
                    approachX = startX - leadIn;
                    approachY = startY + leadIn;
                    exitX = startX - leadIn;
                    exitY = startY - leadIn;
                    arcI = 0;
                    arcJ = -leadIn;
                    exitI = -leadIn;
                    exitJ = 0;
                }
                else
                {
                    entryArcCmd = "G03";
                    exitArcCmd = "G03";
                    approachX = startX - leadIn;
                    approachY = startY - leadIn;
                    exitX = startX - leadIn;
                    exitY = startY + leadIn;
                    arcI = 0;
                    arcJ = leadIn;
                    exitI = -leadIn;
                    exitJ = 0;
                }
                break;
            default:
                entryArcCmd = "G03";
                exitArcCmd = "G02";
                startX = (x0 + x1) / 2.0;
                startY = y0;
                approachX = startX - leadIn;
                approachY = startY - leadIn;
                exitX = startX + leadIn;
                exitY = startY - leadIn;
                arcI = leadIn;
                arcJ = 0;
                exitI = 0;
                exitJ = -leadIn;
                break;
        }

        sb.AppendLine($"G00 X{F(approachX)} Y{F(approachY)}");
        sb.AppendLine($"G01 Z{F(z)} F300");
        sb.AppendLine($"{entryArcCmd} X{F(startX)} Y{F(startY)} I{F(arcI)} J{F(arcJ)}");

        if (r <= 0)
        {
            if (startSide == "oben")
            {
                if (gegenlauf)
                {
                    sb.AppendLine($"G01 X{F(x0)} Y{F(y1)}");
                    sb.AppendLine($"G01 X{F(x0)} Y{F(y0)}");
                    sb.AppendLine($"G01 X{F(x1)} Y{F(y0)}");
                    sb.AppendLine($"G01 X{F(x1)} Y{F(y1)}");
                }
                else
                {
                    sb.AppendLine($"G01 X{F(x1)} Y{F(y1)}");
                    sb.AppendLine($"G01 X{F(x1)} Y{F(y0)}");
                    sb.AppendLine($"G01 X{F(x0)} Y{F(y0)}");
                    sb.AppendLine($"G01 X{F(x0)} Y{F(y1)}");
                }
                sb.AppendLine($"G01 X{F(startX)} Y{F(startY)}");
            }
            else if (startSide == "rechts")
            {
                if (gegenlauf)
                {
                    sb.AppendLine($"G01 X{F(x1)} Y{F(y1)}");
                    sb.AppendLine($"G01 X{F(x0)} Y{F(y1)}");
                    sb.AppendLine($"G01 X{F(x0)} Y{F(y0)}");
                    sb.AppendLine($"G01 X{F(x1)} Y{F(y0)}");
                }
                else
                {
                    sb.AppendLine($"G01 X{F(x1)} Y{F(y0)}");
                    sb.AppendLine($"G01 X{F(x0)} Y{F(y0)}");
                    sb.AppendLine($"G01 X{F(x0)} Y{F(y1)}");
                    sb.AppendLine($"G01 X{F(x1)} Y{F(y1)}");
                }
                sb.AppendLine($"G01 X{F(startX)} Y{F(startY)}");
            }
            else if (startSide == "links")
            {
                if (gegenlauf)
                {
                    sb.AppendLine($"G01 X{F(x0)} Y{F(y0)}");
                    sb.AppendLine($"G01 X{F(x1)} Y{F(y0)}");
                    sb.AppendLine($"G01 X{F(x1)} Y{F(y1)}");
                    sb.AppendLine($"G01 X{F(x0)} Y{F(y1)}");
                }
                else
                {
                    sb.AppendLine($"G01 X{F(x0)} Y{F(y1)}");
                    sb.AppendLine($"G01 X{F(x1)} Y{F(y1)}");
                    sb.AppendLine($"G01 X{F(x1)} Y{F(y0)}");
                    sb.AppendLine($"G01 X{F(x0)} Y{F(y0)}");
                }
                sb.AppendLine($"G01 X{F(startX)} Y{F(startY)}");
            }
            else
            {
                if (gegenlauf)
                {
                    sb.AppendLine($"G01 X{F(x1)} Y{F(y0)}");
                    sb.AppendLine($"G01 X{F(x1)} Y{F(y1)}");
                    sb.AppendLine($"G01 X{F(x0)} Y{F(y1)}");
                    sb.AppendLine($"G01 X{F(x0)} Y{F(y0)}");
                }
                else
                {
                    sb.AppendLine($"G01 X{F(x0)} Y{F(y0)}");
                    sb.AppendLine($"G01 X{F(x0)} Y{F(y1)}");
                    sb.AppendLine($"G01 X{F(x1)} Y{F(y1)}");
                    sb.AppendLine($"G01 X{F(x1)} Y{F(y0)}");
                }
                sb.AppendLine($"G01 X{F(startX)} Y{F(startY)}");
            }
        }
        else
        {
            if (startSide == "oben")
            {
                sb.AppendLine($"G01 X{F(x0 + r)} Y{F(y1)}");
                sb.AppendLine($"G03 X{F(x0)} Y{F(y1 - r)} I0 J{F(-r)}");
                sb.AppendLine($"G01 X{F(x0)} Y{F(y0 + r)}");
                sb.AppendLine($"G03 X{F(x0 + r)} Y{F(y0)} I{F(r)} J0");
                sb.AppendLine($"G01 X{F(x1 - r)} Y{F(y0)}");
                sb.AppendLine($"G03 X{F(x1)} Y{F(y0 + r)} I0 J{F(r)}");
                sb.AppendLine($"G01 X{F(x1)} Y{F(y1 - r)}");
                sb.AppendLine($"G03 X{F(x1 - r)} Y{F(y1)} I{F(-r)} J0");
                sb.AppendLine($"G01 X{F(startX)} Y{F(startY)}");
            }
            else if (startSide == "rechts")
            {
                sb.AppendLine($"G01 X{F(x1)} Y{F(y1 - r)}");
                sb.AppendLine($"G03 X{F(x1 - r)} Y{F(y1)} I{F(-r)} J0");
                sb.AppendLine($"G01 X{F(x0 + r)} Y{F(y1)}");
                sb.AppendLine($"G03 X{F(x0)} Y{F(y1 - r)} I0 J{F(-r)}");
                sb.AppendLine($"G01 X{F(x0)} Y{F(y0 + r)}");
                sb.AppendLine($"G03 X{F(x0 + r)} Y{F(y0)} I{F(r)} J0");
                sb.AppendLine($"G01 X{F(x1 - r)} Y{F(y0)}");
                sb.AppendLine($"G03 X{F(x1)} Y{F(y0 + r)} I0 J{F(r)}");
                sb.AppendLine($"G01 X{F(startX)} Y{F(startY)}");
            }
            else if (startSide == "links")
            {
                sb.AppendLine($"G01 X{F(x0)} Y{F(y0 + r)}");
                sb.AppendLine($"G03 X{F(x0 + r)} Y{F(y0)} I{F(r)} J0");
                sb.AppendLine($"G01 X{F(x1 - r)} Y{F(y0)}");
                sb.AppendLine($"G03 X{F(x1)} Y{F(y0 + r)} I0 J{F(r)}");
                sb.AppendLine($"G01 X{F(x1)} Y{F(y1 - r)}");
                sb.AppendLine($"G03 X{F(x1 - r)} Y{F(y1)} I{F(-r)} J0");
                sb.AppendLine($"G01 X{F(x0 + r)} Y{F(y1)}");
                sb.AppendLine($"G03 X{F(x0)} Y{F(y1 - r)} I0 J{F(-r)}");
                sb.AppendLine($"G01 X{F(startX)} Y{F(startY)}");
            }
            else
            {
                sb.AppendLine($"G01 X{F(x1 - r)} Y{F(y0)}");
                sb.AppendLine($"G03 X{F(x1)} Y{F(y0 + r)} I0 J{F(r)}");
                sb.AppendLine($"G01 X{F(x1)} Y{F(y1 - r)}");
                sb.AppendLine($"G03 X{F(x1 - r)} Y{F(y1)} I{F(-r)} J0");
                sb.AppendLine($"G01 X{F(x0 + r)} Y{F(y1)}");
                sb.AppendLine($"G03 X{F(x0)} Y{F(y1 - r)} I0 J{F(-r)}");
                sb.AppendLine($"G01 X{F(x0)} Y{F(y0 + r)}");
                sb.AppendLine($"G03 X{F(x0 + r)} Y{F(y0)} I{F(r)} J0");
                sb.AppendLine($"G01 X{F(startX)} Y{F(startY)}");
            }
        }

        sb.AppendLine($"{exitArcCmd} X{F(exitX)} Y{F(exitY)} I{F(exitI)} J{F(exitJ)}");
        sb.AppendLine("G00 Z5.0000");
        sb.AppendLine("M05");
        return sb.ToString();
    }

    // Berechnet versetzte Segmentpunkte und liefert (Startpunkt, Liste von G-Code-Moves).
    // Konvexe Ecken → G02/G03-Bogen; konkave Ecken → Miter-Schnittpunkt.
    private static ((double X, double Y) Start, List<string> Moves) BuildOffsetMoves(
        List<(double X, double Y)> pts, double offset, double feed)
    {
        int n = pts.Count;

        // Einheitsnormalen pro Segment (links-Normal: (-dy, dx))
        var norms = new (double x, double y)[n - 1];
        for (int i = 0; i < n - 1; i++)
        {
            double dx = pts[i + 1].X - pts[i].X;
            double dy = pts[i + 1].Y - pts[i].Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            norms[i] = len < 1e-9 ? (0, 0) : (-dy / len, dx / len);
        }

        // Segment-Start- und Endpunkte im versetzten Pfad
        var segS = new (double X, double Y)[n - 1];
        var segE = new (double X, double Y)[n - 1];
        for (int i = 0; i < n - 1; i++)
        {
            segS[i] = (pts[i].X     + norms[i].x * offset, pts[i].Y     + norms[i].y * offset);
            segE[i] = (pts[i + 1].X + norms[i].x * offset, pts[i + 1].Y + norms[i].y * offset);
        }

        // 0=Miter, 1=G02(CW), 2=G03(CCW) pro innerer Ecke
        var cornerKind = new int[n - 2];
        for (int i = 0; i < n - 2; i++)
        {
            double d1x = pts[i + 1].X - pts[i].X,     d1y = pts[i + 1].Y - pts[i].Y;
            double d2x = pts[i + 2].X - pts[i + 1].X, d2y = pts[i + 2].Y - pts[i + 1].Y;
            double cross = d1x * d2y - d1y * d2x;

            if (Math.Abs(offset) > 1e-9 && cross * offset < 0)
            {
                // Konvexe Ecke: Bogen um pts[i+1]
                // segE[i] und segS[i+1] bleiben wie berechnet (je auf ihrer Segmentnormalen)
                cornerKind[i] = cross < 0 ? 1 : 2;
            }
            else
            {
                // Konkave Ecke oder Mitte: Miter-Schnittpunkt
                var (n1x, n1y) = norms[i];
                var (n2x, n2y) = norms[i + 1];
                double bx  = n1x + n2x, by = n1y + n2y;
                double dot = bx * n1x + by * n1y;
                double mx, my;
                if (dot < 1e-9) { mx = pts[i+1].X + n1x * offset; my = pts[i+1].Y + n1y * offset; }
                else             { mx = pts[i+1].X + bx * (offset / dot); my = pts[i+1].Y + by * (offset / dot); }
                segE[i]     = (mx, my);
                segS[i + 1] = (mx, my);
                cornerKind[i] = 0;
            }
        }

        var moves = new List<string>();
        for (int i = 0; i < n - 1; i++)
        {
            moves.Add($"G01 X{F(segE[i].X)} Y{F(segE[i].Y)} F{feed}");

            if (i < n - 2 && cornerKind[i] != 0)
            {
                // Bogen: Zentrum = pts[i+1], aktuelle Pos = segE[i], Ziel = segS[i+1]
                double ix = pts[i + 1].X - segE[i].X;
                double ij = pts[i + 1].Y - segE[i].Y;
                string cmd = cornerKind[i] == 1 ? "G02" : "G03";
                moves.Add($"{cmd} X{F(segS[i+1].X)} Y{F(segS[i+1].Y)} I{F(ix)} J{F(ij)} F{feed}");
            }
        }

        return (segS[0], moves);
    }

    public static string PfadFräsen(PfadFräsenParams p, double workW, double workH)
    {
        var sb = new StringBuilder();
        sb.AppendLine("(Pfad Fräsen)");
        sb.AppendLine($"(Punkte: {p.Punkte.Count}, Koordinaten absolut)");
        sb.AppendLine($"(D={p.FraeserD}, Seite={p.Seite})");
        sb.AppendLine();
        sb.AppendLine($"M03 S{p.Drehzahl}");
        sb.AppendLine("G00 Z5.0000");

        double radius = p.FraeserD / 2.0;
        double offset = p.Seite switch
        {
            "Links"  =>  radius,
            "Rechts" => -radius,
            _        =>  0.0,
        };

        var (start, moves) = BuildOffsetMoves(p.Punkte, offset, p.Vorschub);
        sb.AppendLine($"G00 X{F(start.X)} Y{F(start.Y)}");

        double depth    = -Math.Abs(p.Z);
        double step     = Math.Abs(p.Zustellung);
        double currentZ = 0;

        while (currentZ > depth)
        {
            currentZ = Math.Max(depth, currentZ - step);
            sb.AppendLine($"G01 Z{F(currentZ)} F{p.Vorschub}");

            foreach (var move in moves)
                sb.AppendLine(move);

            if (currentZ > depth)
            {
                sb.AppendLine("G00 Z1.0000");
                sb.AppendLine($"G00 X{F(start.X)} Y{F(start.Y)}");
            }
        }

        sb.AppendLine("G00 Z5.0000");
        sb.AppendLine("M05");
        return sb.ToString();
    }

    private static (double x, double y) ConvertBezugspunkt(string ref_, double xRel, double yRel, double w, double h)
        => ref_ switch
        {
            "Unten links"  => (xRel, yRel),
            "Oben links"   => (xRel, h - yRel),
            "Unten rechts" => (w - xRel, yRel),
            "Oben rechts"  => (w - xRel, h - yRel),
            "Links Mitte"  => (xRel, h / 2 + yRel),
            "Rechts Mitte" => (w - xRel, h / 2 + yRel),
            "Oben Mitte"   => (w / 2 + xRel, h - yRel),
            "Unten Mitte"  => (w / 2 + xRel, yRel),
            "Mitte"  => (w / 2 + xRel, h / 2 + yRel),
            _              => (xRel, yRel)
        };
}
