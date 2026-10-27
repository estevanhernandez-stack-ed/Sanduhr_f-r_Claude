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
/// capitals: Claude, Claude Code, Sanduhr, What's New (the window's name), Deep Work. The cooldown snake is a game, not a name.
/// Page titles stay as they shipped ("Claude Usage", "Publish usage"): PRIVACY.md and the Store copy
/// quote them.
/// </summary>
public static class SettingsNames
{
    // ---- Pages ----

    /// <summary>The sidebar: six groups, each page once. Widget is a page since slice 5; Widget with no anchor still means the widget itself
    /// (SettingsIndex.ShowsTheWidget). Settings v2 names the groups: "Extend" (Claude Code and Mods &amp; Widgets, which holds widget packs too)
    /// and "Desktop" (the Mac's word, and no longer its first page's title). Retired pages (Widgets since slice 4, History since slice 5) stay in
    /// <see cref="SettingsPage"/> for their aliases but are in no group.</summary>
    public static IReadOnlyList<SettingsGroup> Sidebar { get; } =
    [
        new(null, [SettingsPage.General, SettingsPage.Accounts, SettingsPage.Alerts, SettingsPage.Privacy]),
        new("Usage", [SettingsPage.ClaudeUsage, SettingsPage.Publish]),
        new("Extend", [SettingsPage.ClaudeCode, SettingsPage.Mods]),
        new("Widget", [SettingsPage.Widget, SettingsPage.Themes]),
        new("Desktop", [SettingsPage.Desk, SettingsPage.DeskLook, SettingsPage.DeskMessage, SettingsPage.NowPlaying, SettingsPage.Weather,
            SettingsPage.Watchers, SettingsPage.Launcher]),
        new("Help", [SettingsPage.About]),
    ];

    // ---- Page titles added by Settings v2. Desk Look, Watchers and Launcher are pages since slice 3, Mods & Widgets since slice 4,
    // Privacy since slice 6.
    public const string PrivacyTitle = "Privacy";
    public const string DeskLookTitle = "Desk Look";
    public const string WatchersTitle = "Watchers";
    public const string LauncherTitle = "Launcher";
    public const string ModsAndWidgetsTitle = "Mods & Widgets";

    /// <summary>The titles of pages still to come, in sidebar order.</summary>
    public static IReadOnlyList<string> TitlesToCome { get; } = [];

    /// <summary>Every page in sidebar order.</summary>
    public static IReadOnlyList<SettingsPage> Pages { get; } = [.. Sidebar.SelectMany(g => g.Pages)];

    /// <summary>A page's sidebar title.</summary>
    public static string PageTitle(SettingsPage page) => page switch
    {
        SettingsPage.General => "General",
        SettingsPage.Accounts => "Accounts",
        SettingsPage.Alerts => "Alerts",
        SettingsPage.Privacy => PrivacyTitle,
        SettingsPage.History => "History",   // retired in slice 5: keeps its old title for aliases and logs
        SettingsPage.ClaudeUsage => "Claude Usage",
        SettingsPage.Publish => "Publish usage",
        SettingsPage.ClaudeCode => "Claude Code",
        SettingsPage.Mods => ModsAndWidgetsTitle,
        SettingsPage.Themes => "Themes",
        SettingsPage.Desk => "Desk",
        SettingsPage.DeskMessage => "Message",
        SettingsPage.NowPlaying => "Now playing",
        SettingsPage.Weather => "Weather",
        SettingsPage.DeskLook => DeskLookTitle,
        SettingsPage.Watchers => WatchersTitle,
        SettingsPage.Launcher => LauncherTitle,
        SettingsPage.Widgets => "Widgets",   // retired in slice 4: keeps its old title for aliases and logs
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

    /// <summary>Tray and widget menus: opens Settings. Which page it lands on is App's rule.</summary>
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
    /// <summary>History: the 30-day meter chart as a CSV file ("meter history", WF14).</summary>
    public const string ExportMeterHistory = "Export meter history…";
    /// <summary>Claude Usage, Sessions: the session table as a CSV file.</summary>
    public const string ExportSessions = "Export sessions…";
    /// <summary>Claude Usage, Overview, and About: the vault of Claude Code session totals ("session archive", WF14).</summary>
    public const string SessionArchive = "Session archive";
    public const string EraseSessionArchive = "Erase session archive…";
    public const string OpenArchiveFolder = "Open archive folder";
    /// <summary>The empty state of the archive: About, Where settings live (beside the dim Show in Explorer) and the Claude Usage trends chart.</summary>
    public const string NothingArchivedYet = "Nothing archived yet.";
    /// <summary>Publish usage: the folders whose totals may leave ("Folder" is the one word, WF21).</summary>
    public const string FoldersToShare = "Folders to share (off until you tick them)";
    /// <summary>Reset Sanduhr…: two of its five parts, named for everything they remove (WF20).</summary>
    public const string ResetClaudeCodeEntries = "Sanduhr's entries in Claude Code (statusline, MCP server, meters mod, glow hook)";
    public const string ResetMeterHistory = "Meter history and the session archive";
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
    /// <summary>The widget's menu, icon strip and the tray (WF19): one name each. The strip's tooltips add
    /// the shortcut ("Compact mode (Ctrl+D)").</summary>
    public const string CompactMode = "Compact mode";
    public const string DeepWork = "Deep Work";
    public const string CooldownSnake = "Cooldown snake";
    public const string RefreshUsageNow = "Refresh usage now";
    /// <summary>A limit card's menu: hides the limit until shown again.</summary>
    public const string HideThisLimit = "Hide this limit";
    /// <summary>The widget's icon strip: cycles the sparkline style (settings.json "sparkline_style").</summary>
    public const string GraphStyle = "Graph style";
    /// <summary>The widget's icon strip: keeps the widget above other windows (settings.json "pinned").</summary>
    public const string KeepWidgetOnTop = "Keep the widget on top";
    /// <summary>About, Version, and the What's New window (settings.json "whats_new_hide_after_updates"): one key, two boxes.</summary>
    public const string HideWhatsNewAfterUpdates = "Don't show What's New after updates";
    /// <summary>Claude Code: picks a folder Sanduhr did not find.</summary>
    public const string AddFolder = "Add folder…";
    /// <summary>Claude Code: drops a folder that was added by hand.</summary>
    public const string ForgetFolder = "Forget folder";
    /// <summary>Claude Code, a folder card's Forget button, spoken with its folder (WF34).</summary>
    public static string ForgetFolderFor(string folder) => $"{ForgetFolder} {folder}";
    /// <summary>Claude Code, a folder card: the MCP server may read this folder's project totals (settings.json "mcp_roots").</summary>
    public const string LetClaudeReadTotals = "Let Claude read this folder's project totals";
    /// <summary>The same switch spoken with its folder, so each card's reads apart.</summary>
    public static string LetClaudeReadTotalsFor(string folder) => $"{LetClaudeReadTotals}, {folder}";
    /// <summary>A search row for a control inside a collapsed section: "Margins (in Advanced)".</summary>
    public static string InSection(string title, string section) => $"{title} (in {section})";
    /// <summary>The same row spoken, without the parentheses: "Margins, in Advanced".</summary>
    public static string InSectionSpoken(string title, string section) => $"{title}, in {section}";
    /// <summary>Settings' title-bar close button, spoken (its face is a glyph).</summary>
    public const string CloseSettings = "Close Settings";
    /// <summary>Accounts: the follow switch, and the tray's account list marks the account in use.</summary>
    public const string FollowAccount = "Follow the account I'm using";
    /// <summary>Accounts: the follow switch's caption, its numbers read from AccountFollow so they can't drift (WF29).</summary>
    public static string FollowAccountCaption { get; } =
        $"Every {(int)AccountFollow.CheckInterval.TotalMinutes} minutes Sanduhr checks your other signed-in accounts and switches to the one whose session is climbing. "
        + $"A manual switch pauses it for up to {(int)AccountFollow.PauseLength.TotalHours} hours, or until that account sits idle for {(int)AccountFollow.PauseIdle.TotalMinutes} minutes.";
    /// <summary>Accounts: why the account buttons are dim (WF29).</summary>
    public const string PickAnAccount = "Pick an account to switch, rename, update or remove it.";
    /// <summary>The manual key window: where the sessionKey is, in plain steps (WF31).</summary>
    public const string ManualKeySteps =
        "In Edge or Chrome, open claude.ai and sign in. Press F12, open the Application tab, then Cookies, then https://claude.ai. "
        + "Copy the Value next to sessionKey and paste it below.";
    /// <summary>The manual key window: cf_clearance waits behind this line.</summary>
    public const string ManualKeyCloudflareToggle = "Seeing a Cloudflare error?";
    public const string ManualKeyCloudflareHelp =
        "Only if Sanduhr says Cloudflare blocked it: copy the Value next to cf_clearance from the same Cookies list and paste it here.";
    public const string ManualKeyCfLabel = "cf_clearance";
    /// <summary>A Claude Code card's report: re-reads the folder.</summary>
    public const string RefreshFolder = "Refresh";
    /// <summary>Desk: the switch at the top of the page (settings.json "desk_enabled", off by default). It lists
    /// what the Desk draws, as the tour does (WF16).</summary>
    public const string DeskSwitch = "Desk: a clock, your meters, a message and more on the desktop";
    /// <summary>Claude Code: the install that lights the glow, the Mac's name (WF11). "Claude Code glow" is the
    /// section and the Desk piece.</summary>
    public const string ClaudeCodeGlowHook = "Claude Code glow hook";
    /// <summary>Claude Code: a piece installed in an older version. "Update…" beside it is the verb (WF42).</summary>
    public const string PieceOutdated = "Installed, but not the current version.";
    /// <summary>Claude Code, a folder card's Linked account caption, true before and after an install (WF27).</summary>
    public const string LinkedAccountCaption = "Once the statusline or meters are installed here, they show this account's meters.";
    /// <summary>A Claude Code card's report: Claude Code's own retention (cleanupPeriodDays), the fourth "history" (WF14).</summary>
    public const string TranscriptsKept = "Claude Code keeps transcripts for";
    /// <summary>Themes: a pasted or edited theme with no errors or notes (WF43).</summary>
    public const string ThemeJsonValid = "This theme's JSON is valid.";
    /// <summary>Themes, Studio: the file name the theme is saved under (WF43).</summary>
    public const string SaveThemeAs = "Save the theme as";
    /// <summary>Alerts, Sound (settings.json "alert_snake_full"), said plainly (WF43, WF38).</summary>
    public const string SnakeSoundAtFull = "Play the cooldown snake's sound at 100%";
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
    /// <summary>Desk, Where each piece sits: the Launcher, buttons that open apps and links from the desktop.</summary>
    public const string DeskLauncher = "Launcher";
    /// <summary>Desk, Where each piece sits: the companion, whose sprite is chosen in its own row.</summary>
    public const string DeskCompanion = "Companion";
    /// <summary>Launcher, Buttons: opens the app picker.</summary>
    public const string LauncherAddApp = "Add app…";
    /// <summary>Launcher, Buttons: asks for an https link and a label.</summary>
    public const string LauncherAddLink = "Add link…";
    /// <summary>Desk: lets Claude put watchers up through the MCP server (settings.json "watchers_agents", off by default).
    /// "Claude" means any agent using Sanduhr's MCP server, the one word every agent consent uses (WF10).</summary>
    public const string WatchersSwitch = "Let Claude show watchers";
    /// <summary>Desk: CI watchers from this PC's runner containers (settings.json "watchers_ci_runners", off by default; spec ci-watchers-3.9).</summary>
    public const string CiRunnersSwitch = "Show my CI runners' jobs";
    /// <summary>Desk: lets Claude apply a Desk design without the review bar (settings.json "desk_direct", off by default).</summary>
    public const string DeskDirectSwitch = "Let Claude change the Desk directly";
    /// <summary>Desk, Designs: the section that lists the Desk designs kept (ten, pinned ones never dropped).</summary>
    public const string DeskDesigns = "Designs";
    /// <summary>Desk, Designs, and the tray: switches to the design saved before the last change.</summary>
    public const string PutMyDeskBack = "Put my Desk back";
    /// <summary>Desk: starts Arrange mode on the desktop (spec 7.3).</summary>
    public const string ArrangeDesk = DeskArrangeCopy.SettingsButton;
    /// <summary>Desk: the clock, message and Claude line take clicks (spec 7.4).</summary>
    public const string DeskPieceClicksSwitch = DeskPieceClicks.Title;
    /// <summary>Desk: burn-in care for OLED screens (settings.json "desk_oled_care", off by default).</summary>
    public const string OledCareSwitch = OledCare.Title;
    /// <summary>Desk Look, Back plate: a soft plate behind each piece.</summary>
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
        "launcher" => DeskLauncher,
        "companion" => DeskCompanion,
        _ => widget,
    };

