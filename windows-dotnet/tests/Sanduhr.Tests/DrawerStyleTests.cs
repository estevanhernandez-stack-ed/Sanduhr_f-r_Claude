using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class DrawerStyleTests
{
    [Fact]
    public void An_older_drawers_object_reads_as_todays_look()
    {
        var s = DrawerSettings.Decode(JsonNode.Parse("""{"enabled":true,"drawers":[],"rules":[]}"""));
        Assert.Equal(DrawerLookKind.Solid, s.Look);
        Assert.Equal(55, s.Opacity);
        Assert.Empty(s.Looks);
        Assert.True(s.Snap);
        Assert.True(s.KeepClear);
        Assert.Equal(DrawerTidy.Default, s.Tidy);
    }

    [Fact]
    public void Every_new_key_round_trips()
    {
        var s = DrawerSettings.Default with
        {
            Look = DrawerLookKind.Glass, Opacity = 70, Snap = false, KeepClear = false,
            Looks = new Dictionary<string, DrawerLookKind> { ["images"] = DrawerLookKind.Bare },
            Tidy = new DrawerTidy(DrawerCorner.BottomLeft, DrawerFlow.Row),
        };
        var back = DrawerSettings.Decode(JsonNode.Parse(s.Encode().ToJsonString()));
        Assert.Equal(DrawerLookKind.Glass, back.Look);
        Assert.Equal(70, back.Opacity);
        Assert.False(back.Snap);
        Assert.False(back.KeepClear);
        Assert.Equal(DrawerLookKind.Bare, back.LookFor("images"));
        Assert.Equal(DrawerLookKind.Glass, back.LookFor("documents"));
        Assert.Equal(new DrawerTidy(DrawerCorner.BottomLeft, DrawerFlow.Row), back.Tidy);
    }

    [Theory]
    [InlineData("""{"look":"neon"}""")]
    [InlineData("""{"opacity":5}""")]
    [InlineData("""{"opacity":"x"}""")]
    [InlineData("""{"snap":"yes"}""")]
    [InlineData("""{"tidy":{"corner":"middle","direction":"row"}}""")]
    [InlineData("""{"looks":{"images":"neon","BAD ID":"bare"}}""")]
    public void Each_unreadable_key_takes_its_default_alone(string json)
    {
        var s = DrawerSettings.Decode(JsonNode.Parse(json));
        Assert.Equal(DrawerLookKind.Solid, s.Look);
        Assert.InRange(s.Opacity, DrawerStyle.MinOpacity, DrawerStyle.MaxOpacity);
        Assert.True(s.Snap);
        Assert.Empty(s.Looks);
    }

    [Fact]
    public void An_opacity_out_of_range_is_clamped_and_the_tidy_parts_fall_back_one_by_one()
    {
        Assert.Equal(100, DrawerSettings.Decode(JsonNode.Parse("""{"opacity":140}""")).Opacity);
        var t = DrawerSettings.Decode(JsonNode.Parse("""{"tidy":{"corner":"bl"}}""")).Tidy;
        Assert.Equal(new DrawerTidy(DrawerCorner.BottomLeft, DrawerFlow.Column), t);
    }

    [Fact]
    public void Words_round_trip_and_only_solid_and_glass_have_a_fill()
    {
        foreach (var w in DrawerStyle.LookWords) Assert.Equal(w, DrawerStyle.Word(DrawerStyle.Look(w)!.Value));
        foreach (var w in DrawerStyle.CornerWords) Assert.Equal(w, DrawerStyle.Word(DrawerStyle.Corner(w)!.Value));
        foreach (var w in DrawerStyle.FlowWords) Assert.Equal(w, DrawerStyle.Word(DrawerStyle.Flow(w)!.Value));
        Assert.True(DrawerStyle.HasFill(DrawerLookKind.Glass));
        Assert.False(DrawerStyle.HasFill(DrawerLookKind.Outline));
    }
}
