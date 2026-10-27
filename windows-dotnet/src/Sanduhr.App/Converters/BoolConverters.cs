using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Sanduhr.App.Converters;

/// <summary>Two bool converters the Settings pages and panels share.</summary>
public static class Bools
{
    /// <summary>true to false: IsEnabled while a page is busy.</summary>
    public static readonly IValueConverter Not = new NotConverter();

    /// <summary>true to Collapsed, false to Visible: an empty-list line.</summary>
    public static readonly IValueConverter NotVisible = new NotVisibleConverter();

    private sealed class NotConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
    }

    private sealed class NotVisibleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is true ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
