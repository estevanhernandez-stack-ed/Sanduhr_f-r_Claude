using System.Reflection;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>
/// Backs the Settings window's pages. It owns one view model per page (accounts, alerts, history, Claude Usage and
/// Claude Code, publish, themes, the Desk pages, Now Playing, Weather and Widgets), the search box's
/// <see cref="Search"/>, the shortcut recorder, and the commands the About page and the sidebar's foot call: What's
/// New, the tour, open a folder or link, Reset and Quit.
/// </summary>
public sealed class SettingsViewModel
{
    public AccountsViewModel Accounts { get; }

    /// <summary>Backs the Themes tab (item 8): paste/save/apply, copy agent prompt,
    /// open folder, installed-themes management.</summary>
    public ThemesViewModel Themes { get; }

    /// <summary>Backs the History tab (item 9): 30-day chart with per-account /
    /// all-accounts overlay, Week/Month window, CSV export, Clear history.</summary>
    public HistoryTabViewModel History { get; }

    /// <summary>Backs the Claude Usage tab (WS-C): Overview / Trends / Sessions
    /// over the usage vault + live reader.</summary>
    public ClaudeCodeTabViewModel ClaudeCode { get; }

    /// <summary>Backs the Claude Code page: folders, accounts linked to them, Sanduhr's pieces and the glow.</summary>
    public ClaudeCodeViewModel ClaudeCodePage { get; }

    /// <summary>Backs the Mods &amp; Config page: a read-only look at one folder, plus the meters mod switch.</summary>
    public ModsConfigViewModel Mods { get; }
    /// <summary>Settings, Desk (spec 5.1).</summary>
    public DeskSettingsViewModel Desk { get; }

    /// <summary>Settings, Message (spec 6.2): the editor, rotation, special days, the look, Claude's suggestions.</summary>
    public DeskMessagePageViewModel MessagePage { get; }

    /// <summary>Called after the Message page saves the file or its settings (the Desk redraws).</summary>
    public Action? MessageChanged { get; set; }

    /// <summary>Settings, Now playing (spec 6.3).</summary>
    public NowPlayingPageViewModel NowPlayingPage { get; }

    /// <summary>Settings, Weather (spec 6.5).</summary>
    public WeatherPageViewModel WeatherPage { get; }

    /// <summary>Settings, Desk Look, Match the widget theme (theme match).</summary>
    public ThemeMatchViewModel ThemeMatch { get; }

    /// <summary>Settings, Mods &amp; Widgets (slice 4): the one list. Nothing is fetched until the catalog is opened.</summary>
    private ModsPageViewModel _modsPage = null!;

    /// <summary>Settings, Mods &amp; Widgets (slice 4): the one list. Tests and the harness put in their own (a fake catalog); the setter wires
    /// it to this window's pickers.</summary>
    public ModsPageViewModel ModsPage
    {
        get => _modsPage;
        internal set
        {
            _modsPage = value;
            value.SettingsChanged = PacksChanged;
        }
    }

    /// <summary>This PC's font families for the pickers (the bundled ones, the system's, the font packs that are on). A seam for tests.</summary>
    internal Func<IReadOnlyList<string>> PickerFonts { get; set; } =
        () => Sanduhr.App.Desk.DeskFonts.ForThisPc(new Paths()).PickerFamilies();

    /// <summary>
    /// A widget pack was switched, installed or removed (Settings v2 slice 4: every consumer honors the switch): the four font pickers, the Ink
    /// presets and the companion's sprites read again. The widget's theme list is App's (it reloads the themes in the same handler).
    /// </summary>
    public void PacksChanged()
    {
        var fonts = PickerFonts();
        Desk.ReloadFonts(fonts);
        MessagePage.ReloadFonts(fonts);
        ThemeMatch.Editor.ReloadFonts(fonts);
        Themes.Studio.ReloadFonts(fonts);
        Desk.Reload();   // the Ink presets and the companion's sprites
    }

    /// <summary>Backs the General tab's auto-start control (item 10).</summary>
    public GeneralViewModel General { get; }

    /// <summary>Backs the Alerts tab (WS-B): thresholds, projection/reset
    /// toggles, sound + snake sting, and the test-alert button.</summary>
    public AlertsViewModel Alerts { get; }

    /// <summary>Backs the Publish usage tab: opt-in daily publish to an endpoint
    /// the user chooses (preset, URL, auth, token, per-home share map,
    /// publish-now, status).</summary>
    public PublishViewModel Publish { get; }

    /// <summary>Assembly version for About (e.g. "3.0.0").</summary>
    public string Version { get; }

    /// <summary>About's "What's New…" button.</summary>
    public ICommand ShowWhatsNewCommand { get; }

    /// <summary>About's "Take the tour…" button.</summary>
    public ICommand ShowTourCommand { get; }

    /// <summary>The sidebar search.</summary>
    public SettingsSearchViewModel Search { get; } = new();

