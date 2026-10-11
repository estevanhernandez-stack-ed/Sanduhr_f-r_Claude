using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>Following the account in use: every rule of AccountFollow.Decide on made-up readings and a fixed clock.</summary>
public class AccountFollowTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

    private static DateTimeOffset Minutes(double m) => Now.AddMinutes(m);

    private static List<SessionReading> Readings(params (double min, double util)[] pairs)
        => pairs.Select(p => new SessionReading(Minutes(p.min), p.util)).ToList();

    /// <summary>Following on, Personal active, Work signed in: Work rose in its latest check, Personal flat.</summary>
    private static AccountFollow.State ClearSignal()
    {
        var s = new AccountFollow.State
        {
            Enabled = true,
            Active = "Personal",
            SignedIn = new HashSet<string> { "Personal", "Work" },
        };
        s.Readings["Work"] = Readings((-16, 10), (-1, 14));
        s.Readings["Personal"] = Readings((-20, 40), (-10, 40), (-5, 40), (0, 40));
        return s;
    }

    private static void AssertStay(AccountFollow.Reason reason, AccountFollow.Decision d)
        => Assert.Equal(AccountFollow.Decision.Stay(reason), d);

    [Fact]
    public void A_clear_signal_switches()
        => Assert.Equal(AccountFollow.Decision.Switch("Work"), AccountFollow.Decide(ClearSignal(), Now));

    [Fact]
    public void Off_by_default_and_off_never_switches()
    {
        Assert.False(new AccountFollow.State().Enabled);
        var s = ClearSignal();
        s.Enabled = false;
        AssertStay(AccountFollow.Reason.Off, AccountFollow.Decide(s, Now));
        Assert.Empty(AccountFollow.OthersInUse(s, Now));
    }

    [Fact]
    public void Both_in_use_stays_and_marks_the_other()
    {
        var s = ClearSignal();
        s.Readings["Personal"] = Readings((-20, 40), (-10, 40), (-5, 42));
        AssertStay(AccountFollow.Reason.BothInUse, AccountFollow.Decide(s, Now));
        Assert.Equal(new HashSet<string> { "Work" }, AccountFollow.OthersInUse(s, Now));
    }

    [Fact]
    public void The_active_rise_counts_only_inside_the_window()
    {
        var s = ClearSignal();
        s.Readings["Personal"] = Readings((-25, 30), (-20, 40), (-10, 40), (-5, 40));
        Assert.Equal(AccountFollow.Decision.Switch("Work"), AccountFollow.Decide(s, Now));
        s.Readings["Personal"] = Readings((-17, 40), (-12, 41));
        AssertStay(AccountFollow.Reason.BothInUse, AccountFollow.Decide(s, Now));
    }

    [Fact]
    public void At_most_one_automatic_switch_per_30_minutes()
    {
        var s = ClearSignal();
        s.LastAutoSwitch = Minutes(-29);
        AssertStay(AccountFollow.Reason.TooSoon, AccountFollow.Decide(s, Now));
        s.LastAutoSwitch = Minutes(-30);
        Assert.Equal(AccountFollow.Decision.Switch("Work"), AccountFollow.Decide(s, Now));
    }

    [Fact]
    public void A_manual_switch_pauses_for_three_hours()
    {
        var s = ClearSignal();
        s.ManualPause = new AccountFollow.Pause("Personal", Minutes(-120));
        s.Readings["Personal"] = Readings((-120, 5), (-60, 20), (-20, 30), (-5, 30), (0, 30));
        Assert.True(AccountFollow.IsPaused(s, Now));
        AssertStay(AccountFollow.Reason.Paused, AccountFollow.Decide(s, Now));
        s.ManualPause = new AccountFollow.Pause("Personal", Minutes(-180));
        Assert.False(AccountFollow.IsPaused(s, Now));
        Assert.Equal(AccountFollow.Decision.Switch("Work"), AccountFollow.Decide(s, Now));
    }

    [Fact]
    public void The_pause_ends_once_the_picked_account_is_idle_30_minutes()
    {
        var s = ClearSignal();
        s.ManualPause = new AccountFollow.Pause("Personal", Minutes(-60));
        s.Readings["Personal"] = Readings((-60, 5), (-31, 9), (-20, 9), (-5, 9));
        Assert.False(AccountFollow.IsPaused(s, Now));
        Assert.Equal(AccountFollow.Decision.Switch("Work"), AccountFollow.Decide(s, Now));
        s.Readings["Personal"] = Readings((-60, 5), (-29, 9), (-5, 9));
        Assert.True(AccountFollow.IsPaused(s, Now));
        s.ManualPause = new AccountFollow.Pause("Personal", Minutes(-20));
        s.Readings["Personal"] = Readings((-50, 5), (-40, 9), (-10, 9));
        Assert.True(AccountFollow.IsPaused(s, Now));
        s.ManualPause = new AccountFollow.Pause("Personal", Minutes(-30));
        Assert.False(AccountFollow.IsPaused(s, Now));
    }

    [Fact]
    public void A_reset_is_not_use()
    {
        var s = ClearSignal();
        s.Readings["Work"] = Readings((-16, 85), (-1, 2));
        Assert.False(AccountFollow.InUseInLatestCheck(s.Readings["Work"], Now));
        AssertStay(AccountFollow.Reason.NoSignal, AccountFollow.Decide(s, Now));
        s.Readings["Work"] = Readings((-16, 20), (-1, 20));
        AssertStay(AccountFollow.Reason.NoSignal, AccountFollow.Decide(s, Now));
        s = ClearSignal();
        s.Readings["Personal"] = Readings((-12, 90), (-6, 1));
        Assert.Equal(AccountFollow.Decision.Switch("Work"), AccountFollow.Decide(s, Now));
        s.Readings["Personal"] = Readings((-12, 90), (-6, 1), (-1, 3));
        AssertStay(AccountFollow.Reason.BothInUse, AccountFollow.Decide(s, Now));
    }

    [Fact]
    public void One_check_is_only_a_baseline()
    {
        var s = ClearSignal();
        s.Readings["Work"] = Readings((-1, 14));
        AssertStay(AccountFollow.Reason.NoSignal, AccountFollow.Decide(s, Now));
    }

    [Fact]
    public void A_stale_check_is_no_signal()
    {
        var s = ClearSignal();
        s.Readings["Work"] = Readings((-40, 10), (-21, 14));
        AssertStay(AccountFollow.Reason.NoSignal, AccountFollow.Decide(s, Now));
        s.Readings["Work"] = Readings((-35, 10), (-20, 14));
        Assert.Equal(AccountFollow.Decision.Switch("Work"), AccountFollow.Decide(s, Now));
    }

    [Fact]
    public void Never_while_signed_out_switching_or_with_one_signed_in_account()
    {
        var s = ClearSignal();
        s.ActiveSignedOut = true;
        AssertStay(AccountFollow.Reason.NotReady, AccountFollow.Decide(s, Now));
        s = ClearSignal();
        s.Switching = true;
        AssertStay(AccountFollow.Reason.NotReady, AccountFollow.Decide(s, Now));
        s = ClearSignal();
        s.SignedIn = new HashSet<string> { "Personal" };
        AssertStay(AccountFollow.Reason.NotReady, AccountFollow.Decide(s, Now));
        s = ClearSignal();
        s.Active = null;
        AssertStay(AccountFollow.Reason.NotReady, AccountFollow.Decide(s, Now));
    }

    [Fact]
    public void A_signed_out_account_is_never_a_candidate()
    {
        var s = ClearSignal();
        s.SignedIn = new HashSet<string> { "Personal", "Home" };
        AssertStay(AccountFollow.Reason.NoSignal, AccountFollow.Decide(s, Now));
        Assert.Empty(AccountFollow.OthersInUse(s, Now));
    }

    [Fact]
    public void Two_others_in_use_is_not_a_clear_signal()
    {
        var s = ClearSignal();
        s.SignedIn = new HashSet<string> { "Personal", "Work", "Team" };
        s.Readings["Team"] = Readings((-16, 1), (-2, 6));
        AssertStay(AccountFollow.Reason.Unclear, AccountFollow.Decide(s, Now));
        Assert.Equal(new HashSet<string> { "Work", "Team" }, AccountFollow.OthersInUse(s, Now));
    }

    [Fact]
    public void The_active_account_is_never_marked_in_use()
    {
        var s = ClearSignal();
        s.Readings["Personal"] = Readings((-16, 1), (-1, 6));
        Assert.Equal(new HashSet<string> { "Work" }, AccountFollow.OthersInUse(s, Now));
    }

    [Fact]
    public void Record_keeps_order_and_drops_old_readings()
    {
        var s = new AccountFollow.State();
        s.Record("Work", 1, Minutes(-300));
        s.Record("Work", 2, Minutes(-10));
        s.Record("Work", 3, Now);
        Assert.Equal(new double[] { 2, 3 }, s.Readings["Work"].Select(r => r.Utilization));
    }

    [Fact]
    public void Rename_and_forget_carry_the_readings_and_the_pause()
    {
        var s = ClearSignal();
        s.ManualPause = new AccountFollow.Pause("Work", Now);
        s.Rename("Work", "Office");
        Assert.False(s.Readings.ContainsKey("Work"));
        Assert.Equal(2, s.Readings["Office"].Count);
        Assert.Equal("Office", s.ManualPause?.Account);
        s.Forget("Office");
        Assert.False(s.Readings.ContainsKey("Office"));
        Assert.Null(s.ManualPause);
    }

    [Fact]
    public void The_timings_are_the_specs()
    {
        Assert.Equal(TimeSpan.FromSeconds(15 * 60), AccountFollow.CheckInterval);
        Assert.Equal(TimeSpan.FromSeconds(30 * 60), AccountFollow.SwitchSpacing);
        Assert.Equal(TimeSpan.FromSeconds(3 * 60 * 60), AccountFollow.PauseLength);
        Assert.Equal(TimeSpan.FromSeconds(30 * 60), AccountFollow.PauseIdle);
        Assert.Equal(TimeSpan.FromSeconds(60), AccountFollow.NoteLength);
    }
}
