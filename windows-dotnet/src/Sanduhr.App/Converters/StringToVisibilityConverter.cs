using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Sanduhr.App.Converters;

/// <summary>Empty / null string → <see cref="Visibility.Collapsed"/>, else
/// <see cref="Visibility.Visible"/>. Keeps secondary card rows (reset countdown,
/// pace, burn projection, account label) quiet when there's nothing to show —
/// the Python build hid these labels the same way.</summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
