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
        sb.AppendLine($"(TOOL D={F(p.FraeserD)} ANGLE=180)");
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
        sb.AppendLine($"M03 S{(int)p.Drehzahl}");
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
        sb.AppendLine($"M03 S{(int)p.Drehzahl}");
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
        sb.AppendLine($"(TOOL D={F(p.FraeserD)} ANGLE=180)");

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

        // Eintauchrampe: von prevZ diagonal auf curZ absenken (entlang X-Achse der Tasche)
        void AppendEntry(double prevZ, double nextZ)
        {
            double dz    = Math.Abs(nextZ - prevZ);
            double angle = p.Eintauchwinkel;
            if (angle >= 90 || angle <= 0 || dz < 1e-6)
            {
                sb.AppendLine($"G01 Z{F(nextZ)} F{(int)p.VorschubFz}");
                return;
            }
            double rampLen = (dz / 2.0) / Math.Tan(angle * Math.PI / 180.0);
            if (ix0 + rampLen > ix1)
            {
                sb.AppendLine($"G01 Z{F(nextZ)} F{(int)p.VorschubFz}");
                return;
            }
            double midZ = prevZ - dz / 2.0;
            sb.AppendLine($"G01 X{F(ix0 + rampLen)} Z{F(midZ)} F{(int)p.VorschubFz}");
            sb.AppendLine($"G01 X{F(ix0)} Z{F(nextZ)} F{(int)p.VorschubFz}");
        }

        double lastToolX = zx0;
        double lastToolY = zy0;

        while (curZ > depth)
        {
            double prevDepth = curZ;
            curZ = Math.Max(depth, curZ - zStep);

            // 1. G00 zur Taschenecke (Materialkante) in XY
            sb.AppendLine($"G00 X{F(ix0)} Y{F(iy0)}");
            // 2. G00 runter auf vorherige Räumebene (Z=0 beim ersten Pass)
            sb.AppendLine($"G00 Z{F(prevDepth)}");
            // 3. Eintauchrampe diagonal auf neue Tiefe
            AppendEntry(prevDepth, curZ);
            // 4. auf Räumstartpunkt vorfahren
            sb.AppendLine($"G01 X{F(zx0)} Y{F(zy0)} F{(int)p.Vorschub}");

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
            lastToolX = rightward ? zx1 : zx0;
            lastToolY = zy1;

            if (curZ > depth)
                sb.AppendLine(Sz());
        }

        // Schlichten: nächstgelegene Ecke direkt anfahren (kein Austauchen), Kontur im Uhrzeigersinn
        (double x, double y)[] corners = [(ix0, iy0), (ix1, iy0), (ix1, iy1), (ix0, iy1)];
        int startCorner = 0;
        double minDist  = double.MaxValue;
        for (int i = 0; i < 4; i++)
        {
            double dx = corners[i].x - lastToolX;
            double dy = corners[i].y - lastToolY;
            double d  = dx * dx + dy * dy;
            if (d < minDist) { minDist = d; startCorner = i; }
        }
        sb.AppendLine($"G01 X{F(corners[startCorner].x)} Y{F(corners[startCorner].y)} F{(int)p.Vorschub}");
        for (int i = 1; i <= 4; i++)
        {
            var c = corners[(startCorner + i) % 4];
            sb.AppendLine($"G01 X{F(c.x)} Y{F(c.y)} F{(int)p.Vorschub}");
        }

        sb.AppendLine(Sz());
        sb.AppendLine("M05");
        return sb.ToString();
    }

    public static string Nut(NutParams p, double workW, double workH)
    {
        double r    = p.FraeserD / 2.0;
        double step = Math.Max(0.1, p.FraeserD * p.Faktor);

        var (refX, refY) = ConvertBezugspunkt(p.Bezugspunkt, p.XRel, p.YRel, workW, workH);

        // Offset auf untere-linke Nutecke umrechnen
        var (bx, by) = p.Bezugspunkt switch
        {
            "Unten links"  => (0,              0),
            "Unten Mitte"  => (-p.Länge / 2,   0),
            "Unten rechts" => (-p.Länge,        0),
            "Links Mitte"  => (0,              -p.Breite / 2),
            "Mitte"        => (-p.Länge / 2,   -p.Breite / 2),
            "Rechts Mitte" => (-p.Länge,       -p.Breite / 2),
            "Oben links"   => (0,              -p.Breite),
            "Oben Mitte"   => (-p.Länge / 2,   -p.Breite),
            "Oben rechts"  => (-p.Länge,       -p.Breite),
            _              => (0.0,             0.0)
        };

        double ax = refX + bx;
        double ay = refY + by;

        // Fräsermittelpunkt-Bereich – kein Aufmaß, direkt an der Endkontur
        double ix0 = ax + r;
        double iy0 = ay + r;
        double ix1 = ax + p.Länge  - r;
        double iy1 = ay + p.Breite - r;

        var sb = new StringBuilder();
        sb.AppendLine("(Nut fräsen)");
        sb.AppendLine($"(X={F(ax)} Y={F(ay)} L={F(p.Länge)} B={F(p.Breite)})");
        sb.AppendLine($"(D={p.FraeserD}, Bezug={p.Bezugspunkt})");
        sb.AppendLine($"(TOOL D={F(p.FraeserD)} ANGLE=180)");

        if (ix1 < ix0 - 1e-6 || iy1 < iy0 - 1e-6)
        {
            sb.AppendLine("(Nut zu schmal für Werkzeug)");
            sb.AppendLine("M05");
            return sb.ToString();
        }

        sb.AppendLine($"M03 S{p.Drehzahl}");
        sb.AppendLine(Sz());

        double depth = -Math.Abs(p.ZTiefe);
        double zStep = Math.Abs(p.ZZustellung);
        double curZ  = 0;

        void AppendEntry(double prevZ, double nextZ)
        {
            double dz    = Math.Abs(nextZ - prevZ);
            double angle = p.Eintauchwinkel;
            if (angle >= 90 || angle <= 0 || dz < 1e-6)
            {
                sb.AppendLine($"G01 Z{F(nextZ)} F{(int)p.VorschubFz}");
                return;
            }
            double rampLen = (dz / 2.0) / Math.Tan(angle * Math.PI / 180.0);
            if (ix0 + rampLen > ix1)
            {
                sb.AppendLine($"G01 Z{F(nextZ)} F{(int)p.VorschubFz}");
                return;
            }
            double midZ = prevZ - dz / 2.0;
            sb.AppendLine($"G01 X{F(ix0 + rampLen)} Z{F(midZ)} F{(int)p.VorschubFz}");
            sb.AppendLine($"G01 X{F(ix0)} Z{F(nextZ)} F{(int)p.VorschubFz}");
        }

        while (curZ > depth)
        {
            double prevDepth = curZ;
            curZ = Math.Max(depth, curZ - zStep);

            sb.AppendLine($"G00 X{F(ix0)} Y{F(iy0)}");
            sb.AppendLine($"G00 Z{F(prevDepth)}");
            AppendEntry(prevDepth, curZ);

            double y = iy0;
            bool rightward = true;
            while (true)
            {
                sb.AppendLine(rightward
                    ? $"G01 X{F(ix1)} F{(int)p.Vorschub}"
                    : $"G01 X{F(ix0)} F{(int)p.Vorschub}");
                if (y >= iy1 - 1e-6) break;
                y = Math.Min(y + step, iy1);
                sb.AppendLine($"G01 Y{F(y)} F{(int)p.Vorschub}");
                rightward = !rightward;
            }

            if (curZ > depth)
                sb.AppendLine(Sz());
        }

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
        sb.AppendLine($"(TOOL D={F(p.FraeserD)} ANGLE=180)");

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
        sb.AppendLine($"(TOOL D={F(p.Diameter)} ANGLE=180)");
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

        // Kontur-Moves in eine Liste puffern, damit sie in jedem Z-Schritt wiederverwendet werden.
        var contour = new List<string>();
        string L(double x, double y) => $"G01 X{F(x)} Y{F(y)} F{(int)p.VorschubFxy}";
        string A3(double x, double y, double i, double j) => $"G03 X{F(x)} Y{F(y)} I{F(i)} J{F(j)} F{(int)p.VorschubFxy}";

        if (r <= 0)
        {
            if (startSide == "oben")
            {
                if (gegenlauf) { contour.Add(L(x0,y1)); contour.Add(L(x0,y0)); contour.Add(L(x1,y0)); contour.Add(L(x1,y1)); }
                else           { contour.Add(L(x1,y1)); contour.Add(L(x1,y0)); contour.Add(L(x0,y0)); contour.Add(L(x0,y1)); }
                contour.Add(L(startX, startY));
            }
            else if (startSide == "rechts")
            {
                if (gegenlauf) { contour.Add(L(x1,y1)); contour.Add(L(x0,y1)); contour.Add(L(x0,y0)); contour.Add(L(x1,y0)); }
                else           { contour.Add(L(x1,y0)); contour.Add(L(x0,y0)); contour.Add(L(x0,y1)); contour.Add(L(x1,y1)); }
                contour.Add(L(startX, startY));
            }
            else if (startSide == "links")
            {
                if (gegenlauf) { contour.Add(L(x0,y0)); contour.Add(L(x1,y0)); contour.Add(L(x1,y1)); contour.Add(L(x0,y1)); }
                else           { contour.Add(L(x0,y1)); contour.Add(L(x1,y1)); contour.Add(L(x1,y0)); contour.Add(L(x0,y0)); }
                contour.Add(L(startX, startY));
            }
            else
            {
                if (gegenlauf) { contour.Add(L(x1,y0)); contour.Add(L(x1,y1)); contour.Add(L(x0,y1)); contour.Add(L(x0,y0)); }
                else           { contour.Add(L(x0,y0)); contour.Add(L(x0,y1)); contour.Add(L(x1,y1)); contour.Add(L(x1,y0)); }
                contour.Add(L(startX, startY));
            }
        }
        else
        {
            if (startSide == "oben")
            {
                contour.Add(L(x0+r,y1)); contour.Add(A3(x0,y1-r,0,-r)); contour.Add(L(x0,y0+r));
                contour.Add(A3(x0+r,y0,r,0)); contour.Add(L(x1-r,y0)); contour.Add(A3(x1,y0+r,0,r));
                contour.Add(L(x1,y1-r)); contour.Add(A3(x1-r,y1,-r,0)); contour.Add(L(startX,startY));
            }
            else if (startSide == "rechts")
            {
                contour.Add(L(x1,y1-r)); contour.Add(A3(x1-r,y1,-r,0)); contour.Add(L(x0+r,y1));
                contour.Add(A3(x0,y1-r,0,-r)); contour.Add(L(x0,y0+r)); contour.Add(A3(x0+r,y0,r,0));
                contour.Add(L(x1-r,y0)); contour.Add(A3(x1,y0+r,0,r)); contour.Add(L(startX,startY));
            }
            else if (startSide == "links")
            {
                contour.Add(L(x0,y0+r)); contour.Add(A3(x0+r,y0,r,0)); contour.Add(L(x1-r,y0));
                contour.Add(A3(x1,y0+r,0,r)); contour.Add(L(x1,y1-r)); contour.Add(A3(x1-r,y1,-r,0));
                contour.Add(L(x0+r,y1)); contour.Add(A3(x0,y1-r,0,-r)); contour.Add(L(startX,startY));
            }
            else
            {
                contour.Add(L(x1-r,y0)); contour.Add(A3(x1,y0+r,0,r)); contour.Add(L(x1,y1-r));
                contour.Add(A3(x1-r,y1,-r,0)); contour.Add(L(x0+r,y1)); contour.Add(A3(x0,y1-r,0,-r));
                contour.Add(L(x0,y0+r)); contour.Add(A3(x0+r,y0,r,0)); contour.Add(L(startX,startY));
            }
        }

        // Z-Tiefen bestimmen: bei Mehrfachzustellung schrittweise bis zur Endtiefe
        var zLevels = new List<double>();
        if (p.MehrfachZustellung && p.ZZustellung > 0 && z < 0)
        {
            double step = p.ZZustellung;
            for (double cz = -step; cz > z; cz -= step)
                zLevels.Add(Math.Round(cz, 4));
        }
        zLevels.Add(z);

        sb.AppendLine($"G00 X{F(approachX)} Y{F(approachY)}");
        for (int pass = 0; pass < zLevels.Count; pass++)
        {
            sb.AppendLine($"G01 Z{F(zLevels[pass])} F{(int)p.VorschubFz}");
            sb.AppendLine($"{entryArcCmd} X{F(startX)} Y{F(startY)} I{F(arcI)} J{F(arcJ)} F{(int)p.VorschubFxy}");
            foreach (var line in contour) sb.AppendLine(line);
            sb.AppendLine($"{exitArcCmd} X{F(exitX)} Y{F(exitY)} I{F(exitI)} J{F(exitJ)} F{(int)p.VorschubFxy}");
            if (pass < zLevels.Count - 1)
                sb.AppendLine($"G00 X{F(approachX)} Y{F(approachY)}");
        }
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

        // Build absolute endpoint coords + arc midpoints (arcMids[i] = midpoint for segment i-1→i)
        var pts     = new List<(double x, double y)>();
        var arcMids = new List<(double mx, double my)?>();
        for (int i = 0; i < path.Count; i++)
        {
            var p = path[i];
            (double x, double y) pt;
            if (p.Bezugspunkt == "Letzter Punkt" && pts.Count > 0)
                pt = (pts[^1].x + p.XRel, pts[^1].y + p.YRel);
            else
                pt = ConvertBezugspunkt(p.Bezugspunkt, p.XRel, p.YRel, workW, workH);
            pts.Add(pt);

            if (i > 0 && p.Typ == PfadPunktTyp.Bogen)
            {
                arcMids.Add(ResolveBogenMid(pts[i - 1], pts[i], p, workW, workH));
            }
            else
            {
                arcMids.Add(null);
            }
        }

        bool hasBogen = arcMids.Skip(1).Any(m => m.HasValue);

        // Geschlossener Pfad: erster und letzter Punkt identisch (< 0.01 mm)
        bool closed = pts.Count >= 3 &&
            Math.Sqrt(Math.Pow(pts[0].x - pts[^1].x, 2) + Math.Pow(pts[0].y - pts[^1].y, 2)) < 0.01;

        List<PathMove> moves;
        if (hasBogen)
        {
            moves = BuildBogenMoves(pts, arcMids, corr ? r : 0, corr ? sg : 0, closed);
            if (closed && moves.Count > 0)
                moves.Add(moves[0]);
        }
        else
        {
            // Unique points (no duplicate endpoint for closed path)
            var uniquePts = closed ? pts.Take(pts.Count - 1).ToList() : pts;

            // Corner rounding: Startpunkt-R als globaler Fallback, Einzelpunkte können überschreiben
            double globalR = sp.Verrundung;
            var verrundungen = path.Select(p => p.Verrundung > 1e-10 ? p.Verrundung : globalR).ToList();
            bool hasRounding = verrundungen.Any(v => v > 1e-10);

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
        }

        var sb = new StringBuilder();
        sb.AppendLine("(Pfad Fräsen)");
        sb.AppendLine($"(TOOL D={F(sp.FraeserD)} ANGLE=180)");
        sb.AppendLine($"M03 S{(int)sp.Drehzahl}");
        sb.AppendLine(Sz());
        sb.AppendLine($"G00 X{F(moves[0].X)} Y{F(moves[0].Y)}");

        double zStep = Math.Abs(sp.ZZustellung);
        double curZ  = 0;

        void AppendEntry(double prevZ, double nextZ)
        {
            double dz    = Math.Abs(nextZ - prevZ);
            double angle = sp.Eintauchwinkel;
            if (moves.Count >= 2 && !moves[1].IsArc && angle > 0 && angle < 90 && dz > 1e-6)
            {
                double dx      = moves[1].X - moves[0].X;
                double dy      = moves[1].Y - moves[0].Y;
                double segLen  = Math.Sqrt(dx * dx + dy * dy);
                double rampLen = (dz / 2.0) / Math.Tan(angle * Math.PI / 180.0);
                if (segLen >= rampLen * 2 && segLen > 1e-9)
                {
                    double ux = dx / segLen, uy = dy / segLen;
                    double midZ = prevZ - dz / 2.0;
                    sb.AppendLine($"G01 X{F(moves[0].X + ux * rampLen)} Y{F(moves[0].Y + uy * rampLen)} Z{F(midZ)} F{(int)sp.VorschubFz}");
                    sb.AppendLine($"G01 X{F(moves[0].X)} Y{F(moves[0].Y)} Z{F(nextZ)} F{(int)sp.VorschubFz}");
                    return;
                }
            }
            sb.AppendLine($"G01 Z{F(nextZ)} F{(int)sp.VorschubFz}");
        }

        while (curZ > z)
        {
            double prevDepth = curZ;
            curZ = Math.Max(z, curZ - zStep);
            sb.AppendLine($"G00 Z{F(prevDepth)}");
            AppendEntry(prevDepth, curZ);

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

    // Kreismittelpunkt und Richtung aus drei Punkten berechnen
    private static (double cx, double cy, double R, bool cw) ArcFrom3Points(
        double x1, double y1, double xm, double ym, double x2, double y2)
    {
        // Senkrechte Halbierende von P1-Pm und Pm-P2 → Schnittpunkt = Mittelpunkt
        double ax = (x1 + xm) / 2, ay = (y1 + ym) / 2;
        double dax = ym - y1, day = x1 - xm;

        double bx = (xm + x2) / 2, by = (ym + y2) / 2;
        double dbx = y2 - ym, dby = xm - x2;

        double det = dax * (-dby) + dbx * day;
        if (Math.Abs(det) < 1e-12)
            return (0, 0, double.PositiveInfinity, false);   // Punkte kollinear → Gerade

        double t  = ((bx - ax) * (-dby) + dbx * (by - ay)) / det;
        double cx = ax + t * dax;
        double cy = ay + t * day;
        double R  = Math.Sqrt((x1 - cx) * (x1 - cx) + (y1 - cy) * (y1 - cy));

        // Kreuzprodukt (P1→Pm) × (P1→P2): negativ = im Uhrzeigersinn (G02)
        double cross = (xm - x1) * (y2 - y1) - (ym - y1) * (x2 - x1);
        return (cx, cy, R, cross < 0);
    }

    // Moves für Pfad mit Bogen-Segmenten, optional mit Werkzeug-Radiuskorrektur.
    // r = Werkzeugradius, sg = +1 (Links) oder -1 (Rechts); r=0 → kein Offset.
    // Konvexe Ecken erhalten einen Umfahrbogen (Radius r um den Eckpunkt).
    // Bei geschlossenem Pfad wird auch die Naht als Ecke behandelt.
    private static List<PathMove> BuildBogenMoves(
        List<(double x, double y)> pts,
        List<(double mx, double my)?> arcMids,
        double r = 0, double sg = 0, bool closed = false)
    {
        int n = pts.Count;

        if (!(r > 1e-10 && Math.Abs(sg) > 1e-10))
        {
            // Kein Offset: Originalverhalten
            var moves0 = new List<PathMove> { new PathMove(pts[0].x, pts[0].y) };
            for (int i = 1; i < n; i++)
            {
                var mid0 = arcMids[i];
                if (mid0.HasValue)
                {
                    var (x1, y1) = pts[i - 1];
                    var (mx, my) = mid0.Value;
                    var (x2, y2) = pts[i];
                    var (cx, cy, rr, cw) = ArcFrom3Points(x1, y1, mx, my, x2, y2);
                    if (double.IsInfinity(rr))
                        moves0.Add(new PathMove(x2, y2));
                    else
                        moves0.Add(new PathMove(x2, y2, IsArc: true, I: cx - x1, J: cy - y1, CW: cw));
                }
                else
                {
                    moves0.Add(new PathMove(pts[i].x, pts[i].y));
                }
            }
            return moves0;
        }

        // --- Mit Radiuskorrektur ---
        int m = n - 1; // Anzahl Segmente
        var segArc  = new bool[m];
        var segCxA  = new double[m];
        var segCyA  = new double[m];
        var segRNew = new double[m];
        var segCW   = new bool[m];
        var segDir  = new (double x, double y)[m];
        var segNrm  = new (double x, double y)[m];
        var segS    = new (double x, double y)[m]; // versetzter Startpunkt
        var segE    = new (double x, double y)[m]; // versetzter Endpunkt

        for (int s = 0; s < m; s++)
        {
            var p1 = pts[s]; var p2 = pts[s + 1];
            var mid = arcMids[s + 1];
            bool isArcSeg = false;
            if (mid.HasValue)
            {
                var (cx, cy, R, cw) = ArcFrom3Points(p1.x, p1.y, mid.Value.mx, mid.Value.my, p2.x, p2.y);
                if (!double.IsInfinity(R))
                {
                    // CW-Bogen: links = außen → R_new = R + sg*r; CCW: R_new = R - sg*r
                    double rNew = Math.Max(0, R + (cw ? 1.0 : -1.0) * sg * r);
                    segArc[s] = true; segCxA[s] = cx; segCyA[s] = cy; segRNew[s] = rNew; segCW[s] = cw;
                    isArcSeg = true;
                    double d1 = Math.Sqrt(Math.Pow(p1.x - cx, 2) + Math.Pow(p1.y - cy, 2));
                    double d2 = Math.Sqrt(Math.Pow(p2.x - cx, 2) + Math.Pow(p2.y - cy, 2));
                    segS[s] = d1 > 1e-12 ? (cx + (p1.x - cx) / d1 * rNew, cy + (p1.y - cy) / d1 * rNew) : p1;
                    segE[s] = d2 > 1e-12 ? (cx + (p2.x - cx) / d2 * rNew, cy + (p2.y - cy) / d2 * rNew) : p2;
                }
            }
            if (!isArcSeg)
            {
                ComputeDirNrm(p1, p2, sg, out segDir[s], out segNrm[s]);
                segS[s] = (p1.x + segNrm[s].x * r, p1.y + segNrm[s].y * r);
                segE[s] = (p2.x + segNrm[s].x * r, p2.y + segNrm[s].y * r);
            }
        }

        // Tangentenrichtung am Segmentende/-anfang (für Eckenerkennung)
        (double x, double y) SegEndTan(int s) =>
            segArc[s] ? BogenArcTangentAt(segCxA[s], segCyA[s], segE[s].x, segE[s].y, segCW[s]) : segDir[s];
        (double x, double y) SegStartTan(int s) =>
            segArc[s] ? BogenArcTangentAt(segCxA[s], segCyA[s], segS[s].x, segS[s].y, segCW[s]) : segDir[s];

        // effStart/effEnd: defaults sind segS/segE; bei konkaven Ecken → Schnittpunkt
        var effStart  = new (double x, double y)[m];
        var effEnd    = new (double x, double y)[m];
        var vtxConvex = new bool[n];
        for (int s = 0; s < m; s++) { effStart[s] = segS[s]; effEnd[s] = segE[s]; }

        void ProcessVertex(int vIdx, int prevSeg, int nextSeg, (double x, double y) vtxPt)
        {
            var tanOut = SegEndTan(prevSeg);
            var tanIn  = SegStartTan(nextSeg);
            double cross = tanOut.x * tanIn.y - tanOut.y * tanIn.x;
            bool isConvex = Math.Abs(cross) > 0.01 && (sg > 0 ? cross < 0 : cross > 0);
            vtxConvex[vIdx] = isConvex;
            if (!isConvex)
            {
                // Konkave Ecke oder Gerade: Schnittpunkt der versetzten Segmente
                (double x, double y) conn;
                if (!segArc[prevSeg] && !segArc[nextSeg])
                    conn = LineIntersect(segS[prevSeg], segDir[prevSeg], segS[nextSeg], segDir[nextSeg]) ?? segE[prevSeg];
                else if (!segArc[prevSeg])
                    conn = BogenPickClosest(BogenLineCircleIntersect(segS[prevSeg], segDir[prevSeg], segCxA[nextSeg], segCyA[nextSeg], segRNew[nextSeg]), vtxPt, segE[prevSeg]);
                else if (!segArc[nextSeg])
                    conn = BogenPickClosest(BogenLineCircleIntersect(segS[nextSeg], segDir[nextSeg], segCxA[prevSeg], segCyA[prevSeg], segRNew[prevSeg]), vtxPt, segE[prevSeg]);
                else
                    conn = BogenPickClosest(BogenCircleCircleIntersect(segCxA[prevSeg], segCyA[prevSeg], segRNew[prevSeg], segCxA[nextSeg], segCyA[nextSeg], segRNew[nextSeg]), vtxPt, segE[prevSeg]);
                effEnd[prevSeg]   = conn;
                effStart[nextSeg] = conn;
            }
            // Konvexe Ecke: effEnd[prevSeg]=segE[prevSeg], effStart[nextSeg]=segS[nextSeg] (defaults ok)
        }

        // Interne Ecken verarbeiten
        for (int i = 1; i < n - 1; i++)
            ProcessVertex(i, i - 1, i, pts[i]);

        // Naht bei geschlossenem Pfad als Ecke behandeln
        if (closed && m >= 2)
            ProcessVertex(0, m - 1, 0, pts[0]);

        // Ergebnisliste aufbauen
        var result = new List<PathMove>();
        result.Add(new PathMove(effStart[0].x, effStart[0].y));

        for (int s = 0; s < m; s++)
        {
            var sp = effStart[s];
            var ep = effEnd[s];
            if (segArc[s] && segRNew[s] > 1e-10)
                result.Add(new PathMove(ep.x, ep.y, IsArc: true,
                    I: segCxA[s] - sp.x, J: segCyA[s] - sp.y, CW: segCW[s]));
            else
                result.Add(new PathMove(ep.x, ep.y));

            // Konvexer Umfahrbogen nach Segment s
            int nextS = (s + 1) % m;
            int vIdx  = (s == m - 1 && closed) ? 0 : s + 1;
            if (vIdx < n && vtxConvex[vIdx])
            {
                var cornerPt = pts[vIdx];
                result.Add(new PathMove(effStart[nextS].x, effStart[nextS].y, IsArc: true,
                    I: cornerPt.x - ep.x, J: cornerPt.y - ep.y, CW: sg > 0));
            }
        }

        return result;
    }

    private static (double x, double y) BogenPickClosest(
        IEnumerable<(double x, double y)> cands, (double x, double y) hint, (double x, double y) fallback)
    {
        var best = fallback;
        double bestD = double.MaxValue;
        foreach (var c in cands)
        {
            double d = Math.Pow(c.x - hint.x, 2) + Math.Pow(c.y - hint.y, 2);
            if (d < bestD) { bestD = d; best = c; }
        }
        return best;
    }

    private static IEnumerable<(double x, double y)> BogenLineCircleIntersect(
        (double x, double y) o, (double x, double y) d, double cx, double cy, double R)
    {
        double fx = o.x - cx, fy = o.y - cy;
        double a = d.x * d.x + d.y * d.y;
        if (a < 1e-20) yield break;
        double b = 2 * (fx * d.x + fy * d.y);
        double c = fx * fx + fy * fy - R * R;
        double disc = b * b - 4 * a * c;
        if (disc < 0) yield break;
        double sq = Math.Sqrt(disc);
        double t1 = (-b - sq) / (2 * a), t2 = (-b + sq) / (2 * a);
        yield return (o.x + t1 * d.x, o.y + t1 * d.y);
        if (Math.Abs(t2 - t1) > 1e-10)
            yield return (o.x + t2 * d.x, o.y + t2 * d.y);
    }

    private static IEnumerable<(double x, double y)> BogenCircleCircleIntersect(
        double cx1, double cy1, double r1, double cx2, double cy2, double r2)
    {
        double dx = cx2 - cx1, dy = cy2 - cy1, d = Math.Sqrt(dx * dx + dy * dy);
        if (d < 1e-10 || d > r1 + r2 + 1e-6 || d < Math.Abs(r1 - r2) - 1e-6) yield break;
        double a = (r1 * r1 - r2 * r2 + d * d) / (2 * d);
        double h2 = r1 * r1 - a * a;
        if (h2 < 0) yield break;
        double h = Math.Sqrt(h2), mx = cx1 + a * dx / d, my = cy1 + a * dy / d;
        yield return (mx + h * dy / d, my - h * dx / d);
        if (h > 1e-10) yield return (mx - h * dy / d, my + h * dx / d);
    }

    // Tangente eines Kreisbogens am Punkt (px,py) mit Zentrum (cx,cy)
    private static (double x, double y) BogenArcTangentAt(double cx, double cy, double px, double py, bool cw)
    {
        double dx = px - cx, dy = py - cy;
        double R = Math.Sqrt(dx * dx + dy * dy);
        if (R < 1e-12) return (1, 0);
        return cw ? (dy / R, -dx / R) : (-dy / R, dx / R);
    }

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

    public static (double x, double y) ConvertBezugspunkt(string ref_, double xRel, double yRel, double w, double h)
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

    // ── Bogen-Mittelpunkt aus PfadPunktParams berechnen ─────────────────

    private static (double mx, double my) ResolveBogenMid(
        (double x, double y) p1, (double x, double y) p2,
        PfadPunktParams p, double workW, double workH)
    {
        string modus = p.BogenModus ?? "Bogenmitte";

        if (modus == "Bogenmitte")
        {
            if (p.Bezugspunkt == "Letzter Punkt")
                return (p1.x + p.XMid, p1.y + p.YMid);
            return ConvertBezugspunkt(p.Bezugspunkt, p.XMid, p.YMid, workW, workH);
        }

        // Geometrie: Sehnenrichtung und linke Senkrechte
        double dx = p2.x - p1.x, dy = p2.y - p1.y;
        double L  = Math.Sqrt(dx * dx + dy * dy);
        if (L < 1e-10) return ((p1.x + p2.x) / 2, (p1.y + p2.y) / 2);

        double perpX = -dy / L, perpY = dx / L; // links der Fahrtrichtung
        double mcx   = (p1.x + p2.x) / 2, mcy = (p1.y + p2.y) / 2;

        double h; // vorzeichenbehaftete Pfeilhöhe (+ = links)
        if (modus == "Radius")
        {
            double R    = p.XMid;
            double a    = L / 2;
            double absR = Math.Max(Math.Abs(R), a); // Mindestradius = Sehnenhälfte
            h = (absR - Math.Sqrt(Math.Max(0, absR * absR - a * a))) * (R >= 0 ? 1 : -1);
        }
        else // "Pfeilhöhe"
        {
            h = p.XMid;
        }

        return (mcx + h * perpX, mcy + h * perpY);
    }

    // ── Gravieren ────────────────────────────────────────────────────────

    public static string Gravieren(GraviereParams p, double workW, double workH)
    {
        var sb  = new StringBuilder();
        var ctx = BuildTextGeo(p, workW, workH);

        double scale  = ctx.Scale;
        double multiH = ctx.MultiH;
        var flat2     = ctx.FlatDisplay;
        double zDepth = -Math.Abs(p.ZTiefe);

        double MX(double wx) => ctx.Ox + wx * scale;
        double MY(double wy) => ctx.Oy + ctx.YOffset + (multiH - wy) * scale;

        double halfRad        = Math.Min(p.SchneidenWinkel, 179.9) / 2.0 * Math.PI / 180.0;
        double vWidth         = 2.0 * Math.Abs(p.ZTiefe) * Math.Tan(halfRad);
        double effectiveWidth = (p.FraeserD > 0) ? Math.Min(vWidth, p.FraeserD) : vWidth;
        sb.AppendLine($"(FraeserD={F(effectiveWidth)})");
        sb.AppendLine($"(TOOL D={F(p.FraeserD)} ANGLE={F(p.SchneidenWinkel)})");
        sb.AppendLine("(Gravieren)");
        sb.AppendLine($"(Text: {p.Text.Replace('\n', ' ').Replace('\r', ' ')})");
        sb.AppendLine($"(Font: {p.FontFamily}, {F(p.FontSizeMm)} mm, Winkel={p.SchneidenWinkel}°)");
        sb.AppendLine();
        sb.AppendLine($"M03 S{(int)p.Drehzahl}");
        sb.AppendLine(Sz());

        foreach (var figure in flat2.Figures)
        {
            double sx = MX(figure.StartPoint.X);
            double sy = MY(figure.StartPoint.Y);

            sb.AppendLine($"G00 X{F(sx)} Y{F(sy)}");
            sb.AppendLine($"G01 Z{F(zDepth)} F{(int)(p.Vorschub * 0.3)}");

            foreach (var seg in figure.Segments)
            {
                IEnumerable<System.Windows.Point> pts = seg switch
                {
                    System.Windows.Media.PolyLineSegment pls => pls.Points,
                    System.Windows.Media.LineSegment ls      => [ls.Point],
                    _                                         => []
                };
                foreach (var pt in pts)
                    sb.AppendLine($"G01 X{F(MX(pt.X))} Y{F(MY(pt.Y))} F{(int)p.Vorschub}");
            }

            if (figure.IsClosed)
                sb.AppendLine($"G01 X{F(sx)} Y{F(sy)} F{(int)p.Vorschub}");

            sb.AppendLine(Sz());
        }

        sb.AppendLine(Sz());
        sb.AppendLine("M05");
        return sb.ToString();
    }

    public record TextGeoCtx(
        System.Windows.Media.Geometry  AlignedGeo,
        System.Windows.Media.PathGeometry Flat,        // grob (tol 2.0) — VCarve-Berechnung
        System.Windows.Media.PathGeometry FlatDisplay, // fein (tol 0.5) — Darstellung
        double Scale, double MultiH,
        double Ox, double Oy, double YOffset);

    public static TextGeoCtx BuildTextGeo(GraviereParams p, double workW, double workH)
    {
        var typef = new System.Windows.Media.Typeface(
            new System.Windows.Media.FontFamily(p.FontFamily),
            System.Windows.FontStyles.Normal,
            System.Windows.FontWeights.Normal,
            System.Windows.FontStretches.Normal);

        const double emSize = 1000.0;
        var ft = new System.Windows.Media.FormattedText(
            string.IsNullOrEmpty(p.Text) ? " " : p.Text,
            CultureInfo.InvariantCulture,
            System.Windows.FlowDirection.LeftToRight,
            typef, emSize,
            System.Windows.Media.Brushes.Black, 1.0);

        var ftLine = new System.Windows.Media.FormattedText(
            "Ag", CultureInfo.InvariantCulture,
            System.Windows.FlowDirection.LeftToRight,
            typef, emSize, System.Windows.Media.Brushes.Black, 1.0);
        double lineH = ftLine.Height > 1e-6 ? ftLine.Height : 1;

        double scale = p.FontSizeMm > 0 ? p.FontSizeMm / lineH : 1.0;

        bool bezugRechts = p.Bezugspunkt.Contains("rechts", StringComparison.OrdinalIgnoreCase);
        ft.TextAlignment = (p.Ausrichtung == "Rechts" || bezugRechts) ? System.Windows.TextAlignment.Right
                         : p.Ausrichtung == "Mitte"                   ? System.Windows.TextAlignment.Center
                                                                       : System.Windows.TextAlignment.Left;
        if (p.TextBreite > 0 && scale > 0)
            ft.MaxTextWidth = p.TextBreite / scale;

        double multiH  = ft.Height > 1e-6 ? ft.Height : 1;
        double fieldH  = p.TextHoehe > 0 ? p.TextHoehe
                       : (p.FontSizeMm > 0 ? p.FontSizeMm : multiH * scale);
        double yOffset = (fieldH - multiH * scale) / 2.0;

        var geo2        = ft.BuildGeometry(new System.Windows.Point(0, 0));
        var flat2       = geo2.GetFlattenedPathGeometry(2.0, System.Windows.Media.ToleranceType.Absolute);
        var flatDisplay = geo2.GetFlattenedPathGeometry(0.5, System.Windows.Media.ToleranceType.Absolute);

        var (ox, oy) = ConvertBezugspunkt(p.Bezugspunkt, p.XRel, p.YRel, workW, workH);

        // Verschiebung damit der gewählte Bezugspunkt an der richtigen Textfeldecke/-kante liegt
        double textWEff = p.TextBreite > 0 ? p.TextBreite : ft.Width * scale;
        if (p.Bezugspunkt.Contains("Oben"))                                       oy -= fieldH;
        if (p.Bezugspunkt.Contains("rechts", StringComparison.OrdinalIgnoreCase)) ox -= textWEff;
        if (p.Bezugspunkt is "Mitte" or "Oben Mitte" or "Unten Mitte")            ox -= textWEff / 2;

        if (flat2.CanFreeze)       flat2.Freeze();
        if (flatDisplay.CanFreeze) flatDisplay.Freeze();
        if (geo2.CanFreeze)        geo2.Freeze();
        return new TextGeoCtx(geo2, flat2, flatDisplay, scale, multiH, ox, oy, yOffset);
    }

    // ── Textfeld-Tasche (Clipper2-basiert, exakter Polygon-Offset) ──────────

    public static string TextfeldTasche(GraviereParams p, double workW, double workH)
    {
        const double allow   = 0.5; // Schlichtaufmaß mm
        const double arcTolMm = 0.05; // Bogentoleranz mm

        var sb  = new StringBuilder();
        var ctx = BuildTextGeo(p, workW, workH);

        double scale  = ctx.Scale;
        double multiH = ctx.MultiH;

        // WPF ↔ CNC-mm Koordinatenumrechnung
        double MX(double wx) => ctx.Ox + wx * scale;
        double MY(double wy) => ctx.Oy + ctx.YOffset + (multiH - wy) * scale;
        double WX(double cx) => (cx - ctx.Ox) / scale;
        double WY(double cy) => multiH - (cy - ctx.Oy - ctx.YOffset) / scale;

        double r     = p.FraeserD / 2.0;
        double step  = Math.Max(0.1, p.FraeserD * 0.5);
        double depth = -Math.Abs(p.ZTiefe);

        // Clipper2 arbeitet in WPF-Koordinaten (Y-down) — 1 WPF-Einheit = scale mm
        double rWpf     = r     / scale;
        double allowWpf = allow / scale;
        double arcTol   = Math.Max(0.5, arcTolMm / scale);

        var geo = ctx.FlatDisplay;
        if (geo.Bounds.IsEmpty || scale < 1e-9)
        { sb.AppendLine("(kein Text)"); sb.AppendLine("M05"); return sb.ToString(); }

        // WPF-PathFigure → Clipper2 PathD (in WPF-Koordinaten, Y nach unten)
        Clipper2Lib.PathD FigToPath(System.Windows.Media.PathFigure fig)
        {
            var path = new Clipper2Lib.PathD();
            path.Add(new Clipper2Lib.PointD(fig.StartPoint.X, fig.StartPoint.Y));
            foreach (var seg in fig.Segments)
            {
                IEnumerable<System.Windows.Point> pts = seg switch
                {
                    System.Windows.Media.PolyLineSegment pls => pls.Points,
                    System.Windows.Media.LineSegment ls      => [ls.Point],
                    _                                         => []
                };
                foreach (var pt in pts)
                    path.Add(new Clipper2Lib.PointD(pt.X, pt.Y));
            }
            return path;
        }

        // Exakte Schnittlinienberechnung: horizontale Linie wpfY schneidet die Offset-Pfade
        // Gibt sortierte CNC-mm X-Werte zurück; EvenOdd-Paarung ergibt die Fräsintervalle.
        List<double> Scanline(Clipper2Lib.PathsD paths, double wpfY)
        {
            var xs = new List<double>();
            foreach (var path in paths)
            {
                int n = path.Count;
                for (int i = 0; i < n; i++)
                {
                    var a = path[i]; var b = path[(i + 1) % n];
                    if ((a.y < wpfY) != (b.y < wpfY))
                    {
                        double t = (wpfY - a.y) / (b.y - a.y);
                        xs.Add(MX(a.x + t * (b.x - a.x)));
                    }
                }
            }
            xs.Sort();
            return xs;
        }

        // Prüft ob ein CNC-mm Punkt in den Offset-Pfaden liegt (EvenOdd-Regel)
        bool InOffsetPaths(Clipper2Lib.PathsD paths, double cncX, double cncY)
        {
            var pt    = new Clipper2Lib.PointD(WX(cncX), WY(cncY));
            int count = 0;
            foreach (var path in paths)
                if (Clipper2Lib.Clipper.PointInPolygon(pt, path, 6) !=
                    Clipper2Lib.PointInPolygonResult.IsOutside) count++;
            return (count & 1) == 1;
        }

        // Diagonalen Übergang (lastX/Y → targetX/scanY) auf Kollisionsfreiheit prüfen
        bool CanDiag(Clipper2Lib.PathsD paths, double lx, double ly, double tx, double ty)
        {
            for (int i = 1; i <= 5; i++)
            {
                double t = i / 6.0;
                if (!InOffsetPaths(paths, lx + (tx - lx) * t, ly + (ty - ly) * t)) return false;
            }
            return true;
        }

        sb.AppendLine($"(TOOL D={F(p.FraeserD)} ANGLE=180)");
        sb.AppendLine("(Textfeld-Tasche fräsen)");
        sb.AppendLine($"(Text: {p.Text.Replace('\n', ' ').Replace('\r', ' ')})");
        sb.AppendLine($"(Font: {p.FontFamily}, {F(p.FontSizeMm)} mm)");
        sb.AppendLine();
        sb.AppendLine($"M03 S{(int)p.Drehzahl}");
        sb.AppendLine(Sz());

        var groups = GroupFiguresByLetter(geo.Figures.ToList());

        foreach (var group in groups)
        {
            // Buchstabenpfade für Clipper2 aufbauen
            var letterPaths = new Clipper2Lib.PathsD();
            foreach (var fig in group)
                if (fig.IsClosed) letterPaths.Add(FigToPath(fig));
            if (letterPaths.Count == 0) continue;

            // Exakter Polygon-Offset: Schrupp (r+allow) und Schlicht (r)
            var roughPaths  = Clipper2Lib.Clipper.InflatePaths(letterPaths, -(rWpf + allowWpf),
                                  Clipper2Lib.JoinType.Round, Clipper2Lib.EndType.Polygon, 2.0, 6, arcTol);
            var finishPaths = Clipper2Lib.Clipper.InflatePaths(letterPaths, -rWpf,
                                  Clipper2Lib.JoinType.Round, Clipper2Lib.EndType.Polygon, 2.0, 6, arcTol);

            // ── Schrupp-Zickzack ──────────────────────────────────────────────
            // EvenOdd-Scanline über alle roughPaths gemeinsam (korrekt für Löcher).
            // Intervalle werden spaltenweise verarbeitet: erst alle linken Segmente
            // (ivs[0]) von unten nach oben, dann alle rechten (ivs[1]) usw.
            // Dadurch je ein Eintauchen pro Streifen statt eines pro Zeile.
            if (roughPaths.Count > 0)
            {
                double allMinY = roughPaths.SelectMany(q => q).Min(pt => MY(pt.y));
                double allMaxY = roughPaths.SelectMany(q => q).Max(pt => MY(pt.y));

                var rows = new List<(double scanY, List<(double x0, double x1)> ivs)>();
                for (double scanY = allMinY; scanY <= allMaxY + step * 0.01; scanY += step)
                {
                    var xCuts = Scanline(roughPaths, WY(scanY));
                    var ivs   = new List<(double x0, double x1)>();
                    for (int i = 0; i + 1 < xCuts.Count; i += 2)
                        if (xCuts[i + 1] > xCuts[i] + 0.01) ivs.Add((xCuts[i], xCuts[i + 1]));
                    rows.Add((scanY, ivs));
                }

                int maxIvs = rows.Count > 0 ? rows.Max(r => r.ivs.Count) : 0;

                for (int pass = 0; pass < maxIvs; pass++)
                {
                    bool   atDepth   = false;
                    double lastX = 0, lastY = 0;
                    bool   rightward = true;
                    int    lastRi    = -1;
                    double lastIvX0  = 0, lastIvX1 = 0;

                    for (int ri = 0; ri < rows.Count; ri++)
                    {
                        var (scanY, ivs) = rows[ri];
                        if (pass >= ivs.Count) { rightward = !rightward; continue; }

                        var iv         = ivs[pass];
                        double targetX = rightward ? iv.x0 : iv.x1;

                        // adjacent: aufeinanderfolgende Zeile UND beide Punkte
                        // (Startpunkt lastX und Ziel targetX) liegen jeweils im
                        // Intervall der anderen Zeile (±step). Bidirektional, damit
                        // Übergänge breites→schmales Intervall nicht fälschlich
                        // als sicher eingestuft werden (z.B. Kreuzung→Arm).
                        bool adjacent = (ri == lastRi + 1) &&
                                        (targetX >= lastIvX0 - step) &&
                                        (targetX <= lastIvX1 + step) &&
                                        (lastX   >= iv.x0    - step) &&
                                        (lastX   <= iv.x1    + step);

                        if (!atDepth)
                        {
                            sb.AppendLine($"G00 X{F(targetX)} Y{F(scanY)}");
                            sb.AppendLine($"G01 Z{F(depth)} F{(int)(p.Vorschub * 0.3)}");
                            atDepth = true;
                        }
                        else if (adjacent || CanDiag(roughPaths, lastX, lastY, targetX, scanY))
                            sb.AppendLine($"G01 X{F(targetX)} Y{F(scanY)} F{(int)p.Vorschub}");
                        else
                        {
                            sb.AppendLine(Sz());
                            sb.AppendLine($"G00 X{F(targetX)} Y{F(scanY)}");
                            sb.AppendLine($"G01 Z{F(depth)} F{(int)(p.Vorschub * 0.3)}");
                        }

                        if (rightward) sb.AppendLine($"G01 X{F(iv.x1)} F{(int)p.Vorschub}");
                        else           sb.AppendLine($"G01 X{F(iv.x0)} F{(int)p.Vorschub}");

                        lastX    = rightward ? iv.x1 : iv.x0;
                        lastY    = scanY;
                        rightward = !rightward;
                        lastRi   = ri;
                        lastIvX0 = iv.x0;
                        lastIvX1 = iv.x1;
                    }
                    if (atDepth) sb.AppendLine(Sz());
                }
            }
            else
                sb.AppendLine("(Buchstabe zu schmal für Schrupp-Durchgang)");

            // ── Schlicht-Kontur im Gegenlauf ──────────────────────────────────
            if (finishPaths.Count == 0)
            { sb.AppendLine("(Buchstabe zu schmal für Schlicht-Kontur)"); continue; }

            foreach (var fPath in finishPaths)
            {
                if (fPath.Count < 3) continue;
                // Konvertierung in CNC-mm + Gegenlauf (Umkehrung)
                var cncPts = fPath.Select(pt => (X: MX(pt.x), Y: MY(pt.y))).ToList();
                cncPts.Reverse();

                double sx = cncPts[0].X, sy = cncPts[0].Y;
                sb.AppendLine($"G00 X{F(sx)} Y{F(sy)}");
                sb.AppendLine($"G01 Z{F(depth)} F{(int)(p.Vorschub * 0.3)}");
                foreach (var pt2 in cncPts.Skip(1))
                    sb.AppendLine($"G01 X{F(pt2.X)} Y{F(pt2.Y)} F{(int)p.Vorschub}");
                sb.AppendLine($"G01 X{F(sx)} Y{F(sy)} F{(int)p.Vorschub}");
                sb.AppendLine(Sz());
            }
        }

        sb.AppendLine("M05");
        return sb.ToString();
    }

    // Buchstaben-Figuren nach Buchstabe gruppieren:
    // Löcher (Figuren deren Bounds in einer anderen enthalten sind) werden
    // ihrer umgebenden Aussenkontur zugeordnet.
    private static List<List<System.Windows.Media.PathFigure>> GroupFiguresByLetter(
        List<System.Windows.Media.PathFigure> figures)
    {
        if (figures.Count == 0) return [];

        var infos = figures
            .Select(f => { var pg = new System.Windows.Media.PathGeometry(); pg.Figures.Add(f); return (Fig: f, Bounds: pg.Bounds); })
            .Where(t => !t.Bounds.IsEmpty)
            .ToList();

        var isHole = new bool[infos.Count];
        for (int i = 0; i < infos.Count; i++)
            for (int j = 0; j < infos.Count; j++)
                if (i != j && TaBoundsContains(infos[j].Bounds, infos[i].Bounds))
                    { isHole[i] = true; break; }

        var groups = infos
            .Select((t, i) => (t, i))
            .Where(x => !isHole[x.i])
            .OrderBy(x => x.t.Bounds.Left)
            .Select(x => new List<System.Windows.Media.PathFigure> { x.t.Fig })
            .ToList();

        for (int i = 0; i < infos.Count; i++)
        {
            if (!isHole[i]) continue;
            List<System.Windows.Media.PathFigure>? best = null;
            double bestArea = double.MaxValue;
            foreach (var g in groups)
            {
                var pgG = new System.Windows.Media.PathGeometry(); pgG.Figures.Add(g[0]);
                var b = pgG.Bounds;
                if (TaBoundsContains(b, infos[i].Bounds))
                {
                    double a = b.Width * b.Height;
                    if (a < bestArea) { bestArea = a; best = g; }
                }
            }
            best?.Add(infos[i].Fig);
        }
        return groups;
    }

    private static bool TaBoundsContains(System.Windows.Rect outer, System.Windows.Rect inner)
    {
        const double eps = 2.0;
        return inner.Left >= outer.Left - eps && inner.Right  <= outer.Right  + eps &&
               inner.Top  >= outer.Top  - eps && inner.Bottom <= outer.Bottom + eps;
    }

    // ── V-Carve: Einbeschriebene Kreise (Konturabtastung / Medialachs) ───

    /// CNC-Koordinaten (mm), Radius (mm) und Figur-Index eines einbeschriebenen Kreises.
    public record VCarveCircle(double X, double Y, double R, int FigIdx = -1);

    /// <summary>
    /// Läuft entlang einer geschlossenen PathFigure und liefert alle
    /// stepWpf-WPF-Einheiten einen Punkt (x,y) mit Einheitstangente (tx,ty).
    /// </summary>
    private static IEnumerable<(double x, double y, double tx, double ty)>
        WalkFigure(System.Windows.Media.PathFigure fig, double stepWpf)
    {
        // Stützpunkte sammeln
        var pts = new List<(double x, double y)>();
        pts.Add((fig.StartPoint.X, fig.StartPoint.Y));
        foreach (var seg in fig.Segments)
        {
            if (seg is System.Windows.Media.PolyLineSegment pls)
                foreach (var p in pls.Points) pts.Add((p.X, p.Y));
            else if (seg is System.Windows.Media.LineSegment ls)
                pts.Add((ls.Point.X, ls.Point.Y));
        }
        if (pts.Count < 3) yield break;
        int n = pts.Count;

        // Kanten mit kumulativen Startpositionen aufbauen
        var edges = new (double ex, double ey, double len,
                         double utx, double uty, double cum)[n];
        int eCount = 0;
        double cumLen = 0;
        for (int i = 0; i < n; i++)
        {
            int j  = (i + 1) % n;
            double dx = pts[j].x - pts[i].x;
            double dy = pts[j].y - pts[i].y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-10) continue;
            edges[eCount++] = (pts[i].x, pts[i].y, len, dx / len, dy / len, cumLen);
            cumLen += len;
        }
        if (eCount == 0 || cumLen < 1e-9) yield break;

        int ei = 0;
        for (double s = 0.0; s < cumLen - 1e-9; s += stepWpf)
        {
            // Zur richtigen Kante vorrücken
            while (ei + 1 < eCount && edges[ei + 1].cum <= s + 1e-12)
                ei++;
            var e = edges[ei];
            double t = Math.Clamp(s - e.cum, 0.0, e.len);
            yield return (e.ex + e.utx * t, e.ey + e.uty * t, e.utx, e.uty);
        }
    }

    /// <summary>
    /// Berechnet für jeden Konturpunkt alle 0,5 mm den größten einbeschriebenen
    /// Kreis, der innen tangiert und keine andere Konturlinie schneidet.
    /// Gibt CNC-Koordinaten (mm) und Radien zurück.
    /// </summary>
    /// Signierte Fläche einer Polygon-Liste in WPF-Koordinaten (Y-nach-unten).
    /// > 0 → Uhrzeigersinn (CW) auf dem Bildschirm
    /// < 0 → Gegenuhrzeigersinn (CCW)
    private static double SignedArea(System.Windows.Media.PathFigure fig)
    {
        double a = 0;
        double prevX = fig.StartPoint.X, prevY = fig.StartPoint.Y;
        double firstX = prevX, firstY = prevY;
        foreach (var seg in fig.Segments)
        {
            if (seg is System.Windows.Media.PolyLineSegment pls)
                foreach (var pt in pls.Points)
                { a += prevX * pt.Y - pt.X * prevY; prevX = pt.X; prevY = pt.Y; }
            else if (seg is System.Windows.Media.LineSegment ls)
            { var pt = ls.Point; a += prevX * pt.Y - pt.X * prevY; prevX = pt.X; prevY = pt.Y; }
        }
        a += prevX * firstY - firstX * prevY; // closing edge
        return a * 0.5;
    }

    /// Quadratische Distanz Punkt→Segment (kein sqrt nötig für Vergleiche).
    private static double PtSegDist2(double px, double py,
                                     double ax, double ay, double bx, double by)
    {
        double dx = bx - ax, dy = by - ay;
        double len2 = dx * dx + dy * dy;
        if (len2 < 1e-20) return (px - ax) * (px - ax) + (py - ay) * (py - ay);
        double t  = Math.Clamp(((px - ax) * dx + (py - ay) * dy) / len2, 0.0, 1.0);
        double qx = ax + t * dx - px;
        double qy = ay + t * dy - py;
        return qx * qx + qy * qy;
    }

    // Wrapper: BuildTextGeo muss auf dem UI/STA-Thread aufgerufen werden.
    // Für Hintergrund-Berechnungen: ctx auf UI-Thread holen, dann Überladung mit ctx aufrufen.
    public static List<VCarveCircle> ComputeVCarveCircles(
        GraviereParams p, double workW, double workH, double sampleStepMm = 0.5)
        => ComputeVCarveCircles(p, BuildTextGeo(p, workW, workH), sampleStepMm);

    public static List<VCarveCircle> ComputeVCarveCircles(
        GraviereParams p, TextGeoCtx ctx, double sampleStepMm = 0.5)
    {
        if (ctx.FlatDisplay.Bounds.IsEmpty) return [];

        double scale  = ctx.Scale;
        double multiH = ctx.MultiH;
        double stepW  = sampleStepMm / scale;

        double maxRmm = p.SchneidenWinkel < 179.9
            ? p.ZTiefe * Math.Tan(p.SchneidenWinkel * 0.5 * Math.PI / 180.0)
            : p.FraeserD * 0.5;
        double maxRw = maxRmm / scale;

        // Feine Geometrie (0.5) für Bogenabtastung, Normalen UND InGlyph-Ringe.
        // Ein einheitliches Polygon stellt sicher, dass Kreise die der Binary-Search
        // akzeptiert auch InGlyph bestehen — keine Inkonsistenz zwischen Toleranzen.
        var figs = ctx.FlatDisplay.Figures.Where(f => f.IsClosed).ToList();
        if (figs.Count == 0) return [];

        double probe = 0.1 / scale;

        // ── Schneller Even-Odd Point-in-Polygon (ersetzt WPF fillGeo.FillContains) ──────
        // Alle Figur-Ringe vorab aus den bereits geflatteneten Polygonen aufbauen.
        // Even-Odd Ray-Casting ist identisch zu WPFs FillRule.EvenOdd, aber rein arithmetisch
        // und damit ~50-100× schneller als der WPF-Geometry-Aufruf.
        var allRings  = new (double x, double y)[figs.Count][];
        var ringYMin  = new double[figs.Count];
        var ringYMax  = new double[figs.Count];
        for (int ri = 0; ri < figs.Count; ri++)
        {
            var f  = figs[ri];
            var rg = new List<(double x, double y)>();
            rg.Add((f.StartPoint.X, f.StartPoint.Y));
            foreach (var seg in f.Segments)
            {
                if (seg is System.Windows.Media.PolyLineSegment pls)
                    foreach (var pt in pls.Points) rg.Add((pt.X, pt.Y));
                else if (seg is System.Windows.Media.LineSegment ls)
                    rg.Add((ls.Point.X, ls.Point.Y));
            }
            allRings[ri] = rg.ToArray();
            double yMin = double.MaxValue, yMax = double.MinValue;
            foreach (var (_, ry) in allRings[ri]) { if (ry < yMin) yMin = ry; if (ry > yMax) yMax = ry; }
            ringYMin[ri] = yMin; ringYMax[ri] = yMax;
        }
        bool InGlyph(double px, double py)
        {
            bool inside = false;
            for (int ri = 0; ri < allRings.Length; ri++)
            {
                if (py < ringYMin[ri] || py > ringYMax[ri]) continue;
                var ring = allRings[ri];
                int n = ring.Length;
                for (int i = 0, j = n - 1; i < n; j = i++)
                {
                    double xi = ring[i].x, yi = ring[i].y;
                    double xj = ring[j].x, yj = ring[j].y;
                    if ((yi > py) != (yj > py) &&
                        px < (xj - xi) * (py - yi) / (yj - yi) + xi)
                        inside = !inside;
                }
            }
            return inside;
        }

        // ── Original-Segment-Endpunkte sammeln (zur Unterscheidung echter Ecken vs. Kurven-Approx) ──
        // Punkte aus geraden Segmenten im Original sind echte Ecken; Punkte aus Bezier-Approximation nicht.
        // Quantisierungsfaktor: 64 Einheiten pro Pixel bei emSize=1000 → ~0.016 Einheit Toleranz.
        static long Q(double v) => (long)Math.Round(v * 64);
        var origEndpts = new HashSet<(long, long)>();
        void CollectPg(System.Windows.Media.PathGeometry pg)
        {
            foreach (var pf in pg.Figures)
            {
                origEndpts.Add((Q(pf.StartPoint.X), Q(pf.StartPoint.Y)));
                foreach (var seg in pf.Segments)
                {
                    switch (seg)
                    {
                        case System.Windows.Media.LineSegment lseg:
                            origEndpts.Add((Q(lseg.Point.X), Q(lseg.Point.Y)));
                            break;
                        case System.Windows.Media.PolyLineSegment plseg:
                            foreach (var pt in plseg.Points)
                                origEndpts.Add((Q(pt.X), Q(pt.Y)));
                            break;
                        case System.Windows.Media.BezierSegment bseg:
                            origEndpts.Add((Q(bseg.Point3.X), Q(bseg.Point3.Y)));
                            break;
                        case System.Windows.Media.PolyBezierSegment pbseg:
                            for (int i = 2; i < pbseg.Points.Count; i += 3)
                                origEndpts.Add((Q(pbseg.Points[i].X), Q(pbseg.Points[i].Y)));
                            break;
                        case System.Windows.Media.QuadraticBezierSegment qseg:
                            origEndpts.Add((Q(qseg.Point2.X), Q(qseg.Point2.Y)));
                            break;
                        case System.Windows.Media.PolyQuadraticBezierSegment pqseg:
                            for (int i = 1; i < pqseg.Points.Count; i += 2)
                                origEndpts.Add((Q(pqseg.Points[i].X), Q(pqseg.Points[i].Y)));
                            break;
                        case System.Windows.Media.ArcSegment aseg:
                            origEndpts.Add((Q(aseg.Point.X), Q(aseg.Point.Y)));
                            break;
                    }
                }
            }
        }
        var origGeo = ctx.AlignedGeo;
        if (origGeo is System.Windows.Media.PathGeometry origPg)
            CollectPg(origPg);
        else if (origGeo is System.Windows.Media.GeometryGroup origGg)
            foreach (var child in origGg.Children)
                if (child is System.Windows.Media.PathGeometry childPg)
                    CollectPg(childPg);
        else
            CollectPg(origGeo.GetOutlinedPathGeometry());

        // ── Alle Polygon-Segmente vorberechnen (Einwärts-Normale + Tangente) ─
        var segs = new List<(double ax, double ay, double bx, double by,
                              int figIdx, double inx, double iny,
                              double tsx, double tsy)>(figs.Count * 500);

        // Per-Figur-Daten für die Bogen-Abtastung
        var figPts       = new List<(double x, double y)[]>(figs.Count);
        var figEdgeTsx   = new List<double[]>(figs.Count);
        var figEdgeTsy   = new List<double[]>(figs.Count);
        var figSegBounds = new List<bool[]>(figs.Count); // true = Original-Segment-Grenze

        for (int fi = 0; fi < figs.Count; fi++)
        {
            var fig = figs[fi];
            var ptsList   = new List<(double x, double y)>();
            var segBounds = new List<bool>(); // welche Vertices sind echte Segment-Endpunkte?

            // StartPoint: Grenze zwischen letztem und erstem Segment (geschlossener Pfad)
            ptsList.Add((fig.StartPoint.X, fig.StartPoint.Y));
            segBounds.Add(true);

            foreach (var seg in fig.Segments)
            {
                if (seg is System.Windows.Media.PolyLineSegment pls)
                {
                    for (int pi = 0; pi < pls.Points.Count; pi++)
                    {
                        var pt = pls.Points[pi];
                        ptsList.Add((pt.X, pt.Y));
                        // Echter Segment-Endpunkt wenn: letzter Punkt ODER im Original-Endpunkt-Set
                        // (gerade Original-Segmente haben alle Zwischenpunkte als echte Ecken;
                        //  Bezier-Approximation hat nur den letzten Punkt als echten Endpunkt)
                        bool isBound = pi == pls.Points.Count - 1 ||
                                       origEndpts.Contains((Q(pt.X), Q(pt.Y)));
                        segBounds.Add(isBound);
                    }
                }
                else if (seg is System.Windows.Media.LineSegment ls)
                {
                    ptsList.Add((ls.Point.X, ls.Point.Y));
                    segBounds.Add(true);
                }
            }

            var pts  = ptsList.ToArray();
            int nPts = pts.Length;

            var edgeTsx = new double[nPts];
            var edgeTsy = new double[nPts];

            for (int k = 0; k < nPts; k++)
            {
                int k2 = (k + 1) % nPts;
                double ddx = pts[k2].x - pts[k].x, ddy = pts[k2].y - pts[k].y;
                double sLen = Math.Sqrt(ddx * ddx + ddy * ddy);
                double etsx = 0, etsy = 0, einx = 0, einy = 0;
                if (sLen > 1e-10)
                {
                    etsx = ddx / sLen; etsy = ddy / sLen;
                    double lnx = -etsy, lny = etsx;
                    double mx = (pts[k].x + pts[k2].x) * 0.5;
                    double my = (pts[k].y + pts[k2].y) * 0.5;
                    bool lIn = InGlyph(mx + lnx*probe, my + lny*probe);
                    bool rIn = InGlyph(mx - lnx*probe, my - lny*probe);
                    if      ( lIn && !rIn) { einx =  lnx; einy =  lny; }
                    else if (!lIn &&  rIn) { einx = -lnx; einy = -lny; }
                }
                edgeTsx[k] = etsx; edgeTsy[k] = etsy;
                segs.Add((pts[k].x, pts[k].y, pts[k2].x, pts[k2].y, fi, einx, einy, etsx, etsy));
            }
            figPts.Add(pts);
            figEdgeTsx.Add(edgeTsx);
            figEdgeTsy.Add(edgeTsy);
            figSegBounds.Add(segBounds.ToArray());
        }
        var segsArr = segs.ToArray();
        var result  = new List<VCarveCircle>();

        // ── Räumliches Gitter: nur nahe Segmente bei Binarysuche prüfen ──────
        // cellSz = 2*maxRw → jede Zelle deckt alle Segmente innerhalb maxRw ab.
        // Segmente werden mit maxRw-Puffer eingetragen → Single-Cell-Lookup reicht.
        var   flatBounds = ctx.FlatDisplay.Bounds;
        double gridX0    = flatBounds.X - maxRw;
        double gridY0    = flatBounds.Y - maxRw;
        double cellSz    = Math.Max(maxRw * 2.0, 1e-6);
        int    gCols     = Math.Max(1, (int)Math.Ceiling((flatBounds.Width  + 2 * maxRw) / cellSz));
        int    gRows     = Math.Max(1, (int)Math.Ceiling((flatBounds.Height + 2 * maxRw) / cellSz));
        var    gridCells = new int[gRows * gCols][];
        {
            var tmp = new List<int>[gRows * gCols];
            for (int i = 0; i < tmp.Length; i++) tmp[i] = [];
            for (int si = 0; si < segsArr.Length; si++)
            {
                var sg = segsArr[si];
                int cx0 = Math.Max(0, (int)((Math.Min(sg.ax, sg.bx) - maxRw - gridX0) / cellSz));
                int cx1 = Math.Min(gCols - 1, (int)((Math.Max(sg.ax, sg.bx) + maxRw - gridX0) / cellSz));
                int cy0 = Math.Max(0, (int)((Math.Min(sg.ay, sg.by) - maxRw - gridY0) / cellSz));
                int cy1 = Math.Min(gRows - 1, (int)((Math.Max(sg.ay, sg.by) + maxRw - gridY0) / cellSz));
                for (int gy2 = cy0; gy2 <= cy1; gy2++)
                    for (int gx2 = cx0; gx2 <= cx1; gx2++)
                        tmp[gy2 * gCols + gx2].Add(si);
            }
            for (int i = 0; i < tmp.Length; i++) gridCells[i] = tmp[i].ToArray();
        }

        // ── Pro Figur: Konturkreise + Ecken-Kreise in Bogenreihenfolge ──────
        // Segment-Startindex pro Figur für segsArr-Zugriff
        var figSegStart = new int[figs.Count];
        { int fso = 0; for (int fi2 = 0; fi2 < figs.Count; fi2++) { figSegStart[fi2] = fso; fso += figPts[fi2].Length; } }

        const double cornerStepRad = 5.0 * Math.PI / 180.0;

        // Jede Figur (Buchstabe) ist unabhängig → parallel verarbeiten.
        // fillGeo ist Frozen → thread-sicher; segsArr/gridCells sind read-only.
        var resultsPerFig = new List<VCarveCircle>[figs.Count];

        System.Threading.Tasks.Parallel.For(0, figs.Count, fi =>
        {
            var pts       = figPts[fi];
            int nPts      = pts.Length;
            var edgeTsx   = figEdgeTsx[fi];
            var edgeTsy   = figEdgeTsy[fi];
            var segBounds = figSegBounds[fi];
            int so        = figSegStart[fi];
            var localResult = new List<VCarveCircle>();
            bool lastWasRegular = false; // ob localResult[^1] ein regulärer Kreis ist

            // Bogenlängen
            var arcLen = new double[nPts + 1];
            for (int k = 0; k < nPts; k++)
            {
                int    k2 = (k + 1) % nPts;
                double dx = pts[k2].x - pts[k].x, dy = pts[k2].y - pts[k].y;
                arcLen[k + 1] = arcLen[k] + Math.Sqrt(dx * dx + dy * dy);
            }
            double totalArc = arcLen[nPts];
            if (totalArc < stepW) { resultsPerFig[fi] = localResult; return; }

            // Ecken-Karte
            var cornerAtArc = new Dictionary<double, int>();
            for (int k = 0; k < nPts; k++)
            {
                if (!segBounds[k]) continue;
                int kP = (k - 1 + nPts) % nPts;
                if (edgeTsx[kP] * edgeTsx[k] + edgeTsy[kP] * edgeTsy[k] > 0.990) continue;
                var sP = segsArr[so + kP];
                var sC = segsArr[so + k];
                if (sP.inx * sP.inx + sP.iny * sP.iny < 1e-18) continue;
                if (sC.inx * sC.inx + sC.iny * sC.iny < 1e-18) continue;
                double bX = sP.inx + sC.inx, bY = sP.iny + sC.iny;
                if (bX * bX + bY * bY < 1e-18) continue;
                cornerAtArc[arcLen[k]] = k;
            }

            // Abtastpositionen
            var sampleSet = new SortedSet<double>();
            for (double pos = 0; pos < totalArc; pos += stepW) sampleSet.Add(pos);
            foreach (var ap in cornerAtArc.Keys) sampleSet.Add(ap);

            // Interpolator
            (double px, double py, double tx, double ty) PolyInterp(double arcPos)
            {
                int lo = 0, hi = nPts - 1;
                while (lo < hi) { int mid2 = (lo + hi + 1) / 2; if (arcLen[mid2] <= arcPos) lo = mid2; else hi = mid2 - 1; }
                double ax  = pts[lo].x, ay = pts[lo].y;
                double bx2 = pts[(lo + 1) % nPts].x, by2 = pts[(lo + 1) % nPts].y;
                double eL  = arcLen[lo + 1] - arcLen[lo];
                double t   = eL > 1e-12 ? (arcPos - arcLen[lo]) / eL : 0;
                double ddx = bx2 - ax, ddy = by2 - ay;
                double len = Math.Sqrt(ddx * ddx + ddy * ddy);
                return (ax + t * ddx, ay + t * ddy,
                        len > 1e-12 ? ddx / len : 1.0,
                        len > 1e-12 ? ddy / len : 0.0);
            }

            // Größten einbeschriebenen Kreis ab (ox,oy) in Richtung (nx,ny)
            // 10 Iterationen → ~0.015 mm Genauigkeit bei maxRw ≤ 16 mm (reicht für 0.1 mm Schrittweite)
            VCarveCircle? TryPt(double ox, double oy, double nx, double ny, double minRmm = 0.02)
            {
                double rL = 0.0, rH = maxRw;
                for (int it = 0; it < 10; it++)
                {
                    double rm  = (rL + rH) * 0.5;
                    double ecx = ox + nx * rm, ecy = oy + ny * rm;
                    double r2  = rm * rm;
                    bool   ok  = InGlyph(ecx, ecy);
                    if (ok)
                    {
                        int gcx = Math.Clamp((int)((ecx - gridX0) / cellSz), 0, gCols - 1);
                        int gcy = Math.Clamp((int)((ecy - gridY0) / cellSz), 0, gRows - 1);
                        foreach (var si in gridCells[gcy * gCols + gcx])
                        {
                            var sg = segsArr[si];
                            if (PtSegDist2(ecx, ecy, sg.ax, sg.ay, sg.bx, sg.by) < r2 - 1e-9)
                            { ok = false; break; }
                        }
                    }
                    if (ok) rL = rm; else rH = rm;
                }
                if (rL * scale < minRmm) return null;
                double fcx = ox + nx * rL, fcy = oy + ny * rL;
                if (minRmm > 0 && !InGlyph(fcx, fcy)) return null;
                return new VCarveCircle(
                    ctx.Ox + fcx * scale,
                    ctx.Oy + ctx.YOffset + (multiH - fcy) * scale,
                    rL * scale, fi);
            }

            foreach (double arcPos in sampleSet)
            {
                bool isCorner = cornerAtArc.TryGetValue(arcPos, out int ck);

                if (!isCorner)
                {
                    // ── Regulärer Konturkreis ──────────────────────────────
                    var (px, py, tx, ty) = PolyInterp(arcPos);
                    double lnx = -ty, lny = tx;
                    bool leftIn  = InGlyph(px + lnx * probe, py + lny * probe);
                    bool rightIn = InGlyph(px - lnx * probe, py - lny * probe);
                    double cnx = 0, cny = 0;
                    if      ( leftIn && !rightIn) { cnx =  lnx; cny =  lny; }
                    else if (!leftIn &&  rightIn) { cnx = -lnx; cny = -lny; }

                    if (cnx != 0 || cny != 0)
                    {
                        double rLo = 0.0, rHi = maxRw;
                        for (int iter = 0; iter < 10; iter++)
                        {
                            double mid  = (rLo + rHi) * 0.5;
                            double cx   = px + cnx * mid, cy = py + cny * mid;
                            double mid2 = mid * mid;
                            bool   valid = true;
                            int gcx2 = Math.Clamp((int)((cx - gridX0) / cellSz), 0, gCols - 1);
                            int gcy2 = Math.Clamp((int)((cy - gridY0) / cellSz), 0, gRows - 1);
                            foreach (var si in gridCells[gcy2 * gCols + gcx2])
                            {
                                var sg = segsArr[si];
                                if (sg.figIdx == fi && sg.inx * cnx + sg.iny * cny > 0.0 && sg.tsx * tx + sg.tsy * ty > 0.866)
                                    continue;
                                if (PtSegDist2(cx, cy, sg.ax, sg.ay, sg.bx, sg.by) < mid2 - 1e-9)
                                { valid = false; break; }
                            }
                            if (valid) rLo = mid; else rHi = mid;
                        }
                        if (rLo * scale >= 0.05)
                        {
                            double wpfCx = px + cnx * rLo, wpfCy = py + cny * rLo;
                            // Auf konvexen Kurven kann der Kreismittelpunkt rückwärts
                            // wandern. Nur gegen den vorherigen REGULÄREN Kreis prüfen —
                            // Fächer-Kreise liegen radial zur Ecke und würden sonst
                            // fälschlich als Referenz dienen.
                            bool backward = false;
                            if (lastWasRegular && localResult.Count > 0 && localResult[^1].FigIdx == fi)
                            {
                                var prev = localResult[^1];
                                double prevWpfCx = (prev.X - ctx.Ox) / scale;
                                double prevWpfCy = multiH - (prev.Y - ctx.Oy - ctx.YOffset) / scale;
                                backward = (wpfCx - prevWpfCx) * tx + (wpfCy - prevWpfCy) * ty
                                           < -stepW * 1.5;
                            }
                            if (!backward && InGlyph(wpfCx, wpfCy))
                            {
                                localResult.Add(new VCarveCircle(
                                    ctx.Ox + wpfCx * scale,
                                    ctx.Oy + ctx.YOffset + (multiH - wpfCy) * scale,
                                    rLo * scale, fi));
                                lastWasRegular = true;
                            }
                        }
                    }
                    continue;
                }

                // ── Ecken-Kreise (konkav + konvex) ────────────────────────
                int  ckP  = (ck - 1 + nPts) % nPts;
                var  csP  = segsArr[so + ckP];
                var  csC  = segsArr[so + ck];
                double evx = pts[ck].x, evy = pts[ck].y;
                bool isConcave = csP.tsx * csC.tsy - csP.tsy * csC.tsx < 0;

                double a1 = Math.Atan2(csP.iny, csP.inx);
                double a2 = Math.Atan2(csC.iny, csC.inx);
                double da = a2 - a1;
                while (da >  Math.PI) da -= 2 * Math.PI;
                while (da <= -Math.PI) da += 2 * Math.PI;

                if (isConcave)
                {
                    // Konkave Ecke: einbeschriebener Kreis entlang der Bisektrix beider Einwärts-Normalen.
                    // TryPt kann hier nicht verwendet werden — die anliegenden Segmente (die durch den
                    // Eckpunkt laufen) blockieren sonst den Binär-Such-Algorithmus auf Radius≈0.
                    // Stattdessen: Mittelpunkt = Eckpunkt + Bisektrix * (R / sinα), wobei sinα der
                    // Sinus des Winkels zwischen Bisektrix und jeder Einwärts-Normale ist.
                    double nbX = csP.inx + csC.inx, nbY = csP.iny + csC.iny;
                    double nbLen = Math.Sqrt(nbX * nbX + nbY * nbY);
                    if (nbLen > 1e-10)
                    {
                        nbX /= nbLen; nbY /= nbLen;
                        // sin(Winkel Bisektrix → n1) = |nb × n1|
                        double sinA = Math.Abs(nbX * csP.iny - nbY * csP.inx);
                        if (sinA > 1e-6)
                        {
                            // Binärsuche: grösstes R, sodass Kreis (Mittelpunkt = V + nb*(R/sinA), Radius R)
                            // nicht gegen nicht-anliegende Segmente verstösst und innerhalb des Glyphs liegt.
                            double rL = 0, rH = maxRw;
                            for (int it = 0; it < 12; it++)
                            {
                                double rm  = (rL + rH) * 0.5;
                                double d   = rm / sinA;
                                double ecx = evx + nbX * d, ecy = evy + nbY * d;
                                bool   ok  = InGlyph(ecx, ecy);
                                if (ok)
                                {
                                    double rm2 = rm * rm;
                                    int gcx2 = Math.Clamp((int)((ecx - gridX0) / cellSz), 0, gCols - 1);
                                    int gcy2 = Math.Clamp((int)((ecy - gridY0) / cellSz), 0, gRows - 1);
                                    foreach (var si in gridCells[gcy2 * gCols + gcx2])
                                    {
                                        var sg = segsArr[si];
                                        // Anliegende Segmente ausschliessen (laufen durch den Eckpunkt)
                                        if (sg.figIdx == fi &&
                                            ((Math.Abs(sg.ax - evx) < 1e-9 && Math.Abs(sg.ay - evy) < 1e-9) ||
                                             (Math.Abs(sg.bx - evx) < 1e-9 && Math.Abs(sg.by - evy) < 1e-9)))
                                            continue;
                                        if (PtSegDist2(ecx, ecy, sg.ax, sg.ay, sg.bx, sg.by) < rm2 - 1e-9)
                                        { ok = false; break; }
                                    }
                                }
                                if (ok) rL = rm; else rH = rm;
                            }
                            if (rL * scale >= 0.01)
                            {
                                double d   = rL / sinA;
                                double fcx = evx + nbX * d, fcy = evy + nbY * d;
                                localResult.Add(new VCarveCircle(
                                    ctx.Ox + fcx * scale,
                                    ctx.Oy + ctx.YOffset + (multiH - fcy) * scale,
                                    rL * scale, fi));
                            }
                        }
                    }
                }
                else
                {
                    // Konvexe Ecke: nur bei echten Ecken (≥ ~25°).
                    if (Math.Abs(da) < 0.44) { lastWasRegular = false; continue; }

                    // Fächer a1 → a2
                    int ns = Math.Max(4, (int)Math.Ceiling(Math.Abs(da) / cornerStepRad));
                    for (int s = 0; s <= ns; s++)
                    {
                        double a = a1 + da * s / ns;
                        var c = TryPt(evx, evy, Math.Cos(a), Math.Sin(a), minRmm: 0);
                        if (c != null) localResult.Add(c);
                    }
                }
                // Fächer-Kreise nicht als Referenz für den Rückwärts-Check verwenden
                lastWasRegular = false;
            }

            resultsPerFig[fi] = localResult;
        });

        // Ergebnisse in Figur-Reihenfolge zusammenführen (FigIdx-Sortierung bleibt erhalten)
        foreach (var r in resultsPerFig)
            if (r != null) result.AddRange(r);

        return result;
    }

    // ── V-Carve G-Code ───────────────────────────────────────────────────
    // Kreise sind bereits in Kontur-Reihenfolge (nach FigIdx + Bogenposition
    // sortiert). Innerhalb einer Figur wird KEIN Rückzug erzeugt – der Fräser
    // durchfährt Innenecken kontinuierlich mit variabler Tiefe.

    /// <summary>
    /// Dedupliziert Kreise (Mittelpunkte näher als <paramref name="dedupMm"/>) und tastet
    /// den Medialachsen-Pfad pro Figur mit gleichmässigem Abstand <paramref name="spacingMm"/>
    /// neu ab. X, Y und R werden linear interpoliert → glatte Z-Tiefenübergänge im G-Code.
    /// </summary>
    internal static List<VCarveCircle> ResampleVCarveCircles(
        List<VCarveCircle> raw, double dedupMm = 0.05, double spacingMm = 0.2, double simplifyMm = 1.0)
    {
        if (raw.Count == 0) return raw;
        double dedupSq = dedupMm * dedupMm;
        var result = new List<VCarveCircle>(raw.Count);

        int i = 0;
        while (i < raw.Count)
        {
            int fi = raw[i].FigIdx;
            int j  = i;
            while (j < raw.Count && raw[j].FigIdx == fi) j++;

            // 1. Konsekutives Dedup (original): entfernt unmittelbar aufeinanderfolgende Duplikate.
            //    Globales Dedup wurde verworfen – es entfernte Kreuzungskreise die für
            //    die Verknüpfung von Medialachsen-Ästen gebraucht werden.
            var dp = new List<VCarveCircle> { raw[i] };
            for (int k = i + 1; k < j; k++)
            {
                var prev = dp[^1];
                double ddx = raw[k].X - prev.X, ddy = raw[k].Y - prev.Y;
                if (ddx * ddx + ddy * ddy >= dedupSq)
                    dp.Add(raw[k]);
            }

            if (dp.Count == 1) { result.Add(dp[0] with { FigIdx = fi * 10000 }); i = j; continue; }

            // 1b. Ast-Segmentierung: Lücken > splitGap trennen verschiedene Medialachsen-Äste.
            //     Jedes Segment wird separat resamplet (verhindert lineare Interpolation über Äste).
            //     Schwellwert gross genug um Eckkreis-Sequenzen (< 1mm) nicht zu trennen.
            double splitGapSq = 3.0 * 3.0;
            var segs2   = new List<List<VCarveCircle>>();
            var curSeg2 = new List<VCarveCircle> { dp[0] };
            for (int k = 1; k < dp.Count; k++)
            {
                double ddx = dp[k].X - dp[k - 1].X, ddy = dp[k].Y - dp[k - 1].Y;
                if (ddx * ddx + ddy * ddy > splitGapSq) { segs2.Add(curSeg2); curSeg2 = []; }
                curSeg2.Add(dp[k]);
            }
            segs2.Add(curSeg2);

            int subFi = fi * 10000;
            foreach (var dpSeg in segs2)
            {
                int segFi = subFi++;
                if (dpSeg.Count == 1) { result.Add(dpSeg[0] with { FigIdx = segFi }); continue; }

                // 2. Kumulative Bogenlänge
                var cum = new double[dpSeg.Count];
                for (int k = 1; k < dpSeg.Count; k++)
                {
                    double dx = dpSeg[k].X - dpSeg[k - 1].X, dy = dpSeg[k].Y - dpSeg[k - 1].Y;
                    cum[k] = cum[k - 1] + Math.Sqrt(dx * dx + dy * dy);
                }
                double total = cum[^1];
                if (total < 1e-9) { result.Add(dpSeg[0] with { FigIdx = segFi }); continue; }

                // 3. Gleichmässige Neuabtastung (deaktiviert – nur berechnete Kreise verwenden)
                var figRes = dpSeg.Select(c => c with { FigIdx = segFi }).ToList();

                // 4. (Rückwärtsschritte werden bereits bei der Erzeugung in ComputeVCarveCircles
                // gefiltert — kein Post-Processing nötig.)

                result.AddRange(figRes);
            }
            i = j;
        }
        return result;
    }

    // ── V-Carve: G01/G00-Markierung (Erstellungsreihenfolge beibehalten) ────────────────────
    //
    // Die Kreise kommen aus ResampleVCarveCircles bereits in Bogenreihenfolge.
    // Diese Funktion vergibt nur neue FigIdx-Werte (→ G00-Rückzug im G-Code),
    // wenn die Lücke zwischen zwei aufeinanderfolgenden Kreisen > connectMm ist.
    // Kein Umsortieren – die Erstellungsreihenfolge wird 1:1 beibehalten.
    internal static List<VCarveCircle> RouteVCarveSegments(
        List<VCarveCircle> flat, double connectMm = 0.5)
    {
        if (flat.Count < 2) return flat;

        double cSq    = connectMm * connectMm;
        var    result = new List<VCarveCircle>(flat.Count);
        int    newFi  = 0;

        result.Add(flat[0] with { FigIdx = newFi });
        for (int i = 1; i < flat.Count; i++)
        {
            var    prev = flat[i - 1];
            var    curr = flat[i];
            double dx   = curr.X - prev.X, dy = curr.Y - prev.Y;
            if (dx * dx + dy * dy > cSq)
                newFi++;
            result.Add(curr with { FigIdx = newFi });
        }
        return result;
    }

    public static string VCarve(GraviereParams p, double workW, double workH)
    {
        double step    = p.SampleStepMm > 0 ? p.SampleStepMm : Math.Clamp(p.FontSizeMm / 300.0, 0.02, 0.1);
        var circles = RouteVCarveSegments(
                          ResampleVCarveCircles(
                              ComputeVCarveCircles(p, workW, workH, step),
                              spacingMm:  step,
                              simplifyMm: Math.Max(0.1, p.VereinfachungMm)));
        if (circles.Count == 0) return string.Empty;

        double halfRad = p.SchneidenWinkel * 0.5 * Math.PI / 180.0;
        double tanHalf = Math.Tan(halfRad);
        int    zFeed   = Math.Max(1, (int)(p.Vorschub * 0.3));

        var sb = new StringBuilder();
        sb.AppendLine("(V-Carve Gravur)");
        sb.AppendLine($"(Text: {p.Text.Replace('\n', ' ')})");
        sb.AppendLine($"(Schrift: {p.FontFamily}, Höhe: {p.FontSizeMm:F2} mm)");
        sb.AppendLine($"(Stichel: Winkel={p.SchneidenWinkel}°, D={p.FraeserD:F2} mm)");
        sb.AppendLine($"(Max. Tiefe: {p.ZTiefe:F3} mm)");
        sb.AppendLine($"(TOOL D={F(p.FraeserD)} ANGLE={F(p.SchneidenWinkel)})");
        sb.AppendLine();
        sb.AppendLine($"M03 S{(int)p.Drehzahl}");
        sb.AppendLine(Sz());

        int    prevFi = -1;
        double lastX  = 0, lastY = 0;
        foreach (var c in circles)
        {
            double z = -(c.R / tanHalf);
            if (z < -p.ZTiefe) z = -p.ZTiefe;

            if (c.FigIdx != prevFi)
            {
                // Neue Figur: Eckpunkt auf Z=0 fahren, dann Rückzug + Eilgang
                if (prevFi >= 0)
                {
                    sb.AppendLine($"G01 X{F(lastX)} Y{F(lastY)} Z0 F{zFeed}");
                    sb.AppendLine(Sz());
                }
                sb.AppendLine();
                sb.AppendLine($"G00 X{F(c.X)} Y{F(c.Y)}");
                sb.AppendLine($"G01 Z{F(z)} F{zFeed}");
                prevFi = c.FigIdx;
            }
            else
            {
                // Gleiche Figur: kontinuierlicher Schnitt, variable Tiefe, KEIN Rückzug
                sb.AppendLine($"G01 X{F(c.X)} Y{F(c.Y)} Z{F(z)} F{(int)p.Vorschub}");
            }
            lastX = c.X;
            lastY = c.Y;
        }

        sb.AppendLine();
        sb.AppendLine($"G01 X{F(lastX)} Y{F(lastY)} Z0 F{zFeed}");
        sb.AppendLine(Sz());
        sb.AppendLine("M05");
        return sb.ToString();
    }
}
