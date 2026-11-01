using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class SetupSettingsTests
{
    private static readonly SetupSettings Fallback = new(true, new Dictionary<string, bool> { ["claude"] = false });

    [Fact]
    public void Encode_and_decode_round_trip_and_keep_unknown_sources()
    {
        var s = new SetupSettings(false, new Dictionary<string, bool> { ["claude"] = true, ["codex"] = false });
        var back = SetupSettings.Decode(JsonNode.Parse(s.Encode().ToJsonString()), Fallback);
        Assert.False(back.Desktop);
        Assert.True(back.ClaudeUsage);
        Assert.False(back.Usage["codex"]);
    }

    [Fact]
    public void An_unreadable_entry_falls_back_entry_by_entry_never_to_nothing_ticked()
    {
        var back = SetupSettings.Decode(JsonNode.Parse("""{"desktop":"yes","usage":{"claude":true}}"""), Fallback);
        Assert.True(back.Desktop);        // from the fallback
        Assert.True(back.ClaudeUsage);    // read
        var junk = SetupSettings.Decode(JsonNode.Parse("[1,2]"), Fallback);
        Assert.Equal(Fallback.Desktop, junk.Desktop);
        Assert.Equal(Fallback.ClaudeUsage, junk.ClaudeUsage);
    }

    [Theory]
    [InlineData(true, false, false, true, true, false, true, true)]    // an account: usage on, desktop on, the Desk stays on
    [InlineData(true, false, false, false, true, false, true, false)]  // an account, Desk off: desktop still on, the Desk stays off (a later Desk switch draws the whole Desk)
    [InlineData(false, true, true, false, true, true, false, true)]    // Just the Desk: usage off, desktop on, Desk on, the reminder it silenced returns
    [InlineData(false, true, false, false, true, false, false, true)]  // Just the Desk, reminder not silenced: it is not forced back
    [InlineData(true, true, false, true, true, false, true, true)]     // both: the account wins
    [InlineData(true, true, true, false, true, true, true, false)]     // both, silenced: the account wins, the silenced reminder returns, the Desk stays off
    public void Migration_maps_each_starting_case(bool account, bool jtd, bool silenced, bool deskOn,
        bool wantDesktop, bool wantReminder, bool wantUsage, bool wantDeskOn)
    {
        var m = SetupMigration.From(account, jtd, silenced, deskOn);
        Assert.NotNull(m);
        Assert.Equal(wantDesktop, m!.Setup.Desktop);
        Assert.Equal(wantUsage, m.Setup.ClaudeUsage);
        Assert.Equal(wantReminder, m.ReminderOn);
        Assert.Equal(wantDeskOn, m.DeskOn);
    }

    [Fact]
    public void An_upgrader_with_the_Desk_off_shows_desktop_unticked_but_keeps_desktop_for_when_the_Desk_comes_on()
    {
        var m = SetupMigration.From(hasAccount: true, justTheDesk: false, silencedReminder: false, deskEnabled: false)!;
        Assert.True(m.Setup.Desktop);
        Assert.False(SetupChoices.Of(m.Setup, drawersOn: false, deskEnabled: false).Desktop);   // the screen shows the Desk's own switch
        Assert.True(SetupChoices.Of(m.Setup, drawersOn: false, deskEnabled: true).Desktop);
    }

    [Fact]
    public void Neither_an_account_nor_Just_the_Desk_is_a_first_run()
    {
        Assert.Null(SetupMigration.From(false, false, false, false));
        Assert.True(SetupLaunch.OpensSetup(null));
    }

    [Fact]
    public void A_tray_click_opens_the_setup_screen_only_while_no_setup_exists()
    {
        Assert.True(SetupLaunch.TrayClickOpensSetup(setupPending: true));
        Assert.False(SetupLaunch.TrayClickOpensSetup(setupPending: false));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void No_setup_yet_starts_the_widget_hidden_whatever_the_Desk_says(bool deskOn)
    {
        Assert.True(SetupLaunch.WidgetStartsHidden(null, deskOn));
    }

    [Theory]
    [InlineData(false, true, true)]    // usage off, Desk on: hidden, the tray is the way back
    [InlineData(false, false, false)]  // usage off, Desk off: shown, a launch never shows nothing
    [InlineData(true, true, false)]    // usage on: shown
    public void The_widget_starts_hidden_only_with_usage_off_and_the_Desk_running(bool usage, bool deskOn, bool hidden)
    {
        var s = new SetupSettings(true, new Dictionary<string, bool> { ["claude"] = usage });
        Assert.Equal(hidden, SetupLaunch.WidgetStartsHidden(s, deskOn));
    }

    [Fact]
    public void Choices_tick_desktop_only_when_the_Desk_runs_and_need_one_tick()
    {
        var s = new SetupSettings(true, new Dictionary<string, bool> { ["claude"] = false });
        Assert.False(SetupChoices.Of(s, drawersOn: false, deskEnabled: false).Desktop);
        Assert.True(SetupChoices.Of(s, drawersOn: false, deskEnabled: true).Desktop);
        Assert.False(new SetupChoices(false, false, false).AnyTicked);
        Assert.True(new SetupChoices(false, true, false).DeskRuns);
        Assert.Equal(new SetupChoices(true, true, false), SetupChoices.FirstRun);
    }

    [Fact]
    public void ToSettings_keeps_sources_this_build_does_not_show()
    {
        var before = new SetupSettings(true, new Dictionary<string, bool> { ["claude"] = false, ["codex"] = true });
        var after = new SetupChoices(false, true, true).ToSettings(before);
        Assert.False(after.Desktop);
        Assert.True(after.ClaudeUsage);
        Assert.True(after.Usage["codex"]);
    }
}
