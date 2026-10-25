using System.Globalization;
using System.Text;

namespace Sanduhr.Core;

/// <summary>
/// One searchable place in Settings: a page (no anchor), a section or a control. The anchor is
/// page-local and marks the element Settings scrolls to. <paramref name="Subsection"/> names the
/// Claude Usage view (Overview, Trends, Sessions) the anchor sits in, null elsewhere.
/// <paramref name="CollapsedIn"/> names the collapsed section it sits in ("Advanced"), so a search
/// result can say so and opening it can unfold the section; null while it is always shown.
/// </summary>
public sealed record SettingsEntry(
    string Title,
    SettingsPage Page,
    string? Anchor,
    IReadOnlyList<string> Synonyms,
    string? Subsection = null,
    string? CollapsedIn = null);

/// <summary>A place in Settings: a page and, optionally, an anchor on it (spec, "Deep links and entry points").</summary>
public sealed record SettingsLink(SettingsPage Page, string? Anchor);

/// <summary>
/// Settings v2's reach (the Mac's SettingsIndex.swift): every section's anchor, the sidebar search
/// index built from the names table, and prefix-and-word matching. Settings marks each anchor in
/// its XAML; <c>SettingsIndexTests</c> checks every anchor here is marked there.
/// </summary>
public static class SettingsIndex
{
    // General
    public const string Tray = "tray";
    public const string Startup = "startup";
    public const string Shortcuts = "shortcuts";
    /// <summary>Quit Sanduhr sits at the foot of the sidebar, not on General; search focuses it there.</summary>
    public const string Quit = "quit";
    // Accounts
    public const string AccountList = "accounts";
    public const string Follow = "follow";
    // Alerts
    public const string Notifications = "notifications";
    public const string Thresholds = "thresholds";
    public const string Sound = "sound";
    public const string TestAlert = "test";
    public const string SignInReminders = "signin-reminders";
    // History
    public const string Chart = "chart";
    public const string HistoryExport = "export";
    // Claude Usage
    public const string Overview = "overview";
    public const string Vault = "vault";
    public const string Trends = "trends";
    public const string Sessions = "sessions";
    // Claude Code
    public const string Folders = "folders";
    public const string Statusline = "statusline";
    public const string Mcp = "mcp";
    public const string Meters = "meters";
    public const string Glow = "glow";
    // Mods & Config
    public const string ConfigFiles = "config";
    public const string ModList = "mods";
    public const string McpServers = "mcpservers";
    public const string ClaudeMd = "claudemd";
    public const string Retention = "retention";
    // Publish usage
    public const string Destination = "destination";
    public const string Token = "token";
    public const string Homes = "homes";
    public const string Schedule = "schedule";
    // Themes
    public const string Gallery = "gallery";
    public const string Studio = "studio";
    public const string Paste = "paste";
    public const string Installed = "installed";
    // Desk
    public const string DeskOn = "desk";
    public const string DeskPieces = "pieces";
    public const string DeskLook = "look";
    public const string DeskMargins = "margins";
    public const string DeskMap = "map";
    public const string DeskInk = "ink";
    public const string DeskArrange = "arrange";
    public const string DeskClicks = "desk-clicks";
    public const string DeskPlate = "desk-plate";
    public const string DeskWatchers = "desk-watchers";
    public const string DeskLauncher = "desk-launcher";
    public const string DeskDirect = "desk-direct";
    public const string DeskOled = "desk-oled";
    public const string DeskDesigns = "desk-designs";
    // Desk: Message, Now playing, Weather (S6)
    public const string MessageLines = "lines";
    public const string MessageRotation = "rotation";
    public const string MessageSpecial = "special";
    public const string MessageLook = "message-look";
    public const string MessageSuggestion = "suggestion";
    public const string NowPlayingWidget = "np-widget";
    public const string NowPlayingApps = "np-apps";
    public const string NowPlayingBars = "np-bars";
    public const string NowPlayingLooks = "np-looks";
    public const string WeatherPlace = "weather-place";
    public const string WeatherCredit = "weather-credit";
    // Desk: Widgets (3.8)
    public const string WidgetsCatalog = "widgets-catalog";
    public const string WidgetsCompanion = "widgets-companion";
    public const string WidgetsInstalled = "widgets-installed";
    // Claude Code (S6)
    public const string GlowWhere = "glow-where";
    // About
    public const string Version = "version";
    public const string Trademark = "trademark";
    public const string Notices = "notices";
    public const string Where = "where";
    public const string Reset = "reset";

    /// <summary>The Claude Usage views an anchor can sit in.</summary>
    public const string OverviewView = "Overview";
    public const string TrendsView = "Trends";
    public const string SessionsView = "Sessions";

    private static SettingsEntry E(string title, SettingsPage page, string anchor, string[]? synonyms = null,
        string? subsection = null) => new(title, page, anchor, synonyms ?? [], subsection);

