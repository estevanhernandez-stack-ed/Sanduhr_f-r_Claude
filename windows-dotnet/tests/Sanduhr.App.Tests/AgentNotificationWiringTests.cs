using System.IO;
using Sanduhr.App.Services;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.App.Tests;

/// <summary>5.0 spec 4: the notification settings in settings.json, and turn cards through the watcher store.</summary>
[Collection("SettingsWindow")]
public sealed class AgentNotificationWiringTests : AppWindowFixture
{
    private SettingsStore StoreOver(string settingsJson)
    {
        var paths = ScratchPaths();
        Directory.CreateDirectory(paths.AppDataDir);
        File.WriteAllText(paths.SettingsFile, settingsJson);
        return new SettingsStore(paths);
    }

    [Fact]
    public void With_nothing_saved_the_defaults_apply()
    {
        var s = StoreOver("{}").LoadAgentNotifications();
        Assert.True(s.Toast);
        Assert.True(s.DeskCard);
        Assert.False(s.GlowFinished);
        Assert.False(s.GlowWaiting);
        Assert.False(s.Flash);
        Assert.Equal(60, s.ThresholdSeconds);
        Assert.True(s.SkipInFront);
    }

    [Fact]
    public void The_old_glow_switches_carry_over_until_the_new_settings_are_saved()
    {
        var store = StoreOver("{\"claude_code_glow\":{\"waiting\":true,\"done\":false,\"skip_terminal\":false}}");
        var s = store.LoadAgentNotifications();
        Assert.True(s.GlowWaiting);
        Assert.False(s.GlowFinished);
        Assert.True(s.Flash);
        Assert.False(s.SkipInFront);

        var glow = store.LoadClaudeCodeGlow();
        Assert.Equal((true, false, false), (glow.Waiting, glow.Done, glow.SkipTerminal));

        store.SaveAgentNotifications(s with { GlowWaiting = false, Flash = false });
        Assert.False(store.LoadAgentNotifications().GlowWaiting);          // the saved settings win over the old switches
        Assert.False(store.LoadClaudeCodeGlow().Waiting);
    }

    [Fact]
    public void Saving_round_trips_and_leaves_where_the_glow_shows_alone()
    {
        var store = StoreOver("{}");
        var target = store.LoadGlowTarget();
        store.SaveGlowTarget(target);
        store.SaveAgentNotifications((NotificationSettings.Default with { Toast = false, ThresholdSeconds = 300 }).WithAgent("codex", false));

        var back = store.LoadAgentNotifications();
        Assert.False(back.Toast);
        Assert.Equal(300, back.ThresholdSeconds);
        Assert.False(back.AgentOn("codex"));
        Assert.True(back.AgentOn("antigravity"));
        Assert.Equal(target, store.LoadGlowTarget());
    }

    [Fact]
    public void Saving_the_glow_switches_keeps_the_other_notification_settings()
    {
        var store = StoreOver("{}");
        store.SaveAgentNotifications(NotificationSettings.Default with { Toast = false });
        store.SaveClaudeCodeGlow(new GlowSwitches(Waiting: true, Done: true, SkipTerminal: false));

        var s = store.LoadAgentNotifications();
        Assert.False(s.Toast);
        Assert.True(s.GlowWaiting);
        Assert.True(s.GlowFinished);
        Assert.False(s.SkipInFront);
    }

    [Fact]
    public void A_missing_settings_file_reads_well_with_both_sources_off()
    {
        var paths = ScratchPaths();
        Assert.False(File.Exists(paths.SettingsFile));
        Assert.True(new SettingsStore(paths).TryLoadAgentSources(out var codex, out var antigravity));
        Assert.False(codex);
        Assert.False(antigravity);
    }

    [Fact]
    public void Both_sources_on_read_as_on()
    {
        var store = StoreOver("{}");
        store.SaveSetup(new SetupSettings(false, new Dictionary<string, bool> { [SetupSettings.Claude] = true, [SetupSettings.Codex] = true }));
        store.SaveActivity(AgentSources.Antigravity, true);
        Assert.True(store.TryLoadAgentSources(out var codex, out var antigravity));
        Assert.True(codex);
        Assert.True(antigravity);
    }

    [Fact]
    public void A_file_held_open_by_a_save_is_a_failed_read()
    {
        var paths = ScratchPaths();
        Directory.CreateDirectory(paths.AppDataDir);
        File.WriteAllText(paths.SettingsFile, "{}");
        var store = new SettingsStore(paths);
        using (new FileStream(paths.SettingsFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.False(store.TryLoadAgentSources(out _, out _));
    }

    [Fact]
    public void Truncated_json_is_a_failed_read()
    {
        Assert.False(StoreOver("{\"setup\":").TryLoadAgentSources(out _, out _));
    }

    [Fact]
    public void A_failed_read_keeps_the_last_good_values_and_a_good_off_is_believed()
    {
        var known = new App.AgentSourcesKnown();
        Assert.Equal((false, false), (known.Codex, known.Antigravity));   // before any good read, both off

        known.Update(read: true, codex: true, antigravity: true);
        Assert.Equal((true, true), (known.Codex, known.Antigravity));

        known.Update(read: false, codex: false, antigravity: false);
        Assert.Equal((true, true), (known.Codex, known.Antigravity));     // a save in flight is not a switch going off

        known.Update(read: true, codex: false, antigravity: true);
        Assert.Equal((false, true), (known.Codex, known.Antigravity));
    }

    [Fact]
    public void A_turn_card_goes_up_through_the_store_is_renamed_and_clears_with_its_agent() => AppHost.Run(() =>
    {
        using var store = new Sanduhr.App.Desk.WatcherStore(ScratchPaths(), () => false);
        int changed = 0;
        store.Changed += () => changed++;

        store.ApplyTurn(new AgentTurnEvent("codex", AgentTurnKind.Started, "s1", @"C:\proj\alpha", DateTimeOffset.UtcNow), "Codex · alpha", "Codex");
        Assert.Equal("Codex · alpha", Assert.Single(store.Shown).Title);      // no "Let Claude show watchers" needed

        store.RetitleTurns(_ => ("Codex · Dune Fox", "Codex"));
        Assert.Equal("Codex · Dune Fox", store.Shown[0].Title);

        store.ClearTurns("claude");
        Assert.Single(store.Shown);
        store.ClearTurns("codex");
        Assert.Empty(store.Shown);
        Assert.Equal(3, changed);
    });
}
