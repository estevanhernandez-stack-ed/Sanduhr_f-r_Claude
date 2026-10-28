using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>Desk's meter rows, Claude line and staleness (parity with the Mac's DeskMeters.swift).</summary>
public class DeskMetersTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 0, 0, 0, TimeSpan.Zero);
    private const string Session = "2026-10-11T03:00:00Z";
    private const string Week = "2026-10-14T00:00:00Z";

    private static DeskUsage Usage(params DeskTier[] tiers) => new(tiers, Now, false, false, null);

    [Fact]
    public void Rows_FollowTheGivenOrder_WithLabelsAndValues()
    {
        var u = Usage(new DeskTier(TierModel.SevenDay, 63, Week), new DeskTier(TierModel.FiveHour, 7, Session));
        var rows = DeskMeters.Rows(u, Now, TimeZoneInfo.Utc);
        Assert.Equal([TierModel.SevenDay, TierModel.FiveHour], rows.Select(r => r.Key));
        Assert.Equal("Weekly - All Models", rows[0].Label);
        Assert.Equal("Session (5hr)", rows[1].Label);
        Assert.Equal(63, rows[0].Percent);
        Assert.Equal(0.63, rows[0].Fill, 6);
        Assert.Equal("Today 3:00 AM", rows[1].Reset);
        Assert.Equal("Wed 12:00 AM", rows[0].Reset);
    }

    [Fact]
    public void Percent_Truncates()
    {
        var row = Assert.Single(DeskMeters.Rows(Usage(new DeskTier(TierModel.FiveHour, 96.7, null)), Now));
        Assert.Equal(96, row.Percent);
    }

    [Fact]
    public void Fill_Clamps_ButPercentPassesOneHundred()
    {
        var rows = DeskMeters.Rows(Usage(new DeskTier(TierModel.FiveHour, 130, null), new DeskTier(TierModel.SevenDay, -5, null)), Now);
        Assert.Equal(1.0, rows[0].Fill);
        Assert.Equal(130, rows[0].Percent);
        Assert.Equal(0.0, rows[1].Fill);
    }

    [Fact]
    public void Pace_PresentWithAResetTime_AbsentWithout()
    {
        var rows = DeskMeters.Rows(Usage(new DeskTier(TierModel.FiveHour, 10, "2026-10-11T02:30:00Z"), new DeskTier(TierModel.SevenDay, 10, null)), Now);
        Assert.Equal(Pacing.PaceFrac("2026-10-11T02:30:00Z", TierModel.FiveHour, Now), rows[0].Pace);
        Assert.NotNull(rows[0].Pace);
        Assert.Null(rows[1].Pace);
        Assert.Equal("", rows[1].Reset);
    }

    [Fact]
    public void Each_row_carries_its_level_at_the_alert_thresholds()
    {
        var rows = DeskMeters.Rows(Usage(new DeskTier(TierModel.FiveHour, 79.9, null), new DeskTier(TierModel.SevenDay, 80, null)), Now);
        Assert.Equal([MeterLevel.Calm, MeterLevel.Warn], rows.Select(r => r.Level));
        rows = DeskMeters.Rows(Usage(new DeskTier(TierModel.FiveHour, 94.9, null), new DeskTier(TierModel.SevenDay, 95, null)), Now);
        Assert.Equal([MeterLevel.Warn, MeterLevel.Urgent], rows.Select(r => r.Level));
        rows = DeskMeters.Rows(Usage(new DeskTier(TierModel.FiveHour, 65, null), new DeskTier(TierModel.SevenDay, 70, null)), Now,
            warnAt: 60, urgentAt: 70);
        Assert.Equal([MeterLevel.Warn, MeterLevel.Urgent], rows.Select(r => r.Level));
    }

    [Fact]
    public void SignInNeeded_HasNoRows()
    {
        var u = new DeskUsage([new DeskTier(TierModel.FiveHour, 50, null)], Now, true, false, null);
        Assert.Empty(DeskMeters.Rows(u, Now));
    }

    [Fact]
    public void ResetText_MatchesTheCard_AcrossDays()
    {
        Assert.Equal("Tomorrow 9:30 AM", DeskMeters.ResetDateTime("2026-10-12T09:30:00Z", Now, TimeZoneInfo.Utc));
        Assert.Equal("Fri 1:00 PM", DeskMeters.ResetDateTime("2026-10-16T13:00:00Z", Now, TimeZoneInfo.Utc));
        Assert.Equal("Sun Oct 18 1:00 PM", DeskMeters.ResetDateTime("2026-10-18T13:00:00Z", Now, TimeZoneInfo.Utc));
        Assert.Equal("", DeskMeters.ResetDateTime(null, Now, TimeZoneInfo.Utc));
        Assert.Equal("", DeskMeters.ResetDateTime("garbage", Now, TimeZoneInfo.Utc));
    }

    [Fact]
    public void Line_SessionAndWeek()
    {
        var u = Usage(new DeskTier(TierModel.FiveHour, 7, Session), new DeskTier(TierModel.SevenDay, 63.9, Week));
        Assert.Equal("claude   7% session, resets 3:00   63% week", DeskClaudeText.Line(u, TimeZoneInfo.Utc));
    }

    [Fact]
    public void Line_SessionWithoutAResetTime()
    {
        var u = Usage(new DeskTier(TierModel.FiveHour, 7, null));
        Assert.Equal("claude   7% session", DeskClaudeText.Line(u, TimeZoneInfo.Utc));
    }

    [Fact]
    public void Line_OnlyWeek()
    {
        var u = Usage(new DeskTier(TierModel.SevenDay, 63, Week));
        Assert.Equal("claude   63% week", DeskClaudeText.Line(u, TimeZoneInfo.Utc));
    }

    [Fact]
    public void Line_UsesTheAccountLabel()
    {
        var u = Usage(new DeskTier(TierModel.SevenDay, 63, Week)) with { Account = "Work" };
        Assert.Equal("Work   63% week", DeskClaudeText.Line(u, TimeZoneInfo.Utc));
        var p = DeskClaudeText.GetParts(u, TimeZoneInfo.Utc);
        Assert.Equal("Work", p!.Account);
        Assert.Equal("63% week", p.Rest);
    }

    [Fact]
    public void Line_SignIn_FirstAndAgain()
    {
        var first = DeskUsage.Empty with { SignInNeeded = true, FirstSignIn = true };
        var again = DeskUsage.Empty with { SignInNeeded = true, Account = "Work" };
        Assert.Equal("claude   sign in to Sanduhr", DeskClaudeText.Line(first));
        Assert.Equal("Work   sign in again in Sanduhr", DeskClaudeText.Line(again));
        Assert.Equal("sign in to Sanduhr", DeskClaudeText.CompactSignIn);
        Assert.Equal("sign in to Sanduhr", DeskClaudeText.SignInLine(true));
        Assert.Equal("sign in again in Sanduhr", DeskClaudeText.SignInLine(false));
    }

    [Fact]
    public void Line_NothingToSay_IsNull()
    {
        Assert.Null(DeskClaudeText.Line(DeskUsage.Empty));
        Assert.Null(DeskClaudeText.GetParts(Usage(new DeskTier(TierModel.SevenDaySonnet, 5, null))));
    }

    [Fact]
    public void IsStale_Cases()
    {
        Assert.True(DeskUsage.Empty.IsStale(Now));
        Assert.True((DeskUsage.Empty with { FetchedAt = Now, SignInNeeded = true }).IsStale(Now));
        var fresh = DeskUsage.Empty with { FetchedAt = Now };
        Assert.False(fresh.IsStale(Now));
        Assert.False(fresh.IsStale(Now + DeskUsage.StaleAfter));
        Assert.True(fresh.IsStale(Now + DeskUsage.StaleAfter + TimeSpan.FromSeconds(1)));
        Assert.Equal(TimeSpan.FromMinutes(15), DeskUsage.StaleAfter);
    }

    /// <summary>PR #209 review, finding 8: a hidden session card still feeds the Claude line, while the
    /// meters draw only the visible tiers.</summary>
    [Fact]
    public void ClaudeLine_ReadsLineTiers_WhenTheCardsAreHidden()
    {
        var visible = new[] { new DeskTier("seven_day_sonnet", 50, null) };
        var line = new[] { new DeskTier(TierModel.FiveHour, 7, null), new DeskTier(TierModel.SevenDay, 63, null) };
        var u = new DeskUsage(visible, Now, false, false, null, line);
        Assert.Equal("claude   7% session   63% week", DeskClaudeText.Line(u, TimeZoneInfo.Utc));
        Assert.Equal(["seven_day_sonnet"], DeskMeters.Rows(u, Now, TimeZoneInfo.Utc).Select(r => r.Key));
        // Without line tiers the visible ones feed the line, as before.
        Assert.Null(DeskClaudeText.Line(u with { LineTiers = null }, TimeZoneInfo.Utc));
    }
}