    /// <summary>The page a Desk piece's own settings live on (Desk, Pieces' "&lt;Page&gt; settings…"); null for a piece that has
    /// none (the clock, the Claude meters, System, and the companion, whose controls sit in its row).</summary>
    public static SettingsPage? DeskPiecePage(string widget) => widget switch
    {
        "message" => SettingsPage.DeskMessage,
        "nowPlaying" => SettingsPage.NowPlaying,
        "weather" => SettingsPage.Weather,
        "watchers" => SettingsPage.Watchers,
        "launcher" => SettingsPage.Launcher,
        "glow" => SettingsPage.ClaudeCode,
        _ => null,
    };
    /// <summary>Desk Look, Ink and color: the ink picker's label and spoken name (it was spoken "Ink", WF34).</summary>
    public const string DeskInkPicker = "Clock, date, Claude";
    /// <summary>Desk Look, Fonts: the Desk's font, one of the two "Font"s (WF12).</summary>
    public const string DeskFont = "Desk font";
    /// <summary>Desk Look, Fonts: the message's font. <see cref="MessageColor"/> sits under Ink and color.</summary>
    public const string MessageFont = "Message font";
    /// <summary>Desk Look, Ink and color: the message's color.</summary>
    public const string MessageColor = "Message color";
    /// <summary>Desk, Behavior: the Desk's sign-in reminder switch (settings.json "desk_sign_in_reminder", on by default; WF32).</summary>
    public const string SignInReminders = SignInReminder.Title;
    /// <summary>The first-run card's third action (settings.json "just_the_desk"), and the Widget page's Just the Desk section.</summary>
    public const string JustTheDeskButton = JustTheDesk.ButtonTitle;
    /// <summary>Now playing: the widget's line (settings.json "now_playing.widget", off by default).</summary>
    public const string NowPlayingInWidget = "Show what's playing in the widget";
    /// <summary>Now playing, Looks: the Desk draws the song in its look (settings.json "now_playing.style", off by default).</summary>
    public const string NowPlayingStyle = "Style what's playing";
    /// <summary>Now playing, Looks: a look Claude proposes is saved without the card (settings.json "now_playing.looks_direct", off by default).</summary>
    public const string NowPlayingLooksDirect = "Let Claude style songs directly";
    /// <summary>Now playing, Looks: forgets every saved look, after a question.</summary>
    public const string ClearLooks = "Clear Looks…";
    /// <summary>Message, Your lines: the two views of messages.txt, as a segment (WF48).</summary>
    public const string MessageModeList = "List";
    /// <summary>Message, Your lines: the Text half of the segment, messages.txt as written (WF48).</summary>
    public const string MessageModeText = "Text";
    /// <summary>Weather: looks a typed city up with Open-Meteo.</summary>
    public const string WeatherLookUp = "Look up";
    /// <summary>Weather: the nickname the Desk shows instead of the place's name (#315, settings.json "desk_weather.nickname", local only).</summary>
    public const string WeatherShowAs = "Show it as";
    /// <summary>Now playing, Bars: the ink's colors across the row, each bar fading to its base (settings.json "now_playing.bars_gradient", off).</summary>
    public const string NowPlayingBarsGradient = "Gradient bars";
    /// <summary>Claude Code: where the glow lands (settings.json "claude_code_glow.target").</summary>
    public const string GlowWhere = "Where the glow shows";
    /// <summary>Widgets: fetches and verifies the catalog again (it is fetched only when the page opens, spec 3.8, 2).</summary>
    public const string WidgetCatalogCheck = "Check the catalog again";
    /// <summary>Widgets: a catalog row's buttons and an installed row's. Each carries its pack's name for a screen reader.</summary>
    public const string WidgetInstall = "Install";
    public const string WidgetUpdate = "Update";
    public const string WidgetUninstall = "Uninstall…";
    /// <summary>Widgets: how a Claude-made or the user's own pack is listed (spec 3.8, section 3).</summary>
    public const string WidgetMadeHere = "made here";
    /// <summary>Widgets: an installed sprite made here takes the user's own PNG sheets, one per stage (spec 3.8, section 6).</summary>
    public const string WidgetUseOwnArt = "Use my own art…";
    /// <summary>Desk, Pieces, the Companion row: the installed sprite that is the companion (one at a time).</summary>
    public const string WidgetCompanionPick = "Companion sprite";
    /// <summary>Desk, Pieces, the Companion row: it walks the screen's edges (never with Windows' animations off).</summary>
    public const string WidgetCompanionRoam = "Let it roam";

    // ---- Settings v2, slice 4: Mods & Widgets (spec, Mods & Widgets: one list) ----

    /// <summary>Mods &amp; Widgets: the page's first line (WF21).</summary>
    public const string ModsPageLine = "A widget pack is " + DefineWidgetPack + ". A mod is " + DefineMod + ".";
    /// <summary>Mods &amp; Widgets: the intro (WF39). It no longer says "Sanduhr runs nothing here".</summary>
    public const string ModsIntro = "Everything installed for Sanduhr and for Claude Code, in one list. You can switch Sanduhr's mod and widget packs here; other Claude Code mods are shown read-only.";
    /// <summary>Mods &amp; Widgets' three sections, and the Claude Code card's collapsed report.</summary>
    public const string MetersSection = "Meters above the prompt";
    public const string InstalledSection = "Installed";
    public const string CatalogSection = "Catalog";
    public const string FolderHolds = "What this folder holds";
    /// <summary>Meters above the prompt: how Off differs from Remove (WF9).</summary>
    public const string MetersOffVsRemove = "Off keeps the mod's entry and files; Remove takes the entry out and puts settings.json back exactly.";
    /// <summary>A Claude Code plugin or mod Sanduhr can't switch (Principle 5, WF39, mods-parity R1 and R6).</summary>
    public const string ReadOnlyClaudeCode = "Read-only: Sanduhr can't switch this yet. Use claude plugin in a terminal.";
    /// <summary>Where an installed widget pack shows (WF3), by kind.</summary>
    public const string ShowsInThemes = "In Themes";
    public const string ShowsInInk = "In Desk Look, Ink and color";
    public const string ShowsInFonts = "In Desk Look, Fonts";
    public const string ShowsAsCompanion = "As the Desk companion";
    /// <summary>The catalog row's state line (Mods &amp; Widgets, Catalog).</summary>
    public const string CatalogInstalled = "Installed.";
    public static string CatalogVersionInstalled(string version) =>
        string.IsNullOrWhiteSpace(version) ? "An older version is installed." : $"Version {version} is installed.";
    public static string CatalogNeedsSanduhr(string min) => $"Needs Sanduhr {min} or later.";
    public static string CatalogUpdateNeedsSanduhr(string installed, string update, string min) =>
        $"{CatalogVersionInstalled(installed)} {update} needs Sanduhr {min} or later.";
    public static string CatalogIdTaken => $"A widget {WidgetMadeHere} already uses this id.";
    public const string EffectsUnused = "Not used yet: Sanduhr has nothing that draws effects packs.";
    /// <summary>Catalog: the only network call on the page (WF6).</summary>
    public const string CatalogDisclosure = "The catalog comes from GitHub. Sanduhr asks only when you open it, check it again, or install or update a pack.";
    /// <summary>Installed: the filter chips and the text filter's spoken name.</summary>
    public const string FilterAll = "All";
    public const string FilterClaudeCode = "Claude Code";
    public const string FilterWidgets = "Widgets";
    public const string FilterBoxName = "Filter the installed list";
    /// <summary>Installed: the filters hide everything (an empty-state line, WF35).</summary>
    public const string NothingMatches = "Nothing installed matches the filter.";
    /// <summary>A pack's or a folder's on switch: the visible word, then what it switches, for a screen reader.</summary>
    public const string PackOn = "On";
    public static string PackOnFor(string pack) => $"{PackOn}: {pack}";
    public static string MetersIn(string folder) => $"{MetersSection} in {folder}";
    public static string MetersOnFor(string folder) => $"{PackOn}: {MetersIn(folder)}";
    /// <summary>A Claude Code card's meters line, spoken with its state: the section in the folder, then On, Off, Files missing or Can't be read.</summary>
    public static string MetersSpoken(string folder, string state) => $"{MetersIn(folder)}: {state}";
    /// <summary>The names of a card's report lists, read before their items.</summary>
    public const string ReportHooks = "Hooks";
    public const string ReportPluginSwitches = "Plugin switches";
    public const string ReportMcpServers = "MCP servers";
    /// <summary>Installed: Sanduhr's meters mod row links up to its section.</summary>
    public const string MetersLinkUp = "Switch it per folder\u2026";
    /// <summary>A scope over folders: "On in 2 of 3 folders". One folder reads its state alone.</summary>
    public static string OnInFolders(int on, int of) => $"On in {on} of {of} folders";
    public const string FolderOn = "On";
    public const string FolderOff = "Off";
    public const string FolderMissing = "Files missing";
    public const string FolderNotInstalled = "Not installed";
    public const string FolderUnreadable = "Can't be read";
    /// <summary>Installed: the reconcile note in plain words (spec, Installed), counting distinct plugins across the folders (P11).</summary>
    public static string NoSwitchNote(int n) => n == 1
        ? "1 plugin has no on/off entry in settings.json, so Claude Code treats it as on."
        : $"{n} plugins have no on/off entry in settings.json, so Claude Code treats them as on.";
    public static string NoFilesNote(int n) => n == 1
        ? "1 plugin is switched on in settings.json, but its files aren't there."
        : $"{n} plugins are switched on in settings.json, but their files aren't there.";
    /// <summary>Catalog: the companion row's link shows sprites only until "Show every pack".</summary>
    public const string ShowingSprites = "Showing sprites only.";
    public const string ShowEveryPack = "Show every pack";
    /// <summary>The Uninstall button asks first.</summary>
    public static string UninstallQuestion(string pack) => $"Uninstall {pack}?";
    public const string UninstallDetail = "Its files leave Sanduhr's widgets folder on this PC.";
    public const string UninstallConfirm = "Uninstall";
    public const string UninstallCancel = "Cancel";
    /// <summary>The Uninstall button's spoken name: "Uninstall Fixture palette" (no ellipsis; the dialog follows).</summary>
    public static string UninstallFor(string pack) => $"Uninstall {pack}";
    /// <summary>Themes: the theme packs line above the gallery (WF40).</summary>
    public static string ThemePacksLine(int n) => $"Theme packs: {n} installed.";

