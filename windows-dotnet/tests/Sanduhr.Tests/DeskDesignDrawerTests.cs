using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>A Desk design never moves, hides or drops a drawer (plan 1's final review, #8).</summary>
public sealed class DeskDesignDrawerTests
{
    private const string WithDrawer = "clock:bl claude:bl drawer.screenshots:tl:1";

    private static DeskDesign D(string json) => DeskDesign.Parse(JsonNode.Parse(json)).Design!;

    private static DeskDesign Current(string layout) =>
        new() { Layout = DeskDesign.FromArrangement(new DeskArrangement(layout)) };

    [Fact]
    public void A_layout_word_can_be_a_drawer()
    {
        Assert.True(DeskArrangement.IsPiece("clock"));
        Assert.True(DeskArrangement.IsPiece("drawer.screenshots"));
        Assert.False(DeskArrangement.IsPiece("drawer."));
        Assert.False(DeskArrangement.IsPiece("folder"));
    }

    [Fact]
    public void Merging_a_proposal_keeps_the_drawers_the_Desk_has()
    {
        var merged = DeskDesign.Merge(Current(WithDrawer), D("""{"layout":{"clock":{"anchor":"tr"}}}"""));
        var s = DeskDesignApply.Settings(DeskSettings.Default with { Layout = WithDrawer }, DeskInkSettings.Default, LauncherModel.Empty, merged);
        var layout = new DeskArrangement(s.Desk.Layout);
        Assert.Equal(DeskAnchor.TR, layout.Placement("clock")!.Anchor);
        Assert.Equal(new DeskPlacement("drawer.screenshots", DeskAnchor.TL, 1), layout.Placement("drawer.screenshots"));
    }

    [Fact]
    public void A_saved_design_from_before_drawers_keeps_them_where_they_are()
    {
        // A library design saved before drawers existed names no drawer at all.
        var saved = D("""{"layout":{"clock":{"anchor":"tc"},"claude":{"hidden":true}}}""");
        var s = DeskDesignApply.Settings(DeskSettings.Default with { Layout = WithDrawer }, DeskInkSettings.Default, LauncherModel.Empty, saved);
        Assert.Equal(DeskAnchor.TL, new DeskArrangement(s.Desk.Layout).Placement("drawer.screenshots")!.Anchor);
    }

    [Fact]
    public void A_design_cannot_move_a_drawer_even_when_its_layout_names_one()
    {
        var moved = new DeskDesign { Layout = DeskDesign.FromArrangement(new DeskArrangement("clock:bl drawer.screenshots:br:1.4")) };
        Assert.Equal("clock:bl drawer.screenshots:tl",  // the canonical word omits a scale of 1
            DeskDesignApply.WithDrawers(DeskDesignApply.Layout(moved.Layout!), WithDrawer));
    }

    [Fact]
    public void The_review_lists_no_change_for_a_drawer_the_design_leaves_alone()
    {
        var current = Current(WithDrawer);
        var lines = DeskDesign.Diff(current, DeskDesign.Merge(current, D("""{"plate":true}""")));
        Assert.DoesNotContain(lines, l => l.Contains("drawer", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Propose_desk_still_refuses_a_drawer_word()
    {
        var parsed = DeskDesign.Parse(JsonNode.Parse("""{"layout":{"drawer.screenshots":{"anchor":"tl"}}}"""));
        Assert.Contains(parsed.Reasons, r => r.Field == "layout.drawer.screenshots");
    }
}
