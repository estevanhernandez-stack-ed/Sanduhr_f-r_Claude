using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;
using Sanduhr.App.Services;
using Sanduhr.App.Theming;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;

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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _vm = new WidgetViewModel();
        // Apply the saved theme's palette to the Application resources before the
        // window is shown so the first paint is correct (no obsidian→saved flash).
        _vm.ApplyThemeResources();
        // A live theme switch (strip or Settings) raises ThemeChanged — the window
        // swaps its backdrop (Mica for glass themes, solid for a Matrix opt-out).
        _vm.ThemeChanged += p => _window?.ApplyBackdropForTheme(p);
        _vm.TrayPercentChanged += OnTrayPercentChanged;
        _vm.SignInRequested += () => RunSignInAsync(embedded: true);
        _vm.PasteKeyRequested += () => RunSignInAsync(embedded: false);
        _vm.SettingsRequested += ShowSettingsAsync;
        // Keep an open Settings window's account list in sync with quick-switches
        // made elsewhere (tray / widget chip / right-click).
        _vm.AccountsChanged += () => _settingsWindow?.ViewModel.Accounts.Reload();

        _window = new MainWindow { DataContext = _vm };
        _window.Show();

        SetupTray();

        // Load the active account's stored credential + fetch real usage now.
        _vm.Start();
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

        var settings = new MenuItem { Header = "Settings…" };
        settings.Click += async (_, _) => await ShowSettingsAsync();

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

        var addAccount = new MenuItem { Header = "Add account" };
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

        menu.Items.Add(show);
        menu.Items.Add(settings);
        menu.Items.Add(refresh);
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

    /// <summary>
    /// Open the tabbed Settings window (Accounts tab default) — single-instance and
    /// non-modal so a live account switch from here reflects in the widget at once.
    /// Re-invoking focuses the existing window and refreshes its account list.
    /// </summary>
    private Task ShowSettingsAsync()
    {
        if (_vm is null)
            return Task.CompletedTask;

        if (_settingsWindow is not null)
        {
            _settingsWindow.ViewModel.Accounts.Reload();
            _settingsWindow.Activate();
            return Task.CompletedTask;
        }

        var svm = new SettingsViewModel(_vm, () => RunSignInAsync(embedded: true));
        _settingsWindow = new SettingsWindow(svm);
        if (_window is { IsLoaded: true })
            _settingsWindow.Owner = _window;
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Run a sign-in (embedded WebView2 or manual paste) and, on success, kick the
    /// widget to rebuild its fetcher against the new active account and refetch.
    /// Both tray "Add account" and the widget's empty-state prompt route here.
    /// </summary>
    private async Task RunSignInAsync(bool embedded)
    {
        var coordinator = new SignInCoordinator();
        SignInOutcome outcome = embedded
            ? await coordinator.SignInEmbeddedAsync(_window)
            : coordinator.SignInManual(_window);

        if (outcome.Added && _vm is not null)
            await _vm.ReloadAfterSignInAsync();
    }

    private void OnTrayPercentChanged(int percent)
    {
        if (_tray is null)
            return;
        var icon = TrayIconRenderer.Render(percent);
        _tray.Icon = icon;
        _trayIcon?.Dispose();
        _trayIcon = icon;
        _tray.ToolTipText = percent < 0
            ? "Sanduhr für Claude — no data yet"
            : $"Sanduhr für Claude — ⏳ {percent}%";
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

    private void QuitApp()
    {
        _tray?.Dispose();
        _trayIcon?.Dispose();
        _vm?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _trayIcon?.Dispose();
        _vm?.Dispose();
        base.OnExit(e);
    }
}