    // ---- Settings v2, slice 4: the Claude Code cards (spec, Claude Code) ----

    /// <summary>The Claude Code glow section: what it needs, and which folders have it (WF11).</summary>
    public const string GlowNeedsHook = "Needs the " + ClaudeCodeGlowHook + " in a folder above.";
    public static string GlowHookIn(IReadOnlyList<string> folders) => folders.Count switch
    {
        0 => "Not installed in any folder yet.",
        1 => $"Installed in {folders[0]}.",
        _ => $"Installed in {string.Join(", ", folders.Take(folders.Count - 1))} and {folders[^1]}.",
    };
    /// <summary>A folder card's Linked account when no account is signed in (WF32, carried here with the card).</summary>
    public const string AddAccountToLink = "Add an account to link this folder.";

    /// <summary>A folder card's collapsed "What this folder holds": its summary line (a SectionExpander always has one, slice 3 M3).</summary>
    public const string FolderHoldsHelp = "settings.json, the statusline, hooks, plugin switches, MCP servers, CLAUDE.md and how long transcripts are kept.";
    /// <summary>The Refresh button's spoken name in one card: the folder it reads (T7-1).</summary>
    public static string RefreshFolderNamed(string display) => $"{RefreshFolder}, read {display} again";
    /// <summary>The Claude Code glow section's three switches (P8): the label and the spoken name are the same words.</summary>
    public const string GlowWhenWaiting = "Glow when Claude is waiting on you";
    public const string GlowWhenDone = "Glow when Claude finishes";
    public const string GlowNotInTerminal = "Not while a terminal or editor is in front";
    /// <summary>A folder card piece's install button help while the piece is absent (P23: the card's own folder).</summary>
    public static string PieceInstallHelp(string piece) => $"Asks first, then adds {piece} to this folder.";

    // ---- Settings v2, slice 4: the strings the later tasks would have inlined (preflight P10) ----

    /// <summary>InstalledItems (Task 3): where an item came from.</summary>
    public const string SourceSanduhr = "Sanduhr";
    public const string SourceSkillsFolder = "skills folder";
    public const string SourcePluginFolder = "plugin folder";
    public const string SourceFromCatalog = "from the catalog";
    /// <summary>InstalledRowViewModel (Task 9): the kind word on a Claude Code row.</summary>
    public const string KindSanduhrMod = "Sanduhr's mod";
    public const string KindClaudeCodeMod = "Claude Code mod";
    public const string KindClaudeCodePlugin = "Claude Code plugin";
    /// <summary>InstalledRowViewModel.RowName (Task 9): the words a row is read with.</summary>
    public const string RowReadOnly = "read-only";
    public const string RowOn = "on";
    public const string RowOff = "off";
    /// <summary>InstalledRowViewModel.WhereLinkName (Task 9): "Themes settings... for Fixture palette".</summary>
    public static string WhereLinkFor(string linkTitle, string pack) => $"{linkTitle} for {pack}";
    /// <summary>SwitchPack's status line (Task 9).</summary>
    public static string PackIsOn(string name) => $"{name} is on.";
    public static string PackIsOff(string name) => $"{name} is off.";
    /// <summary>SwitchPack's status when widgets.json couldn't be written and the store gave no reason.</summary>
    public const string PackSwitchFailed = "The widget couldn't be switched.";
    /// <summary>MetersFolderRowViewModel (Task 10): why a folder has no switch, and its Update and Remove spoken names.</summary>
    public const string FolderUnreadableReason = "This folder's settings.json can't be read, so Sanduhr changes nothing in it.";
    public static string UpdateMetersIn(string folder) => $"Update {MetersSection} in {folder}";
    public static string RemoveMetersFrom(string folder) => $"Remove {MetersSection} from {folder}";
    /// <summary>The consent dialog before the meters mod is installed into a folder (Task 10), and the install's two statuses.</summary>
    public const string MetersConsentTitle = "Draw your meters above the Claude Code prompt?";
    public const string MetersConsentIntro = "Sanduhr adds a mod that draws your session and weekly meters in a band above the prompt.";
    public const string MetersConsentDetails = "What gets written: the mod's folder, copied under %USERPROFILE%\\.sanduhr\\mods, and one entry in this folder's settings.json under "
        + "env, CLAUDE_CODE_PLUGIN_DIRS. The mod reads only Sanduhr's snapshot file. Off keeps the entry and files; Remove takes out only "
        + "Sanduhr's entry and leaves any other folder in that list alone.";
    public const string MetersInstalled = "Meters installed. New Claude Code sessions show them.";
    public const string MetersInstallFailed = "Couldn't install the meters.";
    /// <summary>ModsPage.xaml (Task 12).</summary>
    public const string ModsShowGroup = "Show";
    public const string CatalogListName = "Widget packs in the catalog";
    public const string UpdateButton = "Update\u2026";
    public const string RemoveButton = "Remove\u2026";
    /// <summary>Meters above the prompt with no folder (P20): the sentence, then a <see cref="ClaudeCodeLinkTitle"/> link to claudecode#folders.</summary>
    public const string NoFolderInMods = "No Claude Code folder found. Add one in";
    /// <summary>"Claude Code settings…", for a page that binds it with x:Static.</summary>
    public static string ClaudeCodeLinkTitle => LinkTitle(SettingsPage.ClaudeCode);
    /// <summary>Mods &amp; Widgets (Task 12): the Installed list's empty line, the catalog's line, and the page's privacy line. The last says
    /// exactly when GitHub is asked: the catalog when it is opened or checked again, a pack when it is installed or updated (Task 11 review).</summary>
    public const string InstalledEmpty = "Nothing installed yet. Widget packs and Claude Code plugins and mods show here, and so do widgets you make with Claude, marked made here.";
    public const string CatalogBlurb = "Sprites, themes, palettes, effects and fonts from the 626 Labs widget catalog. Packs are data only, no code. Sanduhr checks the catalog's signature, then each pack's size, hash and every file before it installs one.";
    public const string InstalledPacksNote = "Installed packs live in Sanduhr's own widgets folder on this PC. Sanduhr asks GitHub for the catalog only when you open it "
        + "or check it again, and Install or Update downloads that pack from GitHub. Reset Sanduhr removes the folder.";
    /// <summary>Use my own art…'s help text (spec 3.8 section 6).</summary>
    public const string UseOwnArtHelp = "Choose a PNG sprite sheet for each stage. It is copied into the widget and never sent anywhere.";

    // ---- Copy moved out of Mods & Config when it retired (Task 12, P13): a card's "What this folder holds" and the meters rows ----

    public const string HoldsCantRead = "Couldn't read this folder.";
    public const string SettingsReadable = "Readable";
    public const string SettingsReadableDetail = "settings.json parses as JSON.";
    public static string SettingsUnreadableAt(int line, int column) => $"Unreadable: line {line}, column {column}";
    public const string SettingsUnreadableStrict = "Claude Code reads settings.json strictly: no comments, no trailing commas. Sanduhr changes nothing in it while it is unreadable.";
    public const string SettingsUnreadable = "Unreadable";
    public const string SettingsCantOpen = "settings.json can't be opened. Sanduhr changes nothing in it.";
    public const string SettingsMissing = "Missing";
    public const string SettingsMissingDetail = "This folder has no settings.json yet.";
    public const string StatuslineSanduhrs = "Sanduhr's statusline";
    public const string StatuslineSomeoneElses = "Someone else's statusline";
    public const string HoldsNone = "None";
    /// <summary>One hook event's line: "2 groups, 1 from Sanduhr".</summary>
    public static string HookGroups(int groups, int fromSanduhr) =>
        $"{groups} {(groups == 1 ? "group" : "groups")}" + (fromSanduhr > 0 ? $", {fromSanduhr} from Sanduhr" : "");
    /// <summary>The hooks summary: "3 groups on 2 events".</summary>
    public static string HooksOnEvents(int groups, int events) =>
        $"{groups} {(groups == 1 ? "group" : "groups")} on {events} {(events == 1 ? "event" : "events")}";
    public static string McpServerCount(int n) => $"{n} {(n == 1 ? "server" : "servers")}";
    public static string McpProjectsOwn(int n) => $"{n} {(n == 1 ? "project has" : "projects have")} their own servers.";
    /// <summary>CLAUDE.md's size: "40 lines, 2 KB".</summary>
    public static string ClaudeMdSize(int lines, long bytes) =>
        $"{lines} {(lines == 1 ? "line" : "lines")}, " + (bytes < 1024 ? $"{bytes} bytes" : $"{bytes / 1024.0:0.#} KB");
    public static string RetentionDays(int days) => $"{days} days.";
    public const string RetentionUnknown = "Unknown while settings.json is unreadable.";

    public const string MetersTurnedOn = "Meters mod turned on.";
    public const string MetersTurnedOff = "Meters mod turned off.";
    public const string MetersSwitchFailed = "Couldn't change the switch.";
    /// <summary>The meters consent dialog's primary button.</summary>
    public const string MetersConsentInstall = "Install";
    public const string MetersUpdateTitle = "Update the meters mod?";
    /// <summary>The Update question's body: the pinned version and the one this Sanduhr ships.</summary>
    public static string MetersUpdateBody(string? pinned, string? current) =>
        $"This folder's meters mod is pinned to {pinned ?? "the installed version"}. Updating points it at {current ?? "the new version"}, the version that ships with this Sanduhr. "
        + "The mod's files are copied again and one entry in this folder's settings.json changes. The old version's files stay until "
        + "Sanduhr prunes them.";
    public const string MetersUpdateConfirm = "Update";
    public const string MetersUpdateNotNow = "Not now";
    public const string MetersUpdated = "Meters mod updated.";
    public const string MetersAlreadyCurrent = "Meters mod is already current.";
    public const string MetersUpdateFailed = "Couldn't update the meters mod.";
    public const string MetersRemoveTitle = "Remove the meters mod?";
    public const string MetersRemoveBody = "Takes Sanduhr's entry out of this folder's settings.json and leaves any other mod alone.";
    public const string MetersRemoveConfirm = "Remove";
    public const string MetersRemoveCancel = "Cancel";
    public const string MetersRemoved = "Meters mod removed.";
    public const string MetersRemoveFailed = "Couldn't remove the meters mod.";
    /// <summary>ClaudeCodePage.xaml (Task 8).</summary>
    public const string ClaudeCodeFoldersSection = "Claude Code folders";
    public const string NoFolderInClaudeCode = "No Claude Code folder found. Add one with Add folder\u2026";

