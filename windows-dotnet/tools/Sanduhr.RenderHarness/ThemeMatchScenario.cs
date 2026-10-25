using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Sanduhr.App.Desk;
using Sanduhr.App.Theming;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

// The theme match scenario: every built-in theme's matched Desk (the clock, the Claude line, the limits as bars, telemetry as rings and a
// message) at the warn level (session 85%) and the urgent level (week 97%), over a dark and a light wallpaper, with the readability guard
// as the Desk applies it; and each theme's widget cards at 41, 85 and 97%, as it ships (the ramp) and with desk.bars set to its gradient.
// Sample data only: no Desk windows, no network, no real usage.
public static partial class Program
{
    private static async Task RenderThemeMatch(WidgetViewModel vm, Assembly asm)
    {
        var foreground0 = GetForegroundWindow();
        var now = new DateTimeOffset(2026, 10, 25, 21, 9, 0, TimeSpan.Zero);
        var screen = new DeskScreen("DISPLAY1", new PxRect(0, 0, 1920, 1080), new PxRect(0, 0, 1920, 1032), 1.0, true,
            TaskbarClearance.Insets(new PxRect(0, 0, 1920, 1080), new PxRect(0, 0, 1920, 1032), [], 1.0));
        var fonts = new DeskFonts();
        var telemetry = new TelemetrySource();
        telemetry.Preview(new TelemetrySample(23, 61, 48, now));
        DeskUsage Usage(double session, double week) => new(
            [new DeskTier(TierModel.FiveHour, session, Iso(now + TimeSpan.FromHours(2.5))), new DeskTier(TierModel.SevenDay, week, Iso(now + TimeSpan.FromDays(3)))],
            now, false, false, null);
        var levels = new[] { ("warn", Usage(85, 63)), ("urgent", Usage(41, 97)) };
        // The backgrounds stand in for wallpapers; the luminance is what the Desk's sample would read behind each stack.
        var walls = new[] { ("dark", Color.FromRgb(0x10, 0x14, 0x1c), 0.006), ("light", Color.FromRgb(0xe9, 0xe6, 0xdf), 0.79) };
        const string layout = "clock:bl claude:bl meters:bl telemetry:tr:1:0:ring message:tl";
        var message = new DeskMessageFrame(new MessageToday(usual: "keep building."), DeskMessageSettings.Default);

        foreach (var key in ThemeCatalog.BuiltInOrder)
        {
            var theme = ThemeCatalog.BuiltIns[key];
            var draw = DeskLookResolver.For(true, theme)!;
            Log.Add($"theme-match {key}: {draw.Look.ToJson().ToJsonString()}");
            foreach (var (wallName, wallColor, wallLum) in walls)
            {
                // The guard as the controller applies it: the plate goes on where the ink's first stop would not read.
                var guarded = draw.Look.Guarded(wallLum);
                var guardedDraw = draw with { Look = guarded };
                var settings = DeskLookResolver.Settings(DeskSettings.Default with { Enabled = true, Layout = layout }, guardedDraw).Normalized();
                foreach (var (levelName, usage) in levels)
                {
                    var view = new DeskView(screen, settings, fonts);
                    var frame = DeskLookResolver.Frame(new DeskFrame { Now = now, Usage = usage, Telemetry = telemetry, Message = message }, guardedDraw);
                    view.Update(frame, new DeskInkMap(draw.Ink));
                    var islands = view.TakeIslands();
                    Log.Add($"theme-match {key}-{levelName}-{wallName}: plate {(guarded.Plate ? "on" : "off")}"
                        + $"{(guarded.Plate && !draw.Look.Plate ? " (the guard)" : "")}, reads {draw.Look.ReadsOver(wallLum)}, islands {islands.Count}");
                    SaveScaled(Composite(islands, wallColor), $"theme-match-{key}-{levelName}-{wallName}", 0.5);
                    await Settle();
                    view.Dispose();
                }
            }

            // The widget: the ramp as the theme ships, and desk.bars set to its gradient (the gradient and the signal).
            var withBars = theme with { Desk = new ThemeDesk { Bars = [.. draw.Look.Gradient.Select(s => "#" + s)] } };
            foreach (var (variant, def) in new[] { ("ramp", theme), ("bars", withBars) })
            {
                var palette = new ThemePalette(key, def);
                var stack = new StackPanel { Width = 396, Background = new SolidColorBrush(palette.Bg) };
                palette.Apply(stack.Resources);
                foreach (var util in new[] { 41, 85, 97 })
                {
                    var card = new TierCardViewModel(TierModel.FiveHour, palette);
                    card.Update(util, Iso(now + TimeSpan.FromHours(2.5)), null, null, [], palette, SparklineStyle.Classic, now);
                    stack.Children.Add(new TierCard { DataContext = card, Margin = new Thickness(8) });
                }
                stack.Measure(new Size(396, double.PositiveInfinity));
                stack.Arrange(new Rect(stack.DesiredSize));
                stack.UpdateLayout();
                Save(stack, $"theme-match-widget-{key}-{variant}");
                await Settle();
            }
        }
        Log.Add($"theme-match: foreground window before {foreground0}, after {GetForegroundWindow()}");
    }
}
