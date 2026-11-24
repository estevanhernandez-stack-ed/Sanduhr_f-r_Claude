using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Sanduhr.App.Desk;
using Sanduhr.Core;
using Rectangle = System.Windows.Shapes.Rectangle;
using Xunit.Abstractions;

namespace Sanduhr.App.Tests;

/// <summary>The neon glow's drawing helper (5.0 spec 5, section 7): a blurred copy of a piece behind it, or one color where the copy
/// costs too much.</summary>
public sealed class DeskGlowLayerTests(ITestOutputHelper output)
{
    private static ResolvedInk Ink(double glow, bool oneColor = false) =>
        DeskInk.Resolve(new DeskInkSettings(DeskInkMode.Custom, "8f5bd6,3a63e0,33fdff", false, glow > 0, glow > 0 ? glow : 0.6), null, null)
            with { GlowOneColor = oneColor };

    private static TextBlock Piece() => new()
    {
        Text = "12:34", FontSize = 100, FontWeight = FontWeights.Bold, FontFamily = new FontFamily("Segoe UI"),
        Foreground = DeskView.InkBrush(["8f5bd6", "33fdff"]), HorizontalAlignment = HorizontalAlignment.Right, Tag = "clock",
    };

    /// <summary>The element as the under-icons host copies it: laid out in a padded root, drawn through a brush into a bitmap.</summary>
    internal static (byte[] Px, int W, int H) Render(FrameworkElement content, double pad)
    {
        if (content.Parent is Border was) was.Child = null;
        var root = new Border { Child = content, Padding = new Thickness(pad), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        root.Arrange(new Rect(root.DesiredSize));
        root.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, () => { });
        int w = (int)Math.Ceiling(root.ActualWidth), h = (int)Math.Ceiling(root.ActualHeight);
        var brush = new VisualBrush(root) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, root.ActualWidth, root.ActualHeight) };
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen()) dc.DrawRectangle(brush, null, new Rect(0, 0, w, h));
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var px = new byte[w * h * 4];
        rtb.CopyPixels(px, w * 4, 0);
        return (px, w, h);
    }

    /// <summary>Whether this machine's bitmap renderer (<see cref="RenderTargetBitmap"/>, WPF's software path) draws the effect: a 40 by 40
    /// white square under it, any pixel 6 beyond the square's edge with alpha above 0. The hosted Arm runner drew no blur here (CI, 2026-11-21).</summary>
    private static bool BitmapDraws(Effect effect)
    {
        const double pad = 20, side = 40;
        var square = new Rectangle { Width = side, Height = side, Fill = Brushes.White, Effect = effect };
        var (px, w, h) = Render(square, pad);
        int x = (int)(pad - 6), y = h / 2;
        int[] probes = [(y * w + x) * 4 + 3, (y * w + (int)(pad + side + 6)) * 4 + 3, (x * w + w / 2) * 4 + 3, ((int)(pad + side + 6) * w + w / 2) * 4 + 3];
        return probes.Any(i => px[i] > 0);
    }

    private static bool DrawsBlur() { bool r = false; Sta.Run(() => r = BitmapDraws(new BlurEffect { Radius = 10 })); return r; }

    private static bool DrawsDropShadow() { bool r = false; Sta.Run(() => r = BitmapDraws(new DropShadowEffect { ShadowDepth = 0, BlurRadius = 10, Color = Colors.White, Opacity = 1 })); return r; }

    /// <summary>Never fails: it says what this machine's bitmap renderer does, so a CI log tells us what the Arm runner does.</summary>
    [Fact]
    public void Effects_in_a_bitmap_are_reported() =>
        output.WriteLine($"EFFECTS IN BITMAP: blur {DrawsBlur()}, drop shadow {DrawsDropShadow()}, process architecture {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");

    private const string NoBlur = "this machine's bitmap renderer does not draw BlurEffect (seen on the Arm runner)";

    [Fact]
    public void No_glow_leaves_the_piece_alone() => Sta.Run(() =>
    {
        var piece = Piece();
        Assert.Same(piece, DeskGlowLayer.On(piece, Ink(0)));
        Assert.Null(piece.Effect);
        Assert.Equal("clock", piece.Tag);
    });

    [Fact]
    public void The_wrapper_takes_the_pieces_tag_and_alignment_and_puts_the_copy_under_it() => Sta.Run(() =>
    {
        var piece = Piece();
        var host = Assert.IsType<GlowHost>(DeskGlowLayer.On(piece, Ink(0.8)));

        Assert.Equal("clock", host.Tag);
        Assert.Null(piece.Tag);   // one tagged element, so the Desk's clicks find one clock
        Assert.Equal(HorizontalAlignment.Right, host.HorizontalAlignment);
        Assert.Equal(2, host.Children.Count);
        var copy = Assert.IsType<Rectangle>(host.Children[0]);
        Assert.Same(piece, host.Children[1]);
        Assert.Null(piece.Effect);   // the glow replaces the shadow
        Assert.False(copy.IsHitTestVisible);
        Assert.Equal(DeskGlow.Opacity(0.8), copy.Opacity, 6);
        Assert.Equal(DeskGlow.Radius(0.8), Assert.IsType<BlurEffect>(copy.Effect).Radius, 6);
        Assert.Same(piece, Assert.IsType<VisualBrush>(copy.Fill).Visual);
    });

    [Fact]
    public void The_copy_follows_the_pieces_size() => Sta.Run(() =>
    {
        var piece = Piece();
        var host = DeskGlowLayer.Wrap(piece, 0.6);
        Render(host, 20);
        var brush = (VisualBrush)((Rectangle)host.Children[0]).Fill;
        Assert.Equal(new Rect(0, 0, piece.ActualWidth, piece.ActualHeight), brush.Viewbox);
        Assert.Equal(piece.ActualWidth, ((Rectangle)host.Children[0]).ActualWidth, 1);

        piece.Text = "1:02";
        Render(host, 20);
        Assert.Equal(new Rect(0, 0, piece.ActualWidth, piece.ActualHeight), brush.Viewbox);
    });

    [Fact]
    public void One_color_is_an_effect_on_the_piece_in_the_inks_middle_color() => Sta.Run(() =>
    {
        var piece = Piece();
        Assert.Same(piece, DeskGlowLayer.On(piece, Ink(0.8, oneColor: true)));
        var glow = Assert.IsType<DropShadowEffect>(piece.Effect);
        Assert.Equal(0, glow.ShadowDepth);
        Assert.Equal(DeskView.Rgb("3a63e0"), glow.Color);
        Assert.Equal(DeskGlow.Radius(0.8), glow.BlurRadius, 6);
        Assert.Equal(DeskGlow.Opacity(0.8), glow.Opacity, 6);
        Assert.Equal("clock", piece.Tag);
    });

    [Theory]
    [InlineData(new[] { "aaaaaa" }, "aaaaaa")]
    [InlineData(new[] { "aaaaaa", "bbbbbb" }, "bbbbbb")]
    [InlineData(new[] { "aaaaaa", "bbbbbb", "cccccc" }, "bbbbbb")]
    [InlineData(new string[0], "ffffff")]
    public void The_middle_color_of_an_ink(string[] stops, string want) => Assert.Equal(want, DeskGlowLayer.Middle(stops));

    [SkippableFact]
    public void The_glow_spills_past_the_ink_and_a_gradient_glows_as_a_gradient() => Sta.Run(() =>
    {
        Skip.IfNot(DrawsBlur(), NoBlur);
        double pad = DeskGlow.Pad(1.0);
        var plain = Render(Piece(), pad);
        var lit = Render(DeskGlowLayer.Wrap(Piece(), 1.0), pad);
        Assert.Equal(plain.W, lit.W);
        Assert.Equal(plain.H, lit.H);

        // The pixels the glow adds: lit with it, clear without it. Premultiplied BGRA.
        long leftR = 0, leftG = 0, leftA = 0, rightR = 0, rightG = 0, rightA = 0;
        int added = 0, inked = 0;
        for (int y = 0; y < lit.H; y++)
            for (int x = 0; x < lit.W; x++)
            {
                int i = (y * lit.W + x) * 4;
                if (plain.Px[i + 3] > 8) { inked++; continue; }
                if (lit.Px[i + 3] <= 8) continue;
                added++;
                if (x < lit.W / 3) { leftR += lit.Px[i + 2]; leftG += lit.Px[i + 1]; leftA += lit.Px[i + 3]; }
                else if (x > lit.W * 2 / 3) { rightR += lit.Px[i + 2]; rightG += lit.Px[i + 1]; rightA += lit.Px[i + 3]; }
            }
        Assert.True(added > inked / 2, $"the glow added {added} pixels around {inked} of ink");
        Assert.True(leftA > 0 && rightA > 0, "the glow reaches both ends");
        // Violet (8f5bd6) on the left, cyan (33fdff) on the right: more red left, more green right.
        Assert.True((double)leftR / leftA > (double)rightR / rightA + 0.15, "the left of the glow is not redder than the right");
        Assert.True((double)rightG / rightA > (double)leftG / leftA + 0.15, "the right of the glow is not greener than the left");
    });

    [SkippableFact]
    public void The_padding_holds_the_whole_glow() => Sta.Run(() =>
    {
        Skip.IfNot(DrawsBlur(), NoBlur);
        var (px, w, h) = Render(DeskGlowLayer.Wrap(Piece(), 1.0), DeskGlow.Pad(1.0));
        // Nothing reaches the bitmap's outermost row or column: the glow fades out inside its room.
        for (int x = 0; x < w; x++)
        {
            Assert.True(px[x * 4 + 3] <= 2, $"top edge lit at {x}");
            Assert.True(px[((h - 1) * w + x) * 4 + 3] <= 2, $"bottom edge lit at {x}");
        }
        for (int y = 0; y < h; y++)
        {
            Assert.True(px[y * w * 4 + 3] <= 2, $"left edge lit at {y}");
            Assert.True(px[(y * w + w - 1) * 4 + 3] <= 2, $"right edge lit at {y}");
        }
    });
}
