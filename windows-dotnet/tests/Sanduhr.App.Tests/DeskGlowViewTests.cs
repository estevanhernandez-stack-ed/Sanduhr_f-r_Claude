using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Sanduhr.App.Desk;
using Sanduhr.Core;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace Sanduhr.App.Tests;

/// <summary>The neon glow on the Desk itself (5.0 spec 5, section 7): which pieces glow and how, the room an island leaves for it, and
/// that the Desk without it draws exactly as before.</summary>
public sealed class DeskGlowViewTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private static DeskScreen Screen()
    {
        var bounds = new PxRect(0, 0, 1920, 1080);
        var work = new PxRect(0, 0, 1920, 1032);
        return new DeskScreen("TEST", bounds, work, 1.0, true, TaskbarClearance.Insets(bounds, work, [], 1.0));
    }

    private static ResolvedInk Glowing(double strength = 0.8) =>
        DeskInk.Resolve(new DeskInkSettings(DeskInkMode.Custom, "8f5bd6,3a63e0,33fdff", false, true, strength), null, null);

    private static DeskView View(string layout, DeskFrame frame, ResolvedInk ink)
    {
        var settings = (DeskSettings.Default with { Enabled = true, Layout = layout, Font = "Segoe UI" }).Normalized();
        var view = new DeskView(Screen(), settings, new DeskFonts(["Segoe UI"]));
        view.Update(frame, new DeskInkMap(ink));
        return view;
    }

    private static IEnumerable<T> All<T>(DependencyObject node) where T : DependencyObject
    {
        if (node is T hit) yield return hit;
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            foreach (var found in All<T>(child))
                yield return found;
    }

    private static DeskUsage Usage()
    {
        string iso = Now.AddHours(2).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffK", System.Globalization.CultureInfo.InvariantCulture);
        return new DeskUsage([new DeskTier(TierModel.FiveHour, 42, iso)], Now.AddMinutes(-1), false, false, null);
    }

    private static bool IsShadow(Effect? e) => e is DropShadowEffect { ShadowDepth: > 0 };

    [Fact]
    public void A_glowing_ink_puts_a_blurred_copy_behind_each_piece_and_draws_no_shadow() => Sta.Run(() =>
    {
        var view = View("clock:bl claude:bl meters:tr", new DeskFrame { Usage = Usage(), Now = Now }, Glowing());
        var hosts = All<GlowHost>(view).ToList();

        Assert.Equal(["claude", "clock", "meters"], hosts.Select(h => (string)h.Tag).Order());
        foreach (var host in hosts)
        {
            var copy = Assert.IsType<Rectangle>(host.Children[0]);
            Assert.Equal(DeskGlow.Radius(0.8), Assert.IsType<BlurEffect>(copy.Effect).Radius, 6);
            Assert.Equal(DeskGlow.Opacity(0.8), copy.Opacity, 6);
        }
        Assert.DoesNotContain(All<FrameworkElement>(view), e => IsShadow(e.Effect));
        // The frames the Desk reports are the wrappers', which are the pieces' size.
        Assert.Equal(3, view.PieceFrames().Count);
        view.Dispose();
    });

    [Fact]
    public void Without_the_glow_the_Desk_draws_as_it_always_has() => Sta.Run(() =>
    {
        var view = View("clock:bl claude:bl", new DeskFrame { Usage = Usage(), Now = Now }, DeskInkMap.Light.Default);
        Assert.Empty(All<GlowHost>(view));
        Assert.Empty(All<Rectangle>(view).Where(r => r.Fill is VisualBrush));
        var clock = All<FrameworkElement>(view).Single(e => (e.Tag as string) == "clock");
        Assert.True(IsShadow(clock.Effect));
        Assert.Equal(DeskGlow.BasePad, view.TakeIslands().Single().Pad);
        view.Dispose();
    });

    [Fact]
    public void The_one_color_hint_draws_one_color_and_no_copy() => Sta.Run(() =>
    {
        var view = View("clock:bl", new DeskFrame { Now = Now }, Glowing() with { GlowOneColor = true });
        Assert.Empty(All<GlowHost>(view));
        var clock = All<FrameworkElement>(view).Single(e => (e.Tag as string) == "clock");
        var glow = Assert.IsType<DropShadowEffect>(clock.Effect);
        Assert.Equal(0, glow.ShadowDepth);
        Assert.Equal(DeskView.Rgb("3a63e0"), glow.Color);
        view.Dispose();
    });

    [Theory]
    [InlineData(0.2)]
    [InlineData(0.6)]
    [InlineData(1.0)]
    public void An_island_leaves_room_for_the_glow(double strength) => Sta.Run(() =>
    {
        var view = View("clock:bl meters:tr", new DeskFrame { Usage = Usage(), Now = Now }, Glowing(strength));
        Assert.All(view.TakeIslands(), i => Assert.Equal(DeskGlow.Pad(strength), i.Pad, 6));
        view.Dispose();
    });

    [Fact]
    public void There_is_one_clock_for_the_Desks_clicks_to_find() => Sta.Run(() =>
    {
        var view = View("clock:bl", new DeskFrame { Now = Now }, Glowing());
        Assert.IsType<GlowHost>(All<FrameworkElement>(view).Single(e => (e.Tag as string) == "clock"));
        view.Dispose();
    });

    [Fact]
    public void A_message_line_glows_in_its_own_copy_and_a_noglow_line_does_not() => Sta.Run(() =>
    {
        var settings = DeskMessageSettings.Default with { Glow = false };
        var lit = View("message:bl", new DeskFrame { Now = Now, Message = new DeskMessageFrame(new MessageToday(usual: "keep building."), settings) }, Glowing());
        var copy = Assert.Single(All<Rectangle>(lit).Where(r => r.Fill is VisualBrush));
        Assert.IsType<BlurEffect>(copy.Effect);
        Assert.DoesNotContain(All<TextBlock>(lit), t => t.Effect is DropShadowEffect);
        lit.Dispose();

        var plain = View("message:bl", new DeskFrame { Now = Now, Message = new DeskMessageFrame(new MessageToday(usual: "{noglow}keep building."), settings) }, Glowing());
        Assert.Empty(All<Rectangle>(plain).Where(r => r.Fill is VisualBrush));
        Assert.DoesNotContain(All<TextBlock>(plain), t => t.Effect is not null);
        plain.Dispose();
    });

    [Fact]
    public void A_message_under_the_icons_glows_in_one_color() => Sta.Run(() =>
    {
        var settings = DeskMessageSettings.Default with { Glow = false };
        var view = View("message:bl", new DeskFrame { Now = Now, Message = new DeskMessageFrame(new MessageToday(usual: "keep building."), settings) },
            Glowing() with { GlowOneColor = true });
        Assert.Empty(All<Rectangle>(view).Where(r => r.Fill is VisualBrush));
        var glow = Assert.IsType<DropShadowEffect>(All<TextBlock>(view).Single(t => t.Text == "keep building.").Effect);
        Assert.Equal(0, glow.ShadowDepth);
        Assert.Equal(DeskGlow.Radius(0.8), glow.BlurRadius, 6);
        view.Dispose();
    });

    [Fact]
    public void Now_playing_glows_inside_its_reflow_host() => Sta.Run(() =>
    {
        var frame = new DeskFrame { Now = Now, Track = new NowPlayingInfo { Title = "Song", Artist = "Band" } };
        var view = View("nowPlaying:bl", frame, Glowing());
        var host = Assert.Single(All<DeskView.ReflowHost>(view));
        Assert.Single(All<GlowHost>(host));
        // The stack still holds the reflow host itself, so the piece can fade and give its height back.
        Assert.IsType<StackPanel>(host.Parent);
        view.Dispose();
    });

    [Fact]
    public void A_launcher_label_glows_in_one_color_and_its_icon_does_not() => Sta.Run(() =>
    {
        var model = new LauncherModel(LauncherOrientation.Row, [LauncherButton.Create(LauncherKind.Link, "https://example.com", label: "Home")!]);
        var launcher = new DeskLauncherFrame(model, new HashSet<string>(), new Dictionary<string, ImageSource>(), new Dictionary<string, string>(), new Dictionary<string, ImageSource>());
        var view = View("launcher:br", new DeskFrame { Now = Now, Launcher = launcher }, Glowing());
        Assert.Empty(All<GlowHost>(view));
        var label = All<TextBlock>(view).Single(t => t.Effect is DropShadowEffect);
        var glow = (DropShadowEffect)label.Effect;
        Assert.Equal(0, glow.ShadowDepth);
        Assert.Equal(DeskView.Rgb("3a63e0"), glow.Color);
        view.Dispose();
    });

    [Fact]
    public void The_switch_reaches_an_ink_the_Desk_did_not_make_and_marks_it_for_under_the_icons()
    {
        var own = DeskInkSettings.Default.WithGlow(true).WithGlowStrength(0.8);
        var followed = DeskInk.Resolve(new DeskInkSettings(DeskInkMode.Custom, "33fdff", true), null, null);

        var lit = DeskController.Lit(followed, own, oneColor: false);
        Assert.Equal(0.8, lit.Glow);
        Assert.Equal(0, lit.ShadowOpacity);
        Assert.False(lit.GlowOneColor);

        Assert.True(DeskController.Lit(followed, own, oneColor: true).GlowOneColor);
        // No glow, no hint: an ink without the glow is the ink it was.
        Assert.Same(followed, DeskController.Lit(followed, DeskInkSettings.Default, oneColor: true));
    }
}
