namespace Sanduhr.Core;

/// <summary>A sidebar group: its heading (none for the first group) and its pages, in order.</summary>
public sealed record SettingsGroup(string? Title, IReadOnlyList<SettingsPage> Pages);

/// <summary>
/// One name per control (Settings v2; the Mac's SettingsNames.swift). A control has the same name in
/// Settings, the tray and widget menus, the tour and What's New. Settings and the menus read their
/// labels from here, and <c>SettingsNamesTests</c> checks the cards, the tour and the app's source
/// against <see cref="Retired"/>, the names a control used to go by.
///
/// Casing: the words follow the Mac, the casing follows Windows. Commands and control labels are
/// sentence case ("Take the tour…", "Sign in"), as Windows writes its own. Proper names keep their
/// capitals: Claude, Claude Code, Sanduhr, What's New (the window's name), Deep Work, Cooldown Snake.
/// Page titles stay as they shipped ("Claude Usage", "Publish usage"): PRIVACY.md and the Store copy
/// quote them.
/// </summary>
public static class SettingsNames
{
    // ---- Pages ----

    /// <summary>The sidebar: six groups, each page once. Widget is not a page (Show me on a widget card
    /// shows the widget itself).</summary>
    public static IReadOnlyList<SettingsGroup> Sidebar { get; } =
    [
        new(null, [SettingsPage.General, SettingsPage.Accounts, SettingsPage.Alerts]),
        new("Usage", [SettingsPage.History, SettingsPage.ClaudeUsage, SettingsPage.Publish]),
        new("Claude Code", [SettingsPage.ClaudeCode, SettingsPage.Mods]),
        new("Widget", [SettingsPage.Themes]),
        new("Desk", [SettingsPage.Desk, SettingsPage.DeskMessage, SettingsPage.NowPlaying, SettingsPage.Weather]),
        new("Help", [SettingsPage.About]),
    ];

    /// <summary>Every page in sidebar order.</summary>
    public static IReadOnlyList<SettingsPage> Pages { get; } = [.. Sidebar.SelectMany(g => g.Pages)];

    /// <summary>A page's sidebar title. Widget, which is not a page, reads "Widget".</summary>
    public static string PageTitle(SettingsPage page) => page switch
    {
        SettingsPage.General => "General",
        SettingsPage.Accounts => "Accounts",
        SettingsPage.Alerts => "Alerts",
        SettingsPage.History => "History",
        SettingsPage.ClaudeUsage => "Claude Usage",
        SettingsPage.Publish => "Publish usage",
        SettingsPage.ClaudeCode => "Claude Code",
        SettingsPage.Mods => "Mods & Config",
        SettingsPage.Themes => "Themes",
        SettingsPage.Desk => "Desk",
        SettingsPage.DeskMessage => "Message",
        SettingsPage.NowPlaying => "Now playing",
        SettingsPage.Weather => "Weather",
        SettingsPage.About => "About",
        SettingsPage.Widget => "Widget",
        _ => page.ToString(),
    };

    /// <summary>The group heading a page sits under, null for the first group.</summary>
    public static string? GroupTitle(SettingsPage page) =>
        Sidebar.FirstOrDefault(g => g.Pages.Contains(page))?.Title;

    /// <summary>The label of a button or menu item that opens a page: "Alerts settings…". A button
    /// that opens Settings names the page it opens.</summary>
    public static string LinkTitle(SettingsPage page) => $"{PageTitle(page)} settings…";

    /// <summary>The page a "<c>Page</c> settings…" label opens, null when it names no page.</summary>
    public static SettingsPage? Linked(string title)
    {
        foreach (var page in Pages)
        {
            if (LinkTitle(page) == title)
                return page;
        }
        return null;
    }

    // ---- Commands and controls ----

    /// <summary>Tray and widget menus: opens Settings where it was left.</summary>
    public const string OpenSettings = "Settings…";
    /// <summary>About, and the tray menu: the What's New window. "What's New" is the window's name.</summary>
    public const string WhatsNew = "What's New…";
    /// <summary>About, and the tray menu: the welcome tour again.</summary>
    public const string TakeTheTour = "Take the tour…";
    /// <summary>General's picker and the tray menu's submenu: what the tray icon's number follows.</summary>
    public const string TrayShows = "Tray shows";
    /// <summary>The tray menu while demo mode runs.</summary>
    public const string EndDemoMode = "End demo mode";
    /// <summary>The signed-out widget and the tour's first step before sign-in.</summary>
    public const string SignIn = "Sign in";
    /// <summary>Accounts, the tray menu and the widget's Accounts submenu.</summary>
    public const string AddAccount = "Add account…";
    /// <summary>General, Shortcuts: the switch's name wherever the keys can't be shown.</summary>
    public const string ShortcutToOpenSettings = "Shortcut to open Settings";
    /// <summary>A limit card's menu.</summary>
    public const string HideUntilItResets = "Hide until it resets";
    public const string HiddenLimits = "Hidden limits";
    public const string StopWarnings = "Stop warnings for this limit";
    public const string WarnAgain = "Warn again for this limit";
    /// <summary>Alerts.</summary>
    public const string SendTestAlert = "Send test alert";
    /// <summary>History and Claude Usage.</summary>
    public const string ExportCsv = "Export CSV…";
    /// <summary>Themes (paste box and Studio).</summary>
    public const string SaveAndApply = "Save & apply";
    /// <summary>About: where Sanduhr keeps its files.</summary>
    public const string WhereSettingsLive = "Where settings live";
    /// <summary>About.</summary>
    public const string ThirdPartyNotices = "Third-party notices";
    /// <summary>About: removes what Sanduhr keeps on this PC (spec 4.7).</summary>
    public const string ResetSanduhr = "Reset Sanduhr…";

