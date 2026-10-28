using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sanduhr.App.Desk;
using Sanduhr.App.Interop;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

// The Desk Launcher scenario (spec, Desk Launcher): the piece drawn on the Desk (a row with labels, a column, a dimmed
// missing app, a hovered and a pressed button) and Settings, Desk, Launcher with buttons added. Nothing here reads the
// real Start menu: a stand-in catalog lists three invented apps with drawn icons, so no installed program's name or
// icon reaches a picture.
public static partial class Program
{
    /// <summary>Three invented apps with icons drawn here.</summary>
    private sealed class FakeCatalog : ILauncherAppCatalog
    {
        private static readonly (string Name, string Id, Color Color)[] Apps =
        [
            ("Acme Dashboard", "Acme.Dashboard_0123456789abc!App", Color.FromRgb(0x2e, 0x6f, 0xd1)),
            ("Acme Music", "Acme.Music_0123456789abc!App", Color.FromRgb(0xd1, 0x4a, 0x7a)),
            ("Acme Notes", "Acme.Notes", Color.FromRgb(0x3a, 0xa3, 0x6a)),
        ];

        public IReadOnlyList<LauncherAppInfo> Enumerate() => [.. Apps.Select(a => new LauncherAppInfo(a.Name, a.Id))];

        public bool Exists(string aumid) => Apps.Any(a => a.Id == aumid);

        public BitmapSource? Icon(string aumid, int px) =>
            Apps.Where(a => a.Id == aumid).Select(a => Draw(a.Name[5..6], a.Color, px)).FirstOrDefault();
    }

    /// <summary>A rounded colored square with a letter: a stand-in for an app's icon.</summary>
    internal static BitmapSource Draw(string letter, Color color, int px)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRoundedRectangle(new SolidColorBrush(color), null, new Rect(px * 0.04, px * 0.04, px * 0.92, px * 0.92), px * 0.2, px * 0.2);
            var text = new FormattedText(letter, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI Semibold"), px * 0.5, Brushes.White, 1.0);
            dc.DrawText(text, new Point((px - text.Width) / 2, (px - text.Height) / 2));
        }
        var rtb = new RenderTargetBitmap(px, px, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    private static async Task RenderLauncher(WidgetViewModel vm, Assembly asm, bool light)
    {
        var paths = new Paths();
        var store = new SettingsStore(paths);
        var cache = new LauncherAppCache(() => new FakeCatalog());
        LauncherAppCache.Current = cache;
        cache.Refresh([]);
        await Settle();

        // A chosen picture, made the way Settings makes one: drawn, stored by hash, loaded back.
        string pictureFile = Path.Combine(paths.AppDataDir, "harness-picture.png");
        using (var f = File.Create(pictureFile))
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(Draw("P", Color.FromRgb(0xe0, 0x8a, 0x1e), 300)));
            enc.Save(f);
        }
        var imported = LauncherImages.Import(paths, pictureFile);
        Log.Add($"launcher: picture import ok={imported.Ok} {imported.Error}");

        var model = new LauncherModel(LauncherOrientation.Row,
        [
            LauncherButton.Create(LauncherKind.App, "Acme.Dashboard_0123456789abc!App", null, "Dashboard", "#4aa8ff")!,
            LauncherButton.Create(LauncherKind.App, "Acme.Music_0123456789abc!App", null, "Music", null)!,
            LauncherButton.Create(LauncherKind.Link, "https://www.example.com/status", imported.FileName, "Status", "#e08a1e")!,
            LauncherButton.Create(LauncherKind.Link, "https://example.org/", null, null, null)!,
            LauncherButton.Create(LauncherKind.App, "Gone.App_0000000000000!App", null, "Old app", null)!,
        ]);