    /// <summary>About: the names the buttons and sections carry.</summary>
    public string WhatsNewTitle => SettingsNames.WhatsNew;
    public string TakeTheTourTitle => SettingsNames.TakeTheTour;
    public string WhereSettingsLiveTitle => SettingsNames.WhereSettingsLive;
    public string ThirdPartyNoticesTitle => SettingsNames.ThirdPartyNotices;
    public string ResetSanduhrTitle => SettingsNames.ResetSanduhr;

    /// <summary>The empty folder lists' link to the Claude Code page, in the names table's "&lt;Page&gt; settings…" form.</summary>
    public string ClaudeCodeSettingsLink => SettingsNames.LinkTitle(SettingsPage.ClaudeCode);
    public string AboutVersionLine => $"Sanduhr für Claude {Version}";

    /// <summary>About, Third-party notices: the packages the app ships with.</summary>
    public IReadOnlyList<ThirdPartyPackage> Packages => ThirdPartyNotices.Packages;
    public IReadOnlyList<ThirdPartyFont> Fonts => ThirdPartyNotices.Fonts;
    public IReadOnlyList<ThirdPartyPackage> DataServices => ThirdPartyNotices.DataServices;

    /// <summary>About, Where settings live: the folder holding settings, history, themes and logs. On a Store
    /// install, the package's LocalCache copy when it exists (see <see cref="DataFolders"/>).</summary>
    public string SettingsFolder { get; }

    /// <summary>About, Where settings live: the usage vault (PRIVACY.md: <c>%LOCALAPPDATA%\Sanduhr\vault\</c>,
    /// or the package's LocalCache copy on a Store install).</summary>
    public string LocalFolder { get; }

    /// <summary>About, Where settings live: <c>%USERPROFILE%\.sanduhr</c>, the files Claude Code, the MCP server,
    /// the statusline and the hooks read.</summary>
    public string ClaudeCodeFilesFolder { get; }

    /// <summary>Opens a folder from About in File Explorer.</summary>
    public ICommand ShowInExplorerCommand { get; }

    /// <summary>Opens a third-party package's page in the browser.</summary>
    public ICommand OpenLinkCommand { get; }

    /// <summary>About's "Reset Sanduhr…" button: asks the orchestrator to open the reset dialog.</summary>
    public ICommand ResetSanduhrCommand { get; }

    /// <summary>Connected by the orchestrator: opens the reset dialog. Null does nothing.</summary>
    public Func<Task>? ResetRequested { get; set; }

    /// <summary>The sidebar foot's "Quit Sanduhr": asks first (WF33), then runs the tray's quit path.</summary>
    public ICommand QuitCommand { get; }

    /// <summary>Connected by the orchestrator: the same quit the tray's Quit runs. Null does nothing.</summary>
    public Action? QuitRequested { get; set; }

    /// <summary>What the sidebar's Quit asks first (spec, General). The tray's Quit stays immediate: using the tray is an explicit act.</summary>
    public const string QuitQuestion = SettingsNames.QuitQuestion;

    /// <summary>Tests answer the Quit question here instead of showing it.</summary>
    internal Func<bool>? ConfirmQuitHook { get; set; }

    private System.Windows.Window? _owner;

    /// <summary>The Quit question parents to the Settings window.</summary>
    public void AttachOwner(System.Windows.Window owner) => _owner = owner;

    private bool ConfirmQuit() => ConfirmQuitHook is { } hook
        ? hook()
        : Sanduhr.App.Views.ThemedDialog.Show(_owner, SettingsNames.QuitSanduhr, QuitQuestion,
              System.Windows.MessageBoxButton.YesNo, Sanduhr.App.Views.ThemedDialogKind.Warning,
              primaryLabel: SettingsNames.QuitConfirm, secondaryLabel: SettingsNames.QuitCancel) == System.Windows.MessageBoxResult.Yes;

    /// <summary>Backs the General tab's Shortcuts section (global shortcut + recorder).</summary>
    public ShortcutsViewModel Shortcuts { get; }

