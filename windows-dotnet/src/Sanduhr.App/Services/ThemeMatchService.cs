using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sanduhr.App.Desk;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Services;

/// <summary>Follow on while a theme of the person's is active: where the Desk's look goes.</summary>
internal enum ThemeAddChoice { AddToTheme, NewTheme, Cancel }

/// <summary>A hand edit while following: where the change goes.</summary>
internal enum ThemeEditChoice { UpdateTheme, NewTheme, Cancel }

/// <summary>What a theme-match action did: whether it went ahead, and the line Settings shows ("" for none).</summary>
internal sealed record ThemeMatchResult(bool Ok, string Message);

/// <summary>
/// "One theme for the widget and the Desk", the writing half (spec, "Follow the widget theme, and Match now"). Follow on keeps the matched
/// look in a theme: a new "&lt;Theme&gt; + Desk" for a built-in or a pack's theme (never edited), or, for a theme of the person's, the
/// theme itself or a new one as they choose. Follow off and Match now copy the look into the Desk's own settings. Turning Follow on and
/// Match now save the Desk first, as an applied design does, so Put my Desk back returns the Desk and the widget theme together. The
/// pure rules are Core's <see cref="ThemeMatch"/>; the questions are seams the tests replace.
/// </summary>
internal sealed class ThemeMatchService
{
    private readonly SettingsStore _store;
    private readonly WidgetViewModel _widget;
    private readonly Func<DeskDesignService?> _designs;
    private static readonly ThemeMatchResult CouldNotSave = new(false, SettingsNames.CouldNotSaveDeskFirst);

    /// <param name="designs">Where the Desk is saved first; default the running one. None (the harness): nothing to save into, so it goes ahead.</param>
    public ThemeMatchService(SettingsStore store, WidgetViewModel widget, Func<DeskDesignService?>? designs = null)
    {
        _store = store;
        _widget = widget;
        _designs = designs ?? (() => DeskDesignService.Current);
    }

    public Func<string, ThemeAddChoice> AskAddOrNew { get; set; } = ThemeMatchPrompts.AddOrNew;

    /// <summary>The wallpaper's luminance under the clock (0 to 1), or null when there is no sample. The Desk's controller wires it (the
    /// readability guard); the default samples nothing.</summary>
    public Func<double?> WallpaperLuminance { get; set; } = () => null;

    /// <summary>Whether the guard is holding the plate on at the moment (the Desk's <c>PlateForced</c>); Follow off keeps it.</summary>
    public Func<bool> PlateForced { get; set; } = () => false;

    public bool Following => _store.LoadDeskFollowsTheme();

    private string ActiveKey => _widget.ActiveThemeKey;
    private ThemeDefinition Active => _widget.GetThemeDefinition(ActiveKey) ?? ThemeCatalog.BuiltIns["obsidian"];

    /// <summary>Follow the widget theme: on.</summary>
    public ThemeMatchResult FollowOn()
    {
        if (Following) return new(true, "");
        string key = ActiveKey;
        var def = Active;
        if (ThemeMatch.Origin(key) == ThemeOrigin.User)
        {
            switch (AskAddOrNew(def.Name))
            {
                case ThemeAddChoice.Cancel:
                    return new(false, "");
                case ThemeAddChoice.AddToTheme:
                    if (!SaveBefore(def.Name)) return CouldNotSave;
                    if (Write(key, ThemeMatch.WithDesk(def, ThemeMatch.SpelledOut(def))) is { } failed) return failed;
                    return Follow(key, def.Name);
            }
        }
        string name = ThemeMatch.NewThemeName(def.Name, NameTaken);
        string newKey = ThemeMatch.NewThemeKey(name, KeyTaken);
        if (!SaveBefore(name)) return CouldNotSave;
        if (Write(newKey, ThemeMatch.WithDesk(def, ThemeMatch.SpelledOut(def), name)) is { } bad) return bad;
        return Follow(newKey, name);
    }

