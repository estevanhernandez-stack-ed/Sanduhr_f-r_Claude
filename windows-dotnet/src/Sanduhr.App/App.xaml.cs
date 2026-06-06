using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;
using Sanduhr.App.Services;
using Sanduhr.App.Theming;
using Sanduhr.App.ViewModels;

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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Inject the (single, default) obsidian palette. Item 8 swaps this.
        ThemePalette.Obsidian.Apply(Resources);

        _vm = new WidgetViewModel();
        _vm.TrayPercentChanged += OnTrayPercentChanged;

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

        var refresh = new MenuItem { Header = "Refresh" };
        refresh.Click += (_, _) => _vm?.RefreshCommand.Execute(null);

        var quit = new MenuItem { Header = "Quit Sanduhr" };
        quit.Click += (_, _) => QuitApp();

        menu.Items.Add(show);
        menu.Items.Add(refresh);
        menu.Items.Add(new Separator());
        menu.Items.Add(quit);
        _tray.ContextMenu = menu;
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
