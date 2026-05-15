using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;

namespace NCHops;

public enum MoveType { Rapid, Line, ArcCW, ArcCCW }

public record Move(MoveType Type, double X, double Y,
    double Xe = 0, double Ye = 0, double I = 0, double J = 0, int LineNumber = 0, double ToolWidthMm = 0);

public record SideMove(string Cmd, double X, double Z, int LineNumber = 0);

public record DrillHole(double X, double Y, int LineNumber);

public static class GCodeParser
{
    // Matches typical G-code words with signed decimals (e.g. X12, Y-3.5, Z.25).
    private static readonly Regex TokenRx = new(
        @"[XYZIJKZFASRT]-?(?:\d+(?:\.\d*)?|\.\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static string StripComment(string line)
    {
        var i = line.IndexOf('(');
        return i >= 0 ? line[..i].Trim() : line;
    }

    private static Dictionary<char, double> ParseWords(string line)
    {
        var dict = new Dictionary<char, double>();
        foreach (Match m in TokenRx.Matches(line))
        {
            var key = char.ToUpperInvariant(m.Value[0]);
            if (double.TryParse(
                    m.Value[1..],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var value))
            {
                dict[key] = value;
            }
        }
        return dict;
    }

    private static readonly Regex RxG0  = new(@"^G0($|[^0-9])", RegexOptions.Compiled);
    private static readonly Regex RxG1  = new(@"^G1($|[^0-9])", RegexOptions.Compiled);
    private static readonly Regex RxG2  = new(@"^G2($|[^0-9])", RegexOptions.Compiled);
    private static readonly Regex RxG3  = new(@"^G3($|[^0-9])", RegexOptions.Compiled);

    // Compute effective tool cut-width at a given depth.
    // angle = full included tip angle (degrees); 180 = flat end mill.
    private static double EffectiveToolWidth(double diam, double angleDeg, double z)
    {
        if (diam <= 0) return 0;
        if (angleDeg >= 180) return diam;
        double halfRad = angleDeg / 2.0 * Math.PI / 180.0;
        double vWidth  = 2.0 * Math.Abs(z) * Math.Tan(halfRad);
        return Math.Min(vWidth, diam);
    }

    private static readonly System.Text.RegularExpressions.Regex RxToolComment = new(
        @"\(TOOL\s+D=([\d.]+)\s+ANGLE=([\d.]+)\)",
        System.Text.RegularExpressions.RegexOptions.Compiled |
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    public static List<Move> ParseTopView(string text)
    {
        var moves = new List<Move>();
        double x = 0, y = 0, curZ = 0;
        double toolD = 0, toolAngle = 180;
        bool hasPos = false;
        int ln = 0;
        var inv = System.Globalization.CultureInfo.InvariantCulture;

        foreach (var raw in text.Split('\n'))
        {
            ln++;
            var trimmed = raw.Trim();

            // Parse TOOL comment (full line, before stripping)
            var tm = RxToolComment.Match(trimmed);
            if (tm.Success)
            {
                toolD     = double.Parse(tm.Groups[1].Value, inv);
                toolAngle = double.Parse(tm.Groups[2].Value, inv);
            }

            var line = StripComment(trimmed);
            if (string.IsNullOrEmpty(line)) continue;

            MoveType? mode = null;
            if (line.StartsWith("G00") || RxG0.IsMatch(line)) mode = MoveType.Rapid;
            else if (line.StartsWith("G01") || RxG1.IsMatch(line)) mode = MoveType.Line;
            else if (line.StartsWith("G02") || RxG2.IsMatch(line)) mode = MoveType.ArcCW;
            else if (line.StartsWith("G03") || RxG3.IsMatch(line)) mode = MoveType.ArcCCW;

            if (mode == null) continue;

            var vals = ParseWords(line);
            if (vals.TryGetValue('Z', out var nz)) curZ = nz;

            double tw = mode == MoveType.Rapid ? 0 : EffectiveToolWidth(toolD, toolAngle, curZ);

            if (mode is MoveType.Rapid or MoveType.Line)
            {
                bool hasX = vals.TryGetValue('X', out var nx);
                bool hasY = vals.TryGetValue('Y', out var ny);
                if (hasX) x = nx;
                if (hasY) y = ny;
                hasPos = true;
                // Z-Only-Moves (z.B. G01 Z-22) haben keine XY-Bewegung →
                // in der Topansicht nicht zeichnen
                if (hasX || hasY)
                    moves.Add(new Move(mode.Value, x, y, LineNumber: ln, ToolWidthMm: tw));
            }
            else if (hasPos)
            {
                double xs = x, ys = y;
                double xe = vals.TryGetValue('X', out var vx) ? vx : xs;
                double ye = vals.TryGetValue('Y', out var vy) ? vy : ys;
                double I = vals.TryGetValue('I', out var vi) ? vi : 0;
                double J = vals.TryGetValue('J', out var vj) ? vj : 0;
                moves.Add(new Move(mode.Value, xs, ys, xe, ye, I, J, ln, tw));
                x = xe; y = ye;
            }
        }
        return moves;
    }

    public static List<DrillHole> ParseDrillPoints(string text)
    {
        var holes = new List<DrillHole>();
        double x = 0, y = 0;
        int ln = 0;

        foreach (var raw in text.Split('\n'))
        {
            ln++;
            var line = StripComment(raw.Trim());
            if (string.IsNullOrEmpty(line)) continue;

            bool hasX = false, hasY = false, hasZ = false;
            double z = 0;
            var vals = ParseWords(line);
            if (vals.TryGetValue('X', out var vx)) { x = vx; hasX = true; }
            if (vals.TryGetValue('Y', out var vy)) { y = vy; hasY = true; }
            if (vals.TryGetValue('Z', out var vz)) { z = vz; hasZ = true; }

            if (hasZ && !hasX && !hasY && z < 0)
            {
                if (!holes.Any(h => Math.Abs(h.X - x) < 0.0001 && Math.Abs(h.Y - y) < 0.0001))
                    holes.Add(new DrillHole(x, y, ln));
            }
        }

        return holes;
    }

    public static List<SideMove> ParseSideView(string text)
    {
        var moves  = new List<SideMove>();
        var inv    = System.Globalization.CultureInfo.InvariantCulture;
        var style  = System.Globalization.NumberStyles.Float;
        double x = 0, y = 0, z = 0;
        int ln = 0;

        foreach (var raw in text.Split('\n'))
        {
            ln++;
            var line = StripComment(raw.Trim());
            if (string.IsNullOrEmpty(line)) continue;

            string? cmd = null;
            double? nx = null, ny = null, nz = null, ni = null, nj = null;

            foreach (var part in line.Split(' '))
            {
                var up = part.ToUpper();
                if      (up.StartsWith("G00") || up == "G0") cmd = "G0";
                else if (up.StartsWith("G01") || up == "G1") cmd = "G1";
                else if (up.StartsWith("G02") || up == "G2") cmd = "G2";
                else if (up.StartsWith("G03") || up == "G3") cmd = "G3";
                else if (up.StartsWith("X") && double.TryParse(up[1..], style, inv, out var vx)) nx = vx;
                else if (up.StartsWith("Y") && double.TryParse(up[1..], style, inv, out var vy)) ny = vy;
                else if (up.StartsWith("Z") && double.TryParse(up[1..], style, inv, out var vz)) nz = vz;
                else if (up.StartsWith("I") && double.TryParse(up[1..], style, inv, out var vi)) ni = vi;
                else if (up.StartsWith("J") && double.TryParse(up[1..], style, inv, out var vj)) nj = vj;
            }

            if (cmd == null) continue;

            if (cmd is "G2" or "G3" && (ni.HasValue || nj.HasValue))
            {
                // Bogen: X-Verlauf approximieren (zeigt z.B. Vollkreise korrekt in der Seitenansicht)
                double xe = nx ?? x, ye = ny ?? y, ze = nz ?? z;
                double cx = x + (ni ?? 0), cy = y + (nj ?? 0);
                double r  = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));

                if (r > 1e-9)
                {
                    double startA = Math.Atan2(y - cy, x - cx);
                    double endA   = Math.Atan2(ye - cy, xe - cx);
                    bool cw = cmd == "G2";
                    bool full = Math.Abs(xe - x) < 1e-6 && Math.Abs(ye - y) < 1e-6;
                    if (full)
                        endA = cw ? startA - 2 * Math.PI : startA + 2 * Math.PI;
                    else
                    {
                        if ( cw && endA > startA) endA -= 2 * Math.PI;
                        if (!cw && endA < startA) endA += 2 * Math.PI;
                    }

                    int steps = Math.Max(4, (int)(Math.Abs(endA - startA) / (Math.PI / 18)));
                    for (int s = 1; s <= steps; s++)
                    {
                        double a  = startA + (endA - startA) * s / steps;
                        double xi = cx + r * Math.Cos(a);
                        double zi = z + (ze - z) * s / steps;
                        moves.Add(new SideMove("G1", xi, zi, ln));
                    }
                }
                x = xe; y = ye; z = ze;
            }
            else
            {
                if (nx.HasValue) x = nx.Value;
                if (ny.HasValue) y = ny.Value;
                if (nz.HasValue) z = nz.Value;
                moves.Add(new SideMove(cmd, x, z, ln));
            }
        }
        return moves;
    }
}
