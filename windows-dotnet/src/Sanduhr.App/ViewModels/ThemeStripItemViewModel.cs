using CommunityToolkit.Mvvm.ComponentModel;

namespace Sanduhr.App.ViewModels;

/// <summary>
/// One clickable entry in the widget's theme strip (the horizontal row of theme
/// names below the title bar — "click a theme name to switch instantly"). Carries
/// the catalog key + display name and whether it is the active theme (drives the
/// accent highlight). Ported from <c>widget._show_theme_menu</c>'s per-theme rows.
/// </summary>
public sealed partial class ThemeStripItemViewModel : ObservableObject
{
    /// <summary>Catalog key (lowercase, e.g. "obsidian") passed to the apply command.</summary>
    public string Key { get; }

    /// <summary>Display name shown in the strip (e.g. "Obsidian").</summary>
    public string Name { get; }

    [ObservableProperty] private bool _isActive;

    public ThemeStripItemViewModel(string key, string name, bool isActive)
    {
        Key = key;
        Name = name;
        _isActive = isActive;
    }
}
