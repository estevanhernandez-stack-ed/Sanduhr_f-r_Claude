using System.IO;
using System.Windows;
using System.Windows.Threading;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>
/// Claude's suggested Desk messages, the app side (spec 6.9): watches <c>%USERPROFILE%\.sanduhr</c> for
/// <c>desk-messages-request.json</c> and hands it to Core's <see cref="DeskMessageHandoff"/>, which checks
/// it again, holds it for Settings, Message's Add / Review / Dismiss and answers in the result file.
/// <b>Held for Este:</b> the MCP tool that writes the request (<c>propose_desk_messages</c>) is a
/// trust-boundary change and is not built, so <see cref="Enabled"/> is false and <see cref="Start"/>
/// watches nothing. Shipping the tool turns this on; nothing else changes.
/// </summary>
internal sealed class DeskMessageHandoffService : IDisposable
{
    /// <summary>Off until the MCP tool ships (spec 6.9).</summary>
    public const bool Enabled = false;

    /// <summary>The app's one service (Settings, Message shows its suggestion); null in tests and the harness.</summary>
    public static DeskMessageHandoffService? Current { get; set; }

    private FileSystemWatcher? _watcher;

    public DeskMessageHandoffService(Paths paths, string messagesPath)
    {
        Handoff = new DeskMessageHandoff(new DeskMessagePaths(paths.ExternalDir, messagesPath),
            direct: () => false, canWrite: () => !ResetGuard.Done);
    }

    /// <summary>The suggestion state Settings, Message shows.</summary>
    public DeskMessageHandoff Handoff { get; }

    public void Start()
    {
        if (!Enabled || _watcher is not null) return;
#pragma warning disable CS0162 // reachable once Enabled turns on with the MCP tool
        var dir = Path.GetDirectoryName(Handoff.Paths.Request)!;
        _watcher = new FileSystemWatcher(dir, MessageProposal.RequestFile) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName };
        _watcher.Created += (_, _) => Check();
        _watcher.Changed += (_, _) => Check();
        _watcher.Renamed += (_, _) => Check();
        _watcher.EnableRaisingEvents = true;
        Check();
#pragma warning restore CS0162
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
/// deletes each as it reads it, keeps the board in memory and ticks it for fades and lost touch.
/// <b>Held for Este:</b> the MCP tools that write requests (<c>watch_start</c>, <c>watch_update</c>,
/// <c>watch_end</c>) are not built, so <see cref="Enabled"/> is false: nothing is watched, no switch is
/// shown, no <c>watchers.json</c> is written, and the Desk's watchers piece draws only in the render
/// harness. Claude Code's background work (the Stop hook) is not read at all.
/// </summary>
internal sealed class WatcherStore : IDisposable
{
    /// <summary>Off until the MCP tools ship (spec 6.9).</summary>
    public const bool Enabled = false;

    private readonly string _dir;
    private readonly DispatcherTimer _tick;
    private FileSystemWatcher? _watcher;

    public WatcherStore(Paths paths)
    {
        _dir = paths.ExternalDir;
        _tick = new DispatcherTimer(DispatcherPriority.Background);
        _tick.Tick += (_, _) => Tick();
    }

    public WatcherBoard Board { get; } = new();

    /// <summary>The board changed (the Desk redraws its watchers).</summary>
    public event Action? Changed;

    public IReadOnlyList<Watcher> Shown => WatcherBoard.Shown(Board.Ordered(), demo: false);

    public void Start()
    {
        if (!Enabled || _watcher is not null) return;
#pragma warning disable CS0162 // reachable once Enabled turns on with the MCP tools
        _watcher = new FileSystemWatcher(_dir, "watch-request-*.json") { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite };
        _watcher.Created += (_, _) => Application.Current?.Dispatcher.BeginInvoke(ReadAll);
        _watcher.EnableRaisingEvents = true;
        ReadAll();
#pragma warning restore CS0162
    }

    /// <summary>Takes every waiting request, oldest name first, deleting each as it is read.</summary>
    internal void ReadAll()
    {
        bool changed = false;
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(_dir, "watch-request-*.json").OrderBy(f => f, StringComparer.Ordinal).ToList(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return; }
        foreach (var f in files)
        {
            byte[]? data = null;
            try
            {
                if (new FileInfo(f).Length <= WatcherRequest.MaxBytes) data = File.ReadAllBytes(f);
                File.Delete(f);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            if (data is not null && WatcherRequest.Decode(data, DateTimeOffset.Now) is { } d)
                changed |= Board.Apply(d.Command, DateTimeOffset.Now);
        }
        if (changed) Changed?.Invoke();
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
        if (Board.NextChange() is not { } next) return;
        var wait = next - DateTimeOffset.Now;
        _tick.Interval = wait < TimeSpan.FromMilliseconds(100) ? TimeSpan.FromMilliseconds(100) : wait;
        _tick.Start();
    }

    public void Dispose()
    {
        _tick.Stop();
        _watcher?.Dispose();
        _watcher = null;
    }
}