    /// <summary>Every anchor in sidebar order, with its title and the words a person might type for it.</summary>
    public static IReadOnlyList<SettingsEntry> Anchors { get; } =
    [
        E(SettingsNames.TrayShows, SettingsPage.General, Tray,
            ["tray", "percent", "number", "icon", "rotate", "session", "weekly", "highest", "whichever is higher"]),
        E("Startup", SettingsPage.General, Startup,
            ["start with Windows", "sign-in", "login", "launch", "auto-start", "Startup apps"]),
        E("Shortcuts", SettingsPage.General, Shortcuts,
            [SettingsNames.ShortcutToOpenSettings, "hotkey", "keyboard", "keys", "Ctrl+Alt+S", "record", "global shortcut"]),
        E(SettingsNames.QuitSanduhr, SettingsPage.General, Quit,
            ["quit", "exit", "close", "close Sanduhr", "stop", "shut down", "end", "sidebar"]),

        E("Accounts", SettingsPage.Accounts, AccountList,
            ["sign in", "sign out", "add account", "rename", "update sign-in", "remove account", "switch",
             "active account", "sessionKey", "session key", "cookie", "cf_clearance"]),
        E(SettingsNames.FollowAccount, SettingsPage.Accounts, Follow,
            ["follow", "auto switch", "in use", "switch automatically", "every 15 minutes"]),

        E("Alerts", SettingsPage.Alerts, Notifications,
            ["notification", "toast", "enable alerts", "projection", "pace", "reset notifications", "Focus Assist"]),
        E("Warn at and Urgent at", SettingsPage.Alerts, Thresholds, ["threshold", "warn", "urgent", "percent"]),
        E("Sound", SettingsPage.Alerts, Sound, ["chime", "sound", "snake", "model-scoped", "mute"]),
        E(SettingsNames.SendTestAlert, SettingsPage.Alerts, TestAlert, ["test", "try"]),
        E(SettingsNames.SignInReminders, SettingsPage.Alerts, SignInReminders,
            ["sign in", "signed out", "without Claude", "reminder", "Just the Desk", "session expired"]),

        E("History chart", SettingsPage.History, Chart,
            ["meter history", "30 days", "week", "month", "account", "all accounts", "chart"]),
        E(SettingsNames.ExportMeterHistory, SettingsPage.History, HistoryExport, ["csv", "export", "export csv", "clear history", "spreadsheet"]),

        E(OverviewView, SettingsPage.ClaudeUsage, Overview,
            ["Claude Code tokens", "today", "last 30 days", "calendar", "projects", "skills", "breakdown"], OverviewView),
        E(SettingsNames.SessionArchive, SettingsPage.ClaudeUsage, Vault,
            ["history vault", "usage vault", "vault", "archive", "erase archive", "open vault folder", "homes", ".claude"], OverviewView),
        E(TrendsView, SettingsPage.ClaudeUsage, Trends, ["weeks", "4 weeks", "12 weeks", "26 weeks", "trend"], TrendsView),
        E(SessionsView, SettingsPage.ClaudeUsage, Sessions,
            ["ledger", "sessions", "models", "yesterday", "7 days", "stack by project", "csv"], SessionsView),

        E("Folders", SettingsPage.ClaudeCode, Folders,
            ["folder", "seat", "home", ".claude", "add folder", "forget folder", "linked account", "account folder", "CLAUDE_CONFIG_DIR"]),
        E("Statusline", SettingsPage.ClaudeCode, Statusline,
            ["status line", "prompt", "install statusline", "remove statusline", "segments", "combine", "replace", "meters"]),
        E("MCP server", SettingsPage.ClaudeCode, Mcp,
            ["MCP", "sanduhr-mcp", "agents", "tools", "burn attribution", "project totals", "quota tools"]),
        E("Meters above the prompt", SettingsPage.ClaudeCode, Meters,
            ["mod", "plugin", "meters", "band", "sanduhr-meters", "CLAUDE_CODE_PLUGIN_DIRS"]),
        E(SettingsNames.ClaudeCodeGlowHook, SettingsPage.ClaudeCode, Glow,
            ["glow when Claude needs you", "glow", "hooks", "notification", "waiting", "finishes", "edge light", "terminal", "try it"]),
        E(SettingsNames.GlowWhere, SettingsPage.ClaudeCode, GlowWhere,
            ["where the glow shows", "screen edge", "monitor", "flash", "taskbar flash", "glow target"]),

        E("Settings and statusline", SettingsPage.Mods, ConfigFiles,
            ["settings.json", "folder", "hooks", "statusline", "readable", "unreadable", "refresh", "inventory"]),
        E("Plugins", SettingsPage.Mods, ModList,
            ["mod", "mods", "plugin", "plugins", "installed", "enabledPlugins", "sanduhr-meters", "switch", "on", "off", "update", "remove"]),
        E("MCP servers", SettingsPage.Mods, McpServers, ["MCP", "servers", "mcpServers", "tools"]),
        E("CLAUDE.md", SettingsPage.Mods, ClaudeMd, ["memory", "instructions", "lines", "size"]),
        E(SettingsNames.TranscriptsKept, SettingsPage.Mods, Retention,
            ["history kept", "retention", "cleanupPeriodDays", "transcripts", "30 days", "delete", "keep"]),

        E("Destination", SettingsPage.Publish, Destination,
            ["endpoint", "URL", "preset", "626 Labs", "dashboard", "auth", "header"]),
        E("Token", SettingsPage.Publish, Token, ["set token", "clear token", "credential", "bearer"]),
        E(SettingsNames.FoldersToShare, SettingsPage.Publish, Homes, ["folders to share", "homes to share", "homes", "folders", "share", ".claude"]),
        E("Publish daily", SettingsPage.Publish, Schedule, ["daily", "schedule", "time", "publish now"]),

        E("Choose a theme", SettingsPage.Themes, Gallery, ["theme", "color", "colour", "gallery", "palette"]),
        E("Studio", SettingsPage.Themes, Studio, ["edit tokens", "glass alpha", "border alpha", "custom theme"]),
        E("Paste a theme", SettingsPage.Themes, Paste,
            [SettingsNames.SaveAndApply, "json", "import", "Copy agent prompt", "themes folder"]),
        E("Installed user themes", SettingsPage.Themes, Installed, ["delete theme", "reload", "user themes"]),

        E(SettingsNames.DeskSwitch, SettingsPage.Desk, DeskOn,
            ["desktop", "wallpaper", "clock", "meters", "Rainmeter", "show the Desk", "on the desktop", "off by default", "monitors"]),
        E(SettingsNames.ArrangeDesk, SettingsPage.Desk, DeskArrange,
            ["drag", "move", "rearrange", "arrange", "reorder", "on the desktop", "layout"]),
        E(SettingsNames.DeskPieceClicksSwitch, SettingsPage.Desk, DeskClicks,
            ["click", "click-through", "right-click", "menu", "desktop icons", "clock", "message"]),
        E(SettingsNames.WatchersSwitch, SettingsPage.Desk, DeskWatchers,
            ["watcher", "watchers", "agent", "agents", "MCP", "watch_start", "CI", "deploy", "progress", "card", "claude code",
             SettingsNames.CiRunnersSwitch, "runner", "runners", "GitHub Actions", "Docker", "jobs", "off by default"]),
        E(SettingsNames.DeskDirectSwitch, SettingsPage.Desk, DeskDirect,
            ["Claude", "agent", "agents", "MCP", "propose_desk", "design", "designs", "review", "apply", "without asking", "wallpaper", "off by default"]),
        E(SettingsNames.OledCareSwitch, SettingsPage.Desk, DeskOled,
            ["oled", "burn-in", "burn in", "burnin", "drift", "dim", "idle", "away", "screen", "pixels", "white", "image retention", "off by default"]),
        E(SettingsNames.DeskDesigns, SettingsPage.Desk, DeskDesigns,
            ["design", "designs", "undo", "put back", SettingsNames.PutMyDeskBack, "restore", "switch", "switch to", "rename", "pin", "delete", "saved looks", "Before", "history", "Claude"]),
        E(SettingsNames.DeskLauncher, SettingsPage.Desk, DeskLauncher,
            ["launcher", "button", "buttons", "shortcut", "shortcuts", "app", "apps", "link", "TouchButton", "Rainmeter",
             SettingsNames.LauncherAddApp, SettingsNames.LauncherAddLink, "image", "label", "PNG", "Chrome app", "PWA"]),
        E("Where each piece sits", SettingsPage.Desk, DeskPieces,
            ["layout", "place", "corner", "position", "size", "hidden", "clock", "meters", "Claude line", "taskbar", "anchor", "monitor", "monitors", "screen", "display", "main monitor", "all monitors", "second monitor", "per monitor",
             "style", "meter style", "slim", "ring", "rings", "gauge", "ticks", "segments", "Roundline", "compact"]),
        E("Look", SettingsPage.Desk, DeskLook,
            ["font", "EsteFont", "EsteFont Pro", "EsteFont 26", "clock size", "time size", "typeface", "Segoe UI"]),
        E(SettingsNames.DeskPlateSwitch, SettingsPage.Desk, DeskPlate,
            ["plate", "backdrop", "background", "contrast", "readable", "desktop icons", "files", "dark plate", "behind the text"]),
        E("Margins", SettingsPage.Desk, DeskMargins,
            ["margin", "edge", "padding", "taskbar", "inset", "spacing"]),
        E("Layout map", SettingsPage.Desk, DeskMap,
            ["map", "preview", "layout", "taskbar", "screen", "monitor", "where"]),
        E(SettingsNames.DeskInk, SettingsPage.Desk, DeskInk,
            ["ink", "text color", "automatic", "light", "dark", "wallpaper", "readable", "shadow", "gradient", "hex"]),

        E("Claude's suggestion", SettingsPage.DeskMessage, MessageSuggestion,
            ["suggestion", "suggested", "Claude", "agent", "review", "dismiss", "propose"]),
        E("Your lines", SettingsPage.DeskMessage, MessageLines,
            ["messages.txt", "message", "edit", "add line", "effects", "glow", "write", "sweep", "shimmer", "font", "ink", "edit as text"]),
        E("Rotation", SettingsPage.DeskMessage, MessageRotation, ["rotate", "daily", "hourly", "pin", "mix", "every day"]),
        E("On special days", SettingsPage.DeskMessage, MessageSpecial,
            ["special day", "birthday", "date", "stack", "take turns", "scroll", "ticker"]),
        E("Message look", SettingsPage.DeskMessage, MessageLook, ["message size", "message color", "message font", "glow"]),

        E(SettingsNames.NowPlayingInWidget, SettingsPage.NowPlaying, NowPlayingWidget,
            ["now playing", "music", "song", "track", "Spotify", "media", "play", "pause", "next", "keep showing while paused", "hide while paused", "hide when nothing plays"]),
        E("Apps", SettingsPage.NowPlaying, NowPlayingApps, ["apps", "exclude", "browser", "player"]),
        E("Bars", SettingsPage.NowPlaying, NowPlayingBars, ["visualizer", "bars", "audio", "level", "equalizer", "peak caps", "gradient",
            SettingsNames.NowPlayingBarsGradient]),
        E("Looks", SettingsPage.NowPlaying, NowPlayingLooks,
            ["looks", "song style", "style", "palette", "gradient", "letter style", "small caps", "bold", "italic", "mood", "saved looks",
             "propose_now_playing_looks", "Claude styles songs", "suggested looks", "song colors"]),

        E(SettingsNames.WeatherLookUp, SettingsPage.Weather, WeatherPlace,
            ["weather", "city", "forecast", "temperature", "Fahrenheit", "Celsius", "Open-Meteo", "location", "nickname", "display name",
             SettingsNames.WeatherShowAs]),
        E("Weather data by Open-Meteo.com", SettingsPage.Weather, WeatherCredit, ["Open-Meteo", "credit", "attribution", "GeoNames"]),

        E("Desk companion", SettingsPage.Widgets, WidgetsCompanion,
            ["companion", "pet", "sprite", "hourglass", "roam", "walk", "grow", "growth", "stage", "mood", SettingsNames.WidgetCompanionShow,
             SettingsNames.WidgetCompanionRoam]),
        E("Widget catalog", SettingsPage.Widgets, WidgetsCatalog,
            ["widgets", "catalog", "install", "download", "sprite", "companion", "pet", "palette", "effects", "font", "theme pack",
             "hourglass", "OLED Neon", "signature", "verified", "GitHub"]),
        E("Installed widgets", SettingsPage.Widgets, WidgetsInstalled,
            ["installed", "uninstall", "remove", "update", "made here", "my widgets", "packs"]),

        E("Version", SettingsPage.About, Version,
            ["version", "What's New", "Take the tour", "tour", "release", "update"]),
        E("Trademark", SettingsPage.About, Trademark, ["Anthropic", "trademark", "disclaimer", "affiliated"]),
        E(SettingsNames.ThirdPartyNotices, SettingsPage.About, Notices, ["license", "licence", "credits", "open source"]),
        E(SettingsNames.WhereSettingsLive, SettingsPage.About, Where,
            ["settings.json", "folder", "AppData", "files", "data", "Credential Manager", "Show in Explorer", ".sanduhr", "Claude Code files"]),
        E(SettingsNames.ResetSanduhr, SettingsPage.About, Reset,
            ["reset", "uninstall", "erase", "start over"]),
    ];

