using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Sanduhr.App.Converters;

/// <summary>Wrap a <see cref="Color"/> in a frozen <see cref="SolidColorBrush"/>
/// for <c>Foreground</c>-style bindings (the % text picks up the same usage-ramp
/// color the bar fill uses).</summary>
public sealed class ColorToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
        return Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
