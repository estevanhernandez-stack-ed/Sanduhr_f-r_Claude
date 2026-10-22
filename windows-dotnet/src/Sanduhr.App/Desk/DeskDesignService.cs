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
/// The agent-designed Desk, the app side (spec, the agent-designed Desk, MCP tools). Two jobs:
/// <list type="bullet">
/// <item><description>Keeps <c>%USERPROFILE%\.sanduhr\desk-state.json</c> current for <c>get_desk</c>: the Desk as a full design (built
/// from the settings and the active theme), the piece and anchor words, the installed font families, the wallpaper path and its
/// local summary, the saved designs (none until phase 2) and the Start-menu apps. Rewritten, debounced, when settings, the
/// wallpaper, the monitors, the installed fonts or the Start menu change; only while the MCP integration is installed
/// (<c>mcp_enabled</c>), and removed when it is not.</description></item>
/// <item><description>Watches <c>desk-design-request.json</c> and hands it to Core's <see cref="DeskDesignHandoff"/>, which checks the
/// design again and answers in <c>desk-design-result.json</c>. In this version the answer to a good design is
/// <c>rejected</c> with the reason <c>not_ready</c>: the live preview, the review bar and the saved designs are the next phase.</description></item>
/// </list>
/// Nothing here is sent anywhere: the wallpaper summary is computed in this process from the image file the Automatic ink
/// already reads, and leaves it only inside desk-state.json for the local MCP server.
/// </summary>
internal sealed class DeskDesignService : IDisposable
{
    /// <summary>On from the first build that ships get_desk and propose_desk. A kill switch for a build that must not write the state.</summary>
    public const bool Enabled = true;

    /// <summary>The app's one service; null in tests and the harness.</summary>
    public static DeskDesignService? Current { get; set; }

    /// <summary>How long changes settle before the state is rewritten (a Settings slider writes many times).</summary>
    public static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(1500);

    private sealed record Inputs(DeskDesign Current, IReadOnlyList<string> Fonts, WallpaperOverview? Overview, string? WallpaperPath, IReadOnlyList<StartMenuApp>? Apps);

    private readonly Paths _paths;
    private readonly SettingsStore _store;
    private readonly LauncherAppCache? _apps;
    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _settle;
    private readonly WallpaperInk _wallpaper = new();
    private FileSystemWatcher? _watcher;
    private IReadOnlyList<string>? _fonts;
    private bool _started;
    private bool _disposed;

    public DeskDesignService(Paths paths, SettingsStore store, LauncherAppCache? apps = null, Dispatcher? ui = null)
    {
        _paths = paths;
        _store = store;
        _apps = apps;
        _ui = ui ?? Dispatcher.CurrentDispatcher;
        Handoff = new DeskDesignHandoff(new DeskDesignPaths(paths.ExternalDir), Context);
        _settle = new DispatcherTimer(DispatcherPriority.Background, _ui) { Interval = Settle };
        _settle.Tick += (_, _) =>
        {
            _settle.Stop();
            Publish();
        };
    }

    /// <summary>The request and result files, and the state writer.</summary>
    public DeskDesignHandoff Handoff { get; }

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

    private void OnFontsChanged(object? sender, EventArgs e)
    {
        _ui.BeginInvoke(() =>
        {
            _fonts = null;
            Refresh();
        });
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
            return;
        }
        var i = Gather();
        var state = DeskState.Build(i.Current, i.Fonts, i.Overview, i.WallpaperPath, i.Apps, [], direct: false, AppVersion);
        Handoff.WriteState(state);
    }

    private Inputs Gather()
    {
        var current = DeskState.Current(_store.LoadDesk(), _store.LoadDeskInk(), _store.LoadLauncher(), ActiveTheme());
        _fonts ??= InstalledFamilies();
        _wallpaper.Refresh();
        var (overview, path) = _wallpaper.Overview();
        IReadOnlyList<StartMenuApp>? apps = null;
        if (_apps is not null)
        {
            _apps.Refresh([]);   // reads the Start menu in the background when the list is old; Changed follows
            if (_apps.Known) apps = _apps.Apps.Select(a => new StartMenuApp(a.Aumid, a.Name)).ToList();
        }
        return new Inputs(current, _fonts, overview, path, apps);
    }

    /// <summary>What the widget's own check of a request knows about this PC.</summary>
    private DeskDesignContext Context()
    {
        var i = Gather();
        return new DeskDesignContext(
            i.Fonts.Count == 0 ? null : i.Fonts, i.Overview, i.Apps?.Select(a => a.Id).ToList(), i.Current);
    }

    /// <summary>The theme on the widget now, as its JSON document (the saved key resolved against the built-ins and the user's themes).</summary>
    private JsonObject? ActiveTheme()
    {
        try
        {
            string key = _store.LoadTheme();
            var map = new Dictionary<string, ThemeDefinition>(ThemeCatalog.BuiltIns);
            foreach (var (k, def) in ThemeCatalog.LoadUserThemes(_paths.ThemesDir)) map[k] = def;
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
    }
}
