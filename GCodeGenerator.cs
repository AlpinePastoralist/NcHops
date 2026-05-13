using System.Globalization;
using System.Text;

namespace NCHops;

public static class GCodeGenerator
{
    public static double SafeZ { get; set; } = 5.0;

    private static string F(double v) => v.ToString("F4", CultureInfo.InvariantCulture);
    private static string Sz() => $"G00 Z{F(SafeZ)}";

    public static string Planfräsen(PlanfräsenParams p)
    {
        double schritt = p.FraeserD * p.Faktor;
        var sb = new StringBuilder();
        sb.AppendLine("(Benötigte Werkzeuge:)");
        sb.AppendLine("(planfräser)");
        sb.AppendLine();
        sb.AppendLine($"M03 S{p.Drehzahl}");
        sb.AppendLine(Sz());
        sb.AppendLine();
        sb.AppendLine($"G00 X{F(p.X0)} Y{F(p.Y0)}");
        sb.AppendLine($"G01 Z{F(p.Z)} F{(int)p.VorschubFz}");

        if (p.Horizontal)
        {
            double y = p.Y0;
            bool hin = true;
            while (y < p.Y1)
            {
                sb.AppendLine(hin ? $"G01 X{F(p.X1)} F{(int)p.Vorschub}" : $"G01 X{F(p.X0)} F{(int)p.Vorschub}");
                y += schritt;
                sb.AppendLine($"G01 Y{F(y)} F{(int)p.Vorschub}");
                if (y >= p.Y1)
                {
                    hin = !hin;
                    sb.AppendLine(hin ? $"G01 X{F(p.X1)} F{(int)p.Vorschub}" : $"G01 X{F(p.X0)} F{(int)p.Vorschub}");
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
                sb.AppendLine(hin ? $"G01 Y{F(p.Y1)} F{(int)p.Vorschub}" : $"G01 Y{F(p.Y0)} F{(int)p.Vorschub}");
                x += schritt;
                sb.AppendLine($"G01 X{F(x)} F{(int)p.Vorschub}");
                if (x >= p.X1)
                {
                    hin = !hin;
                    sb.AppendLine(hin ? $"G01 Y{F(p.Y1)} F{(int)p.Vorschub}" : $"G01 Y{F(p.Y0)} F{(int)p.Vorschub}");
                    break;
                }
                hin = !hin;
            }
        }

        sb.AppendLine();
        sb.AppendLine(Sz());
        sb.AppendLine("M05");
        return sb.ToString();
    }

    public static string Bohrung(BohrungParams p, double workW, double workH)
    {
        var sb = new StringBuilder();
        sb.AppendLine("(Bohrung)");
        sb.AppendLine($"(D={p.Durchmesser}, Bezug={p.Bezugspunkt})");
        sb.AppendLine(Sz());

        var (x, y) = ConvertBezugspunkt(p.Bezugspunkt, p.XRel, p.YRel, workW, workH);
        sb.AppendLine($"G00 X{F(x)} Y{F(y)}");

        var zDepth = -Math.Abs(p.Bohrtiefe);
        var zStep  =  Math.Abs(p.Zustellung);
        double currentZ = 0;
        while (currentZ > zDepth)
        {
            currentZ = Math.Max(zDepth, currentZ - zStep);
            sb.AppendLine($"G01 Z{F(currentZ)} F{(int)p.VorschubFz}");
            sb.AppendLine(Sz());
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
        sb.AppendLine(Sz());

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
                    sb.AppendLine($"G01 Z{F(currentZ)} F{(int)p.VorschubFz}");
                    sb.AppendLine(Sz());
                }
            }
        }

