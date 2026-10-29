using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>The layout map's monitors (4.0.1): every monitor at its place in Windows' arrangement, scaled together to fit the map.</summary>
public class DeskMapLayoutTests
{
    private static void Near(double expected, double actual) => Assert.Equal(expected, actual, 3);

    [Fact]
    public void No_monitors_draw_nothing() =>
        Assert.Empty(DeskMapLayout.Fit([]));

    [Fact]
    public void One_monitor_is_the_map_height_tall_as_before()
    {
        var r = Assert.Single(DeskMapLayout.Fit([new PxRect(0, 0, 1920, 1080)]));
        Near(0, r.X);
        Near(0, r.Y);
        Near(DeskMapLayout.MaxHeight, r.Height);
        Near(1920 * DeskMapLayout.MaxHeight / 1080, r.Width);
    }

    [Fact]
    public void Two_side_by_side_share_the_width_cap()
    {
        var rs = DeskMapLayout.Fit([new PxRect(0, 0, 1920, 1080), new PxRect(1920, 0, 1920, 1080)]);
        double k = DeskMapLayout.MaxWidth / 3840.0;
        Near(0, rs[0].X);
        Near(1920 * k, rs[1].X);
        Near(1920 * k, rs[0].Width);
        Near(1080 * k, rs[1].Height);
        Assert.True(rs[1].X + rs[1].Width <= DeskMapLayout.MaxWidth + 1e-9);
    }

    [Fact]
    public void A_monitor_left_of_main_is_shifted_so_the_leftmost_starts_at_zero()
    {
        var rs = DeskMapLayout.Fit([new PxRect(0, 0, 1920, 1080), new PxRect(-1920, 0, 1920, 1080)]);
        Assert.True(rs[1].X < rs[0].X);
        Near(0, rs[1].X);
    }

    [Fact]
    public void Stacked_monitors_keep_the_height_cap()
    {
        var rs = DeskMapLayout.Fit([new PxRect(0, 0, 1920, 1080), new PxRect(0, -1080, 1920, 1080)]);
        Near(0, rs[1].Y);
        Near(DeskMapLayout.MaxHeight / 2, rs[0].Y);
        Near(DeskMapLayout.MaxHeight, rs[0].Y + rs[0].Height);
    }

    [Fact]
    public void Mixed_sizes_and_offsets_keep_their_proportions()
    {
        var rs = DeskMapLayout.Fit([new PxRect(0, 0, 2560, 1440), new PxRect(2560, 180, 1920, 1080)]);
        double k = rs[0].Width / 2560;
        Near(1440 * k, rs[0].Height);
        Near(2560 * k, rs[1].X);
        Near(180 * k, rs[1].Y);
        Near(1920 * k, rs[1].Width);
    }

    [Fact]
    public void Empty_or_degenerate_bounds_never_divide_by_zero()
    {
        var r = Assert.Single(DeskMapLayout.Fit([new PxRect(0, 0, 0, 0)]));
        Assert.True(double.IsFinite(r.Width) && double.IsFinite(r.Height));
    }
}
