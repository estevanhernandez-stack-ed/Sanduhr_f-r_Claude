using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.App.Theming;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

// The four drawer looks (drawers look and placement plan, Task 9): one drawer of made-up files, drawn as Solid, Glass, Outline and Bare in
// obsidian and aurora, over two wallpaper stand-ins: busy (a gradient with contrasting stripes) and plain (#1e2430). The look is built the
// way the Desk builds it, at opacity 55 and the stand-in's luminance. Glass's real blur is a DWM effect and does not appear in an off
// screen render; the tinted plate is what renders. The drawer service runs over a scratch folder in the temp directory, never the real
// Desktop. Not part of all.
//
//   --scenario drawer-looks
//
// PNGs (16): <out>/drawer-look-<theme>-<busy|plain>-<solid|glass|outline|bare>.png.
public static partial class Program
{
    private const int DrawerLooksW = 900, DrawerLooksH = 520;

    private static (Brush Brush, double Luminance) DrawerLooksWall(bool busy)
    {
        if (!busy) return (new SolidColorBrush(Color.FromRgb(0x1e, 0x24, 0x30)), InkHex.Luminance("1e2430"));
        // A diagonal gradient from deep blue to warm orange with light and dark stripes across it.
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0x1c, 0x3a, 0x66), 0));
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0x7a, 0x4a, 0x8c), 0.5));
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0xe2, 0x8a, 0x44), 1));
        g.Freeze();
        var stripes = new DrawingGroup();
        stripes.Children.Add(new GeometryDrawing(g, null, new RectangleGeometry(new Rect(0, 0, DrawerLooksW, DrawerLooksH))));
        for (int i = 0; i < 14; i++)
        {
            var c = i % 2 == 0 ? Color.FromArgb(0xB0, 0xf4, 0xf0, 0xe6) : Color.FromArgb(0xB0, 0x06, 0x08, 0x0c);
            stripes.Children.Add(new GeometryDrawing(new SolidColorBrush(c), null,
                new RectangleGeometry(new Rect(i * 70 + 10, 0, 22, DrawerLooksH))));
        }
        stripes.Freeze();
        var brush = new DrawingBrush(stripes) { Stretch = Stretch.Fill };
        brush.Freeze();
        // The mean of the gradient's stops, lifted by the half-light stripes: the luminance the Desk would read behind the drawer.
        double lum = (InkHex.Luminance("1c3a66") + InkHex.Luminance("7a4a8c") + InkHex.Luminance("e28a44")) / 3 * 0.5
            + 0.5 * (InkHex.Luminance("f4f0e6") + InkHex.Luminance("06080c")) / 2 * (22.0 / 70) * 2 + 0.0;
        return (brush, Math.Clamp(lum, 0, 1));
    }

    private static async Task RenderDrawerLooks(WidgetViewModel vm, Assembly asm)
    {
        string root = Path.Combine(Path.GetTempPath(), "sanduhr-harness-drawer-looks-" + Guid.NewGuid().ToString("N")[..8]);
        DrawerService? service = null;
        try
        {
            var desktop = Directory.CreateDirectory(Path.Combine(root, "Desktop")).FullName;
            var pub = Directory.CreateDirectory(Path.Combine(root, "Public")).FullName;
            var home = Directory.CreateDirectory(Path.Combine(desktop, "Drawers", "Images")).FullName;
            foreach (var f in new[] { "holiday-photo.png", "notes.txt", "budget.xlsx", "garden-plan.pdf" })
                File.WriteAllText(Path.Combine(home, f), "x");
            var paths = new Paths(appDataBase: Path.Combine(root, "appdata"), localAppDataBase: Path.Combine(root, "local"));
            var store = new SettingsStore(paths);
            store.SaveDrawers(DrawerSettings.Default with { Enabled = true, Drawers = [new DrawerDef("images", "Images", home, true)] });
            store.SaveDesk(DeskSettings.Default with { Enabled = true, Layout = "clock:bl", Font = "" });
            service = new DrawerService(store, paths, new DrawerPlaces(desktop, pub, []), Dispatcher.CurrentDispatcher);
            var font = new FontFamily("Segoe UI");
            var now = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

            foreach (var key in new[] { "obsidian", "aurora" })
            {
                vm.ApplyThemeByKey(key);
                vm.ApplyThemeResources();
                await Settle();
                var draw = DeskLookResolver.For(true, ThemeCatalog.BuiltIns[key])!;
                var frame = DeskLookResolver.Frame(new DeskFrame { Now = now }, draw);
                foreach (var wallName in new[] { "busy", "plain" })
                {
                    var (wallBrush, lum) = DrawerLooksWall(wallName == "busy");
                    foreach (var kind in Enum.GetValues<DrawerLookKind>())
                    {
                        var look = DrawerLook.From(draw.Ink, frame.PlateHex, frame.AccentHex, font, kind, 55, lum);
                        var piece = new DrawerPiece(service.Get("images")!, service, look, 1);
                        var canvas = new Canvas { Width = DrawerLooksW, Height = DrawerLooksH, Background = wallBrush, ClipToBounds = true };
                        var host = new Border { Child = piece };
                        Canvas.SetLeft(host, 60);
                        Canvas.SetTop(host, 50);
                        canvas.Children.Add(host);
                        canvas.Measure(new Size(DrawerLooksW, DrawerLooksH));
                        canvas.Arrange(new Rect(0, 0, DrawerLooksW, DrawerLooksH));
                        canvas.UpdateLayout();
                        Log.Add($"drawer-looks {key}-{wallName}-{kind}: wallpaper luminance {lum:0.000}, chips {look.Chips}");
                        SaveScaled(canvas, $"drawer-look-{key}-{wallName}-{kind.ToString().ToLowerInvariant()}", 1.0);
                        await Settle();
                    }
                }
            }
        }
        finally
        {
            service?.Dispose();
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