        sb.AppendLine("M05");
        return sb.ToString();
    }

    public static string Tasche(TascheFräsenParams p, double workW, double workH)
    {
        const double allowance = 1.0; // Schlichtaufmaß an allen Wänden

        double r    = p.FraeserD / 2.0;
        double step = Math.Max(0.1, p.FraeserD * p.Faktor);

        var (refX, refY) = ConvertBezugspunkt(p.Bezugspunkt, p.XRel, p.YRel, workW, workH);

        // Bezugspunkt zeigt auf die entsprechende Ecke/Seite der Tasche selbst.
        // Offset auf die untere-linke Taschenecke umrechnen.
        var (bx, by) = p.Bezugspunkt switch
        {
            "Unten links"  => (0,             0),
            "Unten Mitte"  => (-p.Breite / 2, 0),
            "Unten rechts" => (-p.Breite,     0),
            "Links Mitte"  => (0,             -p.Höhe / 2),
            "Mitte"        => (-p.Breite / 2, -p.Höhe / 2),
            "Rechts Mitte" => (-p.Breite,     -p.Höhe / 2),
            "Oben links"   => (0,             -p.Höhe),
            "Oben Mitte"   => (-p.Breite / 2, -p.Höhe),
            "Oben rechts"  => (-p.Breite,     -p.Höhe),
            _              => (0.0,            0.0)
        };

        double ax = refX + bx;
        double ay = refY + by;

        // Fräsermittelpunkt-Bereich bei vollem Wandeingriff (Schlichten)
        double ix0 = ax + r;
        double iy0 = ay + r;
        double ix1 = ax + p.Breite - r;
        double iy1 = ay + p.Höhe   - r;

        var sb = new StringBuilder();
        sb.AppendLine("(Tasche fräsen)");
        sb.AppendLine($"(X={F(ax)} Y={F(ay)} B={F(p.Breite)} H={F(p.Höhe)})");
        sb.AppendLine($"(D={p.FraeserD}, Bezug={p.Bezugspunkt})");

        if (ix1 <= ix0 || iy1 <= iy0)
        {
            sb.AppendLine("(Tasche zu klein für Werkzeug)");
            sb.AppendLine("M05");
            return sb.ToString();
        }

        sb.AppendLine($"M03 S{p.Drehzahl}");
        sb.AppendLine(Sz());

        double depth = -Math.Abs(p.ZTiefe);
        double zStep = Math.Abs(p.ZZustellung);
        double curZ  = 0;

        // Schrupp-Bereich: 1mm Aufmaß an allen Wänden lassen
        double rx0 = ix0 + allowance;
        double ry0 = iy0 + allowance;
        double rx1 = ix1 - allowance;
        double ry1 = iy1 - allowance;
        bool hasRoughArea = rx1 > rx0 && ry1 > ry0;

        // Zick-Zack-Räumen (mit oder ohne Aufmaß)
        double zx0 = hasRoughArea ? rx0 : ix0;
        double zy0 = hasRoughArea ? ry0 : iy0;
        double zx1 = hasRoughArea ? rx1 : ix1;
        double zy1 = hasRoughArea ? ry1 : iy1;

        sb.AppendLine($"G00 X{F(zx0)} Y{F(zy0)}");
        while (curZ > depth)
        {
            curZ = Math.Max(depth, curZ - zStep);
            sb.AppendLine($"G01 Z{F(curZ)} F{(int)p.VorschubFz}");

            double y       = zy0;
            bool rightward = true;
            while (true)
            {
                sb.AppendLine(rightward
                    ? $"G01 X{F(zx1)} F{(int)p.Vorschub}"
                    : $"G01 X{F(zx0)} F{(int)p.Vorschub}");
                if (y >= zy1) break;
                y = Math.Min(y + step, zy1);
                sb.AppendLine($"G01 Y{F(y)} F{(int)p.Vorschub}");
                rightward = !rightward;
            }

            if (curZ > depth)
            {
                sb.AppendLine(Sz());
                sb.AppendLine($"G00 X{F(zx0)} Y{F(zy0)}");
            }
        }

        // Schlichten: Konturfahrt im Gegenlauf (Uhrzeigersinn) auf voller Tiefe
        sb.AppendLine(Sz());
        sb.AppendLine($"G00 X{F(ix0)} Y{F(iy0)}");
        sb.AppendLine($"G01 Z{F(depth)} F{(int)p.VorschubFz}");
        sb.AppendLine($"G01 X{F(ix1)} Y{F(iy0)} F{(int)p.Vorschub}");
        sb.AppendLine($"G01 X{F(ix1)} Y{F(iy1)} F{(int)p.Vorschub}");
        sb.AppendLine($"G01 X{F(ix0)} Y{F(iy1)} F{(int)p.Vorschub}");
        sb.AppendLine($"G01 X{F(ix0)} Y{F(iy0)} F{(int)p.Vorschub}");

        sb.AppendLine(Sz());
        sb.AppendLine("M05");
        return sb.ToString();
    }

    public static string Kreistasche(KreistascheParams p, double workW, double workH)
    {
        const double allowance = 1.0;

        double r       = p.FraeserD / 2.0;
        double step    = Math.Max(0.1, p.FraeserD * p.Faktor);
        double Rp      = p.Durchmesser / 2.0;
        double Rm      = Rp - r;

        var (cx, cy) = ConvertBezugspunkt(p.Bezugspunkt, p.XRel, p.YRel, workW, workH);

        var sb = new StringBuilder();
        sb.AppendLine("(Kreistasche fräsen)");
        sb.AppendLine($"(Mitte X={F(cx)} Y={F(cy)}, D={F(p.Durchmesser)})");
        sb.AppendLine($"(D={p.FraeserD}, Eintauchwinkel={p.Eintauchwinkel}°, Bezug={p.Bezugspunkt})");

        if (Rm <= 0)
        {
            sb.AppendLine("(Tasche zu klein für Werkzeug)");
            sb.AppendLine("M05");
            return sb.ToString();
        }

        sb.AppendLine($"M03 S{p.Drehzahl}");
        sb.AppendLine(Sz());

        double depth     = -Math.Abs(p.ZTiefe);
        double zStep     = Math.Abs(p.ZZustellung);
        double roughRm   = Rm - allowance;
        double maxRoughR = roughRm > 0 ? roughRm : Rm;

        // Eintauchradius: erster Kreisschritt, auf Rm begrenzt
        double rEntry   = Math.Min(step, maxRoughR);
        double angleRad = Math.PI / 180.0 * Math.Abs(p.Eintauchwinkel);
        // Z-Absenkung pro Helix-Umdrehung (Eintauchwinkel)
        double zPerRev  = p.Eintauchwinkel > 0.01
            ? 2.0 * Math.PI * rEntry * Math.Tan(angleRad)
            : 0;

        sb.AppendLine($"G00 X{F(cx + rEntry)} Y{F(cy)}");
        double curZ = 0;

        while (curZ > depth)
        {
            double nextZ = Math.Max(depth, curZ - zStep);

            // Helikal eintauchen von curZ nach nextZ im Gegenlauf (G02 = CW)
            if (zPerRev > 1e-6)
            {
                double z = curZ;
                while (z > nextZ)
                {
                    z = Math.Max(nextZ, z - zPerRev);
                    sb.AppendLine($"G02 X{F(cx + rEntry)} Y{F(cy)} Z{F(z)} I{F(-rEntry)} J0 F{(int)p.Vorschub}");
                }
            }
            else
            {
                sb.AppendLine($"G01 Z{F(nextZ)} F{(int)p.VorschubFz}");
            }
            curZ = nextZ;

            // Mittenfreischnitt: Durchmesserbahn räumt den Mittelstumpf
            sb.AppendLine($"G01 X{F(cx - rEntry)} Y{F(cy)} F{(int)p.Vorschub}");
            sb.AppendLine($"G01 X{F(cx + rEntry)} Y{F(cy)} F{(int)p.Vorschub}");

            // Archimedische Spirale von rEntry nach maxRoughR im Gegenlauf (CW = neg. Winkel)
            // Segmente pro Umdrehung aus Sehnentoleranz, damit die Kurve bei großen Radien glatt bleibt
            if (maxRoughR > rEntry)
            {
                const double chordalTol = 0.05; // mm – max. Abstand Sehne/Kreisbogen
                const int    maxSegsPerRev = 144; // max. 2.5° pro Segment
                double maxAngleRad = Math.Acos(1.0 - chordalTol / maxRoughR);
                int segPerRev = Math.Min(maxSegsPerRev, (int)Math.Ceiling(2.0 * Math.PI / maxAngleRad));
                double totalRevs = (maxRoughR - rEntry) / step;
                int totalSegs = Math.Min(2000, Math.Max(segPerRev, (int)Math.Ceiling(totalRevs * segPerRev)));
                for (int i = 1; i <= totalSegs; i++)
                {
                    double t       = (double)i / totalSegs;
                    double spiralR = rEntry + (maxRoughR - rEntry) * t;
                    double angle   = -2.0 * Math.PI * totalRevs * t; // CW
                    sb.AppendLine($"G01 X{F(cx + spiralR * Math.Cos(angle))} Y{F(cy + spiralR * Math.Sin(angle))} F{(int)p.Vorschub}");
                }
            }
            // Abschlusskreis bei maxRoughR (stellt vollständige Abdeckung sicher)
            sb.AppendLine($"G01 X{F(cx + maxRoughR)} Y{F(cy)} F{(int)p.Vorschub}");
            sb.AppendLine($"G02 X{F(cx + maxRoughR)} Y{F(cy)} I{F(-maxRoughR)} J0 F{(int)p.Vorschub}");

            // Für nächste Tiefenstufe zurück zum Eintauchpunkt (im geräumten Bereich)
            if (curZ > depth)
                sb.AppendLine($"G01 X{F(cx + rEntry)} Y{F(cy)} F{(int)p.Vorschub}");
        }

        // Schlichten: voller Radius im Gegenlauf (G02 = CW = rechts herum)
        sb.AppendLine(Sz());
        sb.AppendLine($"G00 X{F(cx + Rm)} Y{F(cy)}");
        sb.AppendLine($"G01 Z{F(depth)} F{(int)p.VorschubFz}");
        sb.AppendLine($"G02 X{F(cx + Rm)} Y{F(cy)} I{F(-Rm)} J0 F{(int)p.Vorschub}");

        sb.AppendLine(Sz());
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
        sb.AppendLine(Sz());

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
        sb.AppendLine($"G01 Z{F(z)} F{(int)p.VorschubFz}");
        sb.AppendLine($"{entryArcCmd} X{F(startX)} Y{F(startY)} I{F(arcI)} J{F(arcJ)} F{(int)p.VorschubFxy}");

        if (r <= 0)
        {
            if (startSide == "oben")
            {
                if (gegenlauf)
                {
                    sb.AppendLine($"G01 X{F(x0)} Y{F(y1)} F{(int)p.VorschubFxy}");
                    sb.AppendLine($"G01 X{F(x0)} Y{F(y0)} F{(int)p.VorschubFxy}");
                    sb.AppendLine($"G01 X{F(x1)} Y{F(y0)} F{(int)p.VorschubFxy}");
                    sb.AppendLine($"G01 X{F(x1)} Y{F(y1)} F{(int)p.VorschubFxy}");
                }
                else
                {
                    sb.AppendLine($"G01 X{F(x1)} Y{F(y1)} F{(int)p.VorschubFxy}");
                    sb.AppendLine($"G01 X{F(x1)} Y{F(y0)} F{(int)p.VorschubFxy}");
                    sb.AppendLine($"G01 X{F(x0)} Y{F(y0)} F{(int)p.VorschubFxy}");
                    sb.AppendLine($"G01 X{F(x0)} Y{F(y1)} F{(int)p.VorschubFxy}");
                }
                sb.AppendLine($"G01 X{F(startX)} Y{F(startY)} F{(int)p.VorschubFxy}");
            }
            else if (startSide == "rechts")
            {
                if (gegenlauf)
                {
                    sb.AppendLine($"G01 X{F(x1)} Y{F(y1)} F{(int)p.VorschubFxy}");
                    sb.AppendLine($"G01 X{F(x0)} Y{F(y1)} F{(int)p.VorschubFxy}");
                    sb.AppendLine($"G01 X{F(x0)} Y{F(y0)} F{(int)p.VorschubFxy}");
                    sb.AppendLine($"G01 X{F(x1)} Y{F(y0)} F{(int)p.VorschubFxy}");
                }
                else
                {
                    sb.AppendLine($"G01 X{F(x1)} Y{F(y0)} F{(int)p.VorschubFxy}");
                    sb.AppendLine($"G01 X{F(x0)} Y{F(y0)} F{(int)p.VorschubFxy}");
                    sb.AppendLine($"G01 X{F(x0)} Y{F(y1)} F{(int)p.VorschubFxy}");
                    sb.AppendLine($"G01 X{F(x1)} Y{F(y1)} F{(int)p.VorschubFxy}");
                }
                sb.AppendLine($"G01 X{F(startX)} Y{F(startY)} F{(int)p.VorschubFxy}");
            }
            else if (startSide == "links")
            {
                if (gegenlauf)
                {
                    sb.AppendLine($"G01 X{F(x0)} Y{F(y0)} F{(int)p.VorschubFxy}");
                    sb.AppendLine($"G01 X{F(x1)} Y{F(y0)} F{(int)p.VorschubFxy}");
                    sb.AppendLine($"G01 X{F(x1)} Y{F(y1)} F{(int)p.VorschubFxy}");
                    sb.AppendLine($"G01 X{F(x0)} Y{F(y1)} F{(int)p.VorschubFxy}");
                }
                else
                {
                    sb.AppendLine($"G01 X{F(x0)} Y{F(y1)} F{(int)p.VorschubFxy}");
                    sb.AppendLine($"G01 X{F(x1)} Y{F(y1)} F{(int)p.VorschubFxy}");
                    sb.AppendLine($"G01 X{F(x1)} Y{F(y0)} F{(int)p.VorschubFxy}");
                    sb.AppendLine($"G01 X{F(x0)} Y{F(y0)} F{(int)p.VorschubFxy}");
                }
                sb.AppendLine($"G01 X{F(startX)} Y{F(startY)} F{(int)p.VorschubFxy}");
            }
            else
            {
                if (gegenlauf)
                {
                    sb.AppendLine($"G01 X{F(x1)} Y{F(y0)} F{(int)p.VorschubFxy}");
                    sb.AppendLine($"G01 X{F(x1)} Y{F(y1)} F{(int)p.VorschubFxy}");
                    sb.AppendLine($"G01 X{F(x0)} Y{F(y1)} F{(int)p.VorschubFxy}");
                    sb.AppendLine($"G01 X{F(x0)} Y{F(y0)} F{(int)p.VorschubFxy}");
                }
                else
                {
                    sb.AppendLine($"G01 X{F(x0)} Y{F(y0)} F{(int)p.VorschubFxy}");
                    sb.AppendLine($"G01 X{F(x0)} Y{F(y1)} F{(int)p.VorschubFxy}");
                    sb.AppendLine($"G01 X{F(x1)} Y{F(y1)} F{(int)p.VorschubFxy}");
                    sb.AppendLine($"G01 X{F(x1)} Y{F(y0)} F{(int)p.VorschubFxy}");
                }
                sb.AppendLine($"G01 X{F(startX)} Y{F(startY)} F{(int)p.VorschubFxy}");
            }
        }
        else
        {
            if (startSide == "oben")
            {
                sb.AppendLine($"G01 X{F(x0 + r)} Y{F(y1)} F{(int)p.VorschubFxy}");
                sb.AppendLine($"G03 X{F(x0)} Y{F(y1 - r)} I0 J{F(-r)} F{(int)p.VorschubFxy}");
                sb.AppendLine($"G01 X{F(x0)} Y{F(y0 + r)} F{(int)p.VorschubFxy}");
                sb.AppendLine($"G03 X{F(x0 + r)} Y{F(y0)} I{F(r)} J0 F{(int)p.VorschubFxy}");
                sb.AppendLine($"G01 X{F(x1 - r)} Y{F(y0)} F{(int)p.VorschubFxy}");
                sb.AppendLine($"G03 X{F(x1)} Y{F(y0 + r)} I0 J{F(r)} F{(int)p.VorschubFxy}");
                sb.AppendLine($"G01 X{F(x1)} Y{F(y1 - r)} F{(int)p.VorschubFxy}");
                sb.AppendLine($"G03 X{F(x1 - r)} Y{F(y1)} I{F(-r)} J0 F{(int)p.VorschubFxy}");
                sb.AppendLine($"G01 X{F(startX)} Y{F(startY)} F{(int)p.VorschubFxy}");
            }
            else if (startSide == "rechts")
            {
                sb.AppendLine($"G01 X{F(x1)} Y{F(y1 - r)} F{(int)p.VorschubFxy}");
                sb.AppendLine($"G03 X{F(x1 - r)} Y{F(y1)} I{F(-r)} J0 F{(int)p.VorschubFxy}");
                sb.AppendLine($"G01 X{F(x0 + r)} Y{F(y1)} F{(int)p.VorschubFxy}");
                sb.AppendLine($"G03 X{F(x0)} Y{F(y1 - r)} I0 J{F(-r)} F{(int)p.VorschubFxy}");
                sb.AppendLine($"G01 X{F(x0)} Y{F(y0 + r)} F{(int)p.VorschubFxy}");
                sb.AppendLine($"G03 X{F(x0 + r)} Y{F(y0)} I{F(r)} J0 F{(int)p.VorschubFxy}");
                sb.AppendLine($"G01 X{F(x1 - r)} Y{F(y0)} F{(int)p.VorschubFxy}");
                sb.AppendLine($"G03 X{F(x1)} Y{F(y0 + r)} I0 J{F(r)} F{(int)p.VorschubFxy}");
                sb.AppendLine($"G01 X{F(startX)} Y{F(startY)} F{(int)p.VorschubFxy}");
            }
            else if (startSide == "links")
            {
                sb.AppendLine($"G01 X{F(x0)} Y{F(y0 + r)} F{(int)p.VorschubFxy}");
                sb.AppendLine($"G03 X{F(x0 + r)} Y{F(y0)} I{F(r)} J0 F{(int)p.VorschubFxy}");
                sb.AppendLine($"G01 X{F(x1 - r)} Y{F(y0)} F{(int)p.VorschubFxy}");
                sb.AppendLine($"G03 X{F(x1)} Y{F(y0 + r)} I0 J{F(r)} F{(int)p.VorschubFxy}");
                sb.AppendLine($"G01 X{F(x1)} Y{F(y1 - r)} F{(int)p.VorschubFxy}");
                sb.AppendLine($"G03 X{F(x1 - r)} Y{F(y1)} I{F(-r)} J0 F{(int)p.VorschubFxy}");
                sb.AppendLine($"G01 X{F(x0 + r)} Y{F(y1)} F{(int)p.VorschubFxy}");
                sb.AppendLine($"G03 X{F(x0)} Y{F(y1 - r)} I0 J{F(-r)} F{(int)p.VorschubFxy}");
                sb.AppendLine($"G01 X{F(startX)} Y{F(startY)} F{(int)p.VorschubFxy}");
            }
            else
            {
                sb.AppendLine($"G01 X{F(x1 - r)} Y{F(y0)} F{(int)p.VorschubFxy}");
                sb.AppendLine($"G03 X{F(x1)} Y{F(y0 + r)} I0 J{F(r)} F{(int)p.VorschubFxy}");
                sb.AppendLine($"G01 X{F(x1)} Y{F(y1 - r)} F{(int)p.VorschubFxy}");
                sb.AppendLine($"G03 X{F(x1 - r)} Y{F(y1)} I{F(-r)} J0 F{(int)p.VorschubFxy}");
                sb.AppendLine($"G01 X{F(x0 + r)} Y{F(y1)} F{(int)p.VorschubFxy}");
                sb.AppendLine($"G03 X{F(x0)} Y{F(y1 - r)} I0 J{F(-r)} F{(int)p.VorschubFxy}");
                sb.AppendLine($"G01 X{F(x0)} Y{F(y0 + r)} F{(int)p.VorschubFxy}");
                sb.AppendLine($"G03 X{F(x0 + r)} Y{F(y0)} I{F(r)} J0 F{(int)p.VorschubFxy}");
                sb.AppendLine($"G01 X{F(startX)} Y{F(startY)} F{(int)p.VorschubFxy}");
            }
        }

        sb.AppendLine($"{exitArcCmd} X{F(exitX)} Y{F(exitY)} I{F(exitI)} J{F(exitJ)} F{(int)p.VorschubFxy}");
        sb.AppendLine(Sz());
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

#if false // PfadFräsen (deaktiviert)
    public static string PfadFräsen(PfadFräsenParams p, double workW, double workH)
    {
        var sb = new StringBuilder();
        sb.AppendLine("(Pfad Fräsen)");
        sb.AppendLine($"(Punkte: {p.Punkte.Count}, Koordinaten absolut)");
        sb.AppendLine($"(D={p.FraeserD}, Seite={p.Seite})");
        sb.AppendLine();
        sb.AppendLine($"M03 S{p.Drehzahl}");
        sb.AppendLine(Sz());

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
                sb.AppendLine(Sz());
                sb.AppendLine($"G00 X{F(start.X)} Y{F(start.Y)}");
            }
        }

        sb.AppendLine(Sz());
        sb.AppendLine("M05");
        return sb.ToString();
    }
