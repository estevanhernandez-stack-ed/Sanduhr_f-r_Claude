using System.Buffers.Binary;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Channels;
using Sanduhr.App.Desk;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>The runner watch loop against a fake Docker (spec ci-watchers-3.9 sections 5 and 8).</summary>
public class RunnerWatchServiceTests
{
    private const string Id1 = "1111111111111111111111111111111111111111111111111111111111111111";
    private const string Id2 = "2222222222222222222222222222222222222222222222222222222222222222";
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 20, 0, 0, TimeSpan.Zero);

    private static string ListJson(params (string Id, string Service, string Image)[] cs) =>
        "[" + string.Join(",", cs.Select(c =>
            $$$"""{"Id":"{{{c.Id}}}","Names":["/{{{c.Service}}}"],"Image":"{{{c.Image}}}","State":"running","Labels":{"com.docker.compose.service":"{{{c.Service}}}"}}""")) + "]";

    /// <summary>Scaled replicas: the same service label, each its own container name.</summary>
    private static string Replicas(params (string Id, string Name)[] cs) =>
        "[" + string.Join(",", cs.Select(c =>
            $$$"""{"Id":"{{{c.Id}}}","Names":["/{{{c.Name}}}"],"Image":"myoung34/github-runner:latest","State":"running","Labels":{"com.docker.compose.service":"runner"}}""")) + "]";

    private static byte[] Frames(params string[] lines) => lines.SelectMany(l =>
    {
        var payload = Encoding.UTF8.GetBytes(l + "\n");
        var header = new byte[8];
        header[0] = 1;
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)payload.Length);
        return header.Concat(payload);
    }).ToArray();

    private static string Line(DateTimeOffset at, string text) =>
        $"{at.UtcDateTime:yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'} {at.UtcDateTime:yyyy-MM-dd HH:mm:ss'Z'}: {text}";

    private sealed class FakeDocker : IDockerApi
    {
        public Func<byte[]> List = () => Encoding.UTF8.GetBytes("[]");
        public Func<CancellationToken, Task<byte[]>>? ListAsync;
        public Dictionary<string, Queue<byte[]>> Logs = new();
        public Func<string, Stream>? Open;
        public int Follows;
        public Task<byte[]> ListContainersAsync(CancellationToken ct) => ListAsync is { } f ? f(ct) : Task.FromResult(List());
        public Task<Stream> FollowLogsAsync(string id, CancellationToken ct)
        {
            Interlocked.Increment(ref Follows);
            if (Open is { } open) return Task.FromResult(open(id));
            var bytes = Logs.TryGetValue(id, out var q) && q.Count > 0 ? q.Dequeue() : [];
            return Task.FromResult<Stream>(new MemoryStream(bytes));
        }
    }

    /// <summary>A log stream that stays open: yields what it is given, then waits for more until completed.
    /// With <paramref name="ignoreCancel"/> a cancelled read keeps waiting, the way a reader that is mid-delivery
    /// does when its follow is stopped.</summary>
    private sealed class HeldStream(byte[] initial, bool ignoreCancel = false) : Stream
    {
        private readonly Channel<byte[]> _ch = Channel.CreateUnbounded<byte[]>();
        private byte[] _cur = [];
        private int _pos;
        private bool _seeded;
        public readonly TaskCompletionSource Disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Push(byte[] bytes) => _ch.Writer.TryWrite(bytes);
        public void Complete() => _ch.Writer.TryComplete();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (!_seeded)
            {
                _seeded = true;
                if (initial.Length > 0) _ch.Writer.TryWrite(initial);
            }
            if (_pos >= _cur.Length)
            {
                if (!await _ch.Reader.WaitToReadAsync(ignoreCancel ? CancellationToken.None : ct).ConfigureAwait(false)) return 0;
                _ch.Reader.TryRead(out var next);
                _cur = next!;
                _pos = 0;
            }
            var n = Math.Min(buffer.Length, _cur.Length - _pos);
            _cur.AsMemory(_pos, n).CopyTo(buffer);
            _pos += n;
            return n;
        }

        protected override void Dispose(bool disposing)
        {
            Disposed.TrySetResult();
            base.Dispose(disposing);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class Sink : IRunnerSink
    {
        public readonly WatcherBoard Board = new();
        public int Glows, Clears, Applies, Onlines;
        public Watcher? Card(string id) { lock (Board) return Board.Get(id); }
        public int Count { get { lock (Board) return Board.Watchers.Count; } }
        public void Apply(string key, string runner, RunnerLine line) { lock (Board) { Applies++; if (line is RunnerLine.Online) Onlines++; if (Board.ApplyRunner(key, runner, line, Now).Failed) Glows++; } }
        public void Gone(string key) { lock (Board) Board.RunnerGone(key, Now); }
        public void Clear() { lock (Board) { Board.Clear(WatcherSource.Runner); Clears++; } }
    }

    private static RunnerWatchService Service(FakeDocker d, Sink s, bool on = true, TimeSpan? replayWait = null) => new(d, () => on, s, () => Now, replayWait);

    private static string OneRunner() => ListJson((Id1, "echo-1", "dunder-gh-runner:2.337.0"));

    /// <summary>Waits for <paramref name="done"/> with a deadline, never an unbounded sleep.</summary>
    private static async Task Until(Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!done())
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out waiting");
            await Task.Delay(5);
        }
    }

    [Fact]
    public async Task ListsRunnersOnlyAndSaysSo()
    {
        var d = new FakeDocker { List = () => Encoding.UTF8.GetBytes(ListJson((Id1, "alpha-1", "dunder-gh-runner:2.337.0"), (Id2, "egress-proxy", "dunder-egress-proxy:1"))) };
        using var svc = Service(d, new Sink());
        await svc.PollOnceAsync(CancellationToken.None);
        Assert.Equal(new RunnerStatus(RunnerStatusKind.Watching, ["alpha-1"]), svc.Status);
        await svc.WhenFollowsSettle();
        Assert.Equal(1, d.Follows);
    }

    [Fact]
    public async Task NoRunners()
    {
        using var svc = Service(new FakeDocker(), new Sink());
        await svc.PollOnceAsync(CancellationToken.None);
        Assert.Equal(RunnerStatusKind.NoRunners, svc.Status.Kind);
    }

    [Fact]
    public async Task DockerDown()
    {
        var d = new FakeDocker { List = () => throw new HttpRequestException("x", new TimeoutException()) };
        using var svc = Service(d, new Sink());
        await svc.PollOnceAsync(CancellationToken.None);
        Assert.Equal(RunnerStatusKind.DockerDown, svc.Status.Kind);
    }

    [Fact]
    public async Task AnErrorStatusIsNotDockerBeingDown()
    {
        var d = new FakeDocker { List = () => throw new HttpRequestException("x", null, System.Net.HttpStatusCode.BadRequest) };
        using var svc = Service(d, new Sink());
        await svc.PollOnceAsync(CancellationToken.None);
        Assert.Equal(RunnerStatusKind.DockerRefused, svc.Status.Kind);
        Assert.Equal("Docker answered but refused the request.", svc.Status.Text);
    }

    [Fact]
    public async Task AccessDeniedShowsTheGroupLine()
    {
        var d = new FakeDocker { List = () => throw new HttpRequestException("x", new UnauthorizedAccessException()) };
        using var svc = Service(d, new Sink());
        var raised = 0;
        svc.StatusChanged += () => raised++;
        await svc.PollOnceAsync(CancellationToken.None);
        await svc.PollOnceAsync(CancellationToken.None);
        Assert.Equal("Docker refused the connection. Your Windows account needs to be in the docker-users group.", svc.Status.Text);
        Assert.Equal(1, raised); // once per change, not per retry
    }

    [Fact]
    public async Task ALiveJobBecomesACardAndAFailureGlows()
    {
        var d = new FakeDocker { List = () => Encoding.UTF8.GetBytes(ListJson((Id1, "echo-1", "dunder-gh-runner:2.337.0"))) };
        d.Logs[Id1] = new([Frames(Line(Now, "Running job: build"), Line(Now.AddSeconds(5), "Job build completed with result: Failed"))]);
        var sink = new Sink();
        using var svc = Service(d, sink);
        await svc.PollOnceAsync(CancellationToken.None);
        await svc.WhenFollowsSettle();
        Assert.Equal(WatcherState.Failed, sink.Board.Get("r:echo-1:build")!.State);
        Assert.Equal(1, sink.Glows);
    }

    [Fact]
    public async Task ReplayedFinishedJobsPutUpNothing()
    {
        var d = new FakeDocker { List = () => Encoding.UTF8.GetBytes(ListJson((Id1, "echo-1", "dunder-gh-runner:2.337.0"))) };
        d.Logs[Id1] = new([Frames(Line(Now.AddHours(-1), "Running job: build"), Line(Now.AddHours(-1).AddMinutes(1), "Job build completed with result: Succeeded"))]);
        var sink = new Sink();
        using var svc = Service(d, sink);
        await svc.PollOnceAsync(CancellationToken.None);
        await svc.WhenFollowsSettle();
        Assert.Empty(sink.Board.Watchers);
    }

    [Fact]
    public async Task StreamEndRefollowsWithoutDuplicates()
    {
        var d = new FakeDocker { List = () => Encoding.UTF8.GetBytes(ListJson((Id1, "echo-1", "dunder-gh-runner:2.337.0"))) };
        var open = Frames(Line(Now.AddMinutes(-5), "Running job: build"));
        d.Logs[Id1] = new([open, open]); // the stream ends, the same tail comes back
        var sink = new Sink();
        using var svc = Service(d, sink);
        await svc.PollOnceAsync(CancellationToken.None);
        await svc.WhenFollowsSettle();
        await svc.PollOnceAsync(CancellationToken.None);
        await svc.WhenFollowsSettle();
        Assert.Single(sink.Board.Watchers);
        Assert.Equal(WatcherState.Running, sink.Board.Get("r:echo-1:build")!.State);
        Assert.Equal(2, d.Follows);
    }

    [Fact]
    public async Task ARunnerThatLeavesEndsItsCard()
    {
        var listed = true;
        var d = new FakeDocker { List = () => Encoding.UTF8.GetBytes(listed ? ListJson((Id1, "echo-1", "dunder-gh-runner:2.337.0")) : "[]") };
        d.Logs[Id1] = new([Frames(Line(Now, "Running job: build"))]);
        var sink = new Sink();
        using var svc = Service(d, sink);
        await svc.PollOnceAsync(CancellationToken.None);
        await svc.WhenFollowsSettle();
        listed = false;
        await svc.PollOnceAsync(CancellationToken.None);
        Assert.Equal(WatcherState.Finished, sink.Board.Get("r:echo-1:build")!.State);
    }

    [Fact]
    public async Task ReplicasThatShareALabelDoNotEndEachOthersCards()
    {
        var d = new FakeDocker { List = () => Encoding.UTF8.GetBytes(Replicas((Id1, "ci-runner-1"), (Id2, "ci-runner-2"))) };
        d.Logs[Id1] = new([Frames(Line(Now, "Running job: build"))]);
        d.Logs[Id2] = new([Frames(Line(Now, "Running job: test"), Line(Now.AddSeconds(5), "Job test completed with result: Failed"))]);
        var sink = new Sink();
        using var svc = Service(d, sink);
        await svc.PollOnceAsync(CancellationToken.None);
        await svc.WhenFollowsSettle();
        Assert.Equal("Watching 2 runners: ci-runner-1, ci-runner-2", svc.Status.Text);
        Assert.Equal(WatcherState.Running, sink.Card("r:ci-runner-1:build")!.State); // the sibling's start and end left it alone
        Assert.Equal(WatcherState.Failed, sink.Card("r:ci-runner-2:test")!.State);
        Assert.Equal(1, sink.Glows);
    }

    [Fact]
    public async Task ASiblingComingOnlineLeavesTheBusyOnesCardRunning()
    {
        var both = false;
        var d = new FakeDocker { List = () => Encoding.UTF8.GetBytes(both ? Replicas((Id1, "ci-runner-1"), (Id2, "ci-runner-2")) : Replicas((Id1, "ci-runner-1"))) };
        var busy = new HeldStream(Frames(Line(Now, "Running job: build"))); // stays open: a job in progress
        d.Open = id => id == Id1 ? busy : new MemoryStream([]); // the sibling is idle: its follow opens with Online
        var sink = new Sink();
        using var svc = Service(d, sink);
        await svc.PollOnceAsync(CancellationToken.None);
        await Until(() => sink.Card("r:ci-runner-1:build") is not null);
        both = true;
        await svc.PollOnceAsync(CancellationToken.None);
        await Until(() => sink.Onlines == 2); // one for the first runner's live start, one for the idle sibling
        Assert.Equal(2, d.Follows);
        Assert.Equal(WatcherState.Running, sink.Card("r:ci-runner-1:build")!.State);
        busy.Complete();
    }

    [Fact]
    public async Task ASiblingThatLeavesEndsOnlyItsOwnCard()
    {
        var both = true;
        var d = new FakeDocker { List = () => Encoding.UTF8.GetBytes(both ? Replicas((Id1, "ci-runner-1"), (Id2, "ci-runner-2")) : Replicas((Id1, "ci-runner-1"))) };
        var s1 = new HeldStream(Frames(Line(Now, "Running job: build")));
        var s2 = new HeldStream(Frames(Line(Now, "Running job: test")));
        d.Open = id => id == Id1 ? s1 : s2;
        var sink = new Sink();
        using var svc = Service(d, sink);
        await svc.PollOnceAsync(CancellationToken.None);
        await Until(() => sink.Count == 2);
        both = false;
        await svc.PollOnceAsync(CancellationToken.None);
        Assert.Equal(WatcherState.Running, sink.Card("r:ci-runner-1:build")!.State);
        Assert.Equal(WatcherState.Finished, sink.Card("r:ci-runner-2:test")!.State);
        s1.Complete();
    }

    [Fact]
    public async Task ARecreatedContainerKeepsItsNameAndIsFollowedAgain()
    {
        var id = Id1;
        var d = new FakeDocker { List = () => Encoding.UTF8.GetBytes(Replicas((id, "ci-runner-1"))) };
        var old = new HeldStream(Frames(Line(Now, "Running job: build")));
        var fresh = new HeldStream([]);
        d.Open = i => i == Id1 ? old : fresh;
        var sink = new Sink();
        using var svc = Service(d, sink, replayWait: TimeSpan.FromMilliseconds(50));
        await svc.PollOnceAsync(CancellationToken.None);
        await Until(() => sink.Card("r:ci-runner-1:build") is not null);
        id = Id2; // Compose recreated it: same name, new id
        await svc.PollOnceAsync(CancellationToken.None);
        await old.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Until(() => d.Follows == 2); // the old follow is cancelled before the new one starts, so disposal no longer implies it
        await Until(() => sink.Card("r:ci-runner-1:build")!.State == WatcherState.Finished); // the old follow's Gone ends the old card
        fresh.Complete();
    }

    [Fact]
    public async Task ARecreatedContainerWhoseLogCannotBeReadStillEndsTheOldCard()
    {
        var id = Id1;
        var d = new FakeDocker { List = () => Encoding.UTF8.GetBytes(Replicas((id, "ci-runner-1"))) };
        var old = new HeldStream(Frames(Line(Now, "Running job: build")));
        d.Open = i => i == Id1 ? old : throw new HttpRequestException("the new container's logs call fails");
        var sink = new Sink();
        using var svc = Service(d, sink, replayWait: TimeSpan.FromMilliseconds(50));
        await svc.PollOnceAsync(CancellationToken.None);
        await Until(() => sink.Card("r:ci-runner-1:build") is not null);
        id = Id2; // Compose recreated it: same name, new id, and its logs can't be read
        await svc.PollOnceAsync(CancellationToken.None);
        await svc.WhenFollowsSettle().WaitAsync(TimeSpan.FromSeconds(10)); // the new follow failed without delivering a single line
        Assert.Equal(WatcherState.Finished, sink.Card("r:ci-runner-1:build")!.State); // the replaced follow's Gone ended it
        old.Complete();
    }

    [Fact]
    public async Task SwitchingOffClearsAndSaysNothing()
    {
        var on = true;
        var sink = new Sink();
        using var svc = new RunnerWatchService(new FakeDocker(), () => on, sink, () => Now);
        await svc.PollOnceAsync(CancellationToken.None);
        on = false;
        svc.Refresh();
        Assert.Equal(RunnerStatus.Off, svc.Status);
        Assert.Equal(1, sink.Clears);
    }

    [Fact]
    public async Task DockerGoingDownEndsTheOpenCardsAndTheyStayEnded()
    {
        var up = true;
        var d = new FakeDocker { List = () => up ? Encoding.UTF8.GetBytes(OneRunner()) : throw new HttpRequestException("x", new TimeoutException()) };
        d.Logs[Id1] = new([Frames(Line(Now, "Running job: build"))]);
        var sink = new Sink();
        using var svc = Service(d, sink);
        await svc.PollOnceAsync(CancellationToken.None);
        await svc.WhenFollowsSettle();
        Assert.Equal(WatcherState.Running, sink.Card("r:echo-1:build")!.State);
        up = false;
        await svc.PollOnceAsync(CancellationToken.None);
        Assert.Equal(RunnerStatusKind.DockerDown, svc.Status.Kind);
        Assert.Equal(WatcherState.Finished, sink.Card("r:echo-1:build")!.State);
        up = true;
        d.List = () => Encoding.UTF8.GetBytes("[]");
        await svc.PollOnceAsync(CancellationToken.None);
        Assert.DoesNotContain(sink.Board.Watchers, w => !w.State.IsEnded());
    }

    [Fact]
    public async Task ARefollowThatReplaysTheJobAsEndedEndsTheCard()
    {
        var d = new FakeDocker { List = () => Encoding.UTF8.GetBytes(OneRunner()) };
        d.Logs[Id1] = new([
            Frames(Line(Now, "Running job: build")),
            Frames(Line(Now.AddMinutes(-2), "Running job: build"), Line(Now.AddMinutes(-1), "Job build completed with result: Succeeded")), // ended 60 s ago
        ]);
        var sink = new Sink();
        using var svc = Service(d, sink);
        await svc.PollOnceAsync(CancellationToken.None);
        await svc.WhenFollowsSettle();
        Assert.Equal(WatcherState.Running, sink.Card("r:echo-1:build")!.State);
        await svc.PollOnceAsync(CancellationToken.None);
        await svc.WhenFollowsSettle();
        Assert.Equal(WatcherState.Finished, sink.Card("r:echo-1:build")!.State);
    }

    [Fact]
    public async Task NothingLandsAfterSwitchingOff()
    {
        var held = new HeldStream([], ignoreCancel: true);
        var d = new FakeDocker { List = () => Encoding.UTF8.GetBytes(OneRunner()), Open = _ => held };
        var on = true;
        var sink = new Sink();
        using var svc = new RunnerWatchService(d, () => on, sink, () => Now);
        await svc.PollOnceAsync(CancellationToken.None);
        on = false;
        svc.Refresh();
        Assert.Equal(1, sink.Clears);
        held.Push(Frames(Line(Now, "Running job: build"))); // the reader delivers after the clear
        held.Complete();
        await held.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, sink.Applies);
        Assert.Equal(0, sink.Count);
    }

    [Fact]
    public async Task TheReplayTimerPutsUpAnOpenJobWithoutMoreBytes()
    {
        var held = new HeldStream(Frames(Line(Now.AddMinutes(-5), "Running job: build")));
        var d = new FakeDocker { List = () => Encoding.UTF8.GetBytes(OneRunner()), Open = _ => held };
        var sink = new Sink();
        using var svc = Service(d, sink, replayWait: TimeSpan.FromMilliseconds(50));
        await svc.PollOnceAsync(CancellationToken.None);
        await Until(() => sink.Card("r:echo-1:build") is not null);
        Assert.Equal(WatcherState.Running, sink.Card("r:echo-1:build")!.State);
        held.Complete();
        await svc.WhenFollowsSettle().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Single(sink.Board.Watchers);
    }

    [Fact]
    public async Task SwitchingOnOffOnRunsOneLoopAndOneCard()
    {
        var d = new FakeDocker { List = () => Encoding.UTF8.GetBytes(OneRunner()) };
        d.Logs[Id1] = new([Frames(Line(Now, "Running job: build")), Frames(Line(Now, "Running job: build"))]);
        var on = true;
        var sink = new Sink();
        using var svc = new RunnerWatchService(d, () => on, sink, () => Now);
        svc.Refresh();
        svc.Refresh(); // already on: no second loop
        await Until(() => sink.Card("r:echo-1:build") is not null);
        Assert.Equal(1, Volatile.Read(ref d.Follows));
        on = false;
        svc.Refresh();
        Assert.Equal(RunnerStatus.Off, svc.Status);
        Assert.Equal(0, sink.Count);
        on = true;
        svc.Refresh();
        await Until(() => sink.Card("r:echo-1:build") is not null);
        Assert.Equal(2, Volatile.Read(ref d.Follows));
        Assert.Single(sink.Board.Watchers);
        Assert.Equal(WatcherState.Running, sink.Card("r:echo-1:build")!.State);
    }

    [Fact]
    public async Task AListThatReturnsAfterTheLoopWasCancelledSaysNothing()
    {
        var release = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var d = new FakeDocker { ListAsync = _ => { entered.TrySetResult(); return release.Task; } };
        using var svc = Service(d, new Sink());
        using var cts = new CancellationTokenSource();
        var poll = svc.PollOnceAsync(cts.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cts.Cancel();
        release.SetResult(Encoding.UTF8.GetBytes(OneRunner()));
        await poll;
        Assert.Equal(RunnerStatus.Off, svc.Status);
        Assert.Equal(0, d.Follows);
    }

    [Fact]
    public async Task AListThatReturnsAfterDisposeSaysNothing()
    {
        var release = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var d = new FakeDocker { ListAsync = _ => { entered.TrySetResult(); return release.Task; } };
        var svc = Service(d, new Sink());
        var poll = svc.PollOnceAsync(CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        svc.Dispose();
        release.SetResult(Encoding.UTF8.GetBytes(OneRunner()));
        await poll;
        Assert.Equal(RunnerStatus.Off, svc.Status);
        Assert.Equal(0, d.Follows);
    }
}