    // ---- Settings v2, slice 5: Accounts (spec, Accounts; WF1, WF27, WF29, WF30, WF32) ----

    /// <summary>The label prompt every embedded sign-in shows, and the manual key window's field (one name: "Label").</summary>
    public const string NameThisAccount = "Name this account";
    public const string AccountLabelField = "Label";
    /// <summary>Accounts, Data for this account: what Sanduhr keeps for the selected account.</summary>
    public const string AccountDataSection = "Data for this account";
    public const string LinkedFolders = "Linked folders";
    /// <summary>An account links at most one Claude Code folder (settings.json "account_folders" maps a label to one path).</summary>
    public static string LinkedFolderLine(string? folder) =>
        folder is null ? "No Claude Code folder is linked to this account." : $"Linked to {folder}.";
    /// <summary>Accounts, Data: the one per-account erase (WF1), spoken with the account.</summary>
    public const string ClearAccountHistory = "Clear this account's history…";
    public static string ClearAccountHistoryNamed(string account) => $"Clear this account's history, {account}";
    public static string ClearHistoryQuestion(string account) => $"Clear {account}'s meter history?";
    public const string ClearHistoryDetail =
        "This permanently removes the 30 days of meter history Sanduhr keeps for this account on this PC. The sparklines and the meter history start again from the next check. Its sign-in is kept.";
    public const string ClearHistoryConfirm = "Clear";
    public const string ClearHistoryCancel = "Cancel";
    public static string HistoryCleared(string account) => $"{account}'s meter history is cleared.";
    public const string HistoryNotCleared = "The meter history couldn't be cleared.";
    /// <summary>Accounts, Data: Remove account…, with everything it removes said under it (WF29, decision 7).</summary>
    public const string RemoveAccount = "Remove account…";
    public static string RemoveAccountNamed(string account) => $"Remove account, {account}";
    public static string RemoveAccountQuestion(string account) => $"Remove {account} from Sanduhr?";
    /// <param name="nameHidden">Streamer mode hides the name: the codename isn't the file's name, so the parenthesis is dropped (P18).</param>
    public static string RemoveAccountDetail(string account, bool nameHidden = false) =>
        $"Removes {account}'s sign-in from Windows Credential Manager, its meter history{(nameHidden ? "" : $" (history.{account}.json)")}, its Claude Code folder link, "
        + "the meter files Claude Code reads for it and its browser profile. It can't be undone.";
    public const string RemoveAccountConfirm = "Remove";
    public const string RemoveAccountCancel = "Cancel";
    /// <summary>Accounts while signed out (Principle 6).</summary>
    public const string SignedOutAccountsLine = "Claude Usage, Alerts and the meters fill in after you sign in.";

    // ---- Settings v2, slice 5: Alerts, Each limit (WF13) ----

    public const string EachLimitSection = "Each limit";
    public const string EachLimitCaption = "The limit card's menu on the widget changes the same switches.";
    public const string EachLimitListName = "Limits";
    public const string WarnForThisLimit = "Warn for this limit";
    public static string WarnForLimit(string limit) => $"{WarnForThisLimit}, {limit}";
    public const string ShowThisLimit = "Show this limit";
    public static string ShowLimit(string limit) => $"{ShowThisLimit}, {limit}";
    public const string HiddenUntilItResets = "Hidden until it resets";
    public const string HiddenUntilResetCaption = HiddenUntilItResets + ". Tick " + ShowThisLimit + " to bring it back now.";
    public const string EachLimitSignedOut = "Your limits show here after you sign in.";
    public const string EachLimitNotYet = "Your limits show here after Sanduhr checks your usage.";

    // ---- Settings v2, slice 5: Claude Usage, Meters (WF14, WF32) ----

    /// <summary>The Meters pill in Claude Usage (P10); SettingsIndex's subsection string must be this.</summary>
    public const string MetersView = "Meters";
    /// <summary>The Meters view's chart section ("meter history", WF14); was "History chart".</summary>
    public const string MeterHistorySection = "Meter history";
    public const string MetersIntro =
        "Rolling 30-day meter history, kept on this PC and never uploaded. Export it as CSV to read in a spreadsheet or with any agent.";
    public const string MeterHistorySignedOut = "Sign in to start recording your meters. Sanduhr keeps 30 days on this PC.";
    public const string MeterHistoryEmpty = "No meter history yet. It fills in each time Sanduhr checks your meters.";
    public const string ExportMeterHistoryTitle = "Export meter history";
    public const string MeterHistoryDialogTitle = "Meter history";

    // ---- Settings v2, slice 5: the Widget page (WF15, WF24, WF32) ----

    /// <summary>The Widget page's first line (WF21; slice 1 P14).</summary>
    public const string WidgetLine = "The widget is " + DefineWidget + ".";
    /// <summary>The page's options section in search; the page title is already "Widget".</summary>
    public const string WidgetOptions = "Widget options";
    public const string GraphStyleClassic = "Classic line";
    public const string GraphStyleHorizon = "Horizon bands";
    public const string DeepWorkLength = "Deep Work length";
    public const string DeepWorkMinutes = "minutes";
    public static string DeepWorkLengthSpoken => $"{DeepWorkLength}, {DeepWorkMinutes}";
    public const string DeepWorkRange = "Use a whole number of minutes from 1 to 120. Not saved.";
    public const string CompactModeCaption = "Ctrl+D on the widget does the same. The widget opens full size at each launch.";
    public const string LimitOrder = "Limit order";
    public const string LimitOrderListName = "Limits in the widget's order";
    public static string MoveLimitUp(string limit) => $"{MoveUp}, {limit}";
    public static string MoveLimitDown(string limit) => $"{MoveDown}, {limit}";
    public const string JustTheDeskOn = "Sanduhr is in Just the Desk: the widget is hidden.";
    public const string ShowTheWidget = "Show the widget";
    public const string ShowTheWidgetCaption = "Ends Just the Desk and shows the widget, which offers Sign in. The Desk stays on.";

    // ---- Settings v2, slice 5: Publish usage, What gets sent (WF28) ----

    public const string PublishSampleSection = "What gets sent";
    /// <summary>Every field group of the record (UsagePublisher.BuildPayload), and that the sample sends nothing (decision 10).</summary>
    public const string PublishRecordLine =
        "Each record carries the day, this PC's name, the folders you ticked, token counts in total, per limit and per project name, "
        + "and your meters' percentages, reset times and when they were read. Show a sample… builds the next one here and sends nothing.";
    public const string ShowASample = "Show a sample…";
    public const string SampleTitle = "The next record";
    /// <summary><paramref name="host"/> is null unless the endpoint is one the senders accept; <paramref name="tokenMissing"/> is true
    /// when the auth scheme needs a token and none is stored. Either way the record goes nowhere, and the header says so.</summary>
    public static string SampleHeader(string date, string? host, bool tokenMissing) =>
        host is null ? $"The next record, for {date}, has no endpoint it can go to yet, so it goes nowhere."
        : tokenMissing ? $"The next record, for {date}, would go to {host}, but it needs a token first, so it goes nowhere yet."
        : $"The next record, for {date}, goes to {host}.";
    /// <summary>The note under a sample with no folder ticked. <paramref name="sendable"/>: the endpoint is usable and any token is stored.</summary>
    public static string SampleNoFolders(bool sendable, bool dailyOn) =>
        !sendable ? "No folder is ticked under Folders to share."
        : dailyOn ? "No folder is ticked under Folders to share. Publish now waits for one; a daily or agent publish would send this record, "
            + "naming no folder or project."
        : "No folder is ticked under Folders to share, and Publish daily is off, so nothing sends this record.";
    public const string SampleClose = "Close";
    /// <summary>Settings, Widget, Limit order, when the widget has limits and every one is hidden.</summary>
    public const string LimitOrderAllHidden = "Every limit is hidden. Show one under Alerts, Each limit.";
    public const string SampleFailed = "The sample couldn't be built.";
    // ---- Settings v2, slice 6: Privacy (WF6, WF10) and About, Updates. Every row says what the code does (decisions 2, 3, 9). ----

    /// <summary>Privacy's first line.</summary>
    public const string PrivacyLine = "What Sanduhr sends off this PC, and what Claude can change on it.";
    /// <summary>Privacy's two sections (spec, Privacy: network and agents).</summary>
    public const string WhatLeavesThisPc = "What leaves this PC";
    public const string WhatClaudeCanChange = "What Claude can change";
    /// <summary>What leaves this PC: each row's three lines start with these.</summary>
    public const string NetworkWhenLabel = "When:";
    public const string NetworkCarriesLabel = "Carries:";
    public const string NetworkOffLabel = "Off:";

    // What leaves this PC, one row each (NetworkDisclosure.All, in its order).
    public const string NetClaudeUsage = "claude.ai, your usage";
    public static string NetClaudeUsageWhen =>
        $"When Sanduhr starts, after you sign in, every {(int)UsageSchedule.Refresh.TotalMinutes} minutes for the account in use, and when you refresh or switch accounts. "
        + $"After you sign in, add an account, switch accounts or leave demo mode, a first ask that fails for a passing reason is tried once more after {(int)UsageSchedule.RetryAfterFailure.TotalSeconds} seconds. "
        + $"If the hidden window stops, Sanduhr restarts it and asks again after {(int)TransportRecovery.FirstDelay.TotalSeconds} and {(int)TransportRecovery.SecondDelay.TotalSeconds} seconds, "
        + $"then every {(int)TransportRecovery.SteadyDelay.TotalMinutes} minutes. "
        + $"With {FollowAccount} on, every {(int)AccountFollow.CheckInterval.TotalMinutes} minutes for each other signed-in account. "
        + $"Another signed-in account linked to a Claude Code folder you are using is read up to every {(int)UsageSchedule.Refresh.TotalMinutes} minutes too.";
    public const string NetClaudeUsageCarries =
        "The account's claude.ai sign-in cookie (sessionKey, and cf_clearance when Cloudflare issued one), and the cookies claude.ai's own page sets. "
        + "In a hidden window Sanduhr opens claude.ai, and asks it for the account's organizations, usage and routine budget.";
    public static string NetClaudeUsageOff => $"Remove the account. For another account, also turn off {FollowAccount} and unlink its Claude Code folder.";

