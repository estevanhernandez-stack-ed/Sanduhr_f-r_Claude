using System.IO;
using System.Windows;
using System.Windows.Threading;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>
/// Claude's suggested Desk messages, the app side (spec 6.9): watches <c>%USERPROFILE%\.sanduhr</c> for
/// <c>desk-messages-request.json</c> and hands it to Core's <see cref="DeskMessageHandoff"/>, which checks
/// it again, holds it for Settings, Message's Add / Review / Dismiss and answers in the result file. The
/// MCP tool that writes the request (<c>propose_desk_messages</c>) shipped in 3.6.0, so this is on.
/// It also keeps <c>desk-messages-state.json</c> (the pin, the rotation, the mix switch and On special
/// days) current for <c>get_desk_messages</c>.
/// </summary>
internal sealed class DeskMessageHandoffService : IDisposable
{
    /// <summary>On since the MCP tool shipped (3.6.0). A kill switch for a build that must not watch the folder.</summary>
    public const bool Enabled = true;

    /// <summary>The app's one service (Settings, Message shows its suggestion); null in tests and the harness.</summary>
    public static DeskMessageHandoffService? Current { get; set; }

    private readonly SettingsStore? _store;
    private FileSystemWatcher? _watcher;

    public DeskMessageHandoffService(Paths paths, string messagesPath, SettingsStore? store = null)
    {
        _store = store;
        Handoff = new DeskMessageHandoff(new DeskMessagePaths(paths.ExternalDir, messagesPath),
            direct: () => false, canWrite: () => !ResetGuard.Done);
    }

    /// <summary>The suggestion state Settings, Message shows.</summary>
    public DeskMessageHandoff Handoff { get; }

    public void Start()
    {
        if (!Enabled || _watcher is not null) return;
        var dir = Path.GetDirectoryName(Handoff.Paths.Request)!;
        try
        {
            Directory.CreateDirectory(dir);
            _watcher = new FileSystemWatcher(dir, MessageProposal.RequestFile) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName };
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
        PublishState();
        Check();
    }

    /// <summary>Writes the pin and rotation <c>get_desk_messages</c> reports, when they changed (the file is
    /// rewritten only then). Called at start and after every change on Settings, Message.</summary>
    public void PublishState()
    {
        if (!Enabled || _store is null || ResetGuard.Done) return;
        var m = _store.LoadDeskMessage();
        Handoff.WriteState(m.PinnedLine, m.Hourly ? "hourly" : "daily", m.Mix, m.Special, m.Seconds);
    }

    private void Check() => Application.Current?.Dispatcher.BeginInvoke(Handoff.Check);

    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;
    }
}

/// <summary>
/// Watchers, the app side (spec 6.9): reads <c>watch-request-*.json</c> from <c>%USERPROFILE%\.sanduhr</c>,
/// deletes each as it reads it, keeps the board in memory and ticks it for fades and lost touch. The MCP
/// tools that write the requests (<c>watch_start</c>, <c>watch_update</c>, <c>watch_end</c>) shipped in
/// 3.6.0, gated by one switch: Settings, Desk, "Let agents show watchers" (off by default). The store
/// writes that switch to <c>watchers.json</c> for the server to read (the server writes nothing and
/// refuses with <c>watchers_off</c> unless it says on), applies a request only while the switch is on,
/// and clears its cards when it goes off. Claude Code's background work (the Stop hook) is not read at all.
/// </summary>
internal sealed class WatcherStore : IDisposable, IRunnerSink
{
    /// <summary>On since the MCP tools shipped (3.6.0). A kill switch for a build that must not watch the folder.</summary>
    public const bool Enabled = true;

    private readonly string _dir;
    private readonly Func<bool> _allowed;
    private readonly Func<bool> _demo;
    private readonly DispatcherTimer _tick;
    private FileSystemWatcher? _watcher;
    private bool _disposed;

    /// <param name="allowed">The user's switch: Settings, Desk, "Let agents show watchers".</param>
    /// <param name="demo">Demo mode is on: work watchers hide.</param>
    public WatcherStore(Paths paths, Func<bool> allowed, Func<bool>? demo = null)
    {
        _dir = paths.ExternalDir;
        _allowed = allowed;
        _demo = demo ?? (() => false);
        _tick = new DispatcherTimer(DispatcherPriority.Background);
        _tick.Tick += (_, _) => Tick();
    }

    public WatcherBoard Board { get; } = new();

    /// <summary>The board changed (the Desk redraws its watchers).</summary>
    public event Action? Changed;

    /// <summary>A watcher started waiting on the user (the glow fires once).</summary>
    public event Action? WaitingStarted;

    /// <summary>A runner's job just failed (the glow fires once, spec ci-watchers-3.9).</summary>
    public event Action? FailedStarted;

    // IRunnerSink: RunnerWatchService calls these from its own threads; each runs on the UI thread.
    void IRunnerSink.Apply(string key, string runner, RunnerLine line) => OnUi(() => ApplyRunner(key, runner, line));
    void IRunnerSink.Gone(string key) => OnUi(() => RunnerGone(key));
    void IRunnerSink.Clear() => OnUi(ClearRunners);

