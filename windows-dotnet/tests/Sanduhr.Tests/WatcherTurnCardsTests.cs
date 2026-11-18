using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class WatcherTurnCardsTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-17T20:00:00Z");

    private static AgentTurnEvent E(AgentTurnKind kind, int? seconds = null, string agent = "codex", string session = "s1", string? folder = @"C:\proj\alpha") =>
        new(agent, kind, session, folder, Now, seconds is { } s ? TimeSpan.FromSeconds(s) : null);

    private static Watcher Card(WatcherBoard b, string agent = "codex", string session = "s1") =>
        b.Get(WatcherBoard.TurnId(agent, session))!;

    [Fact]
    public void A_started_turn_is_a_running_card()
    {
        var b = new WatcherBoard();
        Assert.True(b.ApplyTurn(E(AgentTurnKind.Started), "Codex · alpha", "Codex", Now));
        var w = Card(b);
        Assert.Equal(WatcherSource.Turn, w.Source);
        Assert.Equal(WatcherState.Running, w.State);
        Assert.Equal("Codex · alpha", w.Title);
        Assert.Equal("Codex", w.Short);
        Assert.Equal("codex", w.Kind);
        Assert.Equal("s1", w.Session);
        Assert.Equal(@"C:\proj\alpha", w.Folder);
        Assert.Equal(Now, w.Started);
    }

    [Fact]
    public void A_finished_turn_shows_its_length_and_fades_after_a_minute()
    {
        var b = new WatcherBoard();
        b.ApplyTurn(E(AgentTurnKind.Started), "Codex · alpha", "Codex", Now);
        var end = Now.AddSeconds(300);
        b.ApplyTurn(E(AgentTurnKind.Finished, 252), "Codex · alpha", "Codex", end);

        var w = Card(b);
        Assert.Equal(WatcherState.Finished, w.State);
        Assert.Equal(TimeSpan.FromSeconds(252), w.Elapsed(end.AddSeconds(30)));   // the turn's length, not the wall clock
        Assert.Equal(end + WatcherBoard.TurnFadeAfter, b.NextChange());

        Assert.False(b.Tick(end.AddSeconds(59)));
        Assert.True(b.Tick(end.AddSeconds(60)));
        Assert.Empty(b.Watchers);
    }

    [Fact]
    public void A_finished_turn_with_no_card_still_shows()
    {
        var b = new WatcherBoard();
        Assert.True(b.ApplyTurn(E(AgentTurnKind.Finished, 252), "Codex · alpha", "Codex", Now));
        Assert.Equal(WatcherState.Finished, Card(b).State);
    }

    [Fact]
    public void A_stopped_turn_ends_as_finished_and_says_stopped()
    {
        var b = new WatcherBoard();
        b.ApplyTurn(E(AgentTurnKind.Started), "Codex · alpha", "Codex", Now);
        b.ApplyTurn(E(AgentTurnKind.Stopped, 30), "Codex · alpha", "Codex", Now.AddSeconds(30));
        var w = Card(b);
        Assert.Equal(WatcherState.Finished, w.State);
        Assert.Equal("stopped", WatcherText.DeskNote(w));
    }

    [Fact]
    public void Waiting_marks_the_card_and_a_new_prompt_sets_it_running_again()
    {
        var b = new WatcherBoard();
        b.ApplyTurn(E(AgentTurnKind.Started, agent: "claude"), "Claude Code · alpha", "CC", Now);
        b.ApplyTurn(E(AgentTurnKind.Waiting, agent: "claude"), "Claude Code · alpha", "CC", Now.AddSeconds(20));
        Assert.Equal(WatcherState.Waiting, Card(b, "claude").State);
        Assert.Equal(Now, Card(b, "claude").Started);

        b.ApplyTurn(E(AgentTurnKind.Started, agent: "claude"), "Claude Code · alpha", "CC", Now.AddSeconds(40));
        Assert.Equal(WatcherState.Running, Card(b, "claude").State);
        Assert.Equal(Now.AddSeconds(40), Card(b, "claude").Started);
    }

    [Fact]
    public void Waiting_with_no_card_puts_one_up()
    {
        var b = new WatcherBoard();
        b.ApplyTurn(E(AgentTurnKind.Waiting, agent: "claude"), "Claude Code · alpha", "CC", Now);
        Assert.Equal(WatcherState.Waiting, Card(b, "claude").State);
    }

    [Fact]
    public void Two_sessions_and_two_agents_keep_their_own_cards()
    {
        var b = new WatcherBoard();
        b.ApplyTurn(E(AgentTurnKind.Started, session: "a"), "Codex · alpha", "Codex", Now);
        b.ApplyTurn(E(AgentTurnKind.Started, session: "b"), "Codex · beta", "Codex", Now);
        b.ApplyTurn(E(AgentTurnKind.Started, agent: "claude", session: "a"), "Claude Code · alpha", "CC", Now);
        Assert.Equal(3, b.Watchers.Count);
    }

    [Fact]
    public void A_running_turn_loses_touch_after_an_hour_and_antigravitys_after_ten_minutes()
    {
        var b = new WatcherBoard();
        b.ApplyTurn(E(AgentTurnKind.Started), "Codex · alpha", "Codex", Now);
        b.ApplyTurn(E(AgentTurnKind.Started, agent: "antigravity"), "Antigravity · alpha", "Agy", Now);

        b.Tick(Now.AddMinutes(11));
        Assert.Equal(WatcherState.Running, Card(b).State);
        Assert.Equal(WatcherState.LostTouch, Card(b, "antigravity").State);

        b.Tick(Now.AddMinutes(61));
        Assert.Equal(WatcherState.LostTouch, Card(b).State);
    }

    [Fact]
    public void A_card_that_lost_touch_still_finishes()
    {
        var b = new WatcherBoard();
        b.ApplyTurn(E(AgentTurnKind.Started), "Codex · alpha", "Codex", Now);
        b.Tick(Now.AddMinutes(61));
        b.ApplyTurn(E(AgentTurnKind.Finished, 4000), "Codex · alpha", "Codex", Now.AddMinutes(67));
        Assert.Equal(WatcherState.Finished, Card(b).State);
    }

    /// <summary>Review focus: streamer mode switched on while a card is up.</summary>
    [Fact]
    public void Retitling_renames_turn_cards_only()
    {
        var b = new WatcherBoard();
        b.ApplyTurn(E(AgentTurnKind.Started), "Codex · alpha", "Codex", Now);
        b.Apply(new WatcherCommand.Start("x", "PR 140", null, null, false), Now);

        Assert.True(b.RetitleTurns(w => ($"Codex · codename-for-{w.Folder}", "Codex")));
        Assert.Equal(@"Codex · codename-for-C:\proj\alpha", Card(b).Title);
        Assert.Equal("PR 140", b.Get(WatcherBoard.AgentId("x"))!.Title);
        Assert.False(b.RetitleTurns(w => (w.Title, w.Short)));
    }

    [Fact]
    public void Clearing_an_agents_turns_leaves_the_other_cards()
    {
        var b = new WatcherBoard();
        b.ApplyTurn(E(AgentTurnKind.Started), "Codex · alpha", "Codex", Now);
        b.ApplyTurn(E(AgentTurnKind.Started, agent: "claude"), "Claude Code · alpha", "CC", Now);
        b.Apply(new WatcherCommand.Start("x", "PR 140", null, null, false), Now);

        Assert.True(b.ClearTurns("codex"));
        Assert.Equal(2, b.Watchers.Count);
        Assert.Null(b.Get(WatcherBoard.TurnId("codex", "s1")));
        Assert.False(b.ClearTurns("codex"));
    }

    [Fact]
    public void Demo_mode_hides_turn_cards()
    {
        var b = new WatcherBoard();
        b.ApplyTurn(E(AgentTurnKind.Started), "Codex · alpha", "Codex", Now);
        Assert.Empty(WatcherBoard.Shown(b.Ordered(), demo: true));
        Assert.Single(WatcherBoard.Shown(b.Ordered(), demo: false));
    }

    [Fact]
    public void The_other_sources_keep_their_windows_and_fade()
    {
        var b = new WatcherBoard();
        b.Apply(new WatcherCommand.Start("x", "PR 140", null, null, false), Now);
        b.Apply(new WatcherCommand.End("x", WatcherState.Passed, null), Now.AddSeconds(10));
        Assert.Equal(Now.AddSeconds(10) + WatcherBoard.FadeAfter, b.NextChange());
        Assert.Equal(WatcherBoard.AgentWindow, WatcherBoard.WindowOf(new Watcher
        {
            Id = "a:y", Source = WatcherSource.Agent, Title = "t", State = WatcherState.Running, Started = Now, Touched = Now,
        }));
    }
}
