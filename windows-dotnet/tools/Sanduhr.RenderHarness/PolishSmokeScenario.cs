using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using Sanduhr.App.Services;
using Sanduhr.App.Theming;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

// The polish smoke (polish-smoke): the owner's six live checks for the 4.0 polish wave, run with no clicks, off screen, against a
// scratch profile and scratch Claude Code folders. Written to <out>/polish-smoke/ (PNGs) and <out>/results.json (a verdict per check,
// with numbers and the method each check used).
//
//   $env:SANDUHR_DEV_PROFILE = "$env:TEMP\some-fresh-folder"   # required, and it must sit under %TEMP%
//   dotnet run --project windows-dotnet/tools/Sanduhr.RenderHarness -c Release -- --scenario polish-smoke --out $env:TEMP\sanduhr-polish-smoke-out
//
// Safety: every path the app resolves is checked to sit under the scratch root; accounts live in an in-memory registry (Credential
// Manager is never opened); Claude Code folders are invented under the scratch root; windows are shown off screen, marked no-activate,
// and a CBT hook refuses every activation; the catalog gets an offline handler and nothing calls Start().
//
// Hover and press are forced through WPF's own read-only property keys (UIElement.IsMouseOverPropertyKey,
// ButtonBase.IsPressedPropertyKey), so the real template triggers run; nothing re-implements a trigger. Checks 5 and 6 run the App test
// classes that already cover them (dotnet test --no-build, so build the App tests in Release first) and read the TRX.
public static partial class Program
{
    sealed class PolishCheck
    {
        public int N;
        public string Title = "";
        public string Verdict = "NOT RUN";
        public string Method = "";
        public JsonObject Numbers = new();
        public List<string> Evidence = new();
        public List<string> Pngs = new();
    }

    static readonly SortedDictionary<int, PolishCheck> Polish = new();
    static string PolishRoot = "";
    static string PolishPng = "";

    static PolishCheck PC(int n) => Polish[n];

    static bool PAssert(int n, bool ok, string what)
    {
        PC(n).Evidence.Add((ok ? "ok: " : "FAIL: ") + what);
        return ok;
    }

    static string Redact(string s)
    {
        if (PolishRoot.Length > 0) s = s.Replace(PolishRoot, "<scratch>", StringComparison.OrdinalIgnoreCase);
        s = s.Replace(Path.GetTempPath().TrimEnd('\\'), "<temp>", StringComparison.OrdinalIgnoreCase);
        if (Environment.UserName.Length > 2) s = s.Replace(Environment.UserName, "<user>", StringComparison.OrdinalIgnoreCase);
        return s;
    }

    // ------------------------------------------------------------------ pixels

    sealed class Px
    {
        public required byte[] B;   // Pbgra32 (premultiplied) or Bgra32 after compositing
        public required int W, H;

        public (byte R, byte G, byte Bl, byte A) At(int x, int y)
        {
            int i = (y * W + x) * 4;
            return (B[i + 2], B[i + 1], B[i], B[i + 3]);
        }
    }

    static Px Grab(Visual v, double width, double height)
    {
        int w = Math.Max(1, (int)Math.Ceiling(width)), h = Math.Max(1, (int)Math.Ceiling(height));
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(v);
        var b = new byte[w * h * 4];
        rtb.CopyPixels(b, w * 4, 0);
        return new Px { B = b, W = w, H = h };
    }

    /// <summary>Source-over onto an opaque backdrop: out = src + (1 - a) * backdrop (the source is premultiplied).</summary>
    static Px Over(Px src, Color backdrop)
    {
        var o = new byte[src.B.Length];
        for (int i = 0; i < o.Length; i += 4)
        {
            int a = src.B[i + 3];
            o[i] = (byte)Math.Min(255, src.B[i] + (255 - a) * backdrop.B / 255.0 + 0.5);
            o[i + 1] = (byte)Math.Min(255, src.B[i + 1] + (255 - a) * backdrop.G / 255.0 + 0.5);
            o[i + 2] = (byte)Math.Min(255, src.B[i + 2] + (255 - a) * backdrop.R / 255.0 + 0.5);
            o[i + 3] = 255;
        }
        return new Px { B = o, W = src.W, H = src.H };
    }

    static string SavePx(Px p, string rel, Int32Rect? crop = null)
    {
        string path = Path.Combine(PolishPng, rel + ".png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        BitmapSource bmp = BitmapSource.Create(p.W, p.H, 96, 96, PixelFormats.Bgra32, null, p.B, p.W * 4);
        if (crop is { } c)
        {
            int x = Math.Clamp(c.X, 0, p.W - 1), y = Math.Clamp(c.Y, 0, p.H - 1);
            int w = Math.Clamp(c.Width, 1, p.W - x), h = Math.Clamp(c.Height, 1, p.H - y);
            bmp = new CroppedBitmap(bmp, new Int32Rect(x, y, w, h));
        }
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using (var f = File.Create(path)) enc.Save(f);
        Log.Add($"polish-smoke: png {rel}.png");
        return path;
    }

    static double Lum(byte r, byte g, byte b)
    {
        static double C(byte v)
        {
            double s = v / 255.0;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * C(r) + 0.7152 * C(g) + 0.0722 * C(b);
    }

    static double Ratio((byte R, byte G, byte B) a, (byte R, byte G, byte B) b)
    {
        double la = Lum(a.R, a.G, a.B), lb = Lum(b.R, b.G, b.B);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    static string Hx((byte R, byte G, byte B) c) => $"#{c.R:x2}{c.G:x2}{c.B:x2}";
    static string Hx(Color c) => c.A == 255 ? $"#{c.R:x2}{c.G:x2}{c.B:x2}" : $"#{c.A:x2}{c.R:x2}{c.G:x2}{c.B:x2}";

    static int Dist((byte R, byte G, byte B) a, Color b) =>
        Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);

    static Rect BoundsIn(FrameworkElement el, Visual root) =>
        el.TransformToAncestor(root).TransformBounds(new Rect(el.RenderSize));

    static double EffectiveOpacity(DependencyObject el, DependencyObject root)
    {
        double o = 1;
        for (var d = el; d is not null && !ReferenceEquals(d, root); d = VisualTreeHelper.GetParent(d))
            if (d is UIElement u) o *= u.Opacity;
        return o;
    }

    sealed record LineMeasure(string Text, double Hybrid, double Peak, string Fg, string Bg);

    /// <summary>Each visible text line inside <paramref name="frame"/>: the background is the commonest pixel in the line's box (pixel
    /// sampled from the composited picture); "hybrid" reads the text color from its brush (with its opacity and its ancestors' composited
    /// over that background), "peak" is the pixel in the box that differs most from the background (anti-aliasing can only lower it).</summary>
    static List<LineMeasure> MeasureLines(FrameworkElement frame, Px img)
    {
        var list = new List<LineMeasure>();
        foreach (var tb in Descendants<TextBlock>(frame))
        {
            string text = tb.Text;
            if (string.IsNullOrWhiteSpace(text) || !tb.IsVisible || tb.ActualWidth < 1 || tb.ActualHeight < 1) continue;
            var r = BoundsIn(tb, frame);
            int x0 = Math.Max(0, (int)Math.Floor(r.Left)), y0 = Math.Max(0, (int)Math.Floor(r.Top));
            int x1 = Math.Min(img.W, (int)Math.Ceiling(r.Right)), y1 = Math.Min(img.H, (int)Math.Ceiling(r.Bottom));
            if (x1 - x0 < 1 || y1 - y0 < 1) continue;
            var counts = new Dictionary<int, int>();
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    var p = img.At(x, y);
                    int key = (p.R << 16) | (p.G << 8) | p.Bl;
                    counts[key] = counts.GetValueOrDefault(key) + 1;
                }
            int bgKey = counts.MaxBy(kv => kv.Value).Key;
            var bg = ((byte)(bgKey >> 16), (byte)(bgKey >> 8), (byte)bgKey);
            double peak = 1;
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    var p = img.At(x, y);
                    peak = Math.Max(peak, Ratio((p.R, p.G, p.Bl), bg));
                }
            double hybrid = double.NaN;
            string fgHex = "?";
            if (tb.Foreground is SolidColorBrush sb)
            {
                double a = sb.Color.A / 255.0 * sb.Opacity * EffectiveOpacity(tb, frame);
                byte Mix(byte f, byte b) => (byte)Math.Round(f * a + b * (1 - a));
                var fg = (Mix(sb.Color.R, bg.Item1), Mix(sb.Color.G, bg.Item2), Mix(sb.Color.B, bg.Item3));
                hybrid = Ratio(fg, bg);
                fgHex = Hx(sb.Color) + (a < 0.999 ? $" at {a:0.00}" : "");
            }
            list.Add(new LineMeasure(Redact(text.Length > 48 ? text[..48] + "…" : text), hybrid, peak, fgHex, Hx(bg)));
        }
        return list;
    }

