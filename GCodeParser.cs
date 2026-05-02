using System.Text.RegularExpressions;

namespace NCHops;

public enum MoveType { Rapid, Line, ArcCW, ArcCCW }

public record Move(MoveType Type, double X, double Y,
    double Xe = 0, double Ye = 0, double I = 0, double J = 0);

public record SideMove(string Cmd, double X, double Z);

public static class GCodeParser
{
    private static readonly Regex TokenRx = new(@"[XYIJ]-?\d+\.?\d*", RegexOptions.Compiled);

    public static List<Move> ParseTopView(string text)
    {
        var moves = new List<Move>();
        double x = 0, y = 0;
        bool hasPos = false;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            MoveType? mode = null;
            if (Regex.IsMatch(line, @"^G0($|[^0-9])") || line.StartsWith("G00")) mode = MoveType.Rapid;
            else if (Regex.IsMatch(line, @"^G1($|[^0-9])") || line.StartsWith("G01")) mode = MoveType.Line;
            else if (Regex.IsMatch(line, @"^G2($|[^0-9])") || line.StartsWith("G02")) mode = MoveType.ArcCW;
            else if (Regex.IsMatch(line, @"^G3($|[^0-9])") || line.StartsWith("G03")) mode = MoveType.ArcCCW;

            if (mode == null) continue;

            var vals = TokenRx.Matches(line)
                .ToDictionary(m => m.Value[0], m => double.Parse(m.Value[1..], System.Globalization.CultureInfo.InvariantCulture));

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

    public static List<SideMove> ParseSideView(string text)
    {
        var moves = new List<SideMove>();
        double x = 0, z = 0;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("(")) continue;

            string? cmd = null;
            foreach (var part in line.Split(' '))
            {
                var up = part.ToUpper();
                if (up.StartsWith("G00") || up == "G0") cmd = "G0";
                else if (up.StartsWith("G01") || up == "G1") cmd = "G1";
                else if (up.StartsWith("X")) double.TryParse(up[1..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x);
                else if (up.StartsWith("Z")) double.TryParse(up[1..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out z);
            }

            if (cmd != null)
                moves.Add(new SideMove(cmd, x, z));
        }
        return moves;
    }
}