    /// <summary>Where each name in <see cref="SettingsNames.Table"/> lives on its page.</summary>
    public static IReadOnlyDictionary<string, string> NameAnchors { get; } = new Dictionary<string, string>
    {
        [SettingsNames.TrayShows] = Tray,
        [SettingsNames.ShortcutToOpenSettings] = Shortcuts,
        [SettingsNames.AddAccount] = AccountList,
        [SettingsNames.AddFolder] = Folders,
        [SettingsNames.FollowAccount] = Follow,
        [SettingsNames.SendTestAlert] = TestAlert,
        [SettingsNames.SignInReminders] = SignInReminders,
        [SettingsNames.ExportMeterHistory] = HistoryExport,
        [SettingsNames.SaveAndApply] = Paste,
        [SettingsNames.WhatsNew] = Version,
        [SettingsNames.TakeTheTour] = Version,
        [SettingsNames.WhereSettingsLive] = Where,
        [SettingsNames.ThirdPartyNotices] = Notices,
        [SettingsNames.ResetSanduhr] = Reset,
        [SettingsNames.DeskSwitch] = DeskOn,
        [SettingsNames.DeskInk] = DeskInk,
        [SettingsNames.ArrangeDesk] = DeskArrange,
        [SettingsNames.DeskPieceClicksSwitch] = DeskClicks,
        [SettingsNames.DeskPlateSwitch] = DeskPlate,
        [SettingsNames.WatchersSwitch] = DeskWatchers,
        [SettingsNames.CiRunnersSwitch] = DeskWatchers,
        [SettingsNames.DeskDirectSwitch] = DeskDirect,
        [SettingsNames.OledCareSwitch] = DeskOled,
        [SettingsNames.PutMyDeskBack] = DeskDesigns,
        [SettingsNames.ExportSessions] = Sessions,
        [SettingsNames.SessionArchive] = Vault,
        [SettingsNames.FoldersToShare] = Homes,
        [SettingsNames.ClaudeCodeGlowHook] = Glow,
        [SettingsNames.TranscriptsKept] = Retention,
        [SettingsNames.LetClaudeReadTotals] = Folders,
        [SettingsNames.SnakeSoundAtFull] = Sound,
        [SettingsNames.DeskFont] = DeskLook,
        [SettingsNames.MessageFont] = MessageLook,
        [SettingsNames.MessageColor] = MessageLook,
        [SettingsNames.LauncherAddApp] = DeskLauncher,
        [SettingsNames.LauncherAddLink] = DeskLauncher,
        [SettingsNames.NowPlayingInWidget] = NowPlayingWidget,
        [SettingsNames.NowPlayingStyle] = NowPlayingLooks,
        [SettingsNames.NowPlayingLooksDirect] = NowPlayingLooks,
        [SettingsNames.ClearLooks] = NowPlayingLooks,
        [SettingsNames.WeatherLookUp] = WeatherPlace,
        [SettingsNames.WeatherShowAs] = WeatherPlace,
        [SettingsNames.NowPlayingBarsGradient] = NowPlayingBars,
        [SettingsNames.GlowWhere] = GlowWhere,
        [SettingsNames.WidgetCatalogCheck] = WidgetsCatalog,
        [SettingsNames.WidgetCompanionShow] = WidgetsCompanion,
        [SettingsNames.WidgetCompanionRoam] = WidgetsCompanion,
    };