    public const string NetSignIn = "claude.ai, sign-in window";
    public const string NetSignInWhen = "Only while the sign-in window is open. It opens when you ask to sign in: Add account…, Update sign-in…, or the widget's own sign-in button.";
    public const string NetSignInCarries =
        "What you type into claude.ai's sign-in page. The window is a browser you drive: claude.ai's pages and, if you choose Continue with Google, Google's.";
    public const string NetSignInOff = "It opens only when you ask for it.";

    public const string NetWeather = "Open-Meteo, weather";
    public static string NetWeatherWhen =>
        "Look up asks once for the city you typed. The forecast runs only while the Desk is on, a place is set and Weather is on the Desk, "
        + $"at most every {(int)WeatherSchedule.Refresh.TotalMinutes} minutes, and again at once when you change the place or the unit.";
    public const string NetWeatherCarries =
        "The city name you typed, for Look up. For the forecast, the place's latitude and longitude rounded to 2 decimals, and your temperature unit. "
        + "No account, cookie or identifier; the user agent is just \"Sanduhr\".";
    public const string NetWeatherOff = "Clear the place, or hide the Weather piece.";

    public const string NetCatalog = "GitHub, widget catalog";
    public const string NetCatalogWhen =
        "The first time the Catalog section on Mods & Widgets opens after Settings does (you open it, or a link such as the Companion row's "
        + "Get one… opens it), when you press Check the catalog again, and when you install or update a pack.";
    public const string NetCatalogCarries =
        "Requests for the catalog, its signature and the packs you choose. No account, cookie or identifier; the user agent is just \"Sanduhr\".";
    public const string NetCatalogOff = "Leave the Catalog section closed.";

    public const string NetUpdates = "GitHub, updates";
    public static string NetUpdatesWhen =>
        $"Installs from GitHub only: at most once every {(int)UpdateCheck.Interval.TotalHours} hours, when Sanduhr starts or while it runs, "
        + "and whenever you press Check now. A newer release downloads in the background and installs only when you choose Restart to update.";
    public const string NetUpdatesCarries = "A request for Sanduhr's list of releases and, when one is newer, its files. No account, usage or file of yours.";
    public static string NetUpdatesOff => $"Turn off {CheckForUpdatesDaily}. Check now still asks once when you press it.";

    public const string NetStore = "Microsoft Store, updates";
    public static string NetStoreWhen =>
        $"Store installs only: every {(int)StoreUpdate.CheckInterval.TotalHours} hours while Sanduhr runs, it asks the Store whether an update is "
        + "waiting. When you choose Restart to update, the Store installs it.";
    public const string NetStoreCarries = "Windows asks the Store about Sanduhr's package. Sanduhr adds nothing of yours.";
    public const string NetStoreOff = "No switch on the Store build: the Microsoft Store keeps Sanduhr up to date.";

    public const string NetPublish = "Your endpoint, Publish usage";
    public static string NetPublishWhen =>
        "Each day at the first chance after the time you set, while Publish daily at is on. "
        + $"If a send fails, it tries again every {(int)PublishScheduler.RetryInterval.TotalMinutes} minutes until one succeeds. "
        + "It also sends when you press Publish now. Never without an endpoint, or without a token when your endpoint asks for one.";
    public const string NetPublishCarries =
        "The day, this PC's name, the folders you ticked, token counts in total, per limit and per project name, and your meters' percentages, "
        + "reset times and when they were read. The counts are per project name, so the project names in the folders you tick do leave this PC. "
        + "Your token goes in the header your endpoint asks for.";
    public const string NetPublishOff = "Turn off Publish daily at, and don't press Publish now.";

    /// <summary>A publish the endpoint answered with a 3xx: it fails, and says where it was sent (the host only, never the URL).</summary>
    public static string PublishRedirected(int code, string? host) =>
        host is { Length: > 0 }
            ? $"The endpoint redirected ({code}) to {host}. Sanduhr doesn't follow redirects; set the endpoint to the final address."
            : $"The endpoint redirected ({code}). Sanduhr doesn't follow redirects; set the endpoint to the final address.";

    public const string NetPublishAgent = "Your endpoint, when Claude asks";
    public const string NetPublishAgentWhen = "When Claude calls Sanduhr's publish_usage tool, and only while Publish daily at is on.";
    public const string NetPublishAgentCarries =
        "The same record as Publish usage, to the same endpoint, for the day in Claude's request. The widget sends it; the MCP server only passes the request on, on this PC.";
    public const string NetPublishAgentOff = "Turn off Publish daily at.";

    public const string NetThroughClaude = "Claude's service, through Claude Code";
    public const string NetThroughClaudeWhen =
        "When Claude calls one of Sanduhr's MCP tools. The MCP server makes no network call itself: it answers Claude Code on this PC, "
        + "and Claude Code sends the answer on with your conversation.";
    /// <summary>The paths an MCP answer carries (ping, get_desk, propose_theme), and the picture behind one of them. One wording for the row and the agent caption.</summary>
    private const string McpAnswerPaths =
        "the paths of a small copy of your wallpaper, of Sanduhr's usage file and of a theme Claude saves, which include your Windows user folder; "
        + "the picture itself, if you let Claude Code open that wallpaper copy";
    private const string McpAnswerThemeName = "your current theme's name when Claude proposes a theme";
    public const string NetThroughClaudeCarries =
        "What the tool answers: your meters, limits and plan, and a short code for the account; "
        + "your Desk's design (with the name you gave your weather place, if you set one), Launcher buttons, monitors, fonts and Start menu app names; "
        + "your wallpaper's colors; " + McpAnswerPaths + "; " + McpAnswerThemeName + "; "
        + "your Desk messages; the names of the Claude Code folders Sanduhr found; "
        + "and, for each folder where you let Claude read project totals, token counts per project and model with "
        + "project names, or full project paths when Claude asks.";
    public const string NetThroughClaudeOff =
        "Don't install Sanduhr's MCP server, or remove it from a folder. Each folder's project totals have their own switch.";

    // The rows whose Hosts read as words, not host names (preflight P4, P10, P13). The rows of real host names (Open-Meteo, the catalog) keep
    // theirs in NetworkDisclosure, from the code that asks them, and are the only ones set in a code font.
    public const string NetClaudeUsageHosts = "claude.ai (and what claude.ai's page loads)";
    public const string NetSignInHosts = "claude.ai, and Google's sign-in if you choose it";
    public const string NetUpdatesHosts = "api.github.com, github.com and GitHub's download hosts";
    public const string NetStoreHosts = "the Microsoft Store, through Windows";
    public const string NetPublishHosts = "the endpoint URL you set";
    public const string NetThroughClaudeHosts = "the service Claude Code sends your conversation to";

    /// <summary>Under the table (decision 3: the spec's sentence, qualified to Sanduhr's own calls).</summary>
    public const string NothingElseLeaves = "Nothing else leaves this PC from Sanduhr. Sanduhr has no analytics.";
    public const string NetWebView2Note =
        "The claude.ai rows run in Microsoft Edge WebView2 (the Edge runtime apps use). The pages are claude.ai's own, so claude.ai's code runs in them as in "
        + "your browser, and like Edge, WebView2 can contact Microsoft, for example to check a page with Microsoft Defender SmartScreen.";
    public const string NetYourBrowserNote =
        "A link you click, such as Open-Meteo's credit, a Launcher link or a font's download page, opens in your own browser. "
        + "That is your browser visiting the page; Sanduhr sends nothing with it.";
    public const string ReadPrivacyPolicy = "Read the privacy policy";

