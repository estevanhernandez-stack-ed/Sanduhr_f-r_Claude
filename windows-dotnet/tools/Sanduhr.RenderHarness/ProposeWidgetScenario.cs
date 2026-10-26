using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

// Widgets made with Claude (spec 3.8 section 5): a 16 px pixel sprite built the way propose_widget builds it (pack.json, the
// LICENSE, sprite.json and a pixel sheet, validated strict), handed to a real WidgetReviewService over the dev profile as the
// request file would bring it, staged and drawn by the companion's own loader beside the clock with the review bar over the
// Desk; then the bar alone, Apply, and Settings, Widgets with the pack listed "made here" and Use my own art… on its row.
// Off screen only: no Desk windows, no network (the catalog fetch gets a 503), no real widgets folder.
public static partial class Program
{
    /// <summary>One 16 x 16 frame of a round blob: body 0, outline 1, cheeks 2, pupils 3, eye whites 4.</summary>
    private static JsonArray BlobFrame(double cy, double rx, double ry, bool eyesClosed, int feetShift)
    {
        var rows = new JsonArray();
        int eyeRow = (int)Math.Round(cy - ry * 0.25);
        for (int y = 0; y < 16; y++)
        {
            var row = new StringBuilder(16);
            for (int x = 0; x < 16; x++)
            {
                double dx = (x - 7.5) / rx, dy = (y - cy) / ry, d = dx * dx + dy * dy;
                char c = '.';
                if (d <= 1) c = d > 0.62 ? '1' : '0';
                if (c == '0' && y == eyeRow && x is 5 or 6 or 9 or 10)
                    c = eyesClosed ? '3' : (x is 5 or 9 ? '4' : '3');
                if (c == '0' && !eyesClosed && y == eyeRow - 1 && x is 5 or 6 or 9 or 10) c = '4';
                if (c == '0' && y == eyeRow + 2 && x is 4 or 11) c = '2';
                if (y == 15 && c == '.' && x == 5 + feetShift || y == 15 && c == '.' && x == 10 - feetShift) c = '1';
                row.Append(c);
            }
            rows.Add((JsonNode?)row.ToString());
        }
        return rows;
    }

    private static JsonObject BlobArgs() => new()
    {
        ["kind"] = "sprite",
        ["pack"] = new JsonObject { ["id"] = "neon-blob", ["name"] = "Neon blob", ["description"] = "A round little blob that hops along the taskbar." },
        ["note"] = "A tiny friend in your Desk's neon, drawn as pixels.",
        ["sprite"] = JsonNode.Parse("""
            {
              "schema_version": 2, "frame_w": 16, "frame_h": 16, "scale": 3, "fps": 6, "sheet": "blob.json",
              "animations": { "walk": [0, 1], "sit": [0], "nap": [3], "pop": [2, 0], "cheer": [2, 0, 2], "worry": [1], "focus": [0], "sleep": [3], "celebrate": [2, 1, 2] },
              "moods": { "content": "sit", "focused": "focus", "worried": "worry", "exhausted": "sleep", "celebrate": "celebrate" },
              "triggers": { "needs_you": "pop", "job_passed": "cheer", "near_limit": "worry", "idle": "nap" },
              "roam": { "edges": ["bottom"], "speed_px_s": 24 }
            }
            """),
        ["sheets"] = new JsonObject
        {
            ["blob.json"] = new JsonObject
            {
                ["palette"] = new JsonArray("#22d3ee", "#7c3aed", "#ec4899", "#0f172a", "#f0f0f0"),
                ["frame_w"] = 16,
                ["frame_h"] = 16,
                ["frames"] = new JsonArray(
                    BlobFrame(9.5, 6.5, 5.5, false, 0),
                    BlobFrame(10.2, 7.2, 4.8, false, 1),
                    BlobFrame(7.5, 6.0, 6.0, false, 0),
                    BlobFrame(9.5, 6.5, 5.5, true, 0)),
            },
        },
    };

    /// <summary>The bar and the Desk, as the review would draw them, kept by the capturing host.</summary>
    private sealed class HarnessWidgetHost : IWidgetReviewHost
    {
        public WidgetPreview? Preview;
        public DeskReviewModel? Model;
        public Action<bool>? Apply;
        public bool DeskBusy => false;

        public void ShowPreview(WidgetPreview preview, DeskReviewModel model, Action<bool> apply, Action dismiss)
        {
            Preview = preview;
            Model = model;
            Apply = apply;
        }

        public void EndPreview() { }
        public void Installed(string packId) { }
    }

