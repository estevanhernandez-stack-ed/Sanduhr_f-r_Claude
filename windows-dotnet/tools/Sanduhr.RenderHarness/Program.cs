using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

// Off-screen render harness: opens Sanduhr's windows far off screen and writes PNGs, so a change
// can be checked without launching the app or clicking through it.
//
// Run (PowerShell):
//   $env:SANDUHR_DEV_PROFILE = "C:\path\to\scratch-profile"      # required, refuses to run without it
//   dotnet run --project windows-dotnet/tools/Sanduhr.RenderHarness -- --scenario all --out C:\path\to\out
//
// Options:
//   --out <dir>          where PNGs and log.txt go (default ./harness-out)
//   --scenario <name>    widget | whatsnew | tour | settings | glow | reset | desk | arrange | launcher | review | companion | ridealongs | propose-widget | all; repeatable (default all)
//   --theme <key>        built-in theme key to apply (default obsidian)
//   --light              write a LIGHT user theme into the dev profile's themes folder and apply
//                        it, to check dialogs for legibility on a light surface
//   --app-xaml <path>    App.xaml override (default: found by walking up from the binary)
//
// NEVER construct Sanduhr.App.App here. Its OnStartup starts the real widget against the real
// Windows Credential Manager accounts (sign-in, fetch, publish). The harness runs a plain
// System.Windows.Application with App.xaml's resources parsed in. It never calls Start(),
// never fetches, never adds, removes or renames accounts, never writes credentials. Settings,
// history and themes go to SANDUHR_DEV_PROFILE (Paths honors it). Demo mode starts right after
// the view model is built so no real account label reaches a PNG.
//
// Not everything is a sample: Claude Code homes are discovered from the real user profile, so the
// Claude Usage and Publish usage pages can show real folder names, paths (with the Windows user
// name) and project names. Check those PNGs before sharing them.
public static partial class Program
{
    const string LightThemeKey = "harness-light";

    // Light user theme. The linter's design rules assume a dark base, so it raises dark-base
    // warnings for the three surfaces; the schema and contrast rules pass.
    const string LightThemeJson = """
        {
          "name": "Harness Light",
          "bg": "#f4f4f2",
          "glass": "#ffffff",
          "glass_on_mica": "#fafaf8",
          "title_bg": "#ecece8",
          "border": "#c9c9c2",
          "footer_bg": "#ecece8",
          "bar_bg": "#dcdcd6",
          "text": "#1a1a1a",
          "text_secondary": "#3a3a3a",
          "text_dim": "#555555",
          "text_muted": "#6e6e6e",
          "accent": "#2e6fd1",
          "pace_marker": "#c2410c",
          "sparkline": "#2e6fd1",
          "glass_alpha": 1.0,
          "border_alpha": 0.5,
          "border_tint": null,
          "accent_bloom": {"blur": 3, "alpha": 0.25},
          "inner_highlight": null
        }
        """;

    static string Out = "";
    static readonly List<string> Log = new();

