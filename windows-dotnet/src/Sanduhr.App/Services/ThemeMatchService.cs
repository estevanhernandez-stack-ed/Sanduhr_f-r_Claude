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
    public Func<string, bool, ThemeEditChoice> AskWhere { get; set; } = ThemeMatchPrompts.Where;
    public Func<string, string?> AskName { get; set; } = ThemeMatchPrompts.Name;

    /// <summary>The wallpaper's luminance (0 to 1) under the clock on every screen that draws it; empty when there is no sample. The
    /// Desk's controller wires it (the readability guard); the default samples nothing.</summary>
    public Func<IReadOnlyList<double>> WallpaperLuminance { get; set; } = () => [];

    /// <summary>Whether a font family is on this PC (ruling P13: the Desk takes a look's font only when it is). The App wires the real
    /// check; the default says every font is there.</summary>
    public Func<string, bool> FontAvailable { get; set; } = _ => true;

    /// <summary>Test seam: called with the path right after a staged theme is moved into place, before the widget reloads.</summary>
    internal Action<string> AfterMoved { get; set; } = _ => { };

    /// <summary>The active theme's name (the status line while following).</summary>
    public string ActiveName => Active.Name;

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
                    if (Save(key, ThemeMatch.WithDesk(def, ThemeMatch.SpelledOut(def)), def.Name, overwrite: true, saveFirst: true) is { } failed) return failed;
                    return Follow(key, def.Name);
            }
        }
        string name = ThemeMatch.NewThemeName(def.Name, NameTaken);
        string newKey = ThemeMatch.NewThemeKey(name, KeyTaken);
        if (Save(newKey, ThemeMatch.WithDesk(def, ThemeMatch.SpelledOut(def), name), name, overwrite: false, saveFirst: true) is { } bad) return bad;
        return Follow(newKey, name);
    }

    private ThemeMatchResult Follow(string key, string name)
    {
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
        // Guarded only ever turns the plate on, so folding the screens' samples is exact.
        var samples = WallpaperLuminance();
        var guarded = samples.Aggregate(look, (l, s) => l.Guarded(s));
        WriteOwn(guarded);
        string said = SettingsNames.MatchedTheme(def.Name);
        if (guarded.Plate && !look.Plate) said += " " + SettingsNames.ThemeMatchPlateNote;
        // The Desk's own plate has one strength, so no key can hold a stronger one: say where the clock may be faint, and that Follow fixes it.
        if (guarded.Plate && samples.Any(s => look.PlateOpacityOver(s) > DeskPlate.Opacity)) said += " " + SettingsNames.ThemeMatchFaintNote;
        return new(true, said);
    }

    /// <summary>
    /// A hand edit while following (spec: asked once per edit, never per tick): <paramref name="next"/> is the whole desk block with the
    /// change. Update writes it into the active theme of the person's (never offered for a built-in or a pack's theme); Save as a new
    /// theme makes one, named by the person, and makes it active; Cancel leaves everything as it was (the caller shows the theme's
    /// values again). Not following: nothing.
    /// </summary>
    public ThemeMatchResult Edit(ThemeDesk next)
    {
        if (!Following) return new(false, "");
        string key = ActiveKey;
        var def = Active;
        bool canUpdate = ThemeMatch.Origin(key) == ThemeOrigin.User;
        switch (AskWhere(def.Name, canUpdate))
        {
            case ThemeEditChoice.UpdateTheme when canUpdate:
                if (Save(key, ThemeMatch.WithDesk(def, next), def.Name, overwrite: true, saveFirst: false) is { } failed) return failed;
                _widget.ApplyThemeByKey(key);
                return new(true, SettingsNames.ThemeUpdated(def.Name));
            case ThemeEditChoice.NewTheme:
                if (ThemeMatch.CleanName(AskName(ThemeMatch.NewThemeName(def.Name, NameTaken))) is not { } name) return new(false, "");
                string newKey = ThemeMatch.NewThemeKey(name, KeyTaken);
                if (Save(newKey, ThemeMatch.WithDesk(def, next, name), name, overwrite: false, saveFirst: false) is { } bad) return bad;
                _widget.ApplyThemeByKey(newKey);
                return new(true, SettingsNames.FollowingTheme(name));
            default:
                return new(false, "");
        }
    }

    private void WriteOwn(ThemeDeskLook look)
    {
        var own = ThemeMatch.OwnSettings(look, _store.LoadDesk(), _store.LoadDeskInk(), _store.LoadDeskMessage(), _store.LoadNowPlaying(), FontAvailable);
        _store.SaveDesk(own.Desk);
        _store.SaveDeskPlate(own.Desk.Plate);
        _store.SaveDeskInk(own.Ink);
        _store.SaveDeskMessage(own.Message);
        _store.SaveNowPlaying(own.NowPlaying);
    }

    /// <summary>Saves the Desk as it is first (Put my Desk back). No design service: nothing to save into, so it goes ahead.</summary>
    private bool SaveBefore(string incoming) => _designs() is not { } designs || designs.SaveBeforeChange(incoming);

    /// <summary>
    /// Writes a theme and loads it, in the order that keeps "nothing was changed" true: lint, stage the file, save the Desk first (only
    /// when <paramref name="saveFirst"/>: Follow on, not a hand edit while following), then move the file into place and reload. Null
    /// when it is in and loaded, else the result to show. A new theme (<paramref name="overwrite"/> false) never replaces a file or folder
    /// that is there; Update and Add rewrite the active user file. A file the loader then refuses is taken back out.
    /// </summary>
    private ThemeMatchResult? Save(string key, JsonObject theme, string incomingName, bool overwrite, bool saveFirst)
    {
        var lint = ThemeLint.Lint(theme);
        if (!lint.Ok) return new(false, lint.Errors.First().Message);
        string path = Path.Combine(_widget.ThemesDir, key + ".json");
        string tmp = path + ".tmp";
        try
        {
            Directory.CreateDirectory(_widget.ThemesDir);
            if (!overwrite && (File.Exists(path) || Directory.Exists(path))) return new(false, SettingsNames.ThemeNotWritten("AlreadyExists"));
            File.WriteAllText(tmp, theme.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TryDelete(tmp);
            return new(false, SettingsNames.ThemeNotWritten(e.GetType().Name));
        }
        byte[]? old = null;
        try
        {
            if (saveFirst && !SaveBefore(incomingName)) return CouldNotSave;
            try
            {
                if (overwrite && File.Exists(path)) old = File.ReadAllBytes(path);
                File.Move(tmp, path, overwrite);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return new(false, SettingsNames.ThemeNotWritten(e.GetType().Name));
            }
        }
        finally
        {
            TryDelete(tmp);   // after a move it is already gone
        }
        AfterMoved(path);
        _widget.ReloadUserThemes();
        if (_widget.HasTheme(key)) return null;
        // The loader refused a file the lint passed (ApplyThemeByKey would ignore the key silently): put the old file back, or remove the
        // new one, so the person's own theme keeps loading.
        try
        {
            if (old is not null) File.WriteAllBytes(path, old);
            else File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        _widget.ReloadUserThemes();
        return new(false, SettingsNames.ThemeNotWritten("NotLoaded"));
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
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