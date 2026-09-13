using System.IO;
using System.Windows.Threading;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Services;

/// <summary>
/// App-side owner of <c>propose_theme</c>: <c>sanduhr-mcp</c> drops
/// <c>theme-request.json</c> under <c>%APPDATA%\Sanduhr</c>; this service lints
/// it again (the widget is the authority), saves it under the themes folder,
/// applies it when asked, and answers through <c>theme-result.json</c>. A
/// <see cref="FileSystemWatcher"/> makes an interactive proposal land within a
/// second; the widget's 30-second tick is the fallback when the watcher misses
/// (it can, on some profiles). Everything runs on the dispatcher: a theme apply
/// is a resource swap, milliseconds, and the view model is not thread-safe.
/// </summary>
public sealed class ThemeHandoffService : IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan NoticeDuration = TimeSpan.FromSeconds(8);

    private readonly Paths _paths;
    private readonly WidgetViewModel _widget;
    private readonly Action<string>? _log;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _debounce;
    private FileSystemWatcher? _watcher;
    private bool _handling;

    public ThemeHandoffService(Paths paths, WidgetViewModel widget, Action<string>? log = null)
    {
        _paths = paths;
        _widget = widget;
        _log = log;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _debounce = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = Debounce };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Handle();
        };
        StartWatcher();
    }

    private void StartWatcher()
    {
        try
        {
            Directory.CreateDirectory(_paths.AppDataDir);
            _watcher = new FileSystemWatcher(_paths.AppDataDir, ThemeHandoff.RequestFileName)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                IncludeSubdirectories = false,
            };
            _watcher.Created += OnRequestFileEvent;
            _watcher.Changed += OnRequestFileEvent;
            _watcher.Renamed += OnRequestFileEvent;
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
        {
            _log?.Invoke($"theme handoff watcher unavailable ({e.GetType().Name}); tick fallback only");
            _watcher = null;
        }
    }

    /// <summary>Watcher callbacks arrive on a pool thread; coalesce the burst a
    /// single write produces and handle once, on the dispatcher.</summary>
    private void OnRequestFileEvent(object sender, FileSystemEventArgs e)
    {
        _dispatcher.BeginInvoke(() =>
        {
            _debounce.Stop();
            _debounce.Start();
        });
    }

    /// <summary>Called from the widget's 30-second tick on the UI thread.</summary>
    public void Tick() => Handle();

    /// <summary>Process one pending request, if any. Re-entrancy guarded: the
    /// watcher and the tick can both arrive while a request is in hand.</summary>
    public void Handle()
    {
        if (_handling)
            return;
        _handling = true;
        try
        {
            string reqPath = Path.Combine(_paths.AppDataDir, ThemeHandoff.RequestFileName);
            string resPath = Path.Combine(_paths.AppDataDir, ThemeHandoff.ResultFileName);
            if (ThemeHandoff.TryReadRequest(reqPath, DateTimeOffset.Now) is not { } request)
                return;

            var outcome = ThemeProposals.Process(request, _paths.ThemesDir, _widget.ActiveThemeKey, ThemeCatalog.BuiltIns.ContainsKey);
            if (outcome.SavedPath is not null)
                _widget.ReloadUserThemes();
            if (outcome.ApplyKey is { } key)
            {
                _widget.ApplyThemeByKey(key);
                string name = (string?)outcome.Payload["name"] ?? key;
                _widget.ShowTransientStatus($"Theme '{name}' applied from Claude Code", NoticeDuration);
                Sounds.PlayToggle();
            }
            ThemeHandoff.WriteResult(resPath, reqPath, request.Id, outcome.Payload);
        }
        catch (Exception e)
        {
            _log?.Invoke($"theme handoff failed ({e.GetType().Name}: {e.Message})");
        }
        finally
        {
            _handling = false;
        }
    }

    public void Dispose()
    {
        _debounce.Stop();
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }
    }
}