    // ------------------------------------------------------------------ forcing states through the real triggers

    static readonly DependencyPropertyKey? MouseOverKey =
        typeof(UIElement).GetField("IsMouseOverPropertyKey", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as DependencyPropertyKey;
    static readonly DependencyPropertyKey? PressedKey =
        typeof(ButtonBase).GetField("IsPressedPropertyKey", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as DependencyPropertyKey;
    static readonly DependencyPropertyKey? KeyboardFocusedKey =
        typeof(UIElement).GetField("IsKeyboardFocusedPropertyKey", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as DependencyPropertyKey;

    static void Force(UIElement el, DependencyPropertyKey? key, bool on)
    {
        if (key is null) throw new InvalidOperationException("WPF property key not found by reflection");
        if (on) el.SetValue(key, true);
        else el.ClearValue(key);
    }

    static Color? BrushColor(Brush? b) => b is SolidColorBrush s ? s.Color : null;

    /// <summary>Rest, hover and pressed (with hover, as a real press is) on one button whose template has a "bg" Border. Reads the part's
    /// brushes and the composited pixels at the fill (inside, clear of the text) and at the left border.</summary>
    static async Task<JsonObject> ButtonStates(int check, string label, Button b, FrameworkElement frame, Color backdrop, Color accent, string pngStem)
    {
        var bg = (Border)b.Template.FindName("bg", b);
        var r = BoundsIn(b, frame);
        int fx = (int)(r.Left + 4), fy = (int)(r.Top + r.Height / 2);
        int bx = (int)Math.Floor(r.Left), by = fy;
        var crop = new Int32Rect((int)r.Left - 6, (int)r.Top - 6, (int)r.Width + 12, (int)r.Height + 12);
        var o = new JsonObject();
        async Task<(Color? Fill, Color? Border, (byte, byte, byte) FillPx, (byte, byte, byte) BorderPx)> Read(string state)
        {
            frame.UpdateLayout();
            await Settle();
            var img = Over(Grab(frame, frame.ActualWidth, frame.ActualHeight), backdrop);
            var fp = img.At(fx, fy);
            var bp = img.At(bx, by);
            PC(check).Pngs.Add(SavePx(img, $"{pngStem}-{state}", crop));
            return (BrushColor(bg.Background), BrushColor(bg.BorderBrush), (fp.R, fp.G, fp.Bl), (bp.R, bp.G, bp.Bl));
        }
        var rest = await Read("rest");
        Force(b, MouseOverKey, true);
        bool hoverSeen = b.IsMouseOver || (bool)b.GetValue(UIElement.IsMouseOverProperty);
        var hover = await Read("hover");
        Force(b, PressedKey, true);
        bool pressedSeen = b.IsPressed;
        var pressed = await Read("pressed");
        Force(b, PressedKey, false);
        Force(b, MouseOverKey, false);
        await Settle();

        bool hoverFill = rest.Fill != hover.Fill && Dist(rest.FillPx, Color.FromRgb(hover.FillPx.Item1, hover.FillPx.Item2, hover.FillPx.Item3)) > 0;
        bool pressAccentBrush = pressed.Border == accent;
        int pressBorderDist = Dist(pressed.BorderPx, accent);
        bool pressAccentPx = pressBorderDist <= 24;
        o["label"] = label;
        o["forced_hover_took"] = hoverSeen;
        o["forced_pressed_took"] = pressedSeen;
        o["rest_fill"] = rest.Fill is { } a ? Hx(a) : null;
        o["hover_fill"] = hover.Fill is { } h ? Hx(h) : null;
        o["pressed_fill"] = pressed.Fill is { } p ? Hx(p) : null;
        o["rest_fill_px"] = Hx(rest.FillPx);
        o["hover_fill_px"] = Hx(hover.FillPx);
        o["rest_border"] = rest.Border is { } rb ? Hx(rb) : null;
        o["pressed_border"] = pressed.Border is { } pb ? Hx(pb) : null;
        o["pressed_border_px"] = Hx(pressed.BorderPx);
        o["rest_border_px"] = Hx(rest.BorderPx);
        o["accent"] = Hx(accent);
        o["hover_changes_fill"] = hoverFill;
        o["pressed_border_is_accent_brush"] = pressAccentBrush;
        o["pressed_border_px_distance_to_accent"] = pressBorderDist;
        PAssert(check, hoverSeen && pressedSeen, $"{label}: forced hover and press took (IsMouseOver {hoverSeen}, IsPressed {pressedSeen})");
        PAssert(check, hoverFill, $"{label}: hover changes the fill ({o["rest_fill"]} -> {o["hover_fill"]}; pixel {o["rest_fill_px"]} -> {o["hover_fill_px"]})");
        PAssert(check, pressAccentBrush && pressAccentPx,
            $"{label}: pressed draws the Accent border (brush {o["pressed_border"]} vs Accent {Hx(accent)}; border pixel {o["pressed_border_px"]}, distance {pressBorderDist}; at rest {o["rest_border"]})");
        return o;
    }

    // ------------------------------------------------------------------ entry

    static async Task RenderPolishSmoke()
    {
        string? env = Environment.GetEnvironmentVariable("SANDUHR_DEV_PROFILE");
        if (string.IsNullOrWhiteSpace(env))
            throw new InvalidOperationException("polish-smoke refuses to run: SANDUHR_DEV_PROFILE is not set.");
        PolishRoot = Path.GetFullPath(env).TrimEnd('\\');
        string temp = Path.GetFullPath(Path.GetTempPath());
        if (!PolishRoot.StartsWith(temp, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("polish-smoke refuses to run: SANDUHR_DEV_PROFILE must be a scratch folder under %TEMP%.");
        var paths = new Paths();
        foreach (var p in new[] { paths.AppDataDir, paths.ExternalDirPath, paths.SettingsFile, paths.VaultDir, paths.ThemesDir })
            if (!Path.GetFullPath(p).StartsWith(PolishRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("polish-smoke refuses to run: a path resolves outside the scratch root.");
        PolishPng = Path.Combine(Out, "polish-smoke");
        Directory.CreateDirectory(PolishPng);
        SmokeFg0 = GetForegroundWindow();
        SmokeHookWindows();
        Log.Add($"polish-smoke: foreground handle at start {SmokeFg0}");

        void Add(int n, string title) => Polish[n] = new PolishCheck { N = n, Title = title };
        Add(1, "Reset dialog over white");
        Add(2, "ThemedDialog (the Quit confirm) over white and black");
        Add(3, "Expander toggle states");
        Add(4, "Single-card flash");
        Add(5, "Colors row (DeskInkPackPresetsTests)");
        Add(6, "Meters landing (ModsAndCardsPages landing + The_meters_switch_is_one_Tab_past_its_row)");

        // Scratch Claude Code folders, set before anything reads ClaudeCodeProfile.
        string home = Path.Combine(PolishRoot, "home");
        foreach (var name in new[] { ".claude", ".claude-qxwork" })
        {
            Directory.CreateDirectory(Path.Combine(home, name, "projects"));
            File.WriteAllText(Path.Combine(home, name, "settings.json"), "{}");
        }
        ClaudeCodeProfile.ProfileOverride = home;
        ClaudeCodeProfile.ConfigDirOverride = "";
        WidgetCatalogService.HandlerOverride = () => new OfflineHandler();

        async Task Guarded(int n, Func<Task> body)
        {
            try { await body(); }
            catch (Exception e)
            {
                PC(n).Evidence.Add("stopped: " + e.GetType().Name + ": " + Redact(e.Message));
                PC(n).Verdict = "FAIL";
                Log.Add(e.ToString());
            }
            FgNote($"after check {n}");
        }

        await Guarded(1, () => PolishReset(home));
        await Guarded(2, PolishThemedDialog);
        await Guarded(4, () => PolishSettings(home));   // checks 4 then 3, on one Settings window
        await Guarded(5, () => PolishTests(5, "colors", "FullyQualifiedName~Sanduhr.App.Tests.DeskInkPackPresetsTests"));
        await Guarded(6, () => PolishTests(6, "meters",
            "FullyQualifiedName~ModsAndCardsPagesTests.A_landing_on_meters_focuses_the_first_folders_row|FullyQualifiedName~ModsPageHeardTests.The_meters_switch_is_one_Tab_past_its_row"));

        var runWide = new JsonObject
        {
            ["foreground_at_start"] = SmokeFg0.ToInt64(),
            ["foreground_at_end"] = GetForegroundWindow().ToInt64(),
            ["foreground_drift_notes"] = new JsonArray(SmokeFgDrift.Select(d => (JsonNode?)JsonValue.Create(d)).ToArray()),
            ["activations_refused"] = SmokeActivationsDenied,
            ["scratch_root"] = "<scratch> under %TEMP%",
        };
        var json = new JsonObject
        {
            ["scenario"] = "polish-smoke",
            ["run_wide"] = runWide,
            ["checks"] = new JsonArray(Polish.Values.Select(c => (JsonNode?)new JsonObject
            {
                ["check"] = c.N,
                ["title"] = c.Title,
                ["verdict"] = c.Verdict,
                ["method"] = c.Method,
                ["numbers"] = c.Numbers,
                ["evidence"] = new JsonArray(c.Evidence.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
                ["pngs"] = new JsonArray(c.Pngs.Select(x => (JsonNode?)JsonValue.Create(Path.GetRelativePath(Out, x))).ToArray()),
            }).ToArray()),
        };
        File.WriteAllText(Path.Combine(Out, "results.json"), json.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }));
        foreach (var c in Polish.Values)
            Log.Add($"polish-smoke: check {c.N} {c.Verdict}: {c.Title}");
        Log.Add("polish-smoke: results written");
    }

    static string Verdictify(PolishCheck c, bool allOk) => c.Verdict = c.Verdict == "FAIL" ? "FAIL" : allOk ? "PASS" : "FAIL";

    // ------------------------------------------------------------------ check 1

    static async Task PolishReset(string home)
    {
        var c = PC(1);
        c.Method = "pixel: the frame rendered with RenderTargetBitmap and composited over pure white (and black) in code; per line the " +
                   "background is the commonest pixel in its box, the text color is its brush composited over that pixel (hybrid), cross-checked " +
                   "by the strongest pixel in the box (peak). Opacity: the frame composited over white and over black, compared pixel by pixel. " +
                   "States: IsMouseOver and IsPressed forced through WPF's read-only property keys, so the real FlatButton triggers ran.";
        bool ok = true;
        var white = Colors.White;
        var perTheme = new JsonObject();

        // Control: an opaque white Border with black text, nothing from the app. If RenderTargetBitmap leaves glyph pixels below alpha 255
        // here too, partial alpha on text is the renderer's, not the dialog's.
        {
            var probe = new Border { Background = Brushes.White, Width = 200, Height = 40,
                Child = new TextBlock { Text = "Control line", FontSize = 12, Foreground = Brushes.Black, Margin = new Thickness(8) } };
            TextOptions.SetTextFormattingMode(probe, TextFormattingMode.Display);   // as the dialogs set it
            probe.Measure(new Size(200, 40));
            probe.Arrange(new Rect(0, 0, 200, 40));
            probe.UpdateLayout();
            var pr = Grab(probe, 200, 40);
            int partial = 0, minA = 255;
            for (int i = 3; i < pr.B.Length; i += 4)
                if (pr.B[i] < 255) { partial++; minA = Math.Min(minA, pr.B[i]); }
            c.Numbers["control_opaque_border_with_text_pixels_below_alpha_255"] = partial;
            c.Numbers["control_lowest_alpha"] = minA;
            c.Evidence.Add($"control: an opaque white Border with black 12 px text renders {partial} pixels below alpha 255 (lowest {minA})");
        }
        foreach (var key in ThemeCatalog.BuiltInOrder)
        {
            var palette = new ThemePalette(key, ThemeCatalog.BuiltIns[key]);
            var t = new JsonObject();
            double minHybrid = double.MaxValue, minPeak = double.MaxValue;
            string worst = "";
            int lines = 0;
            foreach (var state in new[] { "choose", "choose-lines", "results" })
            {
                var rows = new List<ResetFolderRow>
                {
                    new(new ResetFolder(Path.Combine(home, ".claude"), [IntegrationKind.Statusline, IntegrationKind.Meters]), true),
                    new(new ResetFolder(Path.Combine(home, ".claude-qxwork"), [IntegrationKind.Mcp, IntegrationKind.Meters]), true, added: true),
                };
                var folders = ResetFolderListViewModel.For(rows, unreadable: state == "choose-lines");
                var rw = new ResetWindow(null, folders);
                palette.Apply(rw.Resources);
                OffScreen(rw);
                rw.Show();
                rw.Left = -20000; rw.Top = -20000;
                NeverActivateWindow(rw);
                try
                {
                    if (state == "choose-lines") rows[1].Undo = false;   // a kept folder: the PaceMarker and TextSecondary lines show
                    if (state == "results")
                        rw.ShowResults(
                        [
                            ResetStepResult.Done(ResetPart.ClaudeCodeEntries),
                            ResetStepResult.Failed(ResetPart.Accounts, ["a sign-in profile folder"]),
                            ResetStepResult.Done(ResetPart.History),
                        ]);
                    await Settle();
                    var frame = (Border)rw.FindName("Frame");
                    var raw = Grab(frame, frame.ActualWidth, frame.ActualHeight);
                    var onWhite = Over(raw, white);
                    var onBlack = Over(raw, Colors.Black);
                    c.Pngs.Add(SavePx(onWhite, $"1-reset/{key}-{state}-over-white"));
                    if (state == "choose") c.Pngs.Add(SavePx(onBlack, $"1-reset/{key}-{state}-over-black"));

                    // Opaque: inside the rounded corners and the 1 px border, every pixel identical over white and over black. Glyph pixels are
                    // counted apart: RenderTargetBitmap writes anti-aliased text with partial alpha even on an opaque fill (see the control below).
                    var textMask = new bool[raw.W * raw.H];
                    foreach (var tb in Descendants<TextBlock>(frame).Where(x => x.IsVisible && x.ActualWidth > 0))
                    {
                        var tr = BoundsIn(tb, frame);
                        for (int y = Math.Max(0, (int)tr.Top - 1); y < Math.Min(raw.H, (int)Math.Ceiling(tr.Bottom) + 1); y++)
                            for (int x = Math.Max(0, (int)tr.Left - 1); x < Math.Min(raw.W, (int)Math.Ceiling(tr.Right) + 1); x++)
                                textMask[y * raw.W + x] = true;
                    }
                    int inset = 11, interior = 0, fillDiff = 0, glyphDiff = 0, all = 0, allDiff = 0, minGlyphAlpha = 255;
                    for (int y = 0; y < raw.H; y++)
                        for (int x = 0; x < raw.W; x++)
                        {
                            int i = (y * raw.W + x) * 4;
                            bool same = onWhite.B[i] == onBlack.B[i] && onWhite.B[i + 1] == onBlack.B[i + 1] && onWhite.B[i + 2] == onBlack.B[i + 2];
                            all++; if (!same) allDiff++;
                            bool inside = x >= inset && y >= inset && x < raw.W - inset && y < raw.H - inset;
                            if (!inside) continue;
                            interior++;
                            if (same) continue;
                            if (textMask[y * raw.W + x]) { glyphDiff++; minGlyphAlpha = Math.Min(minGlyphAlpha, raw.B[i + 3]); }
                            else fillDiff++;
                        }
                    if (state == "choose")
                    {
                        t["frame_brush"] = BrushColor(frame.Background) is { } fb ? Hx(fb) : "?";
                        t["interior_pixels"] = interior;
                        t["interior_fill_pixels_differing_white_vs_black"] = fillDiff;
                        t["interior_glyph_pixels_differing_white_vs_black (RenderTargetBitmap text alpha)"] = glyphDiff;
                        t["lowest_glyph_pixel_alpha"] = minGlyphAlpha;
                        t["edge_pixels_differing_white_vs_black (corners outside the radius + translucent Border stroke)"] = allDiff - fillDiff - glyphDiff;
                        ok &= PAssert(1, fillDiff == 0, $"{key}: frame fill opaque ({fillDiff} of {interior} interior pixels outside text differ over white vs black; " +
                                                         $"{glyphDiff} glyph-edge pixels differ, lowest alpha {minGlyphAlpha}, a RenderTargetBitmap text artifact per the control)");

                        // States on the secondary button (Cancel).
                        var cancel = (Button)rw.FindName("CancelButton");
                        var st = await ButtonStates(1, $"{key} Reset Cancel (FlatButton)", cancel, frame, white, palette.Accent, $"1-reset/{key}-cancel");
                        t["cancel_states"] = st;
                    }

                    var m = MeasureLines(frame, onWhite);
                    lines += m.Count;
                    foreach (var l in m)
                    {
                        if (l.Hybrid < minHybrid) { minHybrid = l.Hybrid; worst = $"{state}: \"{l.Text}\" {l.Fg} on {l.Bg} = {l.Hybrid:0.00}"; }
                        minPeak = Math.Min(minPeak, l.Peak);
                        if (l.Hybrid < 4.5)
                            c.Evidence.Add($"below 4.5: {key} {state} \"{l.Text}\" {l.Fg} on {l.Bg}: hybrid {l.Hybrid:0.00}, peak {l.Peak:0.00}");
                    }
                    if (m.Count > 0)
                        t[$"min_contrast_hybrid_{state}"] = Math.Round(m.Min(l => l.Hybrid), 2);
                }
                finally { rw.Close(); }
                await Settle();
            }
            t["lines_measured"] = lines;
            t["min_contrast_hybrid"] = Math.Round(minHybrid, 2);
            t["min_contrast_pixel_peak"] = Math.Round(minPeak, 2);
            t["worst_line"] = worst;
            ok &= PAssert(1, minHybrid >= 4.5, $"{key}: every line >= 4.5:1 over white (min {minHybrid:0.00} hybrid, {minPeak:0.00} pixel peak; {lines} lines)");
            perTheme[key] = t;
        }
        c.Numbers["per_theme"] = perTheme;
        // The parts at a glance, read back from the per-theme numbers.
        bool All(Func<JsonObject, bool> f) => perTheme.All(kv => f((JsonObject)kv.Value!));
        double MinOf(string k) => perTheme.Min(kv => (double?)((JsonObject)kv.Value!)[k] ?? double.MaxValue);
        c.Numbers["parts"] = new JsonObject
        {
            ["choose_view_min_contrast_all_themes"] = Math.Min(MinOf("min_contrast_hybrid_choose"), MinOf("min_contrast_hybrid_choose-lines")),
            ["results_view_min_contrast_all_themes"] = MinOf("min_contrast_hybrid_results"),
            ["frame_fill_opaque_all_themes"] = All(t => (int?)t["interior_fill_pixels_differing_white_vs_black"] == 0),
            ["hover_changes_fill_all_themes"] = All(t => (bool?)t["cancel_states"]?["hover_changes_fill"] == true),
            ["pressed_adds_accent_border_all_themes"] = All(t => (bool?)t["cancel_states"]?["pressed_border_is_accent_brush"] == true),
        };
        Verdictify(c, ok);
    }

    // ------------------------------------------------------------------ check 2

    static ThemedDialog BuildQuitConfirm()
    {
        // The Quit confirm as SettingsViewModel.ConfirmQuit builds it, minus ShowDialog and the chime (playSound false).
        var ctor = typeof(ThemedDialog).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance,
            [typeof(string), typeof(string), typeof(MessageBoxButton), typeof(ThemedDialogKind), typeof(bool)])
            ?? throw new InvalidOperationException("ThemedDialog's constructor was not found");
        var d = (ThemedDialog)ctor.Invoke([SettingsNames.QuitSanduhr, SettingsViewModel.QuitQuestion, MessageBoxButton.YesNo, ThemedDialogKind.Warning, false]);
        d.PrimaryButton.Content = SettingsNames.QuitConfirm;
        d.SecondaryButton.Content = SettingsNames.QuitCancel;
        return d;
    }

    static async Task PolishThemedDialog()
    {
        var c = PC(2);
        c.Method = "pixel: as check 1 (RenderTargetBitmap, composited over white and over black in code, hybrid and peak per line); the " +
                   "dialog is the Quit confirm as SettingsViewModel builds it (title, question, Quit and Cancel), shown off screen without the chime. " +
                   "States on the secondary button through the real DialogFlatButton triggers (IsMouseOver/IsPressed forced by property key).";
        bool ok = true;
        var perTheme = new JsonObject();
        foreach (var key in ThemeCatalog.BuiltInOrder)
        {
            var palette = new ThemePalette(key, ThemeCatalog.BuiltIns[key]);
            var d = BuildQuitConfirm();
            palette.Apply(d.Resources);
            OffScreen(d);
            d.Show();
            d.Left = -20000; d.Top = -20000;
            NeverActivateWindow(d);
            var t = new JsonObject();
            try
            {
                await Settle();
                var frame = (FrameworkElement)d.Content;
                t["frame_brush"] = BrushColor(((Border)frame).Background) is { } fb ? Hx(fb) : "?";
                var raw = Grab(frame, frame.ActualWidth, frame.ActualHeight);
                foreach (var (name, back) in new[] { ("white", Colors.White), ("black", Colors.Black) })
                {
                    var img = Over(raw, back);
                    c.Pngs.Add(SavePx(img, $"2-quit/{key}-over-{name}"));
                    var m = MeasureLines(frame, img);
                    var worst = m.MinBy(l => l.Hybrid)!;
                    double minPeak = m.Min(l => l.Peak);
                    t[$"over_{name}_min_contrast_hybrid"] = Math.Round(worst.Hybrid, 2);
                    t[$"over_{name}_min_contrast_pixel_peak"] = Math.Round(minPeak, 2);
                    t[$"over_{name}_worst_line"] = $"\"{worst.Text}\" {worst.Fg} on {worst.Bg}";
                    t[$"over_{name}_lines"] = m.Count;
                    foreach (var l in m.Where(l => l.Hybrid < 4.5))
                        c.Evidence.Add($"below 4.5: {key} over {name} \"{l.Text}\" {l.Fg} on {l.Bg}: hybrid {l.Hybrid:0.00}, peak {l.Peak:0.00}");
                    ok &= PAssert(2, worst.Hybrid >= 4.5, $"{key} over {name}: min {worst.Hybrid:0.00} hybrid, {minPeak:0.00} peak ({m.Count} lines)");
                }
                int interior = 0, diff = 0;
                var w = Over(raw, Colors.White); var b = Over(raw, Colors.Black);
                for (int y = 11; y < raw.H - 11; y++)
                    for (int x = 11; x < raw.W - 11; x++)
                    {
                        int i = (y * raw.W + x) * 4;
                        interior++;
                        if (w.B[i] != b.B[i] || w.B[i + 1] != b.B[i + 1] || w.B[i + 2] != b.B[i + 2]) diff++;
                    }
                t["interior_pixels_differing_white_vs_black"] = diff;
                t["interior_pixels"] = interior;
                t["secondary_states"] = await ButtonStates(2, $"{key} Quit confirm Cancel (DialogFlatButton)", d.SecondaryButton, frame, Colors.White, palette.Accent, $"2-quit/{key}-secondary");
            }
            finally { d.Close(); }
            await Settle();
            perTheme[key] = t;
        }
        c.Numbers["per_theme"] = perTheme;
        Verdictify(c, ok);
    }

    // ------------------------------------------------------------------ checks 4 and 3

    static async Task PolishSettings(string home)
    {
        var c4 = PC(4);
        c4.Method = "adorner inspection (the AdornerLayers over the cards and the list: every AnchorHighlightAdorner and the element it adorns) " +
                    "plus pixels (the window's root visual rendered mid-flash; the fraction of points on each candidate outline path within 24 " +
                    "RGB of Accent). Landing through the search path: Search.Query set to the section's title, the FolderConfig row opened with " +
                    "SettingsWindow.OpenSearchRow (reflection: it is private). Card Y sampled on every LayoutUpdated and ScrollChanged.";
        var creds = new SmokeCreds();
        var accounts = new AccountStore(creds);
        accounts.AddAccount("Qxalpha", "placeholder-qxalpha");
        accounts.SetActive("Qxalpha");
        var widget = new WidgetViewModel(new Paths(), accounts);
        widget.ApplyThemeByKey("obsidian");
        widget.ApplyThemeResources();
        widget.SetAccountFolder("Qxalpha", Path.Combine(home, ".claude-qxwork"));
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask,
            checkForUpdatesNow: () => Task.FromResult(new Sanduhr.App.Updates.UpdateProbeResult(Sanduhr.App.Updates.UpdateProbeStatus.UpToDate)), isStoreBuild: false);
        var sw = new SettingsWindow(svm);
        OffScreen(sw);
        sw.Show();
        sw.Left = -20000; sw.Top = -20000;
        NeverActivateWindow(sw);
        try
        {
            await Settle2();
            var hwnd = new WindowInteropHelper(sw).Handle;
            SendMessage(hwnd, 0x0007 /* WM_SETFOCUS */, IntPtr.Zero, IntPtr.Zero);
            try { await PolishFlash(sw, svm); }
            catch (Exception e) { c4.Evidence.Add("stopped: " + e.GetType().Name + ": " + Redact(e.Message)); c4.Verdict = "FAIL"; Log.Add(e.ToString()); }
            FgNote("after check 4");
            try { await PolishExpander(sw, hwnd); }
            catch (Exception e) { PC(3).Evidence.Add("stopped: " + e.GetType().Name + ": " + Redact(e.Message)); PC(3).Verdict = "FAIL"; Log.Add(e.ToString()); }
            FgNote("after check 3");
        }
        finally
        {
            sw.Close();
            try { widget.Dispose(); } catch { }
            await Settle();
        }
    }

    static FrameworkElement RootOf(Window w) => (FrameworkElement)VisualTreeHelper.GetChild(w, 0);

    static async Task PolishFlash(SettingsWindow sw, SettingsViewModel svm)
    {
        var c = PC(4);
        var root = RootOf(sw);
        var page = Descendants<ClaudeCodePage>(sw).FirstOrDefault();
        // The page is realized when it is first selected; the search path selects it, so look it up lazily.
        ItemsControl? Cards() => (page ??= Descendants<ClaudeCodePage>(sw).FirstOrDefault())?.FindName("Cards") as ItemsControl;

        var clock = Stopwatch.StartNew();
        var samples = new List<(double T, double[] Ys, int Adorners)>();
        var scrolls = new List<(double T, double Offset, double Change)>();
        var hookedScrollers = new HashSet<ScrollViewer>();
        List<FrameworkElement> CardEls()
        {
            var list = Cards();
            if (list is null) return [];
            return [.. Enumerable.Range(0, list.Items.Count).Select(i => list.ItemContainerGenerator.ContainerFromIndex(i)).OfType<FrameworkElement>()];
        }
        List<Adorner> Flashes()
        {
            var layers = new HashSet<AdornerLayer>();
            var list = Cards();
            if (list is not null && AdornerLayer.GetAdornerLayer(list) is { } l0) layers.Add(l0);
            foreach (var card in CardEls())
                if (AdornerLayer.GetAdornerLayer(card) is { } l) layers.Add(l);
            if (AdornerLayer.GetAdornerLayer((Visual)sw.Content) is { } top) layers.Add(top);
            var found = new List<Adorner>();
            foreach (var layer in layers)
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(layer); i++)
                    if (VisualTreeHelper.GetChild(layer, i) is Adorner a && a.GetType().Name == "AnchorHighlightAdorner") found.Add(a);
            return found;
        }
        void Sample()
        {
            var cards = CardEls();
            var ys = cards.Where(x => x.IsVisible).Select(x => Math.Round(x.TransformToAncestor(root).Transform(new Point(0, 0)).Y, 1)).ToArray();
            int n = Flashes().Count;
            if (samples.Count == 0 || !samples[^1].Ys.SequenceEqual(ys) || samples[^1].Adorners != n)
                samples.Add((clock.Elapsed.TotalMilliseconds, ys, n));
            if (Cards() is { } list)
                for (DependencyObject? d = VisualTreeHelper.GetParent(list); d is not null; d = VisualTreeHelper.GetParent(d))
                    if (d is ScrollViewer sv && hookedScrollers.Add(sv))
                        sv.ScrollChanged += (_, e) => scrolls.Add((clock.Elapsed.TotalMilliseconds, Math.Round(e.VerticalOffset, 1), Math.Round(e.VerticalChange, 1)));
        }
        EventHandler onLayout = (_, _) => Sample();
        sw.LayoutUpdated += onLayout;

        // The search path: type the section's title, open the folder-config row.
        svm.Search.Query = SettingsNames.FolderHolds;
        var row = svm.Search.Results.FirstOrDefault(r => r.Page == SettingsPage.ClaudeCode && r.Anchor == SettingsIndex.FolderConfig)
                  ?? throw new InvalidOperationException("the search for \"" + SettingsNames.FolderHolds + "\" has no Claude Code folder-config row");
        c.Evidence.Add($"search row: \"{row.DisplayTitle}\" -> {row.Page}/{row.Anchor}");
        var open = typeof(SettingsWindow).GetMethod("OpenSearchRow", BindingFlags.NonPublic | BindingFlags.Instance)
                   ?? throw new InvalidOperationException("SettingsWindow.OpenSearchRow not found");
        double t0 = clock.Elapsed.TotalMilliseconds;
        open.Invoke(sw, [row]);
        Sample();

        double flashStart = -1;
        while (clock.Elapsed.TotalMilliseconds - t0 < 1200)
        {
            await Task.Delay(5);
            Sample();
            if (Flashes().Count > 0) { flashStart = clock.Elapsed.TotalMilliseconds; break; }
        }
        if (flashStart < 0)
        {
            sw.LayoutUpdated -= onLayout;
            c.Evidence.Add("no AnchorHighlightAdorner appeared within 1.2 s of opening the search row");
            c.Verdict = "FAIL";
            return;
        }
        await Task.Delay(300);
        Sample();
        root.UpdateLayout();

        var cards = CardEls();
        var list = Cards()!;
        var flashes = Flashes();
        var mid = Grab(root, root.ActualWidth, root.ActualHeight);
        var midOpaque = Over(mid, Colors.Black);
        c.Pngs.Add(SavePx(midOpaque, "4-flash/mid-flash"));
        double midT = clock.Elapsed.TotalMilliseconds - flashStart;

        Color accent = BrushColor(sw.TryFindResource("Sanduhr.Brush.Accent") as Brush) ?? Colors.Magenta;
        var adorned = flashes.Select(a => a.AdornedElement).ToList();
        int flashedIndex = cards.FindIndex(x => adorned.Any(a => ReferenceEquals(a, x)));
        bool adornsList = adorned.Any(a => ReferenceEquals(a, list));
        c.Numbers["cards"] = cards.Count;
        c.Numbers["anchor_highlight_adorners"] = flashes.Count;
        c.Numbers["adorned_elements"] = new JsonArray(adorned.Select(a => (JsonNode?)JsonValue.Create(
            a is FrameworkElement fe ? $"{fe.GetType().Name} {fe.ActualWidth:0}x{fe.ActualHeight:0}{(ReferenceEquals(fe, list) ? " (the whole Cards list)" : cards.Contains(fe) ? $" (card {cards.IndexOf(fe)})" : "")}" : a.GetType().Name)).ToArray());
        c.Numbers["flashed_card_index"] = flashedIndex;
        c.Numbers["captured_ms_after_flash_start"] = Math.Round(midT);
        c.Numbers["accent"] = Hx(accent);

        // Pixels along each candidate outline (the adorner draws Rect(-4, -3, w + 8, h + 6) with a 2 px pen), only where the scroller shows.
        Rect view = new(0, 0, root.ActualWidth, root.ActualHeight);
        for (DependencyObject? d = VisualTreeHelper.GetParent(list); d is not null; d = VisualTreeHelper.GetParent(d))
            if (d is ScrollViewer sv && sv.ViewportHeight > 0)
            {
                view = BoundsIn(sv, root);
                break;
            }
        (int Points, int Accent) OutlineProbe(Rect r, Func<double, bool>? keepY = null)
        {
            int pts = 0, hit = 0;
            void Probe(double x, double y)
            {
                if (keepY is not null && !keepY(y)) return;
                if (x < view.Left || x > view.Right - 1 || y < view.Top || y > view.Bottom - 1) return;
                pts++;
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int px = (int)Math.Round(x) + dx, py = (int)Math.Round(y) + dy;
                        if (px < 0 || py < 0 || px >= mid.W || py >= mid.H) continue;
                        var p = midOpaque.At(px, py);
                        if (Dist((p.R, p.G, p.Bl), accent) <= 24) { hit++; return; }
                    }
            }
            var o = new Rect(r.X - 4, r.Y - 3, r.Width + 8, r.Height + 6);
            for (double y = o.Top + 6; y <= o.Bottom - 6; y += 2) { Probe(o.Left, y); Probe(o.Right, y); }
            for (double x = o.Left + 6; x <= o.Right - 6; x += 2) { Probe(x, o.Top); Probe(x, o.Bottom); }
            return (pts, hit);
        }
        var perCard = new JsonArray();
        bool othersClean = true;
        double flashedFrac = 0;
        Rect flashedRect = Rect.Empty;
        for (int i = 0; i < cards.Count; i++)
        {
            var r = BoundsIn(cards[i], root);
            var (pts, hit) = OutlineProbe(r);
            double frac = pts == 0 ? double.NaN : hit / (double)pts;
            perCard.Add(new JsonObject { ["card"] = i, ["bounds"] = $"{r.X:0},{r.Y:0} {r.Width:0}x{r.Height:0}", ["outline_points_in_view"] = pts, ["accent_points"] = hit, ["fraction"] = double.IsNaN(frac) ? null : JsonValue.Create(Math.Round(frac, 3)) });
            if (i == flashedIndex) { flashedFrac = frac; flashedRect = r; }
            else if (pts > 0 && frac > 0.1) othersClean = false;
        }
        var lr = BoundsIn(list, root);
        var (lpts, lhit) = OutlineProbe(lr, y => flashedRect.IsEmpty || y < flashedRect.Top - 8 || y > flashedRect.Bottom + 8);
        double listFrac = lpts == 0 ? double.NaN : lhit / (double)lpts;
        c.Numbers["per_card_outline"] = perCard;
        c.Numbers["whole_list_outline_outside_the_flashed_card"] = new JsonObject
        {
            ["bounds"] = $"{lr.X:0},{lr.Y:0} {lr.Width:0}x{lr.Height:0}", ["points_in_view"] = lpts, ["accent_points"] = lhit,
            ["fraction"] = double.IsNaN(listFrac) ? null : JsonValue.Create(Math.Round(listFrac, 3)),
        };

        bool ok = true;
        ok &= PAssert(4, cards.Count >= 2, $"{cards.Count} folder cards present");
        ok &= PAssert(4, flashes.Count == 1, $"{flashes.Count} AnchorHighlightAdorner(s) on the page");
        ok &= PAssert(4, flashedIndex >= 0 && !adornsList, $"the adorner outlines card {flashedIndex}, not the whole list (adorns list: {adornsList})");
        if (flashedIndex >= 0 && !double.IsNaN(flashedFrac))
            ok &= PAssert(4, flashedFrac >= 0.6, $"pixels: {flashedFrac:P0} of the flashed card's visible outline points are Accent");
        ok &= PAssert(4, othersClean, "pixels: no other card's outline is drawn (each at or under 10 % Accent)");
        if (!double.IsNaN(listFrac))
            ok &= PAssert(4, listFrac <= 0.1, $"pixels: the whole list's outline, away from the flashed card, is {listFrac:P0} Accent ({lpts} points)");
        else
            c.Evidence.Add("pixels: the whole list's outline has no visible point outside the flashed card's span (list fills the viewport); adorner inspection carries that part");

        // After the flash: the outline is gone.
        double wait = AnchorHighlightAdorner.Duration.TotalMilliseconds + 250 - (clock.Elapsed.TotalMilliseconds - flashStart);
        if (wait > 0) await Task.Delay((int)wait);
        Sample();
        root.UpdateLayout();
        var after = Over(Grab(root, root.ActualWidth, root.ActualHeight), Colors.Black);
        c.Pngs.Add(SavePx(after, "4-flash/after-flash"));
        c.Numbers["anchor_highlight_adorners_after_1_75_s"] = Flashes().Count;
        sw.LayoutUpdated -= onLayout;

        // Y of the flashed card: before the landing scrolled, at flash start, and every change during the flash.
        if (flashedIndex >= 0)
        {
            var series = samples.Where(s => s.Ys.Length > flashedIndex).Select(s => (s.T, Y: s.Ys[flashedIndex], s.Adorners)).ToList();
            var during = series.Where(s => s.T >= flashStart && s.T <= flashStart + AnchorHighlightAdorner.Duration.TotalMilliseconds).ToList();
            var atStart = series.LastOrDefault(s => s.T <= flashStart + 0.5);
            var first = series.FirstOrDefault();
            c.Numbers["card_y_first_seen"] = series.Count > 0 ? JsonValue.Create(first.Y) : null;
            c.Numbers["card_y_at_flash_start"] = series.Count > 0 ? JsonValue.Create(atStart.Y) : null;
            double minY = during.Count > 0 ? Math.Min(during.Min(s => s.Y), atStart.Y) : atStart.Y;
            double maxY = during.Count > 0 ? Math.Max(during.Max(s => s.Y), atStart.Y) : atStart.Y;
            c.Numbers["card_y_shift_during_flash"] = Math.Round(maxY - minY, 1);
            c.Numbers["card_y_series_ms_after_open"] = new JsonArray(series.Select(s => (JsonNode?)JsonValue.Create(
                $"{s.T - t0:0} ms: y {s.Y}{(s.Adorners > 0 ? " (flashing)" : "")}")).ToArray());
            c.Numbers["scroll_changes_ms_after_open"] = new JsonArray(scrolls.Select(s => (JsonNode?)JsonValue.Create(
                $"{s.T - t0:0} ms: offset {s.Offset} (change {s.Change}){(s.T >= flashStart && s.T <= flashStart + 1500 ? " during flash" : "")}")).ToArray());
            c.Numbers["flash_start_ms_after_open"] = Math.Round(flashStart - t0);
            c.Evidence.Add($"card {flashedIndex} y: first seen {(series.Count > 0 ? first.Y : double.NaN)}, at flash start {atStart.Y}, range during the flash {minY}..{maxY} (shift {maxY - minY:0.0} DIP)");
        }
        c.Numbers["focused_after_landing"] = Keyboard.FocusedElement is FrameworkElement f ? $"{f.GetType().Name} \"{Redact(System.Windows.Automation.AutomationProperties.GetName(f))}\"" : "(none)";
        Verdictify(c, ok);
    }

    static async Task PolishExpander(SettingsWindow sw, IntPtr hwnd)
    {
        var c = PC(3);
        var root = RootOf(sw);
        var page = Descendants<ClaudeCodePage>(sw).First();
        var expander = Descendants<Expander>(page).First(e => e.IsVisible);
        expander.ApplyTemplate();
        var toggle = (ToggleButton)expander.Template.FindName("HeaderSite", expander);
        toggle.ApplyTemplate();
        var ring = (Border)toggle.Template.FindName("ring", toggle);
        c.Numbers["toggle"] = $"Claude Code page, first card's \"{Redact(System.Windows.Automation.AutomationProperties.GetName(toggle))}\" (SectionExpander HeaderSite)";
        bool ok = true;
        string focusMethod = "";
        var perTheme = new JsonObject();
        foreach (var key in ThemeCatalog.BuiltInOrder)
        {
            var palette = new ThemePalette(key, ThemeCatalog.BuiltIns[key]);
            palette.Apply(sw.Resources);
            // Out of focus first, so rest is rest.
            Keyboard.ClearFocus();
            await Settle();
            toggle.BringIntoView();
            await Settle();
            var r = BoundsIn(toggle, root);
            var crop = new Int32Rect((int)r.Left - 8, (int)r.Top - 8, (int)r.Width + 16, (int)r.Height + 16);
            int fx = (int)(r.Left + r.Width - 6), fy = (int)(r.Top + r.Height / 2), bx = (int)Math.Floor(r.Left), by = fy;
            async Task<(Color? Fill, Color? Border, (byte, byte, byte) FillPx, (byte, byte, byte) BorderPx)> Read(string state)
            {
                root.UpdateLayout();
                await Settle();
                var img = Over(Grab(root, root.ActualWidth, root.ActualHeight), Colors.Black);
                c.Pngs.Add(SavePx(img, $"3-expander/{key}-{state}", crop));
                var f = img.At(fx, fy); var b = img.At(bx, by);
                return (BrushColor(ring.Background), BrushColor(ring.BorderBrush), (f.R, f.G, f.Bl), (b.R, b.G, b.Bl));
            }
            var t = new JsonObject();
            var rest = await Read("rest");
            Force(toggle, MouseOverKey, true);
            var hover = await Read("hover");
            Force(toggle, PressedKey, true);
            var pressed = await Read("pressed");
            Force(toggle, PressedKey, false);
            Force(toggle, MouseOverKey, false);

            // Keyboard focus: routed for real first (WM_SETFOCUS to the never-activated window, then Keyboard.Focus). Off screen and never
            // activated, Win32 refuses the focus WPF asks for (measured: lifting the activation guard for the call did not change that), so the
            // fallback forces IsKeyboardFocused by its property key: the real template trigger runs, but no keyboard put it there.
            SendMessage(hwnd, 0x0007, IntPtr.Zero, IntPtr.Zero);
            Keyboard.Focus(toggle);
            bool real = toggle.IsKeyboardFocused;
            if (!real)
                Force(toggle, KeyboardFocusedKey, true);   // only when routing failed; the method says so
            focusMethod = real ? "real keyboard focus (Keyboard.Focus after WM_SETFOCUS)" : "IsKeyboardFocused forced by property key (routing failed)";
            var focused = await Read("focused");
            if (!real) Force(toggle, KeyboardFocusedKey, false);
            Keyboard.ClearFocus();

            bool hoverFill = rest.Fill != hover.Fill && hover.FillPx != rest.FillPx;
            bool pressFill = rest.Fill != pressed.Fill && pressed.FillPx != rest.FillPx;
            bool ringAccent = focused.Border == palette.Accent;
            int ringDist = Dist(focused.BorderPx, palette.Accent);
            t["rest_fill"] = rest.Fill is { } a ? Hx(a) : null;
            t["hover_fill"] = hover.Fill is { } h ? Hx(h) : null;
            t["pressed_fill"] = pressed.Fill is { } p ? Hx(p) : null;
            t["rest_fill_px"] = Hx(rest.FillPx);
            t["hover_fill_px"] = Hx(hover.FillPx);
            t["pressed_fill_px"] = Hx(pressed.FillPx);
            t["focus_ring_brush"] = focused.Border is { } fb ? Hx(fb) : null;
            t["focus_ring_px"] = Hx(focused.BorderPx);
            t["accent"] = Hx(palette.Accent);
            t["focus_ring_px_distance_to_accent"] = ringDist;
            t["focus"] = real ? "real" : "forced";
            ok &= PAssert(3, hoverFill, $"{key}: hover changes the fill ({t["rest_fill"]} -> {t["hover_fill"]}; pixel {t["rest_fill_px"]} -> {t["hover_fill_px"]})");
            ok &= PAssert(3, pressFill, $"{key}: pressed changes the fill ({t["rest_fill"]} -> {t["pressed_fill"]}; pixel {t["pressed_fill_px"]})");
            ok &= PAssert(3, ringAccent && ringDist <= 24, $"{key}: focus ring is Accent (brush {t["focus_ring_brush"]} vs {Hx(palette.Accent)}; pixel {t["focus_ring_px"]}, distance {ringDist}; {t["focus"]} focus)");
            perTheme[key] = t;
        }
        c.Method = "pixel + brush: the Settings window's root visual rendered (RenderTargetBitmap, over black) and cropped to the toggle per state; " +
                   "the ring part's brushes read alongside. Hover and press forced through WPF's read-only property keys (real SectionExpander " +
                   "triggers); focus: " + focusMethod + ". Each built-in theme applied to the window's resources in turn.";
        c.Numbers["per_theme"] = perTheme;
        Verdictify(c, ok);
        if (c.Verdict == "PASS" && perTheme.Any(kv => (string?)((JsonObject)kv.Value!)["focus"] == "forced"))
        {
            c.Verdict = "PARTIAL";
            c.Evidence.Add("PARTIAL: hover, press and the focus ring's brush and pixels pass, but keyboard focus could not be routed to the off-screen, " +
                           "never-activated window; the focused state was forced by property key, so a real Tab onto the toggle is left to a person");
        }
    }

    // ------------------------------------------------------------------ checks 5 and 6

    static string FindWindowsDotnet()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (dir.Name == "windows-dotnet") return dir.FullName;
            string a = Path.Combine(dir.FullName, "windows-dotnet");
            if (Directory.Exists(Path.Combine(a, "tests"))) return a;
        }
        throw new DirectoryNotFoundException("windows-dotnet not found above " + AppContext.BaseDirectory);
    }

