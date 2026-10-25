using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>Theme match, "Meters": the usage signal worked into a meter's gradient at the alert thresholds.</summary>
public class MeterSignalTests
{
    [Theory]
    [InlineData(0, MeterLevel.Calm)]
    [InlineData(79.9, MeterLevel.Calm)]
    [InlineData(80, MeterLevel.Warn)]
    [InlineData(94.9, MeterLevel.Warn)]
    [InlineData(95, MeterLevel.Urgent)]
    [InlineData(130, MeterLevel.Urgent)]
    public void The_level_follows_Warn_at_and_Urgent_at(double percent, MeterLevel level) =>
        Assert.Equal(level, MeterSignal.Level(percent, MeterSignal.DefaultWarnAt, MeterSignal.DefaultUrgentAt));

    [Fact]
    public void Custom_thresholds_move_the_levels()
    {
        Assert.Equal(MeterLevel.Calm, MeterSignal.Level(59, 60, 70));
        Assert.Equal(MeterLevel.Warn, MeterSignal.Level(60, 60, 70));
        Assert.Equal(MeterLevel.Urgent, MeterSignal.Level(70, 60, 70));
    }

    [Fact]
    public void Under_Warn_at_the_gradient_is_the_users_exactly() =>
        Assert.Equal(["6c63ff", "e8e4dc"], MeterSignal.Stops(["#6C63FF", "e8e4dc"], MeterLevel.Calm));

    [Fact]
    public void A_two_stop_gradient_gets_yellow_or_red_in_the_middle()
    {
        Assert.Equal(["6c63ff", "facc15", "e8e4dc"], MeterSignal.Stops(["6c63ff", "e8e4dc"], MeterLevel.Warn));
        Assert.Equal(["6c63ff", "f87171", "e8e4dc"], MeterSignal.Stops(["6c63ff", "e8e4dc"], MeterLevel.Urgent));
    }

    [Fact]
    public void A_solid_meter_is_its_color_to_itself_with_the_same_middle_stop()
    {
        Assert.Equal(["9ad7ff", "facc15", "9ad7ff"], MeterSignal.Stops(["9ad7ff"], MeterLevel.Warn));
        Assert.Equal(["9ad7ff"], MeterSignal.Stops(["9ad7ff"], MeterLevel.Calm));
    }

    [Fact]
    public void Three_and_four_stops_take_the_signal_after_their_first_half()
    {
        Assert.Equal(["111111", "222222", "facc15", "333333"], MeterSignal.Stops(["111111", "222222", "333333"], MeterLevel.Warn));
        Assert.Equal(["111111", "222222", "f87171", "333333", "444444"],
            MeterSignal.Stops(["111111", "222222", "333333", "444444"], MeterLevel.Urgent));
    }

    [Fact]
    public void No_usable_color_is_white_as_the_ink_rule_says() =>
        Assert.Equal(["ffffff", "facc15", "ffffff"], MeterSignal.Stops(["nope"], MeterLevel.Warn));

    [Fact]
    public void Only_a_signal_has_a_text_color_and_it_is_the_ramps_own()
    {
        Assert.Null(MeterSignal.Hex(MeterLevel.Calm));
        Assert.Equal("facc15", MeterSignal.Hex(MeterLevel.Warn));
        Assert.Equal("f87171", MeterSignal.Hex(MeterLevel.Urgent));
        Assert.Equal(ThemeCatalog.UsageColor(60), "#" + MeterSignal.WarnHex);
        Assert.Equal(ThemeCatalog.UsageColor(95), "#" + MeterSignal.UrgentHex);
    }
}
