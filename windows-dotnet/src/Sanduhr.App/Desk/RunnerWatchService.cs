using System.IO;
using System.Net.Http;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>Where runner lines go: the watcher store, which applies them on the UI thread.</summary>
internal interface IRunnerSink
{
    void Apply(string key, string runner, RunnerLine line);
    void Gone(string key);
    void Clear();
}

/// <summary>
/// CI watchers (spec ci-watchers-3.9 section 4): while "Show my CI runners' jobs" is on, lists Docker's
/// containers every <see cref="ListEvery"/>, follows each running runner's log on its own task, and hands
/// each line the replay gate lets through to the sink. Docker down or refusing is a status line, retried
/// every <see cref="RetryEvery"/>, never an error. Logs only state changes, never a job or runner name.
/// <para>
/// Order guarantees: every sink call that carries a line is made under <c>_gate</c> and only while its
/// follow's token is live, and every cancel is followed by a pass through <c>_gate</c> before the sink hears
/// <c>Gone</c> or <c>Clear</c>. A line therefore never reaches the sink after the end or clear that
/// retired its follow (a card could otherwise stay Running for good).
/// </para>
/// </summary>
internal sealed class RunnerWatchService(IDockerApi docker, Func<bool> enabled, IRunnerSink sink, Func<DateTimeOffset>? clock = null, TimeSpan? replayWait = null) : IDisposable
{
    public static readonly TimeSpan ListEvery = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan RetryEvery = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan ReplayWait = TimeSpan.FromSeconds(2);

    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.Now);
    private readonly object _gate = new();
    private readonly Dictionary<string, Follow> _follows = new(StringComparer.Ordinal);
    private CancellationTokenSource? _loop;
    private bool _disposed;
    private RunnerStatus _status = RunnerStatus.Off;

    private sealed record Follow(string Id, string Key, CancellationTokenSource Cts, Task Task);

    public RunnerStatus Status => Volatile.Read(ref _status);

    /// <summary>The status line changed (raised on the loop's thread, outside the lock).</summary>
    public event Action? StatusChanged;

    /// <summary>The setting changed, or the app started: start or stop the loop.</summary>
    public void Refresh()
    {
        if (_disposed) return;
        if (enabled())
        {
            if (_loop is not null) return;
            var loop = new CancellationTokenSource();
            _loop = loop;
            _ = Task.Run(() => LoopAsync(loop.Token));
            return;
        }
        StopLoop();
        StopAllFollows(announce: false);
        sink.Clear();
        SetStatus(RunnerStatus.Off);
    }

    private void StopLoop()
    {
        var loop = _loop;
        _loop = null;
        if (loop is null) return;
        loop.Cancel();
        loop.Dispose();
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                // Never let the loop end silently: log the type, keep the retry wait.
                System.Diagnostics.Debug.WriteLine($"[Sanduhr] CI watchers: {e.GetType().Name}");
            }
            var wait = Status.Kind is RunnerStatusKind.DockerDown or RunnerStatusKind.DockerDenied or RunnerStatusKind.DockerRefused ? RetryEvery : ListEvery;
            try { await Task.Delay(wait, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>One list: the status, a follow for each new runner, an end for each runner that left.</summary>
    internal async Task PollOnceAsync(CancellationToken ct)
    {
        IReadOnlyList<DockerContainer> runners;
        try
        {
            runners = DockerContainers.Runners(DockerContainers.Parse(await docker.ListContainersAsync(ct).ConfigureAwait(false)));   // streamer: key - container list parsed into runner names; the Watchers page and Desk draw them through Mask.Name (CP 19)
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or TimeoutException or UnauthorizedAccessException or OperationCanceledException)
        {
            if (ct.IsCancellationRequested || _disposed) return;
            StopAllFollows(announce: true);
            var kind = DockerPipe.Classify(e) switch
            {
                DockerFailure.Denied => RunnerStatusKind.DockerDenied,
                DockerFailure.Refused => RunnerStatusKind.DockerRefused,
                _ => RunnerStatusKind.DockerDown,
            };
            SetStatus(new RunnerStatus(kind, []), ct);
            return;
        }
        if (ct.IsCancellationRequested || _disposed) return;

        List<Follow> left = [], replaced = [];
        lock (_gate)
        {
            // Rechecked under the lock: Refresh(off) and Dispose cancel first and then take this lock.
            if (ct.IsCancellationRequested || _disposed) return;
            // The follow table is keyed by the container's own name, never the shared service label.
            foreach (var (key, f) in _follows.Where(kv => runners.All(r => r.Key != kv.Key)).ToList())
            {
                _follows.Remove(key);
                left.Add(f);
            }
            foreach (var r in runners)
            {
                if (_follows.TryGetValue(r.Key, out var existing))
                {
                    if (existing.Id == r.Id && !existing.Task.IsCompleted) continue;
                    if (existing.Id != r.Id)
                    {
                        // Recreated under the same name: follow the new one. The old card ends here, before the new
                        // follow exists, so it ends even if the new container's log can't be read. Deliver checks ct
                        // under this lock and the sink queues in order, so this Gone can't overtake or end the new
                        // follow's card.
                        existing.Cts.Cancel();
                        sink.Gone(existing.Key);
                        replaced.Add(existing);
                    }
                    else existing.Cts.Dispose(); // the stream ended: follow again
                }
                var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var token = cts.Token;
                var (id, key, name) = (r.Id, r.Key, r.Name);
                // Task.Run: the follow's synchronous prefix must not run under the lock.
                _follows[key] = new Follow(id, key, cts, Task.Run(() => FollowAsync(id, key, name, token)));
            }
        }
        Retire(left, announce: true);
        Retire(replaced, announce: false); // already cancelled and ended under the lock; this waits out the barrier and disposes
        SetStatus(runners.Count == 0
            ? new RunnerStatus(RunnerStatusKind.NoRunners, [])
            : new RunnerStatus(RunnerStatusKind.Watching, runners.Select(r => r.Name).ToList()), ct);
    }

    /// <summary>The tests' way to wait for the follows the last poll started (each fake stream ends).</summary>
    internal Task WhenFollowsSettle()
    {
        lock (_gate) return Task.WhenAll(_follows.Values.Select(f => f.Task));
    }

    private async Task FollowAsync(string id, string key, string runner, CancellationToken ct)
    {
        try
        {
            await using var stream = await docker.FollowLogsAsync(id, ct).ConfigureAwait(false);
            var follow = new RunnerFollow(_clock());
            var frames = new DockerLogFrames();
            using var replayEnd = new CancellationTokenSource(replayWait ?? ReplayWait);
            using var _ = replayEnd.Token.Register(() =>
            {
                try { Deliver(key, runner, ct, follow.EndReplay); }
                catch (Exception e) { System.Diagnostics.Debug.WriteLine($"[Sanduhr] CI watchers: {e.GetType().Name}"); }
            });
            var buffer = new byte[16 * 1024];
            int n;
            while ((n = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                foreach (var text in frames.Push(buffer.AsSpan(0, n)))
                    if (RunnerLog.Parse(text) is { } line) Deliver(key, runner, ct, () => follow.Feed(line));
            }
            Deliver(key, runner, ct, follow.EndReplay); // a stream that ended before the wait ran out
        }
        catch (Exception e) when (e is OperationCanceledException or HttpRequestException or IOException or UnauthorizedAccessException)
        {
            // The next poll follows again if the runner is still there.
        }
        catch (Exception e)
        {
            // Unexpected: log the type and end this follow; the next poll starts a new one.
            System.Diagnostics.Debug.WriteLine($"[Sanduhr] CI watchers: {e.GetType().Name}");
        }
    }

    /// <summary>Runs one step of the runner's <see cref="RunnerFollow"/> and applies what it lets through,
    /// all under the lock: the replay timer and the reader never touch the gate at once. A follow that was
    /// cancelled delivers nothing, so no line lands after the end or clear that retired it.</summary>
    private void Deliver(string key, string runner, CancellationToken ct, Func<IReadOnlyList<RunnerLine>> step)
    {
        lock (_gate)
        {
            if (ct.IsCancellationRequested) return;
            foreach (var line in step()) sink.Apply(key, runner, line);
        }
    }

    /// <summary>Stops every follow. <paramref name="announce"/>: the sink hears <c>Gone</c> for each, so
    /// an open card ends as finished (Docker went away); a switch-off clears the sink instead.</summary>
    private void StopAllFollows(bool announce)
    {
        List<Follow> all;
        lock (_gate)
        {
            all = [.. _follows.Values];
            _follows.Clear();
        }
        Retire(all, announce);
    }

    /// <summary>Cancels follows already removed from the table, waits out any delivery in flight, then
    /// tells the sink they are gone. Cancel happens outside the lock; the empty lock after it is the barrier.</summary>
    private void Retire(List<Follow> follows, bool announce)
    {
        if (follows.Count == 0) return;
        foreach (var f in follows) f.Cts.Cancel();
        lock (_gate) { }
        foreach (var f in follows)
        {
            f.Cts.Dispose();
            if (announce) sink.Gone(f.Key);
        }
    }

    private void SetStatus(RunnerStatus status, CancellationToken ct = default)
    {
        lock (_gate)
        {
            // A poll that was cancelled (switch-off) or disposed meanwhile must not overwrite the status.
            if (_disposed || ct.IsCancellationRequested || status.Equals(_status)) return;
            if (status.Kind != _status.Kind)
                System.Diagnostics.Debug.WriteLine($"[Sanduhr] CI watchers: {status.Kind}");
            Volatile.Write(ref _status, status);
        }
        StatusChanged?.Invoke();
    }

    public void Dispose()
    {
        lock (_gate) _disposed = true;
        StopLoop();
        StopAllFollows(announce: false);
    }
}
