using System.Windows;
using System.Windows.Input;
using Sanduhr.App.Desk;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings v2, slice 3 (spec, Keyboard and screen reader): Arrange Desk without a mouse. Tab picks a piece (its Hide
/// button), the arrows move it between places, Shift+Up and Shift+Down move it in its stack.</summary>
[Collection("SettingsWindow")]
public sealed class DeskArrangeKeysTests : AppWindowFixture
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

    private static KeyEventArgs Press(Window window, Key key) =>
        new(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };

    /// <summary>The key press itself: the Hide button of a piece holds the keyboard, an arrow reaches it as a PreviewKeyDown, and the
    /// event is handled only when the arrow changed something. The modifier is injected (Keyboard.Modifiers reads the real keys).</summary>
    [DesktopFact]
    public void An_arrow_press_on_a_focused_piece_acts_and_is_handled_only_when_it_acted() => AppHost.Run(() =>
    {
        var settings = (DeskSettings.Default with { Enabled = true, Layout = "clock:bl claude:bl" }).Normalized();
        var working = new DeskArrangement(settings.Layout);
        var content = new DeskArrangeContent(Screen(), settings, new DeskFonts(), Frame, working, primary: true);
        var window = new Window { Content = content, Width = 300, Height = 300 };
        var guard = NoActivate.Show(window);
        try
        {
            guard.RouteKeyboard();
            // No piece holds the keyboard: nothing happens.
            var none = Press(window, Key.Up);
            Assert.False(content.RouteArrow(none, ModifierKeys.Shift));
            Assert.False(none.Handled);
            Assert.Equal("clock:bl claude:bl", working.Layout);

            Assert.True(content.TryFocus(DeskArrangeContent.HideKey("claude")));
            guard.Check();
            Assert.True(content.Buttons[DeskArrangeContent.HideKey("claude")].IsKeyboardFocused);

            var up = Press(window, Key.Up);
            Assert.True(content.RouteArrow(up, ModifierKeys.Shift));
            Assert.True(up.Handled);
            Assert.Equal("claude:bl clock:bl", working.Layout);

            // Already on top: acts on nothing, so the key is not handled.
            content.TryFocus(DeskArrangeContent.HideKey("claude"));
            var top = Press(window, Key.Up);
            Assert.False(content.RouteArrow(top, ModifierKeys.Shift));
            Assert.False(top.Handled);

            // Ctrl and Alt pass through, with or without Shift.
            foreach (var mods in new[] { ModifierKeys.Control, ModifierKeys.Alt, ModifierKeys.Windows, ModifierKeys.Control | ModifierKeys.Shift })
            {
                var e = Press(window, Key.Up);
                Assert.False(content.RouteArrow(e, mods));
                Assert.False(e.Handled);
            }
            Assert.Equal("claude:bl clock:bl", working.Layout);

            // The real handler: a plain Right on the focused piece.
            content.TryFocus(DeskArrangeContent.HideKey("clock"));
            var right = Press(window, Key.Right);
            content.RaiseEvent(right);
            guard.Check();
            Assert.Equal(DeskAnchor.BC, working.Placement("clock")!.Anchor);
            Assert.True(right.Handled);
        }
        finally { window.Close(); content.Dispose(); }
    });
}
