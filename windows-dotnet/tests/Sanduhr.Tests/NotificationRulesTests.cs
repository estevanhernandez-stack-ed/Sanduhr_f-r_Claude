using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class NotificationRulesTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-17T20:00:00Z");
    private static readonly NotificationSettings AllOn = NotificationSettings.Default with { GlowFinished = true, GlowWaiting = true, Flash = true };
    private const NotifySurfaces Everything = NotifySurfaces.Toast | NotifySurfaces.Glow | NotifySurfaces.Flash | NotifySurfaces.Card;

    private static AgentTurnEvent E(AgentTurnKind kind, int? seconds = null, string agent = "codex", string session = "s1") =>
        new(agent, kind, session, null, Now, seconds is { } s ? TimeSpan.FromSeconds(s) : null);

    private static NotifySurfaces Decide(AgentTurnEvent e, NotificationSettings? s = null, bool inFront = false, NotificationLimiter? limiter = null) =>
        NotificationRules.Decide(e, s ?? AllOn, inFront, limiter ?? new NotificationLimiter(), Now);

    // ---- section 4's table ----

    [Fact]
    public void Started_never_interrupts_and_shows_a_card() =>
        Assert.Equal(NotifySurfaces.Card, Decide(E(AgentTurnKind.Started)));

    [Theory]
    [InlineData(AgentTurnKind.Finished)]
    [InlineData(AgentTurnKind.Stopped)]
    public void A_long_turn_ending_out_of_sight_uses_every_surface(AgentTurnKind kind) =>
        Assert.Equal(Everything, Decide(E(kind, 60)));

    [Theory]
    [InlineData(AgentTurnKind.Finished)]
    [InlineData(AgentTurnKind.Stopped)]
    public void A_short_turn_only_updates_the_card(AgentTurnKind kind) =>
        Assert.Equal(NotifySurfaces.Card, Decide(E(kind, 59)));

    [Fact]
    public void A_long_turn_with_the_agents_window_in_front_only_updates_the_card() =>
        Assert.Equal(NotifySurfaces.Card, Decide(E(AgentTurnKind.Finished, 600), inFront: true));

    [Fact]
    public void In_front_does_not_matter_once_the_switch_is_off() =>
        Assert.Equal(Everything, Decide(E(AgentTurnKind.Finished, 600), AllOn with { SkipInFront = false }, inFront: true));

    [Fact]
    public void Waiting_interrupts_at_once_whatever_the_threshold() =>
        Assert.Equal(Everything, Decide(E(AgentTurnKind.Waiting), AllOn with { ThresholdSeconds = 300 }));

    [Fact]
    public void Waiting_with_the_agents_window_in_front_only_updates_the_card() =>
        Assert.Equal(NotifySurfaces.Card, Decide(E(AgentTurnKind.Waiting), inFront: true));

    [Fact]
    public void An_unknown_duration_never_interrupts() =>
        Assert.Equal(NotifySurfaces.Card, Decide(E(AgentTurnKind.Finished)));

    [Fact]
    public void Every_turn_interrupts_even_with_an_unknown_duration()
    {
        Assert.Equal(Everything, Decide(E(AgentTurnKind.Finished), AllOn with { ThresholdSeconds = 0 }));
        Assert.Equal(Everything, Decide(E(AgentTurnKind.Finished, 1, session: "s2"), AllOn with { ThresholdSeconds = 0 }));
    }

    // ---- the switches ----

    [Fact]
    public void An_agent_switched_off_does_nothing_at_all() =>
        Assert.Equal(NotifySurfaces.None, Decide(E(AgentTurnKind.Finished, 600), AllOn.WithAgent("codex", false)));

    [Fact]
    public void An_agent_not_named_in_the_settings_is_on() =>
        Assert.Equal(Everything, Decide(E(AgentTurnKind.Finished, 600, agent: "antigravity")));

    [Fact]
    public void The_defaults_are_a_toast_and_a_card()
    {
        Assert.Equal(NotifySurfaces.Toast | NotifySurfaces.Card, Decide(E(AgentTurnKind.Finished, 600), NotificationSettings.Default));
        Assert.Equal(NotifySurfaces.Toast | NotifySurfaces.Card, Decide(E(AgentTurnKind.Waiting, session: "s2"), NotificationSettings.Default));
    }

    [Fact]
    public void Each_glow_switch_covers_its_own_kind()
    {
        var onlyFinished = AllOn with { GlowWaiting = false };
        Assert.True(Decide(E(AgentTurnKind.Finished, 600), onlyFinished).HasFlag(NotifySurfaces.Glow));
        Assert.False(Decide(E(AgentTurnKind.Waiting, session: "s2"), onlyFinished).HasFlag(NotifySurfaces.Glow));
    }

    [Fact]
    public void With_the_card_off_and_nothing_to_interrupt_nothing_happens() =>
        Assert.Equal(NotifySurfaces.None, Decide(E(AgentTurnKind.Finished, 600),
            AllOn with { DeskCard = false, Toast = false, GlowFinished = false, Flash = false }));

    // ---- the limiter ----

    [Fact]
    public void The_same_kind_from_one_session_waits_twenty_seconds()
    {
        var limiter = new NotificationLimiter();
        var e = E(AgentTurnKind.Finished, 600);
        Assert.Equal(Everything, NotificationRules.Decide(e, AllOn, false, limiter, Now));
        Assert.Equal(NotifySurfaces.Card, NotificationRules.Decide(e, AllOn, false, limiter, Now.AddSeconds(19)));
        Assert.Equal(Everything, NotificationRules.Decide(e, AllOn, false, limiter, Now.AddSeconds(20)));
    }

    [Fact]
    public void A_finished_within_five_seconds_of_a_waiting_only_updates_the_card()
    {
        var limiter = new NotificationLimiter();
        Assert.Equal(Everything, NotificationRules.Decide(E(AgentTurnKind.Waiting), AllOn, false, limiter, Now));
        Assert.Equal(NotifySurfaces.Card, NotificationRules.Decide(E(AgentTurnKind.Finished, 600), AllOn, false, limiter, Now.AddSeconds(4)));
        Assert.Equal(Everything, NotificationRules.Decide(E(AgentTurnKind.Finished, 600), AllOn, false, limiter, Now.AddSeconds(5)));
    }

    /// <summary>Review focus: many runs ending together each get their own say; one session never stacks.</summary>
    [Fact]
    public void Each_session_and_each_agent_has_its_own_limit()
    {
        var limiter = new NotificationLimiter();
        Assert.Equal(Everything, NotificationRules.Decide(E(AgentTurnKind.Finished, 600, session: "a"), AllOn, false, limiter, Now));
        Assert.Equal(Everything, NotificationRules.Decide(E(AgentTurnKind.Finished, 600, session: "b"), AllOn, false, limiter, Now));
        Assert.Equal(Everything, NotificationRules.Decide(E(AgentTurnKind.Finished, 600, agent: "claude", session: "a"), AllOn, false, limiter, Now));
        Assert.Equal(NotifySurfaces.Card, NotificationRules.Decide(E(AgentTurnKind.Finished, 600, session: "a"), AllOn, false, limiter, Now.AddSeconds(1)));
    }

    [Fact]
    public void An_event_that_interrupts_nothing_does_not_use_up_the_limit()
    {
        var limiter = new NotificationLimiter();
        NotificationRules.Decide(E(AgentTurnKind.Finished, 600), AllOn, agentInFront: true, limiter, Now);
        Assert.Equal(Everything, NotificationRules.Decide(E(AgentTurnKind.Finished, 600), AllOn, false, limiter, Now.AddSeconds(1)));
    }

    // ---- in front ----

    [Theory]
    [InlineData("claude", "WindowsTerminal", true)]
    [InlineData("claude", "Code.exe", true)]
    [InlineData("claude", "codex", false)]
    [InlineData("codex", "codex", true)]
    [InlineData("codex", "Codex.exe", true)]
    [InlineData("codex", "pwsh", true)]
    [InlineData("antigravity", "wezterm-gui", true)]
    [InlineData("antigravity", "codex", false)]
    [InlineData("codex", "chrome", false)]
    [InlineData("codex", null, false)]
    [InlineData("codex", " ", false)]
    public void In_front_is_the_agents_own_list(string agent, string? process, bool expected) =>
        Assert.Equal(expected, NotificationRules.InFront(agent, process));

    // ---- the settings ----

    [Fact]
    public void The_defaults_are_the_specs()
    {
        var d = NotificationSettings.Default;
        Assert.True(d.Toast);
        Assert.True(d.DeskCard);
        Assert.False(d.GlowFinished);
        Assert.False(d.GlowWaiting);
        Assert.False(d.Flash);
        Assert.Equal(60, d.ThresholdSeconds);
        Assert.True(d.SkipInFront);
        Assert.Equal([0, 30, 60, 120, 300], NotificationSettings.Thresholds);
    }

    [Fact]
    public void With_nothing_saved_the_old_glow_switches_carry_over()
    {
        var s = NotificationSettings.Decode(null, new GlowSwitches(Waiting: true, Done: false, SkipTerminal: false));
        Assert.True(s.GlowWaiting);
        Assert.False(s.GlowFinished);
        Assert.True(s.Flash);            // on when either glow switch was on
        Assert.False(s.SkipInFront);
        Assert.True(s.Toast);
        Assert.True(s.DeskCard);
        Assert.Equal(60, s.ThresholdSeconds);

        Assert.False(NotificationSettings.Decode(null, new GlowSwitches()).Flash);
    }

    [Fact]
    public void Saved_settings_win_over_the_old_switches_and_round_trip()
    {
        var saved = (NotificationSettings.Default with { Toast = false, GlowFinished = true, ThresholdSeconds = 120 }).WithAgent("codex", false);
        var back = NotificationSettings.Decode(JsonNode.Parse(saved.Encode().ToJsonString()), new GlowSwitches(Waiting: true, Done: true));
        Assert.False(back.Toast);
        Assert.True(back.GlowFinished);
        Assert.False(back.GlowWaiting);
        Assert.Equal(120, back.ThresholdSeconds);
        Assert.False(back.AgentOn("codex"));
        Assert.True(back.AgentOn("claude"));
    }

    [Theory]
    [InlineData("{\"threshold_seconds\":45}", 60)]
    [InlineData("{\"threshold_seconds\":\"120\"}", 60)]
    [InlineData("{\"threshold_seconds\":0}", 0)]
    [InlineData("{\"threshold_seconds\":300}", 300)]
    public void A_threshold_that_is_not_a_choice_reads_as_the_default(string json, int expected) =>
        Assert.Equal(expected, NotificationSettings.Decode(JsonNode.Parse(json), new GlowSwitches()).ThresholdSeconds);

    [Fact]
    public void Unknown_agents_and_wrong_types_are_ignored()
    {
        var s = NotificationSettings.Decode(JsonNode.Parse("{\"agents\":{\"codex\":false,\"nope\":false,\"claude\":\"no\"},\"toast\":\"yes\"}"), new GlowSwitches());
        Assert.False(s.AgentOn("codex"));
        Assert.True(s.AgentOn("claude"));
        Assert.DoesNotContain("nope", s.Agents.Keys);
        Assert.True(s.Toast);
    }
}