    [STAThread]
    public static int Main(string[] args)
    {
        string? profile = Environment.GetEnvironmentVariable("SANDUHR_DEV_PROFILE");
        if (string.IsNullOrWhiteSpace(profile))
        {
            Console.Error.WriteLine("Sanduhr.RenderHarness refuses to run: set SANDUHR_DEV_PROFILE to a scratch folder " +
                                    "so settings, history and themes stay out of the real profile.");
            return 2;
        }

        string? outArg = null, appXamlArg = null, theme = "obsidian";
        bool light = false;
        var scenarios = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--out": outArg = Next(args, ref i); break;
                case "--scenario": scenarios.Add(Next(args, ref i).ToLowerInvariant()); break;
                case "--theme": theme = Next(args, ref i); break;
                case "--app-xaml": appXamlArg = Next(args, ref i); break;
                case "--light": light = true; break;
                default:
                    Console.Error.WriteLine($"Unknown argument: {args[i]}");
                    return 2;
            }
        }
        string[] known = { "widget", "whatsnew", "tour", "settings", "glow", "reset", "desk", "arrange", "launcher", "review", "companion", "ridealongs", "propose-widget" };
        if (scenarios.Count == 0 || scenarios.Contains("all"))
            scenarios = known.ToList();
        var bad = scenarios.Where(s => !known.Contains(s)).ToList();
        if (bad.Count > 0)
        {
            Console.Error.WriteLine($"Unknown scenario: {string.Join(", ", bad)}. Known: {string.Join(", ", known)}, all.");
            return 2;
        }

        Out = Path.GetFullPath(outArg ?? "harness-out");
        Directory.CreateDirectory(Out);
        Console.WriteLine($"Dev profile: {Path.GetFullPath(profile)}");
        Console.WriteLine($"Writing to:  {Out}");

        int code = 0;
        try
        {
            string appXaml = appXamlArg ?? FindAppXaml();
            var appAssembly = typeof(Sanduhr.App.MainWindow).Assembly;
            // NOT Sanduhr.App.App (see the header). A plain Application, pack URIs pointed at the app assembly.
            typeof(Application).GetField("_resourceAssembly", BindingFlags.NonPublic | BindingFlags.Static)!
                .SetValue(null, appAssembly);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources = LoadResources(appXaml, appAssembly.GetName().Name!);

            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(async () =>
            {
                try { await Run(scenarios, theme!, light); }
                catch (Exception e)
                {
                    File.WriteAllText(Path.Combine(Out, "error.txt"), e.ToString());
                    Console.Error.WriteLine(e);
                    code = 1;
                }
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }));
            Dispatcher.Run();
        }
        catch (Exception e)
        {
            File.WriteAllText(Path.Combine(Out, "error.txt"), e.ToString());
            Console.Error.WriteLine(e);
            code = 1;
        }
        return code;
    }

    static string Next(string[] args, ref int i)
    {
        if (i + 1 >= args.Length)
            throw new ArgumentException($"{args[i]} needs a value.");
        return args[++i];
    }

    static string FindAppXaml()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string a = Path.Combine(dir.FullName, "windows-dotnet", "src", "Sanduhr.App", "App.xaml");
            if (File.Exists(a)) return a;
            if (dir.Name == "windows-dotnet")
            {
                string b = Path.Combine(dir.FullName, "src", "Sanduhr.App", "App.xaml");
                if (File.Exists(b)) return b;
            }
        }
        throw new FileNotFoundException("App.xaml not found above " + AppContext.BaseDirectory + "; pass --app-xaml <path>.");
    }

    static ResourceDictionary LoadResources(string appXaml, string assemblyName)
    {
        string xaml = File.ReadAllText(appXaml);
        const string open = "<Application.Resources>", close = "</Application.Resources>";
        int a = xaml.IndexOf(open, StringComparison.Ordinal);
        int b = xaml.IndexOf(close, StringComparison.Ordinal);
        if (a < 0 || b < a)
            throw new InvalidDataException("No <Application.Resources> block in " + appXaml);
        a += open.Length;
        string dict = "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                      "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                      "xmlns:local=\"clr-namespace:Sanduhr.App;assembly=" + assemblyName + "\" " +
                      "xmlns:vm=\"clr-namespace:Sanduhr.App.ViewModels;assembly=" + assemblyName + "\" " +
                      "xmlns:shell=\"clr-namespace:System.Windows.Shell;assembly=PresentationFramework\">" +
                      xaml[a..b] + "</ResourceDictionary>";
        return (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(dict);
    }

    static async Task Settle()
    {
        for (int i = 0; i < 5; i++)
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Task.Delay(150);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    static void Save(FrameworkElement el, string name)
    {
        el.UpdateLayout();
        int w = (int)Math.Ceiling(el.ActualWidth), h = (int)Math.Ceiling(el.ActualHeight);
        var rtb = new RenderTargetBitmap(Math.Max(1, w), Math.Max(1, h), 96, 96, PixelFormats.Pbgra32);
        rtb.Render(el);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        string path = Path.Combine(Out, name + ".png");
        using (var f = File.Create(path))
            enc.Save(f);
        Log.Add($"png {name}.png {w}x{h}");
    }

    static Window? Find(string typeName) =>
        Application.Current.Windows.OfType<Window>().LastOrDefault(w => w.GetType().Name == typeName);

    static void OffScreen(Window w)
    {
        w.WindowStartupLocation = WindowStartupLocation.Manual;
        w.ShowActivated = false;
        w.Left = -20000;
        w.Top = -20000;
    }

    static string WindowsAppMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v ? (v == 0 ? "dark" : "light") : "unknown";
        }
        catch { return "unknown"; }
    }

    static string Slug(string s)
    {
        string slug = Regex.Replace(s.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return slug.Length == 0 ? "tab" : slug;
    }

    static string HeaderText(object? header) => header switch
    {
        null => "",
        string s => s,
        TextBlock tb => tb.Text,
        _ => header.ToString() ?? "",
    };

    static async Task Run(List<string> scenarios, string theme, bool light)
    {
        string themeKey = theme;
        if (light)
        {
            // Lint first; a light theme that fails the schema is fixed, never bypassed.
            var lint = ThemeLint.Lint(LightThemeJson);
            if (!lint.Ok)
                throw new InvalidDataException("Light theme fails lint: " + string.Join("; ", lint.Errors.Select(f => $"{f.Field}: {f.Message}")));
            string dir = new Paths().ThemesDir;
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, LightThemeKey + ".json"), LightThemeJson);
            foreach (var w in lint.Warnings)
                Log.Add($"light theme lint warning {w.Field}: {w.Message}");
            themeKey = LightThemeKey;
        }

        Log.Add($"theme: {themeKey}");
        Log.Add($"light: {(light ? "yes" : "no")}");
        Log.Add($"windows app mode: {WindowsAppMode()}");
        Log.Add($"scenarios: {string.Join(",", scenarios)}");

        // Claude Code folders come from an invented profile, never the real one, so no real path, project or
        // account label reaches a picture. Set before the view model is built (its log reader discovers folders).
        string fakeProfile = BuildFakeProfile();
        Sanduhr.App.Services.ClaudeCodeProfile.ProfileOverride = fakeProfile;
        Sanduhr.App.Services.ClaudeCodeProfile.ConfigDirOverride = "";
        Log.Add("claude code profile: invented (temp folder)");

        // Reads the real Credential Manager accounts (constructor only). No Start().
        var vm = new WidgetViewModel();
        vm.ApplyThemeByKey(themeKey);
        vm.ApplyThemeResources();
        // Before anything renders: sample accounts only ("Work", "Personal").
        vm.StartDemo();
        await Settle();
        // One folder added by hand (its settings.json is deliberately unreadable), and a link from a sample account.
        vm.SaveCcFoldersAdded([Path.Combine(fakeProfile, "elsewhere", "claude-home-extra")]);
        vm.SetAccountFolder("Work", Path.Combine(fakeProfile, ".claude-work"));

        var asm = typeof(Sanduhr.App.App).Assembly;
        Sanduhr.App.MainWindow? main = null;
        try
        {
            if (scenarios.Contains("widget"))
            {
                main = new Sanduhr.App.MainWindow { DataContext = vm };
                OffScreen(main);
                main.Show();
                main.Left = -20000;
                await Settle();
                Save((FrameworkElement)main.Content, "widget");
                // The signed-out first-run card with Sign in, Not now and Just the Desk (S7 7.5). The sample
                // account stays in the view model; only the reason flips, then goes back.
                vm.Reason = SignInReason.FirstRun;
                await Settle();
                Save((FrameworkElement)main.Content, "widget-firstrun");
                vm.Reason = SignInReason.None;
                await Settle();
            }

            if (scenarios.Contains("whatsnew"))
            {
                var wnType = asm.GetType("Sanduhr.App.Views.WhatsNewWindow")!;
                var store = new Sanduhr.App.Services.SettingsStore(new Paths());
                wnType.GetMethod("Show", [typeof(IReadOnlyList<WhatsNewCard>), typeof(string), typeof(Sanduhr.App.Services.SettingsStore), typeof(Action<SettingsPage>)])!
                    .Invoke(null, [WhatsNewCards.Table, "Highlights in 3.5.0", store, new Action<SettingsPage>(_ => { })]);
                var ww = Find("WhatsNewWindow")!;
                OffScreen(ww);
                await Settle();
                Save((FrameworkElement)ww.Content, "whatsnew");
                ww.Close();
                await Settle();
            }

            if (scenarios.Contains("tour"))
            {
                var wnType = asm.GetType("Sanduhr.App.Views.WhatsNewWindow")!;
                var tour = new WelcomeTourViewModel(WelcomeTour.Steps(new HashSet<TourFeature>(), signedIn: true), vm);
                wnType.GetMethod("ShowTour")!.Invoke(null, [tour, new Action<SettingsPage>(_ => { }), new Action<bool>(_ => { })]);
                var tw = Find("WhatsNewWindow")!;
                OffScreen(tw);
                for (int i = 0; i < tour.Count; i++)
                {
                    await Settle();
                    Save((FrameworkElement)tw.Content, $"tour-step{i + 1}");
                    tour.Next();
                }
                tw.Close();
                await Settle();
            }

            if (scenarios.Contains("glow"))
                await RenderGlow(light);
            if (scenarios.Contains("reset"))
                await RenderReset(asm);
            if (scenarios.Contains("desk"))
            {
                await RenderDesk(light);
                await RenderDeskContent();
                await RenderGlowSpot(light);
            }

            if (scenarios.Contains("arrange"))
                await RenderArrange();
            if (scenarios.Contains("settings"))
                await RenderSettings(vm, asm);
            if (scenarios.Contains("launcher"))
                await RenderLauncher(vm, asm, light);
            if (scenarios.Contains("review"))
                await RenderReview(vm, asm);
            if (scenarios.Contains("companion"))
                await RenderCompanion(vm, asm);
            if (scenarios.Contains("ridealongs"))
                await RenderRideAlongs(vm, asm);
            if (scenarios.Contains("propose-widget"))
                await RenderProposeWidget(vm, asm);
        }
        finally
        {
            main?.Close();
            File.WriteAllLines(Path.Combine(Out, "log.txt"), Log);
        }
    }

    /// <summary>
    /// An invented user profile for the Claude Code pages: <c>.claude</c> (a foreign statusline, hooks, plugins,
    /// an invented mod, MCP servers, CLAUDE.md, no retention setting) and <c>.claude-work</c> (Sanduhr's meters mod
    /// installed, retention set), plus an added folder whose settings.json does not parse. Nothing here is a real
    /// path, project or account.
    /// </summary>
    static string BuildFakeProfile()
    {
        string root = Path.Combine(Path.GetTempPath(), "sanduhr-harness-profile-" + Guid.NewGuid().ToString("N")[..8]);
        void Put(string rel, string text)
        {
            string path = Path.Combine(root, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }

        Directory.CreateDirectory(Path.Combine(root, ".claude", "projects", "demo-app"));
        Directory.CreateDirectory(Path.Combine(root, ".claude-work", "projects", "demo-site"));
        Put(Path.Combine(".claude", "settings.json"), """
            {
              "statusLine": { "type": "command", "command": "node statusline.js --api-key sk-ant-demo0000000000000000" },
              "hooks": {
                "PreToolUse": [ { "matcher": "Bash", "hooks": [ { "type": "command", "command": "echo checking" } ] } ],
                "Stop": [ { "hooks": [ { "type": "command", "command": "echo done" } ] } ]
              },
              "enabledPlugins": { "docs-helper@acme-market": true, "lint-bot@acme-market": false },
              "env": { "CLAUDE_CODE_PLUGIN_DIRS": "{PROFILE}\\mods\\band-clock" }
            }
            """.Replace("{PROFILE}", root.Replace("\\", "\\\\")));
        Put(Path.Combine(".claude", "CLAUDE.md"), string.Join("\n", Enumerable.Range(1, 42).Select(i => "Line " + i)) + "\n");
        Put(".claude.json", """
            { "mcpServers": { "docs-search": { "command": "docs-search" }, "tickets": { "command": "tickets" } },
              "projects": { "demo-app": { "mcpServers": { "local-db": { "command": "db" } } } } }
            """);
        Put(Path.Combine(".claude-work", "settings.json"), "{\n  \"cleanupPeriodDays\": 90,\n  \"model\": \"sonnet\"\n}\n");
        Put(Path.Combine("mods", "band-clock", ".claude-plugin", "plugin.json"),
            "{ \"name\": \"band-clock\", \"version\": \"1.2.0\", \"description\": \"Shows the time in a band above the prompt.\" }");
        Put(Path.Combine("mods", "band-clock", "hooks", "hooks.json"), "{ \"modules\": [\"./src/index.ts\"] }");
        Put(Path.Combine("mods", "band-clock", "src", "index.ts"), "slot: 'AbovePrompt'\nconst r = await $.fs.read(path)\n");
        Put(Path.Combine("elsewhere", "claude-home-extra", "settings.json"), "{\n  \"model\": \"sonnet\",\n}\n");

        try
        {
            // Sanduhr's meters mod in .claude-work, from the repo's copy of the mod.
            string? source = null;
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null && source is null; d = d.Parent)
            {
                string c = Path.Combine(d.FullName, "mac", "integrations", "mods", "sanduhr-meters");
                if (Directory.Exists(c)) source = c;
            }
            var integ = new ClaudeCodeIntegrations(root, new Paths().AppDataDir, source);
            var res = integ.Install(IntegrationKind.Meters, Path.Combine(root, ".claude-work"));
            Log.Add($"fake profile: meters mod install ok={res.Ok}");
        }
        catch (Exception e)
        {
            Log.Add($"fake profile: meters mod install failed ({e.GetType().Name})");
        }
        return root;
    }

    /// <summary>The consent dialog the Claude Code page opens, over an invented folder that has another statusline.</summary>
    static async Task RenderConsent(Assembly asm)
    {
        var specType = asm.GetType("Sanduhr.App.Views.ConsentSpec")!;
        var dlgType = asm.GetType("Sanduhr.App.Views.IntegrationConsentDialog")!;
        var spec = Activator.CreateInstance(specType,
            "Show usage under the Claude Code prompt?",
            "Sanduhr adds a statusline with your live session and weekly meters, right where the tokens burn.",
            "~\\.claude",
            "What gets written: the render script, a statusLine entry in this folder's settings.json (a copy is saved first) and a usage snapshot. Remove brings back the statusline that was here before.",
            "Install",
            "node statusline.js --api-key " + ConfigMask.Masked,
            true);
        var dlg = (Window)Activator.CreateInstance(dlgType, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
            null, [spec], null)!;
        OffScreen(dlg);
        dlg.Show();
        dlg.Left = -20000;
        dlg.Top = -20000;
        await Settle();
        Save((FrameworkElement)dlg.Content, "consent-statusline");
        dlg.Close();
        await Settle();
    }

    /// <summary>The Claude Code edge glow over a fake 1280x800 desktop: a maximized-style window and a
    /// 900x600 windowed one, waiting and done, in the live theme. Never opens the real overlay window.</summary>
    static async Task RenderGlow(bool light)
    {
        var screen = new PxRect(0, 0, 1280, 800);
        var layouts = new (string Name, PxRect Frame)[]
        {
            ("maximized", new PxRect(0, 0, 1280, 800)),
            ("windowed", new PxRect(190, 100, 900, 600)),
        };
        foreach (var (name, frame) in layouts)
        foreach (var ev in new[] { ClaudeCodeEvent.Waiting, ClaudeCodeEvent.Done })
        {
            var canvas = new Canvas { Width = screen.Width, Height = screen.Height, Background = new SolidColorBrush(Color.FromRgb(0x58, 0x5c, 0x66)) };
            Color body = light ? Color.FromRgb(0xf2, 0xf2, 0xf4) : Color.FromRgb(0x2b, 0x2b, 0x30);
            Color bar = light ? Color.FromRgb(0xe0, 0xe0, 0xe4) : Color.FromRgb(0x1f, 0x1f, 0x23);
            void Box(double x, double y, double w, double h, Color c)
            {
                var r = new System.Windows.Shapes.Rectangle { Width = w, Height = h, Fill = new SolidColorBrush(c) };
                Canvas.SetLeft(r, x);
                Canvas.SetTop(r, y);
                canvas.Children.Add(r);
            }
            Box(frame.X, frame.Y, frame.Width, frame.Height, body);
            Box(frame.X, frame.Y, frame.Width, 32, bar);

            var target = new GlowTarget(frame, screen);
            var placed = GlowPlacement.Compute(target, screen, 1.0);
            var glow = new Sanduhr.App.Views.GlowOverlayContent { Width = placed.Width, Height = placed.Height };
            glow.SetColor(Sanduhr.App.Views.GlowOverlayContent.ColorFor(ev));
            Canvas.SetLeft(glow, placed.X);
            Canvas.SetTop(glow, placed.Y);
            canvas.Children.Add(glow);

            canvas.Measure(new Size(screen.Width, screen.Height));
            canvas.Arrange(new Rect(0, 0, screen.Width, screen.Height));
            await Settle();
            Save(canvas, $"glow-{name}-{ev.ToString().ToLowerInvariant()}");
            Log.Add($"glow {name} {ev}: rect {placed}");
        }
    }

    /// <summary>
    /// The Desk's content over a wallpaper stand-in (dark #1e2430 and light #d8dde6) with a 48 DIP taskbar
    /// strip along the bottom, at 100% and 150%: the standard layout, the standard layout with the meters
    /// (one tier past 90%), pieces at tc/ml/mr/bc/br/tl at sizes 0.6 and 1.6, and the signed-out lines.
    /// The layout is the same in DIPs at both scales; 150% only renders sharper (2880x1620). Never opens a Desk window.
    /// </summary>
    static async Task RenderDesk(bool light)
    {
        var now = new DateTimeOffset(2026, 10, 11, 10, 9, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 10, 11, 10, 9, 0)));
        string Iso(TimeSpan span) => now.Add(span).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffK", System.Globalization.CultureInfo.InvariantCulture);
        var usage = new DeskUsage(
            [new DeskTier(TierModel.FiveHour, 42, Iso(TimeSpan.FromHours(2))),
             new DeskTier(TierModel.SevenDay, 63, Iso(TimeSpan.FromDays(4))),
             new DeskTier(TierModel.SevenDaySonnet, 91, Iso(TimeSpan.FromDays(4)))],
            now.AddMinutes(-1), false, false, null);
        var signedOut = new DeskUsage([], null, true, true, null);

        var fonts = new Sanduhr.App.Desk.DeskFonts();
        var (family, bold) = fonts.Families(null);
        foreach (var (label, f, weight) in new[] { ("regular", family, FontWeights.Normal), ("bold", bold, FontWeights.Bold) })
        {
            bool ok = new Typeface(f, FontStyles.Normal, weight, FontStretches.Normal).TryGetGlyphTypeface(out var gt);
            string names = ok ? string.Join("/", gt!.Win32FamilyNames.Values) + " | " + gt.FaceNames.Values.FirstOrDefault() : "no glyph typeface";
            Log.Add($"desk font {label}: source={f.Source} resolved={ok} {names}");
        }

        var variants = new (string Name, string Layout, DeskUsage Usage)[]
        {
            ("standard", DeskArrangement.Standard, usage),
            ("meters", "clock:bl claude:bl meters:tr", usage),
            ("anchors-a", "clock:tc:1.6 claude:ml:0.6 meters:mr:1.6", usage),
            ("anchors-b", "clock:br:0.6 claude:bc:1.6 meters:tl:0.6", usage),
            ("signed-out", "clock:bl meters:tr", signedOut),
        };
        var grounds = new (string Name, Color Color)[]
        {
            ("dark", Color.FromRgb(0x1e, 0x24, 0x30)),
            ("light", Color.FromRgb(0xd8, 0xdd, 0xe6)),
        };
        foreach (double scale in new[] { 1.0, 1.5 })
        {
            var bounds = new PxRect(0, 0, (int)(1920 * scale), (int)(1080 * scale));
            int bar = (int)Math.Round(48 * scale);
            var work = new PxRect(0, 0, bounds.Width, bounds.Height - bar);
            var screen = new Sanduhr.App.Desk.DeskScreen("DISPLAY1", bounds, work, scale, true,
                TaskbarClearance.Insets(bounds, work, [], scale));
            foreach (var (gname, gcolor) in grounds)
            foreach (var (name, layout, u) in variants)
            {
                var settings = (DeskSettings.Default with { Enabled = true, Layout = layout }).Normalized();
                var view = new Sanduhr.App.Desk.DeskView(screen, settings, fonts);
                view.Update(u, now);
                var root = new Grid { Width = 1920, Height = 1080, Background = new SolidColorBrush(gcolor) };
                root.Children.Add(new System.Windows.Shapes.Rectangle
                {
                    Height = 48, VerticalAlignment = VerticalAlignment.Bottom,
                    Fill = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)),
                });
                root.Children.Add(view);
                root.Measure(new Size(1920, 1080));
                root.Arrange(new Rect(0, 0, 1920, 1080));
                await Settle();
                SaveScaled(root, $"desk-{name}-{gname}-{scale:0.0}x".Replace('.', '_'), scale);
            }
        }
    }

    /// <summary>
    /// The glow's Desk spot lit (spec 7.3): the standard layout with the spot at top center, in the waiting
    /// and the done color, held at full opacity for the still (the real fade is 0.2 s in, out by 1.5 s).
    /// </summary>
    static async Task RenderGlowSpot(bool light)
    {
        var now = new DateTimeOffset(2026, 10, 11, 10, 9, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 10, 11, 10, 9, 0)));
        var fonts = new Sanduhr.App.Desk.DeskFonts();
        var bounds = new PxRect(0, 0, 1920, 1080);
        var work = new PxRect(0, 0, 1920, 1032);
        var screen = new Sanduhr.App.Desk.DeskScreen("DISPLAY1", bounds, work, 1.0, true, TaskbarClearance.Insets(bounds, work, [], 1.0));
        var settings = (DeskSettings.Default with { Enabled = true, Layout = "clock:bl claude:bl glow:tc" }).Normalized();
        var ground = light ? Color.FromRgb(0xd8, 0xdd, 0xe6) : Color.FromRgb(0x1e, 0x24, 0x30);
        foreach (var ev in new[] { ClaudeCodeEvent.Waiting, ClaudeCodeEvent.Done })
        {
            var view = new Sanduhr.App.Desk.DeskView(screen, settings, fonts);
            view.Update(new Sanduhr.App.Desk.DeskFrame { Now = now }, light
                ? new Sanduhr.App.Desk.DeskInkMap(DeskInk.Resolve(DeskInkSettings.Default with { Mode = DeskInkMode.Dark }, null, null))
                : Sanduhr.App.Desk.DeskInkMap.Light);
            bool lit = view.Glow(Sanduhr.App.Views.GlowOverlayContent.ColorFor(ev));
            // Hold the pill at full opacity: the animation would otherwise be mid-fade when the picture is taken.
            foreach (var pill in Descendants<System.Windows.Shapes.Rectangle>(view).Where(r => r.Fill is RadialGradientBrush))
            {
                pill.BeginAnimation(UIElement.OpacityProperty, null);
                pill.Opacity = 1;
            }
            var root = new Grid { Width = 1920, Height = 1080, Background = new SolidColorBrush(ground) };
            root.Children.Add(view);
            root.Measure(new Size(1920, 1080));
            root.Arrange(new Rect(0, 0, 1920, 1080));
            await Settle();
            SaveScaled(root, $"desk-glow-spot-{ev.ToString().ToLowerInvariant()}", 1.0);
            Log.Add($"desk glow spot {ev}: lit={lit}");
            view.Dispose();
        }
    }

    static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var d in Descendants<T>(child)) yield return d;
        }
    }

    /// <summary>
    /// Arrange mode's surface (spec 7.3) for the primary monitor at 100%: the scrim over a wallpaper stand-in,
    /// the Desk laid out in arranging mode (clock and Claude line, the meters, telemetry, the glow's spot at
    /// top center and now playing with nothing playing as a named placeholder), every placed piece outlined
    /// and named, the eight anchor points, then the clock mid-drag as a ghost with the top right point lit,
    /// and the bar. Never opens a window; the content is the same control the real surface hosts.
    /// </summary>
    static async Task RenderArrange()
    {
        var now = new DateTimeOffset(2026, 10, 11, 10, 9, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 10, 11, 10, 9, 0)));
        string Iso(TimeSpan span) => now.Add(span).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffK", System.Globalization.CultureInfo.InvariantCulture);
        var usage = new DeskUsage(
            [new DeskTier(TierModel.FiveHour, 42, Iso(TimeSpan.FromHours(2))),
             new DeskTier(TierModel.SevenDay, 63, Iso(TimeSpan.FromDays(4)))],
            now.AddMinutes(-1), false, false, null);
        var telemetry = new Sanduhr.App.Desk.TelemetrySource();
        telemetry.Preview(new TelemetrySample(23, 61, 48, now));
        var frame = new Sanduhr.App.Desk.DeskFrame { Usage = usage, Now = now, Telemetry = telemetry };
        var fonts = new Sanduhr.App.Desk.DeskFonts();
        var bounds = new PxRect(0, 0, 1920, 1080);
        var work = new PxRect(0, 0, 1920, 1032);
        var screen = new Sanduhr.App.Desk.DeskScreen("DISPLAY1", bounds, work, 1.0, true, TaskbarClearance.Insets(bounds, work, [], 1.0));
        const string layout = "clock:bl claude:bl meters:tr telemetry:mr glow:tc nowPlaying:br";
        var settings = (DeskSettings.Default with { Enabled = true, Layout = layout }).Normalized();
        foreach (var (gname, gcolor) in new[] { ("wallpaper-dark", Color.FromRgb(0x1e, 0x24, 0x30)), ("wallpaper-light", Color.FromRgb(0xd8, 0xdd, 0xe6)) })
        {
            var working = new DeskArrangement(layout);
            var content = new Sanduhr.App.Desk.DeskArrangeContent(screen, settings, fonts, frame, working, primary: true);
            var root = new Grid { Width = 1920, Height = 1080, Background = new SolidColorBrush(gcolor) };
            root.Children.Add(new System.Windows.Shapes.Rectangle
            {
                Height = 48, VerticalAlignment = VerticalAlignment.Bottom,
                Fill = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)),
            });
            root.Children.Add(content);
            root.Measure(new Size(1920, 1080));
            root.Arrange(new Rect(0, 0, 1920, 1080));
            await Settle();
            Save(root, $"arrange-rest-{gname}");
            foreach (var (w, r) in content.PieceRects) Log.Add($"arrange piece {w}: {r}");

            var clock = content.PieceRects["clock"];
            content.BeginDrag("clock", new Point(clock.X + clock.Width / 2, clock.Y + clock.Height / 2));
            content.MoveDrag(new Point(1640, 130));
            await Settle();
            Save(root, $"arrange-drag-{gname}");
            Log.Add($"arrange drag clock: lit {content.Lit}");
            content.CancelDrag();
            content.Dispose();
        }
        telemetry.Dispose();
    }

    /// <summary>Like <see cref="Save"/>, rendered at <paramref name="scale"/> times 96 dpi.</summary>
    static void SaveScaled(FrameworkElement el, string name, double scale)
    {
        el.UpdateLayout();
        int w = (int)Math.Ceiling(el.ActualWidth * scale), h = (int)Math.Ceiling(el.ActualHeight * scale);
        var rtb = new RenderTargetBitmap(Math.Max(1, w), Math.Max(1, h), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        rtb.Render(el);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using (var f = File.Create(Path.Combine(Out, name + ".png")))
            enc.Save(f);
        Log.Add($"png {name}.png {w}x{h}");
    }

    /// <summary>Demo mode fills Settings with the sample accounts (Stage 3). Fail before anything
    /// renders if a label that is not a sample would reach a PNG.</summary>
    static void AssertOnlySampleAccounts(SettingsViewModel svm)
    {
        var allowed = new HashSet<string>(DemoMode.Accounts) { "All accounts" };
        var shown = svm.Accounts.Accounts.Select(a => a.Label)
            .Concat(svm.History.AccountOptions.Select(o => o.Display))
            .Concat(svm.History.Legend.Select(l => l.Label));
        if (shown.Any(label => !allowed.Contains(label)))
            throw new InvalidOperationException("A non-sample account label would render in Settings; stopping before any PNG.");
    }

    /// <summary>
    /// Stage 6's Desk content (spec 6.12): the message on a special day (a gradient line with its glow, a
    /// script line, a write-in line, drawn as still frames), now playing with its bars, telemetry, weather
    /// and watchers from samples, in Light, Dark and Automatic ink, over a dark, a light and a split
    /// wallpaper stand-in (left half dark, right half light, so Automatic picks per island), at 100% and
    /// 150%. Automatic reads the stand-in through the same WallpaperSampler the app uses. Never opens a Desk
    /// window, never reads the real wallpaper, never touches media sessions, the network or the meter.
    /// </summary>
    static async Task RenderDeskContent()
    {
        var now = new DateTimeOffset(2026, 10, 31, 10, 9, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 10, 31, 10, 9, 0)));
        string Iso(TimeSpan span) => now.Add(span).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffK", System.Globalization.CultureInfo.InvariantCulture);
        var usage = new DeskUsage(
            [new DeskTier(TierModel.FiveHour, 42, Iso(TimeSpan.FromHours(2))),
             new DeskTier(TierModel.SevenDay, 91, Iso(TimeSpan.FromDays(4)))],
            now.AddMinutes(-1), false, false, null);
        const string text = "# sample\nkeep building.\n{write} one thing at a time.\n10-31: {ink:#ff7e5f,#feb47b,#ffd86f} {glow} happy halloween.\n10-31: {font:script} trick or treat.\n";
        var today = DeskMessages.Today(text, now, hourly: false);
        var message = new Sanduhr.App.Desk.DeskMessageFrame(today, DeskMessageSettings.Default with { Size = 64 });
        var track = new NowPlayingInfo
        {
            Title = "Midnight City", Artist = "M83", Duration = 243, Elapsed = 101, ElapsedAt = now, Playing = true, AppId = "Spotify.exe",
        };
        var audio = new Sanduhr.App.Desk.AudioLevelSource();
        float[] levels = [0.05f, 0.2f, 0.45f, 0.3f, 0.7f, 0.5f, 0.9f, 0.6f, 0.35f, 0.8f, 0.55f, 0.25f, 0.65f, 0.4f, 0.75f, 0.3f];
        foreach (var l in levels) audio.Bars.Push(l * l, now);
        var telemetry = new Sanduhr.App.Desk.TelemetrySource();
        telemetry.Preview(new TelemetrySample(23, 61, 48, now));
        var place = WeatherPlace.Create("Fort Worth", "Texas", "United States", 32.7555, -97.3308);
        var reading = new WeatherReading(72, 2, 80, 61, 80, 75, 60, now.AddMinutes(-5));
        IReadOnlyList<Watcher> watchers =
        [
            new Watcher { Id = "a:1", Source = WatcherSource.Agent, Title = "PR 210 CI", Total = 12, Done = 7, State = WatcherState.Running, Started = now.AddMinutes(-6), Touched = now },
            new Watcher { Id = "a:2", Source = WatcherSource.Agent, Title = "Review the release notes", State = WatcherState.Waiting, Started = now.AddMinutes(-2), Touched = now },
        ];
        var frame = new Sanduhr.App.Desk.DeskFrame
        {
            Usage = usage, Now = now, Message = message, Track = track, Bars = true, Audio = audio,
            Telemetry = telemetry, WeatherOn = true, Place = place, Weather = reading, Watchers = watchers,
        };
        const string layout = "message:tl clock:bl claude:bl meters:br nowPlaying:bc telemetry:tr weather:mr watchers:ml";
        var fonts = new Sanduhr.App.Desk.DeskFonts();
        var dark = Color.FromRgb(0x1e, 0x24, 0x30);
        var light = Color.FromRgb(0xd8, 0xdd, 0xe6);
        var grounds = new (string Name, Color Left, Color Right)[] { ("dark", dark, dark), ("light", light, light), ("split", dark, light) };
        var inks = new[] { DeskInkMode.Light, DeskInkMode.Dark, DeskInkMode.Auto };
        foreach (double scale in new[] { 1.0, 1.5 })
        {
            var bounds = new PxRect(0, 0, (int)(1920 * scale), (int)(1080 * scale));
            int bar = (int)Math.Round(48 * scale);
            var work = new PxRect(0, 0, bounds.Width, bounds.Height - bar);
            var screen = new Sanduhr.App.Desk.DeskScreen("DISPLAY1", bounds, work, scale, true, TaskbarClearance.Insets(bounds, work, [], scale));
            var settings = (DeskSettings.Default with { Enabled = true, Layout = layout }).Normalized();
            foreach (var (gname, gl, gr) in grounds)
            {
                // The stand-in as a wallpaper image for Automatic: 192x108, left half gl, right half gr.
                const int iw = 192, ih = 108;
                var px = new byte[iw * ih * 4];
                for (int y = 0; y < ih; y++)
                    for (int x = 0; x < iw; x++)
                    {
                        var c = x < iw / 2 ? gl : gr;
                        int o = (y * iw + x) * 4;
                        px[o] = c.B; px[o + 1] = c.G; px[o + 2] = c.R; px[o + 3] = 255;
                    }
                foreach (var mode in inks)
                {
                    if (mode != DeskInkMode.Auto && gname == "split") continue;
                    var view = new Sanduhr.App.Desk.DeskView(screen, settings, fonts);
                    var inkSettings = DeskInkSettings.Default with { Mode = mode };
                    Sanduhr.App.Desk.DeskInkMap map;
                    var picks = new List<string>();
                    if (mode != DeskInkMode.Auto)
                        map = new(DeskInk.Resolve(inkSettings, null, null));
                    else
                    {
                        view.Update(frame, Sanduhr.App.Desk.DeskInkMap.Light);
                        var by = new Dictionary<DeskAnchor, ResolvedInk>();
                        foreach (var (anchor, r) in view.StackRects())
                        {
                            var region = new PxRect((int)Math.Round(r.X * scale), (int)Math.Round(r.Y * scale),
                                Math.Max(1, (int)Math.Round(r.Width * scale)), Math.Max(1, (int)Math.Round(r.Height * scale)));
                            double? lum = WallpaperSampler.Luminance(px, iw, ih, iw * 4, iw, ih, bounds, region, WallpaperPosition.Fill, bounds, 0);
                            by[anchor] = DeskInk.Resolve(inkSettings, null, lum);
                            picks.Add($"{DeskAnchors.Word(anchor)}={(by[anchor].DarkInk ? "dark" : "light")}({lum:0.00})");
                        }
                        map = new(DeskInk.Resolve(inkSettings, null, null), by);
                    }
                    view.Update(frame, map);
                    var root = new Grid { Width = 1920, Height = 1080 };
                    root.ColumnDefinitions.Add(new ColumnDefinition());
                    root.ColumnDefinitions.Add(new ColumnDefinition());
                    var lr = new System.Windows.Shapes.Rectangle { Fill = new SolidColorBrush(gl) };
                    var rr = new System.Windows.Shapes.Rectangle { Fill = new SolidColorBrush(gr) };
                    Grid.SetColumn(rr, 1);
                    root.Children.Add(lr);
                    root.Children.Add(rr);
                    var strip = new System.Windows.Shapes.Rectangle
                    {
                        Height = 48, VerticalAlignment = VerticalAlignment.Bottom,
                        Fill = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)),
                    };
                    Grid.SetColumnSpan(strip, 2);
                    root.Children.Add(strip);
                    Grid.SetColumnSpan(view, 2);
                    root.Children.Add(view);
                    root.Measure(new Size(1920, 1080));
                    root.Arrange(new Rect(0, 0, 1920, 1080));
                    await Settle();
                    var name = $"desk-s6-{mode.ToString().ToLowerInvariant()}-{gname}-{scale:0.0}x".Replace('.', '_');
                    SaveScaled(root, name, scale);
                    if (picks.Count > 0) Log.Add($"{name} auto ink: {string.Join(" ", picks)}");
                    view.Dispose();
                }
            }
        }
        audio.Dispose();
        telemetry.Dispose();
    }

    /// <summary>Reset Sanduhr: the choose state, then a results state with one failure. No ResetService is built,
    /// so nothing can run; the window only shows what it is handed.</summary>
    static async Task RenderReset(Assembly asm)
    {
        var rwType = asm.GetType("Sanduhr.App.Views.ResetWindow")!;
        var rw = (Window)Activator.CreateInstance(rwType, new object?[] { null })!;
        OffScreen(rw);
        rw.Show();
        rw.Left = -20000;
        rw.Top = -20000;
        await Settle();
        Save((FrameworkElement)rw.Content, "reset-choose");

        var results = new List<ResetStepResult>
        {
            ResetStepResult.Done(ResetPart.ClaudeCodeEntries),
            ResetStepResult.Failed(ResetPart.Accounts, ["a sign-in profile folder"]),
            ResetStepResult.Done(ResetPart.History),
            ResetStepResult.Done(ResetPart.Themes),
            ResetStepResult.Done(ResetPart.Settings),
        };
        rwType.GetMethod("ShowResults")!.Invoke(rw, [results]);
        await Settle();
        Save((FrameworkElement)rw.Content, "reset-results");
        rw.Close();
        await Settle();
    }

    /// <summary>The harness never touches the network: Settings ▸ Widgets fetches the catalog when it opens, so it gets a
    /// network that is never there and draws its offline line.</summary>
    sealed class OfflineHandler : System.Net.Http.HttpMessageHandler
    {
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken ct) =>
            throw new System.Net.Http.HttpRequestException("the render harness is offline");
    }

    static async Task RenderSettings(WidgetViewModel vm, Assembly asm)
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
        await Settle();

        var tabs = (TabControl)sw.FindName("Tabs");
        var pages = new StringBuilder();
        pages.AppendLine("index\theader\taccessible-name");
        int index = 0;
        foreach (var item in tabs.Items.OfType<TabItem>())
        {
            string header = HeaderText(item.Header);
            string name = AutomationProperties.GetName(item);
            if (string.IsNullOrEmpty(name))
                name = header;
            pages.AppendLine($"{index}\t{header}\t{name}");
            tabs.SelectedItem = item;
            await Settle();
            Save((FrameworkElement)sw.Content, $"settings-{index:00}-{Slug(header)}");
            if (item.Tag is "ClaudeCode" or "Mods" or "Desk" or "DeskMessage" or "NowPlaying" or "Weather")
            {
                // These pages scroll: a second picture with the window tall enough to show all of it.
                double h = sw.Height;
                sw.Height = 1900;
                await Settle();
                Save((FrameworkElement)sw.Content, $"settings-{index:00}-{Slug(header)}-full");
                sw.Height = h;
                await Settle();
            }
            index++;
        }
        // The Claude Code page with the Desk's spot chosen (it also places the spot in the dev profile's layout),
        // and the Desk page while Arrange mode would be running on the desktop.
        var ccTab = tabs.Items.OfType<TabItem>().First(t => t.Tag is "ClaudeCode");
        tabs.SelectedItem = ccTab;
        svm.ClaudeCodePage.GlowTarget = ClaudeCodeViewModel.GlowTargets.First(c => c.Kind == GlowTargetKind.Desk);
        double h0 = sw.Height;
        sw.Height = 1900;
        await Settle();
        Save((FrameworkElement)sw.Content, "settings-claudecode-desk-target");
        sw.Height = h0;
        var deskTab = tabs.Items.OfType<TabItem>().First(t => t.Tag is "Desk");
        tabs.SelectedItem = deskTab;
        svm.Desk.Reload();
        svm.Desk.Arranging = true;
        svm.Desk.ArrangingLayout = "clock:tr claude:tr glow:tc";
        await Settle();
        Save((FrameworkElement)sw.Content, "settings-desk-arranging");
        svm.Desk.Arranging = false;
        svm.Desk.ArrangingLayout = null;
        File.WriteAllText(Path.Combine(Out, "settings-pages.txt"), pages.ToString());
        Log.Add($"txt settings-pages.txt {index} pages");

        // Search: the results list in place of the pages, then a result opened at its section.
        svm.Search.Query = "vault";
        await Settle();
        Save((FrameworkElement)sw.Content, "settings-search-vault");
        Log.Add($"search 'vault': {string.Join(" | ", svm.Search.Results.Select(r => r.Title))}");
        svm.Search.Query = "";
        var select = swType.GetMethod("SelectPage")!;
        select.Invoke(sw, [SettingsPage.About, SettingsIndex.Where]);
        await Settle();
        Save((FrameworkElement)sw.Content, "settings-anchor-about-where");
        select.Invoke(sw, [SettingsPage.ClaudeUsage, SettingsIndex.Sessions]);
        await Settle();
        Save((FrameworkElement)sw.Content, "settings-anchor-claude-usage-sessions");
        sw.Close();
        await Settle();
        await RenderConsent(asm);
    }
}
