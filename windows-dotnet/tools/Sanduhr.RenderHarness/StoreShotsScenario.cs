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

    /// <summary>The Desk as the Store shows it: every piece in the active theme's look (follow on, the message in the theme's
    /// colors), an invented song on Now playing (never a real artist), over a dark wallpaper stand-in, with the widget on top.</summary>
    static async Task RenderStoreDesk(BitmapSource? widget, string themeKey, string layout, string name, double widgetLeft, double widgetTop,
        Sanduhr.App.Desk.CompanionService? companion = null, FrameworkElement? overlay = null)
    {
        // The widget's demo resets count from the real clock, so the Desk uses it too and the two agree.
        var now = DateTimeOffset.Now;
        now = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, now.Offset);
        string Iso(TimeSpan span) => now.Add(span).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffK", System.Globalization.CultureInfo.InvariantCulture);
        var usage = new DeskUsage(
            [new DeskTier(TierModel.FiveHour, 42, Iso(TimeSpan.FromHours(2))),
             new DeskTier(TierModel.SevenDay, 63, Iso(TimeSpan.FromDays(4)))],
            now.AddMinutes(-1), false, false, null);
        var message = new Sanduhr.App.Desk.DeskMessageFrame(new MessageToday(usual: "keep building."), DeskMessageSettings.Default);
        var track = new NowPlayingInfo
        {
            Title = "Glass Hours", Artist = "The Pace Markers", Duration = 236, Elapsed = 94, ElapsedAt = now, Playing = true,
        };
        var audio = new Sanduhr.App.Desk.AudioLevelSource();
        float[] levels = [0.1f, 0.35f, 0.6f, 0.45f, 0.85f, 0.55f, 0.95f, 0.7f, 0.4f, 0.8f, 0.6f, 0.3f, 0.7f, 0.5f, 0.75f, 0.35f];
        foreach (var l in levels) audio.Bars.Push(l * l, now);
        var telemetry = new Sanduhr.App.Desk.TelemetrySource();
        telemetry.Preview(new TelemetrySample(23, 61, 48, now));
        var place = WeatherPlace.Create("Lakeside", "Texas", "United States", 32.7555, -97.3308);
        var reading = new WeatherReading(68, 0, 79, 56, 1, 81, 60, now.AddMinutes(-5));

        var theme = ThemeCatalog.BuiltIns[themeKey];
        var draw = Sanduhr.App.Desk.DeskLookResolver.For(true, theme)!;
        var wallTop = Color.FromRgb(0x14, 0x12, 0x24);
        var wallBottom = Color.FromRgb(0x05, 0x06, 0x0a);
        var guarded = draw with { Look = draw.Look.Guarded(0.01) };
        var screen = new Sanduhr.App.Desk.DeskScreen("DISPLAY1", new PxRect(0, 0, 1920, 1080), new PxRect(0, 0, 1920, 1032), 1.0, true,
            TaskbarClearance.Insets(new PxRect(0, 0, 1920, 1080), new PxRect(0, 0, 1920, 1032), [], 1.0));
        var settings = Sanduhr.App.Desk.DeskLookResolver.Settings(DeskSettings.Default with { Enabled = true, Layout = layout }, guarded).Normalized();
        var view = new Sanduhr.App.Desk.DeskView(screen, settings, new Sanduhr.App.Desk.DeskFonts());
        var frame = Sanduhr.App.Desk.DeskLookResolver.Frame(new Sanduhr.App.Desk.DeskFrame
        {
            Usage = usage, Now = now, Message = message, Track = track, Bars = true, Audio = audio,
            Telemetry = telemetry, WeatherOn = true, Place = place, Weather = reading, Companion = companion,
        }, guarded);
        view.Update(frame, new Sanduhr.App.Desk.DeskInkMap(guarded.Ink));

        var accent = Res("Sanduhr.Brush.Accent", Color.FromRgb(0x6c, 0x63, 0xff));
        var root = new Grid { Width = 1920, Height = 1080, Background = new LinearGradientBrush(wallTop, wallBottom, 90) };
        root.Children.Add(new Border
        {
            Background = new RadialGradientBrush(Color.FromArgb(0x40, accent.R, accent.G, accent.B), Colors.Transparent)
            { Center = new Point(0.25, 0.35), GradientOrigin = new Point(0.25, 0.35), RadiusX = 0.6, RadiusY = 0.7 },
        });
        root.Children.Add(new System.Windows.Shapes.Rectangle
        {
            Height = 48, VerticalAlignment = VerticalAlignment.Bottom, Fill = new SolidColorBrush(Color.FromArgb(0xB0, 0x0a, 0x0b, 0x10)),
        });
        root.Children.Add(view);
        if (overlay is not null)
        {
            overlay.HorizontalAlignment = HorizontalAlignment.Center;
            overlay.VerticalAlignment = VerticalAlignment.Top;
            overlay.Margin = new Thickness(0, 8, 0, 0);
            root.Children.Add(overlay);
        }
        if (widget is not null)
        {
        var w = new Image
        {
            Source = widget, Stretch = Stretch.Fill, Width = widget.PixelWidth / ShotScale, Height = widget.PixelHeight / ShotScale,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(widgetLeft, widgetTop, 0, 0),
            Effect = new DropShadowEffect { BlurRadius = 50, ShadowDepth = 12, Direction = 270, Opacity = 0.6, Color = Colors.Black },
        };
        RenderOptions.SetBitmapScalingMode(w, BitmapScalingMode.HighQuality);
        root.Children.Add(w);
        }
        root.Measure(new Size(1920, 1080));
        root.Arrange(new Rect(0, 0, 1920, 1080));
        await Settle2();
        AssertNoUserName(root);
        double s = FrameW / 1920.0;
        var rtb = new RenderTargetBitmap(FrameW, FrameH, 96 * s, 96 * s, PixelFormats.Pbgra32);
        rtb.Render(root);
        string path = Path.Combine(Out, "store-shots", name + ".png");
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using (var f = File.Create(path)) enc.Save(f);
        Log.Add($"png store-shots/{name}.png {FrameW}x{FrameH}");
        view.Dispose();
        audio.Dispose();
        telemetry.Dispose();
    }

    /// <summary>The propose_widget path as the review scenario runs it (the pack built as the MCP server builds it, handed to a real
    /// WidgetReviewService over the dev profile), drawn on the Store Desk instead of the bare one.</summary>
    static async Task RenderStoreReview()
    {
        var paths = new Paths();
        var store = new Sanduhr.App.Services.SettingsStore(paths);
        // The Desk on, so the bar does not say it shows for this preview only.
        store.SaveDesk(DeskSettings.Default with { Enabled = true });
        var now = DateTimeOffset.Now;
        var build = WidgetProposal.Build(BlobArgs(), now);
        if (build.Pack is not { } pack) throw new InvalidOperationException("store-shots: the sample widget did not build: " + string.Join("; ", build.Reasons));
        var host = new HarnessWidgetHost();
        using var service = new Sanduhr.App.Desk.WidgetReviewService(paths, store, clock: () => now) { Host = host };
        service.Handoff.Receive(Encoding.UTF8.GetBytes(WidgetProposal.RequestJson("store-1", pack, "A tiny friend in your Desk's colors, drawn as pixels.", now).ToJsonString()));
        if (host.Preview?.Staged is not { } staged || host.Model is not { } model) throw new InvalidOperationException("store-shots: the sample widget was not staged.");
        using var companion = new Sanduhr.App.Desk.CompanionService(paths, store, claudeCodeDays: () => [], lastInput: () => now, clock: () => now, reduceMotion: () => false);
        companion.PreviewPack(staged, pack.Id);
        companion.Reload();
        companion.Step(now);
        var bar = new Sanduhr.App.Desk.DeskReviewBar();
        bar.SetModel(model);
        await RenderStoreDesk(null, "obsidian",
            "message:tr clock:tr claude:tr telemetry:tr:1:0:ring weather:mr meters:br nowPlaying:bl companion:bc:1.6", "11-desk-review", 0, 0,
            companion, bar);
        companion.EndPreviewPack();
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

            // 9. The Desk following the widget theme, with a song up, and the widget over it.
            await RenderStoreDesk(Widget(), "obsidian",
                "message:tr clock:tr claude:tr telemetry:tr:1:0:ring weather:mr meters:br nowPlaying:bc", "09-desk", 160, 150);

            // 10. The fold, in Aurora: the widget re-tinted with it, the Desk following.
            vm.ApplyThemeByKey("aurora");
            vm.ApplyThemeResources();
            await Settle2();
            await RenderStoreDesk(Widget(), "aurora",
                "clock:ml:1:-18 message:ml:1:-18 weather:ml:1:-18 claude:mr:1:18:slim telemetry:mr:1:18:ring meters:mr:1:18:ticks nowPlaying:bc",
                "10-desk-fold", 760, 70);
            vm.ApplyThemeByKey("obsidian");
            vm.ApplyThemeResources();
            await Settle2();

            // 11. A widget Claude designed: its sprite staged as the companion, the review bar over the Desk.
            await RenderStoreReview();
        }
        finally
        {
            main.Close();
            sw.Close();
        }
    }
}
