using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class DrawerIconAreaTests
{
    private static readonly DrawerRect Work = new(0, 0, 1920, 1032);

    [Fact]
    public void Icons_become_blocked_areas_with_a_margin()
    {
        var a = DrawerIconArea.From([new DrawerRect(10, 10, 76, 86)], 8);
        Assert.Equal(new DrawerRect(2, 2, 92, 102), Assert.Single(a));
    }

    [Fact]
    public void The_fallback_is_the_left_columns_the_items_need()
    {
        var a = Assert.Single(DrawerIconArea.Fallback(Work, itemCount: 25, cellW: 76, cellH: 100));
        Assert.Equal(0, a.X);
        Assert.Equal(3 * 76, a.W);   // 10 rows fit in 1032, so 25 items need 3 columns
    }

    [Fact]
    public void No_items_means_no_fallback_area()
    {
        Assert.Empty(DrawerIconArea.Fallback(Work, 0, 76, 100));
    }

    [Fact]
    public void A_drop_on_the_icons_slides_to_the_nearest_free_spot_on_screen()
    {
        var icons = new DrawerRect(0, 0, 300, 1032);
        var r = new DrawerRect(100, 200, DrawerPlacement.Width, DrawerPlacement.OpenHeight);
        var free = DrawerIconArea.NearestFree(r, Work, [icons]);
        Assert.False(free.Overlaps(icons));
        Assert.Equal(icons.Right + DrawerPlacement.Gap, free.X);
        Assert.Equal(200, free.Y);
    }

    [Fact]
    public void A_free_drop_stays_where_it_is()
    {
        var r = new DrawerRect(800, 200, DrawerPlacement.Width, DrawerPlacement.OpenHeight);
        Assert.Equal(r, DrawerIconArea.NearestFree(r, Work, [new DrawerRect(0, 0, 300, 1032)]));
    }

    [Fact]
    public void A_low_drop_on_the_icons_slides_beside_them_and_keeps_its_height()
    {
        // A drawer may hang below the bottom (only its title bar must stay), so a low drop slides sideways, not up.
        var r = new DrawerRect(100, 800, DrawerPlacement.Width, DrawerPlacement.OpenHeight);
        var free = DrawerIconArea.NearestFree(r, Work, [new DrawerRect(0, 0, 300, 1032)]);
        Assert.Equal(new DrawerRect(312, 800, DrawerPlacement.Width, DrawerPlacement.OpenHeight), free);
    }

    [Fact]
    public void An_L_shaped_area_is_cleared_on_both_sides()
    {
        var column = new DrawerRect(0, 0, 300, 1032);
        var bar = new DrawerRect(0, 0, 1920, 200);
        var r = new DrawerRect(100, 100, DrawerPlacement.Width, DrawerPlacement.OpenHeight);
        var free = DrawerIconArea.NearestFree(r, Work, [column, bar]);
        Assert.False(free.Overlaps(column) || free.Overlaps(bar));
        Assert.Equal(new DrawerRect(312, 212, DrawerPlacement.Width, DrawerPlacement.OpenHeight), free);
    }

    [Fact]
    public void A_drop_that_would_slide_off_screen_picks_the_other_side()
    {
        // Icons fill the right edge; sliding right of them is off screen, so the drawer goes left of them.
        var icons = new DrawerRect(1500, 0, 420, 1032);
        var r = new DrawerRect(1600, 200, DrawerPlacement.Width, DrawerPlacement.OpenHeight);
        var free = DrawerIconArea.NearestFree(r, Work, [icons]);
        Assert.Equal(icons.X - DrawerPlacement.Gap - DrawerPlacement.Width, free.X);
        Assert.True(free.Right <= Work.Right && !free.Overlaps(icons));
    }

    [Fact]
    public void When_nothing_is_free_the_drop_is_only_kept_on_screen()
    {
        var r = new DrawerRect(5000, 5000, DrawerPlacement.Width, DrawerPlacement.OpenHeight);
        var free = DrawerIconArea.NearestFree(r, Work, [Work]);
        Assert.Equal(DrawerSnap.Keep(r, Work), free);
        Assert.True(free.X >= 0 && free.Right <= Work.Right && free.Y >= 0 && free.Y < Work.Bottom);
    }

    [Fact]
    public void An_off_screen_drop_that_lands_on_icons_still_ends_off_them()
    {
        // Kept on screen first, then moved off the icons the keep put it on.
        var icons = new DrawerRect(1500, 0, 420, 400);
        var r = new DrawerRect(5000, 100, DrawerPlacement.Width, DrawerPlacement.OpenHeight);
        var free = DrawerIconArea.NearestFree(r, Work, [icons]);
        Assert.False(free.Overlaps(icons));
        Assert.True(free.X >= 0 && free.Right <= Work.Right && free.Y >= 0 && free.Bottom <= Work.Bottom);
    }
}
