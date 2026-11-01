using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class DrawerSnapTests
{
    private static readonly DrawerRect Work = new(0, 0, 1920, 1032);
    private static DrawerRect D(double x, double y, double h = DrawerPlacement.OpenHeight) => new(x, y, DrawerPlacement.Width, h);

    [Fact]
    public void A_drawer_near_another_lines_up_and_takes_the_gap()
    {
        var other = D(1000, 100);
        var s = DrawerSnap.Snap(D(1005, 100 + DrawerPlacement.OpenHeight + 20), Work, [other], []);
        Assert.Equal(1000, s.X);                                                   // left edges align
        Assert.Equal(100 + DrawerPlacement.OpenHeight + DrawerPlacement.Gap, s.Y); // gap snaps to 12
    }

    [Fact]
    public void Drawers_win_over_pieces_and_the_screen()
    {
        var other = D(1543, 300);
        var piece = new DrawerRect(1540, 700, 200, 100);
        var s = DrawerSnap.Snap(D(1536, 300), Work, [other], [piece]);
        Assert.Equal(1543, s.X);
    }

    [Fact]
    public void A_drawer_docks_beside_a_desk_piece_at_the_gap()
    {
        var clock = new DrawerRect(24, 700, 400, 200);
        var s = DrawerSnap.Snap(D(430, 705), Work, [], [clock]);
        Assert.Equal(clock.Right + DrawerPlacement.Gap, s.X);
        Assert.Equal(700, s.Y);   // tops align
    }

    [Fact]
    public void Near_a_screen_edge_it_snaps_to_the_margin()
    {
        var s = DrawerSnap.Snap(D(1920 - 360 - 30, 30), Work, [], []);
        Assert.Equal(1920 - DrawerPlacement.Margin - DrawerPlacement.Width, s.X);
        Assert.Equal(DrawerPlacement.Margin, s.Y);
    }

    [Fact]
    public void With_nothing_near_it_lands_on_the_grid()
    {
        var s = DrawerSnap.Snap(D(803, 501), Work, [], []);
        Assert.Equal(0, s.X % DrawerSnap.Grid);
        Assert.Equal(0, s.Y % DrawerSnap.Grid);
    }

    [Fact]
    public void Beyond_the_threshold_nothing_but_the_grid_applies()
    {
        var other = D(1000, 100);
        var s = DrawerSnap.Snap(D(1000 + 40, 600), Work, [other], []);
        Assert.Equal(1040, s.X);   // 40 away: the grid, already on it
    }

    [Fact]
    public void Keep_brings_a_drawer_dragged_off_screen_back_by_its_title()
    {
        var k = DrawerSnap.Keep(D(1900, 1100), Work);
        Assert.Equal(1920 - DrawerPlacement.Width, k.X);
        Assert.Equal(1032 - DrawerPlacement.FoldedHeight, k.Y);
    }

    [Fact]
    public void Touching_edges_do_not_overlap()
    {
        Assert.False(new DrawerRect(0, 0, 10, 10).Overlaps(new DrawerRect(10, 0, 10, 10)));
        Assert.True(new DrawerRect(0, 0, 10, 10).Overlaps(new DrawerRect(9, 9, 10, 10)));
    }
}