    /// <summary>Words a page goes by besides its title.</summary>
    public static IReadOnlyDictionary<SettingsPage, string[]> PageSynonyms { get; } = new Dictionary<SettingsPage, string[]>
    {
        [SettingsPage.General] = ["tray", "startup", "shortcut"],
        [SettingsPage.Accounts] = ["sign in", "account"],
        [SettingsPage.Alerts] = ["notification", "warning"],
        [SettingsPage.History] = ["meter history", "chart"],
        // "vault" lives on the archive section only: as a page synonym it ties the section at rank 1 and the page row wins.
        [SettingsPage.ClaudeUsage] = ["Claude Code", "tokens", "usage"],
        [SettingsPage.ClaudeCode] = ["statusline", "MCP", "meters", "mod", "plugin", "hooks", "glow", "folder", "seat"],
        [SettingsPage.Mods] = ["mod", "plugin", "hooks", "statusline", "MCP", "CLAUDE.md", "retention", "config", "settings.json"],
        [SettingsPage.Publish] = ["publish", "upload", "endpoint", "share"],
        [SettingsPage.Themes] = ["theme", "colors", "look"],
        [SettingsPage.Desk] = ["desktop", "wallpaper", "clock", "Rainmeter", "layout", "font", "EsteFont", "margins", "taskbar", "telemetry", "CPU", "RAM", "disk"],
        [SettingsPage.DeskMessage] = ["messages", "messages.txt", "quote", "handwriting"],
        [SettingsPage.NowPlaying] = ["music", "song", "media", "visualizer", "looks", "song style"],
        [SettingsPage.Weather] = ["forecast", "temperature", "city"],
        [SettingsPage.Widgets] = ["widget packs", "companion", "sprite", "catalog", "pet"],
        [SettingsPage.About] = ["help", "version", "trademark", "privacy"],
    };