    public SettingsViewModel(WidgetViewModel widget, Func<Task> addAccountAsync, Func<string, Task> updateSignInAsync,
        GlobalHotKeyService? hotKeys = null, Action? showWhatsNew = null, Action? showTour = null)
    {
        ShowWhatsNewCommand = new RelayCommand(() => showWhatsNew?.Invoke());
        ShowTourCommand = new RelayCommand(() => showTour?.Invoke());
        Accounts = new AccountsViewModel(widget, addAccountAsync, updateSignInAsync);
        Themes = new ThemesViewModel(widget);
        History = new HistoryTabViewModel(widget);
        ClaudeCode = new ClaudeCodeTabViewModel(widget, new LocalCcViewModel(
            widget, widget.LoadLocalCcShowBreakdowns(), widget.SaveLocalCcShowBreakdowns),
            new CcTrendsViewModel(widget),
            new CcLedgerViewModel(widget, widget.LoadLedgerGroupByProject(), widget.SaveLedgerGroupByProject));
        ClaudeCodePage = new ClaudeCodeViewModel(widget);
        Mods = new ModsConfigViewModel(widget);
        Desk = new DeskSettingsViewModel(new SettingsStore(new Paths()), Sanduhr.App.Desk.DeskFonts.ForThisPc(new Paths()).PickerFamilies(),
            inkPresets: () => PackInkPresets.Merged(new WidgetStore(new Paths().WidgetsDir, Sanduhr.App.Services.WidgetCatalogService.EncodePng)));
        var msgPaths = new Paths();
        MessagePage = new DeskMessagePageViewModel(new SettingsStore(msgPaths), System.IO.Path.Combine(msgPaths.AppDataDir, "messages.txt"),
            () => !ResetGuard.Done, () => MessageChanged?.Invoke(), Sanduhr.App.Desk.DeskFonts.ForThisPc(new Paths()).PickerFamilies(),
            Sanduhr.App.Desk.DeskMessageHandoffService.Current?.Handoff);
        NowPlayingPage = new NowPlayingPageViewModel(new SettingsStore(new Paths()), Sanduhr.App.Desk.NowPlayingHub.Current, Sanduhr.App.Desk.NowPlayingLookService.Current);
        WeatherPage = new WeatherPageViewModel(new SettingsStore(new Paths()));
        ThemeMatch = new ThemeMatchViewModel(new ThemeMatchService(new SettingsStore(new Paths()), widget)
            { FontAvailable = family => Sanduhr.App.Desk.DeskFonts.ForThisPc(new Paths()).Has(family) },
            () => widget.Palette.Definition, Sanduhr.App.Desk.DeskFonts.ForThisPc(new Paths()).PickerFamilies());
        // Follow on and off and Match now change the Desk's switch or its own look: the pages that show them read them again, and the
        // running Desk follows (App sets Desk.Changed).
        ThemeMatch.Changed = () =>
        {
            Desk.RefreshFromStore();
            MessagePage.RefreshSettings();
            NowPlayingPage.ReloadSettings();
            Desk.Changed?.Invoke();
        };
        // Each feature page says where its Desk piece sits and can place it (Settings v2, Principle 1): one line per piece,
        // reading and writing the same desk_layout as Desk, Pieces.
        MessagePage.Piece = Desk.PieceStatus("message");
        NowPlayingPage.Piece = Desk.PieceStatus("nowPlaying");
        WeatherPage.Piece = Desk.PieceStatus("weather");
        // The glow's spot is placed by the Claude Code page through its own store: the Desk page reads it again (App replaces this
        // with one that also reloads the running Desk).
        ClaudeCodePage.DeskReload = Desk.Reload;
        // The two pages share one folder list and one set of installed pieces.
        ClaudeCodePage.FoldersChanged += Mods.ReloadFolders;
        ClaudeCodePage.PiecesChanged += Mods.Refresh;
        Mods.OwnModChanged += ClaudeCodePage.Reload;
        General = new GeneralViewModel(widget: widget);
        ModsPage = new ModsPageViewModel(host: ClaudeCodeHost.For(widget));
        Shortcuts = new ShortcutsViewModel(new SettingsStore(new Paths()), hotKeys);
        Alerts = new AlertsViewModel(widget, () =>
        {
            widget.AlertService?.DeliverTest();
            return Task.CompletedTask;
        });
        Publish = new PublishViewModel(widget.Publish);
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        Version = v is null ? "3.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";

        // The real folders this build uses (a dev build's SANDUHR_DEV_PROFILE included).
        var paths = new Paths();
        var folders = DataFolders.Resolve(paths);
        SettingsFolder = folders.Settings;
        LocalFolder = folders.Vault;
        ClaudeCodeFilesFolder = paths.ExternalDir;
        ShowInExplorerCommand = new RelayCommand<string>(OpenFolder,
            folder => !string.IsNullOrEmpty(folder) && System.IO.Directory.Exists(folder));
        OpenLinkCommand = new RelayCommand<string>(OpenLink);
        QuitCommand = new RelayCommand(() =>
        {
            if (ConfirmQuit())
                QuitRequested?.Invoke();
        });
        ResetSanduhrCommand = new RelayCommand(async () =>
        {
            try { if (ResetRequested is { } reset) await reset(); }
            catch (Exception) { /* the dialog reports its own failures */ }
        });
    }

    private static void OpenFolder(string? folder)
    {
        // Opens what exists; never creates a folder (a fresh install may not have a vault yet).
        if (string.IsNullOrEmpty(folder) || !System.IO.Directory.Exists(folder))
            return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{folder}\"")
            {
                UseShellExecute = true,
            });
        }
        catch
        {
            // Opening Explorer is a convenience; the path is on screen either way.
        }
    }

    private static void OpenLink(string? url)
    {
        // Only the fixed https links from ThirdPartyNotices reach here.
        if (url is null || !url.StartsWith("https://", StringComparison.Ordinal))
            return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
        }
    }
}