    private static async Task RenderProposeWidget(WidgetViewModel vm, Assembly asm)
    {
        var paths = new Paths();
        var store = new SettingsStore(paths);
        store.SaveDesk(DeskSettings.Default with { Enabled = true, Layout = "clock:bl claude:bl" });
        var now = new DateTimeOffset(2026, 10, 22, 15, 9, 0, TimeSpan.Zero);

        // ---- the pack, as the MCP server builds it, and the request the widget reads ----
        var build = WidgetProposal.Build(BlobArgs(), now);
        Log.Add($"propose-widget: build ok={build.Ok} {string.Join("; ", build.Reasons)}");
        if (build.Pack is not { } pack) return;
        Log.Add($"propose-widget: files {string.Join(", ", pack.Files.Keys.Order(StringComparer.Ordinal))}");

        var host = new HarnessWidgetHost();
        using var service = new WidgetReviewService(paths, store, clock: () => now) { Host = host };
        service.Handoff.Receive(Encoding.UTF8.GetBytes(WidgetProposal.RequestJson("harness-1", pack, "A tiny friend in your Desk's neon, drawn as pixels.", now).ToJsonString()));
        string answer = File.ReadAllText(Path.Combine(paths.ExternalDir, WidgetProposal.ResultFile));
        Log.Add($"propose-widget: answer {answer}");
        if (host.Preview?.Staged is not { } staged || host.Model is not { } model) return;

        // ---- the Desk with the staged sprite as the companion, and the bar over it ----
        var screen = new DeskScreen("DISPLAY1", new PxRect(0, 0, 1920, 1080), new PxRect(0, 0, 1920, 1032), 1.0, true,
            TaskbarClearance.Insets(new PxRect(0, 0, 1920, 1080), new PxRect(0, 0, 1920, 1032), [], 1.0));
        var usage = new DeskUsage(
            [new DeskTier(TierModel.FiveHour, 34, Iso(now + TimeSpan.FromHours(2.5))), new DeskTier(TierModel.SevenDay, 41, Iso(now + TimeSpan.FromDays(3)))],
            now, false, false, null);
        using var companion = new CompanionService(paths, store, claudeCodeDays: () => [], lastInput: () => now, clock: () => now, reduceMotion: () => false);
        companion.PreviewPack(staged, pack.Id);
        companion.Reload();
        companion.ObserveUsage(usage);
        companion.Step(now);
        Log.Add($"propose-widget: companion pack={companion.PackId} previewing={companion.PreviewingPack} cell={companion.CellSize} pose={companion.Pose}");

        var settings = (DeskSettings.Default with { Enabled = true, Layout = "clock:bl claude:bl companion:br:1.4", Plate = false }).Normalized();
        var view = new DeskView(screen, settings, new DeskFonts());
        view.Update(new DeskFrame { Now = now, Usage = usage, Companion = companion }, DeskInkMap.Light);
        var desk = (Canvas)Composite(view.TakeIslands(), Color.FromRgb(0x1e, 0x24, 0x30));
        var bar = new DeskReviewBar();
        bar.SetModel(model);
        bar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(bar, (1920 - bar.DesiredSize.Width) / 2);
        Canvas.SetTop(bar, 8);
        desk.Children.Add(bar);
        desk.Measure(new Size(1920, 1080));
        desk.Arrange(new Rect(0, 0, 1920, 1080));
        await Settle();
        SaveScaled(desk, "propose-widget-preview", 1.0);

        // ---- the bar alone, in its window ----
        var window = new DeskReviewWindow();
        OffScreen(window);
        window.SetModel(model);
        window.Show();
        window.Left = -20000;
        await Settle();
        Save((FrameworkElement)window.Content, "propose-widget-bar");
        Log.Add($"propose-widget: bar heading={window.ReviewBar.Heading.Text} option={window.ReviewBar.OptionChecked} applyEnabled={window.ReviewBar.Apply.IsEnabled}");
        window.Close();
        companion.EndPreviewPack();

        // ---- Apply, then Settings, Widgets ----
        host.Apply!(true);
        Log.Add($"propose-widget: after Apply {File.ReadAllText(Path.Combine(paths.ExternalDir, WidgetProposal.ResultFile))}");
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
        sw.Height = 1300;
        await Settle();
        swType.GetMethod("SelectPage")!.Invoke(sw, [SettingsPage.Widgets, SettingsIndex.WidgetsInstalled]);
        await Settle();
        await Settle();
        Log.Add($"propose-widget: installed={string.Join("|", svm.ModsPage.Installed.Select(r => r.RowName + (r.CanUseOwnArt ? " [own art]" : "")))}");
        Save((FrameworkElement)sw.Content, "propose-widget-settings-installed");
        sw.Close();
        await Settle();
    }
}