    static async Task PolishTests(int n, string stem, string filter)
    {
        var c = PC(n);
        string wd = FindWindowsDotnet();
        string proj = Path.Combine(wd, "tests", "Sanduhr.App.Tests", "Sanduhr.App.Tests.csproj");
        string resDir = Path.Combine(Out, "tests");
        Directory.CreateDirectory(resDir);
        string trx = $"{stem}.trx";
        foreach (var old in Directory.GetFiles(resDir, trx)) File.Delete(old);
        c.Method = $"dotnet test {Path.GetRelativePath(wd, proj)} -c Release --no-build --filter \"{filter}\", TRX read back (no new code)";
        var psi = new ProcessStartInfo("dotnet",
            ["test", proj, "-c", "Release", "--no-build", "--filter", filter, "--logger", $"trx;LogFileName={trx}", "--results-directory", resDir])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = wd,
        };
        using var proc = Process.Start(psi)!;
        var so = proc.StandardOutput.ReadToEndAsync();
        var se = proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        File.WriteAllText(Path.Combine(resDir, stem + ".log"), await so + Environment.NewLine + await se);
        c.Numbers["exit_code"] = proc.ExitCode;
        string trxPath = Path.Combine(resDir, trx);
        if (!File.Exists(trxPath))
        {
            c.Evidence.Add($"no TRX written (exit {proc.ExitCode}); see tests/{stem}.log");
            c.Verdict = "FAIL";
            return;
        }
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        var doc = XDocument.Load(trxPath);
        var results = doc.Descendants(ns + "UnitTestResult").ToList();
        int passed = results.Count(r => (string?)r.Attribute("outcome") == "Passed");
        int failed = results.Count(r => (string?)r.Attribute("outcome") == "Failed");
        int other = results.Count - passed - failed;
        c.Numbers["total"] = results.Count;
        c.Numbers["passed"] = passed;
        c.Numbers["failed"] = failed;
        c.Numbers["skipped_or_not_executed"] = other;
        c.Numbers["tests"] = new JsonArray(results.Select(r => (JsonNode?)JsonValue.Create($"{(string?)r.Attribute("outcome")}: {(string?)r.Attribute("testName")}")).ToArray());
        var spoken = results.Select(r => r.Descendants(ns + "StdOut").FirstOrDefault()?.Value).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        if (n == 6)
            c.Evidence.Add(spoken.Count > 0
                ? "test output: " + string.Join(" | ", spoken.Select(s => Redact(s!.Trim())))
                : "the tests print no names (they assert the focused row's name equals the row's RowName and the switch's UIA Name equals SwitchName); pass count recorded instead");
        foreach (var r in results.Where(r => (string?)r.Attribute("outcome") != "Passed"))
            c.Evidence.Add($"{(string?)r.Attribute("outcome")}: {(string?)r.Attribute("testName")}: {Redact(r.Descendants(ns + "Message").FirstOrDefault()?.Value ?? "")}");
        bool ok = results.Count > 0 && failed == 0;
        PAssert(n, ok, $"{passed} passed, {failed} failed, {other} skipped or not executed of {results.Count}");
        c.Verdict = !ok ? "FAIL" : other > 0 ? "PARTIAL" : "PASS";
    }
}
