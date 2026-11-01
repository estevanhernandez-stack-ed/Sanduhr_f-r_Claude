using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>Where free drawers sit (drawers amendment 1): arrival, clamping and the saved places.</summary>
public sealed class DrawerPlacementTests
{
    private static DrawerDef Def(string id) => new(id, id, @"C:\x\" + id, true);

    [Fact]
    public void Two_new_drawers_arrive_top_right_one_under_the_other()
    {
        var places = DrawerPlacement.Arrive([Def("a"), Def("b")], new Dictionary<string, DrawerPlace>(), 1920, []);
        Assert.Equal(new DrawerPlace(DeskMonitors.Main, 1536, 24), places["a"]);
        Assert.Equal(new DrawerPlace(DeskMonitors.Main, 1536, 366), places["b"]);
    }

    [Fact]
    public void A_folded_drawer_makes_the_next_start_lower_by_its_own_height()
    {
        var places = DrawerPlacement.Arrive([Def("a"), Def("b")], new Dictionary<string, DrawerPlace>(), 1920, ["a"]);
        Assert.Equal(24 + 42 + 12, places["b"].Y);
    }

    [Fact]
    public void A_drawer_already_in_the_column_pushes_arrivals_below_it()
    {
        var placed = new Dictionary<string, DrawerPlace> { ["old"] = new(DeskMonitors.Main, 1536, 24) };
        var places = DrawerPlacement.Arrive([Def("old"), Def("new")], placed, 1920, []);
        Assert.Equal(new DrawerPlace(DeskMonitors.Main, 1536, 366), places["new"]);
        Assert.Equal(placed["old"], places["old"]);
    }

    [Fact]
    public void A_drawer_placed_elsewhere_does_not_push_arrivals()
    {
        var placed = new Dictionary<string, DrawerPlace> { ["old"] = new(DeskMonitors.Main, 100, 24), ["far"] = new("mon2", 1536, 24) };
        var places = DrawerPlacement.Arrive([Def("old"), Def("far"), Def("new")], placed, 1920, []);
        Assert.Equal(new DrawerPlace(DeskMonitors.Main, 1536, 24), places["new"]);
    }

    [Fact]
    public void Arrive_never_moves_a_placed_drawer()
    {
        var placed = new Dictionary<string, DrawerPlace> { ["a"] = new("mon2", 5, 6) };
        Assert.Equal(new DrawerPlace("mon2", 5, 6), DrawerPlacement.Arrive([Def("a")], placed, 1920, [])["a"]);
    }

    [Fact]
    public void Clamp_pulls_a_drawer_dragged_off_the_right_or_bottom_back_so_its_title_shows()
    {
        var c = DrawerPlacement.Clamp(new DrawerPlace("main", 5000, 5000), 1920, 1080);
        Assert.Equal(new DrawerPlace("main", 1560, 1038), c);
        var n = DrawerPlacement.Clamp(new DrawerPlace("main", -50, -9), 1920, 1080);
        Assert.Equal(new DrawerPlace("main", 0, 0), n);
    }

    [Fact]
    public void Clamp_leaves_an_on_screen_drawer_alone()
    {
        var p = new DrawerPlace("main", 400, 300);
        Assert.Equal(p, DrawerPlacement.Clamp(p, 1920, 1080));
    }

    [Fact]
    public void Places_and_hidden_round_trip()
    {
        var s = DrawerSettings.Default with
        {
            Places = new Dictionary<string, DrawerPlace> { ["images"] = new("main", 1400, 24), ["docs"] = new("mon2", 10.5, 20) },
            Hidden = ["docs"],
        };
        var back = DrawerSettings.Decode(s.Encode());
        Assert.Equal(s.Places.OrderBy(k => k.Key), back.Places.OrderBy(k => k.Key));
        Assert.Equal(["docs"], back.Hidden);
    }

    [Fact]
    public void Bad_entries_drop()
    {
        var o = DrawerSettings.Default.Encode();
        o["places"] = new JsonObject
        {
            ["good"] = new JsonObject { ["monitor"] = "main", ["x"] = 1, ["y"] = 2 },
            ["Bad Id!"] = new JsonObject { ["monitor"] = "main", ["x"] = 1, ["y"] = 2 },
            ["nan"] = new JsonObject { ["monitor"] = "main", ["x"] = "NaN", ["y"] = 2 },
            ["empty"] = new JsonObject { ["monitor"] = "", ["x"] = 1, ["y"] = 2 },
            ["long"] = new JsonObject { ["monitor"] = new string('m', DeskMonitors.MaxIdLength + 1), ["x"] = 1, ["y"] = 2 },
        };
        o["hidden"] = new JsonArray("good", "Bad Id!", "good");
        var back = DrawerSettings.Decode(o);
        Assert.Equal(["good"], back.Places.Keys);
        Assert.Equal(["good"], back.Hidden);
    }

    [Fact]
    public void A_file_with_neither_key_reads_empty()
    {
        var back = DrawerSettings.Decode(new JsonObject { ["enabled"] = true });
        Assert.Empty(back.Places);
        Assert.Empty(back.Hidden);
    }

    [Fact]
    public void Arrival_with_a_tidy_and_icons_takes_the_next_free_slot()
    {
        var drawers = new[] { new DrawerDef("a", "A", @"C:\D\A", true) };
        var icons = new DrawerRect(1920 - 400, 0, 400, 500);
        var places = DrawerPlacement.Arrive(drawers, new Dictionary<string, DrawerPlace>(), 1920, [],
            DrawerTidy.Default, 1032, [icons]);
        var p = places["a"];
        Assert.False(new DrawerRect(p.X, p.Y, DrawerPlacement.Width, DrawerPlacement.OpenHeight).Overlaps(icons));
    }

    [Fact]
    public void Arrival_with_only_icons_blocked_uses_the_default_tidy()
    {
        var icons = new DrawerRect(1920 - 400, 0, 400, 500);
        var p = DrawerPlacement.Arrive([Def("a")], new Dictionary<string, DrawerPlace>(), 1920, [], blocked: [icons])["a"];
        Assert.Equal(new DrawerPlace(DeskMonitors.Main, 1536, 512), p);
    }

    [Fact]
    public void Arrival_from_the_bottom_left_row_takes_the_next_slot_after_a_placed_drawer()
    {
        var tidy = new DrawerTidy(DrawerCorner.BottomLeft, DrawerFlow.Row);
        var placed = new Dictionary<string, DrawerPlace> { ["old"] = new(DeskMonitors.Main, 24, 678) };
        var places = DrawerPlacement.Arrive([Def("old"), Def("new")], placed, 1920, [], tidy, 1032, []);
        Assert.Equal(new DrawerPlace(DeskMonitors.Main, 24 + 360 + 12, 678), places["new"]);
        Assert.Equal(placed["old"], places["old"]);
    }

    [Fact]
    public void A_drawer_with_no_room_arrives_at_the_top_right_and_does_not_throw()
    {
        var all = new DrawerRect(0, 0, 1920, 1032);
        var places = DrawerPlacement.Arrive([Def("a")], new Dictionary<string, DrawerPlace>(), 1920, [],
            DrawerTidy.Default, 1032, [all]);
        Assert.Equal(new DrawerPlace(DeskMonitors.Main, 1536, 24), places["a"]);
    }

    [Fact]
    public void Drawers_with_no_room_do_not_land_on_each_other()
    {
        var all = new DrawerRect(0, 0, 1920, 1032);
        var places = DrawerPlacement.Arrive([Def("a"), Def("b")], new Dictionary<string, DrawerPlace>(), 1920, [],
            DrawerTidy.Default, 1032, [all]);
        Assert.NotEqual(places["a"], places["b"]);
        Assert.Equal(new DrawerPlace(DeskMonitors.Main, 1536, 24), places["a"]);
    }

    [Theory]
    [InlineData(DrawerCorner.BottomRight, 1536)]
    [InlineData(DrawerCorner.BottomLeft, 24)]
    public void A_bottom_corner_with_no_work_height_reads_as_the_top_corner_on_its_side(DrawerCorner corner, double x)
    {
        var places = DrawerPlacement.Arrive([Def("a")], new Dictionary<string, DrawerPlace>(), 1920, [],
            new DrawerTidy(corner, DrawerFlow.Column), blocked: []);
        Assert.Equal(new DrawerPlace(DeskMonitors.Main, x, 24), places["a"]);
    }

    [Fact]
    public void Default_arguments_leave_arrival_exactly_as_it_was()
    {
        var placed = new Dictionary<string, DrawerPlace> { ["old"] = new(DeskMonitors.Main, 1536, 24) };
        var plain = DrawerPlacement.Arrive([Def("old"), Def("n1"), Def("n2")], placed, 1920, ["n1"]);
        var viaNulls = DrawerPlacement.Arrive([Def("old"), Def("n1"), Def("n2")], placed, 1920, ["n1"], null, double.PositiveInfinity, null);
        Assert.Equal(plain.OrderBy(k => k.Key), viaNulls.OrderBy(k => k.Key));
        Assert.Equal(new DrawerPlace(DeskMonitors.Main, 1536, 366), plain["n1"]);
        Assert.Equal(new DrawerPlace(DeskMonitors.Main, 1536, 366 + 42 + 12), plain["n2"]);
    }
}