    /// <summary>The whole index: every page, every anchor, and every name in the names table.</summary>
    public static IReadOnlyList<SettingsEntry> All { get; } = Build();

    private static List<SettingsEntry> Build()
    {
        var all = SettingsNames.Pages
            .Select(p => new SettingsEntry(SettingsNames.PageTitle(p), p, null,
                PageSynonyms.TryGetValue(p, out var s) ? s : []))
            .ToList();
        all.AddRange(Anchors);
        foreach (var (name, page) in SettingsNames.Table)
        {
            var anchor = NameAnchors.TryGetValue(name, out var a) ? a : null;
            if (!all.Any(e => e.Title == name && e.Page == page && e.Anchor == anchor))
            {
                var at = Anchors.FirstOrDefault(e => e.Page == page && e.Anchor == anchor);
                all.Add(new SettingsEntry(name, page, anchor, [], at?.Subsection, at?.CollapsedIn));
            }
        }
        return all;
    }

    /// <summary>The anchors on <paramref name="page"/>, in page order.</summary>
    public static IReadOnlyList<SettingsEntry> EntriesOn(SettingsPage page) => [.. Anchors.Where(e => e.Page == page)];

    /// <summary>The anchor <paramref name="id"/> on <paramref name="page"/>, null when the page has none by that id.</summary>
    public static SettingsEntry? Find(SettingsPage page, string? id) =>
        id is null ? null : Anchors.FirstOrDefault(e => e.Page == page && e.Anchor == id);

