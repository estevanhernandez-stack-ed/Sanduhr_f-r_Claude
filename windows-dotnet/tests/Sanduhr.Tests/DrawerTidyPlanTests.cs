using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class DrawerTidyPlanTests
{
    private static readonly DrawerRect Work = new(0, 0, 1920, 1032);
    private const double W = DrawerPlacement.Width, H = DrawerPlacement.OpenHeight, F = DrawerPlacement.FoldedHeight,
        M = DrawerPlacement.Margin, G = DrawerPlacement.Gap;

    [Fact]
    public void Top_right_column_stacks_down_from_the_corner()
    {
        var s = DrawerTidyPlan.Slots([("a", H), ("b", F)], Work, DrawerTidy.Default, []);
        Assert.Equal(new DrawerRect(1920 - M - W, M, W, H), s["a"]);
        Assert.Equal(new DrawerRect(1920 - M - W, M + H + G, W, F), s["b"]);
    }

    [Theory]
    [InlineData(DrawerCorner.TopLeft, DrawerFlow.Row)]
    [InlineData(DrawerCorner.BottomLeft, DrawerFlow.Column)]
    [InlineData(DrawerCorner.BottomRight, DrawerFlow.Row)]
    public void Every_corner_starts_at_its_margin(DrawerCorner corner, DrawerFlow flow)
    {
        var first = DrawerTidyPlan.Slots([("a", H)], Work, new DrawerTidy(corner, flow), [])["a"];
        bool left = corner is DrawerCorner.TopLeft or DrawerCorner.BottomLeft;
        bool top = corner is DrawerCorner.TopLeft or DrawerCorner.TopRight;
        Assert.Equal(left ? M : 1920 - M - W, first.X);
        Assert.Equal(top ? M : 1032 - M - H, first.Y);
    }

    [Fact]
    public void Top_left_column_stacks_down_from_the_left_margin()
    {
        var s = DrawerTidyPlan.Slots([("a", H), ("b", F)], Work, new DrawerTidy(DrawerCorner.TopLeft, DrawerFlow.Column), []);
        Assert.Equal(new DrawerRect(M, M, W, H), s["a"]);
        Assert.Equal(new DrawerRect(M, M + H + G, W, F), s["b"]);
    }

    [Fact]
    public void Bottom_left_column_stacks_up_from_the_bottom_margin()
    {
        var s = DrawerTidyPlan.Slots([("a", H), ("b", F)], Work, new DrawerTidy(DrawerCorner.BottomLeft, DrawerFlow.Column), []);
        Assert.Equal(new DrawerRect(M, 1032 - M - H, W, H), s["a"]);
        Assert.Equal(new DrawerRect(M, 1032 - M - H - G - F, W, F), s["b"]);
    }

    [Fact]
    public void Bottom_right_row_runs_left_along_the_bottom_margin()
    {
        var s = DrawerTidyPlan.Slots([("a", H), ("b", H)], Work, new DrawerTidy(DrawerCorner.BottomRight, DrawerFlow.Row), []);
        Assert.Equal(new DrawerRect(1920 - M - W, 1032 - M - H, W, H), s["a"]);
        Assert.Equal(new DrawerRect(1920 - M - W - G - W, 1032 - M - H, W, H), s["b"]);
    }

    [Fact]
    public void Top_right_row_runs_left_along_the_top_margin()
    {
        var s = DrawerTidyPlan.Slots([("a", H), ("b", F)], Work, new DrawerTidy(DrawerCorner.TopRight, DrawerFlow.Row), []);
        Assert.Equal(new DrawerRect(1920 - M - W, M, W, H), s["a"]);
        Assert.Equal(new DrawerRect(1920 - M - W - G - W, M, W, F), s["b"]);
    }

    [Fact]
    public void A_full_column_wraps_toward_the_centre()
    {
        var s = DrawerTidyPlan.Slots([("a", H), ("b", H), ("c", H)], Work, DrawerTidy.Default, []);
        Assert.Equal(1920 - M - W - W - G, s["c"].X);   // two open drawers fill 1032; the third starts a new column to the left
        Assert.Equal(M, s["c"].Y);
    }

    [Fact]
    public void A_full_column_on_the_left_wraps_right()
    {
        var s = DrawerTidyPlan.Slots([("a", H), ("b", H), ("c", H)], Work, new DrawerTidy(DrawerCorner.TopLeft, DrawerFlow.Column), []);
        Assert.Equal(new DrawerRect(M + W + G, M, W, H), s["c"]);
    }

    [Fact]
    public void A_full_row_wraps_down_by_the_tallest_drawer_in_it()
    {
        // Five 360 wide drawers fit across 24..1896 (x = 1536, 1164, 792, 420, 48); the sixth starts the next row.
        var ordered = new List<(string, double)> { ("a", F), ("b", H), ("c", H), ("d", H), ("e", H), ("f", H) };
        var s = DrawerTidyPlan.Slots(ordered, Work, new DrawerTidy(DrawerCorner.TopRight, DrawerFlow.Row), []);
        Assert.Equal(48, s["e"].X);
        Assert.Equal(M, s["e"].Y);
        Assert.Equal(new DrawerRect(1920 - M - W, M + H + G, W, H), s["f"]);   // the row's tallest is H, not the folded first
    }

    [Fact]
    public void A_full_row_at_the_bottom_wraps_up()
    {
        var ordered = new List<(string, double)> { ("a", H), ("b", H), ("c", H), ("d", H), ("e", H), ("f", H) };
        var s = DrawerTidyPlan.Slots(ordered, Work, new DrawerTidy(DrawerCorner.BottomRight, DrawerFlow.Row), []);
        Assert.Equal(new DrawerRect(1920 - M - W, 1032 - M - H - G - H, W, H), s["f"]);
    }

    [Fact]
    public void Slots_skip_the_icon_area()
    {
        var icons = new DrawerRect(1920 - 400, 0, 400, 500);
        var s = DrawerTidyPlan.Slots([("a", H)], Work, DrawerTidy.Default, [icons]);
        Assert.False(s["a"].Overlaps(icons));
    }

    [Fact]
    public void Slots_never_overlap_each_other_or_the_icons()
    {
        var icons = new List<DrawerRect> { new(0, 0, 228, 1032), new(1700, 300, 100, 300) };
        var ordered = Enumerable.Range(0, 8).Select(i => ($"d{i}", i % 3 == 0 ? F : H)).ToList();
        foreach (var corner in Enum.GetValues<DrawerCorner>())
            foreach (var flow in Enum.GetValues<DrawerFlow>())
            {
                var s = DrawerTidyPlan.Slots(ordered, Work, new DrawerTidy(corner, flow), icons);
                var all = s.Values.ToList();
                for (int i = 0; i < all.Count; i++)
                {
                    Assert.DoesNotContain(icons, ic => all[i].Overlaps(ic));
                    for (int j = i + 1; j < all.Count; j++) Assert.False(all[i].Overlaps(all[j]), $"{corner} {flow}");
                    Assert.True(all[i].X >= M - 0.5 && all[i].Right <= 1920 - M + 0.5 && all[i].Y >= M - 0.5 && all[i].Bottom <= 1032 - M + 0.5);
                }
            }
    }

    [Fact]
    public void A_drawer_that_cannot_fit_anywhere_is_left_out()
    {
        var tiny = new DrawerRect(0, 0, W + 2 * M, H + 2 * M);
        var s = DrawerTidyPlan.Slots([("a", H), ("b", H)], tiny, DrawerTidy.Default, []);
        Assert.True(s.ContainsKey("a"));
        Assert.False(s.ContainsKey("b"));
    }

    [Fact]
    public void A_work_area_narrower_than_a_drawer_leaves_everything_out()
    {
        foreach (var flow in Enum.GetValues<DrawerFlow>())
            Assert.Empty(DrawerTidyPlan.Slots([("a", H)], new DrawerRect(0, 0, 200, 1032), new DrawerTidy(DrawerCorner.BottomLeft, flow), []));
    }

    [Fact]
    public void An_area_blocked_end_to_end_ends_with_nothing_placed_and_no_hang()
    {
        foreach (var corner in Enum.GetValues<DrawerCorner>())
            foreach (var flow in Enum.GetValues<DrawerFlow>())
                Assert.Empty(DrawerTidyPlan.Slots([("a", H), ("b", F)], Work, new DrawerTidy(corner, flow), [Work]));
    }

    [Fact]
    public void Many_icons_are_stepped_past_without_giving_up()
    {
        // 30 icon cells fill x 0..228 down the whole height; the left column must wrap clear of them.
        var icons = Enumerable.Range(0, 30).Select(i => new DrawerRect(i / 10 * 76, i % 10 * 100, 76, 100)).ToList();
        var s = DrawerTidyPlan.Slots([("a", H)], Work, new DrawerTidy(DrawerCorner.TopLeft, DrawerFlow.Column), icons);
        Assert.Equal(M + W + G, s["a"].X);
        Assert.Equal(M, s["a"].Y);
    }

    [Fact]
    public void Order_puts_the_drawer_nearest_the_corner_first()
    {
        var current = new Dictionary<string, DrawerRect>
        {
            ["far"] = new(100, 600, W, H),
            ["near"] = new(1500, 30, W, H),
        };
        Assert.Equal(["near", "far"], DrawerTidyPlan.Order(current, Work, DrawerCorner.TopRight));
    }

    [Fact]
    public void Order_breaks_ties_by_id_and_follows_the_corner()
    {
        var current = new Dictionary<string, DrawerRect>
        {
            ["b"] = new(100, 100, W, H),
            ["a"] = new(100, 100, W, H),
            ["low"] = new(100, 600, W, H),
        };
        Assert.Equal(["a", "b", "low"], DrawerTidyPlan.Order(current, Work, DrawerCorner.TopLeft));
        Assert.Equal(["low", "a", "b"], DrawerTidyPlan.Order(current, Work, DrawerCorner.BottomLeft));
    }
}
