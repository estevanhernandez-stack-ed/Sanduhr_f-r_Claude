using System.Text.Json;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class CodexTurnReaderTests
{
    private const string T0 = "2026-11-17T20:00:00.000Z";
    private const string T2m = "2026-11-17T20:02:04.000Z";

    private static string Line(string timestamp, string type, Dictionary<string, object?> payload) =>
        JsonSerializer.Serialize(new Dictionary<string, object?> { ["timestamp"] = timestamp, ["type"] = type, ["payload"] = payload });

    private static string Meta(string? parent = null, string? forkedFrom = null, string cwd = @"C:\proj\alpha") =>
        Line(T0, "session_meta", new() { ["id"] = "s", ["cwd"] = cwd, ["parent_thread_id"] = parent, ["forked_from_id"] = forkedFrom });

    private static string Context(string cwd) => Line(T0, "turn_context", new() { ["cwd"] = cwd, ["model"] = "m" });

    private static string Event(string timestamp, string kind, Dictionary<string, object?>? more = null)
    {
        var p = new Dictionary<string, object?> { ["type"] = kind, ["turn_id"] = "t1" };
        foreach (var kv in more ?? []) p[kv.Key] = kv.Value;
        return Line(timestamp, "event_msg", p);
    }

    [Fact]
    public void A_turn_starts_and_finishes_with_the_logged_duration()
    {
        var r = new CodexTurnReader("rollout-a");
        Assert.Null(r.Read(Meta()));
        var started = r.Read(Event(T0, "task_started"));
        Assert.Equal(new AgentTurnEvent("codex", AgentTurnKind.Started, "rollout-a", @"C:\proj\alpha", DateTimeOffset.Parse(T0)), started);
        Assert.True(r.TurnOpen);

        var done = r.Read(Event(T2m, "task_complete", new() { ["duration_ms"] = 124000 }));
        Assert.Equal(AgentTurnKind.Finished, done!.Kind);
        Assert.Equal(TimeSpan.FromSeconds(124), done.Duration);
        Assert.False(r.TurnOpen);
    }

    [Fact]
    public void Without_a_logged_duration_it_is_the_time_between_the_markers()
    {
        var r = new CodexTurnReader("rollout-a");
        r.Read(Meta());
        r.Read(Event(T0, "task_started"));
        Assert.Equal(TimeSpan.FromSeconds(124), r.Read(Event(T2m, "task_complete"))!.Duration);
    }

    [Fact]
    public void A_completion_stamped_before_its_start_has_no_duration()
    {
        var r = new CodexTurnReader("rollout-a");
        r.Read(Meta());
        r.Read(Event(T2m, "task_started"));
        var done = r.Read(Event(T0, "task_complete"));
        Assert.Equal(AgentTurnKind.Finished, done!.Kind);
        Assert.Null(done.Duration);
    }

    [Fact]
    public void An_aborted_turn_is_stopped()
    {
        var r = new CodexTurnReader("rollout-a");
        r.Read(Meta());
        r.Read(Event(T0, "task_started"));
        var stopped = r.Read(Event(T2m, "turn_aborted"));
        Assert.Equal(AgentTurnKind.Stopped, stopped!.Kind);
        Assert.Equal(TimeSpan.FromSeconds(124), stopped.Duration);
    }

    [Fact]
    public void The_project_is_the_turn_contexts_folder()
    {
        var r = new CodexTurnReader("rollout-a");
        r.Read(Meta(cwd: @"C:\proj\alpha"));
        r.Read(Context(@"C:\proj\beta"));
        Assert.Equal(@"C:\proj\beta", r.Read(Event(T0, "task_started"))!.Folder);
    }

    [Theory]
    [InlineData("parent-thread", null)]
    [InlineData(null, "forked-from")]
    public void A_sub_agent_rollout_raises_nothing(string? parent, string? forkedFrom)
    {
        var r = new CodexTurnReader("rollout-child");
        r.Read(Meta(parent, forkedFrom));
        Assert.True(r.IsChild);
        Assert.Null(r.Read(Event(T0, "task_started")));
        Assert.Null(r.Read(Event(T2m, "task_complete", new() { ["duration_ms"] = 124000 })));
    }

    /// <summary>A forked rollout's second session_meta is its parent's copy, without the keys (seen 2026-11-17).</summary>
    [Fact]
    public void The_child_mark_sticks_after_a_later_meta_without_it()
    {
        var r = new CodexTurnReader("rollout-child");
        r.Read(Meta(parent: "parent-thread"));
        r.Read(Meta());
        Assert.True(r.IsChild);
        Assert.Null(r.Read(Event(T0, "task_started")));
    }

    [Fact]
    public void The_agents_last_message_never_reaches_an_event()
    {
        var r = new CodexTurnReader("rollout-a");
        r.Read(Meta());
        r.Read(Event(T0, "task_started"));
        var done = r.Read(Event(T2m, "task_complete", new() { ["duration_ms"] = 1000, ["last_agent_message"] = "SECRET-REPLY" }));
        Assert.DoesNotContain("SECRET-REPLY", done!.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"type\":\"event_msg\"}")]
    [InlineData("{\"timestamp\":\"nope\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\"}}")]
    [InlineData("{\"type\":\"event_msg\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\"}}")]
    [InlineData("{\"timestamp\":\"2026-11-17T20:00:00Z\",\"type\":\"response_item\",\"payload\":{\"type\":\"message\",\"content\":\"task_complete\"}}")]
    public void A_line_it_cannot_use_is_skipped(string line) =>
        Assert.Null(new CodexTurnReader("rollout-a").Read(line));

    [Fact]
    public void A_logged_duration_too_large_for_a_time_span_falls_back_to_the_markers()
    {
        var r = new CodexTurnReader("rollout-a");
        r.Read(Meta());
        r.Read(Event(T0, "task_started"));
        var done = r.Read(Event(T2m, "task_complete", new() { ["duration_ms"] = 1e300 }));
        Assert.Equal(AgentTurnKind.Finished, done!.Kind);
        Assert.Equal(TimeSpan.FromSeconds(124), done.Duration);
        Assert.False(r.TurnOpen);
    }
}
