using System.Globalization;
using Sanduhr.App.Theming;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.App.Tests;

/// <summary>
/// A window whose reset has passed (Codex idle for hours, or Claude between a reset and the next fetch) says when it
/// reset, never "Resets in now" or "Today" for a reset days ago, and draws no pace or burn for a window that's over.
/// Found in the 4.8.0 live check.
/// </summary>
public sealed class TierCardPassedResetTests
{
    private static TierCardViewModel Card(string tier, int util, DateTimeOffset resetsAt, DateTimeOffset now)
    {
        var palette = new ThemePalette("obsidian", ThemeCatalog.BuiltIns["obsidian"]);
        var vm = new TierCardViewModel(tier, palette);
        vm.Update(util, resetsAt.ToString("o", CultureInfo.InvariantCulture), null, null, [], palette, SparklineStyle.Classic, now);
        return vm;
    }

    private static string Clock(DateTimeOffset t) => t.ToLocalTime().ToString("h:mm tt", CultureInfo.InvariantCulture);

    [Fact]
    public void A_reset_days_ago_names_its_date_and_drops_the_pace() => Sta.Run(() =>
    {
        var now = new DateTimeOffset(new DateTime(2026, 11, 11, 20, 0, 0, DateTimeKind.Local));
        var reset = new DateTimeOffset(new DateTime(2026, 11, 9, 21, 36, 0, DateTimeKind.Local));
        var vm = Card(TierModel.FiveHour, 0, reset, now);
        Assert.Equal("Reset Nov 9, " + Clock(reset), vm.ResetCountdown);
        Assert.Equal("", vm.ResetDateTime);
        Assert.Equal("", vm.PaceLabel);
        Assert.True(double.IsNaN(vm.PaceFraction));
        Assert.False(vm.HasBurn);
    });

    [Fact]
    public void A_reset_earlier_today_says_reset_at() => Sta.Run(() =>
    {
        var now = new DateTimeOffset(new DateTime(2026, 11, 11, 20, 0, 0, DateTimeKind.Local));
        var reset = new DateTimeOffset(new DateTime(2026, 11, 11, 15, 10, 0, DateTimeKind.Local));
        var vm = Card(TierModel.FiveHour, 0, reset, now);
        Assert.Equal("Reset at " + Clock(reset), vm.ResetCountdown);
        Assert.Equal("", vm.ResetDateTime);
    });

    [Fact]
    public void A_future_reset_still_counts_down() => Sta.Run(() =>
    {
        var now = new DateTimeOffset(new DateTime(2026, 11, 11, 20, 0, 0, DateTimeKind.Local));
        var vm = Card(TierModel.SevenDay, 12, now.AddDays(4).AddHours(20), now);
        Assert.StartsWith("Resets in ", vm.ResetCountdown);
        Assert.NotEqual("", vm.ResetDateTime);
    });
}