    /// <summary>A match: the entry, and 0 when its title matched, 1 when only a synonym did, -1 when a boost named it.</summary>
    public sealed record Hit(SettingsEntry Entry, int Rank);

    /// <summary>
    /// The entries <paramref name="query"/> finds, best first: boosted sections, then title matches, then synonym
    /// matches, then in sidebar order, then in index order. Empty for a blank query.
    /// </summary>
    public static IReadOnlyList<Hit> Search(string? query, IReadOnlyList<SettingsEntry>? index = null)
    {
        var q = Words(query);
        if (q.Count == 0)
            return [];
        index ??= All;
        var order = SettingsNames.Pages;
        var boosted = Boosted(q, index);
        var found = new List<(Hit Hit, int I)>();
        for (int i = 0; i < index.Count; i++)
        {
            var e = index[i];
            if (boosted.Any(b => ReferenceEquals(b, e)))
                continue;
            if (Matches(q, e.Title))
                found.Add((new Hit(e, 0), i));
            else if (e.Synonyms.Any(s => Matches(q, s)))
                found.Add((new Hit(e, 1), i));
        }
        int PageOrder(SettingsPage p) { var at = IndexOf(order, p); return at < 0 ? int.MaxValue : at; }
        return [.. boosted.Select(e => new Hit(e, -1)),
            .. found
            .OrderBy(f => f.Hit.Rank)
            .ThenBy(f => PageOrder(f.Hit.Entry.Page))
            .ThenBy(f => f.I)
            .Select(f => f.Hit)];
    }

    /// <summary>
    /// A word that lands on one section above every title match (spec, Search, Synonyms). Like an alias it names its page
    /// by id, so it does nothing until that page and anchor are in the index: the rows below wait for slices 3 to 7.
    /// </summary>
    public sealed record Boost(IReadOnlyList<string> Queries, string PageId, string Anchor);

    /// <summary>The spec's synonyms, each the whole query (any case), never a prefix of it.</summary>
    public static IReadOnlyList<Boost> Boosts { get; } =
    [
        new(["font", "fonts"], "desklook", "look"),
        new(["update", "updates"], "about", "updates"),
        new(["docker", "runner", "runners"], "watchers", "ci-runners"),
        new(["privacy", "offline", "network", "send"], "privacy", "network"),
        new(["stream", "streamer", "screen share", "hide names", "obs"], "privacy", "streamer"),
        new(["demo"], "privacy", "streamer"),
        new(["codename", "codenames"], "privacy", "streamer-allowlist"),
        new(["widgets", "packs", "pack"], "mods", "catalog"),
    ];

    /// <summary>The entries a boost names for exactly this query, in boost order, when they are in <paramref name="index"/>.</summary>
    private static List<SettingsEntry> Boosted(IReadOnlyList<string> query, IReadOnlyList<SettingsEntry> index)
    {
        var phrase = string.Join(' ', query);
        var hits = new List<SettingsEntry>();
        foreach (var boost in Boosts)
        {
            if (!boost.Queries.Any(w => string.Join(' ', Words(w)) == phrase) || PageFromId(boost.PageId) is not { } page)
                continue;
            if (index.FirstOrDefault(e => e.Page == page && e.Anchor == boost.Anchor) is { } entry
                && !hits.Any(h => ReferenceEquals(h, entry)))
                hits.Add(entry);
        }
        return hits;
    }

    /// <summary>
    /// The hits grouped by page for the sidebar: each page once, in the order of its best hit, with
    /// its matching sections and controls under it (the page's own entry is the page row, not a line
    /// under it; a title shows once per page).
    /// </summary>
    public static IReadOnlyList<(SettingsPage Page, IReadOnlyList<SettingsEntry> Entries)> Grouped(IReadOnlyList<Hit> hits)
    {
        var pages = new List<SettingsPage>();
        foreach (var h in hits)
            if (!pages.Contains(h.Entry.Page))
                pages.Add(h.Entry.Page);
        var result = new List<(SettingsPage, IReadOnlyList<SettingsEntry>)>();
        foreach (var page in pages)
        {
            var seen = new List<SettingsEntry>();
            foreach (var h in hits)
            {
                if (h.Entry.Page == page && h.Entry.Anchor is not null && !seen.Any(e => e.Title == h.Entry.Title))
                    seen.Add(h.Entry);
            }
            result.Add((page, seen));
        }
        return result;
    }

    /// <summary>Each query word starts a word of <paramref name="text"/> ("glow" finds "Notch glow",
    /// "tra sh" finds "Tray shows"), or the whole query starts <paramref name="text"/>.</summary>
    public static bool Matches(IReadOnlyList<string> query, string text)
    {
        var t = Words(text);
        var phrase = string.Join(' ', query);
        if (phrase.Length > 0 && string.Join(' ', t).StartsWith(phrase, StringComparison.Ordinal))
            return true;
        return query.Count > 0 && query.All(q => t.Any(w => w.StartsWith(q, StringComparison.Ordinal)));
    }