#endif // PfadFräsen Ende

    public static string PfadFräsen(IReadOnlyList<PfadPunktParams> path, double workW, double workH)
    {
        if (path.Count == 0) return string.Empty;
        var sp    = path[0];
        double z  = -Math.Abs(sp.ZTiefe);
        double r  = sp.FraeserD / 2.0;
        bool corr = sp.Radiuskorrektur != "Mittig";
        double sg = sp.Radiuskorrektur == "Links" ? 1.0 : -1.0;

        var pts = new List<(double x, double y)>();
        for (int i = 0; i < path.Count; i++)
        {
            var p = path[i];
            if (p.Bezugspunkt == "Letzter Punkt" && pts.Count > 0)
                pts.Add((pts[^1].x + p.XRel, pts[^1].y + p.YRel));
            else
                pts.Add(ConvertBezugspunkt(p.Bezugspunkt, p.XRel, p.YRel, workW, workH));
        }

        // Geschlossener Pfad: erster und letzter Punkt identisch (< 0.01 mm)
        bool closed = pts.Count >= 3 &&
            Math.Sqrt(Math.Pow(pts[0].x - pts[^1].x, 2) + Math.Pow(pts[0].y - pts[^1].y, 2)) < 0.01;

        // Unique points (no duplicate endpoint for closed path)
        var uniquePts = closed ? pts.Take(pts.Count - 1).ToList() : pts;

        // Corner rounding: Startpunkt-R als globaler Fallback, Einzelpunkte können überschreiben
        double globalR = sp.Verrundung;
        var verrundungen = path.Select(p => p.Verrundung > 1e-10 ? p.Verrundung : globalR).ToList();
        bool hasRounding = verrundungen.Any(v => v > 1e-10);

        List<PathMove> moves;
        if (corr && r > 1e-10 && uniquePts.Count >= 2)
        {
            moves = closed
                ? ComputeClosedOffsetMoves(uniquePts, r, sg, verrundungen)
                : ComputeOffsetMoves(uniquePts, r, sg, verrundungen);
        }
        else if (hasRounding && uniquePts.Count >= 3)
        {
            // Mittig mit Verrundung: Offset = 0, Bögen direkt in Werkstückpfad
            moves = closed
                ? ComputeClosedOffsetMoves(uniquePts, 0, 1, verrundungen)
                : ComputeOffsetMoves(uniquePts, 0, 1, verrundungen);
            if (closed && moves.Count > 0) moves.Add(moves[0]);
        }
        else
        {
            moves = uniquePts.Select(p => new PathMove(p.x, p.y)).ToList();
            if (closed) moves.Add(moves[0]); // Pfad schliessen
        }

        var sb = new StringBuilder();
        sb.AppendLine("(Pfad Fräsen)");
        sb.AppendLine($"M03 S{(int)sp.Drehzahl}");
        sb.AppendLine(Sz());
        sb.AppendLine($"G00 X{F(moves[0].X)} Y{F(moves[0].Y)}");

        double zStep = Math.Abs(sp.ZZustellung);
        double curZ  = 0;
        while (curZ > z)
        {
            curZ = Math.Max(z, curZ - zStep);
            sb.AppendLine($"G01 Z{F(curZ)} F{(int)sp.VorschubFz}");

            for (int i = 1; i < moves.Count; i++)
            {
                var mv = moves[i];
                if (mv.IsArc)
                    sb.AppendLine($"{(mv.CW ? "G02" : "G03")} X{F(mv.X)} Y{F(mv.Y)} I{F(mv.I)} J{F(mv.J)} F{(int)sp.Vorschub}");
                else
                    sb.AppendLine($"G01 X{F(mv.X)} Y{F(mv.Y)} F{(int)sp.Vorschub}");
            }

            if (curZ > z)
            {
                sb.AppendLine(Sz());
                sb.AppendLine($"G00 X{F(moves[0].X)} Y{F(moves[0].Y)}");
            }
        }

        sb.AppendLine(Sz());
        sb.AppendLine("M05");
        return sb.ToString();
    }

    private record struct PathMove(double X, double Y, bool IsArc = false,
                                   double I = 0, double J = 0, bool CW = false);

    // Ecken mit Radius runden (approximiert als Segmente, vor Offset-Berechnung)
    // verrundungen[i] = Radius an Ecke i (für Links/Rechts = Werkstückradius)
    private static List<(double x, double y)> ApplyCornerRounding(
        List<(double x, double y)> pts, List<double> verrundungen, bool closed)
    {
        int n = pts.Count;
        var result = new List<(double x, double y)>(n * 2);

        for (int i = 0; i < n; i++)
        {
            double R = i < verrundungen.Count ? verrundungen[i] : 0;

            // Offene Pfade: kein Runden an den Endpunkten
            if (!closed && (i == 0 || i == n - 1)) { result.Add(pts[i]); continue; }
            if (R < 1e-10)                          { result.Add(pts[i]); continue; }

            int prevIdx = closed ? ((i - 1 + n) % n) : i - 1;
            int nextIdx = closed ? ((i + 1) % n)     : i + 1;

            var a = pts[prevIdx];
            var b = pts[i];
            var c = pts[nextIdx];

            double ax = b.x - a.x, ay = b.y - a.y;
            double lenA = Math.Sqrt(ax * ax + ay * ay);
            double bx = c.x - b.x, by = c.y - b.y;
            double lenB = Math.Sqrt(bx * bx + by * by);
            if (lenA < 1e-10 || lenB < 1e-10) { result.Add(b); continue; }

            (double x, double y) d1 = (ax / lenA, ay / lenA);
            (double x, double y) d2 = (bx / lenB, by / lenB);

            double cross = d1.x * d2.y - d1.y * d2.x;
            if (Math.Abs(cross) < 1e-6) { result.Add(b); continue; } // kollinear

            double dot   = Math.Clamp(d1.x * d2.x + d1.y * d2.y, -1.0, 1.0);
            double theta = Math.Acos(dot);                 // Winkel zwischen den Richtungen
            double t     = R * Math.Tan(theta / 2.0);     // Rückschnitt auf dem Segment

            // Sicherstellen, dass der Bogen in den Segmenten passt
            t = Math.Min(t, lenA * 0.45);
            t = Math.Min(t, lenB * 0.45);
            double Reff = t / Math.Tan(theta / 2.0);      // effektiver Radius nach Kürzung

            double psx = b.x - t * d1.x, psy = b.y - t * d1.y; // Bogenbeginn
            double pex = b.x + t * d2.x, pey = b.y + t * d2.y; // Bogenende

            // Bogenmittelpunkt (senkrecht auf d1, zur Innenseite)
            double sgn = Math.Sign(cross); // +1 = Linkskurve (CCW), -1 = Rechtskurve (CW)
            double nx  = -d1.y * sgn, ny = d1.x * sgn;
            double cx  = psx + nx * Reff, cy = psy + ny * Reff;

            double startAngle = Math.Atan2(psy - cy, psx - cx);
            double endAngle   = Math.Atan2(pey - cy, pex - cx);

            double arcSpan;
            if (sgn > 0) { arcSpan = endAngle - startAngle; if (arcSpan < 0) arcSpan += 2 * Math.PI; }
            else         { arcSpan = startAngle - endAngle; if (arcSpan < 0) arcSpan += 2 * Math.PI; arcSpan = -arcSpan; }

            result.Add((psx, psy));

            const double chordalTol = 0.05;
            double maxStep = 2 * Math.Acos(Math.Clamp(1.0 - chordalTol / Reff, -1.0, 1.0));
            int steps = Math.Clamp((int)Math.Ceiling(Math.Abs(arcSpan) / maxStep), 1, 36);
            for (int s = 1; s <= steps; s++)
            {
                double angle = startAngle + arcSpan * s / steps;
                result.Add((cx + Reff * Math.Cos(angle), cy + Reff * Math.Sin(angle)));
            }
            // letzter hinzugefügter Punkt = pex/pey (Bogenende)
        }
        return result;
    }

    // Offset für offenen Pfad
    private static List<PathMove> ComputeOffsetMoves(
        List<(double x, double y)> pts, double r, double sign, List<double>? verrundungen = null)
    {
        int n = pts.Count;
        var dir = new (double x, double y)[n - 1];
        var nrm = new (double x, double y)[n - 1];
        for (int k = 0; k < n - 1; k++) ComputeDirNrm(pts[k], pts[k + 1], sign, out dir[k], out nrm[k]);

        var result = new List<PathMove>();
        result.Add(new PathMove(pts[0].x + nrm[0].x * r, pts[0].y + nrm[0].y * r));

        for (int i = 0; i < n - 1; i++)
        {
            double bx = pts[i + 1].x + nrm[i].x * r;
            double by = pts[i + 1].y + nrm[i].y * r;

            if (i == n - 2)
            {
                result.Add(new PathMove(bx, by));
            }
            else
            {
                double cross = dir[i].x * dir[i + 1].y - dir[i].y * dir[i + 1].x;
                double R = verrundungen != null && i + 1 < verrundungen.Count ? verrundungen[i + 1] : 0;

                if (R > 1e-10 && Math.Abs(cross) > 1e-9)
                {
                    // Verrundeter Eckpunkt: G2/G3-Bogen direkt im Offset-Pfad
                    double dot   = Math.Clamp(dir[i].x * dir[i + 1].x + dir[i].y * dir[i + 1].y, -1.0, 1.0);
                    double theta = Math.Acos(dot);
                    double lenA  = Math.Sqrt(Math.Pow(pts[i + 1].x - pts[i    ].x, 2) + Math.Pow(pts[i + 1].y - pts[i    ].y, 2));
                    double lenB  = Math.Sqrt(Math.Pow(pts[i + 2].x - pts[i + 1].x, 2) + Math.Pow(pts[i + 2].y - pts[i + 1].y, 2));
                    double t     = R * Math.Tan(theta / 2.0);
                    t = Math.Min(t, lenA * 0.45);
                    t = Math.Min(t, lenB * 0.45);
                    double Reff = t / Math.Tan(theta / 2.0);

                    // Kürzungspunkte auf dem Originalpfad
                    double psx = pts[i + 1].x - t * dir[i    ].x, psy = pts[i + 1].y - t * dir[i    ].y;
                    double pex = pts[i + 1].x + t * dir[i + 1].x, pey = pts[i + 1].y + t * dir[i + 1].y;

                    // Offset der Kürzungspunkte
                    double psxo = psx + nrm[i    ].x * r, psyo = psy + nrm[i    ].y * r;
                    double pexo = pex + nrm[i + 1].x * r, peyo = pey + nrm[i + 1].y * r;

                    // Bogenmittelpunkt (Werkstückpfad, senkrecht zur Eingangsrichtung, zur Kurveninnenseite)
                    double sgn_turn = cross > 0 ? 1.0 : -1.0;
                    double cx = psx + Reff * (-dir[i].y * sgn_turn);
                    double cy = psy + Reff * ( dir[i].x * sgn_turn);

                    // Werkzeugbahn-Bogenradius: r_off = Reff - sgn_turn * sign * r
                    double r_off = Reff - sgn_turn * sign * r;

                    if (r_off > 1e-6)
                    {
                        result.Add(new PathMove(psxo, psyo));
                        result.Add(new PathMove(pexo, peyo, IsArc: true,
                            I: cx - psxo, J: cy - psyo, CW: sgn_turn < 0));
                    }
                    else
                    {
                        // Werkzeugradius zu groß: Schnittpunkt-Fallback
                        double a0x = pts[i    ].x + nrm[i    ].x * r, a0y = pts[i    ].y + nrm[i    ].y * r;
                        double a1x = pts[i + 1].x + nrm[i + 1].x * r, a1y = pts[i + 1].y + nrm[i + 1].y * r;
                        var inter = LineIntersect((a0x, a0y), dir[i], (a1x, a1y), dir[i + 1]);
                        result.Add(inter.HasValue ? new PathMove(inter.Value.x, inter.Value.y) : new PathMove(bx, by));
                    }
                }
                else
                {
                    // Scharfe Ecke (original)
                    bool isConvex = r > 1e-10 && (sign > 0 ? cross < 0 : cross > 0) && Math.Abs(cross) > 0.01;
                    if (isConvex)
                    {
                        double ax = pts[i + 1].x + nrm[i + 1].x * r;
                        double ay = pts[i + 1].y + nrm[i + 1].y * r;
                        result.Add(new PathMove(bx, by));
                        result.Add(new PathMove(ax, ay, IsArc: true,
                            I: pts[i + 1].x - bx, J: pts[i + 1].y - by, CW: sign > 0));
                    }
                    else
                    {
                        double a0x = pts[i    ].x + nrm[i    ].x * r, a0y = pts[i    ].y + nrm[i    ].y * r;
                        double a1x = pts[i + 1].x + nrm[i + 1].x * r, a1y = pts[i + 1].y + nrm[i + 1].y * r;
                        var inter = LineIntersect((a0x, a0y), dir[i], (a1x, a1y), dir[i + 1]);
                        result.Add(inter.HasValue ? new PathMove(inter.Value.x, inter.Value.y) : new PathMove(bx, by));
                    }
                }
            }
        }
        return result;
    }

    // Offset für geschlossenen Pfad (pts: m eindeutige Punkte, kein Duplikat am Ende)
    private static List<PathMove> ComputeClosedOffsetMoves(
        List<(double x, double y)> pts, double r, double sign, List<double>? verrundungen = null)
    {
        int m = pts.Count;
        var dir = new (double x, double y)[m];
        var nrm = new (double x, double y)[m];
        for (int k = 0; k < m; k++) ComputeDirNrm(pts[k], pts[(k + 1) % m], sign, out dir[k], out nrm[k]);

        // Startpunkt = Ecke bei pts[0] (Übergang von Segment[m-1] zu Segment[0])
        double cross0 = dir[m - 1].x * dir[0].y - dir[m - 1].y * dir[0].x;
        double R0     = verrundungen != null && verrundungen.Count > 0 ? verrundungen[0] : 0;
        double a0x    = pts[0].x + nrm[0    ].x * r, a0y = pts[0].y + nrm[0    ].y * r;
        double bm_x   = pts[0].x + nrm[m - 1].x * r, bm_y = pts[0].y + nrm[m - 1].y * r;

        PathMove startPt;
        PathMove[] closingMoves;

        if (R0 > 1e-10 && Math.Abs(cross0) > 1e-9)
        {
            // Verrundete Ecke an pts[0]
            double dot0   = Math.Clamp(dir[m-1].x*dir[0].x + dir[m-1].y*dir[0].y, -1.0, 1.0);
            double theta0 = Math.Acos(dot0);
            double lenA0  = Math.Sqrt(Math.Pow(pts[0].x-pts[m-1].x,2)+Math.Pow(pts[0].y-pts[m-1].y,2));
            double lenB0  = Math.Sqrt(Math.Pow(pts[1].x-pts[0  ].x,2)+Math.Pow(pts[1].y-pts[0  ].y,2));
            double t0     = R0 / Math.Tan(theta0 / 2.0);
            t0 = Math.Min(t0, lenA0 * 0.45);
            t0 = Math.Min(t0, lenB0 * 0.45);
            double Reff0 = t0 * Math.Tan(theta0 / 2.0);

            double psx0 = pts[0].x - t0 * dir[m-1].x, psy0 = pts[0].y - t0 * dir[m-1].y; // Kürzung Eingang
            double pex0 = pts[0].x + t0 * dir[0    ].x, pey0 = pts[0].y + t0 * dir[0  ].y; // Kürzung Ausgang

            double psxo0 = psx0 + nrm[m-1].x * r, psyo0 = psy0 + nrm[m-1].y * r;
            double pexo0 = pex0 + nrm[0    ].x * r, peyo0 = pey0 + nrm[0  ].y * r;

            double sgn0  = cross0 > 0 ? 1.0 : -1.0;
            double cxw0  = psx0 + Reff0 * (-dir[m-1].y * sgn0);
            double cyw0  = psy0 + Reff0 * ( dir[m-1].x * sgn0);
            double roff0 = Reff0 - sgn0 * sign * r;

            startPt = new PathMove(pexo0, peyo0);
            if (roff0 > 1e-6)
            {
                closingMoves = [
                    new PathMove(psxo0, psyo0),
                    new PathMove(pexo0, peyo0, IsArc: true,
                        I: cxw0 - psxo0, J: cyw0 - psyo0, CW: sgn0 < 0)
                ];
            }
            else
            {
                double sx = pts[m-1].x + nrm[m-1].x * r, sy = pts[m-1].y + nrm[m-1].y * r;
                var int0 = LineIntersect((sx, sy), dir[m-1], (a0x, a0y), dir[0]);
                startPt = int0.HasValue ? new PathMove(int0.Value.x, int0.Value.y) : new PathMove(a0x, a0y);
                closingMoves = [startPt];
            }
        }
        else
        {
            // Scharfe Ecke an pts[0]
            bool conv0 = r > 1e-10 && (sign > 0 ? cross0 < 0 : cross0 > 0) && Math.Abs(cross0) > 0.01;
            if (conv0)
            {
                startPt = new PathMove(a0x, a0y);
                closingMoves = [
                    new PathMove(bm_x, bm_y),
                    new PathMove(a0x, a0y, IsArc: true,
                        I: pts[0].x - bm_x, J: pts[0].y - bm_y, CW: sign > 0)
                ];
            }
            else
            {
                double sx = pts[m - 1].x + nrm[m - 1].x * r, sy = pts[m - 1].y + nrm[m - 1].y * r;
                var inter = LineIntersect((sx, sy), dir[m - 1], (a0x, a0y), dir[0]);
                startPt = inter.HasValue ? new PathMove(inter.Value.x, inter.Value.y) : new PathMove(a0x, a0y);
                closingMoves = [startPt];
            }
        }

        var result = new List<PathMove> { startPt };

        // Ecken bei pts[1..m-1]
        for (int i = 1; i < m; i++)
        {
            int prev   = i - 1;
            int nextSeg = i % m; // Richtungsindex des ausgehenden Segments
            double bx  = pts[i].x + nrm[prev   ].x * r, by = pts[i].y + nrm[prev   ].y * r;
            double ax  = pts[i].x + nrm[nextSeg ].x * r, ay = pts[i].y + nrm[nextSeg].y * r;
            double cr  = dir[prev].x * dir[nextSeg].y - dir[prev].y * dir[nextSeg].x;
            double R   = verrundungen != null && i < verrundungen.Count ? verrundungen[i] : 0;

            if (R > 1e-10 && Math.Abs(cr) > 1e-9)
            {
                double dot   = Math.Clamp(dir[prev].x*dir[nextSeg].x + dir[prev].y*dir[nextSeg].y, -1.0, 1.0);
                double theta = Math.Acos(dot);
                int    ni    = (i + 1) % m;
                double lenA  = Math.Sqrt(Math.Pow(pts[i].x-pts[prev].x,2)+Math.Pow(pts[i].y-pts[prev].y,2));
                double lenB  = Math.Sqrt(Math.Pow(pts[ni ].x-pts[i  ].x,2)+Math.Pow(pts[ni ].y-pts[i  ].y,2));
                double t     = R * Math.Tan(theta / 2.0);
                t = Math.Min(t, lenA * 0.45);
                t = Math.Min(t, lenB * 0.45);
                double Reff = t / Math.Tan(theta / 2.0);

                double psx = pts[i].x - t * dir[prev   ].x, psy = pts[i].y - t * dir[prev   ].y;
                double pex = pts[i].x + t * dir[nextSeg].x, pey = pts[i].y + t * dir[nextSeg].y;

                double psxo = psx + nrm[prev   ].x * r, psyo = psy + nrm[prev   ].y * r;
                double pexo = pex + nrm[nextSeg ].x * r, peyo = pey + nrm[nextSeg].y * r;

                double sgn_turn = cr > 0 ? 1.0 : -1.0;
                double cx  = psx + Reff * (-dir[prev].y * sgn_turn);
                double cy  = psy + Reff * ( dir[prev].x * sgn_turn);
                double roff = Reff - sgn_turn * sign * r;

                if (roff > 1e-6)
                {
                    result.Add(new PathMove(psxo, psyo));
                    result.Add(new PathMove(pexo, peyo, IsArc: true,
                        I: cx - psxo, J: cy - psyo, CW: sgn_turn < 0));
                }
                else
                {
                    double p0x = pts[prev].x + nrm[prev   ].x * r, p0y = pts[prev].y + nrm[prev   ].y * r;
                    var inter = LineIntersect((p0x, p0y), dir[prev], (ax, ay), dir[nextSeg]);
                    result.Add(inter.HasValue ? new PathMove(inter.Value.x, inter.Value.y) : new PathMove(bx, by));
                }
            }
            else
            {
                // Scharfe Ecke (original)
                bool conv = r > 1e-10 && (sign > 0 ? cr < 0 : cr > 0) && Math.Abs(cr) > 0.01;
                if (conv)
                {
                    result.Add(new PathMove(bx, by));
                    result.Add(new PathMove(ax, ay, IsArc: true,
                        I: pts[i].x - bx, J: pts[i].y - by, CW: sign > 0));
                }
                else
                {
                    double p0x = pts[prev].x + nrm[prev   ].x * r, p0y = pts[prev].y + nrm[prev   ].y * r;
                    var inter = LineIntersect((p0x, p0y), dir[prev], (ax, ay), dir[nextSeg]);
                    result.Add(inter.HasValue ? new PathMove(inter.Value.x, inter.Value.y) : new PathMove(bx, by));
                }
            }
        }

        result.AddRange(closingMoves);
        return result;
    }

    private static void ComputeDirNrm(
        (double x, double y) a, (double x, double y) b, double sign,
        out (double x, double y) dir, out (double x, double y) nrm)
    {
        double dx = b.x - a.x, dy = b.y - a.y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-10) { dir = (1, 0); nrm = (0, sign); return; }
        dir = (dx / len, dy / len);
        nrm = (-dy / len * sign, dx / len * sign);
    }

    private static (double x, double y)? LineIntersect(
        (double x, double y) a, (double x, double y) da,
        (double x, double y) b, (double x, double y) db)
    {
        double denom = da.x * db.y - da.y * db.x;
        if (Math.Abs(denom) < 1e-10) return null;
        double t = ((b.x - a.x) * db.y - (b.y - a.y) * db.x) / denom;
        return (a.x + t * da.x, a.y + t * da.y);
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
