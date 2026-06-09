using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>
/// One theme entry in the swatch area (the widget's quick-switch grid below the
/// title bar AND Settings → Themes). Carries the catalog key, display name,
/// whether it is the active theme (drives the bold + fuller underline), the theme's
/// accent ink + own font (the name IS the swatch — rendered in the theme's accent
/// color and typeface, with a thin accent underline as the only extra color cue),
/// plus the legacy preview brushes kept for compatibility. A click routes through
/// <see cref="ApplyCommand"/> back into the widget VM so both swatch grids switch
/// in lockstep. Ported from the Python build's per-theme strip rows.
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

    /// <summary>The theme's own display font: a terminal theme's monospace family
    /// (e.g. Matrix → "Cascadia Code, Consolas") when it declares one, else the
    /// default UI font. The discreet name-entry renders in this so each row previews
    /// the theme's typeface as well as its accent ink. Mirrors
    /// <c>ThemePalette.ResolveValueFont</c> so the swatch font matches the live
    /// value-label font a theme would paint.</summary>
    public FontFamily NameFont { get; }

    [ObservableProperty] private bool _isActive;

    private static readonly FontFamily DefaultFont = new("Segoe UI Variable Display, Segoe UI");

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
        NameFont = ResolveFont(def);
    }

    /// <summary>Pick the theme's preview font: its monospace family (with the
    /// theme's declared fallback, defaulting to Consolas) when set, else the UI
    /// default. WPF resolves the comma list left-to-right, skipping uninstalled
    /// families, so no manual probe is needed.</summary>
    private static FontFamily ResolveFont(ThemeDefinition def)
    {
        if (!string.IsNullOrEmpty(def.MonospaceFont))
        {
            var fallback = string.IsNullOrEmpty(def.MonospaceFallback) ? "Consolas" : def.MonospaceFallback;
            return new FontFamily($"{def.MonospaceFont}, {fallback}");
        }
        return DefaultFont;
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
