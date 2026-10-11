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
//   --scenario <name>    widget | whatsnew | tour | settings | all; repeatable (default all)
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
public static class Program
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
        string[] known = { "widget", "whatsnew", "tour", "settings" };
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

        // Reads the real Credential Manager accounts (constructor only). No Start().
        var vm = new WidgetViewModel();
        vm.ApplyThemeByKey(themeKey);
        vm.ApplyThemeResources();
        // Before anything renders: sample accounts only ("Work", "Personal").
        vm.StartDemo();
        await Settle();

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

            if (scenarios.Contains("settings"))
                await RenderSettings(vm, asm);
        }
        finally
        {
            main?.Close();
            File.WriteAllLines(Path.Combine(Out, "log.txt"), Log);
        }
    }

    /// <summary>Demo mode swaps the widget's labels, but the Settings tabs list the real registry
    /// (Accounts list, General summary, History account picker). Replace every one with the demo
    /// labels before the window exists, so no real label reaches a PNG.</summary>
    static void MaskAccountLabels(SettingsViewModel svm)
    {
        var accounts = svm.Accounts;
        accounts.Accounts.Clear();
        accounts.Accounts.Add(new AccountItemViewModel("Work", true));
        accounts.Accounts.Add(new AccountItemViewModel("Personal", false));
        accounts.HasNoAccounts = false;
        accounts.ActiveLabel = "Work";
        accounts.SelectedAccount = accounts.Accounts[0];

        var history = svm.History;
        history.SelectedOption = history.AccountOptions[0];
        history.AccountOptions.Clear();
        history.AccountOptions.Add(new AccountOption("All accounts", null));
        history.AccountOptions.Add(new AccountOption("Work", "Work"));
        history.AccountOptions.Add(new AccountOption("Personal", "Personal"));
        history.SelectedOption = history.AccountOptions[0];
        history.Legend.Clear();
        history.Legend.Add(new LegendItem("Work", Brushes.SteelBlue));
        history.Legend.Add(new LegendItem("Personal", Brushes.IndianRed));
        history.ShowLegend = true;
    }

    static async Task RenderSettings(WidgetViewModel vm, Assembly asm)
    {
        var svm = new SettingsViewModel(vm, () => Task.CompletedTask, _ => Task.CompletedTask);
        MaskAccountLabels(svm);
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
            index++;
        }
        File.WriteAllText(Path.Combine(Out, "settings-pages.txt"), pages.ToString());
        Log.Add($"txt settings-pages.txt {index} pages");
        sw.Close();
        await Settle();
    }
}
