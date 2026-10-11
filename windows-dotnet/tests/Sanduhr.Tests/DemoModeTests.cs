using System.Globalization;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Demo mode's sample accounts, numbers and history (port of the Mac's demo tests).</summary>
public class DemoModeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    private static double Util(System.Text.Json.Nodes.JsonObject o, string key) =>
        o[key]!["utilization"]!.GetValue<double>();

    private static DateTimeOffset Resets(System.Text.Json.Nodes.JsonObject o, string key) =>
        DateTimeOffset.Parse(o[key]!["resets_at"]!.GetValue<string>(), CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);

    [Fact]
    public void AccountsAndActive()
    {
        Assert.Equal(new[] { "Work", "Personal" }, DemoMode.Accounts);
        Assert.Equal("Work", DemoMode.Active);
    }

    [Fact]
    public void WorkUsageKeysAndValues()
    {
        var u = DemoMode.Usage("Work", Now);
        Assert.Equal(new[] { "five_hour", "seven_day", "seven_day_sonnet" }, u.Select(p => p.Key));
        Assert.Equal(42.0, Util(u, "five_hour"));
        Assert.Equal(63.0, Util(u, "seven_day"));
        Assert.Equal(18.0, Util(u, "seven_day_sonnet"));
        Assert.Equal(Now.AddHours(2), Resets(u, "five_hour"));
        Assert.Equal(Now.AddDays(4), Resets(u, "seven_day"));
        Assert.Equal(Now.AddDays(4), Resets(u, "seven_day_sonnet"));
    }

    [Fact]
    public void PersonalUsageKeysAndValues()
    {
        var u = DemoMode.Usage("Personal", Now);
        Assert.Equal(new[] { "five_hour", "seven_day" }, u.Select(p => p.Key));
        Assert.Equal(12.0, Util(u, "five_hour"));
        Assert.Equal(31.0, Util(u, "seven_day"));
        Assert.Equal(Now.AddHours(4), Resets(u, "five_hour"));
        Assert.Equal(Now.AddDays(5), Resets(u, "seven_day"));
    }

    [Fact]
    public void UnknownLabelGetsWorkNumbers()
    {
        Assert.Equal(42.0, Util(DemoMode.Usage("Someone", Now), "five_hour"));
    }

    [Theory]
    [InlineData("Work")]
    [InlineData("Personal")]
    public void EveryResetParsesAndLiesInTheFuture(string account)
    {
        var u = DemoMode.Usage(account, Now);
        foreach (var (key, _) in u)
            Assert.True(Resets(u, key) > Now, key);
    }

    [Fact]
    public void UsageIsDeterministicAndFresh()
    {
        var a = DemoMode.Usage("Work", Now);
        var b = DemoMode.Usage("Work", Now);
        Assert.NotSame(a, b);
        Assert.Equal(a.ToJsonString(), b.ToJsonString());
        a.Remove("five_hour");
        Assert.NotNull(DemoMode.Usage("Work", Now)["five_hour"]);
    }

    [Theory]
    [InlineData("Work", "five_hour", 42)]
    [InlineData("Work", "seven_day", 63)]
    [InlineData("Work", "seven_day_sonnet", 18)]
    [InlineData("Personal", "five_hour", 12)]
    [InlineData("Personal", "seven_day", 31)]
    public void HistoryEndsAtTheSample(string account, string tier, int expected)
    {
        var h = DemoMode.History(account, tier, Now);
        Assert.Equal(48, h.Count);
        Assert.Equal(expected, h[^1]);
        Assert.All(h, v => Assert.InRange(v, 0, 100));
    }

    [Fact]
    public void HistoryHonorsCount()
    {
        Assert.Equal(10, DemoMode.History("Work", "five_hour", Now, 10).Count);
        Assert.Empty(DemoMode.History("Work", "five_hour", Now, 0));
        Assert.Empty(DemoMode.History("Work", "five_hour", Now, -3));
    }

    [Fact]
    public void WorkAndPersonalDiffer()
    {
        Assert.NotEqual(DemoMode.History("Work", "five_hour", Now),
                        DemoMode.History("Personal", "five_hour", Now));
    }

    [Fact]
    public void HistoryEmptyForMissingTiers()
    {
        Assert.Empty(DemoMode.History("Personal", "seven_day_sonnet", Now));
        Assert.Empty(DemoMode.History("Work", "nope", Now));
    }

    [Fact]
    public void NextWraps()
    {
        Assert.Equal("Personal", DemoMode.Next("Work"));
        Assert.Equal("Work", DemoMode.Next("Personal"));
        Assert.Equal("Work", DemoMode.Next(null));
        Assert.Equal("Work", DemoMode.Next("Someone"));
    }

    [Theory]
    [InlineData("--demo", null, true)]
    [InlineData("--DEMO", null, true)]
    [InlineData("--demo-x", null, false)]
    [InlineData(null, "1", true)]
    [InlineData(null, "true", true)]
    [InlineData(null, "TRUE", true)]
    [InlineData(null, " 1 ", true)]
    [InlineData(null, "0", false)]
    [InlineData(null, "", false)]
    [InlineData(null, null, false)]
    public void IsRequestedCases(string? arg, string? env, bool expected)
    {
        string[] args = arg is null ? [] : [arg];
        Assert.Equal(expected, DemoMode.IsRequested(args, env));
    }

    [Fact]
    public void IsRequestedWithNoArgs() => Assert.False(DemoMode.IsRequested([], null));

    [Fact]
    public void LabelsAndBanner()
    {
        Assert.Equal("Work (demo)", DemoMode.ChipText("Work"));
        Assert.Equal("Demo · Work 42%", DemoMode.TrayLabel("Work", 42));
        Assert.Contains("Demo", DemoMode.Banner);
        Assert.Equal("--demo", DemoMode.Switch);
        Assert.Equal("SANDUHR_DEMO", DemoMode.EnvironmentVariable);
    }

    [Theory]
    [InlineData(true, false, true)]   // same fetcher, demo off: the answer lands
    [InlineData(true, true, false)]   // demo came on while it was out: dropped
    [InlineData(false, false, false)] // the fetcher was rebuilt (demo ended, account switched): dropped
    [InlineData(false, true, false)]
    public void AnswerApplies_OnlyFromTheCurrentFetcherOutsideDemo(bool sameFetcher, bool demo, bool expected) =>
        Assert.Equal(expected, DemoMode.AnswerApplies(sameFetcher, demo));
}
