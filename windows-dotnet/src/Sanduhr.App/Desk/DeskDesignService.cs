using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>
/// What the Desk design service needs from the running app to show a design for review (spec, the agent-designed Desk, the live
/// review): the Desk and the widget redraw from memory, the floating bar, and the reload after a design is applied. The App's
/// <see cref="DeskDesignHost"/> is the real one; tests use a fake.
/// </summary>
internal interface IDeskDesignHost
{
    /// <summary>Draws the design from memory (nothing is written) and shows the review bar with <paramref name="model"/>; called again
    /// while the bar is up (a missing font arrived) it draws and updates in place. <paramref name="theme"/> is the widget theme to
    /// preview, null to leave the theme alone.</summary>
    void ShowPreview(DeskDesignSettings settings, ThemeDefinition? theme, DeskReviewModel model, Action apply, Action dismiss);

    /// <summary>Puts the saved Desk and theme back and takes the bar down.</summary>
    void EndPreview();

    /// <summary>A design was written to the settings: the Desk and Settings follow, and the theme <paramref name="themeKey"/> (null: unchanged) is selected.</summary>
    void Applied(string? themeKey);
}

/// <summary>What a saved-design action did, for the line Settings reads out.</summary>
internal readonly record struct DesignActionResult(bool Ok, string Message);

/// <summary>
/// The agent-designed Desk, the app side (spec, the agent-designed Desk, MCP tools, the live review, saved designs). Three jobs:
/// <list type="bullet">
/// <item><description>Keeps <c>%USERPROFILE%\.sanduhr\desk-state.json</c> current for <c>get_desk</c>: the Desk as a full design (built
/// from the settings and the active theme), the piece and anchor words, the installed font families, the wallpaper (a downscaled
/// preview file and its local summary), the saved designs, whether Claude may change the Desk directly, and the Start-menu apps.
/// Rewritten, debounced, when settings, the wallpaper, the monitors, the installed fonts or the Start menu change; only while the MCP
/// integration is installed (<c>mcp_enabled</c>), and removed (the preview with it) when it is not.</description></item>
/// <item><description>Watches <c>desk-design-request.json</c> and hands it to Core's <see cref="DeskDesignHandoff"/>, which checks the design
/// again. A good design is previewed (<see cref="IDeskDesignHost"/>: the Desk and widget redraw from memory, a floating bar offers
/// Apply and Dismiss) and answered <c>previewing</c> at once; Dismiss answers <c>rejected</c>, Apply saves the Desk it replaces
/// as a design, writes the new one and answers <c>applied</c>. With "Let Claude change the Desk directly" on, a design that needs
/// no font is applied at once, still saving the previous Desk first. A newer proposal replaces a preview.</description></item>
/// <item><description>Keeps the saved designs (<see cref="DesignStore"/>): switch to one, rename, pin, delete, and "Put my Desk back".</description></item>
/// </list>
/// Nothing here is sent anywhere: the wallpaper summary is computed in this process from the image file the Automatic ink
/// already reads, and leaves it only inside desk-state.json (and the preview file) for the local MCP client.
/// </summary>
internal sealed class DeskDesignService : IDisposable
{
    /// <summary>On from the first build that ships get_desk and propose_desk. A kill switch for a build that must not write the state.</summary>
    public const bool Enabled = true;

    /// <summary>The app's one service; null in tests and the harness.</summary>
    public static DeskDesignService? Current { get; set; }

    /// <summary>How long changes settle before the state is rewritten (a Settings slider writes many times).</summary>
    public static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(1500);

    private sealed record Inputs(DeskDesign Current, IReadOnlyList<string> Fonts, WallpaperOverview? Overview, string? WallpaperPath, string? WallpaperSource, IReadOnlyList<StartMenuApp>? Apps, IReadOnlyList<DeskMonitorInfo> Monitors);

    /// <summary>The proposal being reviewed.</summary>
    private sealed record Review(string Id, DeskDesign Proposal, IReadOnlyList<MissingFont> Fonts, IReadOnlyList<DeskReason> Warnings);

