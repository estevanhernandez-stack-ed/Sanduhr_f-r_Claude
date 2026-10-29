using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sanduhr.App.Controls;
using Sanduhr.App.Theming;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Theme match, "Meters", on the widget: the ramp stays for every theme without <c>desk.bars</c> (no built-in sets it, so nothing
/// changes at upgrade); a theme with it fills the bars with its gradient and the usage signal, and the percent takes the signal.
/// </summary>
public sealed class WidgetBarsTests
{
    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    private static TierCardViewModel Card(ThemeDefinition theme, int util)
    {
        var palette = new ThemePalette("t", theme);
        var vm = new TierCardViewModel(TierModel.FiveHour, palette);
        vm.Update(util, null, null, null, [], palette, SparklineStyle.Classic, DateTimeOffset.Now);
        return vm;
    }

    [Theory]
    [InlineData("obsidian", 10)]
    [InlineData("aurora", 60)]
    [InlineData("ember", 85)]
    [InlineData("mint", 97)]
    [InlineData("matrix", 50)]
    [InlineData("blueprint", 90)]
    public void A_theme_without_desk_bars_keeps_the_ramp(string key, int util) => Sta.Run(() =>
    {
        var vm = Card(ThemeCatalog.BuiltIns[key], util);
        Assert.Null(vm.BarBrush);
        Assert.Equal(ThemePalette.UsageColor(util), vm.BarColor);
        Assert.Equal(ThemePalette.UsageColor(util), ((SolidColorBrush)vm.ValueBrush).Color);
    });

    [Fact]
    public void Desk_bars_fill_the_bar_with_the_gradient_and_the_signal() => Sta.Run(() =>
    {
        var theme = ThemeCatalog.BuiltIns["obsidian"] with { Desk = new ThemeDesk { Bars = ["#6c63ff", "#e8e4dc"] } };
        var calm = Card(theme, 40);
        Assert.Equal([Rgb(0x6c, 0x63, 0xff), Rgb(0xe8, 0xe4, 0xdc)], ((LinearGradientBrush)calm.BarBrush!).GradientStops.Select(s => s.Color));
        Assert.Equal(Rgb(0xe8, 0xe4, 0xdc), ((SolidColorBrush)calm.ValueBrush).Color);   // the theme's text under Warn at
        var warn = Card(theme, 85);
        Assert.Equal(Rgb(0xfa, 0xcc, 0x15), ((LinearGradientBrush)warn.BarBrush!).GradientStops[1].Color);
        Assert.Equal(Rgb(0xfa, 0xcc, 0x15), ((SolidColorBrush)warn.ValueBrush).Color);
        warn.SetThresholds(50, 80);   // Alerts moved: 85 is past Urgent at now
        Assert.Equal(Rgb(0xf8, 0x71, 0x71), ((LinearGradientBrush)warn.BarBrush!).GradientStops[1].Color);
        Assert.Equal(Rgb(0xf8, 0x71, 0x71), ((SolidColorBrush)warn.ValueBrush).Color);
    });

    [Fact]
    public void Switching_to_and_from_a_theme_with_desk_bars_recolors_the_card() => Sta.Run(() =>
    {
        var plain = ThemeCatalog.BuiltIns["obsidian"];
        var vm = Card(plain, 85);
        vm.ApplyPalette(new ThemePalette("x", plain with { Desk = new ThemeDesk { Bars = ["#6c63ff"] } }));
        Assert.NotNull(vm.BarBrush);
        vm.ApplyPalette(new ThemePalette("obsidian", plain));
        Assert.Null(vm.BarBrush);
        Assert.Equal(ThemePalette.UsageColor(85), ((SolidColorBrush)vm.ValueBrush).Color);
    });

    [Fact]
    public void A_desk_block_without_bars_leaves_the_card_pixel_identical() => AppHost.Run(() =>
    {
        var plain = ThemeCatalog.BuiltIns["obsidian"];
        var matched = plain with { Desk = new ThemeDesk { Ink = ["#ffffff"], Plate = false, Shadow = "#000000", Glow = "#6c63ff" } };
        // The compare below runs both renders under the new code, so pin the ramp path itself: no fill brush on the bar,
        // the percent in the ramp's color.
        foreach (var theme in new[] { plain, matched })
        {
            var card = Build(theme, 85);
            Assert.Null(Descendants<UsageBar>(card).Single().FillBrush);
            var value = Descendants<TextBlock>(card).Single(t => t.Text == ((TierCardViewModel)card.DataContext).ValueText);
            Assert.Equal(ThemePalette.UsageColor(85), ((SolidColorBrush)value.Foreground).Color);
        }
        Assert.Equal(Render(plain, 85), Render(matched, 85));
        Assert.NotEqual(Render(plain, 85), Render(plain with { Desk = new ThemeDesk { Bars = ["#6c63ff"] } }, 85));
    });

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T hit) yield return hit;
            foreach (var d in Descendants<T>(child)) yield return d;
        }
    }

    private static TierCard Build(ThemeDefinition theme, int util)
    {
        var card = new TierCard { DataContext = Card(theme, util), Width = 380 };
        new ThemePalette("t", theme).Apply(card.Resources);
        card.Measure(new Size(380, double.PositiveInfinity));
        card.Arrange(new Rect(card.DesiredSize));
        card.UpdateLayout();
        return card;
    }

    private static byte[] Render(ThemeDefinition theme, int util)
    {
        var card = Build(theme, util);
        var rtb = new RenderTargetBitmap((int)Math.Ceiling(card.ActualWidth), (int)Math.Ceiling(card.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        rtb.Render(card);
        var px = new byte[rtb.PixelWidth * rtb.PixelHeight * 4];
        rtb.CopyPixels(px, rtb.PixelWidth * 4, 0);
        return px;
    }
}
