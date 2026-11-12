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
    public void A_passed_reset_says_when_it_reset_never_today()
    {
        // Live check, 4.8.0: an idle Codex 5-hour window that reset two days earlier read "Today 9:36 PM".
        var later = new DateTimeOffset(2026, 10, 11, 20, 0, 0, TimeSpan.Zero);
        Assert.Equal("Reset at 3:10 PM", DeskMeters.ResetDateTime("2026-10-11T15:10:00Z", later, TimeZoneInfo.Utc));
        Assert.Equal("Reset Oct 9, 9:36 PM", DeskMeters.ResetDateTime("2026-10-09T21:36:00Z", later, TimeZoneInfo.Utc));
        Assert.Equal("Tomorrow 9:30 AM", DeskMeters.ResetDateTime("2026-10-12T09:30:00Z", later, TimeZoneInfo.Utc));
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

    // ---- agent layer: a piece's source, combined rows, the Codex line ----

    private static DeskUsage TwoSources(double claudeWeek = 40, double codexWeek = 70, IReadOnlySet<string>? codexStale = null) =>
        Usage(new DeskTier(TierModel.FiveHour, 10, Session), new DeskTier(TierModel.SevenDay, claudeWeek, Week)) with
        {
            SourceTiers = new Dictionary<string, IReadOnlyList<DeskTier>>
            {
                ["codex"] = [new DeskTier(TierModel.FiveHour, 25, Session), new DeskTier(TierModel.SevenDay, codexWeek, Week)],
            },
            SourceStaleTiers = codexStale is null ? null : new Dictionary<string, IReadOnlySet<string>> { ["codex"] = codexStale },
        };

    [Fact]
    public void CombinedRows_JoinsTheWeeklyWindow_AndTheFurtherSourceContinuesTheBar()
    {
        var rows = DeskMeters.CombinedRows(TwoSources(), "claude", Now, TimeZoneInfo.Utc);
        var weekly = Assert.Single(rows, r => r.Row.Key == TierModel.SevenDay);
        Assert.Equal("Weekly", weekly.Row.Label);
        Assert.Equal(40, weekly.Row.Percent);                       // the primary's number
        Assert.Equal("claude", weekly.Layout.Primary!.Source);
        Assert.Equal(0.4, weekly.Layout.Primary.To, 6);
        Assert.Equal("codex", weekly.Layout.Beyond!.Source);
        Assert.Equal(0.4, weekly.Layout.Beyond.From, 6);
        Assert.Equal(0.7, weekly.Layout.Beyond.To, 6);
        Assert.NotNull(weekly.Row.Pace);                            // Claude is primary: its pace mark stays
    }

    [Fact]
    public void CombinedRows_WithCodexPrimary_TakesItsNumberAndResetAndDropsClaudesPace()
    {
        var rows = DeskMeters.CombinedRows(TwoSources(claudeWeek: 40, codexWeek: 70), "codex", Now, TimeZoneInfo.Utc);
        var weekly = Assert.Single(rows, r => r.Row.Key == TierModel.SevenDay);
        Assert.Equal(70, weekly.Row.Percent);
        Assert.Equal(0.7, weekly.Row.Fill, 6);
        Assert.Null(weekly.Row.Pace);
        Assert.Equal("codex", weekly.Layout.Primary!.Source);
        Assert.Equal("claude", Assert.Single(weekly.Layout.Ticks).Source);
        Assert.Null(weekly.Layout.Beyond);
    }

    [Fact]
    public void CombinedRows_WithOnlyClaude_IsRowsWithASingleSegmentEach()
    {
        var u = Usage(new DeskTier(TierModel.FiveHour, 10, Session), new DeskTier(TierModel.SevenDay, 40.9, Week), new DeskTier("seven_day_sonnet", 12, null));
        var rows = DeskMeters.Rows(u, Now, TimeZoneInfo.Utc);
        var combined = DeskMeters.CombinedRows(u, "claude", Now, TimeZoneInfo.Utc);
        Assert.Equal(rows, combined.Select(c => c.Row));
        for (int i = 0; i < rows.Count; i++)
        {
            var l = combined[i].Layout;
            Assert.Equal("claude", l.Primary!.Source);
            Assert.Equal(0, l.Primary.From);
            Assert.Equal(u.Tiers[i].Utilization / 100, l.Primary.To, 6);
            Assert.Null(l.Beyond);
            Assert.Empty(l.Ticks);
        }
    }

    [Fact]
    public void CombinedRows_AModelWindowCodexDoesNotReport_StaysClaudesAlone()
    {
        var u = TwoSources() with { Tiers = [new DeskTier("seven_day_sonnet", 55, null)] };
        var (row, layout) = Assert.Single(DeskMeters.CombinedRows(u, "claude", Now, TimeZoneInfo.Utc));
        Assert.Equal("Weekly - Sonnet", row.Label);
        Assert.Null(layout.Beyond);
        Assert.Empty(layout.Ticks);
    }

    [Fact]
    public void CombinedRows_AStaleCodexTier_MarksOnlyThatTierStale()
    {
        var rows = DeskMeters.CombinedRows(TwoSources(codexStale: new HashSet<string> { TierModel.SevenDay }), "claude", Now, TimeZoneInfo.Utc);
        var session = Assert.Single(rows, r => r.Row.Key == TierModel.FiveHour).Layout;
        var weekly = Assert.Single(rows, r => r.Row.Key == TierModel.SevenDay).Layout;
        Assert.False(Assert.Single(session.Ticks).Stale);
        Assert.True(weekly.Beyond!.Stale);
        Assert.True(Assert.Single(weekly.Ticks).Stale);
    }

    [Fact]
    public void CombinedRows_PercentsTruncateOnTheRowAndNameEverySourceInTheAccessibleName()
    {
        var u = TwoSources(claudeWeek: 40.9, codexWeek: 70.6, codexStale: new HashSet<string> { TierModel.SevenDay });
        var weekly = Assert.Single(DeskMeters.CombinedRows(u, "claude", Now, TimeZoneInfo.Utc), r => r.Row.Key == TierModel.SevenDay);
        Assert.Equal(40, weekly.Row.Percent);
        string name = DeskMeters.CombinedName("Weekly", weekly.Layout);
        Assert.StartsWith("Weekly. ", name);
        Assert.Contains("Claude 40%.", name);
        Assert.Contains("Codex 70%, as of earlier.", name);
    }

    [Fact]
    public void CombinedRows_ASignInNeededDrawsNoRows()
    {
        Assert.Empty(DeskMeters.CombinedRows(TwoSources() with { SignInNeeded = true }, "claude", Now, TimeZoneInfo.Utc));
    }

    [Fact]
    public void SourceRows_Codex_HasItsLabelsNoPaceAndPerTierStaleness()
    {
        var u = TwoSources(codexStale: new HashSet<string> { TierModel.FiveHour });
        var rows = DeskMeters.SourceRows(u, "codex", Now, TimeZoneInfo.Utc);
        Assert.Equal(["Codex session (5hr)", "Codex weekly"], rows.Select(r => r.Label));
        Assert.Equal([25, 70], rows.Select(r => r.Percent));
        Assert.All(rows, r => Assert.Null(r.Pace));
        Assert.Equal([true, false], rows.Select(r => r.Stale));
        Assert.Equal("Today 3:00 AM", rows[0].Reset);
        Assert.Equal(DeskMeters.Rows(u, Now, TimeZoneInfo.Utc), DeskMeters.SourceRows(u, "claude", Now, TimeZoneInfo.Utc));
    }

    [Fact]
    public void SourceRows_ACodexThatReportsNothing_IsNone()
    {
        Assert.Empty(DeskMeters.SourceRows(Usage(new DeskTier(TierModel.FiveHour, 10, null)), "codex", Now, TimeZoneInfo.Utc));
    }

    [Fact]
    public void Resolve_CombinedWithNoClaudeAndACodex_DrawsCodex_ElseItStaysCombined()
    {
        Assert.Equal("codex", DeskMeters.Resolve(TwoSources() with { Tiers = [] }, "combined"));
        Assert.Equal("combined", DeskMeters.Resolve(TwoSources(), "combined"));
        Assert.Equal("combined", DeskMeters.Resolve(TwoSources() with { Tiers = [], SignInNeeded = true }, "combined"));   // a sign-in line wins
        Assert.Equal("combined", DeskMeters.Resolve(DeskUsage.Empty, "combined"));
        Assert.Equal("claude", DeskMeters.Resolve(TwoSources() with { Tiers = [] }, "claude"));
    }

    [Fact]
    public void ForSource_Codex_IsCodexAloneAndStaleWhenNoTierIsFresh()
    {
        var all = TwoSources(codexStale: new HashSet<string> { TierModel.FiveHour, TierModel.SevenDay });
        var alone = DeskMeters.ForSource(all with { SignInNeeded = true }, "codex", Now);
        Assert.False(alone.SignInNeeded);
        Assert.Equal("Codex", alone.Account);
        Assert.Equal(["five_hour", "seven_day"], alone.Tiers.Select(t => t.Key));
        Assert.True(alone.IsStale(Now));
        Assert.False(DeskMeters.ForSource(TwoSources(), "codex", Now).IsStale(Now));
        Assert.Same(all, DeskMeters.ForSource(all, "claude", Now));
    }

    [Fact]
    public void CodexLine_ReadsFiveHourAndWeek_TruncatedAndNullWithNone()
    {
        Assert.Equal("Codex 5h 25% · wk 70%", DeskClaudeText.CodexLine(TwoSources()));
        var weekOnly = Usage() with { SourceTiers = new Dictionary<string, IReadOnlyList<DeskTier>> { ["codex"] = [new DeskTier(TierModel.SevenDay, 12.9, null)] } };
        Assert.Equal("Codex wk 12%", DeskClaudeText.CodexLine(weekOnly));
        Assert.Null(DeskClaudeText.CodexLine(Usage()));
        // The Claude line is unchanged by Codex being there.
        Assert.Equal("claude   10% session, resets 3:00   40% week", DeskClaudeText.Line(TwoSources(), TimeZoneInfo.Utc));
    }
}