    private readonly Paths _paths;
    private readonly SettingsStore _store;
    private readonly LauncherAppCache? _apps;
    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _settle;
    private readonly WallpaperInk _wallpaper = new();
    private readonly WallpaperPreviewFile _preview = new();
    private readonly DesignStore _designs;
    private FileSystemWatcher? _watcher;
    private IReadOnlyList<string>? _fonts;
    private Review? _review;
    private bool _started;
    private bool _disposed;

    public DeskDesignService(Paths paths, SettingsStore store, LauncherAppCache? apps = null, Dispatcher? ui = null)
    {
        _paths = paths;
        _store = store;
        _apps = apps;
        _ui = ui ?? Dispatcher.CurrentDispatcher;
        _designs = new DesignStore(paths);
        InstalledFonts = InstalledWithPacks;
        Handoff = new DeskDesignHandoff(new DeskDesignPaths(paths.ExternalDir), Context) { Handler = OnProposal };
        _settle = new DispatcherTimer(DispatcherPriority.Background, _ui) { Interval = Settle };
        _settle.Tick += (_, _) =>
        {
            _settle.Stop();
            Publish();
        };
    }

    /// <summary>The request and result files, and the state writer.</summary>
    public DeskDesignHandoff Handoff { get; }

    /// <summary>The Desk and widget side of a review; null where there is none (tests, the harness): a design then waits as <c>not_ready</c>
    /// unless Claude may change the Desk directly.</summary>
    public IDeskDesignHost? Host { get; set; }

    /// <summary>The song looks (spec, a look per song): a design's <c>looks</c> are saved through it when the design is applied. Null where there are
    /// none (tests, the harness): the looks are then left alone and the answer says so.</summary>
    public NowPlayingLookService? Looks { get; set; }

    /// <summary>What the widget's own check of a song look knows about this PC (the wallpaper under the now-playing piece, for the contrast rule).</summary>
    internal DeskDesignContext LooksContext() => Context();

    /// <summary>The connected monitors, named (tests set it).</summary>
    internal Func<IReadOnlyList<DeskMonitorInfo>> Monitors { get; set; } = DeskMonitorSource.Read;

    /// <summary>A monitor id as a person knows it ("1920×1080, right of main"), for the review's lines.</summary>
    private string? MonitorName(string id) => DeskMonitorNames.NameOf(Monitors(), id);

    /// <summary>The installed font families (tests set it).</summary>
    internal Func<IReadOnlyList<string>> InstalledFonts { get; set; } = InstalledFamilies;

    /// <summary>The system's families and the installed font packs' (3.9.1): what a design may name as installed.</summary>
    private IReadOnlyList<string> InstalledWithPacks() => PackFontCatalog.Union(InstalledFamilies(),
        PackFontCatalog.Families(new WidgetStore(_paths.WidgetsDir, WidgetCatalogService.EncodePng)));

    /// <summary>The clock (tests set it).</summary>
    internal Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    /// <summary>The saved designs or "Put my Desk back" changed (Settings and the tray follow).</summary>
    public event Action? DesignsChanged;

    /// <summary>A design is on the desktop waiting for Apply or Dismiss.</summary>
    public bool Reviewing => _review is not null;

    public void Start()
    {
        if (!Enabled || _started || _disposed) return;
        _started = true;
        SettingsStore.AnyWritten += OnSettingsWritten;
        if (_apps is not null) _apps.Changed += Refresh;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        SystemEvents.InstalledFontsChanged += OnFontsChanged;

        var dir = Handoff.Paths.Support;
        try
        {
            Directory.CreateDirectory(dir);
            _watcher = new FileSystemWatcher(dir, DeskDesign.RequestFile) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName };
            _watcher.Created += (_, _) => Check();
            _watcher.Changed += (_, _) => Check();
            _watcher.Renamed += (_, _) => Check();
            _watcher.Error += (_, _) => Check();
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
        {
            // No watcher: a request waits (for up to its ten minutes) until the next launch picks it up.
            _watcher?.Dispose();
            _watcher = null;
        }
        Check();
        Refresh();   // off the start-up path: the state is written once things have settled
    }

