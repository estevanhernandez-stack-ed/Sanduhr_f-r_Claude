using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class DrawerPiecesTests
{
    [Theory]
    [InlineData("drawer.screenshots", true)]
    [InlineData("drawer.older-files", true)]
    [InlineData("drawer.", false)]
    [InlineData("drawer.Bad", false)]
    [InlineData("drawers.x", false)]
    [InlineData("clock", false)]
    [InlineData(null, false)]
    public void A_drawer_word_is_the_prefix_and_a_valid_id(string? widget, bool ok) =>
        Assert.Equal(ok, DrawerPieces.IsWord(widget));

    [Fact]
    public void Word_and_IdOf_round_trip()
    {
        Assert.Equal("drawer.screenshots", DrawerPieces.Word("screenshots"));
        Assert.Equal("screenshots", DrawerPieces.IdOf("drawer.screenshots"));
        Assert.Null(DrawerPieces.IdOf("clock"));
    }

    [Fact]
    public void A_layout_keeps_drawer_pieces_beside_the_others()
    {
        var a = new DeskArrangement("clock:bl drawer.screenshots:tl:1.2 drawer.documents:tl");
        Assert.Equal(["clock", "drawer.screenshots", "drawer.documents"], a.Pieces.Select(p => p.Widget));
        Assert.Equal(1.2, a.Pieces[1].Scale);
        Assert.Equal("clock:bl drawer.screenshots:tl:1.2 drawer.documents:tl", a.Layout);
    }

    [Fact]
    public void A_bad_drawer_word_is_dropped_like_any_unknown_word()
    {
        var a = new DeskArrangement("clock:bl drawer.Bad:tl drawer.:tl");
        Assert.Equal(["clock"], a.Pieces.Select(p => p.Widget));
    }

    [Fact]
    public void A_drawer_with_an_unreadable_anchor_lands_top_left() =>
        Assert.Equal(DeskAnchor.TL, new DeskArrangement("drawer.screenshots:zz").Pieces.Single().Anchor);

    [Fact]
    public void Drawer_pieces_sort_after_every_known_widget() =>
        Assert.Equal(DeskArrangement.Known.Count, DeskArrangement.Rank("drawer.screenshots"));
}
