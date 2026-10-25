using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Settings opens inside its monitor's work area (spec, "Short windows"; WF41), in DIPs.</summary>
public class WindowFitTests
{
    private static WindowFit.Area Fit(double left, double top, double width, double height) =>
        WindowFit.Fit(new(left, top, width, height), width: 780, height: 800, minWidth: 680, minHeight: 520);

    private static void AssertArea(WindowFit.Area expected, WindowFit.Area actual)
    {
        Assert.Equal(expected.Left, actual.Left, 6);
        Assert.Equal(expected.Top, actual.Top, 6);
        Assert.Equal(expected.Width, actual.Width, 6);
        Assert.Equal(expected.Height, actual.Height, 6);
    }

    [Fact]
    public void A_roomy_screen_gets_the_designed_size_centred() =>
        Assert.Equal(new WindowFit.Area(570, 120, 780, 800), Fit(0, 0, 1920, 1040));

    [Fact]
    public void A_1366x768_laptop_gets_the_whole_work_height() =>
        Assert.Equal(new WindowFit.Area(293, 0, 780, 728), Fit(0, 0, 1366, 728));

    [Fact]
    public void A_1080p_screen_at_150_percent_fits_its_693_DIP_work_area()
    {
        var fit = Fit(0, 0, 1280, 1040 / 1.5);
        Assert.Equal(1040 / 1.5, fit.Height, 3);
        Assert.Equal(0, fit.Top);
        Assert.Equal(250, fit.Left);
    }

    [Fact]
    public void Below_the_minimum_the_top_stays_on_screen() =>
        Assert.Equal(new WindowFit.Area(0, 0, 680, 520), Fit(0, 0, 640, 480));

    [Fact]
    public void A_monitor_left_of_the_main_one_keeps_its_offset() =>
        Assert.Equal(new WindowFit.Area(-1350, 120, 780, 800), Fit(-1920, 0, 1920, 1040));

    // Work area smaller than the minimum.

    [Fact]
    public void A_work_area_too_narrow_only_for_the_width_keeps_the_minimum_width_flush_left() =>
        Assert.Equal(new WindowFit.Area(0, 120, 680, 800), Fit(0, 0, 600, 1040));

    [Fact]
    public void A_work_area_too_short_only_for_the_height_keeps_the_minimum_height_flush_top() =>
        Assert.Equal(new WindowFit.Area(570, 0, 780, 520), Fit(0, 0, 1920, 500));

    [Fact]
    public void A_work_area_exactly_the_minimum_fills_it() =>
        Assert.Equal(new WindowFit.Area(0, 0, 680, 520), Fit(0, 0, 680, 520));

    [Fact]
    public void A_work_area_a_hair_under_the_minimum_still_gets_the_minimum_anchored_at_its_origin() =>
        AssertArea(new WindowFit.Area(0, 0, 680, 520), Fit(0, 0, 679.5, 519.5));

    [Fact]
    public void A_work_area_taller_than_the_minimum_but_shorter_than_designed_takes_all_its_height() =>
        Assert.Equal(new WindowFit.Area(293, 0, 780, 600), Fit(0, 0, 1366, 600));

    // Non-zero work-area origin.

    [Fact]
    public void A_taskbar_on_the_left_shifts_the_centre_by_its_width() =>
        Assert.Equal(new WindowFit.Area(594, 120, 780, 800), Fit(48, 0, 1872, 1040));

    [Fact]
    public void A_taskbar_on_top_shifts_the_window_down_by_its_height() =>
        Assert.Equal(new WindowFit.Area(570, 140, 780, 800), Fit(0, 40, 1920, 1000));

    [Fact]
    public void A_monitor_above_the_main_one_keeps_its_negative_top() =>
        Assert.Equal(new WindowFit.Area(570, -920, 780, 800), Fit(0, -1040, 1920, 1040));

    [Fact]
    public void A_short_work_area_at_a_negative_origin_starts_at_its_top_edge() =>
        Assert.Equal(new WindowFit.Area(-1350, -1040, 780, 728), Fit(-1920, -1040, 1920, 728));

    [Fact]
    public void Below_the_minimum_at_a_non_zero_origin_the_window_anchors_to_the_work_area_corner() =>
        Assert.Equal(new WindowFit.Area(-1000, 50, 680, 520), Fit(-1000, 50, 640, 480));

    // Fractional DIPs (125, 150, 175 percent scaling).

    [Fact]
    public void A_1080p_screen_at_125_percent_centres_on_whole_and_fractional_halves()
    {
        // 1920 x 1040 px work area / 1.25 = 1536 x 832 DIPs.
        Assert.Equal(new WindowFit.Area(378, 16, 780, 800), Fit(0, 0, 1920 / 1.25, 1040 / 1.25));
    }

    [Fact]
    public void A_1080p_screen_at_175_percent_gets_a_fractional_height_and_a_fractional_centre()
    {
        double w = 1920 / 1.75, h = 1040 / 1.75;
        var fit = Fit(0, 0, w, h);
        AssertArea(new WindowFit.Area((w - 780) / 2, 0, 780, h), fit);
        Assert.Equal(594.285714, fit.Height, 6);
        Assert.Equal(158.571429, fit.Left, 6);
    }

    [Fact]
    public void A_fractional_origin_on_a_scaled_second_monitor_is_kept_exactly()
    {
        double left = -1920 / 1.75, top = 24 / 1.75, w = 1920 / 1.75, h = 1016 / 1.75;
        var fit = Fit(left, top, w, h);
        AssertArea(new WindowFit.Area(left + (w - 780) / 2, top, 780, h), fit);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(1.75)]
    [InlineData(2.0)]
    [InlineData(2.5)]
    public void The_foot_and_left_edge_stay_inside_the_work_area_at_every_scale_whenever_the_minimum_fits(double scale)
    {
        foreach (var (pxW, pxH) in new[] { (1366, 728), (1536, 824), (1920, 1040), (2560, 1400), (3840, 2120) })
        {
            foreach (var (originX, originY) in new[] { (0.0, 0.0), (-pxW / scale, 0.0), (48 / scale, 0.0), (0.0, 40 / scale), (0.0, -pxH / scale) })
            {
                var work = new WindowFit.Area(originX, originY, pxW / scale, pxH / scale);
                var fit = WindowFit.Fit(work, 780, 800, 680, 520);

                Assert.True(fit.Width >= 680 && fit.Height >= 520, "never below the minimum");
                Assert.True(fit.Width <= 780 && fit.Height <= 800, "never above the designed size");
                Assert.True(fit.Left >= work.Left - 1e-9, $"left edge off screen at {scale}x {pxW}x{pxH}");
                Assert.True(fit.Top >= work.Top - 1e-9, $"top edge off screen at {scale}x {pxW}x{pxH}");

                if (work.Width >= 680 && work.Height >= 520)
                {
                    Assert.True(fit.Left + fit.Width <= work.Left + work.Width + 1e-9, $"right edge off screen at {scale}x {pxW}x{pxH}");
                    Assert.True(fit.Top + fit.Height <= work.Top + work.Height + 1e-9, $"foot off screen at {scale}x {pxW}x{pxH}");
                }
            }
        }
    }
}
