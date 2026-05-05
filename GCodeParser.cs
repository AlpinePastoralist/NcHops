using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;

namespace NCHops;

public enum MoveType { Rapid, Line, ArcCW, ArcCCW }

public record Move(MoveType Type, double X, double Y,
    double Xe = 0, double Ye = 0, double I = 0, double J = 0);

public record SideMove(string Cmd, double X, double Z);

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

    public static List<Move> ParseTopView(string text)
    {
        var moves = new List<Move>();
        double x = 0, y = 0;
        bool hasPos = false;

        foreach (var raw in text.Split('\n'))
        {
            var line = StripComment(raw.Trim());
            if (string.IsNullOrEmpty(line)) continue;

            MoveType? mode = null;
            if (Regex.IsMatch(line, @"^G0($|[^0-9])") || line.StartsWith("G00")) mode = MoveType.Rapid;
            else if (Regex.IsMatch(line, @"^G1($|[^0-9])") || line.StartsWith("G01")) mode = MoveType.Line;
            else if (Regex.IsMatch(line, @"^G2($|[^0-9])") || line.StartsWith("G02")) mode = MoveType.ArcCW;
            else if (Regex.IsMatch(line, @"^G3($|[^0-9])") || line.StartsWith("G03")) mode = MoveType.ArcCCW;

            if (mode == null) continue;

            var vals = ParseWords(line);

            if (mode is MoveType.Rapid or MoveType.Line)
            {
                if (vals.TryGetValue('X', out var nx)) x = nx;
                if (vals.TryGetValue('Y', out var ny)) y = ny;
                hasPos = true;
                moves.Add(new Move(mode.Value, x, y));
            }
            else if (hasPos)
            {
                double xs = x, ys = y;
                double xe = vals.TryGetValue('X', out var vx) ? vx : xs;
                double ye = vals.TryGetValue('Y', out var vy) ? vy : ys;
                double I = vals.TryGetValue('I', out var vi) ? vi : 0;
                double J = vals.TryGetValue('J', out var vj) ? vj : 0;
                moves.Add(new Move(mode.Value, xs, ys, xe, ye, I, J));
                x = xe; y = ye;
            }
        }
        return moves;
    }

    public static List<Point> ParseDrillPoints(string text)
    {
        var points = new List<Point>();
        double x = 0, y = 0;

        foreach (var raw in text.Split('\n'))
        {
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
                var point = new Point(x, y);
                if (!points.Any(p => Math.Abs(p.X - point.X) < 0.0001 && Math.Abs(p.Y - point.Y) < 0.0001))
                    points.Add(point);
            }
        }

        return points;
    }

    public static List<SideMove> ParseSideView(string text)
    {
        var moves = new List<SideMove>();
        double x = 0, z = 0;

        foreach (var raw in text.Split('\n'))
        {
            var line = StripComment(raw.Trim());
            if (string.IsNullOrEmpty(line)) continue;

            string? cmd = null;
            foreach (var part in line.Split(' '))
            {
                var up = part.ToUpper();
                if (up.StartsWith("G00") || up == "G0") cmd = "G0";
                else if (up.StartsWith("G01") || up == "G1") cmd = "G1";
                else if (up.StartsWith("G02") || up == "G2") cmd = "G2";
                else if (up.StartsWith("G03") || up == "G3") cmd = "G3";
                else if (up.StartsWith("X")) double.TryParse(up[1..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x);
                else if (up.StartsWith("Z")) double.TryParse(up[1..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out z);
            }

            if (cmd != null)
                moves.Add(new SideMove(cmd, x, z));
        }
        return moves;
    }
}
