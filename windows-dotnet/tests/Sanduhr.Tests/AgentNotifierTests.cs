using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class AgentNotifierTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-17T20:00:00Z");

    private NotificationSettings _settings = NotificationSettings.Default with { GlowFinished = true, GlowWaiting = true, Flash = true };
    private bool _demo;
    private string? _foreground;
    private readonly List<string> _calls = [];

    private AgentNotifier Sut() => new(() => _settings, () => _demo, () => _foreground,
        folder => folder is null ? null : "masked:" + Path.GetFileName(folder), () => Now)
    {
        Toast = (title, body, tag) => _calls.Add($"toast|{title}|{body}|{tag}"),
        Glow = (e, flash) => _calls.Add($"glow|{e.Agent}|{e.Kind}|{flash}"),
        Flash = () => _calls.Add("flash"),
        Card = (e, title, shortTitle) => _calls.Add($"card|{title}|{shortTitle}|{e.Kind}"),
    };

    private static AgentTurnEvent E(AgentTurnKind kind, int? seconds = 252, string agent = "codex") =>
        new(agent, kind, "s1", @"C:\proj\alpha", Now, seconds is { } s ? TimeSpan.FromSeconds(s) : null);

    [Fact]
    public void A_long_finished_turn_reaches_every_surface_with_the_masked_project()
    {
        var e = E(AgentTurnKind.Finished);
        var surfaces = Sut().Handle(e);
        Assert.Equal(NotifySurfaces.Toast | NotifySurfaces.Glow | NotifySurfaces.Flash | NotifySurfaces.Card, surfaces);
        Assert.Equal(
        [
            "card|Codex · masked:alpha|Codex|Finished",
            $"toast|Codex finished|masked:alpha, 4m 12s|{AgentNotifier.Tag(e)}",
            "glow|codex|Finished|True",
        ], _calls);
    }

    [Fact]
    public void With_the_glow_off_the_flash_goes_out_alone()
    {
        _settings = _settings with { GlowFinished = false };
        Sut().Handle(E(AgentTurnKind.Finished));
        Assert.Contains("flash", _calls);
        Assert.DoesNotContain(_calls, c => c.StartsWith("glow"));
    }

    [Fact]
    public void With_the_flash_off_the_glow_does_not_flash()
    {
        _settings = _settings with { Flash = false };
        Sut().Handle(E(AgentTurnKind.Finished));
        Assert.Contains("glow|codex|Finished|False", _calls);
        Assert.DoesNotContain("flash", _calls);
    }

    [Fact]
    public void A_started_turn_only_puts_up_its_card()
    {
        Sut().Handle(E(AgentTurnKind.Started, null, "claude"));
        Assert.Equal(["card|Claude Code · masked:alpha|CC|Started"], _calls);
    }

    [Fact]
    public void Waiting_names_what_it_is_and_carries_no_duration()
    {
        Sut().Handle(E(AgentTurnKind.Waiting, 9, "claude"));
        Assert.Contains(_calls, c => c.StartsWith("toast|Claude Code is waiting on you|masked:alpha|"));
    }

    [Fact]
    public void The_agents_window_in_front_leaves_only_the_card()
    {
        _foreground = "codex";
        Sut().Handle(E(AgentTurnKind.Finished));
        Assert.Equal(["card|Codex · masked:alpha|Codex|Finished"], _calls);
    }

    [Fact]
    public void Another_agents_window_in_front_does_not_count()
    {
        _foreground = "codex";
        Sut().Handle(E(AgentTurnKind.Finished, agent: "claude"));
        Assert.Contains(_calls, c => c.StartsWith("toast|"));
    }

    [Fact]
    public void Demo_mode_does_nothing()
    {
        _demo = true;
        Assert.Equal(NotifySurfaces.None, Sut().Handle(E(AgentTurnKind.Finished)));
        Assert.Empty(_calls);
    }

    [Fact]
    public void An_agent_switched_off_does_nothing()
    {
        _settings = _settings.WithAgent("codex", false);
        Sut().Handle(E(AgentTurnKind.Finished));
        Assert.Empty(_calls);
    }

    [Fact]
    public void Settings_are_read_for_each_event()
    {
        var sut = Sut();
        _settings = _settings with { Toast = false };
        sut.Handle(E(AgentTurnKind.Finished));
        Assert.DoesNotContain(_calls, c => c.StartsWith("toast"));
    }

    [Fact]
    public void A_surface_that_throws_does_not_stop_the_others()
    {
        var sut = Sut();
        var logged = new List<string>();
        sut.Log = logged.Add;
        sut.Toast = (_, _, _) => throw new InvalidOperationException("boom");
        sut.Handle(E(AgentTurnKind.Finished));
        Assert.Contains(_calls, c => c.StartsWith("card"));
        Assert.Contains(_calls, c => c.StartsWith("glow"));
        Assert.Equal(["agent notifications: toast failed (InvalidOperationException)"], logged);
    }

    [Fact]
    public void A_missing_surface_is_skipped()
    {
        var sut = new AgentNotifier(() => _settings, () => false, () => null, _ => null, () => Now);
        Assert.Equal(NotifySurfaces.Toast | NotifySurfaces.Glow | NotifySurfaces.Flash | NotifySurfaces.Card, sut.Handle(E(AgentTurnKind.Finished)));
    }

    [Fact]
    public void The_tag_is_sixteen_hex_characters_and_one_per_session()
    {
        var a = AgentNotifier.Tag(E(AgentTurnKind.Finished));
        Assert.Matches("^[0-9A-F]{16}$", a);
        Assert.Equal(a, AgentNotifier.Tag(E(AgentTurnKind.Waiting)));
        Assert.NotEqual(a, AgentNotifier.Tag(E(AgentTurnKind.Finished, agent: "claude")));
        Assert.NotEqual(a, AgentNotifier.Tag(E(AgentTurnKind.Finished) with { Session = "s2" }));
    }
}
