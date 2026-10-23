using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// #319: the design review's Apply / Dismiss bar opened top center on the primary monitor while the design previewed on another.
/// A bar over the Desk opens on the screen that draws the most of what it is about; the primary breaks ties and covers the rest.
/// </summary>
public sealed class DeskBarScreenTests
{
    private const string MainId = @"\\?\DISPLAY#GSM5B09#5&2c9d3a1&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
    private const string SideId = @"\\?\DISPLAY#DEL4167#5&2c9d3a1&0&UID4353#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
    private static readonly string[] Connected = [MainId, SideId];

    // The secondary listed first, as Windows may list them: the rule must not lean on the order.
    private static readonly IReadOnlyList<DeskScreenRef> Screens = [new(SideId, false), new(MainId, true)];

    private static IReadOnlyList<DeskPlacement> Layout(string layout) => new DeskArrangement(layout).Pieces;

    private static string On(string id) => "@" + DeskMonitors.Escape(id);

    [Fact]
    public void The_bar_goes_where_most_of_the_pieces_are()
    {
        Assert.Equal(0, DeskMonitors.BarScreen(Screens, Layout($"clock:tl{On(SideId)} launcher:br{On(SideId)} claude:bl"), Connected));
        Assert.Equal(1, DeskMonitors.BarScreen(Screens, Layout($"clock:tl@main launcher:br@main claude:bl{On(SideId)}"), Connected));
    }

    [Fact]
    public void A_tie_or_nothing_to_go_by_is_the_primary()
    {
        Assert.Equal(1, DeskMonitors.BarScreen(Screens, Layout("clock:tl claude:bl"), Connected));              // every monitor: a tie
        Assert.Equal(1, DeskMonitors.BarScreen(Screens, Layout($"clock:tl{On(SideId)} claude:bl@main"), Connected));
        Assert.Equal(1, DeskMonitors.BarScreen(Screens, [], Connected));
        Assert.Equal(-1, DeskMonitors.BarScreen([], Layout("clock:tl"), Connected));
    }

    [Fact]
    public void A_piece_on_an_unplugged_monitor_counts_on_the_primary_where_it_draws()
    {
        const string gone = @"\\?\DISPLAY#XYZ0000#0&0&0&UID1#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
        Assert.Equal(1, DeskMonitors.BarScreen(Screens, Layout($"clock:tl{On(gone)} launcher:br{On(gone)} claude:bl{On(SideId)}"), Connected));
    }

    [Fact]
    public void A_review_is_about_the_pieces_the_design_moves()
    {
        var current = new DeskArrangement("clock:tl claude:bl meters:br");
        var moved = DeskMonitors.ReviewPieces(current, new DeskArrangement($"clock:tl claude:bl{On(SideId)} meters:br launcher:tr{On(SideId)}"));
        Assert.Equal(["claude", "launcher"], moved.Select(p => p.Widget));

        // Resized counts too; untouched pieces do not.
        Assert.Equal(["clock"], DeskMonitors.ReviewPieces(current, new DeskArrangement("clock:tl:1.4 claude:bl meters:br")).Select(p => p.Widget));

        // A design that leaves the layout as it is (a font, an ink): every piece it places.
        Assert.Equal(["clock", "claude", "meters"], DeskMonitors.ReviewPieces(current, new DeskArrangement("clock:tl claude:bl meters:br")).Select(p => p.Widget));
    }

    [Fact]
    public void The_reported_case_opens_on_the_secondary()
    {
        // Desk on the secondary: every piece placed there; the design previews there.
        var current = new DeskArrangement($"clock:tl{On(SideId)} claude:bl{On(SideId)}");
        var proposed = new DeskArrangement($"clock:tr{On(SideId)} claude:bl{On(SideId)} launcher:br{On(SideId)}");
        Assert.Equal(0, DeskMonitors.BarScreen(Screens, DeskMonitors.ReviewPieces(current, proposed), Connected));
    }
}
