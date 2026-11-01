using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.App.Theming;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

// The four drawer looks (drawers look and placement plan, Task 9): one drawer of made-up files, drawn as Solid, Glass, Outline and Bare in
// obsidian and aurora, over three wallpaper stand-ins: busy (a gradient with alternating light and dark stripes, average near 0.45), plain (#1e2430) and bright (#d8dde6). The look is built the
// way the Desk builds it, at opacity 55 and the stand-in's luminance. Glass's real blur is a DWM effect and does not appear in an off
// screen render; the tinted plate is what renders. The drawer service runs over a scratch folder in the temp directory, never the real
// Desktop. Not part of all.
//
//   --scenario drawer-looks
//
// PNGs (24): <out>/drawer-look-<theme>-<busy|plain|bright>-<solid|glass|outline|bare>.png.
public static partial class Program
{
    private const int DrawerLooksW = 900, DrawerLooksH = 520;

    private static (Brush Brush, double Luminance) DrawerLooksWall(string name)
    {
        if (name == "plain") return (new SolidColorBrush(Color.FromRgb(0x1e, 0x24, 0x30)), InkHex.Luminance("1e2430"));
        if (name == "bright") return (new SolidColorBrush(Color.FromRgb(0xd8, 0xdd, 0xe6)), InkHex.Luminance("d8dde6"));
        // Busy: a diagonal gradient with light and dark stripes alternating across it, so the average sits near 0.45 while a label can
        // still land on a light stripe.
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0x1c, 0x3a, 0x66), 0));
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0x7a, 0x4a, 0x8c), 0.5));
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0xe2, 0x8a, 0x44), 1));
        g.Freeze();
        var stripes = new DrawingGroup();
        stripes.Children.Add(new GeometryDrawing(g, null, new RectangleGeometry(new Rect(0, 0, DrawerLooksW, DrawerLooksH))));
        var light = new SolidColorBrush(Color.FromRgb(0xf4, 0xf0, 0xe6));
        var dark = new SolidColorBrush(Color.FromRgb(0x0a, 0x0c, 0x10));
        for (int x = 0, n = 0; x < DrawerLooksW; x += 90, n++)
        {
            stripes.Children.Add(new GeometryDrawing(light, null, new RectangleGeometry(new Rect(x, 0, 38, DrawerLooksH))));
            stripes.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(x + 38, 0, 30, DrawerLooksH))));
        }
        stripes.Freeze();
        var brush = new DrawingBrush(stripes) { Stretch = Stretch.Fill };
        brush.Freeze();
        return (brush, MeanLuminance(brush));
    }

    /// <summary>The wallpaper's mean relative luminance, measured from the rendered stand-in (what the Desk's sample would read).</summary>
    private static double MeanLuminance(Brush brush)
    {
        var r = new Border { Width = DrawerLooksW, Height = DrawerLooksH, Background = brush };
        r.Measure(new Size(DrawerLooksW, DrawerLooksH));
        r.Arrange(new Rect(0, 0, DrawerLooksW, DrawerLooksH));
        var rtb = new RenderTargetBitmap(DrawerLooksW, DrawerLooksH, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(r);
        var px = new byte[DrawerLooksW * DrawerLooksH * 4];
        rtb.CopyPixels(px, DrawerLooksW * 4, 0);
        static double Lin(byte v) { double c = v / 255.0; return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
        double sum = 0;
        for (int i = 0; i < px.Length; i += 4)
            sum += 0.2126 * Lin(px[i + 2]) + 0.7152 * Lin(px[i + 1]) + 0.0722 * Lin(px[i]);
        return sum / (px.Length / 4);
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

            // Icons load off the UI thread; wait for every item's image (capped) so no shot is missing a thumbnail.
            var drawer = service.Get("images")!;
            for (int tries = 0; tries < 100 && drawer.Items.Any(it => it.Icon is null); tries++)
                await Task.Delay(50);
            Log.Add($"drawer-looks icons loaded: {drawer.Items.Count(it => it.Icon is not null)} of {drawer.Items.Count}");

            foreach (var key in new[] { "obsidian", "aurora" })
            {
                vm.ApplyThemeByKey(key);
                vm.ApplyThemeResources();
                await Settle();
                var draw = DeskLookResolver.For(true, ThemeCatalog.BuiltIns[key])!;
                var frame = DeskLookResolver.Frame(new DeskFrame { Now = now }, draw);
                foreach (var wallName in new[] { "busy", "plain", "bright" })
                {
                    var (wallBrush, lum) = DrawerLooksWall(wallName);
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
