using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace NCHops;

// ConverterParameter format: "F2|mm"  →  displays "10.00mm", parses "10.00mm" or "10" back to double
public class UnitValueConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double d && parameter is string p)
        {
            var parts = p.Split('|');
            string fmt  = parts[0];
            string unit = parts.Length > 1 ? parts[1] : "";
            return d.ToString(fmt, CultureInfo.InvariantCulture) + unit;
        }
        return value?.ToString() ?? "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var s = (value?.ToString() ?? "").Trim();
        int end = s.Length;
        while (end > 0 && !char.IsDigit(s[end - 1]) && s[end - 1] != '.' && s[end - 1] != ',')
            end--;
        if (double.TryParse(s[..end], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            return v;
        return DependencyProperty.UnsetValue;
    }
}