    private ThemeMatchResult Follow(string key, string name)
    {
        _widget.ReloadUserThemes();
        _store.SaveDeskFollowsTheme(true);
        _widget.ApplyThemeByKey(key);   // raises ThemeChanged: the Desk draws the theme's look
        return new(true, SettingsNames.FollowingTheme(name));
    }

    /// <summary>Follow the widget theme: off. The look stays, as the Desk's own settings, so nothing jumps.</summary>
    public ThemeMatchResult FollowOff()
    {
        if (!Following) return new(true, "");
        var look = ThemeDeskLook.From(Active);
        if (PlateForced()) look = look with { Plate = true };   // nothing jumps: a plate the guard holds on stays on
        WriteOwn(look);
        _store.SaveDeskFollowsTheme(false);
        return new(true, SettingsNames.KeptAsOwnLook);
    }

    /// <summary>Match now: the theme's look copied into the Desk's own settings once, the Desk saved first. Follow stays off; no theme is made.</summary>
    public ThemeMatchResult MatchNow()
    {
        var def = Active;
        if (!SaveBefore(SettingsNames.MatchingTheme(def.Name))) return CouldNotSave;
        var look = ThemeDeskLook.From(def);
        var guarded = look.Guarded(WallpaperLuminance());
        WriteOwn(guarded);
        string said = SettingsNames.MatchedTheme(def.Name);
        return new(true, guarded.Plate && !look.Plate ? said + " " + SettingsNames.ThemeMatchPlateNote : said);
    }

    private void WriteOwn(ThemeDeskLook look)
    {
        var own = ThemeMatch.OwnSettings(look, _store.LoadDesk(), _store.LoadDeskInk(), _store.LoadDeskMessage(), _store.LoadNowPlaying());
        _store.SaveDesk(own.Desk);
        _store.SaveDeskPlate(own.Desk.Plate);
        _store.SaveDeskInk(own.Ink);
        _store.SaveDeskMessage(own.Message);
        _store.SaveNowPlaying(own.NowPlaying);
    }

    /// <summary>Saves the Desk as it is first (Put my Desk back). No design service: nothing to save into, so it goes ahead.</summary>
    private bool SaveBefore(string incoming) => _designs() is not { } designs || designs.SaveBeforeChange(incoming);

    /// <summary>Writes a theme under the themes folder, linted first; null when it was written, else the result to show.</summary>
    private ThemeMatchResult? Write(string key, JsonObject theme)
    {
        var lint = ThemeLint.Lint(theme);
        if (!lint.Ok) return new(false, lint.Errors.First().Message);
        try
        {
            Directory.CreateDirectory(_widget.ThemesDir);
            string path = Path.Combine(_widget.ThemesDir, key + ".json");
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, theme.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, path, overwrite: true);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new(false, SettingsNames.ThemeNotWritten(e.GetType().Name));
        }
    }

    private bool NameTaken(string name) => _widget.Themes.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>A new theme's file key is taken when a theme has it, a file has it, or Windows reserves it as a device name.</summary>
    internal bool KeyTaken(string key) =>
        IsReservedDeviceName(key) || _widget.HasTheme(key) || File.Exists(Path.Combine(_widget.ThemesDir, key + ".json"));

    /// <summary>CON, PRN, AUX, NUL, COM1 to COM9 and LPT1 to LPT9, any case: a file of that name cannot be made on Windows.</summary>
    internal static bool IsReservedDeviceName(string key) =>
        key.ToUpperInvariant() is "CON" or "PRN" or "AUX" or "NUL"
            or "COM1" or "COM2" or "COM3" or "COM4" or "COM5" or "COM6" or "COM7" or "COM8" or "COM9"
            or "LPT1" or "LPT2" or "LPT3" or "LPT4" or "LPT5" or "LPT6" or "LPT7" or "LPT8" or "LPT9";
}