    /// <summary>Lowercased words with accents folded ("für" reads "fur"), split at anything that is
    /// not a letter or a digit.</summary>
    public static IReadOnlyList<string> Words(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return [];
        var folded = new StringBuilder(s.Length);
        foreach (var c in s.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                folded.Append(char.ToLowerInvariant(c));
        }
        var words = new List<string>();
        var current = new StringBuilder();
        foreach (var c in folded.ToString())
        {
            if (char.IsLetterOrDigit(c))
            {
                current.Append(c);
            }
            else if (current.Length > 0)
            {
                words.Add(current.ToString());
                current.Clear();
            }
        }
        if (current.Length > 0)
            words.Add(current.ToString());
        return words;
    }

    // ---- reach (Settings v2, slice 2): aliases ----

    /// <summary>
    /// An old page or anchor and where it lives in v2. <paramref name="ToPageId"/> is the target page's lower-case id
    /// (the enum name, any case), so a row can name a page this build doesn't have yet: such a row does nothing until it
    /// does. A null <paramref name="FromAnchor"/> stands for the whole page once the page leaves the sidebar.
    /// </summary>
    public sealed record Alias(SettingsPage FromPage, string? FromAnchor, string ToPageId, string ToAnchor);

    /// <summary>Every old place that moves in v2 (spec, "Deep links and entry points", Aliases), anchor rows before page rows.</summary>
    public static IReadOnlyList<Alias> Aliases { get; } =
    [
        new(SettingsPage.History, Chart, "claudeusage", "meters"),
        new(SettingsPage.History, HistoryExport, "claudeusage", "meters-export"),
        new(SettingsPage.Widgets, WidgetsCatalog, "mods", "catalog"),
        new(SettingsPage.Widgets, WidgetsInstalled, "mods", ModList),
        new(SettingsPage.Widgets, WidgetsCompanion, "desk", DeskPieces),
        new(SettingsPage.Desk, DeskLook, "desklook", "look"),
        new(SettingsPage.Desk, DeskInk, "desklook", "ink"),
        new(SettingsPage.Desk, DeskPlate, "desklook", "desk-plate"),
        new(SettingsPage.Desk, DeskWatchers, "watchers", "watchers"),
        new(SettingsPage.Desk, DeskLauncher, "launcher", "launcher"),
        new(SettingsPage.Desk, DeskDirect, "desk", DeskDesigns),
        new(SettingsPage.DeskMessage, MessageLook, "desklook", "sizes"),
        new(SettingsPage.Alerts, SignInReminders, "desk", "signin-reminders"),
        new(SettingsPage.ClaudeCode, Meters, "mods", "meters"),
        new(SettingsPage.ClaudeCode, Statusline, "claudecode", Folders),
        new(SettingsPage.ClaudeCode, Mcp, "claudecode", Folders),
        new(SettingsPage.Mods, ConfigFiles, "claudecode", "folder-config"),
        new(SettingsPage.Mods, McpServers, "claudecode", "folder-config"),
        new(SettingsPage.Mods, ClaudeMd, "claudecode", "folder-config"),
        new(SettingsPage.Mods, Retention, "claudecode", "folder-config"),
        new(SettingsPage.History, null, "claudeusage", "meters"),
        new(SettingsPage.Widgets, null, "mods", "catalog"),
    ];

    /// <summary>The page an id names, in any case; null for anything else, numbers and spaces included.</summary>
    public static SettingsPage? PageFromId(string? id) =>
        !string.IsNullOrEmpty(id) && id.All(char.IsAsciiLetter) && Enum.TryParse<SettingsPage>(id, ignoreCase: true, out var page)
            ? page
            : null;

    /// <summary>
    /// Where (<paramref name="page"/>, <paramref name="anchor"/>) lives now: itself while it is still in the index; else
    /// its alias's target once that target is in the index; else itself, unchanged. Cards, tests and agent text written
    /// before 4.0 land through this.
    /// </summary>
    public static SettingsLink Resolve(SettingsPage page, string? anchor) =>
        Resolve(page, anchor, Anchors, SettingsNames.Pages);

    /// <summary><see cref="Resolve(SettingsPage, string?)"/> over a given index and sidebar, so tests can stand in for later slices.</summary>
    public static SettingsLink Resolve(SettingsPage page, string? anchor, IReadOnlyList<SettingsEntry> anchors,
        IReadOnlyList<SettingsPage> pages)
    {
        bool Live(SettingsPage p, string? a) =>
            pages.Contains(p) && (a is null || anchors.Any(e => e.Page == p && e.Anchor == a));
        if (Live(page, anchor))
            return new(page, anchor);
        var row = (anchor is null ? null : Aliases.FirstOrDefault(a => a.FromPage == page && a.FromAnchor == anchor))
            ?? (pages.Contains(page) ? null : Aliases.FirstOrDefault(a => a.FromPage == page && a.FromAnchor is null));
        if (row is not null && PageFromId(row.ToPageId) is { } to && Live(to, row.ToAnchor))
            return new(to, row.ToAnchor);
        return new(page, anchor);
    }

