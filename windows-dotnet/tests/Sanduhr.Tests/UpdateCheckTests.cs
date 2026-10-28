using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Settings v2, slice 6: when the GitHub build asks for a newer release ("update_check"), and the intervals Privacy's copy quotes.</summary>
public class UpdateCheckTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Off_is_never_due()
    {
        Assert.False(UpdateCheck.Due(enabled: false, lastChecked: null, now: Now));
        Assert.False(UpdateCheck.Due(enabled: false, lastChecked: Now.AddDays(-30), now: Now));
    }

    [Fact]
    public void On_it_is_due_first_then_once_a_day()
    {
        Assert.Equal(TimeSpan.FromHours(24), UpdateCheck.Interval);
        Assert.True(UpdateCheck.Due(true, null, Now));
        Assert.False(UpdateCheck.Due(true, Now.AddHours(-23), Now));
        Assert.True(UpdateCheck.Due(true, Now - UpdateCheck.Interval, Now));
    }

    [Fact]
    public void A_stamp_from_the_future_or_unreadable_is_due()
    {
        // A clock that moved backwards must not park the check forever (Review Focus 3).
        Assert.True(UpdateCheck.Due(true, Now.AddDays(2), Now));
        Assert.Null(UpdateCheck.ParseStamp("not a time"));
        Assert.Null(UpdateCheck.ParseStamp(null));
        Assert.True(UpdateCheck.Due(true, UpdateCheck.ParseStamp("not a time"), Now));
    }

    [Fact]
    public void A_stamp_round_trips()
    {
        var text = UpdateCheck.FormatStamp(Now);
        Assert.Equal(Now, UpdateCheck.ParseStamp(text));
        // The stamp 3.x builds wrote (DateTimeOffset.UtcNow.ToString("O")) still reads.
        Assert.Equal(Now, UpdateCheck.ParseStamp(Now.ToString("O") + "\r\n"));
    }

    [Fact]
    public void The_usage_schedule_is_five_minutes_and_a_five_second_retry()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), UsageSchedule.Refresh);
        Assert.Equal(TimeSpan.FromSeconds(5), UsageSchedule.RetryAfterFailure);
    }
}
