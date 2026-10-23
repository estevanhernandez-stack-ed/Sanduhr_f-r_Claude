using System.Windows.Media;
using System.Windows.Threading;
using Sanduhr.App.Interop;
using Sanduhr.Core;

namespace Sanduhr.App.Services;

/// <summary>
/// What the Desk and Settings know about the Start menu, kept in memory and filled in the background: the app
/// list (names and IDs), which of a Launcher's apps are still installed, and the icons of the apps its buttons
/// use. Every shell call runs off the UI thread; <see cref="Changed"/> is raised on the UI thread when something
/// arrived that a redraw can use (the first list, a different list, an icon). Nothing is written to disk.
/// </summary>
internal sealed class LauncherAppCache : IDisposable
{
    /// <summary>How long a list is trusted before the next redraw asks again (installs and removals are rare).</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);

    /// <summary>The icon size asked of the shell: the Desk draws it at most about 100 DIPs, the picker at 40.</summary>
    public const int IconPixels = 256;

    /// <summary>The app's one cache, shared by the Desk and Settings so the Start menu is read once; null in tests and the harness.</summary>
    public static LauncherAppCache? Current { get; set; }

    private readonly Func<ILauncherAppCatalog> _catalogFactory;
    private readonly Dispatcher _ui;
    private readonly object _gate = new();
    private ILauncherAppCatalog? _catalog;
    private IReadOnlyList<LauncherAppInfo>? _apps;
    private Dictionary<string, string> _names = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _startMenuNames = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _present = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ImageSource> _icons = new(StringComparer.Ordinal);
    private readonly HashSet<string> _iconsAsked = new(StringComparer.Ordinal);
    private DateTimeOffset _listedAt = DateTimeOffset.MinValue;
    private bool _refreshing;
    private bool _disposed;

    public LauncherAppCache(Func<ILauncherAppCatalog>? catalogFactory = null, Dispatcher? ui = null)
    {
        _catalogFactory = catalogFactory ?? (() => new LauncherApps());
        _ui = ui ?? Dispatcher.CurrentDispatcher;
    }

    /// <summary>Something a redraw can use arrived (UI thread).</summary>
    public event Action? Changed;

    /// <summary>True once a list has been read.</summary>
    public bool Known
    {
        get { lock (_gate) return _apps is not null; }
    }

    /// <summary>The apps the Start menu lists, by name; empty until the first list arrives.</summary>
    public IReadOnlyList<LauncherAppInfo> Apps
    {
        get { lock (_gate) return _apps ?? []; }
    }

    /// <summary>The Start menu's name for an app; null when unknown (not listed, or no list yet).</summary>
    public string? NameOf(string aumid)
    {
        lock (_gate) return _names.TryGetValue(aumid, out var n) ? n : null;
    }

    /// <summary>
    /// The Start menu's name for any app id, such as a media session's (Settings, Now playing: #317). From the list
    /// when it has the id, otherwise asked of the shell once (blocking, on the shell thread) and remembered; null when
    /// the Start menu does not list it, so the caller falls back to its own rule. Never throws.
    /// </summary>
    public string? StartMenuName(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId)) return null;
        lock (_gate)
        {
            if (_names.TryGetValue(appId, out var listed)) return listed;
            if (_apps?.FirstOrDefault(a => string.Equals(a.Aumid, appId, StringComparison.OrdinalIgnoreCase)) is { } app) return app.Name;
            if (_startMenuNames.TryGetValue(appId, out var asked)) return asked;
        }
        string? name = null;
        if (LauncherTarget.Validate(LauncherKind.App, appId).Ok)
        {
            try { name = EnsureCatalog()?.Name(appId); }
            catch (Exception) { name = null; }
        }
        lock (_gate) _startMenuNames[appId] = name;
        return name;
    }

    /// <summary>True only when a list has been read and it does not have this app: an app that may simply not be
    /// loaded yet is never reported gone.</summary>
    public bool IsMissing(string aumid)
    {
        lock (_gate) return _apps is not null && !_present.Contains(aumid);
    }

    /// <summary>Asks the shell now whether the Start menu has this app (blocking: call off the UI thread). The click
    /// path uses it so a stale list never turns a good click into a Settings page or a dead one into a launch.</summary>
    public bool ExistsNow(string aumid)
    {
        try { return EnsureCatalog()?.Exists(aumid) ?? false; }
        catch (Exception) { return false; }
    }

    /// <summary>The app's icon at about <paramref name="px"/> pixels if it has arrived; otherwise null, and the shell is
    /// asked for it in the background (<see cref="Changed"/> follows when it lands).</summary>
    public ImageSource? IconOf(string aumid, int px = IconPixels)
    {
        var key = aumid + "|" + px;
        lock (_gate)
        {
            if (_icons.TryGetValue(key, out var icon)) return icon;
            if (!_iconsAsked.Add(key)) return null;
        }
        Task.Run(() =>
        {
            ImageSource? icon = null;
            try { icon = EnsureCatalog()?.Icon(aumid, px); }
            catch (Exception) { /* no icon: the piece draws its placeholder */ }
            if (icon is null) return;
            lock (_gate) _icons[key] = icon;
            Raise();
        });
        return null;
    }

    /// <summary>
    /// Reads the list in the background when there is none or it is older than <see cref="MaxAge"/> (or
    /// <paramref name="force"/>). <paramref name="wanted"/> are the IDs the caller cares about: any the list lacks is
    /// asked of the shell one by one before it is called missing.
    /// </summary>
    public void Refresh(IReadOnlyCollection<string> wanted, bool force = false)
    {
        lock (_gate)
        {
            if (_disposed || _refreshing) return;
            if (!force && _apps is not null && DateTimeOffset.UtcNow - _listedAt < MaxAge) return;
            _refreshing = true;
        }
        Task.Run(() =>
        {
            try
            {
                var catalog = EnsureCatalog();
                if (catalog is null) return;
                var apps = catalog.Enumerate();
                var present = new HashSet<string>(apps.Select(a => a.Aumid), StringComparer.Ordinal);
                foreach (var id in wanted)
                    if (!present.Contains(id) && !LauncherTarget.IsPathForm(id) && catalog.Exists(id)) present.Add(id);   // a path form is present only by listing
                bool changed;
                lock (_gate)
                {
                    changed = _apps is null || !present.SetEquals(_present) || !apps.Select(a => a.Name).SequenceEqual(_apps.Select(a => a.Name));
                    _apps = apps;
                    _names = apps.ToDictionary(a => a.Aumid, a => a.Name, StringComparer.Ordinal);
                    _present = present;
                    _listedAt = DateTimeOffset.UtcNow;
                }
                if (changed) Raise();
            }
            catch (Exception)
            {
                // The shell would not answer: the next redraw tries again.
            }
            finally
            {
                lock (_gate) _refreshing = false;
            }
        });
    }

    private ILauncherAppCatalog? EnsureCatalog()
    {
        lock (_gate)
        {
            if (_disposed) return null;
            return _catalog ??= _catalogFactory();
        }
    }

    private void Raise()
    {
        if (_disposed) return;
        try { _ui.BeginInvoke(() => { if (!_disposed) Changed?.Invoke(); }); }
        catch (Exception) { /* the dispatcher is shutting down */ }
    }

    public void Dispose()
    {
        ILauncherAppCatalog? catalog;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            catalog = _catalog;
            _catalog = null;
        }
        (catalog as IDisposable)?.Dispose();
    }
}