        // ---- the Desk ----
        var fonts = new DeskFonts();
        foreach (double scale in new[] { 1.0, 1.5 })
        {
            var bounds = new PxRect(0, 0, (int)(1920 * scale), (int)(1080 * scale));
            int bar = (int)Math.Round(48 * scale);
            var work = new PxRect(0, 0, bounds.Width, bounds.Height - bar);
            var screen = new DeskScreen("DISPLAY1", bounds, work, scale, true, TaskbarClearance.Insets(bounds, work, [], scale));
            foreach (var (gname, gcolor) in new[] { ("dark", Color.FromRgb(0x1e, 0x24, 0x30)), ("light", Color.FromRgb(0xd8, 0xdd, 0xe6)) })
            foreach (var (name, layout, orientation, count) in new[]
            {
                ("row", "launcher:bl:1 clock:tl", LauncherOrientation.Row, 5),
                ("row-big", "launcher:bc:1.6", LauncherOrientation.Row, 3),
                ("column", "launcher:mr clock:tl", LauncherOrientation.Column, 5),
                ("row-small", "launcher:tr:0.6", LauncherOrientation.Row, 4),
            })
            {
                var m = new LauncherModel(orientation, [.. model.Buttons.Take(count)]);
                var images = new Dictionary<string, ImageSource>(StringComparer.Ordinal);
                if (imported.FileName is { } file && LauncherImages.Load(paths, file) is { } loaded) images[file] = loaded;
                var icons = new Dictionary<string, ImageSource>(StringComparer.Ordinal)
                {
                    ["Acme.Dashboard_0123456789abc!App"] = Draw("D", Color.FromRgb(0x2e, 0x6f, 0xd1), 256),
                    ["Acme.Music_0123456789abc!App"] = Draw("M", Color.FromRgb(0xd1, 0x4a, 0x7a), 256),
                };
                var names = new Dictionary<string, string>
                {
                    ["Acme.Dashboard_0123456789abc!App"] = "Acme Dashboard",
                    ["Acme.Music_0123456789abc!App"] = "Acme Music",
                };
                var frame = new DeskFrame
                {
                    Now = new DateTimeOffset(2026, 10, 21, 10, 9, 0, TimeSpan.Zero),
                    Launcher = new DeskLauncherFrame(m, new HashSet<string> { "Gone.App_0000000000000!App" }, icons, names, images),
                };
                var settings = (DeskSettings.Default with { Enabled = true, Layout = layout }).Normalized();
                var view = new DeskView(screen, settings, fonts);
                view.Update(frame, DeskInkMap.Light);
                var root = new Grid { Width = 1920, Height = 1080, Background = new SolidColorBrush(gcolor) };
                root.Children.Add(view);
                root.Measure(new Size(1920, 1080));
                root.Arrange(new Rect(0, 0, 1920, 1080));
                // The second button hovered, the third pressed: the glow and the dip, as the mouse and touch draw them.
                var buttons = new List<LauncherButtonVisual>();
                void Walk(DependencyObject d)
                {
                    if (d is LauncherButtonVisual b) buttons.Add(b);
                    foreach (var child in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>()) Walk(child);
                }
                Walk(view);
                if (name is "row" or "column" && buttons.Count > 2)
                {
                    buttons[1].SetGlow(true);
                    buttons[2].SetGlow(true);
                    buttons[2].SetDip(true);
                }
                await Settle();
                SaveScaled(root, $"launcher-desk-{name}-{gname}-{scale:0.0}x".Replace('.', '_'), scale);
            }
        }

        // ---- Settings, Desk, Launcher ----
        store.SaveLauncher(model);
        var layoutNow = store.LoadDesk();
        store.SaveDesk(layoutNow with { Enabled = true, Layout = "clock:bl claude:bl" });
        var svm = new SettingsViewModel(vm, () => Task.CompletedTask, _ => Task.CompletedTask);
        var swType = asm.GetType("Sanduhr.App.Views.SettingsWindow")!;
        var sw = (Window)Activator.CreateInstance(swType, svm)!;
        OffScreen(sw);
        sw.Show();
        sw.Left = -20000;
        sw.Top = -20000;
        sw.Height = 1900;
        await Settle();
        var select = swType.GetMethod("SelectPage")!;
        select.Invoke(sw, [SettingsPage.Launcher, SettingsIndex.LauncherButtons]);
        svm.Desk.Launcher.Select(4);
        await Settle();
        Save((FrameworkElement)sw.Content, "launcher-settings-hidden");
        // Placed: the note goes.
        svm.Desk.Pieces.First(p => p.Widget == "launcher").Place = DeskSettingsViewModel.PlaceFor(DeskAnchor.BL);
        await Settle();
        Save((FrameworkElement)sw.Content, "launcher-settings-placed");
        svm.Desk.Launcher.SelectedOrientation = "Column";
        await Settle();
        Log.Add($"launcher: settings rows={svm.Desk.Launcher.Rows.Count} orientation={svm.Desk.Launcher.SelectedOrientation} names={string.Join("|", svm.Desk.Launcher.Rows.Select(r => r.Title))}");
        sw.Close();
        await Settle();

        // ---- the pickers ----
        var picker = new Sanduhr.App.Views.LauncherAppPickerWindow(cache);
        OffScreen(picker);
        picker.Show();
        picker.Left = -20000;
        await Settle();
        Save((FrameworkElement)picker.Content, "launcher-picker");
        picker.Close();
        var link = new Sanduhr.App.Views.LauncherLinkWindow();
        OffScreen(link);
        link.Show();
        link.Left = -20000;
        await Settle();
        Save((FrameworkElement)link.Content, "launcher-link");
        link.Close();
        cache.Dispose();
        LauncherAppCache.Current = null;
        try { File.Delete(pictureFile); } catch (IOException) { }
    }
}
