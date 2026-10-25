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

    /// <summary>The sidebar: six groups, each page once. Widget is not a page (Show me on a widget card
    /// shows the widget itself). Settings v2 names the groups: "Extend" (it will hold widget packs, which
    /// are not Claude Code's) and "Desktop" (the Mac's word, and no longer its first page's title).</summary>
    public static IReadOnlyList<SettingsGroup> Sidebar { get; } =
    [
        new(null, [SettingsPage.General, SettingsPage.Accounts, SettingsPage.Alerts]),
        new("Usage", [SettingsPage.History, SettingsPage.ClaudeUsage, SettingsPage.Publish]),
        new("Extend", [SettingsPage.ClaudeCode, SettingsPage.Mods]),
        new("Widget", [SettingsPage.Themes]),
        new("Desktop", [SettingsPage.Desk, SettingsPage.DeskLook, SettingsPage.DeskMessage, SettingsPage.NowPlaying, SettingsPage.Weather,
            SettingsPage.Watchers, SettingsPage.Launcher, SettingsPage.Widgets]),
        new("Help", [SettingsPage.About]),
    ];

    // ---- Page titles added by Settings v2. Desk Look, Watchers and Launcher are pages since slice 3; Privacy (slice 6) and
    // Mods & Widgets (slice 4, the Mods page's new title) are still to come.
    public const string PrivacyTitle = "Privacy";
    public const string DeskLookTitle = "Desk Look";
    public const string WatchersTitle = "Watchers";
    public const string LauncherTitle = "Launcher";
    public const string ModsAndWidgetsTitle = "Mods & Widgets";

    /// <summary>The titles of pages still to come, in sidebar order.</summary>
    public static IReadOnlyList<string> TitlesToCome { get; } = [PrivacyTitle, ModsAndWidgetsTitle];

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
        SettingsPage.DeskLook => DeskLookTitle,
        SettingsPage.Watchers => WatchersTitle,
        SettingsPage.Launcher => LauncherTitle,
        SettingsPage.Widgets => "Widgets",
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
    /// <summary>The What's New window (settings.json "whats_new_hide_after_updates").</summary>
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
    /// <summary>Mods &amp; Config: re-reads the folder.</summary>
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
    /// <summary>Mods &amp; Config: Claude Code's own retention (cleanupPeriodDays), the fourth "history" (WF14).</summary>
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
    /// <summary>Desk, Where each piece sits: the companion, the sprite chosen in Settings, Widgets (spec 3.8 section 4).</summary>
    public const string DeskCompanion = "Companion";
    /// <summary>Desk, Launcher: opens the app picker.</summary>
    public const string LauncherAddApp = "Add app…";
    /// <summary>Desk, Launcher: asks for an https link and a label.</summary>
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
    /// <summary>Desk, Look: the ink the Desk draws in (spec 6.7). The section's name.</summary>
    public const string DeskInk = "Ink";
    /// <summary>Desk, Ink: the picker's label and spoken name (it was spoken "Ink", WF34).</summary>
    public const string DeskInkPicker = "Clock, date, Claude";
    /// <summary>Desk, Look: the Desk's font, one of the two "Font"s (WF12).</summary>
    public const string DeskFont = "Desk font";
    /// <summary>Message, Look: the message's font and color.</summary>
    public const string MessageFont = "Message font";
    public const string MessageColor = "Message color";
    /// <summary>Alerts: the Desk's sign-in reminder switch (settings.json "desk_sign_in_reminder", on by default).</summary>
    public const string SignInReminders = SignInReminder.Title;
    /// <summary>The first-run card's third action (settings.json "just_the_desk"); it has no Settings control.</summary>
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
    public const string WidgetUninstall = "Uninstall";
    /// <summary>Widgets: how a Claude-made or the user's own pack is listed (spec 3.8, section 3).</summary>
    public const string WidgetMadeHere = "made here";
    /// <summary>Widgets: an installed sprite made here takes the user's own PNG sheets, one per stage (spec 3.8, section 6).</summary>
    public const string WidgetUseOwnArt = "Use my own art…";
    /// <summary>Widgets, Desk companion (spec 3.8 section 4): the installed sprite that is the companion (one at a time).</summary>
    public const string WidgetCompanionPick = "Companion sprite";
    /// <summary>Widgets, Desk companion: places or hides the Desk's companion piece.</summary>
    public const string WidgetCompanionShow = "Show the companion on the Desk";
    /// <summary>Widgets, Desk companion: it walks the screen's edges (never with Windows' animations off).</summary>
    public const string WidgetCompanionRoam = "Let it roam";

    // ---- Settings v2, slice 3: the Desk split ----

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
    /// <summary>Desk, Pieces, the Companion row with no sprite installed. The link names the page that holds the catalog today.</summary>
    public const string NoCompanionYet = "No companion yet.";
    public static string GetACompanion => $"Get one in {PageTitle(SettingsPage.Widgets)}…";
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
    /// <summary>The Widgets page's first line (Mods &amp; Widgets' in slice 4).</summary>
    public const string WidgetPackLine = "A widget pack is " + DefineWidgetPack + ".";

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
        (AddAccount, SettingsPage.Accounts),
        (SendTestAlert, SettingsPage.Alerts),
        (ExportMeterHistory, SettingsPage.History),
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
        (CiRunnersSwitch, SettingsPage.Desk),
        (DeskDirectSwitch, SettingsPage.Desk),
        (OledCareSwitch, SettingsPage.Desk),
        (PutMyDeskBack, SettingsPage.Desk),
        (ExportSessions, SettingsPage.ClaudeUsage),
        (SessionArchive, SettingsPage.ClaudeUsage),
        (FoldersToShare, SettingsPage.Publish),
        (ClaudeCodeGlowHook, SettingsPage.ClaudeCode),
        (TranscriptsKept, SettingsPage.Mods),
        (LetClaudeReadTotals, SettingsPage.ClaudeCode),
        (SnakeSoundAtFull, SettingsPage.Alerts),
        (DeskFont, SettingsPage.Desk),
        (MessageFont, SettingsPage.DeskMessage),
        (MessageColor, SettingsPage.DeskMessage),
        (LauncherAddApp, SettingsPage.Desk),
        (LauncherAddLink, SettingsPage.Desk),
        (SignInReminders, SettingsPage.Alerts),
        (NowPlayingInWidget, SettingsPage.NowPlaying),
        (NowPlayingStyle, SettingsPage.NowPlaying),
        (NowPlayingLooksDirect, SettingsPage.NowPlaying),
        (ClearLooks, SettingsPage.NowPlaying),
        (WeatherLookUp, SettingsPage.Weather),
        (WeatherShowAs, SettingsPage.Weather),
        (NowPlayingBarsGradient, SettingsPage.NowPlaying),
        (GlowWhere, SettingsPage.ClaudeCode),
        (WidgetCatalogCheck, SettingsPage.Widgets),
        (WidgetCompanionShow, SettingsPage.Widgets),
        (WidgetCompanionRoam, SettingsPage.Widgets),
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