    // ---- reach: links and the --settings switch ----

    /// <summary>A page's id in links and <c>--settings</c>: its enum name in lower case ("claudeusage").</summary>
    public static string PageId(SettingsPage page) => page.ToString().ToLowerInvariant();

    /// <summary>The command-line switch that opens Settings: <c>Sanduhr.exe --settings &lt;page&gt;[#&lt;anchor&gt;]</c>.</summary>
    public const string SettingsSwitch = "--settings";

    /// <summary>
    /// A link's text, "desk#pieces", as a place: the page by id (any case), the anchor after "#" resolved through the
    /// aliases and dropped when the page has no such anchor. Text that names no Settings page opens General; the widget
    /// is kept, so the caller can show it. Null text is no link.
    /// </summary>
    public static SettingsLink? ParseLink(string? text)
    {
        if (text is null)
            return null;
        var trimmed = text.Trim();
        int hash = trimmed.IndexOf('#');
        var pageText = (hash < 0 ? trimmed : trimmed[..hash]).Trim();
        var anchorText = hash < 0 ? "" : trimmed[(hash + 1)..].Trim().ToLowerInvariant();
        if (PageFromId(pageText) is not { } page)
            return new(SettingsPage.General, null);
        var link = Check(page, anchorText.Length == 0 ? null : anchorText);
        return link.Page != SettingsPage.Widget && !SettingsNames.Pages.Contains(link.Page)
            ? new(SettingsPage.General, null)
            : link;
    }

    /// <summary>
    /// The one checked-anchor rule: (<paramref name="page"/>, <paramref name="anchor"/>) resolved through the aliases
    /// (<see cref="Resolve(SettingsPage, string?)"/>), with the anchor dropped to the page's top when the index has no
    /// such section. Every place a link enters Settings, from <see cref="ParseLink"/> to the App's entry points, passes
    /// through here, so nothing unchecked reaches <see cref="Landing"/>.
    /// </summary>
    public static SettingsLink Check(SettingsPage page, string? anchor) => Check(page, anchor, Anchors, SettingsNames.Pages);

    /// <summary><see cref="Check(SettingsPage, string?)"/> over a given index and sidebar, so tests can stand in for later slices.</summary>
    public static SettingsLink Check(SettingsPage page, string? anchor, IReadOnlyList<SettingsEntry> anchors,
        IReadOnlyList<SettingsPage> pages)
    {
        var link = Resolve(page, anchor, anchors, pages);
        return link.Anchor is not null && !anchors.Any(e => e.Page == link.Page && e.Anchor == link.Anchor)
            ? link with { Anchor = null }
            : link;
    }

    /// <summary>
    /// The value of <see cref="SettingsSwitch"/> in <paramref name="args"/>: "desk#pieces" for "--settings desk#pieces" or
    /// "--settings=desk#pieces"; "general" when the switch has no value; null without the switch.
    /// </summary>
    public static string? LinkArgument(IEnumerable<string> args)
    {
        var list = args as IReadOnlyList<string> ?? args.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            var arg = list[i];
            if (arg.StartsWith(SettingsSwitch + "=", StringComparison.OrdinalIgnoreCase))
            {
                var value = arg[(SettingsSwitch.Length + 1)..];
                return string.IsNullOrWhiteSpace(value) ? PageId(SettingsPage.General) : value;
            }
            if (string.Equals(arg, SettingsSwitch, StringComparison.OrdinalIgnoreCase))
            {
                var value = i + 1 < list.Count && !list[i + 1].StartsWith("--", StringComparison.Ordinal)
                    ? list[i + 1]
                    : null;
                return string.IsNullOrWhiteSpace(value) ? PageId(SettingsPage.General) : value;
            }
        }
        return null;
    }

    /// <summary>The place a launch's <see cref="SettingsSwitch"/> asks for, null without the switch.</summary>
    public static SettingsLink? LinkFromArgs(IEnumerable<string> args) => ParseLink(LinkArgument(args));

    // ---- reach: menus and reopening ----

    /// <summary>
    /// The section a menu's "&lt;Page&gt; settings…" opens at (spec, Menus table). Slices 3 and 5 move Alerts to
    /// <c>each-limit</c> and add the Launcher here; null opens the page top.
    /// </summary>
    public static string? MenuAnchor(SettingsPage page) => page switch
    {
        SettingsPage.Accounts => AccountList,
        SettingsPage.Alerts => Notifications,
        SettingsPage.Desk => DeskPieces,
        SettingsPage.DeskMessage => MessageLines,
        _ => null,
    };

    /// <summary>Where an opening Settings lands: the place asked for; else where it was left this session; else General.</summary>
    public static SettingsLink Landing(SettingsPage? page, string? anchor, SettingsLink? last) =>
        page is { } p ? new(p, anchor) : last ?? new(SettingsPage.General, null);

    private static int IndexOf(IReadOnlyList<SettingsPage> list, SettingsPage p)
    {
        for (int i = 0; i < list.Count; i++)
            if (list[i] == p)
                return i;
        return -1;
    }
}