    // What Claude can change (AgentConsents.All).
    /// <summary>The spec's line, with "in a folder" dropped: a server set up by hand outside Claude Code folders works too (decision 11).</summary>
    public const string AgentsIntro = "Claude means any agent that uses Sanduhr's MCP server. Claude reaches these through that server, and each needs it installed.";
    /// <summary>The install status, counted the way the Claude Code cards count it (decision 11).</summary>
    public static string McpInstalledStatus(int installed, int folders) =>
        folders == 0 ? "Sanduhr found no Claude Code folder, so its MCP server isn't installed in one."
        : installed == 0 ? "Sanduhr's MCP server isn't installed in any Claude Code folder."
        : installed == folders ? (folders == 1 ? "Sanduhr's MCP server is installed in your Claude Code folder."
            : $"Sanduhr's MCP server is installed in all {folders} Claude Code folders.")
        : $"Sanduhr's MCP server is installed in {installed} of {folders} Claude Code folders.";
    public static string McpStatusLinkName => $"{LinkTitle(SettingsPage.ClaudeCode)}, MCP server";
    public const string AgentOn = "On";
    public const string AgentOff = "Off";
    public const string AgentAsksFirst = "Asks you first";
    public const string AgentNoSwitch = "No switch";
    public static string AgentFollowsPublish(bool on) => (on ? AgentOn : AgentOff) + ", with Publish daily at";
    /// <summary>The folder reads' state: "On for ~\.claude. Off for ~\.claude-work." (per discovered folder).</summary>
    public static string AgentFolders(IReadOnlyList<(string Folder, bool On)> folders)
    {
        if (folders.Count == 0)
            return "No Claude Code folder found.";
        static string Join(List<string> names) =>
            names.Count == 1 ? names[0] : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];
        var on = folders.Where(f => f.On).Select(f => f.Folder).ToList();
        var off = folders.Where(f => !f.On).Select(f => f.Folder).ToList();
        var parts = new List<string>();
        if (on.Count > 0) parts.Add($"On for {Join(on)}.");
        if (off.Count > 0) parts.Add($"Off for {Join(off)}.");
        return string.Join(" ", parts);
    }
    public const string AgentWatchersCaption = "Live cards on the Desk for work Claude is watching. Off: none show.";
    public const string AgentDeskDirectCaption =
        "On: Claude's Desk designs apply at once, after Sanduhr saves your Desk; one that needs a font you don't have still waits. "
        + "Off: each waits on the Desk for Apply or Dismiss.";
    public const string AgentSongsDirectCaption = "On: a look Claude suggests for a song is saved at once. Off: each waits as a card on Now playing.";
    public const string AgentFolderReadsCaption =
        "Per folder: token counts per project and per model, the folder's session archive, and its recent use beside your meters. "
        + "Project names go with them, or full project paths when Claude asks.";
    public const string AgentPublish = "Publish your usage when Claude asks";
    public const string AgentPublishCaption = "Claude can ask Sanduhr to send the day's record to your endpoint. It follows Publish daily at.";
    public const string AgentTheme = "Give the widget a theme";
    public const string AgentThemeCaption = "Claude can save a theme beside yours and switch the widget to it. Choose another one in Themes any time.";
    public const string AgentMessages = "Suggest Desk messages";
    public const string AgentMessagesCaption = "Claude's lines wait as Claude's suggestion on Message until you add or dismiss them.";
    public const string AgentWidgetPacks = "Make a widget pack";
    public const string AgentWidgetPacksCaption = "A companion, palette or effects pack Claude makes waits for your Apply or Dismiss.";
    public const string AgentReads = "Read your meters and your Desk";
    public const string AgentReadsCaption =
        "Your meters, limits and plan, and a short code for the account; "
        + "your Desk's design (with the name you gave your weather place, if you set one), Launcher buttons, monitors, fonts and Start menu app names; your wallpaper's colors; "
        + McpAnswerPaths + "; " + McpAnswerThemeName + "; your Desk messages; "
        + "and the names of the Claude Code folders Sanduhr found.";
    /// <summary>Privacy's foot: one line and "About settings…" to about#where (decision 12).</summary>
    public const string PrivacyWhereLine = "Sanduhr keeps its settings, history, session archive and sign-in keys on this PC. About lists each place.";
    public static string PrivacyWhereLinkName => $"{LinkTitle(SettingsPage.About)}, {WhereSettingsLive}";

    // About, Updates (spec, About; WF6).
    public const string UpdatesSection = "Updates";
    public const string CheckForUpdatesDaily = "Check for updates once a day";
    public static string UpdatesCaption =>
        $"Sanduhr asks GitHub for a newer release at most once every {(int)UpdateCheck.Interval.TotalHours} hours, when it starts or while it runs. "
        + "A new version downloads in the background and installs only when you choose Restart to update.";
    public const string CheckNow = "Check now";
    public const string CheckNowCaption = "Check now asks once, even with the switch off.";
    public const string UpdateChecking = "Checking GitHub for a newer Sanduhr…";
    public const string UpdateUpToDate = "Sanduhr is up to date.";
    public static string UpdateReady(string version) => $"Sanduhr {version} is ready. Restart to update is in the tray menu and on the widget.";
    public const string UpdateNotInstalled = "This copy wasn't installed by Sanduhr's installer, so it can't update itself.";
    public const string UpdateFailed = "Couldn't reach GitHub. Check your connection and try again.";
    /// <summary>About's jump link to the Reset section: the section's name, not the command's (preflight P15).</summary>
    public const string ResetJump = "Reset";
    public const string StoreKeepsUpToDate = "The Microsoft Store keeps Sanduhr up to date.";

    // Streamer mode (Settings v2, slice 7; spec, Streamer mode; WF7). The switch, its caption, Shuffle's words and the banner are the spec's.
    public const string StreamerSection = "Streamer mode";
    /// <summary>The tray's and the widget's right-click item: one name for both, so they can't drift (Principle 2).</summary>
    public const string StreamerMenuItemHeader = StreamerSection;
    public const string StreamerSwitch = "Streamer mode: hide accounts, folders and projects";
    public const string StreamerCaption = "Live numbers stay. Names become codenames until you choose to show one.";
    public const string StreamerCantReach = "Claude Code's own screen isn't covered. Check what it shows before you share it.";
    /// <summary>The rest of what it can't reach, exactly (slice 7 decision 11; preflight P4; owner P6 = A, 2026-10-26: MCP answers keep full paths).</summary>
    public const string StreamerCantReachMore =
        "Also not covered: what you type yourself (Desk messages, a place's Show it as name, a new account's name); windows Windows opens "
        + "for Sanduhr (Explorer, the Save dialog), which show your user folder; the task text on Claude Code's watcher cards (the folders in an absolute Windows path in it are hidden); what Claude proposes for the Desk, which shows as "
        + "Claude wrote it (the review bar's title and note, and the suggestions on Message); claude.ai's own pages in the sign-in window; and what Sanduhr's MCP server answers Claude, which keeps full paths and folder names and "
        + "becomes part of your conversation.";
    /// <summary>Under the switch: the search word "demo" lands here (spec, Search). True to DemoMode: it starts only from the command line or SANDUHR_DEMO.</summary>
    public const string StreamerDemoLine =
        "Demo mode is separate: it swaps in sample numbers and accounts, and starts only when Sanduhr is started with --demo or SANDUHR_DEMO=1.";
    public const string ShuffleCodenames = "Shuffle codenames…";
    public const string ShuffleCaption = "Draws new codenames for everything. Names you chose to show stay shown.";
    public const string ShuffleQuestion = "Shuffle codenames?";
    public const string ShuffleDetail = "Every codename changes. Viewers following a name will lose it.";
    public const string ShuffleConfirm = "Shuffle";
    public const string ShuffleCancel = "Cancel";
    /// <summary>The status line after a confirmed Shuffle, shown through a live region (preflight P14).</summary>
    public const string ShuffleDone = "New codenames drawn.";
    public const string ShowRealNameSection = "Show the real name";
    public const string ShowRealNameOnStream = "Show the real name on stream";
    /// <summary>The statusline consent's sentence on the folder name (P18): under streamer mode the line shows the folder's codename.</summary>
    public const string StatuslineNameClause =
        "The line can start with this folder's name (its codename while streamer mode is on) or the SANDUHR_LABEL you set in its "
        + "settings, worked out when it renders and never stored.";
    public const string AllowlistCaptionOn = "While streamer mode is on, this list shows codenames only.";
    public const string AllowlistCaptionOff = "Each name with its codename. A ticked name shows as it is while streamer mode is on.";
    public const string AllowlistEmpty = "Nothing has a codename yet. A name gets one the first time streamer mode hides it.";
    public static string AllowlistRowName(string shown) => $"{ShowRealNameOnStream}, {shown}";
    public static string AllowlistRowOff(string codename, string real) => $"{codename} · {real}";
    public static string CodenameCategoryTitle(CodenameCategory category) => category switch
    {
        CodenameCategory.Account => "Accounts",
        CodenameCategory.Email => "Emails",
        CodenameCategory.Folder => "Claude Code folders",
        CodenameCategory.Project => "Projects",
        CodenameCategory.Skill => "Skills",
        CodenameCategory.Repo => "Repos",
        CodenameCategory.Runner => "CI runners",
        CodenameCategory.Host => "Publish endpoints",
        CodenameCategory.City => "Places",
        CodenameCategory.AddOn => "Plugins, marketplaces and packs",
        _ => category.ToString(),
    };
    public const string StreamerBanner = "Streamer mode is on: names are codenames.";
    public static string StreamerBannerLinkName => $"{LinkTitle(SettingsPage.Privacy)}, {StreamerSection}";
    public const string StreamerIndicatorName = "Streamer mode on";
    public const string StreamerTooltipPrefix = "Streamer mode · ";
    /// <summary>The tray tooltip (spec: it starts with "Streamer mode · " while on). It names no account at HEAD (slice 7 decision 2).</summary>
    public static string TrayTooltip(bool streamer, string text) => streamer ? StreamerTooltipPrefix + text : text;
    /// <summary>The widget's account chip as spoken: the shown name, then the indicator while on (owner taste 4).</summary>
    public static string StreamerChipName(string shown, bool streamer) => streamer ? $"{shown}, {StreamerIndicatorName}" : shown;
    public const string ExportsKeepRealNames = "Exports keep real names.";
    /// <summary>Shown in place of a sample's machine name while streamer mode is on (owner taste 13).</summary>
    public const string StreamerThisPc = "this PC";
    public const string PlacesHiddenNote = "Streamer mode shows places as codenames. Turn it off to see their names.";
    public const string EndpointHiddenNote = "Streamer mode hides the address. Turn it off to edit it.";

    // ---- Settings v2, slice 8: Reset's folders (WF20, R12) and About's caption ----

    /// <summary>About, under Reset Sanduhr...: what it removes, so nobody has to open the dialog to learn it (spec, Reset). The spec's
    /// sentence left out themes and widgets (slice 8 decision 5).</summary>
    public const string ResetSanduhrCaption =
        "Removes your accounts, history, session archive, themes, widgets, settings, Desk messages and Sanduhr's Claude Code entries. You choose which.";
    /// <summary>Reset Sanduhr...: the folder list under the Claude Code part, spoken.</summary>
    public const string ResetFoldersName = "Claude Code folders to reset";
    /// <summary>Under the part when no folder holds a receipt or Sanduhr's MCP entry.</summary>
    public const string ResetNoFolders = "No Claude Code folder holds anything of Sanduhr's.";
    /// <summary>Shown while a folder is kept (the part or a row is unticked). Keeping is per kind: the entries and the files they run stay.</summary>
    public const string ResetFoldersKept = "Unticked folders keep Sanduhr's entries and the files they need. Remove them later in Settings, Claude Code.";
    /// <summary>Shown instead of <see cref="ResetFoldersKept"/> while the Claude Code part itself is unticked: the rows stay ticked but
    /// dimmed, and every folder is kept (preflight P12).</summary>
    public const string ResetFoldersPartOff = "With this part unticked, every folder keeps Sanduhr's entries and the files they need.";
    /// <summary>Also shown while a kept folder has meters and Accounts and sign-in keys is ticked.</summary>
    public const string ResetKeptSignIn = "Their meters stay empty until you sign in to Sanduhr again.";
    /// <summary>Also shown while a kept folder has meters and Settings is ticked: statusline_enabled goes with settings.json and comes back
    /// when the Claude Code page reads the folders.</summary>
    public const string ResetKeptSettings = "With the settings reset, their meters come back once you open Settings, Claude Code.";
    /// <summary>Also shown while a kept folder has an MCP server and Settings is ticked: mcp-settings.json and mcp_enabled go with the
    /// settings, and only Install sets them again (preflight P2).</summary>
    public const string ResetKeptMcpSettings =
        "With the settings reset, its MCP server stays but answers without your usage until you add it again in Settings, Claude Code.";
    /// <summary>Also shown while a kept folder was added by hand and Settings is ticked: cc_folders_added goes with settings.json.</summary>
    public const string ResetKeptAdded = "A folder you added yourself leaves that page with the settings: add it there again to remove its entries.";
    /// <summary>installs.json (or a copy set aside) can't be read: everything of Sanduhr's in Claude Code stays.</summary>
    public const string ResetReceiptsUnreadable =
        "Sanduhr's record of what it installed can't be read, so some entries can't be listed or removed here. Sanduhr keeps its files for them; remove the entries in Settings, Claude Code, or by hand.";

    /// <summary>The order the part's label names the kinds in, and each row's kinds line.</summary>
    public static readonly IReadOnlyList<IntegrationKind> ResetKindOrder =
        [IntegrationKind.Statusline, IntegrationKind.Mcp, IntegrationKind.Meters, IntegrationKind.Hooks];

    /// <summary>One kind of entry as the part's label names it.</summary>
    public static string ResetKindWord(IntegrationKind kind) => kind switch
    {
        IntegrationKind.Statusline => "statusline",
        IntegrationKind.Mcp => "MCP server",
        IntegrationKind.Meters => "meters mod",
        IntegrationKind.Hooks => "glow hook",
        _ => "",
    };

    /// <summary>A row's kinds line: "statusline, meters mod", in <see cref="ResetKindOrder"/>.</summary>
    public static string ResetFolderKinds(IEnumerable<IntegrationKind> kinds)
    {
        var set = kinds.ToHashSet();
        return string.Join(", ", ResetKindOrder.Where(set.Contains).Select(ResetKindWord));
    }

    // ---- Settings v2, slice 3: the Desk split ----

    /// <summary>A page's row of jump links, spoken (spec, Keyboard and screen reader).</summary>
    public const string JumpLinksName = "Sections on this page";
    /// <summary>Now playing's first jump link (its section is "Show what's playing in the widget").</summary>
    public const string NowPlayingJumpWidget = "In the widget";

    /// <summary>Desk: the piece rows' section (was "Where each piece sits").</summary>
    public const string PiecesSection = "Pieces";
    /// <summary>Desk: the clicks switch and the sign-in reminders.</summary>
    public const string DeskBehavior = "Behavior";
    /// <summary>Desk: the collapsed section holding Margins and OLED care.</summary>
    public const string DeskAdvanced = "Advanced";
    /// <summary>Desk: the map at the top of the page, and its fold's name below 600 DIP.</summary>
    public const string DeskLayoutMap = "Layout map";
    /// <summary>Desk, Layout map: it is a picture, not a control (WF5).</summary>
    public const string DeskMapCaption = "Where each piece sits now. Move pieces with the rows below or Arrange Desk…";
    /// <summary>Desk, Pieces: Order (WF5), live when another piece shares the place.</summary>
    public const string MoveUp = "Move up";
    public const string MoveDown = "Move down";
    /// <summary>An Order button spoken: "Move Weather up in bottom left".</summary>
    public static string MoveUpIn(string piece, string place) => $"Move {piece} up in {place.ToLowerInvariant()}";
    public static string MoveDownIn(string piece, string place) => $"Move {piece} down in {place.ToLowerInvariant()}";
    /// <summary>A row's Show box spoken: its visible label is the piece's name.</summary>
    public static string PieceShownSpoken(string piece) => $"{piece}, on the Desk";
    /// <summary>A feature page's button when its piece is hidden (Principle 1); it places the piece where Desk, Pieces would.</summary>
    public const string PlaceOnDesk = "Place it on the Desk";
    public const string NotOnDesk = "Not on the Desk.";
    /// <summary>Added to a placed piece's line while the Desk itself is off.</summary>
    public const string DeskIsOff = "The Desk is off: turn it on in Settings, Desk.";
    /// <summary>A feature page's line about its piece: "On the Desk: bottom right." or "Not on the Desk."</summary>
    public static string PieceStatus(string? place, bool deskOn) =>
        place is null ? NotOnDesk : $"On the Desk: {place.ToLowerInvariant()}." + (deskOn ? "" : " " + DeskIsOff);
    /// <summary>Desk Look's sections (spec, Desk Look).</summary>
    public const string FontsSection = "Fonts";
    public const string SizesSection = "Sizes";
    public const string InkAndColor = "Ink and color";
    public const string BackPlate = "Back plate";
    /// <summary>Desk Look's controls whose labels were literals.</summary>
    public const string ClockSize = "Clock size";
    public const string MessageSize = "Message size";
    public const string GlowAroundMessage = "Glow around the message";
    public const string DropShadow = "Drop shadow under the text";

    // ---- One theme for the widget and the Desk (theme match) ----
    /// <summary>Desk Look's first section (theme match).</summary>
    public const string ThemeMatchSection = "Match the widget theme";
    public const string FollowThemeSwitch = "Follow the widget theme";
    public const string FollowThemeCaption =
        "The Desk draws its colors, gradients, font, plate and shadow from the widget's theme, and changes with it. The look is kept in a theme of yours, so it travels with the theme.";
    public const string MatchNowButton = "Match now";
    public const string MatchNowCaption =
        "Copies the widget theme's look into the Desk's own settings once. The Desk keeps it when the theme changes.";
    /// <summary>Under the ink choices in Desk Look.</summary>
    public const string DeskInkCaption = "Automatic reads how bright your wallpaper image is behind each piece and picks light or dark ink, so it stays readable. It reads the wallpaper file only, never your screen. Picking one of the Colors switches the ink to Custom, which takes one hex color, or two to four with commas for a gradient, such as 8f5bd6,3a63e0,33fdff. A meter past the alert levels (warn and urgent, set in Settings, Alerts) takes a yellow or red stop in the middle of its color, in every ink.";
    /// <summary>Under Desk Look's own look controls while the Desk follows the theme.</summary>
    public const string ThemeLookSetsThis = "The widget theme sets this while the Desk follows it. Change it above, in Match the widget theme.";
    /// <summary>The status line after Follow on, an update or a new theme.</summary>
    public static string FollowingTheme(string theme) => $"The Desk follows {theme}.";
    public const string KeptAsOwnLook = "The Desk keeps this look as its own.";
    /// <summary>The incoming name of the design Match now saves first: "Before matching Obsidian".</summary>
    public static string MatchingTheme(string theme) => $"matching {theme}";
    public static string MatchedTheme(string theme) => $"The Desk now matches {theme}. Its look is its own, so it stays when the theme changes.";
    /// <summary>Appended to the Match now line when the readability guard turned the plate on.</summary>
    public const string ThemeMatchPlateNote = "The plate is on so the clock reads over this wallpaper.";
    /// <summary>Under the Follow switch while the guard holds the plate on or draws it stronger.</summary>
    public const string ThemeFollowPlateNote = "The plate is stronger so the clock reads over this wallpaper.";
    /// <summary>Appended to the Match now line when the clock may still be faint over this wallpaper at the plate's one fixed strength.</summary>
    public const string ThemeMatchFaintNote = "The clock may be faint over this wallpaper. Follow the widget theme keeps it readable.";
    public const string CouldNotSaveDeskFirst = "The Desk could not be saved first, so nothing was changed.";
    public static string ThemeNotWritten(string why) => $"The theme could not be written ({why}), so nothing was changed.";
    public static string AddOrNewQuestion(string theme) =>
        $"{theme} is one of your themes. The Desk's look can go into it, or into a new theme made from it.";
    public static string AddDeskLookTo(string theme) => $"Add the Desk look to {theme}";
    public const string MakeANewTheme = "Make a new theme";
    public const string ThemeMatchCancel = "Cancel";
    public static string WhereQuestion(string theme, bool canUpdate) => canUpdate
        ? $"The Desk follows {theme}. Save this change into it, or into a new theme?"
        : $"The Desk follows {theme}, a built-in or pack theme that stays as it is. Save this change as a new theme?";
    public static string UpdateTheme(string theme) => $"Update {theme}";
    public const string SaveAsNewTheme = "Save as a new theme";
    public const string NewThemeNamePrompt = "Name of the new theme";
    public static string ThemeUpdated(string theme) => $"Updated {theme}.";

    /// <summary>The theme's Desk look editor (Desk Look while following; the Theme Studio).</summary>
    public const string ThemeDeskLookHeading = "The theme's Desk look";
    public const string ThemeDeskFont = "Desk font in this theme";
    public const string ThemeInkColors = "Ink colors";
    public const string ThemeBarColors = "Meter bar colors";
    public const string ThemeMessageColors = "Message colors";
    public const string ThemeGlowColor = "Message glow color";
    public const string ThemePlateSwitch = "Plate behind the text in this theme";
    public const string ThemePlateColor = "Plate color";
    public const string ThemeShadowColor = "Shadow color";
    public const string ThemeDeskFromTheme = "From the theme";
    public const string DesksOwnFont = "The Desk's own font";
    public const string ThemeColorsProblem = "Use one to four hex colors, like #e8e4dc, #6c63ff.";
    public const string ThemeColorProblem = "Use one hex color, like #6c63ff.";

    /// <summary>The Theme Studio's font picker (theme match) and its first choice, no font (Segoe UI).</summary>
    public const string ThemeFontPicker = "Font";
    public const string ThemeDefaultFont = "Segoe UI (the default)";
    public const string ThemeStudioDeskNote =
        "The Desk shows these while it follows the widget theme (Settings, Desk Look, Match the widget theme).";
    /// <summary>Watchers and Launcher: the section that holds the piece's status line.</summary>
    public const string WatchersPlace = "Where they show";
    public const string LauncherPlace = "Where it shows";
    /// <summary>Launcher: the buttons, their pictures, Add app… and Add link….</summary>
    public const string LauncherButtons = "Buttons";
    /// <summary>Watchers: under "Show my CI runners' jobs" (WF8).</summary>
    public const string CiRunnersCaption = "Independent of the switch above.";
    /// <summary>The Watchers page's first line, and the Companion row's (WF21).</summary>
    public const string WatchersLine = "Watchers are " + DefineWatchers + ".";
    public const string CompanionLine = "The companion is " + DefineCompanion + ".";
    /// <summary>Desk, Pieces, the Companion row with no sprite installed. The link opens the catalog on Mods &amp; Widgets, showing sprites.</summary>
    public const string NoCompanionYet = "No companion yet.";
    public static string GetACompanion => $"Get one in {PageTitle(SettingsPage.Mods)}…";
    /// <summary>The Companion row when sprites are installed but every one is switched off; the link opens the Installed list showing widgets.</summary>
    public const string SpritesSwitchedOff = "Your sprites are switched off.";
    public static string TurnASpriteOn => $"Turn one on in {PageTitle(SettingsPage.Mods)}…";
    /// <summary>Desk, Designs: the collapsed section's one-line summary.</summary>
    public static string DesignsSummary(int saved, bool canPutBack) =>
        saved == 0 ? "None saved yet." : $"{saved} saved." + (canPutBack ? " " + PutMyDeskBack + " is ready." : "");
    /// <summary>The sidebar's Quit asks first (WF33): the question and its two buttons.</summary>
    public const string QuitQuestion = "Quit Sanduhr? The widget and the Desk close.";
    public const string QuitConfirm = "Quit";
    public const string QuitCancel = "Cancel";

    // ---- Core words (WF21): each is defined once, used as its home page's first line and in the tour.
    public const string DefineDesk = "the clock, meters and pieces Sanduhr draws on your desktop";
    public const string DefineWidget = "the small floating window with your meters";
    public const string DefinePiece = "one thing on the Desk";
    public const string DefineWatchers = "cards that show work in progress, from Claude or your CI";
    public const string DefineCompanion = "a small animated sprite on the Desk";
    public const string DefineMod = "an add-on that Claude Code loads";
    public const string DefineWidgetPack = "an add-on for Sanduhr: a theme, colors, a font or a companion";

    /// <summary>The Desk page's first line.</summary>
    public const string DeskLine = "The Desk is " + DefineDesk + ".";

    /// <summary>Every core word and its definition.</summary>
    public static IReadOnlyList<(string Word, string Definition)> CoreWords { get; } =
    [
        ("Desk", DefineDesk),
        ("Widget", DefineWidget),
        ("Piece", DefinePiece),
        ("Watchers", DefineWatchers),
        ("Companion", DefineCompanion),
        ("Mod", DefineMod),
        ("Widget pack", DefineWidgetPack),
    ];

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
        (StreamerSection, SettingsPage.Privacy),
        (ShuffleCodenames, SettingsPage.Privacy),
        (AddAccount, SettingsPage.Accounts),
        (SendTestAlert, SettingsPage.Alerts),
        (WarnForThisLimit, SettingsPage.Alerts),
        (ShowThisLimit, SettingsPage.Alerts),
        (ExportMeterHistory, SettingsPage.ClaudeUsage),
        (SaveAndApply, SettingsPage.Themes),
        (WhatsNew, SettingsPage.About),
        (TakeTheTour, SettingsPage.About),
        (HideWhatsNewAfterUpdates, SettingsPage.About),
        (CheckForUpdatesDaily, SettingsPage.About),
        (CheckNow, SettingsPage.About),
        (WhereSettingsLive, SettingsPage.About),
        (ThirdPartyNotices, SettingsPage.About),
        (ResetSanduhr, SettingsPage.About),
        (AddFolder, SettingsPage.ClaudeCode),
        (FollowAccount, SettingsPage.Accounts),
        (ClearAccountHistory, SettingsPage.Accounts),
        (RemoveAccount, SettingsPage.Accounts),
        (DeskSwitch, SettingsPage.Desk),
        (DeskInkPicker, SettingsPage.DeskLook),
        (ArrangeDesk, SettingsPage.Desk),
        (DeskPieceClicksSwitch, SettingsPage.Desk),
        (DeskPlateSwitch, SettingsPage.DeskLook),
        (WatchersSwitch, SettingsPage.Watchers),
        (CiRunnersSwitch, SettingsPage.Watchers),
        (DeskDirectSwitch, SettingsPage.Desk),
        (OledCareSwitch, SettingsPage.Desk),
        (PutMyDeskBack, SettingsPage.Desk),
        (ExportSessions, SettingsPage.ClaudeUsage),
        (SessionArchive, SettingsPage.ClaudeUsage),
        (FoldersToShare, SettingsPage.Publish),
        (ShowASample, SettingsPage.Publish),
        (ClaudeCodeGlowHook, SettingsPage.ClaudeCode),
        (TranscriptsKept, SettingsPage.ClaudeCode),
        (LetClaudeReadTotals, SettingsPage.ClaudeCode),
        (SnakeSoundAtFull, SettingsPage.Alerts),
        (DeskFont, SettingsPage.DeskLook),
        (MessageFont, SettingsPage.DeskLook),
        (MessageColor, SettingsPage.DeskLook),
        (FollowThemeSwitch, SettingsPage.DeskLook),
        (MatchNowButton, SettingsPage.DeskLook),
        (LauncherAddApp, SettingsPage.Launcher),
        (LauncherAddLink, SettingsPage.Launcher),
        (SignInReminders, SettingsPage.Desk),
        (NowPlayingInWidget, SettingsPage.NowPlaying),
        (NowPlayingStyle, SettingsPage.NowPlaying),
        (NowPlayingLooksDirect, SettingsPage.NowPlaying),
        (ClearLooks, SettingsPage.NowPlaying),
        (WeatherLookUp, SettingsPage.Weather),
        (WeatherShowAs, SettingsPage.Weather),
        (NowPlayingBarsGradient, SettingsPage.NowPlaying),
        (GlowWhere, SettingsPage.ClaudeCode),
        (WidgetCatalogCheck, SettingsPage.Mods),
        (WidgetCompanionPick, SettingsPage.Desk),
        (WidgetCompanionRoam, SettingsPage.Desk),
        (CompactMode, SettingsPage.Widget),
        (GraphStyle, SettingsPage.Widget),
        (KeepWidgetOnTop, SettingsPage.Widget),
        (DeepWorkLength, SettingsPage.Widget),
        (LimitOrder, SettingsPage.Widget),
        (ShowTheWidget, SettingsPage.Widget),
    ];

    /// <summary>
    /// Names that are gone, each with the one that replaced it. None may appear in a menu, the tour,
    /// What's New or a Settings label. <c>Whole</c> names match only a whole string ("Add account"
    /// is retired, "Add account…" is not); the rest match anywhere in a string, case included.
    /// </summary>
    public static IReadOnlyList<(string Old, string Now, bool Whole)> Retired { get; } =
    [
        ("Take the Tour", TakeTheTour, false),
        ("Show the companion on the Desk", DeskCompanion, false),
        ("Tray Shows", TrayShows, false),
        ("End Demo Mode", EndDemoMode, false),
        ("Sign In", SignIn, true),
        ("Add account", AddAccount, true),
        ("Manage accounts…", LinkTitle(SettingsPage.Accounts), false),
        ("Edit buttons…", LinkTitle(SettingsPage.Launcher), false),
        ("Alerts Settings…", LinkTitle(SettingsPage.Alerts), false),
        ("Save and apply", SaveAndApply, false),
        ("Save & Apply", SaveAndApply, false),
        ("Let agents show watchers", WatchersSwitch, false),
        ("Claude can read this folder's project totals", LetClaudeReadTotals, false),
        ("Edit as text…", MessageModeText, false),
        ("Back to the list", MessageModeList, false),
        ("Export CSV…", ExportMeterHistory, false),
        ("History vault", SessionArchive, false),
        ("Usage vault", SessionArchive, false),
        ("Erase archive", EraseSessionArchive, false),
        ("Open vault folder", OpenArchiveFolder, false),
        ("Homes to share", FoldersToShare, false),
        ("(statusline, MCP server)", ResetClaudeCodeEntries, false),
        ("Usage history and the vault", ResetMeterHistory, false),
        ("Desk: the clock and your Claude meters on the desktop", DeskSwitch, false),
        ("Glow when Claude needs you", ClaudeCodeGlowHook, false),
        ("Install again to update it", PieceOutdated, false),
        ("History kept", TranscriptsKept, false),
        ("Keep a local usage history", SessionArchive, false),
        ("Keep history", SessionArchive, false),
        ("Looks good", ThemeJsonValid, true),
        ("Save as:", SaveThemeAs, true),
        ("100% plays the !", SnakeSoundAtFull, false),
        ("Compact Mode", CompactMode, false),
        ("Expand", CompactMode, true),
        ("Deep Work (Focus)", DeepWork, false),
        ("Deep Work focus timer", DeepWork, false),
        ("Cooldown Snake", CooldownSnake, false),
        ("Cooldown snake game", CooldownSnake, false),
        ("Hide this tier", HideThisLimit, false),
        ("Edit messages…", LinkTitle(SettingsPage.DeskMessage), false),
        ("Cycle graph style", GraphStyle, false),
        ("Pin on top", KeepWidgetOnTop, false),
        ("Pin always on top", KeepWidgetOnTop, false),
        ("Don't show after updates", HideWhatsNewAfterUpdates, false),
        ("Where each piece sits", PiecesSection, true),
        ("Mods & Config", ModsAndWidgetsTitle, false),
        // Whole only (P3): "Widgets settings…" is inside "Mods & Widgets settings…", the page's own link title.
        ("Widgets settings…", LinkTitle(SettingsPage.Mods), true),
        // Slice 5: the account's name is its Label, in the key window as in the sign-in prompt (decision 8).
        ("Account name", AccountLabelField, true),
        // Slice 5: History's Clear became Accounts' per-account erase (WF1).
        ("Clear history", ClearAccountHistory, true),
        // Slice 5: the History page is Claude Usage's Meters view (WF14). Whole only: "meter history" and the Meters view's names are live.
        ("History", PageTitle(SettingsPage.ClaudeUsage), true),
        ("History chart", MeterHistorySection, true),
        ("History settings…", LinkTitle(SettingsPage.ClaudeUsage), true),
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

    /// <summary>
    /// What is wrong with the places <paramref name="text"/> names, empty when nothing is. Prose names a
    /// place one way only: "Settings, &lt;Page&gt;" or "Settings, &lt;Page&gt;, &lt;Section&gt;", where the
    /// page is a sidebar title and the section is the title of one of that page's anchors in
    /// <see cref="SettingsIndex"/>. "Settings ▸ X" and "Settings &gt; X" are the old forms. Only a
    /// capitalised word after a comma is read as a page or section, so "Settings, About, or the tray
    /// menu" and a literal that ends in "Settings, " pass. A known page title followed by another
    /// capitalised word in the same segment ("Settings, Desk Look, Font") is a longer, unknown title and
    /// is flagged as no page, and a section followed by another capitalised word is flagged the same way.
    /// </summary>
    public static IReadOnlyList<string> PathProblems(string text)
    {
        var found = new List<string>();
        if (string.IsNullOrEmpty(text))
            return found;
        foreach (var old in new[] { "Settings ▸", "Settings >", "Settings ›", "Settings →" })
        {
            if (text.Contains(old, StringComparison.Ordinal))
                found.Add($"\"{old}\": write \"Settings, <Page>, <Section>\"");
        }
        const string lead = "Settings, ";
        int at = 0;
        while ((at = text.IndexOf(lead, at, StringComparison.Ordinal)) >= 0)
        {
            at += lead.Length;
            var rest = text[at..];
            if (rest.Length == 0 || !char.IsUpper(rest[0]))
                continue;
            SettingsPage? page = null;
            foreach (var p in Pages)
            {
                if (StartsWithWord(rest, PageTitle(p)) && (page is null || PageTitle(p).Length > PageTitle(page.Value).Length))
                    page = p;
            }
            if (page is null)
            {
                found.Add($"no page \"{FirstWords(rest)}\" after \"Settings, \"");
                continue;
            }
            var after = rest[PageTitle(page.Value).Length..];
            // A capitalised word right after the matched title is part of a longer title we do not know.
            if (after.Length > 1 && after[0] == ' ' && char.IsUpper(after[1]))
            {
                found.Add($"no page \"{FirstWords(rest)}\" after \"Settings, \"");
                continue;
            }
            if (after.Length > 2 && after.StartsWith(", ", StringComparison.Ordinal) && char.IsUpper(after[2]))
            {
                var section = after[2..];
                string? best = null;
                foreach (var e in SettingsIndex.EntriesOn(page.Value))
                {
                    if (StartsWithWord(section, e.Title) && (best is null || e.Title.Length > best.Length))
                        best = e.Title;
                }
                // The page rule again: a capitalised word right after the matched title is part of a
                // longer title we do not know ("Ink and color" must not pass as "Ink").
                var tail = best is null ? "" : section[best.Length..];
                if (best is null || (tail.Length > 1 && tail[0] == ' ' && char.IsUpper(tail[1])))
                    found.Add($"no section \"{FirstWords(section)}\" on {PageTitle(page.Value)}");
            }
        }
        return found;
    }

    private static bool StartsWithWord(string text, string words) =>
        text.StartsWith(words, StringComparison.Ordinal)
        && (text.Length == words.Length || !char.IsLetterOrDigit(text[words.Length]));

    private static string FirstWords(string text)
    {
        var end = text.IndexOfAny([',', '.', ')', ';', ':']);
        return end < 0 ? text : text[..end];
    }
}
