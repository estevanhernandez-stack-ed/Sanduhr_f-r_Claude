using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;
using Sanduhr.App.Services;
using Sanduhr.App.Theming;
using Sanduhr.App.Updates;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App;

/// <summary>
/// Application entry. Runs as a tray-resident widget (LSUIElement-equivalent —
/// the panel has no taskbar button; <c>ShutdownMode=OnExplicitShutdown</c> keeps
/// the process alive when the window is hidden). Wires the obsidian theme, the
/// <see cref="WidgetViewModel"/>, the glass window, and the tray icon, then
/// kicks the first fetch.
/// </summary>
public partial class App : Application
{
    private TaskbarIcon? _tray;
    private WidgetViewModel? _vm;
    private MainWindow? _window;
    private Icon? _trayIcon;
    private SettingsWindow? _settingsWindow;
    private DispatcherTimer? _trayRotateTimer;
    private GlobalHotKeyService? _hotKeys;
    private ClaudeCodeGlowService? _glow;
    private Desk.DeskController? _desk;
    /// <summary>This PC's font families for the theme's font (theme match): drawn when present, warned about when not. Read again when a
    /// font pack comes or goes.</summary>
    private Desk.DeskFonts _themeFonts = new([]);
    private Desk.NowPlayingHub? _nowPlaying;
    private Desk.DeskMessageSource? _messages;
    private Desk.DeskMessageHandoffService? _handoff;
    private Desk.DeskDesignService? _deskDesign;
    private Desk.WidgetReviewService? _widgetReview;
    private Desk.NowPlayingLookService? _looks;
    private Desk.WatcherStore? _watchers;
    private Desk.DockerPipe? _dockerPipe;
    private Desk.RunnerWatchService? _runners;

    /// <summary>The CI runners status changed (spec ci-watchers-3.9); raised on the UI thread for an open Settings.</summary>
    private event Action? RunnerStatusShown;
    private LauncherAppCache? _launcherApps;
    private MenuItem? _endDemoItem;
    private MenuItem? _restartToUpdateItem;
    private MenuItem? _restartItem;
    private RestartToUpdateService? _restartToUpdate;
    private bool _quitting;

    /// <summary>Where Settings was when it last closed, for the app's life (spec, Reopen where it was left). Never saved.</summary>
    private SettingsLink? _lastSettings;

    /// <summary>The claim on this data folder (Program.Main). A second launch asks it to show the widget, or Settings at its `--settings` link.</summary>
    internal SingleInstance? Instance { get; init; }

    /// <summary>How long a quit waits for WebView2's browser processes to close by themselves before ending them.</summary>
    private static readonly TimeSpan BrowserExitWait = TimeSpan.FromSeconds(5);

    /// <summary>Where this install stands with the welcome tour, decided once at launch.</summary>
    private TourState? _tourState;
    /// <summary>The tour opens after a first successful fetch at most once a launch.</summary>
    private bool _tourOfferedThisLaunch;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Settings whose shape changed (S6: hotkey_settings as a per-action map), once, before any read.
        var startupSettings = new SettingsStore(new Sanduhr.Core.Paths());
        startupSettings.RunMigrations();
        // The projection sanduhr-mcp reads (consent, publish readiness) lives outside AppData; bring it
        // in line with settings.json now, whatever wrote settings last.
        startupSettings.SyncMcpExport();

