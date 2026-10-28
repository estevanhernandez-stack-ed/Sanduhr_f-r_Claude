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
    /// <remarks><c>ConverterParameter=empty</c> turns it around: visible only while the string is empty (theme match's greyed
    /// defaults show under an empty row).</remarks>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool empty = string.IsNullOrWhiteSpace(value as string);
        bool whenEmpty = string.Equals(parameter as string, "empty", StringComparison.Ordinal);
        return empty == whenEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