    /// <summary>Always queued, never inline, even from the UI thread: the sink calls then run in the order
    /// they were made, so a clear cannot overtake an apply posted before it and leave a ghost card.</summary>
    private static void OnUi(Action a)
    {
        var d = Application.Current?.Dispatcher;
        if (d is null) a();
        else d.BeginInvoke(a);
    }

    internal void ApplyRunner(string key, string runner, RunnerLine line)
    {
        if (_disposed) return;
        var change = Board.ApplyRunner(key, runner, line, DateTimeOffset.Now);
        if (change.Changed) Changed?.Invoke();
        if (change.Failed) FailedStarted?.Invoke();
        Schedule();
    }

    internal void RunnerGone(string key)
    {
        if (_disposed) return;
        if (Board.RunnerGone(key, DateTimeOffset.Now)) Changed?.Invoke();
        Schedule();
    }

    internal void ClearRunners()
    {
        if (_disposed) return;
        Board.Clear(WatcherSource.Runner);
        Changed?.Invoke();
    }

    public IReadOnlyList<Watcher> Shown => WatcherBoard.Shown(Board.Ordered(), _demo());

    public void Start()
    {
        if (!Enabled || _watcher is not null || _disposed) return;
        try
        {
            Directory.CreateDirectory(_dir);
            _watcher = new FileSystemWatcher(_dir, WatcherRequest.RequestPattern) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite };
            // A request lands by rename from a temp name, so Created alone would miss it.
            _watcher.Created += (_, _) => Application.Current?.Dispatcher.BeginInvoke(ReadAll);
            _watcher.Renamed += (_, _) => Application.Current?.Dispatcher.BeginInvoke(ReadAll);
            _watcher.Error += (_, _) => Application.Current?.Dispatcher.BeginInvoke(ReadAll);
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
        {
            _watcher?.Dispose();
            _watcher = null;
        }
        WriteSwitch(_allowed());
        ReadAll();
    }

    /// <summary>The switch in Settings changed: <c>watchers.json</c> follows, and turning it off drops the
    /// agents' cards.</summary>
    public void SwitchChanged()
    {
        bool on = _allowed();
        WriteSwitch(on);
        if (on) return;
        Board.Clear(WatcherSource.Agent);
        Changed?.Invoke();
    }

    /// <summary>
    /// <c>watchers.json</c>: <c>{"agents":true,"schema_version":1}</c>. Written when the switch is on or
    /// when it must say off over an older "on"; never created just to say off (absent reads as off).
    /// </summary>
    private void WriteSwitch(bool on)
    {
        if (_disposed || ResetGuard.Done) return;
        var path = Path.Combine(_dir, WatcherRequest.SwitchFile);
        var data = WatcherRequest.SwitchJson(on);
        try
        {
            if (File.Exists(path))
            {
                if (File.ReadAllBytes(path).AsSpan().SequenceEqual(data)) return;
            }
            else if (!on)
            {
                return;
            }
            Directory.CreateDirectory(_dir);
            var tmp = path + ".tmp";
            File.WriteAllBytes(tmp, data);
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The server then reads the old file or none: off unless the old one said on.
        }
    }

    /// <summary>Takes every waiting request, oldest name first, deleting each as it reads it. While the
    /// switch is off a request is deleted unread.</summary>
    internal void ReadAll()
    {
        if (_disposed) return;
        bool changed = false, waiting = false;
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(_dir, WatcherRequest.RequestPattern).OrderBy(f => f, StringComparer.Ordinal).ToList(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return; }
        bool allowed = _allowed();
        foreach (var f in files)
        {
            byte[]? data = null;
            try
            {
                if (allowed && new FileInfo(f).Length <= WatcherRequest.MaxBytes) data = File.ReadAllBytes(f);
                File.Delete(f);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            if (data is not null && WatcherRequest.Decode(data, DateTimeOffset.Now) is { } d)
            {
                var before = Board.Ordered();
                waiting |= Board.Apply(d.Command, DateTimeOffset.Now);
                changed |= !before.SequenceEqual(Board.Ordered());
            }
        }
        if (changed) Changed?.Invoke();
        if (waiting) WaitingStarted?.Invoke();
        Schedule();
    }

    private void Tick()
    {
        _tick.Stop();
        if (Board.Tick(DateTimeOffset.Now)) Changed?.Invoke();
        Schedule();
    }

    private void Schedule()
    {
        if (_disposed || Board.NextChange() is not { } next) return;
        var wait = next - DateTimeOffset.Now;
        _tick.Interval = wait < TimeSpan.FromMilliseconds(100) ? TimeSpan.FromMilliseconds(100) : wait;
        _tick.Start();
    }

    public void Dispose()
    {
        _disposed = true;
        _tick.Stop();
        _watcher?.Dispose();
        _watcher = null;
    }
}
