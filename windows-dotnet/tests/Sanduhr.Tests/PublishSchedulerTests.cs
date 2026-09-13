using Sanduhr.Core;

namespace Sanduhr.Tests;

public class PublishSchedulerTests
{
    private static readonly TimeOnly At0645 = new(6, 45);

    private static DateTimeOffset Local(int year, int month, int day, int hour, int minute)
    {
        var dt = new DateTime(year, month, day, hour, minute, 0);
        return new DateTimeOffset(dt, TimeZoneInfo.Local.GetUtcOffset(dt));
    }

    [Fact]
    public void Disabled_never_runs()
    {
        var d = PublishScheduler.Decide(Local(2026, 9, 13, 9, 0), enabled: false, At0645, null, null);
        Assert.Equal(PublishVerdict.Disabled, d.Verdict);
    }

    [Fact]
    public void Before_publish_time_waits_and_target_is_yesterday()
    {
        var d = PublishScheduler.Decide(Local(2026, 9, 13, 6, 44), enabled: true, At0645, null, null);
        Assert.Equal(PublishVerdict.BeforePublishTime, d.Verdict);
        Assert.Equal(new DateOnly(2026, 9, 12), d.TargetDate);
    }

    [Fact]
    public void At_publish_time_with_nothing_published_runs_for_yesterday()
    {
        var d = PublishScheduler.Decide(Local(2026, 9, 13, 6, 45), enabled: true, At0645, null, null);
        Assert.Equal(PublishVerdict.Run, d.Verdict);
        Assert.Equal(new DateOnly(2026, 9, 12), d.TargetDate);
    }

    [Fact]
    public void Already_published_target_date_does_not_run_again()
    {
        var d = PublishScheduler.Decide(Local(2026, 9, 13, 12, 0), enabled: true, At0645,
            lastOkDate: new DateOnly(2026, 9, 12), lastAttemptAt: Local(2026, 9, 13, 6, 45));
        Assert.Equal(PublishVerdict.AlreadyPublished, d.Verdict);
    }

    [Fact]
    public void Older_last_ok_date_runs_for_the_new_target()
    {
        var d = PublishScheduler.Decide(Local(2026, 9, 14, 7, 0), enabled: true, At0645,
            lastOkDate: new DateOnly(2026, 9, 12), lastAttemptAt: Local(2026, 9, 13, 6, 45));
        Assert.Equal(PublishVerdict.Run, d.Verdict);
        Assert.Equal(new DateOnly(2026, 9, 13), d.TargetDate);
    }

    [Fact]
    public void Failed_attempt_backs_off_for_fifteen_minutes_then_retries()
    {
        var failedAt = Local(2026, 9, 13, 6, 45);
        var tooSoon = PublishScheduler.Decide(Local(2026, 9, 13, 6, 59), enabled: true, At0645, null, failedAt);
        Assert.Equal(PublishVerdict.Backoff, tooSoon.Verdict);
        var again = PublishScheduler.Decide(Local(2026, 9, 13, 7, 0), enabled: true, At0645, null, failedAt);
        Assert.Equal(PublishVerdict.Run, again.Verdict);
    }

    [Fact]
    public void Late_start_after_publish_time_still_runs_once()
    {
        // App launched at 21:00 — the 06:45 slot is long gone; publish yesterday now, once.
        var d = PublishScheduler.Decide(Local(2026, 9, 13, 21, 0), enabled: true, At0645, null, null);
        Assert.Equal(PublishVerdict.Run, d.Verdict);
    }

    // NextRunAt is what the Settings status line prints as "Next:". It must be
    // when the scheduler fires next, never a date derived from the target day.

    [Fact]
    public void After_a_successful_publish_today_next_run_is_tomorrow_at_publish_time()
    {
        // 14:29 on the 13th, and the 12th (yesterday) went out at 06:45. Today's slot is spent;
        // the next real run is the 14th at 06:45, reporting the 13th.
        var d = PublishScheduler.Decide(Local(2026, 9, 13, 14, 29), enabled: true, At0645,
            lastOkDate: new DateOnly(2026, 9, 12), lastAttemptAt: Local(2026, 9, 13, 6, 45));
        Assert.Equal(PublishVerdict.AlreadyPublished, d.Verdict);
        Assert.Equal(new DateTime(2026, 9, 14, 6, 45, 0), d.NextRunAt);
    }

    [Fact]
    public void Published_early_by_hand_before_the_slot_still_points_next_run_at_tomorrow()
    {
        // "Publish now" at 01:00 sent the 12th; today's 06:45 slot would report the 12th again,
        // so it is skipped and the next run is tomorrow's slot.
        var d = PublishScheduler.Decide(Local(2026, 9, 13, 6, 0), enabled: true, At0645,
            lastOkDate: new DateOnly(2026, 9, 12), lastAttemptAt: Local(2026, 9, 13, 1, 0));
        Assert.Equal(PublishVerdict.AlreadyPublished, d.Verdict);
        Assert.Equal(new DateTime(2026, 9, 14, 6, 45, 0), d.NextRunAt);
    }

    [Fact]
    public void Before_publish_time_with_yesterday_unpublished_next_run_is_today_at_publish_time()
    {
        var d = PublishScheduler.Decide(Local(2026, 9, 13, 6, 44), enabled: true, At0645,
            lastOkDate: new DateOnly(2026, 9, 11), lastAttemptAt: Local(2026, 9, 12, 6, 45));
        Assert.Equal(PublishVerdict.BeforePublishTime, d.Verdict);
        Assert.Equal(new DateTime(2026, 9, 13, 6, 45, 0), d.NextRunAt);
    }

    [Fact]
    public void Past_publish_time_with_yesterday_unpublished_next_run_is_now()
    {
        var now = Local(2026, 9, 13, 14, 29);
        var d = PublishScheduler.Decide(now, enabled: true, At0645,
            lastOkDate: new DateOnly(2026, 9, 11), lastAttemptAt: Local(2026, 9, 12, 6, 45));
        Assert.Equal(PublishVerdict.Run, d.Verdict);
        Assert.Equal(now.LocalDateTime, d.NextRunAt);
    }

    [Fact]
    public void Backoff_next_run_is_the_retry_moment()
    {
        var failedAt = Local(2026, 9, 13, 6, 45);
        var d = PublishScheduler.Decide(Local(2026, 9, 13, 6, 50), enabled: true, At0645, null, failedAt);
        Assert.Equal(PublishVerdict.Backoff, d.Verdict);
        Assert.Equal(new DateTime(2026, 9, 13, 7, 0, 0), d.NextRunAt);
    }

    [Fact]
    public void Disabled_has_no_next_run()
    {
        var d = PublishScheduler.Decide(Local(2026, 9, 13, 9, 0), enabled: false, At0645, null, null);
        Assert.Null(d.NextRunAt);
    }

    [Theory]
    [InlineData("06:45", 6, 45)]
    [InlineData("6:05", 6, 5)]
    [InlineData(" 23:59 ", 23, 59)]
    public void Parse_publish_time_accepts_24h_hh_mm(string text, int h, int m)
        => Assert.Equal(new TimeOnly(h, m), PublishScheduler.ParsePublishTime(text));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("24:00")]
    [InlineData("6pm")]
    [InlineData("0645")]
    public void Parse_publish_time_rejects_garbage(string? text)
        => Assert.Null(PublishScheduler.ParsePublishTime(text));

    [Fact]
    public void Format_round_trips()
        => Assert.Equal("06:45", PublishScheduler.FormatPublishTime(PublishScheduler.DefaultPublishTime));
}
