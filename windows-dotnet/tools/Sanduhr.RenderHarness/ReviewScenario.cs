using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

// The live review scenario (spec, the agent-designed Desk): the floating bar in three states (a design to apply, a design waiting for a
// font with its link, the Desk being off) and Settings, Desk with the direct switch and the Designs list. The designs come from
// requests handed to a real DeskDesignService over the dev profile, so the list shows what Apply really keeps.
public static partial class Program
{
    private static async Task RenderReview(WidgetViewModel vm, Assembly asm)
    {
        // ---- the bar ----
        var shown = new DeskReviewModel("Dusk over the lake",
            "Warm ink and a plate, with the clock moved to the top right to sit on the quiet part of the sky.",
            ["Clock and date: moved from bottom left to top right", "Clock and date: size 100% to 140%", "Font: EsteFont Pro to Segoe UI",
             "Plate behind the text: on", "Launcher: 3 buttons in a row", "Widget theme: Obsidian to Dusk"],
            [], false, []);
        var waiting = shown with
        {
            Title = "Stencil Desk",
            MissingFonts = [new DeskReviewFont("Stencil Sans", "https://fonts.example.com/stencil-sans")],
            Warnings = ["theme.accent: reads low against the glass"],
        };
        var off = shown with { Title = "Quiet", Note = null, Changes = ["Plate behind the text: on"], DeskOff = true };
        foreach (var (name, model) in new[] { ("review-bar", shown), ("review-bar-font-waiting", waiting), ("review-bar-desk-off", off) })
        {
            var window = new DeskReviewWindow();
            OffScreen(window);
            window.SetModel(model);
            window.Show();
            window.Left = -20000;
            await Settle();
            Save((FrameworkElement)window.Content, name);
            Log.Add($"review: {name} applyEnabled={window.ReviewBar.Apply.IsEnabled}");
            window.Close();
        }

        // ---- Settings, Desk, direct switch and Designs ----
        var paths = new Paths();
        var store = new SettingsStore(paths);
        store.SaveDesk(DeskSettings.Default with { Enabled = true, Layout = "clock:bl claude:bl", Font = "Segoe UI" });
        store.SaveDeskDirect(true);
        using var service = new DeskDesignService(paths, store) { InstalledFonts = () => ["Segoe UI", "Georgia"] };
        DeskDesignService.Current = service;
        try
        {
            int n = 0;
            foreach (var json in new[]
            {
                """{"name":"Dusk over the lake","plate":true,"layout":{"clock":{"anchor":"tr","size":1.4}}}""",
                """{"name":"Quiet mornings","font":"Georgia","layout":{"clock":{"anchor":"bc"},"claude":{"hidden":true}}}""",
                """{"name":"Back to basics","layout":{"clock":{"anchor":"bl","size":1},"claude":{"anchor":"bl"}},"plate":false}""",
            })
            {
                service.Clock = () => new DateTimeOffset(2026, 10, 21, 9 + n++, 15, 0, TimeSpan.Zero);
                var request = "{\"schema_version\":1,\"id\":\"h" + n + "\",\"requested_at\":\"" + DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'")
                    + "\",\"design\":" + json + "}";
                service.Handoff.Receive(Encoding.UTF8.GetBytes(request));
            }
            service.Designs();
            var designs = service.Designs();
            if (designs.Count > 1) service.Pin(designs[^1].Id, true);
            Log.Add($"review: designs={string.Join("|", service.Designs().Select(d => d.Name + (d.Pinned ? "*" : "")))} putBack={service.CanPutBack}");

            var svm = new SettingsViewModel(vm, () => Task.CompletedTask, _ => Task.CompletedTask);
            svm.Desk.Designs.Reload();
            svm.Desk.Designs.PutBackCommand.Execute(null);
            var swType = asm.GetType("Sanduhr.App.Views.SettingsWindow")!;
            var sw = (Window)Activator.CreateInstance(swType, svm)!;
            OffScreen(sw);
            sw.Show();
            sw.Left = -20000;
            sw.Top = -20000;
            sw.Height = 1900;
            await Settle();
            swType.GetMethod("SelectPage")!.Invoke(sw, [SettingsPage.Desk, SettingsIndex.DeskDirect]);
            await Settle();
            Save((FrameworkElement)sw.Content, "review-settings-direct");
            swType.GetMethod("SelectPage")!.Invoke(sw, [SettingsPage.Desk, SettingsIndex.DeskDesigns]);
            await Settle();
            Save((FrameworkElement)sw.Content, "review-settings-designs");
            // Where each piece sits, with the Monitor dropdown (this PC's own monitors).
            store.SaveDesk(DeskSettings.Default with { Enabled = true, Layout = "clock:bl claude:bl@main launcher:tr meters:tl" });
            svm.Desk.Reload();
            sw.Height = 2700;
            swType.GetMethod("SelectPage")!.Invoke(sw, [SettingsPage.Desk, SettingsIndex.DeskPieces]);
            await Settle();
            if (Descendants<Sanduhr.App.Views.DeskPiecesPanel>(sw).FirstOrDefault() is { } piecesPanel) Save(piecesPanel, "monitor-settings-pieces");
            Log.Add($"review: monitors={string.Join("|", svm.Desk.Monitors.Select(m => m.Name))}");
            Log.Add($"review: status='{svm.Desk.Designs.Status}' rows={svm.Desk.Designs.Rows.Count} canPutBack={svm.Desk.Designs.CanPutBack}");
            sw.Close();
            await Settle();
        }
        finally
        {
            DeskDesignService.Current = null;
        }
    }
}
