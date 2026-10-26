using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

// The Desk companion scenario (spec 3.8 section 4): the first-party hourglass installed into the dev profile's widgets folder
// through WidgetStore (validated as the catalog client would), then drawn on a composited Desk the way the host places each
// island: every stage sitting beside the clock, a mood and a trigger, and one roaming frame part way along the bottom. Then
// Settings, Desk, Pieces with the Companion row. No network (the catalog fetch gets a 503), no Desk windows, no real
// widgets folder, no session logs read (growth is set by hand).
public static partial class Program
{
    private sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }

    /// <summary>The hourglass pack: the test fixtures' copy of sanduhr-widgets' first-party pack, found above the binary.</summary>
    private static string? FindHourglass()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string a = Path.Combine(dir.FullName, "windows-dotnet", "tests", "Sanduhr.Tests", "fixtures", "widget-packs", "packs", "hourglass");
            if (Directory.Exists(a)) return a;
            string b = Path.Combine(dir.FullName, "tests", "Sanduhr.Tests", "fixtures", "widget-packs", "packs", "hourglass");
            if (Directory.Exists(b)) return b;
        }
        return null;
    }

    /// <summary>A screen's islands placed as <see cref="DesktopLayerHost"/> places them (aligned edge on the anchor), on a background.</summary>
    private static FrameworkElement Composite(IReadOnlyList<DeskIsland> islands, Color background)
    {
        var canvas = new Canvas { Width = 1920, Height = 1080, Background = new SolidColorBrush(background), ClipToBounds = true };
        foreach (var island in islands)
        {
            var host = new Border { Child = island.Content };
            host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = host.DesiredSize.Width, h = host.DesiredSize.Height;
            var at = island.Motion?.Anchor ?? island.Anchor;
            Canvas.SetLeft(host, at.X - island.Horizontal switch { HorizontalAlignment.Right => w, HorizontalAlignment.Center => w / 2, _ => 0 });
            Canvas.SetTop(host, at.Y - island.Vertical switch { VerticalAlignment.Bottom => h, VerticalAlignment.Center => h / 2, _ => 0 });
            canvas.Children.Add(host);
        }
        // The taskbar's band, so the sprite's footing reads.
        var bar = new Border { Width = 1920, Height = 48, Background = new SolidColorBrush(Color.FromArgb(0xE0, 0x10, 0x12, 0x18)) };
        Canvas.SetTop(bar, 1032);
        canvas.Children.Add(bar);
        canvas.Measure(new Size(1920, 1080));
        canvas.Arrange(new Rect(0, 0, 1920, 1080));
        return canvas;
    }

    private static async Task RenderCompanion(WidgetViewModel vm, Assembly asm)
    {
        var paths = new Paths();
        var store = new SettingsStore(paths);
        string? source = FindHourglass();
        if (source is null)
        {
            Log.Add("companion: the hourglass pack was not found above the binary; skipped");
            return;
        }
        var files = Directory.GetFiles(source, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(source, f).Replace('\\', '/'), File.ReadAllBytes, StringComparer.Ordinal);
        var verdict = PackValidator.Validate(files, folderName: "hourglass", strict: true);
        Log.Add($"companion: hourglass validates ok={verdict.Ok} {string.Join("; ", verdict.Errors)}");
        if (verdict.Manifest is not { } manifest) return;
        var widgets = new WidgetStore(paths.WidgetsDir, WidgetCatalogService.EncodePng);
        var installed = widgets.Install(files, manifest, WidgetSource.Catalog);
        Log.Add($"companion: installed into the dev profile ok={installed.Ok} {installed.Error} at {widgets.Root}");
        store.SaveCompanion(new CompanionSettings("hourglass", false));

        var now = new DateTimeOffset(2026, 10, 22, 15, 9, 0, TimeSpan.Zero);
        var screen = new DeskScreen("DISPLAY1", new PxRect(0, 0, 1920, 1080), new PxRect(0, 0, 1920, 1032), 1.0, true,
            TaskbarClearance.Insets(new PxRect(0, 0, 1920, 1080), new PxRect(0, 0, 1920, 1032), [], 1.0));
        var fonts = new DeskFonts();
        var usage = new DeskUsage(
            [new DeskTier(TierModel.FiveHour, 34, Iso(now + TimeSpan.FromHours(2.5))), new DeskTier(TierModel.SevenDay, 41, Iso(now + TimeSpan.FromDays(3)))],
            now, false, false, null);

        IReadOnlyList<DeskIsland> Draw(CompanionService service, string layout)
        {
            var settings = (DeskSettings.Default with { Enabled = true, Layout = layout, Plate = false }).Normalized();
            var view = new DeskView(screen, settings, fonts);
            view.Update(new DeskFrame { Now = now, Usage = usage, Companion = service }, DeskInkMap.Light);
            return view.TakeIslands();
        }

        // ---- each stage, sitting (content) beside the clock, on a dark and a light wallpaper ----
        double[] growth = [0, 0.1, 0.35, 0.75];
        string[] names = ["egg", "sprout", "runner", "keeper"];
        for (int i = 0; i < growth.Length; i++)
        {
            using var service = new CompanionService(paths, store, claudeCodeDays: () => [], lastInput: () => now, clock: () => now, reduceMotion: () => false);
            service.Reload();
            service.ApplyGrowth(new CompanionGrowthState(growth[i], new HashSet<DateOnly>(), TimeSpan.Zero));
            service.ObserveUsage(usage);
            service.Step(now);
            Log.Add($"companion: stage {service.Stage?.Id} cell {service.CellSize} pose {service.Pose}");
            foreach (var (bg, color) in new[] { ("dark", Color.FromRgb(0x1e, 0x24, 0x30)), ("light", Color.FromRgb(0xd8, 0xdd, 0xe6)) })
            {
                SaveScaled(Composite(Draw(service, "clock:bl claude:bl companion:br:1.6"), color), $"companion-{i + 1}-{names[i]}-{bg}", 1.0);
                await Settle();
            }
        }

        // ---- a trigger (needs_you) and a mood (worried), at the keeper stage ----
        {
            using var service = new CompanionService(paths, store, claudeCodeDays: () => [], lastInput: () => now, clock: () => now, reduceMotion: () => false);
            service.Reload();
            service.ApplyGrowth(new CompanionGrowthState(0.8, new HashSet<DateOnly>(), TimeSpan.Zero));
            Watcher W(WatcherState s) => new() { Id = "a:1", Source = WatcherSource.Agent, Title = "deploy", State = s, Started = now, Touched = now };
            service.ObserveWatchers([W(WatcherState.Running)]);
            service.ObserveWatchers([W(WatcherState.Waiting)]);
            service.Step(now + TimeSpan.FromSeconds(0.25));
            Log.Add($"companion: needs_you pose {service.Pose}");
            SaveScaled(Composite(Draw(service, "clock:bl companion:bc:1.6"), Color.FromRgb(0x1e, 0x24, 0x30)), "companion-trigger-needs-you", 1.0);
            service.ObserveUsage(new DeskUsage([new DeskTier(TierModel.SevenDay, 90, Iso(now + TimeSpan.FromDays(1)))], now, false, false, null));
            service.Step(now + TimeSpan.FromSeconds(5));
            Log.Add($"companion: weekly 90% mood {service.Mood} pose {service.Pose}");
            SaveScaled(Composite(Draw(service, "clock:bl companion:bc:1.6"), Color.FromRgb(0x1e, 0x24, 0x30)), "companion-mood-worried", 1.0);
            await Settle();
        }

        // ---- roaming: part way along the bottom, mid-walk ----
        store.SaveCompanion(new CompanionSettings("hourglass", true));
        {
            using var service = new CompanionService(paths, store, claudeCodeDays: () => [], lastInput: () => now, clock: () => now, reduceMotion: () => false);
            service.Reload();
            service.ApplyGrowth(new CompanionGrowthState(0.35, new HashSet<DateOnly>(), TimeSpan.Zero));
            var islands = Draw(service, "clock:bl claude:bl companion:bl:1.6");
            service.Want(service, true);
            for (int s = 1; s <= 70; s++) service.Step(now + TimeSpan.FromSeconds(s * 0.1));
            var walker = islands.Single(i => i.Motion is not null);
            Log.Add($"companion: roaming at {walker.Motion!.Anchor} walking={service.Walking} pose {service.Pose}");
            SaveScaled(Composite(islands, Color.FromRgb(0x1e, 0x24, 0x30)), "companion-roaming", 1.0);
            service.Want(service, false);
            await Settle();
        }

        // ---- Settings, Desk, Pieces ----
        var desk = store.LoadDesk();
        store.SaveDesk(desk with { Enabled = true, Layout = "clock:bl claude:bl companion:br" });
        var svm = new SettingsViewModel(vm, () => Task.CompletedTask, _ => Task.CompletedTask)
        {
            ModsPage = new ModsPageViewModel(() => new WidgetCatalogService(paths, new NoNetwork()), store),
        };
        var swType = asm.GetType("Sanduhr.App.Views.SettingsWindow")!;
        var sw = (Window)Activator.CreateInstance(swType, svm)!;
        OffScreen(sw);
        sw.Show();
        sw.Left = -20000;
        sw.Top = -20000;
        sw.Height = 1100;
        await Settle();
        swType.GetMethod("SelectPage")!.Invoke(sw, [SettingsPage.Desk, SettingsIndex.DeskPieces]);
        await Settle();
        await Settle();
        Log.Add($"companion: settings companions={svm.Desk.Companion.Companions.Count} shown={svm.Desk.Pieces.Single(p => p.Widget == CompanionSettings.Widget).Shown} roams={svm.Desk.Companion.Roams} status={svm.Desk.Companion.Status}");
        Save((FrameworkElement)sw.Content, "companion-settings-desk");
        sw.Close();
        await Settle();
    }

    private static string Iso(DateTimeOffset t) => t.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
}
