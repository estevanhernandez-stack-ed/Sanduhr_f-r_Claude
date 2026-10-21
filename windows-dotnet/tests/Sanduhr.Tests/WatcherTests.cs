using System.Text;
using System.Text.Json;
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>Watchers: the state machine, ordering, lost touch and fades, the request and Stop
/// report decoding (with Claude Code's Stop hook input as fixtures), limits and text (parity with
/// the Mac's WatcherTests, minus the store, band file, hook extractor and notch-only cases).</summary>
public class WatcherTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    private static WatcherCommand Start(string id, string title = "CI on main", int? total = null, Uri? link = null,
        bool work = false, string? short_ = null) => new WatcherCommand.Start(id, title, link, total, work, short_);

    private static WatcherCommand Update(string id, int? done = null, string? note = null, WatcherState? state = null) =>
        new WatcherCommand.Update(id, done, note, state);

    private static WatcherCommand End(string id, WatcherState result, string? note = null) =>
        new WatcherCommand.End(id, result, note);

    private static BackgroundTask Bg(string id, string status = "running", string type = "shell",
        string description = "Run tests", string? name = null) => new(id, type, status, description, name);

    private static StopReport Stop(string session, params BackgroundTask[] tasks) => new(session, null, tasks);

    private static byte[] Bytes(string json) => Encoding.UTF8.GetBytes(json);

    // State machine

    [Fact]
    public void StartUpdateWaitAndEnd()
    {
        var b = new WatcherBoard();
        Assert.False(b.Apply(Start("w1", total: 12), T0));
        var w = b.Get("a:w1")!;
        Assert.Equal(WatcherState.Running, w.State);
        Assert.Equal(0, w.Done);
        Assert.Equal("0/12", w.Progress);
        Assert.Equal(WatcherSource.Agent, w.Source);
        // Waiting fires the glow once, not again while it stays waiting.
        Assert.True(b.Apply(Update("w1", 3, "lint passed", WatcherState.Waiting), T0.AddSeconds(10)));
        Assert.False(b.Apply(Update("w1", 4, null, WatcherState.Waiting), T0.AddSeconds(20)));
        Assert.Equal("lint passed", b.Get("a:w1")!.Note);
        Assert.Equal("4/12", b.Get("a:w1")!.Progress);
        // An update without a state keeps the state.
        Assert.False(b.Apply(Update("w1", 5), T0.AddSeconds(25)));
        Assert.Equal(WatcherState.Waiting, b.Get("a:w1")!.State);
        Assert.False(b.Apply(Update("w1", state: WatcherState.Running), T0.AddSeconds(30)));
        b.Apply(End("w1", WatcherState.Passed, "all green"), T0.AddSeconds(60));
        var done = b.Get("a:w1")!;
        Assert.Equal(WatcherState.Passed, done.State);
        Assert.Equal("12/12", done.Progress);
        Assert.Equal(T0.AddSeconds(60), done.Ended);
        Assert.Equal(TimeSpan.FromSeconds(60), done.Elapsed(T0.AddSeconds(600)));
        // Ended: further updates and ends do nothing.
        Assert.False(b.Apply(Update("w1", 1, "late", WatcherState.Waiting), T0.AddSeconds(61)));
        b.Apply(End("w1", WatcherState.Failed), T0.AddSeconds(62));
        Assert.Equal(WatcherState.Passed, b.Get("a:w1")!.State);
        // Unknown ids are ignored.
        Assert.False(b.Apply(Update("nope", 1, null, WatcherState.Waiting), T0));
        Assert.Single(b.Watchers);
    }

    [Fact]
    public void StartingTheSameIdAgainReplacesIt()
    {
        var b = new WatcherBoard();
        b.Apply(Start("w1", "one"), T0);
        b.Apply(Start("w1", "two"), T0.AddSeconds(5));
        Assert.Single(b.Watchers);
        Assert.Equal("two", b.Get("a:w1")!.Title);
    }

    [Fact]
    public void OrderingByUrgencyThenRecency()
    {
        var b = new WatcherBoard();
        foreach (var id in new[] { "run-old", "run-new", "wait", "fail", "pass", "lost" }) b.Apply(Start(id), T0);
        b.Apply(Update("run-old"), T0.AddSeconds(100));
        b.Apply(Update("run-new"), T0.AddSeconds(200));
        b.Apply(Update("wait", state: WatcherState.Waiting), T0.AddSeconds(50));
        b.Apply(End("fail", WatcherState.Failed), T0.AddSeconds(40));
        b.Apply(End("pass", WatcherState.Passed), T0.AddSeconds(300));
        b.Tick(T0.AddSeconds(650)); // "lost" heard nothing for over 10 minutes; the others did
        Assert.Equal(["a:wait", "a:fail", "a:run-new", "a:run-old", "a:lost"], b.Ordered().Select(w => w.Id));
        Assert.True(WatcherState.Waiting.Urgency() < WatcherState.Failed.Urgency());
        Assert.True(WatcherState.Failed.Urgency() < WatcherState.Running.Urgency());
        Assert.True(WatcherState.Running.Urgency() < WatcherState.LostTouch.Urgency());
        Assert.True(WatcherState.LostTouch.Urgency() < WatcherState.Passed.Urgency());
    }

    [Fact]
    public void LostTouchAfterTheWindowAndBackOnAnUpdate()
    {
        var b = new WatcherBoard();
        b.Apply(Start("w1"), T0);
        Assert.False(b.Tick(T0.AddSeconds(600)));
        Assert.Equal(WatcherState.Running, b.Get("a:w1")!.State);
        Assert.True(b.Tick(T0.AddSeconds(601)));
        Assert.Equal(WatcherState.LostTouch, b.Get("a:w1")!.State);
        // Lost touch stays until dismissed or heard from again.
        b.Tick(T0.AddSeconds(86_400));
        Assert.Equal(WatcherState.LostTouch, b.Get("a:w1")!.State);
        b.Apply(Update("w1", note: "back"), T0.AddSeconds(86_401));
        Assert.Equal(WatcherState.Running, b.Get("a:w1")!.State);
        Assert.Equal(T0.AddSeconds(86_401 + 601), b.NextChange());
    }

    [Fact]
    public void AutomaticWatchersLoseTouchOnlyAfterAnHourWithoutAStop()
    {
        var b = new WatcherBoard();
        b.ApplyStop(Stop("s1", Bg("t1")), false, T0);
        b.Tick(T0.AddSeconds(3000));
        Assert.Equal(WatcherState.Running, b.Get("b:s1:t1")!.State);
        b.Tick(T0.AddSeconds(3601));
        Assert.Equal(WatcherState.LostTouch, b.Get("b:s1:t1")!.State);
    }

    [Fact]
    public void PassedAndFinishedFadeFailedStays()
    {
        var b = new WatcherBoard();
        b.Apply(Start("p"), T0);
        b.Apply(Start("f"), T0);
        b.Apply(End("p", WatcherState.Passed), T0.AddSeconds(10));
        b.Apply(End("f", WatcherState.Failed), T0.AddSeconds(10));
        Assert.Equal(T0.AddSeconds(16), b.NextChange());
        Assert.False(b.Tick(T0.AddSeconds(15.9)));
        Assert.True(b.Tick(T0.AddSeconds(16)));
        Assert.Equal(["a:f"], b.Watchers.Select(w => w.Id));
        b.Tick(T0.AddSeconds(86_400));
        Assert.Equal(["a:f"], b.Watchers.Select(w => w.Id));
        Assert.Null(b.NextChange());
        b.Dismiss("a:f");
        Assert.Empty(b.Watchers);
    }

    [Fact]
    public void DismissAllAndClearBySource()
    {
        var b = new WatcherBoard();
        b.Apply(Start("w1"), T0);
        b.ApplyStop(Stop("s", Bg("t")), false, T0);
        b.Clear(WatcherSource.Agent);
        Assert.Equal([WatcherSource.Automatic], b.Watchers.Select(w => w.Source));
        b.DismissAll();
        Assert.Empty(b.Watchers);
    }

    [Fact]
    public void CapacityDropsTheLeastUrgentOldest()
    {
        var b = new WatcherBoard();
        b.Apply(Start("first"), T0);
        b.Apply(Update("first", state: WatcherState.Waiting), T0);
        for (var i = 0; i < WatcherBoard.Capacity; i++) b.Apply(Start($"w{i}"), T0.AddSeconds(i + 1));
        Assert.Equal(WatcherBoard.Capacity, b.Watchers.Count);
        Assert.NotNull(b.Get("a:first")); // waiting is the most urgent: kept
        Assert.Null(b.Get("a:w0"));       // the oldest running one went
    }

    [Fact]
    public void DemoHidesWorkWatchersOnly()
    {
        var b = new WatcherBoard();
        b.Apply(Start("home"), T0);
        b.Apply(Start("job", work: true), T0);
        Assert.Equal(2, WatcherBoard.Shown(b.Ordered(), demo: false).Count);
        Assert.Equal(["a:home"], WatcherBoard.Shown(b.Ordered(), demo: true).Select(w => w.Id));
    }

    [Fact]
    public void StartDerivesAShortTitleUnlessGiven()
    {
        var b = new WatcherBoard();
        b.Apply(Start("a", "PR #140 CI"), T0);
        b.Apply(Start("b", "Deploy the cluster", short_: "deploy"), T0);
        Assert.Equal("PR 140", b.Get("a:a")!.Short);
        Assert.Equal("deploy", b.Get("a:b")!.Short);
    }

    // Automatic watchers from Stop

    private const string StopInFlight = """
        {
          "session_id": "6f1d2c3b-0a9e-4b8c-9d7e-112233445566",
          "transcript_path": "C:\\Users\\someone\\.claude\\projects\\app\\6f1d2c3b.jsonl",
          "cwd": "C:\\Users\\someone\\app",
          "permission_mode": "default",
          "hook_event_name": "Stop",
          "stop_hook_active": false,
          "last_assistant_message": "SECRET-CONVERSATION: the tests are running in the background.",
          "background_tasks": [
            { "id": "bash_1", "type": "shell", "status": "running", "description": "Run the full test suite",
              "command": "SECRET_TOKEN=abc123 npm test -- --runInBand" },
            { "id": "agent_7", "type": "subagent", "status": "running", "description": "Review the auth changes",
              "agent_type": "code-reviewer" },
            { "id": "mon_2", "type": "monitor", "status": "pending", "description": "Watch the deploy log",
              "server": "logs", "tool": "tail" },
            { "id": "wf_3", "type": "workflow", "status": "running", "description": "", "name": "release-train" }
          ],
          "session_crons": []
        }
        """;

    private const string StopOneLeft = """
        {
          "session_id": "6f1d2c3b-0a9e-4b8c-9d7e-112233445566",
          "hook_event_name": "Stop",
          "last_assistant_message": "SECRET-CONVERSATION: one task is still going.",
          "background_tasks": [
            { "id": "bash_1", "type": "shell", "status": "running",
              "description": "Run the full test suite... [+40 chars]",
              "command": "SECRET_TOKEN=abc123 npm test -- --runInBand" }
          ]
        }
        """;

    private const string StopIdle = """
        {
          "session_id": "6f1d2c3b-0a9e-4b8c-9d7e-112233445566",
          "hook_event_name": "Stop",
          "last_assistant_message": "SECRET-CONVERSATION: all done.",
          "background_tasks": [],
          "session_crons": []
        }
        """;

    private const string Sess = "6f1d2c3b-0a9e-4b8c-9d7e-112233445566";

    [Fact]
    public void StopHookInputParsesToTheFewFieldsKept()
    {
        var r = WatcherRequest.DecodeStop(Bytes(StopInFlight))!;
        Assert.Equal(Sess, r.Session);
        Assert.Null(r.Folder); // a Stop hook input names no folder; the hook's report does
        Assert.Equal(["bash_1", "agent_7", "mon_2", "wf_3"], r.Tasks.Select(t => t.Id));
        Assert.Equal(["Run the full test suite", "Review the auth changes", "Watch the deploy log", "release-train"],
            r.Tasks.Select(t => t.Title));
        Assert.Equal(["background shell", "background subagent", "background monitor", "workflow release-train"],
            r.Tasks.Select(t => t.Note));
        Assert.All(r.Tasks, t => Assert.Equal(WatcherState.Running, t.State));
        // Nothing of the command or the conversation survives the parse.
        var kept = r.ToString() + string.Concat(r.Tasks.Select(t => t.ToString()));
        Assert.DoesNotContain("SECRET", kept);
        Assert.DoesNotContain("npm test", kept);
    }

    [Fact]
    public void ATaskThatLeavesTheListEndsAsFinished()
    {
        var b = new WatcherBoard();
        Assert.True(b.ApplyStop(WatcherRequest.DecodeStop(Bytes(StopInFlight))!, false, T0));
        Assert.Equal(4, b.Watchers.Count);
        Assert.All(b.Watchers, w =>
        {
            Assert.Equal(WatcherSource.Automatic, w.Source);
            Assert.Equal(WatcherState.Running, w.State);
        });
        Assert.True(b.ApplyStop(WatcherRequest.DecodeStop(Bytes(StopOneLeft))!, false, T0.AddSeconds(30)));
        Assert.Equal(WatcherState.Running, b.Get($"b:{Sess}:bash_1")!.State);
        Assert.Equal(T0.AddSeconds(30), b.Get($"b:{Sess}:bash_1")!.Touched);
        Assert.Equal(WatcherState.Finished, b.Get($"b:{Sess}:agent_7")!.State);
        Assert.Equal(T0.AddSeconds(30), b.Get($"b:{Sess}:wf_3")!.Ended);
        // Another session's Stop leaves this one alone.
        b.ApplyStop(Stop("other"), false, T0.AddSeconds(31));
        Assert.Equal(WatcherState.Running, b.Get($"b:{Sess}:bash_1")!.State);
        b.ApplyStop(WatcherRequest.DecodeStop(Bytes(StopIdle))!, false, T0.AddSeconds(40));
        Assert.Equal(WatcherState.Finished, b.Get($"b:{Sess}:bash_1")!.State);
        // Finished fades like passed.
        b.Tick(T0.AddSeconds(46));
        Assert.Empty(b.Watchers);
    }

    [Fact]
    public void EndedStatusesInTheListEndTheWatcher()
    {
        var b = new WatcherBoard();
        b.ApplyStop(Stop("s", Bg("a"), Bg("b")), true, T0);
        Assert.All(b.Watchers, w => Assert.True(w.Work));
        b.ApplyStop(Stop("s", Bg("a", "failed"), Bg("b", "killed")), false, T0.AddSeconds(5));
        Assert.Equal(WatcherState.Failed, b.Get("b:s:a")!.State);
        Assert.Equal(WatcherState.Finished, b.Get("b:s:b")!.State);
        // A task first seen already ended is not put up.
        b.ApplyStop(Stop("s", Bg("c", "completed")), false, T0.AddSeconds(6));
        Assert.Null(b.Get("b:s:c"));
    }

    [Fact]
    public void StopReportShapeAndLimits()
    {
        var long_ = new string('x', 300);
        var json = $$"""
            {"schema_version":1,"session":"s-1","folder":"C:\\Users\\x\\.claude-work","tasks":[
              {"id":"t1","type":"shell","status":"running","description":"{{long_}}","name":null},
              {"id":"bad id with spaces","type":"shell","status":"running","description":"x"},
              {"id":"t2","type":"workflow","status":"pending","description":"","name":"deploy"},
              "not an object"]}
            """;
        var r = WatcherRequest.DecodeStop(Bytes(json))!;
        Assert.Equal(@"C:\Users\x\.claude-work", r.Folder);
        Assert.Equal(["t1", "t2"], r.Tasks.Select(t => t.Id));
        Assert.Equal(WatcherLimits.Title, r.Tasks[0].Title.Length);
        Assert.EndsWith("\u2026", r.Tasks[0].Title);
        Assert.Equal("workflow deploy", r.Tasks[1].Note);
        Assert.Equal("deploy", r.Tasks[1].Short);
        Assert.Null(WatcherRequest.DecodeStop(Bytes("""{"schema_version":2,"session":"s","tasks":[]}""")));
        Assert.Null(WatcherRequest.DecodeStop(Bytes("""{"tasks":[]}""")));
        Assert.Null(WatcherRequest.DecodeStop(Bytes("not json")));
        Assert.Null(WatcherRequest.DecodeStop(Bytes("""{"session":"s","folder":"relative\\path","tasks":[]}"""))!.Folder);
        Assert.Null(WatcherRequest.DecodeStop(Bytes("""{"session":"s","folder":"/posix/path","tasks":[]}"""))!.Folder);
        var many = string.Join(",", Enumerable.Range(0, 30).Select(i => $$"""{"id":"t{{i}}","type":"shell","status":"running","description":"d"}"""));
        var big = WatcherRequest.DecodeStop(Bytes($$"""{"session":"s","tasks":[{{many}}]}"""))!;
        Assert.Equal(WatcherBoard.TasksPerStop, big.Tasks.Count);
    }

    [Fact]
    public void BackgroundTaskStatusesMapToStates()
    {
        Assert.Equal(WatcherState.Running, Bg("a", "running").State);
        Assert.Equal(WatcherState.Running, Bg("a", "pending").State);
        Assert.Equal(WatcherState.Running, Bg("a", "mystery").State);
        Assert.Equal(WatcherState.Failed, Bg("a", "ERROR").State);
        Assert.Equal(WatcherState.Finished, Bg("a", "Completed").State);
        Assert.Equal("Background shell", Bg("a", description: "").Title);
        Assert.Equal("Run", Bg("a", description: "Run: tests").Short);
    }

    // Agent requests

    [Fact]
    public void RequestsRoundTripAndAreCheckedAgain()
    {
        var link = new Uri("https://github.com/o/r/actions/runs/1");
        var folder = @"C:\Users\x\.claude";
        foreach (var command in new[]
        {
            Start("w0123456789ab", total: 12, link: link, work: true),
            Update("w0123456789ab", 3, "lint", WatcherState.Waiting),
            End("w0123456789ab", WatcherState.Failed, "2 failed"),
        })
        {
            var d = WatcherRequest.Decode(WatcherRequest.ToJson(command, T0, folder), T0.AddSeconds(1));
            Assert.Equal(command, d?.Command);
        }
        Assert.Equal(folder, WatcherRequest.Decode(WatcherRequest.ToJson(Start("w1"), T0, folder), T0)?.Folder);
        // Stale, from the future, or broken: dropped.
        Assert.Null(WatcherRequest.Decode(WatcherRequest.ToJson(Start("w1"), T0), T0.AddSeconds(601)));
        Assert.NotNull(WatcherRequest.Decode(WatcherRequest.ToJson(Start("w1"), T0), T0.AddSeconds(600)));
        Assert.Null(WatcherRequest.Decode(WatcherRequest.ToJson(Start("w1"), T0.AddSeconds(120)), T0));
        Assert.Null(WatcherRequest.Decode(Bytes("nope"), T0));
        Assert.Null(WatcherRequest.Decode(Bytes("[]"), T0));
        Assert.Null(WatcherRequest.Decode(new byte[WatcherRequest.MaxBytes + 1], T0));
    }

    private static byte[] Raw(Dictionary<string, object?> o)
    {
        var all = new Dictionary<string, object?>
        {
            ["schema_version"] = 1, ["requested_at"] = "2026-10-11T12:00:00.000Z", ["id"] = "w1",
        };
        foreach (var (k, v) in o) all[k] = v;
        return JsonSerializer.SerializeToUtf8Bytes(all);
    }

    [Fact]
    public void RequestsWithBadFieldsAreRejectedOrTrimmed()
    {
        Assert.Null(WatcherRequest.Decode(Raw(new() { ["op"] = "start", ["title"] = "  " }), T0));
        Assert.Null(WatcherRequest.Decode(Raw(new() { ["op"] = "explode", ["title"] = "x" }), T0));
        Assert.Null(WatcherRequest.Decode(Raw(new() { ["op"] = "update", ["state"] = "passed" }), T0));
        Assert.Null(WatcherRequest.Decode(Raw(new() { ["op"] = "end", ["result"] = "finished" }), T0));
        Assert.Null(WatcherRequest.Decode(Raw(new() { ["op"] = "start", ["title"] = "x", ["id"] = "../etc" }), T0));
        Assert.Null(WatcherRequest.Decode(Raw(new() { ["op"] = "start", ["title"] = "x", ["schema_version"] = 2 }), T0));
        Assert.Null(WatcherRequest.Decode(Raw(new() { ["op"] = "start", ["title"] = "x", ["requested_at"] = "yesterday" }), T0));
        Assert.Null(WatcherRequest.Decode(Raw(new() { ["op"] = "start", ["title"] = "x", ["requested_at"] = "2026-10-11T12:00:00" }), T0));
        // A bad link or total is dropped, the card still goes up; a long title is clipped.
        var d = WatcherRequest.Decode(Raw(new()
        {
            ["op"] = "start", ["title"] = new string('t', 200), ["link"] = "http://example.com", ["total"] = 0,
        }), T0);
        var start = Assert.IsType<WatcherCommand.Start>(d?.Command);
        Assert.Equal(WatcherLimits.Title, start.Title.Length);
        Assert.Null(start.Link);
        Assert.Null(start.Total);
        Assert.False(start.Work);
        Assert.Equal(Start("w1", "x"), WatcherRequest.Decode(Raw(new() { ["op"] = "start", ["title"] = "x", ["total"] = true }), T0)?.Command);
        Assert.Equal(Start("w1", "x"), WatcherRequest.Decode(Raw(new() { ["op"] = "start", ["title"] = "x", ["total"] = 2.5 }), T0)?.Command);
        // done is clamped, never negative.
        var upd = Assert.IsType<WatcherCommand.Update>(WatcherRequest.Decode(Raw(new() { ["op"] = "update", ["done"] = -4 }), T0)?.Command);
        Assert.Equal(0, upd.Done);
    }

    [Fact]
    public void RequestFoldersMustBeAbsoluteWindowsPaths()
    {
        Folder(@"C:\Users\x\.claude-work", expect: @"C:\Users\x\.claude-work");
        Folder(@"\\server\share\claude", expect: @"\\server\share\claude");
        Folder("/Users/x/.claude", expect: null);   // the Mac's POSIX form is not a Windows path
        Folder(@"relative\path", expect: null);
        Folder(@"C:drive-relative", expect: null);
        Folder("", expect: null);

        static void Folder(string folder, string? expect) =>
            Assert.Equal(expect, WatcherRequest.Decode(Raw(new() { ["op"] = "start", ["title"] = "x", ["folder"] = folder }), T0)?.Folder);
    }

    [Fact]
    public void ValidIdsAreShortPlainTokens()
    {
        Assert.Equal("w0123456789ab", WatcherRequest.ValidId("w0123456789ab"));
        Assert.Equal("a-b_c.d:e", WatcherRequest.ValidId("a-b_c.d:e"));
        Assert.Null(WatcherRequest.ValidId(null));
        Assert.Null(WatcherRequest.ValidId(""));
        Assert.Null(WatcherRequest.ValidId(new string('a', 65)));
        Assert.NotNull(WatcherRequest.ValidId(new string('a', 64)));
        Assert.Null(WatcherRequest.ValidId("has space"));
        Assert.Null(WatcherRequest.ValidId("../etc"));
        Assert.Null(WatcherRequest.ValidId("caf\u00E9"));
    }

    [Fact]
    public void LinksAreHttpsOnly()
    {
        Assert.NotNull(WatcherLimits.Link("https://example.com/x?y=1"));
        Assert.NotNull(WatcherLimits.Link("HTTPS://example.com/x"));
        foreach (var bad in new[]
        {
            "http://example.com", "javascript:alert(1)", "file:///etc/passwd", "https://", "https://u:p@example.com",
            "https://u@example.com", "sanduhr://debug/x", "https://" + new string('a', 2100), "", "not a url",
        })
        {
            Assert.Null(WatcherLimits.Link(bad));
        }
        Assert.Null(WatcherLimits.Link(null));
    }

    [Fact]
    public void LinesAreFlatTrimmedAndClipped()
    {
        Assert.Equal("a b c", WatcherLimits.Line("a\nb\tc", 10));
        Assert.Null(WatcherLimits.Line("   ", 10));
        Assert.Null(WatcherLimits.Line(null, 10));
        Assert.Equal("abcd\u2026", WatcherLimits.Line("abcdefghijk", 5));
        Assert.Equal("abcde", WatcherLimits.Line("abcde", 5));
        // Clipped text drops the space before the ellipsis.
        Assert.Equal("ab\u2026", WatcherLimits.Line("ab cdefgh", 4));
    }

    // Text

    private static IReadOnlyList<Watcher> Watchers(int n)
    {
        var b = new WatcherBoard();
        for (var i = 0; i < n; i++) b.Apply(Start($"w{i}", $"Deploy the production cluster {i}", total: 10), T0.AddSeconds(i));
        return b.Ordered();
    }

    [Fact]
    public void ElapsedReadsShort()
    {
        Assert.Equal("12s", WatcherText.Elapsed(TimeSpan.FromSeconds(12.7)));
        Assert.Equal("0s", WatcherText.Elapsed(TimeSpan.Zero));
        Assert.Equal("59s", WatcherText.Elapsed(TimeSpan.FromSeconds(59.9)));
        Assert.Equal("4m", WatcherText.Elapsed(TimeSpan.FromSeconds(240)));
        Assert.Equal("1h 05m", WatcherText.Elapsed(TimeSpan.FromSeconds(3600 + 5 * 60)));
    }

    [Fact]
    public void DeskNoteAndSpoken()
    {
        var w = Watchers(1)[0];
        Assert.Null(WatcherText.DeskNote(w));
        w = w with { State = WatcherState.Waiting };
        Assert.Equal("waiting on you", WatcherText.DeskNote(w));
        w = w with { Note = "needs a look" };
        Assert.Equal("needs a look", WatcherText.DeskNote(w));
        Assert.Equal("Deploy the production cluster 0, waiting on you, 1m, 0/10, needs a look",
            WatcherText.Spoken(w, T0.AddSeconds(60)));
        Assert.Equal("lost touch", WatcherText.DeskNote(w with { Note = null, State = WatcherState.LostTouch }));
        Assert.Equal("failed", WatcherText.DeskNote(w with { Note = null, State = WatcherState.Failed }));
        Assert.Null(WatcherText.DeskNote(w with { Note = null, State = WatcherState.Passed }));
    }

    [Fact]
    public void RestAndFullLines()
    {
        var three = Watchers(3);
        var now = T0.AddSeconds(245);
        // The newest is first among equals.
        Assert.Equal("Deploy \u00B7 0/10 +2", WatcherText.RestLine(three, now));
        Assert.Equal("Deploy the production cluster 2 \u00B7 4m \u00B7 0/10 +2", WatcherText.FullLine(three, now));
        Assert.Null(WatcherText.RestLine([], now));
        Assert.Null(WatcherText.FullLine([], now));
        // Without a total the rest line shows the time.
        var b = new WatcherBoard();
        b.Apply(Start("x", "deploy"), T0);
        Assert.Equal("deploy \u00B7 4m", WatcherText.RestLine(b.Ordered(), now));
    }

    [Theory]
    [InlineData("PR 140 CI", "PR 140")]
    [InlineData("PR #140", "PR 140")]
    [InlineData("pull 140", "PR 140")]
    [InlineData("pull request #12 checks", "#12")]
    [InlineData("Issue #77 triage", "#77")]
    [InlineData("Release 2.8.0 build", "v2.8.0")]
    [InlineData("ship v2.8.0", "v2.8.0")]
    [InlineData("Deploy the production cluster", "Deploy")]
    [InlineData("Supercalifragilistic thing", "Supercalif")]
    public void ShortTitleDerivesTheRestingForm(string title, string expected) =>
        Assert.Equal(expected, WatcherText.ShortTitle(title));

    [Fact]
    public void FirstWordDropsPunctuationAndClips()
    {
        Assert.Equal("Build", WatcherText.FirstWord("Build: the thing"));
        Assert.Null(WatcherText.FirstWord("   "));
        Assert.Null(WatcherText.FirstWord("--- x"));
        Assert.Equal("abcdefghij", WatcherText.FirstWord("abcdefghijklmnop"));
    }

    [Fact]
    public void SpokenSaysFailed()
    {
        var b = new WatcherBoard();
        b.Apply(Start("f", "Deploy"), T0);
        b.Apply(End("f", WatcherState.Failed), T0.AddSeconds(10));
        var w = b.Ordered()[0];
        Assert.Contains("failed", WatcherText.Spoken(w, T0.AddSeconds(10)));
        Assert.Equal("failed", WatcherState.Failed.Label());
        Assert.Equal("lost_touch", WatcherState.LostTouch.Wire());
        Assert.Equal("waiting", WatcherState.Waiting.Wire());
    }

    // The switch file and the request file names (the MCP server reads the first and writes the second)

    [Fact]
    public void TheSwitchFileSaysOnOnlyWhenSchema1AndAgentsTrue()
    {
        Assert.Equal("""{"agents":true,"schema_version":1}""", System.Text.Encoding.UTF8.GetString(WatcherRequest.SwitchJson(true)));
        Assert.Equal("""{"agents":false,"schema_version":1}""", System.Text.Encoding.UTF8.GetString(WatcherRequest.SwitchJson(false)));
        Assert.True(WatcherRequest.SwitchAllows(WatcherRequest.SwitchJson(true)));
        Assert.False(WatcherRequest.SwitchAllows(WatcherRequest.SwitchJson(false)));
        Assert.False(WatcherRequest.SwitchAllows(null));
        Assert.False(WatcherRequest.SwitchAllows(Bytes("""{"agents":true}""")));
        Assert.False(WatcherRequest.SwitchAllows(Bytes("""{"schema_version":2,"agents":true}""")));
        Assert.False(WatcherRequest.SwitchAllows(Bytes("""{"schema_version":1,"agents":"true"}""")));
        Assert.False(WatcherRequest.SwitchAllows(Bytes("""{"schema_version":1,"agents":1}""")));
        Assert.False(WatcherRequest.SwitchAllows(Bytes("[true]")));
        Assert.False(WatcherRequest.SwitchAllows(Bytes("not json")));
        Assert.False(WatcherRequest.SwitchAllows(new byte[WatcherRequest.MaxBytes + 1]));
    }

    [Fact]
    public void RequestFileNamesSortByTimeThenCallAndMatchThePattern()
    {
        Assert.Equal("watchers.json", WatcherRequest.SwitchFile);
        Assert.Equal("watch-request-*.json", WatcherRequest.RequestPattern);
        var a = WatcherRequest.RequestFileName(T0, 2, "w0123456789ab");
        var b = WatcherRequest.RequestFileName(T0, 10, "w0123456789ab");
        var c = WatcherRequest.RequestFileName(T0.AddMilliseconds(1), 1, "w0123456789ab");
        Assert.Matches(@"^watch-request-\d{13}-\d{6}-w0123456789ab\.json$", a);
        Assert.True(string.CompareOrdinal(a, b) < 0 && string.CompareOrdinal(b, c) < 0);
    }

    [Fact]
    public void ProgressClampsToTheTotal()
    {
        var b = new WatcherBoard();
        b.Apply(Start("p", total: 5), T0);
        b.Apply(Update("p", 9), T0);
        Assert.Equal("5/5", b.Get("a:p")!.Progress);
        b.Apply(Start("q"), T0);
        Assert.Null(b.Get("a:q")!.Progress);
    }
}
