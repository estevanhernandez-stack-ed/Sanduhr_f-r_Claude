using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Sanduhr.App.ViewModels;

namespace Sanduhr.App.Views;

/// <summary>
/// Settings ▸ Widgets (spec 3.8): the verified catalog and the installed packs. <see cref="WidgetsPageViewModel"/> is the
/// DataContext. Each time the page becomes visible it asks the view model to <see cref="WidgetsPageViewModel.Activate"/>,
/// which is the only time the catalog is fetched without a click.
/// </summary>
public partial class WidgetsPage : UserControl
{
    /// <summary>true to false, for IsEnabled while the page is busy.</summary>
    public static readonly IValueConverter Not = new NotConverter();

    /// <summary>true to Collapsed, false to Visible (the empty-list line).</summary>
    public static readonly IValueConverter NotVisible = new NotVisibleConverter();

    public WidgetsPage()
    {
        InitializeComponent();
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) Activate();
        };
        DataContextChanged += (_, _) =>
        {
            if (IsVisible) Activate();
        };
    }

    private void Activate() => _ = (DataContext as WidgetsPageViewModel)?.Activate();

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