    /// <summary>The Quit button at the foot of the sidebar (not on a page, so not in <see cref="Table"/>).</summary>
    public const string QuitSanduhr = "Quit Sanduhr";
    /// <summary>Claude Code: picks a folder Sanduhr did not find.</summary>
    public const string AddFolder = "Add folder…";
    /// <summary>Claude Code: drops a folder that was added by hand.</summary>
    public const string ForgetFolder = "Forget folder";
    /// <summary>Accounts: the follow switch, and the tray's account list marks the account in use.</summary>
    public const string FollowAccount = "Follow the account I'm using";
    /// <summary>Mods &amp; Config: re-reads the folder.</summary>
    public const string RefreshFolder = "Refresh";
    /// <summary>Desk: the switch at the top of the page (settings.json "desk_enabled", off by default).</summary>
    public const string DeskSwitch = "Desk: the clock and your Claude meters on the desktop";
    /// <summary>Desk, Where each piece sits: the pieces, named as on the Mac.</summary>
    public const string DeskClock = "Clock and date";
    public const string DeskClaudeLine = "Claude meters (line)";
    public const string DeskClaudeBars = "Claude meters (bars)";
    /// <summary>Desk, Where each piece sits: the S6 pieces (spec 6.10).</summary>
    public const string DeskMessage = "Message";
    public const string DeskNowPlaying = "Now playing";
    public const string DeskTelemetry = "System (CPU, RAM, disk)";
    public const string DeskWeather = "Weather";
    /// <summary>Desk, Where each piece sits: the glow's spot (spec 7.3), named as Arrange mode labels it.</summary>
    public const string DeskGlow = "Claude Code glow";
    /// <summary>Desk, Where each piece sits: the watchers (spec 6.9), live cards for work an agent is watching.</summary>
    public const string DeskWatchers = "Watchers";
    /// <summary>Desk: lets agents put watchers up through the MCP server (settings.json "watchers_agents", off by default).</summary>
    public const string WatchersSwitch = "Let agents show watchers";
    /// <summary>Desk: starts Arrange mode on the desktop (spec 7.3).</summary>
    public const string ArrangeDesk = DeskArrangeCopy.SettingsButton;
    /// <summary>Desk: the clock, message and Claude line take clicks (spec 7.4).</summary>
    public const string DeskPieceClicksSwitch = DeskPieceClicks.Title;
    /// <summary>Desk, Look: a soft plate behind each piece.</summary>
    public const string DeskPlateSwitch = DeskPlate.Title;
    /// <summary>The name a Desk piece goes by on the layout map and in Arrange mode; an unknown piece goes by its key.</summary>
    public static string DeskPieceName(string widget) => widget switch
    {
        "message" => DeskMessage,
        "clock" => DeskClock,
        "claude" => DeskClaudeLine,
        "meters" => DeskClaudeBars,
        "nowPlaying" => DeskNowPlaying,
        "telemetry" => DeskTelemetry,
        "weather" => DeskWeather,
        "glow" => DeskGlow,
        "watchers" => DeskWatchers,
        _ => widget,
    };
    /// <summary>Desk, Look: the ink the Desk draws in (spec 6.7).</summary>
    public const string DeskInk = "Ink";
    /// <summary>Alerts: the Desk's sign-in reminder switch (settings.json "desk_sign_in_reminder", on by default).</summary>
    public const string SignInReminders = SignInReminder.Title;
    /// <summary>The first-run card's third action (settings.json "just_the_desk"); it has no Settings control.</summary>
    public const string JustTheDeskButton = JustTheDesk.ButtonTitle;
    /// <summary>Now playing: the widget's line (settings.json "now_playing.widget", off by default).</summary>
    public const string NowPlayingInWidget = "Show what's playing in the widget";
    /// <summary>Weather: looks a typed city up with Open-Meteo.</summary>
    public const string WeatherLookUp = "Look up";
    /// <summary>Claude Code: where the glow lands (settings.json "claude_code_glow.target").</summary>
    public const string GlowWhere = "Where the glow shows";

