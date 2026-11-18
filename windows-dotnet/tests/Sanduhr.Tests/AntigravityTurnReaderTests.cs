using System.Text.Json;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class AntigravityTurnReaderTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-11-17T20:00:00Z");

    private static string Step(DateTimeOffset createdAt, string type, string status = "DONE", bool toolCalls = false, string? content = null)
    {
        var o = new Dictionary<string, object?>
        {
            ["type"] = type, ["status"] = status, ["created_at"] = createdAt.ToString("o"), ["step_index"] = 1, ["source"] = "MODEL",
        };
        if (toolCalls) o["tool_calls"] = new[] { new { name = "SECRET-TOOL" } };
        if (content is not null) o["content"] = content;
        return JsonSerializer.Serialize(o);
    }

    [Fact]
    public void A_user_input_starts_a_turn()
    {
        var r = new AntigravityTurnReader("conv-1");
        var e = r.Read(Step(T0, "USER_INPUT"), @"C:\proj\alpha", T0);
        Assert.Equal(new AgentTurnEvent("antigravity", AgentTurnKind.Started, "conv-1", @"C:\proj\alpha", T0), e);
        Assert.True(r.TurnOpen);
    }

    [Fact]
    public void A_final_looking_step_finishes_after_ninety_quiet_seconds()
    {
        var r = new AntigravityTurnReader("conv-1");
        r.Read(Step(T0, "USER_INPUT"), null, T0);
        var end = T0.AddSeconds(200);
        Assert.Null(r.Read(Step(end, "PLANNER_RESPONSE"), null, end));
        Assert.Equal(end + AntigravityTurnReader.Quiet, r.DueAt);
        Assert.Null(r.Due(end.AddSeconds(89), null));

        var done = r.Due(end.AddSeconds(90), @"C:\proj\alpha");
        Assert.Equal(AgentTurnKind.Finished, done!.Kind);
        Assert.Equal(end, done.At);
        Assert.Equal(TimeSpan.FromSeconds(200), done.Duration);
        Assert.Equal(@"C:\proj\alpha", done.Folder);
        Assert.False(r.TurnOpen);
        Assert.Null(r.DueAt);
        Assert.Null(r.Due(end.AddSeconds(500), null));
    }

    [Fact]
    public void A_step_that_asks_for_a_tool_is_not_an_ending()
    {
        var r = new AntigravityTurnReader("conv-1");
        r.Read(Step(T0, "USER_INPUT"), null, T0);
        r.Read(Step(T0.AddSeconds(5), "PLANNER_RESPONSE", toolCalls: true), null, T0.AddSeconds(5));
        Assert.Null(r.DueAt);
        Assert.Null(r.Due(T0.AddSeconds(900), null));
    }

    /// <summary>19 of 341 mid-turn steps looked final and were followed by a SYSTEM_MESSAGE 5 to 83 s later (2026-11-17).</summary>
    [Theory]
    [InlineData(5)]
    [InlineData(83)]
    public void A_look_alike_ending_followed_by_more_work_raises_nothing(int gapSeconds)
    {
        var r = new AntigravityTurnReader("conv-1");
        r.Read(Step(T0, "USER_INPUT"), null, T0);
        r.Read(Step(T0.AddSeconds(30), "PLANNER_RESPONSE"), null, T0.AddSeconds(30));
        var next = T0.AddSeconds(30 + gapSeconds);
        Assert.Null(r.Read(Step(next, "SYSTEM_MESSAGE"), null, next));
        Assert.Null(r.DueAt);
        Assert.Null(r.Due(next.AddSeconds(900), null));
        Assert.True(r.TurnOpen);
    }

    [Fact]
    public void A_line_that_was_not_read_still_cancels_a_pending_end()
    {
        var r = new AntigravityTurnReader("conv-1");
        r.Read(Step(T0, "USER_INPUT"), null, T0);
        r.Read(Step(T0.AddSeconds(30), "PLANNER_RESPONSE"), null, T0.AddSeconds(30));
        r.Activity();
        Assert.Null(r.DueAt);
    }

    [Fact]
    public void A_running_or_failed_planner_step_is_not_an_ending()
    {
        var r = new AntigravityTurnReader("conv-1");
        r.Read(Step(T0, "USER_INPUT"), null, T0);
        r.Read(Step(T0.AddSeconds(5), "PLANNER_RESPONSE", status: "RUNNING"), null, T0.AddSeconds(5));
        Assert.Null(r.DueAt);
        r.Read(Step(T0.AddSeconds(6), "GENERIC", status: "ERROR"), null, T0.AddSeconds(6));
        Assert.Null(r.DueAt);
    }

    [Fact]
    public void An_ending_stamped_before_its_start_has_no_duration()
    {
        var r = new AntigravityTurnReader("conv-1");
        r.Read(Step(T0.AddSeconds(50), "USER_INPUT"), null, T0);
        r.Read(Step(T0, "PLANNER_RESPONSE"), null, T0.AddSeconds(60));
        Assert.Null(r.Due(T0.AddSeconds(200), null)!.Duration);
    }

    [Fact]
    public void An_ending_with_no_start_seen_still_finishes_with_no_duration()
    {
        var r = new AntigravityTurnReader("conv-1");
        r.Read(Step(T0, "PLANNER_RESPONSE"), null, T0);
        var done = r.Due(T0.AddSeconds(90), null);
        Assert.Equal(AgentTurnKind.Finished, done!.Kind);
        Assert.Null(done.Duration);
    }

    [Fact]
    public void Settle_drops_a_pending_end_and_closes_the_turn_silently()
    {
        var r = new AntigravityTurnReader("conv-1");
        r.Read(Step(T0, "USER_INPUT"), null, T0);
        r.Read(Step(T0.AddSeconds(30), "PLANNER_RESPONSE"), null, T0.AddSeconds(30));
        r.Settle();
        Assert.Null(r.DueAt);
        Assert.False(r.TurnOpen);
        Assert.Null(r.Due(T0.AddSeconds(900), null));
    }

    [Fact]
    public void Settle_leaves_a_turn_that_is_still_working_open()
    {
        var r = new AntigravityTurnReader("conv-1");
        r.Read(Step(T0, "USER_INPUT"), null, T0);
        r.Read(Step(T0.AddSeconds(5), "PLANNER_RESPONSE", toolCalls: true), null, T0.AddSeconds(5));
        r.Settle();
        Assert.True(r.TurnOpen);
    }

    [Fact]
    public void Step_text_and_tool_names_never_reach_an_event()
    {
        var r = new AntigravityTurnReader("conv-1");
        var started = r.Read(Step(T0, "USER_INPUT", content: "SECRET-PROMPT"), null, T0);
        r.Read(Step(T0.AddSeconds(5), "PLANNER_RESPONSE", toolCalls: true, content: "SECRET-REPLY"), null, T0.AddSeconds(5));
        r.Read(Step(T0.AddSeconds(9), "PLANNER_RESPONSE", content: "SECRET-REPLY"), null, T0.AddSeconds(9));
        var done = r.Due(T0.AddSeconds(200), null);
        Assert.DoesNotContain("SECRET", started!.ToString());
        Assert.DoesNotContain("SECRET", done!.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("{\"type\":\"USER_INPUT\",\"type\":\"USER_INPUT\"}")]
    [InlineData("{\"type\":\"USER_INPUT\",\"created_at\":\"nope\"}")]
    public void A_line_it_cannot_use_is_skipped(string line) =>
        Assert.Null(new AntigravityTurnReader("conv-1").Read(line, null, T0));
}
