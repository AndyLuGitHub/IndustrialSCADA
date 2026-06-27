using System.Globalization;
using System.Windows.Data;

namespace IndustrialSCADA.Core.Converters;

/// <summary>
/// Converts a nullable DateTime to a formatted string for display.
/// Returns an em dash when the value is null.
/// </summary>
public class NullableDateTimeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DateTime dt)
            return dt.ToString("yyyy-MM-dd HH:mm:ss");
        return "\u2014";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