    /// <summary>Something the state shows may have changed: rewrite it once things settle.</summary>
    public void Refresh()
    {
        if (!Enabled || _disposed) return;
        if (!_ui.CheckAccess()) { _ui.BeginInvoke(Refresh); return; }
        _settle.Stop();
        _settle.Start();
    }

    private void OnSettingsWritten() => Refresh();

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.Desktop or UserPreferenceCategory.Color) Refresh();
    }

    private void OnDisplayChanged(object? sender, EventArgs e) => Refresh();

    private void OnFontsChanged(object? sender, EventArgs e) => _ui.BeginInvoke(FontsChanged);

    /// <summary>The installed fonts changed: the list is read again, and a design waiting for a font draws with it and Apply wakes up
    /// as soon as the font is there. UI thread.</summary>
    internal void FontsChanged()
    {
        _fonts = null;
        Refresh();
        if (_review is not null && !_disposed) ShowReview();
    }

    private void Check()
    {
        if (_disposed) return;
        _ui.BeginInvoke(() =>
        {
            if (!_disposed && !ResetGuard.Done) Handoff.Check();
        });
    }

    // -- the state ----------------------------------------------------------------------------------------

    /// <summary>Writes (or removes) desk-state.json now. UI thread.</summary>
    internal void Publish()
    {
        if (_disposed || ResetGuard.Done) return;
        if (!Enabled || !_store.LoadMcpEnabled())
        {
            Handoff.DeleteState();   // the integration is off: no state for it to read
            _preview.Delete(PreviewPath);   // and no copy of the wallpaper
            return;
        }
        var i = Gather();
        string? previewPath = i.WallpaperPath is not null && i.WallpaperSource is not null ? _preview.Ensure(i.WallpaperPath, PreviewPath) : null;
        if (previewPath is null) _preview.Delete(PreviewPath);
        var state = DeskState.Build(i.Current, i.Fonts, i.Overview, previewPath, i.Apps, _designs.List(), _store.LoadDeskDirect(), AppVersion,
            wallpaperSource: previewPath is null ? null : i.WallpaperSource, wallpaperIsImage: i.WallpaperPath is not null, monitors: i.Monitors);
        Handoff.WriteState(state);
    }

    /// <summary>Where the downscaled wallpaper copy for Claude Code lives.</summary>
    private string PreviewPath => Path.Combine(Handoff.Paths.Support, WallpaperPreview.FileName);

    private Inputs Gather()
    {
        var current = CurrentDesign();
        _fonts ??= InstalledFonts();
        _wallpaper.Refresh();
        var (overview, path) = _wallpaper.Overview();
        IReadOnlyList<StartMenuApp>? apps = null;
        if (_apps is not null)
        {
            _apps.Refresh([]);   // reads the Start menu in the background when the list is old; Changed follows
            if (_apps.Known) apps = _apps.Apps.Select(a => new StartMenuApp(a.Aumid, a.Name)).ToList();
        }
        return new Inputs(current, _fonts, overview, path, _wallpaper.PathSource, apps, Monitors());
    }

    /// <summary>The Desk as it is now, as a full design (the settings and the active theme).</summary>
    internal DeskDesign CurrentDesign() =>
        DeskState.Current(_store.LoadDesk(), _store.LoadDeskInk(), _store.LoadLauncher(), ActiveTheme(), _store.LoadDeskWeather().Nickname,
            _store.LoadDeskFollowsTheme());

    /// <summary>What the widget's own check of a request knows about this PC.</summary>
    private DeskDesignContext Context()
    {
        var i = Gather();
        return new DeskDesignContext(
            i.Fonts.Count == 0 ? null : i.Fonts, i.Overview, i.Apps?.Select(a => a.Id).ToList(), i.Current, i.Monitors);
    }

    /// <summary>The theme on the widget now, as its JSON document (the saved key resolved against the built-ins and the user's themes).</summary>
    private JsonObject? ActiveTheme()
    {
        try
        {
            string key = _store.LoadTheme();
            var map = ThemeMap();
            if (!map.TryGetValue(key, out var theme))
            {
                key = "obsidian";
                theme = ThemeCatalog.BuiltIns[key];
            }
            return ThemeDraft.FromDefinition(key, theme).ToJson();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The built-in themes, the user's and the installed theme packs'.</summary>
    private Dictionary<string, ThemeDefinition> ThemeMap() =>
        PackThemes.Merged(_paths.ThemesDir, new WidgetStore(_paths.WidgetsDir, WidgetCatalogService.EncodePng));

    private bool FontInstalled(string family)
    {
        if (family.Length == 0 || BundledFonts.Bundled(family) is not null) return true;
        _fonts ??= InstalledFonts();
        return _fonts.Contains(family, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The installed font families, as the Desk's font picker names them (WPF's <see cref="Fonts.SystemFontFamilies"/>).</summary>
    private static IReadOnlyList<string> InstalledFamilies()
    {
        try
        {
            return Fonts.SystemFontFamilies.Select(f => f.Source)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            return [];
        }
    }

    /// <summary>The widget's own version, as the snapshot writer stamps it.</summary>
    private static string AppVersion
    {
        get
        {
            var v = typeof(DeskDesignService).Assembly.GetName().Version;
            return v is null ? "unknown" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    // -- the live review ----------------------------------------------------------------------------------

    /// <summary>A design that passed the rules (the handoff's handler): previewed for the user, or applied at once when Claude may
    /// change the Desk directly.</summary>
    private DeskAnswer OnProposal(string id, DeskValidation verdict)
    {
        try { return OnProposalCore(id, verdict); }
        catch (Exception e) when (IsRecoverable(e))
        {
            Log("proposal", e);
            return DeskAnswer.Refused([new DeskReason("design", "could not be shown (" + e.GetType().Name + "); nothing was changed")]);
        }
    }

    private DeskAnswer OnProposalCore(string id, DeskValidation verdict)
    {
        if (ResetGuard.Done || verdict.Design is not { } proposal) return DeskAnswer.NotReady;
        var current = CurrentDesign();
        var merged = DeskDesign.Merge(current, proposal);
        var changes = DeskDesign.Diff(current, merged, SettingsNames.DeskPieceName, MonitorName);

        // A Launcher image is a file Sanduhr stored; a design cannot bring one, and one that went is not a design to apply.
        var missingImages = DeskDesigns.MissingImages(proposal, _designs.ImageExists);
        if (missingImages.Count > 0)
            return DeskAnswer.Refused([new DeskReason("launcher", "names a Launcher image that is not on this PC: " + string.Join(", ", missingImages.Take(3)))]);

        var fonts = verdict.Fonts.Where(f => !FontInstalled(f.Family)).ToList();
        if (_store.LoadDeskDirect() && fonts.Count == 0)
        {
            ReplaceReview();
            return ApplyDesign(proposal, changes, verdict.Warnings);
        }
        if (Host is null) return DeskAnswer.NotReady;

        ReplaceReview();
        _review = new Review(id, proposal, fonts, verdict.Warnings);
        ShowReview();
        return new DeskAnswer(DeskAnswer.Previewing, Fonts: fonts.Count > 0 ? fonts : null, Changes: changes.Count > 0 ? changes : null,
            Warnings: verdict.Warnings.Count > 0 ? verdict.Warnings : null);
    }

    /// <summary>A newer proposal takes the place of the one on the desktop: the older one is answered rejected.</summary>
    private void ReplaceReview()
    {
        if (_review is not { } old) return;
        _review = null;
        HostEnd();
        Answer(old.Id, new DeskAnswer(DeskAnswer.Rejected, "replaced", [new DeskReason("design", "replaced by a newer proposal")]));
    }

    /// <summary>Draws the review now: the Desk and widget from memory, and the bar (built again when a font arrives).</summary>
    private void ShowReview()
    {
        if (_review is not { } r || Host is not { } host) return;
        var current = CurrentDesign();
        var merged = DeskDesign.Merge(current, r.Proposal);
        var desk = _store.LoadDesk();
        var model = DeskReviewModel.Build(r.Proposal, current, merged, r.Fonts, FontInstalled, desk.Enabled, SettingsNames.DeskPieceName, r.Warnings, MonitorName);

        var settings = DeskDesignApply.Settings(desk, _store.LoadDeskInk(), _store.LoadLauncher(), merged);
        // A font this PC lacks previews in the nearest installed look; the design keeps the family it asked for.
        if (!string.IsNullOrEmpty(settings.Desk.Font) && !FontInstalled(settings.Desk.Font))
        {
            string stand = DeskDesignApply.PreviewFont(settings.Desk.Font, FontInstalled);
            settings = settings with { Desk = settings.Desk with { Font = DeskDesignApply.FontSetting(new DeskFontChoice(stand)) } };
        }
        ThemeDefinition? theme = merged.Theme is { } t && r.Proposal.Theme is not null ? ThemeLint.Lint(t).Theme : null;

        try { host.ShowPreview(settings, theme, model, ApplyReview, DismissReview); }
        catch (Exception e) when (IsRecoverable(e)) { Log("preview", e); }
    }

    /// <summary>Dismiss (the button, or Escape): the Desk and widget go back at once, nothing was written, the caller is told.</summary>
    public void DismissReview()
    {
        if (_review is not { } r) return;
        _review = null;
        HostEnd();
        Answer(r.Id, new DeskAnswer(DeskAnswer.Rejected, "dismissed", [new DeskReason("design", "dismissed by the user")]));
    }

    /// <summary>Apply (the button, or Enter): saves the Desk it replaces, writes the design, tells the caller. Waits while a font is missing.</summary>
    public void ApplyReview()
    {
        if (_review is not { } r) return;
        if (r.Fonts.Any(f => !FontInstalled(f.Family))) return;   // Apply waits for the font
        _review = null;
        var merged = DeskDesign.Merge(CurrentDesign(), r.Proposal);
        var changes = DeskDesign.Diff(CurrentDesign(), merged, SettingsNames.DeskPieceName, MonitorName);
        var answer = ApplyDesign(r.Proposal, changes, r.Warnings);
        HostEnd();   // the preview gives way to what was written
        Answer(r.Id, answer);
    }

    private void Answer(string id, DeskAnswer answer)
    {
        if (_disposed || ResetGuard.Done) return;
        Handoff.Answer(id, answer);
    }

    // -- applying and saved designs ------------------------------------------------------------------------

    /// <summary>
    /// Writes a proposal over the Desk: the Desk it replaces is saved first (a saved design that is the same look is reused, else
    /// "Before &lt;name&gt;"), then the layout, font, plate, ink, Launcher, theme and messages are written, the new look is saved
    /// under the design's name, and "Put my Desk back" is pointed at the saved previous Desk.
    /// </summary>
    private DeskAnswer ApplyDesign(DeskDesign proposal, IReadOnlyList<string> changes, IReadOnlyList<DeskReason> warnings)
    {
        try { return ApplyDesignCore(proposal, changes, warnings); }
        catch (Exception e) when (IsRecoverable(e))
        {
            Log("apply", e);
            return DeskAnswer.Refused([new DeskReason("design", "could not be applied (" + e.GetType().Name + ")")]);
        }
    }

    private DeskAnswer ApplyDesignCore(DeskDesign proposal, IReadOnlyList<string> changes, IReadOnlyList<DeskReason> warnings)
    {
        var before = CurrentDesign();
        var merged = DeskDesign.Merge(before, proposal);
        var warn = new List<DeskReason>(warnings);
        var saved = SaveCurrentFirst(before, proposal.Name);
        if (saved is null)
            return DeskAnswer.Refused([new DeskReason("design", "the Desk it replaces could not be saved, so nothing was changed")]);

        string? themeKey;
        try
        {
            themeKey = WriteDesign(merged, proposal.Theme is not null);
            if (proposal.Messages is { } messages && !WriteMessages(messages)) warn.Add(new DeskReason("messages", "messages.txt could not be changed, so the messages were left alone"));
            if (proposal.Looks is { Count: > 0 } looks) warn.AddRange(SaveLooks(looks));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return DeskAnswer.Refused([new DeskReason("design", "could not be written (" + e.GetType().Name + ")")]);
        }

        var after = CurrentDesign();
        KeepApplied(after, proposal.Name);
        _store.SaveDeskPutBack(saved.Id);
        HostApplied(themeKey);
        AfterChange();
        return new DeskAnswer(DeskAnswer.Applied, Changes: changes.Count > 0 ? changes : null, Warnings: warn.Count > 0 ? warn : null);
    }

    /// <summary>Saves the Desk as it is, unless a saved design already is that look; returns the design to put back to. Null when it cannot be saved.</summary>
    private SavedDesign? SaveCurrentFirst(DeskDesign before, string? incomingName)
    {
        var existing = _designs.List();
        if (DeskDesigns.FindSame(existing, before) is { } same) return same;
        string name = DeskDesigns.BeforeName(incomingName);
        var design = new SavedDesign(SavedDesign.NewId(), name, false, Clock().AddSeconds(-1), before with { Name = name, Note = null });
        _designs.Add(design, _store.LoadLauncher());
        return _designs.Load(design.Id);
    }

    /// <summary>
    /// Saves the Desk as it is before a change Settings makes to it (theme match: Follow on, Match now), unless a saved design already is
    /// that look, and points "Put my Desk back" at it, as applying a design does. False when it could not be saved.
    /// </summary>
    public bool SaveBeforeChange(string incomingName)
    {
        if (_disposed || ResetGuard.Done) return false;   // a late call after Reset must not write into a cleared store
        try
        {
            if (SaveCurrentFirst(CurrentDesign(), incomingName) is not { } saved) return false;
            _store.SaveDeskPutBack(saved.Id);
            AfterChange();
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log("save before", e);
            return false;
        }
    }

    /// <summary>Keeps the look that was just written as a saved design: the one that is already that look (now the newest), or a new one.</summary>
    private void KeepApplied(DeskDesign after, string? name)
    {
        var existing = _designs.List();
        var launcher = _store.LoadLauncher();
        if (DeskDesigns.FindSame(existing, after) is { } same)
        {
            _designs.Add(same with { AppliedAt = Clock() }, launcher);
            return;
        }
        string title = DeskDesigns.NormalizeName(name) ?? "Claude's design";
        _designs.Add(new SavedDesign(SavedDesign.NewId(), title, false, Clock(), after with { Name = title, Note = null }), launcher);
    }

    /// <summary>Writes the settings a full design asks for. Returns the theme key now saved, null when the design brings no theme.</summary>
    private string? WriteDesign(DeskDesign merged, bool withTheme)
    {
        var desk = _store.LoadDesk();
        var ink = _store.LoadDeskInk();
        var launcher = _store.LoadLauncher();
        var next = DeskDesignApply.Settings(desk, ink, launcher, merged);
        if (next.Desk != desk) _store.SaveDesk(next.Desk);
        if (next.Desk.Plate != desk.Plate) _store.SaveDeskPlate(next.Desk.Plate);
        if (next.Ink != ink) _store.SaveDeskInk(next.Ink);
        if (next.Launcher.ToJson().ToJsonString() != launcher.ToJson().ToJsonString()) _store.SaveLauncher(next.Launcher);
        // Theme match: a design that says whether the Desk follows the theme sets it (Put my Desk back restores both sides).
        if (next.FollowsTheme is { } follows && follows != _store.LoadDeskFollowsTheme()) _store.SaveDeskFollowsTheme(follows);
        // The weather nickname (#315) is the one part of desk_weather a design may set; the place and the unit stay the user's.
        if (next.WeatherNickname is { } nickname)
        {
            var weather = _store.LoadDeskWeather();
            var nick = DeskWeatherSettings.NormalizeNickname(nickname);
            if (nick != weather.Nickname) _store.SaveDeskWeather(weather with { Nickname = nick });
        }

        if (!withTheme || merged.Theme is not { } theme) return null;
        var map = ThemeMap();
        var plan = DesignTheme.Plan(theme, k => map.TryGetValue(k, out var d) ? d : null);
        if (plan is null) return null;
        if (plan.Write)
        {
            Directory.CreateDirectory(_paths.ThemesDir);
            string path = Path.Combine(_paths.ThemesDir, plan.Key + ".json");
            string temp = path + ".tmp";
            File.WriteAllText(temp, theme.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, path, overwrite: true);
        }
        _store.SaveTheme(plan.Key);
        return plan.Key;
    }

    /// <summary>The design's song looks go into the user's saved looks (a song already there takes the new look). What could not be done, or
    /// that Style what's playing is off so they will not show yet, comes back as warnings.</summary>
    private IEnumerable<DeskReason> SaveLooks(IReadOnlyList<SongLook> looks)
    {
        if (Looks is not { } service)
            return [new DeskReason("looks", "could not be saved here, so the song looks were left alone")];
        if (!service.Handoff.Save(looks))
            return [new DeskReason("looks", "the saved looks could not be written, so the song looks were left alone")];
        return service.StyleOn
            ? []
            : [new DeskReason("looks", $"saved {looks.Count} song look{(looks.Count == 1 ? "" : "s")}, but Style what's playing is off, so they show once it is on (Settings, Now playing, Looks)")];
    }

    /// <summary>The design's message lines go into messages.txt the way Add or Replace does it, keeping the old file as messages.txt.previous.</summary>
    private bool WriteMessages(DeskMessagesChange change)
    {
        string path = Path.Combine(_paths.AppDataDir, "messages.txt");
        bool had = File.Exists(path);
        string existing = "";
        if (had)
        {
            if (MessageFileIo.ReadText(path) is not { } text) return false;
            existing = text;
        }
        var proposal = new MessageProposal("design", Clock(), change.Lines, change.Mode, null);
        if (MessageProposal.Apply(proposal, existing) is not { } applied) return false;
        try
        {
            if (had) MessageFileIo.WriteAtomic(path + ".previous", MessageFileIo.Encode(existing));
            MessageFileIo.WriteAtomic(path, MessageFileIo.Encode(applied.Text));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The host (the Desk, the widget, the bar) failed while following a change that is already written: logged, never fatal.</summary>
    private void HostApplied(string? themeKey)
    {
        try { Host?.Applied(themeKey); }
        catch (Exception e) when (IsRecoverable(e)) { Log("refresh", e); }
    }

    private void HostEnd()
    {
        try { Host?.EndPreview(); }
        catch (Exception e) when (IsRecoverable(e)) { Log("end preview", e); }
    }

    /// <summary>Anything but a process-ending condition: a design applying must never terminate the widget.</summary>
    private static bool IsRecoverable(Exception e) => e is not (OutOfMemoryException or StackOverflowException or AccessViolationException);

    /// <summary>One line in the log: the operation and the exception type, never design content (PRIVACY.md).</summary>
    private void Log(string operation, Exception e)
    {
        try { File.AppendAllText(_paths.LogFile, $"{DateTime.UtcNow:o} desk design {operation} failed ({e.GetType().Name}){Environment.NewLine}"); }
        catch (Exception) { /* the log is a courtesy */ }
    }

    private void AfterChange()
    {
        DesignsChanged?.Invoke();
        Refresh();
    }

    /// <summary>The saved designs, newest first.</summary>
    public IReadOnlyList<SavedDesign> Designs() => _designs.List();

    /// <summary>True when a saved design exists for "Put my Desk back".</summary>
    public bool CanPutBack => _store.LoadDeskPutBack() is { } id && _designs.Load(id) is not null;

    /// <summary>Switches the Desk to a saved design: the Desk it replaces is saved first (unless a saved design already is that look),
    /// then the design is written and "Put my Desk back" points at what was replaced. A Launcher image that went is dropped from its button.</summary>
    public DesignActionResult SwitchTo(string id)
    {
        try { return SwitchToCore(id); }
        catch (Exception e) when (IsRecoverable(e))
        {
            Log("switch", e);
            return new(false, "That did not work (" + e.GetType().Name + "). Your Desk was left as it is.");
        }
    }

    private DesignActionResult SwitchToCore(string id)
    {
        if (_disposed || ResetGuard.Done) return new(false, "Sanduhr is closing.");
        var target = _designs.Load(id);
        if (target is null) return new(false, "That design is gone.");
        if (_review is not null) DismissReview();

        var design = DeskDesigns.WithoutMissingImages(target.Design, _designs.ImageExists);
        // A saved design is a whole Desk: no follows_theme is one that does not follow (theme match).
        design = design with { FollowsTheme = design.FollowsTheme ?? false };
        var before = CurrentDesign();
        if (DeskDesigns.FindSame([target], before) is not null) return new(true, $"{target.Name} is your Desk already.");

        var previous = SaveCurrentFirst(before, target.Name);
        if (previous is null) return new(false, "The Desk could not be saved first, so nothing was changed.");
        string? themeKey;
        try
        {
            themeKey = WriteDesign(DeskDesign.Merge(before, design with { Name = null, Note = null }), withTheme: design.Theme is not null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new(false, "The design could not be written (" + e.GetType().Name + ").");
        }
        _designs.Add(target with { AppliedAt = Clock() }, _store.LoadLauncher());
        _store.SaveDeskPutBack(previous.Id);
        HostApplied(themeKey);
        AfterChange();
        return new(true, $"Switched to {target.Name}.");
    }

    /// <summary>"Put my Desk back": switches to the design saved before the last change.</summary>
    public DesignActionResult PutBack()
    {
        if (_store.LoadDeskPutBack() is not { } id || _designs.Load(id) is null)
            return new(false, "There is nothing to put back yet.");
        var result = SwitchTo(id);
        return result.Ok ? new(true, "Your Desk is back as it was.") : result;
    }

    public DesignActionResult Rename(string id, string? name)
    {
        var d = _designs.Load(id);
        if (d is null) return new(false, "That design is gone.");
        if (DeskDesigns.NormalizeName(name) is not { } clean) return new(false, "A design needs a name.");
        _designs.Save(d with { Name = clean, Design = d.Design with { Name = clean } });
        AfterChange();
        return new(true, $"Renamed to {clean}.");
    }

    public DesignActionResult Pin(string id, bool pinned)
    {
        var d = _designs.Load(id);
        if (d is null) return new(false, "That design is gone.");
        _designs.Save(d with { Pinned = pinned });
        AfterChange();
        return new(true, pinned ? $"{d.Name} is pinned. It is never dropped." : $"{d.Name} is not pinned.");
    }

    public DesignActionResult Delete(string id)
    {
        var d = _designs.Load(id);
        if (d is null) return new(false, "That design is gone.");
        _designs.Delete(id, _store.LoadLauncher());
        if (_store.LoadDeskPutBack() == id) _store.SaveDeskPutBack(null);
        AfterChange();
        return new(true, $"Deleted {d.Name}.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _settle.Stop();
        SettingsStore.AnyWritten -= OnSettingsWritten;
        if (_apps is not null) _apps.Changed -= Refresh;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        SystemEvents.InstalledFontsChanged -= OnFontsChanged;
        _watcher?.Dispose();
        _watcher = null;
        if (_review is not null) HostEnd();
        _review = null;
        (Host as IDisposable)?.Dispose();
    }
}
