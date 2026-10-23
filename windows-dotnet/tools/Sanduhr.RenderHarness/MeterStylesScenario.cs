using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

// The meter styles scenario (#336): the Claude line, the Claude limits and telemetry in each style (bar, slim, ring, ticks), upright
// and in a ±18 degree fold like ridealongs-fold-neon; the dense right face that looked busy in #334's fold (the Claude line, telemetry
// and the limits stacked) as bars and again with slim and rings; a close-up of each style at twice the size with a limit over the
// warning line; Arrange Desk over the restyled fold; and Settings, Desk with the Style pickers. Sample data only: no Desk windows,
// no network, no real usage.
public static partial class Program
{
    private static async Task RenderMeterStyles(WidgetViewModel vm, Assembly asm)
    {
        var paths = new Paths();
        var store = new SettingsStore(paths);
        var now = new DateTimeOffset(2026, 10, 23, 21, 9, 0, TimeSpan.Zero);
        var screen = new DeskScreen("DISPLAY1", new PxRect(0, 0, 1920, 1080), new PxRect(0, 0, 1920, 1032), 1.0, true,
            TaskbarClearance.Insets(new PxRect(0, 0, 1920, 1080), new PxRect(0, 0, 1920, 1032), [], 1.0));
        var fonts = new DeskFonts();
        DeskUsage Usage(double session, double week) => new(
            [new DeskTier(TierModel.FiveHour, session, Iso(now + TimeSpan.FromHours(2.5))), new DeskTier(TierModel.SevenDay, week, Iso(now + TimeSpan.FromDays(3)))],
            now, false, false, null);
        var usage = Usage(34, 63);
        var telemetry = new TelemetrySource();
        telemetry.Preview(new TelemetrySample(23, 61, 48, now));
        var neon = DeskInk.Resolve(new DeskInkSettings(DeskInkMode.Custom, "22d3ee,a855f7,ec4899", true), null, null);
        var dark = Color.FromRgb(0x10, 0x14, 0x1c);

        (IReadOnlyList<DeskIsland> Islands, DeskView View) Draw(string layout, DeskInkMap inks, DeskUsage? u = null, bool plate = false)
        {
            var settings = (DeskSettings.Default with { Enabled = true, Layout = layout, Plate = plate }).Normalized();
            var view = new DeskView(screen, settings, fonts);
            view.Update(new DeskFrame { Now = now, Usage = u ?? usage, Telemetry = telemetry }, inks);
            return (view.TakeIslands(), view);
        }

        string S(DeskMeterStyle s) => s == DeskMeterStyle.Bar ? "" : ":" + DeskMeterStyles.Word(s);
        string Word(string widget, string anchor, int tilt, DeskMeterStyle s) =>
            tilt == 0 && s == DeskMeterStyle.Bar ? $"{widget}:{anchor}" : $"{widget}:{anchor}:1:{tilt}{S(s)}";

        // ---- each style, upright and folded: the Claude line and limits on the left face, telemetry on the right ----
        foreach (var style in DeskMeterStyles.All)
        {
            foreach (var (name, tilt, inks) in new[] { ("upright", 0, DeskInkMap.Light), ("fold", 18, new DeskInkMap(neon)) })
            {
                string layout = string.Join(' ', Word("clock", "ml", -tilt, DeskMeterStyle.Bar), Word("claude", "ml", -tilt, style),
                    Word("meters", "ml", -tilt, style), Word("telemetry", "mr", tilt, style));
                var (islands, view) = Draw(layout, inks);
                Log.Add($"meterstyles {DeskMeterStyles.Word(style)}-{name}: layout '{new DeskArrangement(layout).Layout}' islands {islands.Count}");
                SaveScaled(Composite(islands, dark), $"meterstyles-{DeskMeterStyles.Word(style)}-{name}", 1.0);
                await Settle();
                view.Dispose();
            }
        }

        // ---- the dense right face from #334's fold: before (bars) and after (slim line, ring telemetry, slim limits) ----
        foreach (var (name, claude, tele, meters) in new[]
                 {
                     ("dense-before", DeskMeterStyle.Bar, DeskMeterStyle.Bar, DeskMeterStyle.Bar),
                     ("dense-after", DeskMeterStyle.Slim, DeskMeterStyle.Ring, DeskMeterStyle.Slim),
                     ("dense-rings", DeskMeterStyle.Ring, DeskMeterStyle.Ring, DeskMeterStyle.Slim),
                 })
        {
            string layout = $"clock:ml:1:-18 nowPlaying:ml:1:-18 {Word("telemetry", "mr", 18, tele)} {Word("claude", "mr", 18, claude)} {Word("meters", "mr", 18, meters)}";
            var (islands, view) = Draw(layout, new DeskInkMap(neon));
            SaveScaled(Composite(islands, dark), $"meterstyles-{name}", 1.0);
            await Settle();
            view.Dispose();
            // The right face alone, at twice the size, to read it (drawn again: the composite holds the first one).
            (islands, view) = Draw(layout, new DeskInkMap(neon));
            var face = islands.Single(i => i.Widgets.Contains("meters"));
            var host = new Border { Child = face.Content, Background = new SolidColorBrush(dark), Padding = new Thickness(24) };
            host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            host.Arrange(new Rect(host.DesiredSize));
            Log.Add($"meterstyles {name}: right face {host.DesiredSize.Width:0}x{host.DesiredSize.Height:0}");
            SaveScaled(host, $"meterstyles-{name}-face", 2.0);
            await Settle();
            view.Dispose();
        }

        // ---- close-ups: each style at size 1.6 with the session over the warning line, in light ink and on the plate ----
        foreach (var style in DeskMeterStyles.All.Where(s => s != DeskMeterStyle.Bar))
        {
            string layout = $"claude:bl:1.6:0{S(style)} meters:bl:1.6:0{S(style)} telemetry:br:1.6:0{S(style)}";
            var (islands, view) = Draw(layout, DeskInkMap.Light, Usage(93, 41), plate: true);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Background = new SolidColorBrush(Color.FromRgb(0x2a, 0x3a, 0x52)) };
            foreach (var island in islands)
                row.Children.Add(new Border { Child = island.Content, Padding = new Thickness(36), VerticalAlignment = VerticalAlignment.Bottom });
            row.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            row.Arrange(new Rect(row.DesiredSize));
            SaveScaled(row, $"meterstyles-closeup-{DeskMeterStyles.Word(style)}", 1.5);
            await Settle();
            view.Dispose();
        }

