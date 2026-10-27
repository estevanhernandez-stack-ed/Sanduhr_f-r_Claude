using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>
/// "The theme's Desk look" (theme match): one row per part of a theme's desk block (the Desk font, the ink, the meter bars, the message,
/// the glow, the plate and its tint, the shadow). A row is empty until set and shows the rule's value greyed meanwhile. Desk Look (while
/// the Desk follows) and the Theme Studio both use it. A row commits when it is left (a text box losing focus, the checkbox, a font
/// picked), never per keystroke; a row that does not parse commits nothing and says why.
/// </summary>
public sealed partial class ThemeDeskEditorViewModel : ObservableObject
{
    private bool _loading;
    private ThemeDesk _desk = new();

    public ThemeDeskEditorViewModel(IReadOnlyList<string> fontFamilies)
    {
        _fontChoices = new ObservableCollection<string>([SettingsNames.ThemeDeskFromTheme, .. fontFamilies]);
    }

    /// <summary>The font packs changed: the Desk font row's choices follow; the shown theme's font stays listed (as <see cref="Load"/> keeps it).</summary>
    public void ReloadFonts(IReadOnlyList<string> families)
    {
        var wanted = new List<string> { SettingsNames.ThemeDeskFromTheme };
        wanted.AddRange(families);
        if (_desk.Font is { Length: > 0 } f && !wanted.Contains(f)) wanted.Add(f);
        FontListSync.Sync(_fontChoices, wanted);
    }

    /// <summary>The Desk font row's choices: "From the theme" (no desk.font), then this PC's families.</summary>
    private readonly ObservableCollection<string> _fontChoices;
    public IReadOnlyList<string> FontChoices => _fontChoices;

    /// <summary>A committed edit: the whole desk block with the change.</summary>
    public Action<ThemeDesk>? Committed { get; set; }

    /// <summary>The desk block as the rows hold it now.</summary>
    public ThemeDesk Desk => _desk;

    [ObservableProperty] private string _font = SettingsNames.ThemeDeskFromTheme;
    [ObservableProperty] private string _ink = "";
    [ObservableProperty] private string _bars = "";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private string _glow = "";
    [ObservableProperty] private bool _plate = true;
    [ObservableProperty] private string _plateColor = "";
    [ObservableProperty] private string _shadow = "";

    [ObservableProperty] private string _fontDefault = "";
    [ObservableProperty] private string _inkDefault = "";
    [ObservableProperty] private string _barsDefault = "";
    [ObservableProperty] private string _messageDefault = "";
    [ObservableProperty] private string _glowDefault = "";
    [ObservableProperty] private string _plateColorDefault = "";
    [ObservableProperty] private string _shadowDefault = "";

    /// <summary>Why the last edit was not committed; null when it was.</summary>
    [ObservableProperty] private string? _problem;

    /// <summary>Shows <paramref name="theme"/>'s desk block and its rules' values. Not an edit.</summary>
    public void Load(ThemeDefinition theme)
    {
        _loading = true;
        _desk = theme.Desk ?? new ThemeDesk();
        // A font this PC lacks stays in the list so the picker is not blank (the Desk keeps its own font when drawing).
        if (_desk.Font is { Length: > 0 } f && !_fontChoices.Contains(f)) _fontChoices.Add(f);
        ShowRules(theme);
        Font = _desk.Font ?? SettingsNames.ThemeDeskFromTheme;
        Ink = Join(_desk.Ink);
        Bars = Join(_desk.Bars);
        Message = Join(_desk.Message);   // streamer: notname - the theme's Desk message colour list
        Glow = _desk.Glow ?? "";
        PlateColor = _desk.PlateColor ?? "";
        Shadow = _desk.Shadow ?? "";
        Plate = _desk.Plate ?? true;
        Problem = null;
        _loading = false;
    }

