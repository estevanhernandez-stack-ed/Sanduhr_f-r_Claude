using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

// The Store screenshots scenario (store-shots): Settings v2 pages and the widget, rendered off screen at 1.6x and laid
// out on 2560x1440 frames, written to <out>/store-shots/. Same rules as every scenario here: demo accounts only, the
// invented Claude Code profile (plus invented session logs, SeedStoreLogs, so the Overview has a month to draw), no
// network, never App, never Start(). Check every frame before it goes anywhere: the pictures are what the Store shows.
//
//   --scenario store-shots [--theme obsidian]
public static partial class Program
{
    const int FrameW = 2560, FrameH = 1440;
    const double ShotScale = 1.6;

    static readonly string[] StoreProjects = ["pixel-garden", "trail-notes", "recipe-box", "weather-kit", "hourglass-site"];
    static readonly string[] StoreSkills = ["code-review", "release-notes", "design-pass", "test-first"];

    /// <summary>Thirty days of invented assistant events in the invented profile's two folders: weekdays heavy,
    /// weekends light, today busy. Project paths are invented (D:\code\...), never a real one.</summary>
    static void SeedStoreLogs(string root)
    {
        var rng = new Random(626);
        var now = DateTimeOffset.UtcNow;
        foreach (var (home, proj) in new[] { (".claude", "demo-app"), (".claude-work", "demo-site") })
        {
            string dir = Path.Combine(root, home, "projects", proj);
            Directory.CreateDirectory(dir);
            for (int day = 0; day < 30; day++)
            {
                var date = now.AddDays(-day);
                bool weekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                int events = day == 0 ? 60 : weekend ? rng.Next(0, 12) : rng.Next(20, 90);
                if (events == 0) continue;
                var sb = new StringBuilder();
                for (int i = 0; i < events; i++)
                {
                    string project = StoreProjects[rng.Next(day % 7 == 3 ? 2 : StoreProjects.Length)];
                    var at = day == 0
                        ? now.AddMinutes(-rng.Next(5, 300))
                        : new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.Zero).AddMinutes(rng.Next(13 * 60, 23 * 60));
                    var o = new JsonObject
                    {
                        ["type"] = "assistant",
                        ["timestamp"] = at.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                        ["cwd"] = $"D:\\code\\{project}",
                        ["sessionId"] = $"s{day:00}{rng.Next(3)}",
                        ["message"] = new JsonObject
                        {
                            ["model"] = "claude-sonnet-demo",
                            ["usage"] = new JsonObject { ["input_tokens"] = rng.Next(400, 9000), ["output_tokens"] = rng.Next(200, 4000) },
                        },
                    };
                    if (rng.Next(4) == 0) o["attributionSkill"] = StoreSkills[rng.Next(StoreSkills.Length)];
                    sb.AppendLine(o.ToJsonString());
                }
                File.WriteAllText(Path.Combine(dir, $"session-{day:00}.jsonl"), sb.ToString());
            }
        }
    }

    /// <summary>Refuses a frame that shows the Windows user name: the invented profile lives under %TEMP%, so a full
    /// path on screen would carry it. Only text that is visible and inside the element's bounds counts.</summary>
    static void AssertNoUserName(FrameworkElement el)
    {
        string user = Environment.UserName;
        var bounds = new Rect(el.RenderSize);
        foreach (var tb in Descendants<TextBlock>(el).Where(t => t.IsVisible && t.ActualHeight > 0))
        {
            string text = tb.Text;
            if (text.Length == 0 || text.IndexOf(user, StringComparison.OrdinalIgnoreCase) < 0) continue;
            Rect r = tb.TransformToAncestor(el).TransformBounds(new Rect(tb.RenderSize));
            if (r.IntersectsWith(bounds))
                throw new InvalidOperationException("store-shots: a frame would show the Windows user name; reframe it.");
        }
    }

    static BitmapSource Snap(FrameworkElement el, double scale)
    {
        el.UpdateLayout();
        AssertNoUserName(el);
        int w = (int)Math.Ceiling(el.ActualWidth * scale), h = (int)Math.Ceiling(el.ActualHeight * scale);
        var rtb = new RenderTargetBitmap(Math.Max(1, w), Math.Max(1, h), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        rtb.Render(el);
        rtb.Freeze();
        return rtb;
    }

    static Color Res(string key, Color fallback) =>
        Application.Current.TryFindResource(key) is SolidColorBrush b ? b.Color : fallback;

    /// <summary>One frame: a dark field lit by the theme's accent, each layer at its pixel spot with a soft shadow.</summary>
    static void Frame(string name, params (BitmapSource Img, double X, double Y)[] layers)
    {
        var accent = Res("Sanduhr.Brush.Accent", Color.FromRgb(0x7c, 0x6c, 0xff));
        var bg = Res("Sanduhr.Brush.Bg", Color.FromRgb(0x0b, 0x0c, 0x10));
        var root = new Grid { Width = FrameW, Height = FrameH };
        root.Background = new LinearGradientBrush(
            Color.FromRgb((byte)(bg.R / 2), (byte)(bg.G / 2), (byte)(bg.B / 2)), Color.FromRgb(4, 5, 8), 90);
        root.Children.Add(new Border
        {
            Background = new RadialGradientBrush(Color.FromArgb(0x55, accent.R, accent.G, accent.B), Colors.Transparent)
            { Center = new Point(0.3, 0.25), GradientOrigin = new Point(0.3, 0.25), RadiusX = 0.7, RadiusY = 0.8 },
        });
        root.Children.Add(new Border
        {
            Background = new RadialGradientBrush(Color.FromArgb(0x30, accent.R, accent.G, accent.B), Colors.Transparent)
            { Center = new Point(0.85, 0.9), GradientOrigin = new Point(0.85, 0.9), RadiusX = 0.5, RadiusY = 0.6 },
        });
        var canvas = new Canvas();
        foreach (var (img, x, y) in layers)
        {
            var image = new Image
            {
                Source = img, Width = img.PixelWidth, Height = img.PixelHeight, Stretch = Stretch.Fill,
                Effect = new DropShadowEffect { BlurRadius = 70, ShadowDepth = 18, Direction = 270, Opacity = 0.6, Color = Colors.Black },
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            Canvas.SetLeft(image, x);
            Canvas.SetTop(image, y);
            canvas.Children.Add(image);
        }
        root.Children.Add(canvas);
        root.Measure(new Size(FrameW, FrameH));
        root.Arrange(new Rect(0, 0, FrameW, FrameH));
        root.UpdateLayout();
        var rtb = new RenderTargetBitmap(FrameW, FrameH, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(root);
        string path = Path.Combine(Out, "store-shots", name + ".png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using (var f = File.Create(path)) enc.Save(f);
        Log.Add($"png store-shots/{name}.png {FrameW}x{FrameH}");
    }

    /// <summary>Settings on the left, the widget beside it, both centered on the frame.</summary>
    static void Pair(string name, BitmapSource settings, BitmapSource? widget)
    {
        const double gap = 110;
        double total = settings.PixelWidth + (widget is null ? 0 : gap + widget.PixelWidth);
        double x = (FrameW - total) / 2;
        double y = (FrameH - settings.PixelHeight) / 2;
        if (widget is null)
        {
            Frame(name, (settings, x, y));
            return;
        }
        Frame(name, (settings, x, y), (widget, x + settings.PixelWidth + gap, (FrameH - widget.PixelHeight) / 2 - 60));
    }

    static async Task RenderStoreShots(WidgetViewModel vm, Assembly asm)
    {
        Sanduhr.App.Services.WidgetCatalogService.HandlerOverride = () => new OfflineHandler();
        var svm = new SettingsViewModel(vm, () => Task.CompletedTask, _ => Task.CompletedTask);
        AssertOnlySampleAccounts(svm);
        var swType = asm.GetType("Sanduhr.App.Views.SettingsWindow")!;
        var sw = (Window)Activator.CreateInstance(swType, svm)!;
        OffScreen(sw);
        sw.Show();
        sw.Left = -20000;
        sw.Top = -20000;
        sw.Width = 900;
        sw.Height = 820;
        await Settle2();
        NeverActivate(sw);
        var tabs = (TabControl)sw.FindName("Tabs");
        tabs.ApplyTemplate();
        var c = new PassCtx
        {
            Sw = sw, Tabs = tabs, Svm = svm, Select = swType.GetMethod("SelectPage")!,
            Root = (FrameworkElement)sw.Content,
            Host = (ContentPresenter)tabs.Template.FindName("PART_SelectedContentHost", tabs),
        };

        var main = new Sanduhr.App.MainWindow { DataContext = vm };
        OffScreen(main);
        main.Show();
        main.Left = -20000;
        await Settle2();
        // Dressed for the Store: no model name on a meter (the captions' rule holds for the pictures too), and the demo
        // banner and footer folded away. The numbers stay samples.
        foreach (var t in vm.Tiers.Where(t => t.TierKey == TierModel.SevenDaySonnet).ToList())
            vm.Tiers.Remove(t);
        foreach (var b in Descendants<Border>((DependencyObject)main.Content).Where(b => System.Windows.Automation.AutomationProperties.GetName(b) == vm.DemoBanner))
            b.Visibility = Visibility.Collapsed;
        vm.FooterText = "";
        main.SizeToContent = SizeToContent.Height;
        await Settle2();
        BitmapSource Widget() => Snap((FrameworkElement)main.Content, ShotScale);
        BitmapSource Settings() => Snap(c.Root, ShotScale);

        async Task Open(SettingsPage page, string? anchor = null, string? view = null)
        {
            c.Svm.Search.Clear();
            await SelectUnitTop(c, page, view);
            if (anchor is not null)
            {
                c.Select.Invoke(sw, [page, anchor]);
                await Settle2();
            }
        }

        try
        {
            // 1. Search: every setting one query away.
            await Open(SettingsPage.General);
            c.Svm.Search.Query = "update";
            await Settle2();
            Pair("01-search", Settings(), Widget());
            c.Svm.Search.Clear();
            await Settle();

            // 2. Claude Usage, Overview: a month of local Claude Code burn.
            await Open(SettingsPage.ClaudeUsage, view: SettingsIndex.OverviewView);
            Pair("02-overview", Settings(), Widget());

            // 3. Claude Usage, Meters.
            await Open(SettingsPage.ClaudeUsage, view: SettingsIndex.MetersView);
            Pair("03-meters", Settings(), null);

            // 4. Claude Code: statusline, band and MCP per folder.
            await Open(SettingsPage.ClaudeCode);
            Pair("04-claude-code", Settings(), null);

            // 5. Mods & Widgets: one list for both.
            await Open(SettingsPage.Mods);
            Pair("05-mods-widgets", Settings(), null);

            // 6. Privacy: every destination, and what Claude can change.
            await Open(SettingsPage.Privacy, SettingsIndex.Network);
            Pair("06-privacy", Settings(), null);

            // 7. Streamer mode on: codenames on the widget and in Settings.
            vm.Streamer.SetOn(true);
            await Settle2();
            await Open(SettingsPage.Privacy, SettingsIndex.Streamer);
            Pair("07-streamer", Settings(), Widget());
            vm.Streamer.SetOn(false);
            await Settle2();

            // 8. Themes.
            await Open(SettingsPage.Themes);
            Pair("08-themes", Settings(), Widget());
        }
        finally
        {
            main.Close();
            sw.Close();
        }
    }
}