        // ---- Arrange Desk over the restyled fold: outlines along the slant, and the hit-test of a ring row ----
        {
            const string fold = "clock:ml:1:-18 claude:ml:1:-18:slim meters:ml:1:-18:ticks telemetry:mr:1:18:ring";
            var settings = (DeskSettings.Default with { Enabled = true, Layout = fold }).Normalized();
            var frame = new DeskFrame { Usage = usage, Now = now, Telemetry = telemetry };
            var content = new DeskArrangeContent(screen, settings, fonts, frame, new DeskArrangement(fold), primary: true);
            var root = new Grid { Width = 1920, Height = 1080, Background = new SolidColorBrush(Color.FromRgb(0x1e, 0x24, 0x30)) };
            root.Children.Add(content);
            root.Measure(new Size(1920, 1080));
            root.Arrange(new Rect(0, 0, 1920, 1080));
            await Settle();
            Save(root, "meterstyles-arrange-fold");
            var t = content.PieceRects["telemetry"];
            Log.Add($"meterstyles arrange: telemetry box {t}; top left hits '{content.PieceAt(new Point(t.Left + 3, t.Top + 3)) ?? "nothing"}', " +
                $"bottom left hits '{content.PieceAt(new Point(t.Left + 3, t.Bottom - 3)) ?? "nothing"}', top right hits '{content.PieceAt(new Point(t.Right - 3, t.Top + 3)) ?? "nothing"}'");
            content.Dispose();
        }
        telemetry.Dispose();

        // ---- Settings, Desk, Where each piece sits, with the Style pickers ----
        store.SaveDesk(DeskSettings.Default with { Enabled = true, Layout = "clock:bl claude:bl:1:0:slim meters:tr:1:0:ring telemetry:tr:1:0:ticks" });
        var svm = new SettingsViewModel(vm, () => Task.CompletedTask, _ => Task.CompletedTask);
        AssertOnlySampleAccounts(svm);
        var swType = asm.GetType("Sanduhr.App.Views.SettingsWindow")!;
        var sw = (Window)Activator.CreateInstance(swType, svm)!;
        OffScreen(sw);
        sw.Show();
        sw.Left = -20000;
        sw.Top = -20000;
        sw.Height = 2700;
        await Settle();
        swType.GetMethod("SelectPage")!.Invoke(sw, [SettingsPage.Desk, SettingsIndex.DeskPieces]);
        await Settle();
        if (Descendants<Sanduhr.App.Views.DeskPiecesPanel>(sw).FirstOrDefault() is { } panel) Save(panel, "meterstyles-settings-pieces");
        sw.Close();
        await Settle();
    }
}
