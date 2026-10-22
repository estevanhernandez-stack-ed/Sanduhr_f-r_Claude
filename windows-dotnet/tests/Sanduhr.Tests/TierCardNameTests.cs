using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>What a screen reader reads for a tier card: what the card shows, in words.</summary>
public class TierCardNameTests
{
    [Theory]
    [InlineData("29m", "29 minutes")]
    [InlineData("1m", "1 minute")]
    [InlineData("2h 5m", "2 hours 5 minutes")]
    [InlineData("1h", "1 hour")]
    [InlineData("3d 5h 30m", "3 days 5 hours 30 minutes")]
    [InlineData("now", "now")]
    [InlineData("--", "--")]
    public void DurationsAreSpoken(string shown, string spoken) => Assert.Equal(spoken, TierModel.SpokenDuration(shown));

    [Fact]
    public void ACardReadsLabelValueResetAndPace() =>
        Assert.Equal("Session (5hr), 12%, resets in 29 minutes, on pace",
            TierModel.CardName("Session (5hr)", "12%", "Resets in 29m", "On pace", "", ""));

    [Fact]
    public void BurnAndLocalTokensAreAddedWhenShown() =>
        Assert.Equal("Weekly, 80%, resets in 2 days 4 hours, 12% ahead, At this rate you hit the limit in 5h, 1.5k local tokens since the last refresh",
            TierModel.CardName("Weekly", "80%", "Resets in 2d 4h", "12% ahead", "At this rate you hit the limit in 5h", "+1.5k"));

    [Fact]
    public void ACardWithNoResetTimeLeavesThePartOut() =>
        Assert.Equal("Routines, 3/5", TierModel.CardName("Routines", "3/5", "", "", "", ""));
}
