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
            while (y <= p.Y1)
            {
                sb.AppendLine(hin ? $"G01 X{F(p.X1)}" : $"G01 X{F(p.X0)}");
                y += schritt;
                sb.AppendLine($"G01 Y{F(y)}");
                hin = !hin;
            }
        }
        else
        {
            double x = p.X0;
            bool hin = true;
            while (x <= p.X1)
            {
                sb.AppendLine(hin ? $"G01 Y{F(p.Y1)}" : $"G01 Y{F(p.Y0)}");
                x += schritt;
                sb.AppendLine($"G01 X{F(x)}");
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
        sb.AppendLine("G21");
        sb.AppendLine("G90");
        sb.AppendLine("G00 Z5.0000");

        foreach (var ref_ in p.Bezugspunkte)
        {
            var (x, y) = ConvertBezugspunkt(ref_, p.XRel, p.YRel, workW, workH);
            sb.AppendLine($"(Bohrung Bezugspunkt: {ref_})");
            sb.AppendLine($"G00 X{F(x)} Y{F(y)}");
            sb.AppendLine($"G01 Z{F(p.Bohrtiefe)} F300");
            sb.AppendLine("G00 Z5.0000");
        }

        sb.AppendLine("M05");
        return sb.ToString();
    }

    private static (double x, double y) ConvertBezugspunkt(string ref_, double xRel, double yRel, double w, double h)
        => ref_ switch
        {
            "unten_links"  => (xRel, yRel),
            "oben_links"   => (xRel, h - yRel),
            "unten_rechts" => (w - xRel, yRel),
            "oben_rechts"  => (w - xRel, h - yRel),
            "links_mitte"  => (xRel, h / 2 + yRel),
            "rechts_mitte" => (w - xRel, h / 2 + yRel),
            "oben_mitte"   => (w / 2 + xRel, h - yRel),
            "unten_mitte"  => (w / 2 + xRel, yRel),
            "mitte_mitte"  => (w / 2 + xRel, h / 2 + yRel),
            _              => (xRel, yRel)
        };
}
