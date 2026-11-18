using System.Text;
using System.Text.Json;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public sealed class AgentActivityWatcherTests : IDisposable
{
    private readonly TempDir _t = new();
    private DateTimeOffset _now = DateTimeOffset.UtcNow.AddSeconds(-2);   // a little behind the file system, so new files are newer
    private bool _codexOn = true, _antigravityOn = true, _claudeOn = true;
    private readonly List<AgentTurnEvent> _events = [];
    private readonly List<string> _off = [];

    private string CodexHome => Path.Combine(_t.Path, "codex");
    private string Sessions => Path.Combine(CodexHome, "sessions", "2026", "11", "17");
    private string AgyHome => Path.Combine(_t.Path, "agy");
    private string Signals => Path.Combine(_t.Path, "signals");

    public AgentActivityWatcherTests()
    {
        Directory.CreateDirectory(Sessions);
        Directory.CreateDirectory(Path.Combine(AgyHome, "brain"));
        Directory.CreateDirectory(Signals);
    }

    public void Dispose() => _t.Dispose();

    private AgentActivityWatcher Sut()
    {
        var w = new AgentActivityWatcher(
            () => _codexOn ? [CodexHome] : [],
            () => _antigravityOn ? AgyHome : null,
            conversation => @"C:\proj\" + conversation,
            () => _claudeOn, Signals, () => _now);
        w.Turn += _events.Add;
        w.AgentOff += _off.Add;
        return w;
    }

    private static string Iso(DateTimeOffset t) => t.ToString("o");

    private static string CodexLine(DateTimeOffset at, string type, Dictionary<string, object?> payload) =>
        JsonSerializer.Serialize(new Dictionary<string, object?> { ["timestamp"] = Iso(at), ["type"] = type, ["payload"] = payload }) + "\n";

    private string Meta(string? parent = null) =>
        CodexLine(_now, "session_meta", new() { ["id"] = "s", ["cwd"] = @"C:\proj\alpha", ["parent_thread_id"] = parent });

    private string Started(DateTimeOffset at) => CodexLine(at, "event_msg", new() { ["type"] = "task_started", ["turn_id"] = "t" });

    private string Complete(DateTimeOffset at, int ms = 124000) =>
        CodexLine(at, "event_msg", new() { ["type"] = "task_complete", ["turn_id"] = "t", ["duration_ms"] = ms });

    private string Rollout(string name, string text, bool newFile = false)
    {
        var path = Path.Combine(Sessions, name + ".jsonl");
        File.AppendAllText(path, text);
        if (newFile) File.SetCreationTimeUtc(path, _now.UtcDateTime.AddSeconds(5));
        return path;
    }

    private static string Step(DateTimeOffset at, string type, bool toolCalls = false)
    {
        var o = new Dictionary<string, object?> { ["type"] = type, ["status"] = "DONE", ["created_at"] = Iso(at) };
        if (toolCalls) o["tool_calls"] = new[] { 1 };
        return JsonSerializer.Serialize(o) + "\n";
    }

    private string Transcript(string conversation, string text, bool newFile = false)
    {
        var dir = Path.Combine(AgyHome, "brain", conversation, ".system_generated", "logs");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "transcript.jsonl");
        File.AppendAllText(path, text);
        if (newFile) File.SetCreationTimeUtc(path, _now.UtcDateTime.AddSeconds(5));
        return path;
    }

    private void Signal(string ev, string session, long? atMs = null, int pid = 100, string? raw = null)
    {
        long at = atMs ?? _now.ToUnixTimeMilliseconds();
        var json = raw ?? JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["v"] = 1, ["event"] = ev, ["cwd"] = @"C:\proj\alpha", ["session"] = session, ["at"] = at,
        });
        File.WriteAllText(Path.Combine(Signals, $"cc-signal-{at}-{pid}.json"), json);
    }

    // ---- Codex ----

    [Fact]
    public void History_is_not_replayed_and_new_lines_are_events()
    {
        Rollout("rollout-a", Meta() + Started(_now.AddMinutes(-9)) + Complete(_now.AddMinutes(-7)));
        using var w = Sut();
        w.Scan();
        Assert.Empty(_events);

        Rollout("rollout-a", Started(_now) + Complete(_now.AddSeconds(124)));
        w.Scan();
        Assert.Equal([AgentTurnKind.Started, AgentTurnKind.Finished], _events.Select(e => e.Kind));
        Assert.All(_events, e => Assert.Equal(("codex", "rollout-a", @"C:\proj\alpha"), (e.Agent, e.Session, e.Folder)));
        Assert.Equal(TimeSpan.FromSeconds(124), _events[1].Duration);
    }

    [Fact]
    public void A_turn_already_running_at_launch_still_finishes()
    {
        Rollout("rollout-a", Meta() + Started(_now.AddMinutes(-3)));
        using var w = Sut();
        w.Scan();
        Rollout("rollout-a", Complete(_now));
        w.Scan();
        Assert.Equal(AgentTurnKind.Finished, Assert.Single(_events).Kind);
    }

    [Fact]
    public void A_rollout_created_after_launch_is_read_from_its_start()
    {
        using var w = Sut();
        w.Scan();
        Rollout("rollout-new", Meta() + Started(_now) + Complete(_now.AddSeconds(124)), newFile: true);
        w.Scan();
        Assert.Equal(2, _events.Count);
    }

    [Fact]
    public void An_old_rollout_first_seen_later_is_history()
    {
        using var w = Sut();
        w.Scan();
        var path = Rollout("rollout-old", Meta() + Started(_now) + Complete(_now.AddSeconds(124)));
        File.SetCreationTimeUtc(path, _now.UtcDateTime.AddDays(-2));
        w.Scan();
        Assert.Empty(_events);
    }

    [Fact]
    public void A_sub_agents_rollout_is_silent_even_past_the_priming_tail()
    {
        var filler = CodexLine(_now, "response_item", new() { ["type"] = "message", ["content"] = new string('x', 4000) });
        var sb = new StringBuilder(Meta(parent: "parent-thread"));
        while (sb.Length <= AgentActivityWatcher.PrimeTailBytes + 8000) sb.Append(filler);
        Rollout("rollout-child", sb.ToString() + Started(_now.AddMinutes(-1)));
        using var w = Sut();
        w.Scan();
        Rollout("rollout-child", Complete(_now));
        w.Scan();
        Assert.Empty(_events);
    }

    [Fact]
    public void A_line_written_in_two_halves_is_read_once_it_is_whole()
    {
        Rollout("rollout-a", Meta());
        using var w = Sut();
        w.Scan();
        var line = Started(_now);
        Rollout("rollout-a", line[..20]);
        w.Scan();
        Assert.Empty(_events);
        Rollout("rollout-a", line[20..]);
        w.Scan();
        Assert.Equal(AgentTurnKind.Started, Assert.Single(_events).Kind);
    }

    [Fact]
    public void A_line_over_a_megabyte_is_skipped_and_the_next_is_read()
    {
        Rollout("rollout-a", Meta());
        using var w = Sut();
        w.Scan();
        Rollout("rollout-a", CodexLine(_now, "response_item", new() { ["type"] = "message", ["content"] = new string('x', AgentActivityWatcher.MaxLineBytes + 10) }) + Started(_now));
        w.Scan();
        Assert.Equal(AgentTurnKind.Started, Assert.Single(_events).Kind);
    }

    /// <summary>Review focus: a rollout that shrinks or disappears mid-turn invents no ending.</summary>
    [Fact]
    public void A_rollout_that_shrinks_or_disappears_mid_turn_raises_nothing()
    {
        var path = Rollout("rollout-a", Meta() + Started(_now.AddMinutes(-1)));
        using var w = Sut();
        w.Scan();

        File.WriteAllText(path, Meta());            // rewritten shorter
        w.Scan();
        Assert.Empty(_events);

        Rollout("rollout-a", Started(_now) + Complete(_now.AddSeconds(124)));   // it carries on from its new end
        w.Scan();
        Assert.Equal(2, _events.Count);
        Assert.All(_events, e => Assert.Equal(@"C:\proj\alpha", e.Folder));

        _events.Clear();
        Rollout("rollout-a", Started(_now.AddMinutes(5)));
        w.Scan();
        File.Delete(path);                          // compressed away mid-turn
        w.Scan();
        Assert.Equal(AgentTurnKind.Started, Assert.Single(_events).Kind);
    }

    /// <summary>Review focus: a source switched off mid-turn; switching it back on replays nothing.</summary>
    [Fact]
    public void A_source_switched_off_goes_quiet_and_coming_back_replays_nothing()
    {
        Rollout("rollout-a", Meta());
        using var w = Sut();
        w.Scan();
        Rollout("rollout-a", Started(_now));
        w.Scan();
        Assert.Single(_events);

        _codexOn = false;
        w.Scan();
        w.Scan();
        Assert.Equal(["codex"], _off);              // said once
        Rollout("rollout-a", Complete(_now.AddSeconds(124)) + Started(_now.AddMinutes(4)));
        w.Scan();
        Assert.Single(_events);                     // nothing while off

        _codexOn = true;
        w.Scan();
        Assert.Single(_events);                     // the first pass back is history
        Rollout("rollout-a", Complete(_now.AddMinutes(6)));
        w.Scan();
        Assert.Equal(AgentTurnKind.Finished, _events[^1].Kind);
    }

    [Fact]
    public void A_sub_agents_rollout_stays_silent_after_it_is_rewritten_shorter()
    {
        var path = Rollout("rollout-child", Meta(parent: "parent-thread") + Started(_now.AddMinutes(-1)));
        using var w = Sut();
        w.Scan();
        File.WriteAllText(path, Meta(parent: "parent-thread"));
        w.Scan();
        Rollout("rollout-child", Started(_now) + Complete(_now.AddSeconds(124)));
        w.Scan();
        Assert.Empty(_events);
    }

    [Fact]
    public void A_history_file_that_is_locked_during_the_first_pass_is_not_replayed_when_it_opens()
    {
        var path = Rollout("rollout-a", Meta() + Started(_now.AddMinutes(-9)) + Complete(_now.AddMinutes(-7)));
        File.SetCreationTimeUtc(path, _now.UtcDateTime.AddDays(-1));
        using var w = Sut();
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            w.Scan();                               // the prime cannot open it
        w.Scan();                                   // opens now: still history
        Assert.Empty(_events);

        Rollout("rollout-a", Started(_now) + Complete(_now.AddSeconds(124)));
        w.Scan();
        Assert.Equal([AgentTurnKind.Started, AgentTurnKind.Finished], _events.Select(e => e.Kind));
    }

    [Fact]
    public void A_listener_that_throws_on_agent_off_does_not_stop_the_others()
    {
        Rollout("rollout-a", Meta());
        using var w = Sut();
        w.AgentOff += _ => throw new InvalidOperationException("boom");
        var heard = new List<string>();
        w.AgentOff += heard.Add;
        w.Scan();
        _codexOn = false;
        w.Scan();
        Assert.Equal(["codex"], heard);
    }

    [Fact]
    public void Two_scans_deliver_their_events_in_file_order()
    {
        using var w = Sut();
        w.Scan();
        var delivered = new List<AgentTurnKind>();
        var gate = new object();
        Task? second = null;
        bool secondFinishedEarly = false;
        int fired = 0;
        w.Turn += e =>
        {
            if (Interlocked.Exchange(ref fired, 1) == 0)
            {
                Rollout("rollout-new", Started(_now.AddMinutes(1)));   // a later event, for the second scan
                second = Task.Run(w.Scan);
                secondFinishedEarly = second.Wait(400);
            }
            lock (gate) delivered.Add(e.Kind);
        };
        Rollout("rollout-new", Meta() + Started(_now) + Complete(_now.AddSeconds(124)), newFile: true);
        w.Scan();
        second!.Wait();
        Assert.False(secondFinishedEarly);
        Assert.Equal([AgentTurnKind.Started, AgentTurnKind.Finished, AgentTurnKind.Started], delivered);
    }

    // ---- Antigravity ----

    [Fact]
    public void An_antigravity_turn_starts_and_finishes_ninety_seconds_after_its_last_step()
    {
        using var w = Sut();
        w.Scan();
        Transcript("conv-1", Step(_now, "USER_INPUT") + Step(_now.AddSeconds(5), "PLANNER_RESPONSE", toolCalls: true)
            + Step(_now.AddSeconds(200), "PLANNER_RESPONSE"), newFile: true);
        w.Scan();
        var started = Assert.Single(_events);
        Assert.Equal((AgentTurnKind.Started, "antigravity", "conv-1", @"C:\proj\conv-1"), (started.Kind, started.Agent, started.Session, started.Folder));

        _now = _now.AddSeconds(89);
        w.Scan();
        Assert.Single(_events);

        _now = _now.AddSeconds(1);
        w.Scan();
        Assert.Equal(AgentTurnKind.Finished, _events[^1].Kind);
        Assert.Equal(TimeSpan.FromSeconds(200), _events[^1].Duration);
        Assert.Equal(@"C:\proj\conv-1", _events[^1].Folder);
    }

    [Fact]
    public void An_antigravity_conversation_that_ended_before_launch_raises_nothing_later()
    {
        Transcript("conv-1", Step(_now.AddMinutes(-2), "USER_INPUT") + Step(_now.AddMinutes(-1), "PLANNER_RESPONSE"));
        using var w = Sut();
        w.Scan();
        _now = _now.AddMinutes(5);
        w.Scan();
        Assert.Empty(_events);
    }

    [Fact]
    public void With_antigravity_off_its_files_are_not_read()
    {
        _antigravityOn = false;
        using var w = Sut();
        w.Scan();
        Transcript("conv-1", Step(_now, "USER_INPUT"), newFile: true);
        w.Scan();
        Assert.Empty(_events);
        Assert.Empty(_off);                         // it was never on
    }

    // ---- Claude Code ----

    /// <summary>Review focus: two sessions end in the same millisecond.</summary>
    [Fact]
    public void Two_sessions_signals_in_the_same_millisecond_both_arrive_and_the_files_go()
    {
        using var w = Sut();
        long at = _now.ToUnixTimeMilliseconds();
        Signal("done", "sess-a", at, pid: 100);
        Signal("done", "sess-b", at, pid: 200);
        w.Scan();
        Assert.Equal(["sess-a", "sess-b"], _events.Select(e => e.Session).Order());
        Assert.All(_events, e => Assert.Equal(("claude", AgentTurnKind.Finished), (e.Agent, e.Kind)));
        Assert.Empty(Directory.GetFiles(Signals));
    }

    [Fact]
    public void A_prompt_then_a_done_gives_the_turns_length()
    {
        using var w = Sut();
        Signal("prompt", "sess-a", _now.ToUnixTimeMilliseconds());
        w.Scan();
        _now = _now.AddSeconds(252);
        Signal("done", "sess-a", _now.ToUnixTimeMilliseconds());
        w.Scan();
        Assert.Equal(TimeSpan.FromSeconds(252), _events[^1].Duration);
    }

    [Fact]
    public void A_stale_malformed_or_oversized_signal_is_deleted_unread()
    {
        using var w = Sut();
        Signal("done", "old", _now.AddMinutes(-11).ToUnixTimeMilliseconds());
        Signal("done", "bad", pid: 300, raw: "not json");
        Signal("done", "big", pid: 400, raw: new string('x', ClaudeCodeSignalFile.MaxBytes + 1));
        w.Scan();
        Assert.Empty(_events);
        Assert.Empty(Directory.GetFiles(Signals));
    }

    [Fact]
    public void With_claude_code_off_a_signal_is_deleted_and_raises_nothing()
    {
        using var w = Sut();
        w.Scan();
        _claudeOn = false;
        Signal("done", "sess-a");
        w.Scan();
        Assert.Empty(_events);
        Assert.Empty(Directory.GetFiles(Signals));
        Assert.Equal(["claude"], _off);
    }

    // ---- the service ----

    [Fact]
    public void A_listener_that_throws_does_not_stop_the_others()
    {
        using var w = new AgentActivityWatcher(() => [], () => null, _ => null, () => true, Signals, () => _now);
        w.Turn += _ => throw new InvalidOperationException("boom");
        w.Turn += _events.Add;
        Signal("done", "sess-a");
        w.Scan();
        Assert.Single(_events);
    }

    [Fact]
    public void Missing_folders_are_simply_empty()
    {
        using var w = new AgentActivityWatcher(() => [Path.Combine(_t.Path, "nope")], () => Path.Combine(_t.Path, "nope2"), _ => null,
            () => true, Path.Combine(_t.Path, "nope3"), () => _now);
        w.Scan();
        w.Start();
        w.Refresh();
        Assert.Empty(_events);
    }
}
