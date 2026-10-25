using Sanduhr.App.Desk;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings v2, slice 3 (spec, Keyboard and screen reader): Arrange Desk without a mouse. Tab picks a piece (its Hide
/// button), the arrows move it between places, Shift+Up and Shift+Down move it in its stack.</summary>
public sealed class DeskArrangeKeysTests
{
    private static DeskScreen Screen() =>
        new("DISPLAY1", new PxRect(0, 0, 1920, 1080), new PxRect(0, 0, 1920, 1032), 1.0, true,
            TaskbarClearance.Insets(new PxRect(0, 0, 1920, 1080), new PxRect(0, 0, 1920, 1032), [], 1.0), "main");

    private static readonly DeskFrame Frame = new() { Now = new DateTimeOffset(2026, 10, 25, 10, 9, 0, TimeSpan.Zero) };

    [Fact]
    public void Arrows_move_the_piece_and_Shift_arrows_reorder_its_stack() => Sta.Run(() =>
    {
        var settings = (DeskSettings.Default with { Enabled = true, Layout = "clock:bl claude:bl" }).Normalized();
        var working = new DeskArrangement(settings.Layout);
        var content = new DeskArrangeContent(Screen(), settings, new DeskFonts(), Frame, working, primary: true);
        var focusAfter = new List<string>();
        content.Toggled += focusAfter.Add;

        Assert.True(content.HandleArrow("claude", "Up", shift: true));
        Assert.Equal("claude:bl clock:bl", working.Layout);
        Assert.False(content.HandleArrow("claude", "Up", shift: true));      // already on top: nothing changes
        Assert.True(content.HandleArrow("clock", "Right", shift: false));
        Assert.Equal(DeskAnchor.BC, working.Placement("clock")!.Anchor);
        Assert.False(content.HandleArrow("clock", "Down", shift: false));    // the screen's edge
        Assert.False(content.HandleArrow("clock", "Tab", shift: false));     // not an arrow
        Assert.False(content.HandleArrow("message", "Left", shift: false));  // not placed
        Assert.Equal([DeskArrangeContent.HideKey("claude"), DeskArrangeContent.HideKey("clock")], focusAfter);
        content.Dispose();
    });
}