    /// <summary>The greyed values: <paramref name="theme"/>'s rules, its desk block left out (the Studio calls it as colors change).</summary>
    public void ShowRules(ThemeDefinition theme)
    {
        var rules = ThemeDeskLook.From(theme with { Desk = null });
        FontDefault = rules.Font ?? SettingsNames.DesksOwnFont;
        InkDefault = Join(rules.Ink);
        BarsDefault = Join(rules.Bars);
        MessageDefault = Join(rules.Message);   // streamer: notname - the theme's Desk message colour list
        GlowDefault = "#" + rules.Glow;
        PlateColorDefault = "#" + rules.PlateColor;
        ShadowDefault = "#" + rules.Shadow;
    }

    partial void OnFontChanged(string value) =>
        Commit(_desk with { Font = value is { Length: > 0 } f && f != SettingsNames.ThemeDeskFromTheme ? f : null });

    partial void OnInkChanged(string value) => CommitStops(value, s => _desk with { Ink = s });
    partial void OnBarsChanged(string value) => CommitStops(value, s => _desk with { Bars = s });
    partial void OnMessageChanged(string value) => CommitStops(value, s => _desk with { Message = s });
    partial void OnGlowChanged(string value) => CommitColor(value, c => _desk with { Glow = c });
    partial void OnPlateColorChanged(string value) => CommitColor(value, c => _desk with { PlateColor = c });
    partial void OnShadowChanged(string value) => CommitColor(value, c => _desk with { Shadow = c });
    partial void OnPlateChanged(bool value) => Commit(_desk with { Plate = value });

    private void CommitStops(string text, Func<IReadOnlyList<string>?, ThemeDesk> edit)
    {
        if (_loading) return;
        if (ParseStops(text) is not { } stops)
        {
            Problem = SettingsNames.ThemeColorsProblem;
            return;
        }
        Commit(edit(stops.Count == 0 ? null : stops));
    }

    private void CommitColor(string text, Func<string?, ThemeDesk> edit)
    {
        if (_loading) return;
        var t = (text ?? "").Trim();
        if (t.Length == 0)
        {
            Commit(edit(null));
            return;
        }
        if (t.Contains(',') || InkHex.Normalize(t) is not { } hex)
        {
            Problem = SettingsNames.ThemeColorProblem;
            return;
        }
        Commit(edit("#" + hex));
    }

    private void Commit(ThemeDesk next)
    {
        if (_loading) return;
        Problem = null;
        if (Same(_desk, next)) return;   // retyped or cleared to what is already there: nothing to ask
        _desk = next;
        Committed?.Invoke(next);
    }

    private static bool Same(ThemeDesk a, ThemeDesk b) =>
        a.Font == b.Font && a.Plate == b.Plate
        && string.Equals(a.Glow?.TrimStart('#'), b.Glow?.TrimStart('#'), StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.PlateColor?.TrimStart('#'), b.PlateColor?.TrimStart('#'), StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.Shadow?.TrimStart('#'), b.Shadow?.TrimStart('#'), StringComparison.OrdinalIgnoreCase)
        && Join(a.Ink) == Join(b.Ink) && Join(a.Bars) == Join(b.Bars) && Join(a.Message) == Join(b.Message);   // streamer: notname - compares Desk message colour lists

    /// <summary>"#e8e4dc, 6C63FF" is ["#e8e4dc", "#6c63ff"]; empty is []; a bad color or more than four is null.</summary>
    internal static IReadOnlyList<string>? ParseStops(string text)
    {
        var parts = (text ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > ThemeLint.MaxDeskStops) return null;
        var list = new List<string>(parts.Length);
        foreach (var p in parts)
        {
            if (InkHex.Normalize(p) is not { } hex) return null;
            list.Add("#" + hex);
        }
        return list;
    }

    private static string Join(IReadOnlyList<string>? stops) =>
        stops is null ? "" : string.Join(", ", stops.Select(s => "#" + s.TrimStart('#').ToLowerInvariant()));
}