    /// <summary>The trademark disclaimer, the standard text, word for word (About carries it).</summary>
    public const string TrademarkNotice =
        "\"Claude\" and \"claude.ai\" are trademarks of Anthropic PBC, used nominatively. "
        + "Sanduhr für Claude is an independent third-party tool, not affiliated with, endorsed by, "
        + "or associated with Anthropic PBC.";

    /// <summary>Every control name with the page it lives on (menu-only commands have none).</summary>
    public static IReadOnlyList<(string Name, SettingsPage Page)> Table { get; } =
    [
        (TrayShows, SettingsPage.General),
        (ShortcutToOpenSettings, SettingsPage.General),
        (AddAccount, SettingsPage.Accounts),
        (SendTestAlert, SettingsPage.Alerts),
        (ExportCsv, SettingsPage.History),
        (SaveAndApply, SettingsPage.Themes),
        (WhatsNew, SettingsPage.About),
        (TakeTheTour, SettingsPage.About),
        (WhereSettingsLive, SettingsPage.About),
        (ThirdPartyNotices, SettingsPage.About),
        (ResetSanduhr, SettingsPage.About),
        (AddFolder, SettingsPage.ClaudeCode),
        (FollowAccount, SettingsPage.Accounts),
        (DeskSwitch, SettingsPage.Desk),
        (DeskInk, SettingsPage.Desk),
        (ArrangeDesk, SettingsPage.Desk),
        (DeskPieceClicksSwitch, SettingsPage.Desk),
        (DeskPlateSwitch, SettingsPage.Desk),
        (WatchersSwitch, SettingsPage.Desk),
        (SignInReminders, SettingsPage.Alerts),
        (NowPlayingInWidget, SettingsPage.NowPlaying),
        (WeatherLookUp, SettingsPage.Weather),
        (GlowWhere, SettingsPage.ClaudeCode),
    ];

    /// <summary>
    /// Names that are gone, each with the one that replaced it. None may appear in a menu, the tour,
    /// What's New or a Settings label. <c>Whole</c> names match only a whole string ("Add account"
    /// is retired, "Add account…" is not); the rest match anywhere in a string, case included.
    /// </summary>
    public static IReadOnlyList<(string Old, string Now, bool Whole)> Retired { get; } =
    [
        ("Take the Tour", TakeTheTour, false),
        ("Tray Shows", TrayShows, false),
        ("End Demo Mode", EndDemoMode, false),
        ("Sign In", SignIn, true),
        ("Add account", AddAccount, true),
        ("Manage accounts…", LinkTitle(SettingsPage.Accounts), false),
        ("Alerts Settings…", LinkTitle(SettingsPage.Alerts), false),
        ("Save and apply", SaveAndApply, false),
        ("Save & Apply", SaveAndApply, false),
    ];

    /// <summary>The retired names <paramref name="text"/> still uses, empty when it uses none.</summary>
    public static IReadOnlyList<string> RetiredNames(string text)
    {
        if (string.IsNullOrEmpty(text))
            return [];
        return [.. Retired
            .Where(r => r.Whole ? text == r.Old : text.Contains(r.Old, StringComparison.Ordinal))
            .Select(r => r.Old)];
    }

    /// <summary>
    /// Every "<c>Page</c> settings…" in <paramref name="text"/> whose page has no sidebar title, empty
    /// when each names a real page. The page is the run of words right before " settings…" that
    /// starts with a capital ("Claude Usage settings…", "Publish usage settings…"); a lone
    /// "Settings…" and Windows' own "Windows Startup settings…" pass.
    /// </summary>
    public static IReadOnlyList<string> UnknownLinks(string text)
    {
        const string suffix = " settings…";
        var found = new List<string>();
        if (string.IsNullOrEmpty(text))
            return found;
        int at = 0;
        while ((at = text.IndexOf(suffix, at, StringComparison.Ordinal)) >= 0)
        {
            var before = text[..at];
            var title = LongestPageTitleEnding(before);
            if (title is null)
            {
                // Name the capitalised word run it ends with, so the failure says what it saw.
                var words = before.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var last = words.Length > 0 ? words[^1].TrimStart('"', '(', '“') : "";
                if (last.Length > 0 && char.IsUpper(last[0]) && !IsWindowsOwn(before))
                    found.Add(last + suffix);
            }
            at += suffix.Length;
        }
        return found;
    }

    private static string? LongestPageTitleEnding(string before)
    {
        string? best = null;
        foreach (var page in Pages)
        {
            var title = PageTitle(page);
            if (before.EndsWith(title, StringComparison.Ordinal)
                && (before.Length == title.Length || !char.IsLetter(before[^(title.Length + 1)]))
                && (best is null || title.Length > best.Length))
                best = title;
        }
        return best;
    }

    /// <summary>Links into Windows' own Settings app, which are not Sanduhr pages.</summary>
    private static bool IsWindowsOwn(string before) =>
        before.EndsWith("Windows Startup", StringComparison.Ordinal)
        || before.EndsWith("Notification", StringComparison.Ordinal);
}
