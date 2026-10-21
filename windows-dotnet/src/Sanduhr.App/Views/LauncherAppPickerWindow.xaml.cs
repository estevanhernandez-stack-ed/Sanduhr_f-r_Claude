using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Sanduhr.App.Interop;
using Sanduhr.App.Services;

namespace Sanduhr.App.Views;

/// <summary>One app in the picker. Its icon is asked of the shell the first time a row is drawn and arrives a moment later.</summary>
internal sealed class AppPickItem(LauncherAppInfo app, LauncherAppCache cache) : ObservableObject
{
    public LauncherAppInfo App { get; } = app;
    public string Name => App.Name;
    public string Aumid => App.Aumid;

    /// <summary>The app's icon at 64 pixels, or null until the shell has given it.</summary>
    public ImageSource? Icon => cache.IconOf(App.Aumid, 64);

    public void IconMayHaveArrived() => OnPropertyChanged(nameof(Icon));

    /// <summary>What a screen reader says for the row: the app's name.</summary>
    public override string ToString() => App.Name;
}

/// <summary>
/// Add app… (spec, Desk Launcher): a searchable list of what the Start menu lists, with icons. Typing filters by name;
/// Down moves into the list; Enter or a double click picks. The list is read in the background, so the dialog opens
/// at once and fills in. <see cref="Chosen"/> is the pick, with <c>DialogResult == true</c>.
/// </summary>
internal partial class LauncherAppPickerWindow : Window
{
    private readonly LauncherAppCache _cache;
    private readonly List<AppPickItem> _all = [];
    private readonly ObservableCollection<AppPickItem> _shown = [];
    private int _knownCount = -1;

    /// <summary>The app that was picked; null when the dialog was cancelled.</summary>
    public LauncherAppInfo? Chosen { get; private set; }

    public LauncherAppPickerWindow(LauncherAppCache cache)
    {
        _cache = cache;
        InitializeComponent();
        AppList.ItemsSource = _shown;
        Loaded += (_, _) => SearchBox.Focus();
        _cache.Changed += OnCacheChanged;
        Closed += (_, _) => _cache.Changed -= OnCacheChanged;
        Load();
        _cache.Refresh([]);
    }

    private void OnCacheChanged()
    {
        if (_cache.Apps.Count != _knownCount) Load();
        foreach (var item in _all) item.IconMayHaveArrived();
    }

    private void Load()
    {
        var apps = _cache.Apps;
        _knownCount = apps.Count;
        _all.Clear();
        _all.AddRange(apps.Select(a => new AppPickItem(a, _cache)));
        Filter();
    }

    private void Filter()
    {
        var chosen = (AppList.SelectedItem as AppPickItem)?.Aumid;
        var q = SearchBox.Text.Trim();
        _shown.Clear();
        foreach (var item in _all)
            if (q.Length == 0 || item.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                || item.Aumid.Contains(q, StringComparison.OrdinalIgnoreCase))
                _shown.Add(item);
        AppList.SelectedItem = _shown.FirstOrDefault(i => i.Aumid == chosen) ?? (q.Length > 0 ? _shown.FirstOrDefault() : null);
        StatusText.Visibility = _shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = !_cache.Known ? "Reading your Start menu…"
            : _all.Count == 0 ? "No apps found in your Start menu."
            : "No app by that name.";
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => Filter();

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && _shown.Count > 0)
        {
            AppList.SelectedItem ??= _shown[0];
            (AppList.ItemContainerGenerator.ContainerFromItem(AppList.SelectedItem) as ListBoxItem)?.Focus();
            e.Handled = true;
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => AddButton.IsEnabled = AppList.SelectedItem is not null;

    private void OnListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(AppList, d) is ListBoxItem)
            Accept();
    }

    private void OnAddClick(object sender, RoutedEventArgs e) => Accept();

    private void Accept()
    {
        if (AppList.SelectedItem is not AppPickItem item) return;
        Chosen = item.App;
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
