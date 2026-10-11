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
    private MenuItem? _endDemoItem;

    /// <summary>Where this install stands with the welcome tour, decided once at launch.</summary>
    private TourState? _tourState;
    /// <summary>The tour opens after a first successful fetch at most once a launch.</summary>
    private bool _tourOfferedThisLaunch;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Store installs only: bring data an older, virtualized Store build left in the
        // package's LocalCache to the real AppData paths. Must run before anything below
        // constructs Paths or reads settings. One-shot, never throws.
        PackagedDataMigrationRunner.RunIfPackaged();

        _vm = new WidgetViewModel();
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
        _vm.SettingsPageRequested += page => ShowSettingsAsync(page);
        // Keep an open Settings window's account list in sync with quick-switches
        // made elsewhere (tray / widget chip / right-click).
        _vm.AccountsChanged += () => _settingsWindow?.ViewModel.Accounts.Reload();

        _window = new MainWindow { DataContext = _vm };
        _window.Show();

        _vm.AttachAlertService(new AlertService(new Sanduhr.Core.Paths(), ShowWindow));

        SetupTray();
        SyncTrayRotation();
        SetupHotKeys();
        StartGlow();

        // Usage vault (WS-C): consent resolves BEFORE the service attaches —
        // attach order is the consent gate; Start()'s first fetch cycle then
        // kicks the initial backfill through the attached service.
        var vaultService = new VaultService(new SettingsStore(new Sanduhr.Core.Paths()), _vm.CcReader);
        if (vaultService.NeedsConsentPrompt)
            vaultService.SaveConsent(VaultConsentDialog.ShowConsent(_window, vaultService.DetectedRootNames()));
        _vm.AttachVaultService(vaultService);

        // Publish usage: everything defaults off (toggle + every home, no endpoint),
        // so attaching is inert until the user opts in under Settings > Publish usage.
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

        // A Store update cannot replace files this process holds: the install
        // fails and leaves the old build running (3.4.1, 2026-09-14). Inert on
        // the Velopack and dev builds, which have no Store identity.
        _vm.AttachStoreUpdateService(new StoreUpdateService(_vm, QuitApp, log: msg =>
        {
            try { System.IO.File.AppendAllText(themePaths.LogFile, $"{DateTime.UtcNow:o} {msg}{Environment.NewLine}"); }
            catch { }
        }));

        // Statusline bridge (WS-E): the script has no update channel of its own —
        // the widget is its updater. Refresh the installed copy on every start
        // while the integration is enabled (idempotent single-file write).
        if (_vm.LoadStatuslineEnabled())
        {
            new StatuslineInstaller(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                _vm.StatuslineBinDir).InstallScript();
        }

        // MCP integration (WS-E registration slice): same widget-as-updater rule.
        // A newer app build carries a newer server; refresh the version dir +
        // launcher so new Claude Code sessions get it. Version-dir indirection
        // means running sessions keep their pinned copy — no lock fight.
        if (_vm.LoadMcpEnabled())
        {
            string mcpSource = System.IO.Path.Combine(AppContext.BaseDirectory, "mcp");
            if (System.IO.Directory.Exists(mcpSource))
            {
                new McpIntegrationInstaller(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    _vm.SanduhrAppDataDir).RefreshInstall(mcpSource);
            }
        }

        // Load the active account's stored credential + fetch real usage now.
        _vm.Start();

        // Demo mode (screenshots): `--demo` or SANDUHR_DEMO=1. Session-only sample numbers,
        // labelled as demo on the widget and the tray; nothing is fetched or written.
        if (DemoMode.IsRequested(Environment.GetCommandLineArgs().Skip(1),
                Environment.GetEnvironmentVariable(DemoMode.EnvironmentVariable)))
            _vm.StartDemo();

        // Best-effort GitHub/Velopack update probe — fire-and-forget so a slow or offline
        // network never delays the first paint. No-ops on the Store/MSIX install and in dev.
        _ = new UpdateChecker().CheckForUpdatesAsync();

        ScheduleWhatsNewAtLaunch();
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
            var onboarding = _vm.ActiveAccount is null;

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
        WhatsNewWindow.Show(cards, header, new SettingsStore(new Sanduhr.Core.Paths()), page =>
        {
            if (page == SettingsPage.Widget)
                ShowWindow();
            else
                _ = ShowSettingsAsync(page);
        });

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
            var steps = WelcomeTour.Steps(new HashSet<TourFeature>(), signedIn: signedIn);
            WhatsNewWindow.ShowTour(new WelcomeTourViewModel(steps, _vm), page =>
            {
                if (page == SettingsPage.Widget)
                    ShowWindow();
                else
                    _ = ShowSettingsAsync(page);
            }, EndTour);
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

        var refresh = new MenuItem { Header = "Refresh" };
        refresh.Click += (_, _) => _vm?.RefreshCommand.Execute(null);

        // Accounts ▸ quick-switch submenu — repopulated on open so the live registry
        // and active marker stay current.
        var accounts = new MenuItem { Header = "Accounts" };
        accounts.Items.Add(new MenuItem { Header = "…", IsEnabled = false });
        accounts.SubmenuOpened += (_, _) =>
        {
            if (_vm is not null)
                AccountMenuBuilder.PopulateSwitchList(accounts.Items, _vm, includeAdd: false, includeManage: false);
        };

        var addAccount = new MenuItem { Header = SettingsNames.AddAccount };
        addAccount.Click += async (_, _) => await RunSignInAsync(embedded: true);

        // Focus + Game affordances (parity with widget.py's tray entries).
        var focus = new MenuItem { Header = "Deep Work (Focus)" };
        focus.Click += (_, _) => _vm?.ToggleFocusCommand.Execute(null);

        var game = new MenuItem { Header = "Cooldown Snake" };
        game.Click += (_, _) =>
        {
            ShowWindow();
            _vm?.StartGameCommand.Execute(null);
        };

        var quit = new MenuItem { Header = "Quit Sanduhr" };
        quit.Click += (_, _) => QuitApp();

        menu.Items.Add(_endDemoItem);
        menu.Items.Add(show);
        menu.Items.Add(settings);
        menu.Items.Add(whatsNew);
        menu.Items.Add(tour);
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
    /// Open the Settings window (on General, or on <paramref name="page"/>) — single-instance and
    /// non-modal so a live account switch from here reflects in the widget at once.
    /// Re-invoking focuses the existing window and refreshes its account list.
    /// </summary>
    private Task ShowSettingsAsync(SettingsPage? page = null)
    {
        if (_vm is null)
            return Task.CompletedTask;

        if (_settingsWindow is not null)
        {
            _settingsWindow.ViewModel.Accounts.Reload();
            if (page is { } p)
                _settingsWindow.SelectPage(p);
            _settingsWindow.Activate();
            return Task.CompletedTask;
        }

        var svm = new SettingsViewModel(_vm, () => RunSignInAsync(embedded: true), RunUpdateSignInAsync, _hotKeys,
            ShowWhatsNewFromMenu, ShowTour);
        // The Claude Code page re-reads the glow switches and plays "Try it" through the running glow service.
        svm.ClaudeCodePage.GlowReload = () => _glow?.Reload();
        svm.ClaudeCodePage.GlowPreview = e => _glow?.Preview(e);
        svm.ResetRequested = () =>
        {
            ShowReset();
            return Task.CompletedTask;
        };
        try
        {
            _settingsWindow = new SettingsWindow(svm);
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
            throw;
        }
        if (_window is { IsLoaded: true })
            _settingsWindow.Owner = _window;
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        if (page is { } first)
            _settingsWindow.SelectPage(first);
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
            await _vm.ReloadAfterSignInAsync();
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
            await _vm.ReloadAfterSignInAsync();
    }

    /// <summary>In-place MANUAL re-auth of the active account (recovery card,
    /// manual-origin primary or paste-during-recovery secondary).</summary>
    private async Task RunManualReauthAsync()
    {
        var coordinator = new SignInCoordinator();
        var outcome = await coordinator.ReauthenticateManualActiveAsync(_window);
        if (outcome.Added && _vm is not null)
            await _vm.ReloadAfterSignInAsync();
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
            await _vm.ReloadAfterSignInAsync();
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
            },
            vm.InactiveReader.DeleteAllProfilesAsync);
        ResetWindow.ShowFor(_settingsWindow, service, QuitApp);
    }

    private void QuitApp()
    {
        // After a reset nothing may write settings.json, snapshot.json or history on the way out.
        // (QuitApp is also the normal quit; the guard is a no-op until a reset has run.)
        WhatsNewWindow.Quitting = true;
        _glow?.Dispose();
        _hotKeys?.Dispose();
        _tray?.Dispose();
        _trayIcon?.Dispose();
        _vm?.Dispose();
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
        _hotKeys?.Dispose();
        _tray?.Dispose();
        _trayIcon?.Dispose();
        _vm?.Dispose();
        base.OnExit(e);
    }
}
