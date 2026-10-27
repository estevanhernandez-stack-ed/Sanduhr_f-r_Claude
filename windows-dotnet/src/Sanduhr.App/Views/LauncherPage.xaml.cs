using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Sanduhr.App.ViewModels;

namespace Sanduhr.App.Views;

/// <summary>Settings, Launcher (Settings v2, slice 3). A selected button's row scrolls into view: a click on a missing app opens
/// Settings at it, and the page may not exist yet when the row is selected.</summary>
public partial class LauncherPage : UserControl
{
    private LauncherSettingsViewModel? _launcher;

    public LauncherPage()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Follow((DataContext as DeskSettingsViewModel)?.Launcher);
        Loaded += (_, _) =>
        {
            Follow((DataContext as DeskSettingsViewModel)?.Launcher);
            if (_launcher?.Rows.FirstOrDefault(r => r.IsSelected) is { } selected) ScrollSoon(selected);
        };
        Unloaded += (_, _) => Follow(null);
    }

    private void Follow(LauncherSettingsViewModel? next)
    {
        if (ReferenceEquals(_launcher, next)) return;
        if (_launcher is not null)
        {
            _launcher.Rows.CollectionChanged -= OnRowsChanged;
            foreach (var row in _launcher.Rows) row.PropertyChanged -= OnRowChanged;
        }
        _launcher = next;
        if (_launcher is not null)
        {
            _launcher.Rows.CollectionChanged += OnRowsChanged;
            foreach (var row in _launcher.Rows) row.PropertyChanged += OnRowChanged;
        }
    }

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (LauncherButtonRow row in e.OldItems) row.PropertyChanged -= OnRowChanged;
        if (e.Action == NotifyCollectionChangedAction.Reset && _launcher is not null)
            foreach (var row in _launcher.Rows) row.PropertyChanged -= OnRowChanged;
        if (e.NewItems is null && e.Action != NotifyCollectionChangedAction.Reset) return;
        foreach (LauncherButtonRow row in e.NewItems ?? Array.Empty<LauncherButtonRow>())
        {
            row.PropertyChanged -= OnRowChanged;
            row.PropertyChanged += OnRowChanged;
            // Reload builds rows with IsSelected set in the initializer, which raises nothing.
            if (row.IsSelected) ScrollSoon(row);
        }
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LauncherButtonRow.IsSelected) && sender is LauncherButtonRow { IsSelected: true } row)
            ScrollSoon(row);
    }

    /// <summary>After the landing's own scroll to the menu anchor settles, which would otherwise win.</summary>
    private void ScrollSoon(LauncherButtonRow row) =>
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
            (ButtonRows.ItemContainerGenerator.ContainerFromItem(row) as FrameworkElement)?.BringIntoView());
}
