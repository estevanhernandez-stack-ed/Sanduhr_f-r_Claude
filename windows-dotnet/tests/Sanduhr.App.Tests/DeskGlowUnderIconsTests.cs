using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Sanduhr.App.Desk;
using Sanduhr.Core;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace Sanduhr.App.Tests;

/// <summary>
/// The Desk's glow spot as the under-icons host copies it (a <see cref="VisualBrush"/> into a <see cref="RenderTargetBitmap"/>): each glow
/// comes out in its own color, and a second glow in its own. Live, the host's copy kept the pill's first stop colors when a glow changed
/// them in place, so every glow under the icons was white (2026-11-05, A/B/A on the dev build); <see cref="DeskView.Glow"/> now gives the
/// pill a new frozen brush per glow. That stale copy needs the island in its own window and does not happen here, so this test pins the
/// colors through the copy path, not the bug itself.
/// </summary>
public sealed class DeskGlowUnderIconsTests
{
    private static DeskScreen Screen()
    {
        var bounds = new PxRect(0, 0, 1920, 1080);
        var work = new PxRect(0, 0, 1920, 1032);
        return new DeskScreen("TEST", bounds, work, 1.0, true, TaskbarClearance.Insets(bounds, work, [], 1.0));
    }

    private static IEnumerable<T> All<T>(DependencyObject node) where T : DependencyObject
    {
        if (node is T hit) yield return hit;
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            foreach (var found in All<T>(child))
                yield return found;
    }

    /// <summary>The pill's center pixel as the under-icons host would copy it.</summary>
    private static Color Copy(Rectangle pill)
    {
        // The host copies on a timer tick, after the dispatcher has run the change through.
        Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, () => { });
        var root = (FrameworkElement)pill.Parent;
        root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        root.Arrange(new Rect(root.DesiredSize));
        int w = (int)Math.Ceiling(root.ActualWidth), h = (int)Math.Ceiling(root.ActualHeight);
        var brush = new VisualBrush(root) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, root.ActualWidth, root.ActualHeight) };
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen()) dc.DrawRectangle(brush, null, new Rect(0, 0, w, h));
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var px = new byte[4];
        rtb.CopyPixels(new Int32Rect(w / 2, h / 2, 1, 1), px, 4, 0);
        return Color.FromArgb(px[3], px[2], px[1], px[0]);
    }

    [Fact]
    public void A_glow_under_the_icons_is_drawn_in_its_color_not_white() => Sta.Run(() =>
    {
        var settings = (DeskSettings.Default with { Enabled = true, Layout = "glow:tc", Font = "Segoe UI" }).Normalized();
        var view = new DeskView(Screen(), settings, new DeskFonts(["Segoe UI"]));
        view.Update(new DeskFrame { Now = new DateTimeOffset(2026, 11, 5, 20, 0, 0, TimeSpan.Zero) }, DeskInkMap.Light);
        var pill = All<Rectangle>(view).Single(r => r.Fill is RadialGradientBrush);

        pill.Opacity = 1;
        Copy(pill);   // the copy before any glow, as the host makes one at every check

        var green = Color.FromRgb(69, 205, 119);
        Assert.True(view.Glow(green));
        pill.BeginAnimation(UIElement.OpacityProperty, null);
        pill.Opacity = 1;

        var center = Copy(pill);
        Assert.True(center.A > 200, $"the copied pill is {center}, not opaque (slot {((FrameworkElement)pill.Parent).ActualWidth}x{((FrameworkElement)pill.Parent).ActualHeight}, pill {pill.ActualWidth}x{pill.ActualHeight})");
        Assert.True(center.G > center.R + 80 && center.G > center.B + 50, $"the copied pill is {center}, not green");

        var violet = Color.FromRgb(100, 92, 235);
        Assert.True(view.Glow(violet));
        pill.BeginAnimation(UIElement.OpacityProperty, null);
        pill.Opacity = 1;
        center = Copy(pill);
        Assert.True(center.B > center.G + 80, $"the second glow's copy is {center}, not violet");
    });
}