        _vm = new WidgetViewModel();
        // A theme's font (theme match): drawn when this PC has the family, bundled, installed or from a font pack; the theme lint warns
        // about one it lacks. Set before the first ApplyThemeResources so the first paint is in the theme's font.
        _themeFonts = Desk.DeskFonts.ForThisPc(new Sanduhr.Core.Paths());
        _vm.FontAvailable = f => _themeFonts.Has(f);
        Sanduhr.App.Theming.ThemePalette.ResolveFont = f => _themeFonts.Has(f) ? _themeFonts.Families(f).Regular : null;
        // Apply the saved theme's palette to the Application resources before the
        // window is shown so the first paint is correct (no obsidian→saved flash).
        _vm.ApplyThemeResources();
        // A live theme switch (strip or Settings) raises ThemeChanged — the window
        // swaps its backdrop (Mica for glass themes, solid for a Matrix opt-out).
        _vm.ThemeChanged += p => _window?.ApplyBackdropForTheme(p);
        _vm.TrayReadingChanged += OnTrayReadingChanged;
        _vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(WidgetViewModel.TrayMode))
                SyncTrayRotation();
            if (args.PropertyName == nameof(WidgetViewModel.IsDemo) && _endDemoItem is not null)
                _endDemoItem.Visibility = _vm.IsDemo ? Visibility.Visible : Visibility.Collapsed;
        };
        _vm.FetchSucceeded += OnFetchSucceeded;
        _vm.SignInRequested += () => RunSignInAsync(embedded: true);
        _vm.ReauthRequested += RunReauthAsync;
        _vm.ManualReauthRequested += RunManualReauthAsync;
        _vm.PasteKeyRequested += () => RunSignInAsync(embedded: false);
        _vm.SettingsRequested += () => ShowSettingsAsync();
        // "<Page> settings…" from the tier card and the account menus lands on its section (spec, Menus table).
        _vm.SettingsPageRequested += page => ShowSettingsAsync(page, SettingsIndex.MenuAnchor(page));
        _vm.JustTheDeskRequested += ChooseJustTheDesk;
        // Keep an open Settings window's account list in sync with quick-switches
        // made elsewhere (tray / widget chip / right-click).
        _vm.AccountsChanged += () => _settingsWindow?.ViewModel.Accounts.Reload();

        _window = new MainWindow { DataContext = _vm };
        // Just the Desk with no account (spec 7.5): the widget starts hidden and the tray is the way back.
        // The handle is created without showing, so the glow, alerts and tray have a window to point at.
        if (JustTheDesk.WidgetStartsHidden(_vm.JustTheDesk, _vm.AccountStore.ListAccounts().Count > 0,
                new SettingsStore(new Sanduhr.Core.Paths()).LoadDesk().Enabled))
            new System.Windows.Interop.WindowInteropHelper(_window).EnsureHandle();
        else
            _window.Show();

        _vm.AttachAlertService(new AlertService(new Sanduhr.Core.Paths(), ShowWindow));

        SetupTray();
        SyncTrayRotation();
        Instance?.Listen(message => Dispatcher.BeginInvoke(() => { if (!_quitting) OnSecondLaunch(message); }));
        SetupHotKeys();
        StartGlow();
        StartDesk();

        // Session archive (WS-C): consent resolves BEFORE the service attaches —
        // attach order is the consent gate; Start()'s first fetch cycle then
        // kicks the initial backfill through the attached service.
        var vaultService = new VaultService(new SettingsStore(new Sanduhr.Core.Paths()), _vm.CcReader);
        if (vaultService.NeedsConsentPrompt)
            vaultService.SaveConsent(VaultConsentDialog.ShowConsent(_window, vaultService.DetectedRootNames()));
        _vm.AttachVaultService(vaultService);

        // Publish usage: everything defaults off (toggle + every home, no endpoint),
        // so attaching is inert until the user opts in under Settings, Publish usage.
        // The publish token rides the same Credential Manager seam as the accounts.
        _vm.AttachPublishService(new UsagePublishService(
            new SettingsStore(new Sanduhr.Core.Paths()),
            _vm.CcReader,
            new Sanduhr.Core.Paths(),
            new PublishTokenStore(new WindowsCredentialManager(AccountStore.Service))));

        // propose_theme (MCP): the widget answers theme proposals dropped next to the
        // snapshot. Lint, save under themes\, apply when asked, reply. Watches the
        // request file so a proposal from the terminal lands within a second.
        var themePaths = new Sanduhr.Core.Paths();
        _vm.AttachThemeHandoffService(new ThemeHandoffService(themePaths, _vm, msg =>
        {
            // PRIVACY.md contract: operation + exception type only, never theme content.
            try { System.IO.File.AppendAllText(themePaths.LogFile, $"{DateTime.UtcNow:o} {msg}{Environment.NewLine}"); }
            catch { }
        }));

        // Restart to update (3.8 spec 8b): one offer for both channels, nothing without a click. A Store
        // update cannot replace files this process holds (3.4.1, 2026-09-14), so on yes Sanduhr closes its
        // WebView2 helpers, registers for restart and asks the Store to install now. The Store check is
        // inert on the Velopack and dev builds, which have no Store identity.
        void LogUpdate(string msg)
        {
            try { System.IO.File.AppendAllText(themePaths.LogFile, $"{DateTime.UtcNow:o} {msg}{Environment.NewLine}"); }
            catch { }
        }
        _restartToUpdate = new RestartToUpdateService(_vm,
            new AppRestartHost(ReleaseBrowsersForUpdateAsync, ResumeBrowsersAfterUpdate, Instance, RestartApp, LogUpdate),
            LogUpdate);
        _restartToUpdate.OfferChanged += UpdateRestartItems;
        _vm.RestartToUpdateRequested += () => _restartToUpdate?.Accept();
        _vm.RestartRequested += RestartApp;
        _vm.AttachStoreUpdateService(new StoreUpdateService(_restartToUpdate, OwnerWindowHandle, log: LogUpdate));

        // Background, in order, each step on its own: the one-time re-point of pre-3.5.0 registrations to
        // %USERPROFILE%\.sanduhr, then the statusline bridge (WS-E) and the MCP integration refresh. The
        // widget is the updater for both: the script and the server have no update channel of their own,
        // so a newer build rewrites the installed copy on start while the integration is enabled
        // (version-dir indirection means running sessions keep their pinned copy, no lock fight).
        // Nothing here may throw out of the task, and none of it delays the first paint.
        bool statuslineEnabled = _vm.LoadStatuslineEnabled();
        bool mcpEnabled = _vm.LoadMcpEnabled();
        string statuslineBinDir = _vm.StatuslineBinDir;
        string externalDir = _vm.SanduhrExternalDir;
        string appDataDir = _vm.SanduhrAppDataDir;
        _ = Task.Run(() =>
        {
            try { ExternalDirMigrationRunner.Run(startupSettings, new Sanduhr.Core.Paths()); }
            catch { }

            if (statuslineEnabled)
            {
                try
                {
                    new StatuslineInstaller(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        statuslineBinDir).InstallScript();
                }
                catch { }
            }

            if (mcpEnabled)
            {
                try
                {
                    string mcpSource = System.IO.Path.Combine(AppContext.BaseDirectory, "mcp");
                    if (System.IO.Directory.Exists(mcpSource))
                    {
                        new McpIntegrationInstaller(
                            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                            appDataDir, externalDir: externalDir).RefreshInstall(mcpSource);
                    }
                }
                catch { }
            }
        });

        // Load the active account's stored credential + fetch real usage now.
        _vm.Start();

        // Demo mode (screenshots): `--demo` or SANDUHR_DEMO=1. Session-only sample numbers,
        // labelled as demo on the widget and the tray; nothing is fetched or written.
        if (DemoMode.IsRequested(Environment.GetCommandLineArgs().Skip(1),
                Environment.GetEnvironmentVariable(DemoMode.EnvironmentVariable)))
            _vm.StartDemo();

        // Best-effort GitHub/Velopack update probe — fire-and-forget so a slow or offline
        // network never delays the first paint. No-ops on the Store/MSIX install and in dev.
        // A downloaded release joins the same Restart to update offer as the Store's.
        _ = new UpdateChecker().CheckForUpdatesAsync((version, installer) =>
            Dispatcher.BeginInvoke(() => _restartToUpdate?.Offer(version, installer)));

        ScheduleWhatsNewAtLaunch();

        // `Sanduhr.exe --settings <page>[#<anchor>]` as the first launch: Settings opens there once the widget is up.
        if (SettingsIndex.LinkFromArgs(Environment.GetCommandLineArgs().Skip(1)) is { } startLink)
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => OpenSettingsLink(startLink)));
    }

    /// <summary>The app version as Major.Minor.Build, the same shape Settings shows.</summary>
    private static string AppVersion
    {
        get
        {
            var v = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
            return v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    /// <summary>
    /// What's New after an update: decide from the stored versions, record the version before
    /// showing, and open the window two seconds later so the widget settles first.
    /// </summary>
    private void ScheduleWhatsNewAtLaunch()
    {
        if (_vm is null)
            return;
        try
        {
            var store = new SettingsStore(new Sanduhr.Core.Paths());
            var current = AppVersion;
            var lastSeen = store.LoadWhatsNewLastSeen();
            // After Start(): it migrates legacy credentials, so the account list is settled.
            var fresh = WhatsNew.IsFresh(lastSeen, store.LoadWhatsNewInstalledVersion(),
                _vm.AccountStore.ListAccounts().Count);
            var onboarding = JustTheDesk.Onboarding(_vm.ActiveAccount is not null, store.LoadJustTheDesk().On);

            // The tour is decided before What's New records anything: pending only on a fresh
            // install (no account, nothing seen). While it is pending, What's New waits for it.
            var accounts = _vm.AccountStore.ListAccounts().Count;
            var existingTour = WelcomeTour.Parse(store.LoadTourState());
            _tourState = WelcomeTour.Decide(existingTour, fresh, accounts, lastSeen);
            if (existingTour != _tourState)
                store.SaveTourState(WelcomeTour.Format(_tourState.Value));

            var decision = WhatsNew.AtLaunch(lastSeen, current, fresh, onboarding,
                store.LoadWhatsNewHideAfterUpdates(), tour: _tourState == TourState.Pending);

            if (fresh)
                store.SaveWhatsNewInstalledVersion(current);
            // The plate behind the Desk's text is on for a fresh install, whose desktop may be full of
            // files; an existing install has no key and reads off, so its Desk looks as it did.
            store.SeedDeskPlate(fresh);
            if (decision.Record)
                store.SaveWhatsNewLastSeen(current);
            if (decision.Show.Count == 0)
                return;

            var cards = decision.Show;
            var header = WhatsNew.RangeLabel(cards, lastSeen, current) ?? $"Version {current}";
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                OpenWhatsNew(cards, header);
            };
            timer.Start();
        }
        catch
        {
            // What's New is a courtesy: never let it stop the widget.
        }
    }

    private void OpenWhatsNew(IReadOnlyList<WhatsNewCard> cards, string header) =>
        WhatsNewWindow.Show(cards, header, new SettingsStore(new Sanduhr.Core.Paths()), ShowMe);

    /// <summary>A card's Show me: the widget, or Settings at the card's page and section (WF17).</summary>
    private void ShowMe(SettingsPage page, string? anchor)
    {
        if (page == SettingsPage.Widget)
            ShowWindow();
        else
            _ = ShowSettingsAsync(page, anchor);
    }

    /// <summary>
    /// The first successful fetch on a fresh install opens the tour, once a launch, so its first
    /// card shows the person's own meters. Posted, so the fetch finishes before the window opens.
    /// </summary>
    private void OnFetchSucceeded()
    {
        if (!WelcomeTour.ShowsAfterFetch(_tourState, fetched: true, _tourOfferedThisLaunch))
            return;
        _tourOfferedThisLaunch = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(ShowTour));
    }

    /// <summary>
    /// The welcome tour: from the first fetch, the tray's "Take the tour…" and Settings, About.
    /// Reopening shows the same steps with the current settings and records nothing.
    /// </summary>
    private void ShowTour()
    {
        if (_vm is null)
            return;
        try
        {
            var signedIn = _vm.ActiveAccount is not null || _vm.IsDemo;
            var steps = WelcomeTour.StepsFor(_vm.JustTheDesk, new HashSet<TourFeature>(), signedIn);
            WhatsNewWindow.ShowTour(new WelcomeTourViewModel(steps, _vm), ShowMe, EndTour);
        }
        catch (Exception e)
        {
            // The tour is a courtesy: never let it stop the widget.
            try
            {
                System.IO.File.AppendAllText(new Sanduhr.Core.Paths().LogFile,
                    $"{DateTime.UtcNow:o} tour failed to open ({e.GetType().Name}){Environment.NewLine}");
            }
            catch { }
        }
    }

    /// <summary>Finish or Skip: only a pending tour records anything, and then What's New's
    /// last-seen becomes this version so no update cards follow the tour.</summary>
    private void EndTour(bool finished)
    {
        try
        {
            var store = new SettingsStore(new Sanduhr.Core.Paths());
            if (WelcomeTour.End(WelcomeTour.Parse(store.LoadTourState()), finished) is not { } ended)
                return;
            store.SaveTourState(WelcomeTour.Format(ended));
            store.SaveWhatsNewLastSeen(AppVersion);
            _tourState = ended;
        }
        catch
        {
            // Settings are best-effort here; the tour simply offers again next launch.
        }
    }

    /// <summary>"What's New…" from the tray and Settings: the installed version's highlights, or every card.</summary>
    private void ShowWhatsNewFromMenu()
    {
        var installed = new SettingsStore(new Sanduhr.Core.Paths()).LoadWhatsNewInstalledVersion();
        var (cards, header) = WhatsNew.ForMenu(AppVersion, installed);
        OpenWhatsNew(cards, header);
    }

    private void SetupTray()
    {
        _trayIcon = TrayIconRenderer.Render(-1);
        _tray = new TaskbarIcon
        {
            Icon = _trayIcon,
            ToolTipText = "Sanduhr für Claude",
        };
        _tray.TrayLeftMouseUp += (_, _) => ToggleWindow();

        var menu = new ContextMenu();

        var show = new MenuItem { Header = "Show" };
        show.Click += (_, _) => ShowWindow();

        var settings = new MenuItem { Header = SettingsNames.OpenSettings };
        settings.Click += async (_, _) => await ShowSettingsAsync();

        var whatsNew = new MenuItem { Header = SettingsNames.WhatsNew };
        whatsNew.Click += (_, _) => ShowWhatsNewFromMenu();

        var tour = new MenuItem { Header = SettingsNames.TakeTheTour };
        tour.Click += (_, _) => ShowTour();

        // Only while demo mode is on: the way back to the real numbers, labelled as demo.
        _endDemoItem = new MenuItem
        {
            Header = SettingsNames.EndDemoMode,
            Visibility = _vm?.IsDemo == true ? Visibility.Visible : Visibility.Collapsed,
        };
        _endDemoItem.Click += async (_, _) =>
        {
            if (_vm is not null)
                await _vm.EndDemoAsync();
        };

        // Only while the Desk is on (or arranging): starts Arrange mode, then finishes it with Done (spec 7.3).
        _arrangeItem = new MenuItem { Header = DeskArrangeCopy.MenuItem, Visibility = Visibility.Collapsed };
        _arrangeItem.Click += (_, _) =>
        {
            if (_desk?.Arranging == true) _desk.EndArrange(DeskArrangeEnd.Done);
            else _desk?.BeginArrange();
        };
        menu.Opened += (_, _) => UpdateArrangeItem();

        // Only while a design was saved before the last change: switches to it (spec, the agent-designed Desk).
        _putBackItem = new MenuItem { Header = SettingsNames.PutMyDeskBack, Visibility = Visibility.Collapsed };
        _putBackItem.Click += (_, _) => _deskDesign?.PutBack();
        menu.Opened += (_, _) => _putBackItem.Visibility = _deskDesign?.CanPutBack == true ? Visibility.Visible : Visibility.Collapsed;

        var refresh = new MenuItem { Header = SettingsNames.RefreshUsageNow };
        refresh.Click += (_, _) => _vm?.RefreshCommand.Execute(null);

        // Accounts ▸ quick-switch submenu — repopulated on open so the live registry
        // and active marker stay current.
        var accounts = new MenuItem { Header = "Accounts" };
        accounts.Items.Add(new MenuItem { Header = "…", IsEnabled = false });
        accounts.SubmenuOpened += (_, _) =>
        {
            if (_vm is not null)
                AccountMenuBuilder.PopulateSwitchList(accounts.Items, _vm, includeAdd: false, includeManage: true);
        };

        var addAccount = new MenuItem { Header = SettingsNames.AddAccount };
        addAccount.Click += async (_, _) => await RunSignInAsync(embedded: true);

        // Focus + Game affordances (parity with widget.py's tray entries).
        var focus = new MenuItem { Header = SettingsNames.DeepWork };
        focus.Click += (_, _) => _vm?.ToggleFocusCommand.Execute(null);

        var game = new MenuItem { Header = SettingsNames.CooldownSnake };
        game.Click += (_, _) =>
        {
            ShowWindow();
            _vm?.StartGameCommand.Execute(null);
        };

        var quit = new MenuItem { Header = SettingsNames.QuitSanduhr };
        quit.Click += (_, _) => QuitApp();

        // Only while an update waits (3.8 spec 8b): the same Restart to update as the toast and the widget.
        _restartToUpdateItem = new MenuItem { Header = RestartToUpdate.ButtonText, Visibility = Visibility.Collapsed };
        _restartToUpdateItem.Click += (_, _) => _restartToUpdate?.Accept();
        // Only while the usage connection would not come back (issue #331).
        _restartItem = new MenuItem { Header = "Restart Sanduhr", Visibility = Visibility.Collapsed };
        _restartItem.Click += (_, _) => RestartApp();
        menu.Opened += (_, _) => UpdateRestartItems();

        menu.Items.Add(_restartToUpdateItem);
        menu.Items.Add(_restartItem);
        menu.Items.Add(_endDemoItem);
        menu.Items.Add(show);
        menu.Items.Add(settings);
        menu.Items.Add(whatsNew);
        menu.Items.Add(tour);
        menu.Items.Add(_arrangeItem);
        menu.Items.Add(_putBackItem);
        menu.Items.Add(refresh);
        menu.Items.Add(BuildTrayShowsMenu());
        menu.Items.Add(new Separator());
        menu.Items.Add(accounts);
        menu.Items.Add(addAccount);
        menu.Items.Add(new Separator());
        menu.Items.Add(focus);
        menu.Items.Add(game);
        menu.Items.Add(new Separator());
        menu.Items.Add(quit);
        _tray.ContextMenu = menu;
    }

    /// <summary>A Desk piece's right-click menu (spec 7.4): open its Settings page, arrange, or turn the Desk off.</summary>
    private void OnDeskMenu(Desk.DeskMenuAction action)
    {
        switch (action)
        {
            case Desk.DeskMenuAction.EditMessages: _ = ShowSettingsAsync(SettingsPage.DeskMessage, SettingsIndex.MenuAnchor(SettingsPage.DeskMessage)); break;
            case Desk.DeskMenuAction.DeskSettings: _ = ShowSettingsAsync(SettingsPage.Desk, SettingsIndex.MenuAnchor(SettingsPage.Desk)); break;
            case Desk.DeskMenuAction.LauncherSettings: _ = ShowSettingsAsync(SettingsPage.Launcher, SettingsIndex.MenuAnchor(SettingsPage.Launcher)); break;
            case Desk.DeskMenuAction.Arrange: _desk?.BeginArrange(); break;
            case Desk.DeskMenuAction.TurnOffDesk:
                try
                {
                    var store = new SettingsStore(new Sanduhr.Core.Paths());
                    store.SaveDesk(store.LoadDesk() with { Enabled = false });
                }
                catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
                {
                    return;
                }
                _desk?.Reload();
                _settingsWindow?.ViewModel.Desk.Reload();
                break;
        }
    }

    /// <summary>A Launcher button for an app that is no longer installed was chosen: Settings, Launcher opens at its buttons, that one selected.</summary>
    private void OnLauncherMissing(int index)
    {
        _ = ShowSettingsAsync(SettingsPage.Launcher, SettingsIndex.MenuAnchor(SettingsPage.Launcher));
        if (_settingsWindow?.ViewModel.Desk.Launcher is { } launcher)
        {
            launcher.Reload();
            launcher.Select(index);
        }
    }

    private MenuItem? _arrangeItem;
    private MenuItem? _putBackItem;

    /// <summary>The tray's Arrange entry follows the Desk: hidden while it is off, "Finish arranging" while arranging.</summary>
    private void UpdateArrangeItem()
    {
        if (_arrangeItem is null) return;
        bool arranging = _desk?.Arranging == true;
        _arrangeItem.Visibility = arranging || _desk?.Running == true ? Visibility.Visible : Visibility.Collapsed;
        _arrangeItem.Header = arranging ? DeskArrangeCopy.TrayFinish : DeskArrangeCopy.MenuItem;
    }

    /// <summary>A design was applied: an open Settings window re-reads the Desk page, the Launcher and the designs.</summary>
    private void FollowDesignChange()
    {
        if (_settingsWindow?.ViewModel.Desk is not { } page) return;
        page.RefreshFromStore();
    }

    /// <summary>Arrange mode began, dropped a piece or ended: the tray entry and an open Settings window follow
    /// (the Desk page's note, the live map, and after Done the saved rows).</summary>
    private void OnArrangeChanged()
    {
        UpdateArrangeItem();
        if (_desk is not { } desk || _settingsWindow?.ViewModel.Desk is not { } page) return;
        bool was = page.Arranging;
        page.Arranging = desk.Arranging;
        page.ArrangingLayout = desk.Session?.Working.Layout;
        if (was && !desk.Arranging) page.Reload();
    }

    /// <summary>The Desk (spec 5.1): off until switched on in Settings, Desk.</summary>
    private void StartDesk()
    {
        if (_vm is null) return;
        var vm = _vm;
        var paths = new Sanduhr.Core.Paths();
        var store = new SettingsStore(paths);
        _nowPlaying = new Desk.NowPlayingHub();
        Desk.NowPlayingHub.Current = _nowPlaying;
        _messages = new Desk.DeskMessageSource(store, paths);
        _handoff = new Desk.DeskMessageHandoffService(paths, _messages.FilePath, store);
        Desk.DeskMessageHandoffService.Current = _handoff;
        _handoff.Start(); // Claude's suggested Desk messages (spec 6.9)
        // Watchers (spec 6.9): agents' cards, only while "Let Claude show watchers" is on; in memory only.
        _watchers = new Desk.WatcherStore(paths, store.LoadWatchersAgents, () => vm.IsDemo);
        _watchers.WaitingStarted += () =>
        {
            // A watcher waiting on you glows once, but only where its card can be seen.
            if (_desk is { Running: true, WatchersPlaced: true }) _glow?.Preview(ClaudeCodeEvent.Waiting);
        };
        _watchers.Start();
        // CI watchers (spec ci-watchers-3.9): runner containers' jobs, only while "Show my CI runners' jobs" is on.
        _watchers.FailedStarted += () =>
        {
            if (!vm.IsDemo && _desk is { Running: true, WatchersPlaced: true }) _glow?.Preview(ClaudeCodeEvent.Waiting);
        };
        _dockerPipe = new Desk.DockerPipe();
        var runners = new Desk.RunnerWatchService(_dockerPipe, store.LoadWatchersCiRunners, _watchers);
        _runners = runners;
        runners.StatusChanged += () => Dispatcher.BeginInvoke(new Action(() => RunnerStatusShown?.Invoke()));
        runners.Refresh();
        // The Launcher (spec, Desk Launcher): one in-memory view of the Start menu for the Desk and Settings.
        _launcherApps = new LauncherAppCache();
        LauncherAppCache.Current = _launcherApps;
        // The agent-designed Desk (spec 3.7.0): desk-state.json for get_desk while the MCP integration is installed,
        // and the request/result handoff for propose_desk (phase 1 answers not_ready; the preview is the next phase).
        _deskDesign = new Desk.DeskDesignService(paths, store, _launcherApps);
        Desk.DeskDesignService.Current = _deskDesign;
        _deskDesign.Start();
        // A look per song (spec, a look per song): the gradient and letter style the Desk draws the playing song in, Claude's suggested looks
        // (the MCP tool and the now-playing mod's offer), and now-playing-look.json for the mod. The contrast rule is judged against the wallpaper.
        _looks = new Desk.NowPlayingLookService(paths, store, _nowPlaying, () => _deskDesign.LooksContext());
        Desk.NowPlayingLookService.Current = _looks;
        _deskDesign.Looks = _looks;
        _desk = new Desk.DeskController(store, () => new Desk.DesktopLayerHost(),
            () => vm.CurrentDeskUsage, _nowPlaying, _messages, _watchers, paths, _launcherApps, looks: _looks)
        {
            ThemeText = ThemeTextHex,
        };
        // The live review of Claude's Desk designs: the Desk and widget draw a design from memory, the floating bar asks Apply or Dismiss.
        _deskDesign.Host = new Desk.DeskDesignHost(_desk, () => _vm, FollowDesignChange);
        _deskDesign.DesignsChanged += () => _settingsWindow?.ViewModel.Desk.Designs.Reload();
        // Widgets made with Claude (spec 3.8 section 5): propose_widget's pack previews on the Desk and installs only on Apply.
        _widgetReview = new Desk.WidgetReviewService(paths, store);
        Desk.WidgetReviewService.Current = _widgetReview;
        _widgetReview.Host = new Desk.WidgetReviewHost(_desk, store, () =>
        {
            _settingsWindow?.ViewModel.WidgetsPage.ReloadLocal();
            // A palette Claude made joins the Ink presets (3.9.1).
            _settingsWindow?.ViewModel.Desk.Reload();
        });
        _widgetReview.Start();
        _desk.LauncherMissingClicked += OnLauncherMissing;
        _vm.DeskUsageChanged += _desk.UsageChanged;
        // The glow's Desk spot (spec 7.3): the glow service asks the Desk first and falls back to the window.
        if (_glow is not null) _glow.DeskGlow = c => _desk?.Glow(c) == true;
        _desk.ArrangeChanged += OnArrangeChanged;
        _desk.WidgetDimRequested += (dimmed, ease) => _window?.SetCareDim(dimmed, ease);
        _desk.MenuRequested += OnDeskMenu;
        _vm.ThemeChanged += p => _desk?.ThemeChanged(p.Definition);
        // The meters' usage signal follows Alerts' thresholds.
        _vm.AlertSettingsChanged += () => _desk?.Reload();
        // The widget's now-playing line (spec 6.3, W8) shares the Desk's media-session listener.
        _vm.NowPlaying = new NowPlayingLineViewModel(_nowPlaying, store);
        // The theme a following Desk draws from (theme match), before the first redraw.
        _desk.ThemeChanged(_vm.Palette.Definition);
        _desk.Reload();
        _looks.Start();
    }

    /// <summary>The live widget theme's text color as six hex digits (Theme ink, spec 6.7).</summary>
    private static string? ThemeTextHex() =>
        Current?.Resources["Sanduhr.Brush.Text"] is System.Windows.Media.SolidColorBrush b
            ? $"{b.Color.R:x2}{b.Color.G:x2}{b.Color.B:x2}" : null;

    /// <summary>Claude Code edge glow (Stage 4.5): watches the hook signal files; both switches start off.</summary>
    private void StartGlow()
    {
        if (_window is null) return;
        var paths = new Sanduhr.Core.Paths();
        _glow = new ClaudeCodeGlowService(new SettingsStore(paths), paths, _window);
        _glow.Start();
    }

    /// <summary>The global shortcut: registers what <c>hotkey_settings</c> asks for (off until the user turns it on).</summary>
    private void SetupHotKeys()
    {
        try
        {
            _hotKeys = new GlobalHotKeyService(action =>
            {
                if (action == HotKeyAction.Settings)
                    _ = ShowSettingsAsync();
            });
            var store = new SettingsStore(new Sanduhr.Core.Paths());
            _hotKeys.Apply(new Dictionary<HotKeyAction, HotKeySetting>
            {
                [HotKeyAction.Settings] = store.LoadHotKeySetting(HotKeyAction.Settings),
            });
        }
        catch (Exception e)
        {
            // A shortcut that cannot register must never stop the widget from starting.
            try
            {
                System.IO.File.AppendAllText(new Sanduhr.Core.Paths().LogFile,
                    $"{DateTime.UtcNow:o} global shortcut failed to start ({e.GetType().Name}){Environment.NewLine}");
            }
            catch { }
        }
    }

    /// <summary>
    /// A second launch asked: <c>--settings desk#pieces</c> opens Settings there; a plain launch shows the widget.
    /// </summary>
    private void OnSecondLaunch(string? message)
    {
        if (SettingsIndex.ParseLink(message) is { } link)
            OpenSettingsLink(link);
        else
            ShowWindow();
    }

    /// <summary>A link from outside the app: the widget, or Settings at that page and section.</summary>
    private void OpenSettingsLink(SettingsLink link)
    {
        if (link.Page == SettingsPage.Widget)
            ShowWindow();
        else
            _ = ShowSettingsAsync(link.Page, link.Anchor);
    }

    /// <summary>
    /// Open the Settings window at <paramref name="page"/> and <paramref name="anchor"/>, or, with no page, where it was
    /// left this session (General the first time); single-instance and non-modal so a live account switch from here
    /// reflects in the widget at once. Re-invoking focuses the existing window, refreshes its account list and, with a
    /// page, moves it there.
    /// </summary>
    private Task ShowSettingsAsync(SettingsPage? page = null, string? anchor = null)
    {
        if (_vm is null)
            return Task.CompletedTask;

        // Landing only ever sees a checked place (SettingsIndex.Check, the rule ParseLink uses), so nothing raw reaches it.
        if (page is { } asked)
            (page, anchor) = SettingsIndex.Check(asked, anchor);

        if (_settingsWindow is not null)
        {
            _settingsWindow.ViewModel.Accounts.Reload();
            if (page is { } p)
                _settingsWindow.SelectPage(p, anchor);
            _settingsWindow.Activate();
            return Task.CompletedTask;
        }

        var landing = SettingsIndex.Landing(page, anchor, _lastSettings);
        var svm = new SettingsViewModel(_vm, () => RunSignInAsync(embedded: true), RunUpdateSignInAsync, _hotKeys,
            ShowWhatsNewFromMenu, ShowTour);
        // The Claude Code page re-reads the glow switches and plays "Try it" through the running glow service.
        svm.ClaudeCodePage.GlowReload = () => _glow?.Reload();
        svm.ClaudeCodePage.DeskReload = () => { _desk?.Reload(); svm.Desk.Reload(); };
        svm.ClaudeCodePage.GlowPreview = e => _glow?.Preview(e);
        svm.Desk.Changed = () => _desk?.Reload();
        // Desk Look, Match the widget theme: the plate note follows the running Desk's readability guard.
        svm.ThemeMatch.PlateForced = () => _desk?.PlateForced == true;
        Action plateShown = () => svm.ThemeMatch.Reload();
        if (_desk is not null) _desk.PlateForcedChanged += plateShown;
        // Match now's guard asks the Desk what is behind the clock, and whether the guard already holds the plate on.
        svm.ThemeMatch.Service.WallpaperLuminance = () => _desk?.ClockLuminance();
        svm.ThemeMatch.Service.PlateForced = () => _desk?.PlateForced == true;
        svm.Desk.ArrangeRequested = () => _desk?.BeginArrange();
        svm.Desk.Arranging = _desk?.Arranging == true;
        svm.Desk.ArrangingLayout = _desk?.Session?.Working.Layout;
        svm.NowPlayingPage.Changed = () => { _vm?.NowPlaying?.Reload(); _desk?.Reload(); _looks?.SettingsChanged(); };
        // get_desk reports the weather nickname (#315), so a change rewrites desk-state.json too.
        svm.WeatherPage.Changed = () => { _desk?.Reload(); _deskDesign?.Refresh(); };
        // An install or uninstall (a companion sprite included), theme, font or palette pack.
        // 3.9.1: it also reloads the theme list, since an installed theme pack joins it and an uninstalled one leaves it.
        // A font pack changes what the Desk and a design may draw in: drop their font lists before the Desk redraws.
        svm.WidgetsPage.Changed = () =>
        {
            _themeFonts = Desk.DeskFonts.ForThisPc(new Sanduhr.Core.Paths());
            _vm?.ReloadUserThemes();
            _vm?.ApplyThemeResources();
            _desk?.ResetFonts();
            _deskDesign?.FontsChanged();
            _desk?.Reload();
            svm.Desk.Reload();
        };
        svm.MessageChanged = () => { _desk?.Reload(); _handoff?.PublishState(); };
        svm.Desk.WatchersChanged = () => _watchers?.SwitchChanged();
        svm.Desk.CiRunnersChanged = () => _runners?.Refresh();
        svm.Desk.CiRunnersStatus = _runners?.Status.Text ?? "";
        Action showRunnerStatus = () => svm.Desk.CiRunnersStatus = _runners?.Status.Text ?? "";
        RunnerStatusShown += showRunnerStatus;
        svm.QuitRequested = QuitApp;
        svm.ResetRequested = () =>
        {
            ShowReset();
            return Task.CompletedTask;
        };
        try
        {
            _settingsWindow = new SettingsWindow(svm);
            _settingsWindow.Closed += (_, _) =>
            {
                RunnerStatusShown -= showRunnerStatus;
                if (_desk is not null) _desk.PlateForcedChanged -= plateShown;
            };
        }
        catch (Exception e)
        {
            // The command that opens Settings is async and swallows exceptions, so a
            // XAML or view-model failure here would otherwise vanish: log it first.
            try
            {
                System.IO.File.AppendAllText(new Sanduhr.Core.Paths().LogFile,
                    $"{DateTime.UtcNow:o} settings window failed to open ({e.GetType().Name}: {e.Message}){Environment.NewLine}{e}{Environment.NewLine}");
            }
            catch { }
            RunnerStatusShown -= showRunnerStatus; // no window to show it in
            throw;
        }
        if (_window is { IsLoaded: true })
            _settingsWindow.Owner = _window;
        // On the widget's monitor, inside its work area, so the foot and Quit never sit off screen (WF41). Once, before the
        // first Show of a new window; a reopen of the window already up never comes through here.
        _settingsWindow.FitToWorkArea(_window);
        var opened = _settingsWindow;
        _settingsWindow.Closed += (_, _) =>
        {
            _lastSettings = opened.Place; // reopen here this session (spec, Reopen where it was left)
            _settingsWindow = null;
        };
        _settingsWindow.Show();
        _settingsWindow.SelectPage(landing.Page, landing.Anchor);
        _settingsWindow.Activate();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Run a sign-in (embedded WebView2 or manual paste) and, on success, kick the
    /// widget to rebuild its fetcher against the new active account and refetch.
    /// Both tray "Add account…" and the widget's empty-state prompt route here.
    /// </summary>
    private async Task RunSignInAsync(bool embedded)
    {
        var coordinator = new SignInCoordinator();
        SignInOutcome outcome = embedded
            ? await coordinator.SignInEmbeddedAsync(_window)
            : await coordinator.SignInManual(_window);

        if (outcome.Added && _vm is not null)
            await ReloadAfterSignInAsync();
    }

    /// <summary>
    /// Re-authenticate the active account IN PLACE (the widget's Expired/Blocked
    /// recovery card routes here), then rebuild + refetch. Distinct from
    /// <see cref="RunSignInAsync"/>, which adds a NEW account.
    /// </summary>
    private async Task RunReauthAsync()
    {
        var coordinator = new SignInCoordinator();
        var outcome = await coordinator.ReauthenticateActiveAsync(_window);
        if (outcome.Added && _vm is not null)
            await ReloadAfterSignInAsync();
    }

    /// <summary>In-place MANUAL re-auth of the active account (recovery card,
    /// manual-origin primary or paste-during-recovery secondary).</summary>
    private async Task RunManualReauthAsync()
    {
        var coordinator = new SignInCoordinator();
        var outcome = await coordinator.ReauthenticateManualActiveAsync(_window);
        if (outcome.Added && _vm is not null)
            await ReloadAfterSignInAsync();
    }

    /// <summary>Settings "Update sign-in…": in-place credential refresh for ANY
    /// account, routed by that account's origin. Only reload the live fetcher
    /// when the refreshed account is the active one.</summary>
    private async Task RunUpdateSignInAsync(string label)
    {
        if (_vm is null)
            return;
        Window? owner = _settingsWindow ?? (Window?)_window;
        var coordinator = new SignInCoordinator();
        var outcome = _vm.AccountStore.GetOrigin(label) == AccountOrigin.Manual
            ? await coordinator.ReauthenticateManualAsync(owner, label)
            : await coordinator.ReauthenticateEmbeddedAsync(owner, label);
        if (outcome.Added && _vm.ActiveAccount == label)
            await ReloadAfterSignInAsync();
    }

    /// <summary>
    /// The one place an added account is settled (spec 7.5): rebuild the widget on it, then end Just the
    /// Desk if it was on. Every sign-in, pasted key and re-sign-in routes here, so the decision runs once
    /// per add. Ending it restores the reminder only when Just the Desk had turned it off, redraws the
    /// Desk and shows the widget for the new account, with no relaunch.
    /// </summary>
    private async Task ReloadAfterSignInAsync()
    {
        if (_vm is not { } vm)
            return;
        // The account is saved by now: end Just the Desk before the fetch, so the widget comes up for it at
        // once and a fetch that throws can't leave Just the Desk on.
        EndJustTheDeskIfAccount(vm);
        await vm.ReloadAfterSignInAsync();
    }

    private void EndJustTheDeskIfAccount(WidgetViewModel vm)
    {
        try
        {
            if (vm.AccountStore.ListAccounts().Count == 0)
                return;
            var store = new SettingsStore(new Sanduhr.Core.Paths());
            var (state, reminderOn, ended) = JustTheDesk.EndBySignIn(store.LoadJustTheDesk(), store.LoadDeskSignInReminder());
            if (!ended)
                return;
            store.SaveJustTheDesk(state);
            store.SaveDeskSignInReminder(reminderOn);
            vm.JustTheDesk = false;
            if (_settingsWindow?.ViewModel.Alerts is { } alerts) alerts.DeskSignInReminder = reminderOn;
            vm.RefreshDesk();
            ShowWindow();
        }
        catch
        {
            // Settings are best-effort here; the account is added either way.
        }
    }

    /// <summary>
    /// The first-run card's Just the Desk: save the choice, turn the Desk on, redraw it (the Claude pieces
    /// stay placed and draw nothing) and put the widget away. The tray icon stays the way back.
    /// </summary>
    private void ChooseJustTheDesk()
    {
        if (_vm is not { } vm)
            return;
        try
        {
            var store = new SettingsStore(new Sanduhr.Core.Paths());
            var (state, reminderOn, deskEnabled) = JustTheDesk.Choose(store.LoadJustTheDesk(), store.LoadDeskSignInReminder());
            // The Desk first: a failed write after it leaves Just the Desk unchosen, never chosen with no Desk.
            if (deskEnabled)
                store.SaveDesk(store.LoadDesk() with { Enabled = true });
            store.SaveDeskSignInReminder(reminderOn);
            store.SaveJustTheDesk(state);
        }
        catch
        {
            return;   // nothing saved, nothing changed: the card stays
        }
        vm.JustTheDesk = true;
        _desk?.Reload();
        vm.RefreshDesk();
        _window?.Hide();

        // No fetch is coming to offer a pending tour, so it opens a moment after the choice instead.
        if (!WelcomeTour.ShowsForJustTheDesk(_tourState, _tourOfferedThisLaunch))
            return;
        _tourOfferedThisLaunch = true;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            ShowTour();
        };
        timer.Start();
    }

    private void OnTrayReadingChanged(TrayReading? reading)
    {
        if (_tray is null)
            return;
        var icon = TrayIconRenderer.Render(reading?.Percent ?? -1);
        _tray.Icon = icon;
        _trayIcon?.Dispose();
        _trayIcon = icon;
        _tray.ToolTipText = reading is null
            ? "Sanduhr für Claude — no data yet"
            : _vm?.IsDemo == true
                ? $"Sanduhr für Claude — {DemoMode.TrayLabel(reading.Label, reading.Percent)}"
                : $"Sanduhr für Claude — {reading.Label} {reading.Percent}%";
    }

    /// <summary>"Tray shows" submenu: one checkable item per mode, checks refreshed on open.</summary>
    private MenuItem BuildTrayShowsMenu()
    {
        var parent = new MenuItem { Header = SettingsNames.TrayShows };
        var items = new List<(TrayMode Mode, MenuItem Item)>();
        foreach (var mode in TrayReadout.All)
        {
            // Not IsCheckable: a checkable item toggles itself on click, which would
            // uncheck the current mode. The checks follow the setting instead.
            var item = new MenuItem { Header = TrayReadout.Title(mode) };
            var captured = mode;
            item.Click += (_, _) =>
            {
                if (_vm is not null)
                    _vm.TrayMode = captured;
                SyncChecks();
            };
            items.Add((mode, item));
            parent.Items.Add(item);
        }
        void SyncChecks()
        {
            foreach (var (mode, item) in items)
                item.IsChecked = _vm?.TrayMode == mode;
        }
        parent.SubmenuOpened += (_, _) => SyncChecks();
        return parent;
    }

    /// <summary>The 8-second rotate timer runs only while the mode is Rotate.</summary>
    private void SyncTrayRotation()
    {
        if (_vm is null)
            return;
        if (_vm.TrayMode == TrayMode.Rotate)
        {
            _trayRotateTimer ??= CreateRotateTimer();
            _trayRotateTimer.Start();
        }
        else
        {
            _trayRotateTimer?.Stop();
        }
    }

    private DispatcherTimer CreateRotateTimer()
    {
        var timer = new DispatcherTimer { Interval = TrayReadout.RotateInterval };
        timer.Tick += (_, _) => _vm?.AdvanceTrayRotation();
        return timer;
    }

    private void ToggleWindow()
    {
        if (_window is null)
            return;
        if (_window.IsVisible)
            _window.Hide();
        else
            ShowWindow();
    }

    private void ShowWindow()
    {
        if (_window is null)
            return;
        _window.Show();
        _window.Activate();
    }

    /// <summary>
    /// Settings, About, Reset Sanduhr…: opens the reset dialog over Settings. The dialog runs the checked
    /// steps and quits. <c>stop</c> marks the reset (nothing writes settings, snapshot, history or the vault
    /// again), stops follow, the seat meters, the timers, the fetch client and the glow watcher before any step
    /// removes anything; <c>undo</c> takes Sanduhr's entries out of Claude Code folders and reports kinds of
    /// failures only.
    /// </summary>
    private void ShowReset()
    {
        if (_vm is not { } vm)
            return;
        var paths = new Sanduhr.Core.Paths();
        var service = new ResetService(
            paths,
            vm.AccountStore,
            new PublishTokenStore(new WindowsCredentialManager(AccountStore.Service)),
            () => Task.Run<IReadOnlyList<string>>(() => ClaudeCodeViewModel.UndoEverything(vm)),
            () =>
            {
                ResetGuard.MarkDone();
                vm.StopBackgroundWork();
                _glow?.Stop();
                _desk?.StopForReset();
                _runners?.Dispose();
                _dockerPipe?.Dispose();
                _watchers?.Dispose();
                _vm?.NowPlaying?.Dispose();
            },
            vm.InactiveReader.DeleteAllProfilesAsync,
            () => new SettingsStore(paths).ClearClaudeCodeSwitchesForReset());
        ResetWindow.ShowFor(_settingsWindow, service, QuitApp);
    }

    /// <summary>The tray's restart items follow the update offer and the transport's state.</summary>
    private void UpdateRestartItems()
    {
        if (_restartToUpdateItem is { } update)
        {
            bool offered = _restartToUpdate?.Offered == true;
            update.Visibility = offered ? Visibility.Visible : Visibility.Collapsed;
            update.ToolTip = offered ? _restartToUpdate!.Flow.Notice : null;
        }
        if (_restartItem is { } restart)
            restart.Visibility = _vm?.TransportStopped == true ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The widget's window handle, the owner of the Store's consent dialog.</summary>
    private nint OwnerWindowHandle()
    {
        try { return _window is null ? 0 : new System.Windows.Interop.WindowInteropHelper(_window).EnsureHandle(); }
        catch { return 0; }
    }

    /// <summary>
    /// Restart to update, step one: the 3.7.2 teardown without quitting. No browser may start, the usage
    /// transport and any sign-in window close, and the browsers get the quit's bounded wait to exit (the
    /// guard ends the ones that do not), so none keeps the package in use while it installs.
    /// </summary>
    private async Task ReleaseBrowsersForUpdateAsync()
    {
        BrowserProcessGuard.Current.BeginHold();
        _vm?.ReleaseTransport();
        foreach (var signIn in Windows.OfType<SignInWindow>().ToList())
        {
            try { signIn.Close(); } catch { /* best-effort */ }
        }
        try { await BrowserProcessGuard.Current.WaitForExitAsync(BrowserExitWait); }
        catch { /* the job still ends them when the process exits */ }
    }

    /// <summary>The update did not happen: browsers may start again, and the widget fetches now.</summary>
    private void ResumeBrowsersAfterUpdate()
    {
        BrowserProcessGuard.Current.EndHold();
        if (!_quitting)
            _vm?.RefreshCommand.Execute(null);
    }

    /// <summary>
    /// Restart Sanduhr: start a new copy, then quit this one. The new copy finds this one not answering
    /// (<see cref="SingleInstance.StopAnswering"/>), waits for it to exit (up to the 15 s hand-off, longer
    /// than the quit's 5 s browser wait) and takes over. Used by "the usage connection stopped" and by a
    /// Store install that finished without closing Sanduhr.
    /// </summary>
    private void RestartApp()
    {
        if (_quitting)
            return;
        Instance?.StopAnswering();
        string? family = null;
        bool packaged = Sanduhr.Core.Startup.IsPackaged();
        if (packaged)
        {
            try { family = global::Windows.ApplicationModel.Package.Current.Id.FamilyName; }
            catch { /* no identity after all */ }
        }
        if (AppRelaunch.Target(packaged, family, Environment.ProcessPath) is { } target)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target.FileName, target.Arguments) { UseShellExecute = false })?.Dispose();
            }
            catch (Exception e)
            {
                try { System.IO.File.AppendAllText(new Sanduhr.Core.Paths().LogFile, $"{DateTime.UtcNow:o} restart: relaunch failed ({e.GetType().Name}){Environment.NewLine}"); }
                catch { }
            }
        }
        QuitApp();
    }

    private async void QuitApp()
    {
        if (_quitting)
            return;
        _quitting = true;
        Instance?.StopAnswering();
        // After a reset nothing may write settings.json, snapshot.json or history on the way out.
        // (QuitApp is also the normal quit; the guard is a no-op until a reset has run.)
        WhatsNewWindow.Quitting = true;
        BrowserProcessGuard.Current.BeginShutdown();
        _glow?.Dispose();
        _desk?.Dispose();
        _looks?.Dispose();
        _launcherApps?.Dispose();
        _messages?.Dispose();
        _handoff?.Dispose();
        _deskDesign?.Dispose();
        _widgetReview?.Dispose();
        _runners?.Dispose();
        _dockerPipe?.Dispose();
        _watchers?.Dispose();
        _vm?.NowPlaying?.Dispose();
        _nowPlaying?.Dispose();
        _hotKeys?.Dispose();
        _tray?.Dispose();
        _trayIcon?.Dispose();
        _vm?.Dispose();
        _window?.Hide();
        // The sign-in window owns a WebView2 too; closing it disposes its controller.
        foreach (var signIn in Windows.OfType<SignInWindow>().ToList())
        {
            try { signIn.Close(); } catch { /* best-effort */ }
        }
        // Every controller is closed now. Wait (bounded) for the browser processes to exit before this
        // process does: one left running keeps the package in use and the next update fails (0x80073D02).
        try { await BrowserProcessGuard.Current.WaitForExitAsync(BrowserExitWait); }
        catch { /* the job still ends them when the process exits */ }
        Shutdown();
    }

    /// <summary>Signing out of Windows closes every window: not a Skip of the tour.</summary>
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        WhatsNewWindow.Quitting = true;
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        WhatsNewWindow.Quitting = true;
        _glow?.Dispose();
        _desk?.Dispose();
        _looks?.Dispose();
        _launcherApps?.Dispose();
        _messages?.Dispose();
        _handoff?.Dispose();
        _deskDesign?.Dispose();
        _widgetReview?.Dispose();
        _runners?.Dispose();
        _dockerPipe?.Dispose();
        _watchers?.Dispose();
        _vm?.NowPlaying?.Dispose();
        _nowPlaying?.Dispose();
        _hotKeys?.Dispose();
        _tray?.Dispose();
        _trayIcon?.Dispose();
        _vm?.Dispose();
        // Exits that skipped QuitApp (signing out of Windows, a Store-driven shutdown): same bounded wait,
        // without awaiting. After a QuitApp this finds nothing left.
        BrowserProcessGuard.Current.BeginShutdown();
        try { BrowserProcessGuard.Current.WaitForExit(TimeSpan.FromSeconds(2)); }
        catch { /* the job still ends them when the process exits */ }
        base.OnExit(e);
    }
}
