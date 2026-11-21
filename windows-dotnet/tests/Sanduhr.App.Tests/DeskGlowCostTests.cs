using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Sanduhr.App.Desk;
using Sanduhr.Core;
using Xunit.Abstractions;

namespace Sanduhr.App.Tests;

/// <summary>
/// What one copy of the clock's island costs under the desktop icons (5.0 spec 5, section 7): the under-icons host draws each island in
/// software into a bitmap, and a clock redraws every minute. The spec's budget is 8 ms at strength 1.0, measured on the owner's PC.
/// This test asserts nothing about time (another machine would give another answer); it prints the numbers, and the person running it
/// sets <see cref="DeskGlowLayer.UnderIconsOneColor"/> from them. Run it alone with a detailed logger to read them.
/// </summary>
public sealed class DeskGlowCostTests(ITestOutputHelper output)
{
    private const int Warmups = 3;
    private const int Runs = 21;

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    private static DeskScreen Screen()
    {
        var bounds = new PxRect(0, 0, 1920, 1080);
        var work = new PxRect(0, 0, 1920, 1032);
        return new DeskScreen("TEST", bounds, work, 1.0, true, TaskbarClearance.Insets(bounds, work, [], 1.0));
    }

    /// <summary>The clock's island as the Desk builds it at its standard size, with <paramref name="glow"/> applied to the clock.</summary>
    private static FrameworkElement Island(Func<FrameworkElement, FrameworkElement>? glow, double scale)
    {
        var settings = (DeskSettings.Default with { Enabled = true, Layout = "clock:bl", Font = "Segoe UI" }).Normalized();
        var view = new DeskView(Screen(), settings, new DeskFonts(["Segoe UI"]));
        var ink = DeskInk.Resolve(new DeskInkSettings(DeskInkMode.Custom, "8f5bd6,3a63e0,33fdff", false), null, null);
        view.Update(new DeskFrame { Now = new DateTimeOffset(2026, 11, 20, 20, 0, 0, TimeSpan.Zero) }, new DeskInkMap(ink));
        var stack = (Panel)view.TakeIslands().Single().Content;
        if (glow is not null)
        {
            var clock = (FrameworkElement)stack.Children[0];
            stack.Children.RemoveAt(0);
            stack.Children.Insert(0, glow(clock));
        }
        // The host scales the island's root to the monitor: the same transform here, so the bitmap has the monitor's pixels.
        stack.LayoutTransform = new System.Windows.Media.ScaleTransform(scale, scale);
        return stack;
    }

    private static double Median(Func<FrameworkElement> build, double pad)
    {
        var times = new List<double>();
        for (int i = 0; i < Warmups + Runs; i++)
        {
            var content = build();
            long t0 = Stopwatch.GetTimestamp();
            DeskGlowLayerTests.Render(content, pad);
            double ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            if (i >= Warmups) times.Add(ms);
        }
        times.Sort();
        return times[times.Count / 2];
    }

    [Fact]
    public void The_clocks_island_is_timed_with_and_without_the_glow() => Sta.Run(() =>
    {
        double scale = GetDpiForSystem() / 96.0;
        double pad = DeskGlow.Pad(1.0) * scale;
        double none = Median(() => Island(null, scale), DeskGlow.BasePad * scale);
        double copy = Median(() => Island(c => DeskGlowLayer.Wrap(c, 1.0), scale), pad);
        double one = Median(() => Island(c => { c.Effect = DeskGlowLayer.OneColor("3a63e0", 1.0); return c; }, scale), pad);

        output.WriteLine($"GLOW COST at scale {scale:F2}, median of {Runs}: no glow {none:F2} ms, blurred copy {copy:F2} ms, one color {one:F2} ms.");
        output.WriteLine($"GLOW COST budget 8.00 ms: UnderIconsOneColor should be {(copy >= 8.0 ? "true" : "false")}; it is {DeskGlowLayer.UnderIconsOneColor}.");
        Assert.True(none > 0 && copy > 0 && one > 0);
    });
}
