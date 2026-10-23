using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Where the Launcher's buttons sit in a row or a column at each size, and which button a point is on.</summary>
public class LauncherLayoutTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var o in new[] { LauncherOrientation.Row, LauncherOrientation.Column })
        foreach (var count in new[] { 1, 2, 5, 8 })
        foreach (var size in new[] { 112 * 0.6, 112.0, 112 * 1.6 })
        foreach (var labels in new[] { false, true })
            yield return [o, count, size, labels];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ButtonsDoNotOverlapAndSitInOrder(LauncherOrientation o, int count, double size, bool labels)
    {
        var g = LauncherLayout.Compute(count, o, size, labels);
        Assert.Equal(count, g.Slots.Count);
        for (int i = 1; i < count; i++)
        {
            var a = g.Slots[i - 1].Frame;
            var b = g.Slots[i].Frame;
            if (o == LauncherOrientation.Row) Assert.True(b.Left > a.Right, "row buttons have a gap");
            else Assert.True(b.Top > a.Bottom, "column buttons have a gap");
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryButtonIsInsideThePieceAndTheImageInsideItsButton(LauncherOrientation o, int count, double size, bool labels)
    {
        var g = LauncherLayout.Compute(count, o, size, labels);
        foreach (var s in g.Slots)
        {
            Assert.True(s.Frame.Left >= 0 && s.Frame.Top >= 0);
            Assert.True(s.Frame.Right <= g.Width + 1e-9 && s.Frame.Bottom <= g.Height + 1e-9);
            Assert.True(s.Image.Left >= s.Frame.Left && s.Image.Right <= s.Frame.Right + 1e-9);
            Assert.True(s.Image.Top >= s.Frame.Top && s.Image.Bottom <= s.Frame.Bottom + 1e-9);
            Assert.Equal(s.Image.Width, s.Image.Height, 6);
            Assert.Equal(labels, s.Label.Height > 0);
            if (labels) Assert.True(s.Label.Top >= s.Image.Bottom && s.Label.Bottom <= s.Frame.Bottom + 1e-9);
        }
    }

    [Fact]
    public void ARowRunsLeftToRightAndAColumnTopToBottomWithTheSameButtonSize()
    {
        var row = LauncherLayout.Compute(3, LauncherOrientation.Row, 112, false);
        var col = LauncherLayout.Compute(3, LauncherOrientation.Column, 112, false);
        Assert.All(row.Slots, s => Assert.Equal(0, s.Frame.Top));
        Assert.All(col.Slots, s => Assert.Equal(0, s.Frame.Left));
        Assert.Equal(row.Slots[0].Frame.Width, col.Slots[0].Frame.Width);
        Assert.Equal(row.Slots[0].Frame.Height, col.Slots[0].Frame.Height);
        Assert.Equal(row.Height, col.Slots[0].Frame.Height, 6);
        Assert.True(row.Width > col.Width && col.Height > row.Height);
    }

    [Fact]
    public void TheLayoutScalesWithTheSize()
    {
        var small = LauncherLayout.Compute(4, LauncherOrientation.Row, 112 * 0.6, true);
        var big = LauncherLayout.Compute(4, LauncherOrientation.Row, 112 * 1.6, true);
        Assert.True(big.ImageSize > small.ImageSize * 2.5);
        Assert.True(big.Width > small.Width * 2.5);
        Assert.Equal(LauncherLayout.ImageShare * 112, LauncherLayout.Compute(1, LauncherOrientation.Row, 112, false).ImageSize, 6);
    }

    [Fact]
    public void LabelsWidenEveryButtonSoImagesLineUp()
    {
        var plain = LauncherLayout.Compute(3, LauncherOrientation.Row, 112, false);
        var labelled = LauncherLayout.Compute(3, LauncherOrientation.Row, 112, true);
        Assert.True(labelled.Slots[0].Frame.Width > plain.Slots[0].Frame.Width);
        Assert.True(labelled.Height > plain.Height);
        Assert.All(labelled.Slots, s => Assert.Equal(labelled.Slots[0].Image.Top, s.Image.Top));
        Assert.True(labelled.LabelSize >= LauncherLayout.MinLabelSize);
    }

    [Fact]
    public void NoButtonsIsAnEmptyPiece()
    {
        var g = LauncherLayout.Compute(0, LauncherOrientation.Row, 112, true);
        Assert.Empty(g.Slots);
        Assert.Equal(0, g.Width);
        Assert.Null(LauncherLayout.HitTest(g, 0, 0));
    }

    [Fact]
    public void MoreThanEightIsCutToEight() =>
        Assert.Equal(8, LauncherLayout.Compute(20, LauncherOrientation.Row, 112, false).Slots.Count);

    [Theory]
    [InlineData(LauncherOrientation.Row)]
    [InlineData(LauncherOrientation.Column)]
    public void HitTestFindsEachButtonAtItsCenterAndNothingInTheGaps(LauncherOrientation o)
    {
        var g = LauncherLayout.Compute(4, o, 112, true);
        for (int i = 0; i < 4; i++)
        {
            var f = g.Slots[i].Frame;
            Assert.Equal(i, LauncherLayout.HitTest(g, f.MidX, f.MidY));
            Assert.Equal(i, LauncherLayout.HitTest(g, f.Left, f.Top));              // the corner is in
            Assert.Null(LauncherLayout.HitTest(g, f.Right, f.Bottom));              // the far corner is out (half-open)
        }
        for (int i = 0; i < 3; i++)
        {
            var a = g.Slots[i].Frame;
            var b = g.Slots[i + 1].Frame;
            double gx = o == LauncherOrientation.Row ? (a.Right + b.Left) / 2 : a.MidX;
            double gy = o == LauncherOrientation.Row ? a.MidY : (a.Bottom + b.Top) / 2;
            Assert.Null(LauncherLayout.HitTest(g, gx, gy));
        }
        Assert.Null(LauncherLayout.HitTest(g, -1, 5));
        Assert.Null(LauncherLayout.HitTest(g, g.Width + 1, 5));
        Assert.Null(LauncherLayout.HitTest(g, 5, g.Height + 1));
    }

    [Theory]
    [InlineData(112 * 0.6)]
    [InlineData(112.0)]
    [InlineData(112 * 1.6)]
    public void TheDeskClickRuleAgreesWithTheLayoutAtEveryScale(double size)
    {
        var g = LauncherLayout.Compute(5, LauncherOrientation.Row, size, true);
        var frames = g.Slots.Select(s => s.Frame).ToList();
        var input = new DeskElementsInput(new HashSet<string> { "launcher" }, LauncherFrames: frames);
        var elements = DeskElements.Build(input);
        Assert.Equal(5, elements.Count);
        Assert.All(elements, e => Assert.Equal(DeskElementKind.LauncherButton, e.Kind));
        Assert.Equal(["0", "1", "2", "3", "4"], elements.Select(e => e.Key));
        for (double x = -4; x < g.Width + 4; x += 1.7)
            for (double y = -4; y < g.Height + 4; y += 3.1)
            {
                var hit = DeskHitTest.Element(x, y, elements);
                var expected = LauncherLayout.HitTest(g, x, y);
                Assert.Equal(expected?.ToString(), hit?.Key);
            }
    }

    [Fact]
    public void ALauncherButtonTakesExactlyItsFrameWhateverTheOtherPiecesSlack()
    {
        Assert.Equal((0d, 0d), DeskHitTest.Slack(DeskElementKind.LauncherButton));
        var button = new DeskElement(DeskElementKind.LauncherButton, "0", new DeskRect(100, 100, 60, 60));
        Assert.Equal(button, DeskHitTest.Element(100, 100, [button]));
        Assert.Null(DeskHitTest.Element(99.5, 130, [button]));
        Assert.Null(DeskHitTest.Element(160, 130, [button]));
        // Beating a neighbour's slack: the button wins where both reach.
        var clock = new DeskElement(DeskElementKind.Clock, null, new DeskRect(100, 40, 200, 56));
        Assert.Equal(button, DeskHitTest.Element(120, 100, [clock, button]));
    }

    [Fact]
    public void AnEmptyLauncherFrameTakesNoClick()
    {
        var empty = new DeskElement(DeskElementKind.LauncherButton, "0", default);
        Assert.Null(DeskHitTest.Element(0, 0, [empty]));
    }

    // #318: a label wraps to two lines and a labelled button widens within a max, so 24 characters draw whole.

    [Fact]
    public void ATwoLineLabelRowIsTwoLinesTallAndNoMore()
    {
        var one = LauncherLayout.Compute(3, LauncherOrientation.Row, 112, true);
        var two = LauncherLayout.Compute(3, LauncherOrientation.Row, 112, true, labelLines: 2);
        var five = LauncherLayout.Compute(3, LauncherOrientation.Row, 112, true, labelLines: 5);
        double line = LauncherLayout.LabelLineHeight(one.LabelSize);

        Assert.Equal(line, one.Slots[0].Label.Height);
        Assert.Equal(2 * line, two.Slots[0].Label.Height);
        Assert.Equal(two.Height, five.Height);           // capped at MaxLabelLines
        Assert.Equal(one.Height + line, two.Height);
        Assert.Equal(one.Width, two.Width);              // more lines, same width
    }

    [Fact]
    public void ALabelledButtonWidensOnlyWithinItsMax()
    {
        var usual = LauncherLayout.Compute(2, LauncherOrientation.Row, 112, true);
        var wide = LauncherLayout.Compute(2, LauncherOrientation.Row, 112, true, 2, labelledWidth: 2.2);
        var widest = LauncherLayout.Compute(2, LauncherOrientation.Row, 112, true, 2, labelledWidth: 9);
        var narrow = LauncherLayout.Compute(2, LauncherOrientation.Row, 112, true, 2, labelledWidth: 0.5);

        Assert.Equal(usual.ImageSize * LauncherLayout.LabelledWidthShare, usual.Slots[0].Frame.Width, 6);
        Assert.Equal(usual.ImageSize * 2.2, wide.Slots[0].Frame.Width, 6);
        Assert.Equal(usual.ImageSize * LauncherLayout.MaxLabelledWidthShare, widest.Slots[0].Frame.Width, 6);
        Assert.Equal(usual.Slots[0].Frame.Width, narrow.Slots[0].Frame.Width, 6);
        // The image stays centered over its wider label.
        Assert.Equal(wide.Slots[0].Frame.MidX, wide.Slots[0].Image.MidX, 6);
        // No labels: the width share does nothing.
        Assert.Equal(LauncherLayout.Compute(2, LauncherOrientation.Row, 112, false).Width,
            LauncherLayout.Compute(2, LauncherOrientation.Row, 112, false, 2, 2.4).Width, 6);
    }
}
