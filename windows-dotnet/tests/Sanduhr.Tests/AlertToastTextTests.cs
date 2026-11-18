using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>The limit alert's toast says exactly what it said in 4.9 (pinned before the toast moved, 5.0 spec 4).</summary>
public class AlertToastTextTests
{
    private static AlertEvent E(AlertKind kind, int pct) => new(kind, "seven_day", pct, null);

    [Fact]
    public void Full() =>
        Assert.Equal(("Weekly at 100%", "Limit reached."), AlertToastText.For(E(AlertKind.Full, 100), "Weekly"));

    [Fact]
    public void Urgent() =>
        Assert.Equal(("Weekly at 95%", "Nearly out of headroom."), AlertToastText.For(E(AlertKind.Urgent, 95), "Weekly"));

    [Fact]
    public void Warn_with_no_reset_time_has_no_body() =>
        Assert.Equal(("Weekly at 80%", ""), AlertToastText.For(E(AlertKind.Warn, 80), "Weekly"));

    [Fact]
    public void Projection() =>
        Assert.Equal(("Codex Session (5hr) on pace to hit the cap", "Current burn rate exhausts this tier before it resets."),
            AlertToastText.For(E(AlertKind.Projection, 60), "Codex Session (5hr)"));

    [Fact]
    public void Reset_carries_the_previous_peak() =>
        Assert.Equal(("Weekly reset", "Fresh window after peaking at 80%."), AlertToastText.For(E(AlertKind.Reset, 80), "Weekly"));
}
