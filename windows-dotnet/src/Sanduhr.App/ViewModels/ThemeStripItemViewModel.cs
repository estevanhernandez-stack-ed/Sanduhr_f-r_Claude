using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>
/// One theme tile in the swatch area (the widget's quick-switch grid below the
/// title bar AND Settings → Themes). Carries the catalog key, display name,
/// whether it is the active theme (drives the accent ring), and the theme's OWN
/// preview brushes so each tile renders in its theme's colors — a glass fill, an
/// accent stripe + chip, and the name in the theme's text color. A click routes
/// through <see cref="ApplyCommand"/> back into the widget VM so both swatch grids
/// switch in lockstep. Ported from the Python build's per-theme strip rows.
/// </summary>
public sealed partial class ThemeStripItemViewModel : ObservableObject
{
    private readonly Action<string> _apply;

    /// <summary>Catalog key (lowercase, e.g. "obsidian") passed to the apply path.</summary>
    public string Key { get; }

    /// <summary>Display name shown on the tile (e.g. "Obsidian").</summary>
    public string Name { get; }

    /// <summary>Tile fill — the theme's glass-on-Mica surface.</summary>
    public Brush GlassBrush { get; }

    /// <summary>The theme's solid background (deepest surface).</summary>
    public Brush BgBrush { get; }

    /// <summary>The theme's accent — stripe, chip, and the active/hover ring.</summary>
    public Brush AccentBrush { get; }

    /// <summary>The theme's primary text color — the name reads in its own ink.</summary>
    public Brush TextBrush { get; }

    /// <summary>The theme's (tinted) border for the tile's resting edge.</summary>
    public Brush PreviewBorder { get; }

    [ObservableProperty] private bool _isActive;

    public ThemeStripItemViewModel(string key, ThemeDefinition def, bool isActive, Action<string> apply)
    {
        Key = key;
        Name = def.Name;
        _isActive = isActive;
        _apply = apply;

        GlassBrush = Frozen(def.GlassOnMica);
        BgBrush = Frozen(def.Bg);
        AccentBrush = Frozen(def.Accent);
        TextBrush = Frozen(def.Text);
        PreviewBorder = Frozen(def.BorderTint ?? def.Border);
    }

    /// <summary>Apply this theme — live re-tint + persist + soft chime, via the
    /// widget VM (the single owner of the live palette + shared swatch state).</summary>
    [RelayCommand]
    private void Apply() => _apply(Key);

    private static SolidColorBrush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        brush.Freeze();
        return brush;
    }
}
