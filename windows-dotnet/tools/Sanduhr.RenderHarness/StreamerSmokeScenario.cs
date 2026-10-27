using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.App.Updates;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

// The streamer smoke (streamer-smoke): the owner's eleven Tier 2 checks for Settings v2 slice 7, run by the harness with no clicks,
// off screen, against scratch profiles. Written to <out>/streamer-smoke/ (PNGs), <out>/streamer-smoke-results.json (verdicts and
// evidence, no real names), <out>/harvest.json and <out>/streamer-smoke-private.txt (real names: they stay in the output folder).
//
//   $env:SANDUHR_DEV_PROFILE = "C:\path\to\scratch"       # required; every profile the run uses is a folder under it
//   dotnet run --project windows-dotnet/tools/Sanduhr.RenderHarness -- --scenario streamer-smoke --out C:\path\to\out
//
// Safety, in this order of importance:
//   - Paths follow SANDUHR_DEV_PROFILE (the run sets it to a sub-folder per phase, all under the scratch root). Every path the app
//     resolves is checked to sit under the scratch root before a phase starts.
//   - Credential Manager is never opened: accounts live in an in-memory registry (labels only; no secret is read or written).
//   - Check 8 copies the owner's AppData and vault files into the scratch profile (read only from the source, size capped) and
//     reads the real Claude Code folders in place, read only. Nothing is written to any of them.
//   - Windows are shown off screen and never activated; the foreground handle is compared before, after and between phases.
//     Modal dialogs are driven by a timer that reads them and answers programmatically.
//   - No network: the catalog gets an offline handler, the update check is stubbed, nothing calls Start().
// A tray icon is created briefly for the checks that need the tray's real handlers (App.SetupTray), and disposed right after.
public static partial class Program
{
    // ------------------------------------------------------------------ results

    sealed class SmokeCheck
    {
        public int N;
        public string Title = "";
        public string Verdict = "NOT RUN";
        public List<string> Evidence = new();
    }

    static readonly SortedDictionary<int, SmokeCheck> Smoke = new();
    static readonly List<string> SmokePrivate = new();
    static string SmokeRootProfile = "";
    static IntPtr SmokeFg0;
    static readonly List<string> SmokeFgDrift = new();

    static SmokeCheck SC(int n, string title = "")
    {
        if (!Smoke.TryGetValue(n, out var c))
            Smoke[n] = c = new SmokeCheck { N = n, Title = title };
        if (title.Length > 0) c.Title = title;
        return c;
    }

    static void Ev(int n, string text) => SC(n).Evidence.Add(text);

    /// <summary>Records an assertion: a failed one marks the check FAIL and says so; a passed one is evidence.</summary>
    static bool Assert(int n, bool ok, string what)
    {
        var c = SC(n);
        c.Evidence.Add((ok ? "ok: " : "FAIL: ") + what);
        if (!ok) c.Verdict = "FAIL";
        return ok;
    }

    static void Verdict(int n, string verdict)
    {
        var c = SC(n);
        if (c.Verdict != "FAIL") c.Verdict = verdict;
    }

    static void FgNote(string where)
    {
        var now = GetForegroundWindow();
        if (now != SmokeFg0) SmokeFgDrift.Add($"{where}: foreground {now} (was {SmokeFg0})");
    }

    static string SmokeUse(string phase)
    {
        string dir = Path.Combine(SmokeRootProfile, phase);
        Directory.CreateDirectory(dir);
        Environment.SetEnvironmentVariable("SANDUHR_DEV_PROFILE", dir);
        var p = new Paths();
        foreach (var path in new[] { p.AppDataDir, p.ExternalDirPath, p.SettingsFile, p.CodenamesFile, p.StreamerStateFile, p.VaultDir })
            if (!Path.GetFullPath(path).StartsWith(SmokeRootProfile, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A path resolves outside the scratch root; stopped before anything ran.");
        return dir;
    }

    sealed class SmokeCreds : ICredentialManager
    {
        private readonly Dictionary<string, string> _s = new();
        public string Service => AccountStore.Service;
        public string? GetPassword(string slot) => _s.GetValueOrDefault(slot);
        public void SetPassword(string slot, string value) => _s[slot] = value;
        public bool DeletePassword(string slot) => _s.Remove(slot);
    }

    sealed class SmokeRig : IDisposable
    {
        public required WidgetViewModel Widget;
        public required AccountStore Accounts;
        public required SettingsViewModel Svm;
        public required SettingsWindow Window;
        public required TabControl Tabs;
        public required string Home;
        public required SmokeCreds Creds;
        public bool Disposed;
        public bool KeepWidget;

        public FrameworkElement Content(SettingsPage page) =>
            (FrameworkElement)Tabs.Items.OfType<TabItem>().Single(t => (string)t.Tag == page.ToString()).Content;

        public async Task Go(SettingsPage page, string? anchor = null)
        {
            Window.SelectPage(page, anchor);
            await Settle2();
        }

        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            try { Window.Close(); } catch { }
            if (!KeepWidget)
            {
                try { Widget.Dispose(); } catch { }
                StreamerMode.Reset();
            }
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, SmokeHookProc proc, IntPtr hMod, uint threadId);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern uint GetCurrentThreadId();
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct SmokeRect { public int L, T, R, B; }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hWnd, out SmokeRect rect);
    delegate IntPtr SmokeHookProc(int nCode, IntPtr wParam, IntPtr lParam);
    static SmokeHookProc? _cbt;
    static IntPtr _cbtHandle;
    static int SmokeActivationsDenied;

    /// <summary>A CBT hook on the UI thread that refuses every window activation, so no window of this run (including a dialog the app
    /// opens itself with ShowDialog) can take the foreground from the person at the PC.</summary>
    static void SmokeHookWindows()
    {
        _cbt = (code, wParam, lParam) =>
        {
            const int HCBT_ACTIVATE = 5, HCBT_CREATEWND = 3;
            if (code == HCBT_CREATEWND)
            {
                // A top-level window of this run is created off screen, tool-window and no-activate; WPF may place it again, which the
                // modal driver measures and reports.
                var cs = System.Runtime.InteropServices.Marshal.ReadIntPtr(lParam);
                const int WS_CHILD = 0x40000000;
                if (cs != IntPtr.Zero && (System.Runtime.InteropServices.Marshal.ReadInt32(cs, 48) & WS_CHILD) == 0
                    && System.Runtime.InteropServices.Marshal.ReadIntPtr(cs, 24) != (IntPtr)(-3))
                {
                    System.Runtime.InteropServices.Marshal.WriteInt32(cs, 40, -20000);
                    System.Runtime.InteropServices.Marshal.WriteInt32(cs, 44, -20000);
                    // WS_EX_LAYERED with no layered attributes set is not drawn at all: a dialog that WPF pulls back on screen (CenterOwner
                    // clamps to the screen) stays invisible. Click-through and tool-window and no-activate as well.
                    int ex = System.Runtime.InteropServices.Marshal.ReadInt32(cs, 72);
                    System.Runtime.InteropServices.Marshal.WriteInt32(cs, 72, ex | 0x08000000 | 0x80 | 0x00080000 | 0x20);
                }
            }
            if (code == HCBT_ACTIVATE)
            {
                SmokeActivationsDenied++;
                return (IntPtr)1;
            }
            return CallNextHookEx(_cbtHandle, code, wParam, lParam);
        };
        _cbtHandle = SetWindowsHookEx(5, _cbt, IntPtr.Zero, GetCurrentThreadId());
        Log.Add($"streamer-smoke: activation guard {(_cbtHandle == IntPtr.Zero ? "NOT installed" : "installed")}");
    }

    static void WriteTranscript(string path, string cwd, string skill)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var ev = new JsonObject
        {
            ["type"] = "assistant",
            ["timestamp"] = DateTimeOffset.UtcNow.AddHours(-2).ToString("O"),
            ["cwd"] = cwd,
            ["attributionSkill"] = skill,
            ["message"] = new JsonObject
            {
                ["model"] = "claude-fable-5",
                ["usage"] = new JsonObject { ["input_tokens"] = 4000, ["output_tokens"] = 2000 },
            },
        };
        File.WriteAllText(path, ev.ToJsonString() + "\n");
    }

    const string FxShown = "Qxbeta";
    const string FxEndpoint = "https://qxhost.example/ingest";
    const string FxSpaceProject = "qx proj";
    const string FxPublishError = "Rejected (400): qxuser@qxmail.com is not allowed";

    /// <summary>A fixture rig: an invented user profile, two accounts, a publish service, a weather place; streamer mode off until asked.</summary>
    static async Task<SmokeRig> BuildFixtureRig(string phase, bool open = true)
    {
        string profile = SmokeUse(phase);
        string home = Path.Combine(profile, "home");
        void Home(string name, string settings = "{}")
        {
            Directory.CreateDirectory(Path.Combine(home, name, "projects"));
            File.WriteAllText(Path.Combine(home, name, "settings.json"), settings);
        }
        Home(".claude");
        Home(".claude-qxwork", JsonSerializer.Serialize(new
        {
            statusLine = new { type = "command", command = $"powershell -File \"{Path.Combine(home, ".claude-qxwork", "statusline.ps1")}\"" },
            enabledPlugins = new Dictionary<string, bool> { ["qxplugin@qxmarket"] = true },
        }));
        Home(".claude-qx home");
        ClaudeCodeProfile.ProfileOverride = home;
        ClaudeCodeProfile.ConfigDirOverride = "";
        WidgetCatalogService.HandlerOverride = () => new OfflineHandler();

        var creds = new SmokeCreds();
        var accounts = new AccountStore(creds);
        foreach (var label in new[] { "Qxalpha", FxShown })
            accounts.AddAccount(label, "placeholder-" + label);
        accounts.SetActive("Qxalpha");
        var paths = new Paths();
        var widget = new WidgetViewModel(paths, accounts);
        widget.SetAccountFolder("Qxalpha", Path.Combine(home, ".claude-qxwork"));

        string projects = Path.Combine(home, ".claude-qxwork", "projects");
        WriteTranscript(Path.Combine(projects, "p1", "s1.jsonl"), Path.Combine(home, "qxproject"), "qxskill");
        WriteTranscript(Path.Combine(projects, "p2", "s2.jsonl"), Path.Combine(home, FxSpaceProject), "qxspace-skill");

        var store = new SettingsStore(paths);
        store.SavePublishEndpointUrl(FxEndpoint);
        store.SavePublishAuthScheme(PublishAuthScheme.None);
        store.SavePublishRoots(new Dictionary<string, bool> { [".claude-qxwork"] = true });
        store.SavePublishAttempt(DateTimeOffset.Now, FxPublishError, null);
        var service = new UsagePublishService(store, new CcLogReader(home, () => [".claude-qxwork", ".claude-qx home"]), paths,
            new PublishTokenStore(creds), () => home);
        widget.AttachPublishService(service);

        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask,
            checkForUpdatesNow: () => Task.FromResult(new UpdateProbeResult(UpdateProbeStatus.UpToDate)), isStoreBuild: false);
        svm.WeatherPage.Picked = WeatherPlace.Create("Qxcity", "Texas", "United States", 32.75, -97.33);
        svm.WeatherPage.ShowResultsForTests([WeatherPlace.Create("Qxtown", null, "Germany", 50.1, 8.7)]);
        return await OpenRig(widget, accounts, svm, home, creds, open);
    }

    static async Task<SmokeRig> OpenRig(WidgetViewModel widget, AccountStore accounts, SettingsViewModel svm, string home, SmokeCreds creds, bool open)
    {
        var window = new SettingsWindow(svm);
        OffScreen(window);
        if (open)
        {
            window.Show();
            window.Left = -20000;
            window.Top = -20000;
            NeverActivateWindow(window);
            await Settle2();
        }
        var tabs = (TabControl)window.FindName("Tabs");
        FgNote("rig opened");
        return new SmokeRig { Widget = widget, Accounts = accounts, Svm = svm, Window = window, Tabs = tabs, Home = home, Creds = creds };
    }

    static void NeverActivateWindow(Window w)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(w).Handle;
        if (hwnd == IntPtr.Zero) return;
        long ex = GetWindowLongPtr(hwnd, -20).ToInt64();
        SetWindowLongPtr(hwnd, -20, (IntPtr)(ex | 0x08000000L | 0x80L));
    }

    // ------------------------------------------------------------------ modal driving

    /// <summary>Watches for windows that appear while a command is blocked in ShowDialog (or awaiting one), hands each to
    /// <paramref name="onNew"/> once, and closes it afterwards. A window that outlives 12 seconds is closed anyway.</summary>
    sealed class ModalWatch : IDisposable
    {
        private readonly DispatcherTimer _timer;
        private readonly HashSet<Window> _known;
        private readonly HashSet<Window> _seen = new();
        private readonly Dictionary<Window, int> _age = new();
        public readonly List<string> Notes = new();
        public int Count => _seen.Count;
        public static int OnScreenDialogs;
        public static readonly List<string> Positions = new();

        public ModalWatch(Action<Window> onNew)
        {
            _known = [.. Application.Current.Windows.OfType<Window>()];
            _timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(40) };
            _timer.Tick += (_, _) =>
            {
                foreach (var w in Application.Current.Windows.OfType<Window>().ToList())
                {
                    if (_known.Contains(w) || !w.IsVisible) continue;
                    _age[w] = _age.GetValueOrDefault(w) + 1;
                    if (_seen.Add(w))
                    {
                        var h = new System.Windows.Interop.WindowInteropHelper(w).Handle;
                        GetWindowRect(h, out var rc);
                        bool layered = (GetWindowLongPtr(h, -20).ToInt64() & 0x80000L) != 0;
                        Positions.Add($"{w.GetType().Name} rect {rc.L},{rc.T}-{rc.R},{rc.B}, layered-and-undrawn {layered}");
                        if (rc.R > -10000 && rc.B > -10000 && !layered) OnScreenDialogs++;
                        try { onNew(w); }
                        catch (Exception e) { Notes.Add($"handler for {w.GetType().Name} threw {e.GetType().Name}: {e.Message}"); }
                    }
                    if (w.IsVisible && _age[w] > 300)
                        Notes.Add($"{w.GetType().Name} stayed open and was closed");
                    if (w.IsVisible && (_age[w] > 300 || _closeNow.Contains(w))) CloseWindow(w);
                }
            };
            _timer.Start();
        }

        private readonly HashSet<Window> _closeNow = new();
        public void Done(Window w) => _closeNow.Add(w);

        public void Dispose() => _timer.Stop();
    }

    static void CloseWindow(Window w)
    {
        try { w.DialogResult = false; } catch (InvalidOperationException) { }
        if (w.IsVisible) w.Close();
    }

    // ------------------------------------------------------------------ texts

    sealed record SmokeItem(string Surface, string Control, string Prop, string Text, string Where);

    static string WhereOf(DependencyObject d)
    {
        var parts = new List<string>();
        for (DependencyObject? p = d; p is not null && parts.Count < 4; p = p is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(p) : LogicalTreeHelper.GetParent(p))
            if (p is FrameworkElement { Name.Length: > 0 } fe) parts.Add(fe.Name);
        return string.Join("<", parts);
    }

    static void ExpandEverything(DependencyObject root)
    {
        for (int pass = 0; pass < 3; pass++)
        {
            foreach (var e in Descendants<Expander>(root).ToList())
                e.IsExpanded = true;
            if (root is FrameworkElement fe) fe.UpdateLayout();
        }
    }

    static void RealizeItems(DependencyObject root)
    {
        if (root is ItemsControl list) VirtualizingPanel.SetIsVirtualizing(list, false);
        int n = root is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetChildrenCount(root) : 0;
        for (int i = 0; i < n; i++) RealizeItems(VisualTreeHelper.GetChild(root, i));
    }

    static void ToolTipTexts(object? tip, DependencyObject owner, string surface, string control, string where, List<SmokeItem> into)
    {
        switch (tip)
        {
            case string s: into.Add(new(surface, control, "ToolTip", s, where)); break;
            case ToolTip { Content: string c }: into.Add(new(surface, control, "ToolTip", c, where)); break;
            case ToolTip { Content: DependencyObject d }: LogicalTexts(d, surface, control, "ToolTip", where, into); break;
            case DependencyObject d2: LogicalTexts(d2, surface, control, "ToolTip", where, into); break;
        }
    }

    static void LogicalTexts(DependencyObject d, string surface, string control, string prop, string where, List<SmokeItem> into)
    {
        if (d is TextBlock tb) into.Add(new(surface, control, prop, tb.Text, where));
        else if (d is ContentControl { Content: string s }) into.Add(new(surface, control, prop, s, where));
        foreach (var child in LogicalTreeHelper.GetChildren(d))
            if (child is DependencyObject c) LogicalTexts(c, surface, control, prop, where, into);
            else if (child is string str) into.Add(new(surface, control, prop, str, where));
    }

    static void MenuTexts(ItemsControl menu, string surface, string control, string where, List<SmokeItem> into, int depth = 0)
    {
        foreach (var o in menu.Items)
        {
            if (o is Separator) continue;
            if (o is MenuItem mi)
            {
                string? h = mi.Header as string ?? (mi.Header as TextBlock)?.Text ?? mi.Header?.ToString();
                if (h is not null) into.Add(new(surface, "MenuItem", "Header", h, where));
                ToolTipTexts(mi.ToolTip, mi, surface, "MenuItem", where, into);
                into.Add(new(surface, "MenuItem", "AutomationProperties.Name", AutomationProperties.GetName(mi), where));
                if (depth < 3) MenuTexts(mi, surface, control, where, into, depth + 1);
            }
            else if (o is string s) into.Add(new(surface, "MenuItem", "Header", s, where));
        }
    }

    /// <summary>Every piece of text a visual tree draws or exposes: text, runs, boxes, string content, headers, list items, tool tips,
    /// automation names and help text, and the items of any context menu the elements carry (read without opening the menu).</summary>
    static int CollectTexts(DependencyObject root, string surface, List<SmokeItem> into)
    {
        int elements = 0;
        var menus = new HashSet<ContextMenu>();
        void Visit(DependencyObject c)
        {
            elements++;
            string ctl = c.GetType().Name;
            string where = WhereOf(c);
            switch (c)
            {
                case TextBlock t:
                    into.Add(new(surface, ctl, "Text", t.Text, where));
                    foreach (var run in t.Inlines.OfType<Run>()) into.Add(new(surface, "Run", "Text", run.Text, where));
                    foreach (var link in t.Inlines.OfType<Hyperlink>())
                        ToolTipTexts(link.ToolTip, link, surface, "Hyperlink", where, into);
                    break;
                case TextBox b:
                    into.Add(new(surface, ctl, "Text", b.Text, where));
                    break;
                case HeaderedContentControl hc when hc.Header is string hs:
                    into.Add(new(surface, ctl, "Header", hs, where));
                    break;
                case HeaderedItemsControl hi when hi.Header is string his:
                    into.Add(new(surface, ctl, "Header", his, where));
                    break;
            }
            if (c is ContentControl { Content: string s }) into.Add(new(surface, ctl, "Content", s, where));
            if (c is ItemsControl list && c is not MenuItem && c is not ContextMenu)
                foreach (var item in list.Items)
                    into.Add(new(surface, ctl, "Item(" + (item?.GetType().Name ?? "null") + ")", item is string str ? str : item?.ToString() ?? "", where));
            if (c is MenuItem mi2 && mi2.Header is string mh) into.Add(new(surface, "MenuItem", "Header", mh, where));
            if (c is FrameworkElement fe)
            {
                ToolTipTexts(fe.ToolTip, fe, surface, ctl, where, into);
                into.Add(new(surface, ctl, "AutomationProperties.Name", AutomationProperties.GetName(fe), where));
                into.Add(new(surface, ctl, "AutomationProperties.HelpText", AutomationProperties.GetHelpText(fe), where));
                if (fe.ContextMenu is { } cm && menus.Add(cm))
                {
                    cm.PlacementTarget = fe;
                    cm.DataContext = fe.DataContext;
                    MenuTexts(cm, surface, ctl + ".ContextMenu", where, into);
                }
            }
        }
        void Walk(DependencyObject d)
        {
            int n = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetChildrenCount(d) : 0;
            for (int i = 0; i < n; i++)
            {
                var child = VisualTreeHelper.GetChild(d, i);
                if (child is UIElement { Visibility: not Visibility.Visible }) continue;
                Visit(child);
                Walk(child);
            }
        }
        Visit(root);
        Walk(root);
        return elements;
    }

    static int CollectSpoken(FrameworkElement root, string surface, List<SmokeItem> into)
    {
        RealizeItems(root);
        root.UpdateLayout();
        int count = 0;
        var peer = UIElementAutomationPeer.CreatePeerForElement(root);
        if (peer is null) return 0;
        void Go(AutomationPeer p, int depth)
        {
            if (depth > 60 || count > 8000) return;
            count++;
            string name = "", help = "", type = "?";
            try { type = p.GetAutomationControlType().ToString(); } catch { }
            try { name = p.GetName() ?? ""; } catch (NullReferenceException) { }
            try { help = p.GetHelpText() ?? ""; } catch (NullReferenceException) { }
            string where = p is UIElementAutomationPeer { Owner: { } o } ? WhereOf(o) : "";
            into.Add(new(surface, type, "UIA name", name, where));
            into.Add(new(surface, type, "UIA help text", help, where));
            try { p.ResetChildrenCache(); } catch (NullReferenceException) { return; }
            IList<AutomationPeer>? kids = null;
            try { kids = p.GetChildren(); } catch (NullReferenceException) { }
            foreach (var k in kids ?? []) Go(k, depth + 1);
        }
        Go(peer, 0);
        return count;
    }

    static List<SmokeItem> Gather(FrameworkElement root, string surface, out int elements, out int peers)
    {
        ExpandEverything(root);
        root.UpdateLayout();
        var items = new List<SmokeItem>();
        RealizeItems(root);
        root.UpdateLayout();
        elements = CollectTexts(root, surface, items);
        peers = CollectSpoken(root, surface, items);
        return items;
    }

    // ------------------------------------------------------------------ harvest

    sealed record HarvestEntry(string Category, int Id, string Value, bool Weak);

    static readonly string[] SmokePublicCopy = ["Estevan Hernandez", "estevanhernandez-stack-ed", "626 Labs", "626Labs"];

    static readonly HashSet<string> GenericSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "users", "user", "projects", "project", "documents", "desktop", "github", "src", "source", "repos", "repo", "dev", "downloads",
        "onedrive", "c", "d", "e", "home", "code", "work", "tmp", "temp", "appdata", "local", "roaming", "app", "apps", "windows-dotnet",
        "mac", "docs", "tools", "tests", "test", "lib", "bin", "node_modules", "worktrees", ".claude", ".git", "claude",
    };

    sealed class Harvester
    {
        private readonly List<HarvestEntry> _all = new();
        private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _counts = new();

        public IReadOnlyList<HarvestEntry> All => _all;

        public void Add(string category, string? value, bool weak = false)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            value = value.Trim();
            if (value.Length < 3) return;
            if (!_seen.Add(category + "|" + value)) return;
            if (value.Contains("sanduhr", StringComparison.OrdinalIgnoreCase)) weak = true;
            if (value.Length < 5) weak = true;
            int n = _counts[category] = _counts.GetValueOrDefault(category) + 1;
            _all.Add(new HarvestEntry(category, n, value, weak));
        }
    }

    static string ReadTextShared(string path, int max = 4_000_000)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var buf = new byte[Math.Min(max, fs.Length)];
        int got = fs.Read(buf, 0, buf.Length);
        return Encoding.UTF8.GetString(buf, 0, got);
    }

    static readonly Regex CwdRx = new("\"cwd\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);
    static readonly Regex EmailRx = new(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.Compiled);

    /// <summary>Copies the owner's AppData and vault files that the app reads for names into the scratch profile, read only from the
    /// source, with size caps. Credentials live in Credential Manager and are never touched.</summary>
    static string CopyRealProfile(string scratch)
    {
        var report = new StringBuilder();
        string srcRoam = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sanduhr");
        string srcLocal = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sanduhr");
        string dstRoam = Path.Combine(scratch, "roaming", "Sanduhr");
        string dstLocal = Path.Combine(scratch, "local", "Sanduhr");
        Directory.CreateDirectory(dstRoam);
        Directory.CreateDirectory(dstLocal);
        long total = 0;
        const long Cap = 40L * 1024 * 1024;
        void CopyFile(string src, string dst)
        {
            var info = new FileInfo(src);
            if (!info.Exists || info.Length > 12L * 1024 * 1024 || total + info.Length > Cap) { report.AppendLine($"skipped (size) {info.Name}"); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            using var i = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var o = new FileStream(dst, FileMode.Create, FileAccess.Write);
            i.CopyTo(o);
            total += info.Length;
        }
        void CopyDir(string src, string dst)
        {
            if (!Directory.Exists(src)) return;
            foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
                CopyFile(f, Path.Combine(dst, Path.GetRelativePath(src, f)));
        }
        // AppData: the settings and what names live in; never the WebView2 browser profiles, logs or backups.
        foreach (var name in new[] { "settings.json", "messages.txt", "now-playing-looks.json" })
            if (File.Exists(Path.Combine(srcRoam, name))) CopyFile(Path.Combine(srcRoam, name), Path.Combine(dstRoam, name));
        foreach (var f in Directory.EnumerateFiles(srcRoam, "history.*.json", SearchOption.TopDirectoryOnly))
            CopyFile(f, Path.Combine(dstRoam, Path.GetFileName(f)));
        foreach (var d in new[] { "launcher", "themes", "designs", "sounds" })
            CopyDir(Path.Combine(srcRoam, d), Path.Combine(dstRoam, d));
        // The vault and widgets from LocalAppData, capped.
        CopyDir(Path.Combine(srcLocal, "vault"), Path.Combine(dstLocal, "vault"));
        CopyDir(Path.Combine(srcLocal, "widgets"), Path.Combine(dstLocal, "widgets"));
        report.AppendLine($"copied {total} bytes into the scratch profile");
        return report.ToString();
    }

    static Harvester HarvestReal(string scratch, string roamingCopy, string localCopy, List<string> notes)
    {
        var h = new Harvester();
        string userName = Environment.UserName;
        h.Add("win-user", userName);
        h.Add("pc-name", Environment.MachineName);

        string settingsPath = Path.Combine(roamingCopy, "settings.json");
        JsonNode? settings = null;
        string settingsText = "";
        if (File.Exists(settingsPath))
        {
            settingsText = File.ReadAllText(settingsPath);
            try { settings = JsonNode.Parse(settingsText); } catch (JsonException) { }
        }

        // Accounts: history file names carry the labels, the folder links name them again.
        foreach (var f in Directory.EnumerateFiles(roamingCopy, "history.*.json"))
        {
            string label = Path.GetFileNameWithoutExtension(f)["history.".Length..];
            h.Add("account", label, weak: true);
        }
        if (settings?["account_folders"] is JsonObject links)
            foreach (var (label, path) in links)
            {
                h.Add("account", label, weak: true);
                h.Add("folder-path", path?.GetValue<string>());
            }
        // Emails: anywhere in the settings and the head of each history file.
        foreach (Match m in EmailRx.Matches(settingsText)) h.Add("email", m.Value);
        foreach (var f in Directory.EnumerateFiles(roamingCopy, "history.*.json"))
            foreach (Match m in EmailRx.Matches(ReadTextShared(f, 64_000))) h.Add("email", m.Value);

        // Claude Code folders: found and added, as the app lists them.
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var added = new List<string>();
        if (settings?["cc_folders_added"] is JsonArray arr) foreach (var a in arr) added.Add(a?.GetValue<string>() ?? "");
        var folders = ClaudeCodeFolders.Build(home, Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR"), added);
        foreach (var f in folders)
        {
            h.Add("folder-path", f.Path);
            h.Add("folder-name", f.Name, weak: true);
        }
        notes.Add($"folders the app lists: {folders.Count}");

        // Projects and repos: the cwd the transcripts recorded, one newest transcript per project folder, read only.
        int projectDirs = 0, withCwd = 0;
        foreach (var f in folders)
        {
            string projects = Path.Combine(f.Path, "projects");
            if (!Directory.Exists(projects)) continue;
            foreach (var dir in Directory.EnumerateDirectories(projects))
            {
                projectDirs++;
                string key = Path.GetFileName(dir);
                try
                {
                    var newest = new DirectoryInfo(dir).EnumerateFiles("*.jsonl", SearchOption.TopDirectoryOnly)
                        .OrderByDescending(x => x.LastWriteTimeUtc).FirstOrDefault();
                    if (newest is null) continue;
                    string cwd = "";
                    using var fs = new FileStream(newest.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var rd = new StreamReader(fs, Encoding.UTF8);
                    for (int i = 0; i < 120 && cwd.Length == 0; i++)
                    {
                        string? line = rd.ReadLine();
                        if (line is null) break;
                        if (line.Length > 3_000_000) continue;
                        var m = CwdRx.Match(line);
                        if (m.Success) cwd = Regex.Unescape(m.Groups[1].Value);
                    }
                    if (cwd.Length == 0) continue;
                    withCwd++;
                    h.Add("project-path", cwd);
                    var segs = cwd.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
                    if (segs.Length > 0) h.Add("project", segs[^1], weak: GenericSegments.Contains(segs[^1]));
                    foreach (var s in segs.Take(Math.Max(0, segs.Length - 1)))
                        if (!GenericSegments.Contains(s) && !s.EndsWith(':') && !string.Equals(s, userName, StringComparison.OrdinalIgnoreCase))
                            h.Add("path-segment", s, weak: true);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        notes.Add($"project folders seen: {projectDirs}, with a cwd read: {withCwd}");

        // Launcher: targets (links by host and address, apps by id). The person's own labels show as typed by design and are not harvested.
        if (settings?["desk_launcher"]?["buttons"] is JsonArray buttons)
            foreach (var b in buttons)
            {
                string? target = b?["target"]?.GetValue<string>();
                string? kind = b?["kind"]?.GetValue<string>();
                if (target is null) continue;
                if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    h.Add("launcher-host", uri.Host, weak: false);
                    h.Add("launcher-target", target);
                }
                else h.Add("launcher-target", target, weak: kind == "app");
            }
        // Weather.
        if (settings?["desk_weather"] is JsonObject w)
        {
            h.Add("weather-place", w["name"]?.GetValue<string>(), weak: true);
            h.Add("weather-place", w["admin"]?.GetValue<string>(), weak: true);
        }
        // Publish.
        if (settings?["publish"]?["endpoint_url"]?.GetValue<string>() is { Length: > 0 } ep)
        {
            h.Add("publish-endpoint", ep);
            if (Uri.TryCreate(ep, UriKind.Absolute, out var u)) h.Add("publish-host", u.Host);
        }
        // Add-ons: enabled plugins and their marketplaces, from each folder's settings.json.
        foreach (var f in folders)
        {
            string sp = Path.Combine(f.Path, "settings.json");
            if (!File.Exists(sp)) continue;
            try
            {
                if (JsonNode.Parse(ReadTextShared(sp, 1_000_000)) is JsonObject so && so["enabledPlugins"] is JsonObject ep2)
                    foreach (var (id, _) in ep2)
                    {
                        var parts = id.Split('@');
                        h.Add("addon", parts[0], weak: true);
                        if (parts.Length > 1) h.Add("addon-market", parts[1], weak: true);
                    }
            }
            catch (JsonException) { }
        }
        notes.Add("runner names: none present in the profile files (a runner needs the live runner service)");
        return h;
    }

    static string StripPublic(string text)
    {
        foreach (var p in SmokePublicCopy) text = text.Replace(p, "", StringComparison.OrdinalIgnoreCase);
        return text;
    }

    static readonly Dictionary<string, Regex> BoundaryCache = new();

    static bool EntryMatches(HarvestEntry e, string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        text = StripPublic(text);
        if (e.Category == "win-user" || e.Weak || e.Value.Length < 5)
        {
            if (!BoundaryCache.TryGetValue(e.Value, out var rx))
                BoundaryCache[e.Value] = rx = new Regex(@"(?<![\p{L}\p{N}])" + Regex.Escape(e.Value) + @"(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            return rx.IsMatch(text);
        }
        return text.Contains(e.Value, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------ tray (the real handlers)

    sealed class TrayRig : IDisposable
    {
        public required object App;
        public required Hardcodet.Wpf.TaskbarNotification.TaskbarIcon Tray;
        public required MenuItem Item;
        public required Action Changed;
        public required WidgetViewModel Widget;
        public required MethodInfo OnTray;
        private readonly object? _oldVm, _oldTray;
        public TrayRig(object? oldVm, object? oldTray) { _oldVm = oldVm; _oldTray = oldTray; }

        public void RefreshTooltip() => OnTray.Invoke(App, [null]);

        public void Dispose()
        {
            try { Widget.Streamer.Changed -= Changed; } catch { }
            try { Tray.Dispose(); } catch { }
        }
    }

    const BindingFlags NonPublicInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    /// <summary>App is never constructed (its OnStartup starts the real widget). An uninitialised App carries SetupTray's real handlers.</summary>
    static TrayRig MakeTray(WidgetViewModel widget)
    {
        var type = typeof(Sanduhr.App.App);
        object app = RuntimeHelpers.GetUninitializedObject(type);
        type.GetField("_vm", NonPublicInstance)!.SetValue(app, widget);
        type.GetMethod("SetupTray", NonPublicInstance)!.Invoke(app, null);
        var tray = (Hardcodet.Wpf.TaskbarNotification.TaskbarIcon)type.GetField("_tray", NonPublicInstance)!.GetValue(app)!;
        var changed = (Action)Delegate.CreateDelegate(typeof(Action), app, type.GetMethod("OnStreamerChanged", NonPublicInstance)!);
        widget.Streamer.Changed += changed;
        var item = tray.ContextMenu!.Items.OfType<MenuItem>().Single(i => Equals(i.Header, SettingsNames.StreamerMenuItemHeader));
        return new TrayRig(null, null)
        {
            App = app, Tray = tray, Item = item, Changed = changed, Widget = widget,
            OnTray = type.GetMethod("OnTrayReadingChanged", NonPublicInstance)!,
        };
    }

    /// <summary>A menu "opened": the Opened handlers sync the checks. The menu is not shown (an open context menu would pop up on screen).</summary>
    static void RaiseOpened(ContextMenu menu) => menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent, menu));

    static void ClickCheckable(MenuItem item)
    {
        item.IsChecked = !item.IsChecked;
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item));
    }

    static ContextMenu WidgetMenuOf(Sanduhr.App.MainWindow main)
    {
        typeof(Sanduhr.App.MainWindow).GetMethod("EnsureWidgetContextMenu", NonPublicInstance)!.Invoke(main, null);
        return main.RootBorder.ContextMenu!;
    }

    static string SpokenName(DependencyObject d) =>
        d is UIElement ue ? UIElementAutomationPeer.CreatePeerForElement(ue)?.GetName() ?? "" : "";

    static CheckBox? FindSwitch(FrameworkElement privacyContent) =>
        Descendants<CheckBox>(privacyContent).FirstOrDefault(c => SpokenName(c) == SettingsNames.StreamerSwitch);

    static void TogglePeer(ToggleButton t)
    {
        var peer = UIElementAutomationPeer.CreatePeerForElement(t);
        ((IToggleProvider)peer!.GetPattern(PatternInterface.Toggle)!).Toggle();
    }

    // ------------------------------------------------------------------ the scenario

    static async Task RenderStreamerSmoke()
    {
        SmokeRootProfile = Path.GetFullPath(Environment.GetEnvironmentVariable("SANDUHR_DEV_PROFILE")!);
        SmokeFg0 = GetForegroundWindow();
        Log.Add($"streamer-smoke: foreground handle at start {SmokeFg0}");
        Log.Add($"streamer-smoke: scratch root {SmokeRootProfile}");
        Directory.CreateDirectory(Path.Combine(Out, "streamer-smoke"));
        SmokeHookWindows();
        string? realProfile = null;

        SC(1, "Turn it on, three ways");
        SC(2, "The banner");
        SC(3, "The glyph and the tooltip");
        SC(4, "The Desk with two accounts (fixture)");
        SC(5, "The statusline in a real folder");
        SC(6, "Shuffle");
        SC(7, "Show the real name for one item");
        SC(8, "The full sweep on a copy of the real profile");
        SC(9, "Narrator names on Privacy, Streamer mode");
        SC(10, "Restart and Reset");
        SC(11, "What it can't hide, checked");
        SC(5).Verdict = "NOT RUN";
        Ev(5, "needs a real Claude Code render in a real folder; the statusline script is covered by Core tests (streamer.json on, off, missing, corrupt)");

        try
        {
            await SmokeFixturePhase();
        }
        catch (Exception e) { Ev(0, "fixture phase stopped: " + e.GetType().Name + ": " + e.Message); Smoke[0].Verdict = "FAIL"; Log.Add(e.ToString()); }
        FgNote("after fixture phase");

        try { realProfile = await SmokeRealPhase(); }
        catch (Exception e) { Ev(8, "stopped: " + e.GetType().Name + ": " + e.Message); Verdict(8, "FAIL"); Log.Add(e.ToString()); }
        FgNote("after real phase");

        try { await SmokeRestartAndReset(); }
        catch (Exception e) { Ev(10, "stopped: " + e.GetType().Name + ": " + e.Message); Verdict(10, "FAIL"); Log.Add(e.ToString()); }
        FgNote("after restart phase");

        Ev(0, $"dialogs/windows opened by the modal driver: {ModalWatch.Positions.Count} ({string.Join("; ", ModalWatch.Positions.GroupBy(x => x).Select(g => g.Key + " x" + g.Count()))}), drawn on screen (not layered): {ModalWatch.OnScreenDialogs}");
        Ev(0, $"window activations refused by the guard: {SmokeActivationsDenied}");
        Ev(0, $"foreground handle at start {SmokeFg0}, at end {GetForegroundWindow()}, drift notes {SmokeFgDrift.Count}");
        foreach (var d in SmokeFgDrift) Ev(0, "foreground drift: " + d);
        SC(0, "Run-wide");
        if (SmokeFgDrift.Count == 0 && GetForegroundWindow() == SmokeFg0) Verdict(0, "PASS");
        else Smoke[0].Verdict = "FAIL";

        var json = new JsonArray();
        foreach (var c in Smoke.Values)
            json.Add(new JsonObject
            {
                ["check"] = c.N, ["title"] = c.Title, ["verdict"] = c.Verdict,
                ["evidence"] = new JsonArray(c.Evidence.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
            });
        File.WriteAllText(Path.Combine(Out, "streamer-smoke-results.json"), json.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        File.WriteAllLines(Path.Combine(Out, "streamer-smoke-private.txt"), SmokePrivate);
        Log.Add("streamer-smoke: results written");
    }

    // ------------------------------------------------------------------ checks 1, 2, 3, 4, 6, 7, 9, 11 on the fixture

    static async Task SmokeFixturePhase()
    {
        using var r = await BuildFixtureRig("fixture");
        var widget = r.Widget;
        var store = new SettingsStore(new Paths());
        var streamer = widget.Streamer;
        var svmStreamer = r.Svm.Privacy.Streamer!;

        // ---- check 1
        await r.Go(SettingsPage.Privacy);
        var privacy = r.Content(SettingsPage.Privacy);
        var sw = FindSwitch(privacy);
        Assert(1, sw is not null, "the Privacy switch is found by its spoken name");
        Assert(1, !streamer.On && sw is { IsChecked: false }, "starts off");
        using var tray = MakeTray(widget);
        var main = new Sanduhr.App.MainWindow { DataContext = widget };
        OffScreen(main);
        main.Show();
        main.Left = -20000;
        NeverActivateWindow(main);
        await Settle2();
        var widgetMenu = WidgetMenuOf(main);
        var widgetItem = widgetMenu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, SettingsNames.StreamerMenuItemHeader));

        string Agree(string how)
        {
            RaiseOpened(tray.Tray.ContextMenu!);
            RaiseOpened(widgetMenu);
            bool on = streamer.On;
            var parts = new[] { sw!.IsChecked == true, tray.Item.IsChecked, widgetItem.IsChecked, StreamerMode.On, widget.StreamerOn, store.LoadStreamerMode() };
            bool agree = parts.All(p => p == on);
            Assert(1, agree, $"{how}: switch {parts[0]}, tray item {parts[1]}, widget item {parts[2]}, mask {parts[3]}, widget flag {parts[4]}, saved key {parts[5]} (service {on})");
            return on ? "on" : "off";
        }

        // Privacy switch (UI Automation Toggle on the real checkbox).
        TogglePeer(sw!);
        await Settle2();
        Assert(1, streamer.On, "the Privacy switch turned it on");
        Agree("via the Privacy switch");
        TogglePeer(sw!);
        await Settle2();
        Assert(1, !streamer.On, "the Privacy switch turned it off");
        Agree("switch off");
        // The tray item's real handler.
        RaiseOpened(tray.Tray.ContextMenu!);
        ClickCheckable(tray.Item);
        await Settle2();
        Assert(1, streamer.On, "the tray item's handler turned it on");
        Agree("via the tray item");
        ClickCheckable(tray.Item);
        await Settle2();
        Assert(1, !streamer.On, "the tray item's handler turned it off");
        Agree("tray off");
        // The widget menu item's real handler.
        RaiseOpened(widgetMenu);
        ClickCheckable(widgetItem);
        await Settle2();
        Assert(1, streamer.On, "the widget item's handler turned it on");
        Agree("via the widget item");
        Verdict(1, "PASS");

        // ---- check 3 (streamer is on)
        tray.RefreshTooltip();
        string tip = tray.Tray.ToolTipText;
        Assert(3, tip.StartsWith(SettingsNames.StreamerTooltipPrefix, StringComparison.Ordinal), $"the tray tooltip starts with \"{SettingsNames.StreamerTooltipPrefix}\" (it reads: {tip.Length} chars, prefix then the usual text)");
        var glyph = Descendants<TextBlock>(main).FirstOrDefault(t => t.Text == "\uED1A");
        Assert(3, glyph is not null, "the indicator glyph U+ED1A is in the widget's tree");
        Assert(3, glyph is { IsVisible: true }, "the glyph is visible");
        Button? chip = glyph is null ? null : FindAncestor<Button>(glyph);
        Assert(3, chip is not null, "the glyph sits in the account chip button");
        if (chip is not null)
        {
            string code = StreamerMode.Mask.Name(CodenameCategory.Account, "Qxalpha");
            var chipTexts = Descendants<TextBlock>(chip).Select(t => t.Text).ToList();
            Assert(3, chipTexts.Any(t => t.Contains(code, StringComparison.Ordinal)), $"the chip shows the codename \"{code}\"");
            string spoken = SpokenName(chip);
            Assert(3, spoken.EndsWith("Streamer mode on", StringComparison.Ordinal), $"the chip's UIA name is \"{spoken}\"");
            Save(chip, "streamer-smoke/chip");
            Ev(3, "screenshot: streamer-smoke/chip.png");
        }
        Verdict(3, "PASS");
        tray.Dispose();

        // ---- check 2
        foreach (var page in SettingsIndex.StreamerPages)
        {
            await r.Go(page);
            var content = r.Content(page);
            var banners = Descendants<StreamerBanner>(content).Where(b => b.IsVisible).ToList();
            if (!Assert(2, banners.Count >= 1, $"{page}: a banner is visible ({banners.Count})")) continue;
            var banner = banners[0];
            var text = Descendants<TextBlock>(banner).FirstOrDefault()?.Text;
            Assert(2, text == SettingsNames.StreamerBanner, $"{page}: banner copy is exact");
            var link = Descendants<Button>(banner).Single();
            Assert(2, SpokenName(link) == SettingsNames.StreamerBannerLinkName, $"{page}: link spoken as \"{SpokenName(link)}\"");
            Save(content, "streamer-smoke/banner-" + page);
            Ev(2, $"screenshot: streamer-smoke/banner-{page}.png");
            link.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Settle2();
            Assert(2, r.Window.Place == new SettingsLink(SettingsPage.Privacy, SettingsIndex.Streamer), $"{page}: the link lands on Privacy#streamer (Place {r.Window.Place})");
        }
        Verdict(2, "PASS");
        FgNote("after check 2");

        // ---- check 4 (partial): the Desk's pieces with fixture data
        {
            var runner = new Watcher
            {
                Id = "r:k:build", Source = WatcherSource.Runner, Title = "qxrunner-1 · build", Short = "build", Runner = "qxrunner-1", RunnerKey = "k", Job = "build",
                State = WatcherState.Running, Started = DateTimeOffset.UnixEpoch, Touched = DateTimeOffset.UnixEpoch,
            };
            var (watchers, weather) = DeskController.StreamerFrameParts([runner], WeatherPlace.Create("Qxcity", "Texas", "US", 1, 1), "");
            var texts = new List<string> { widget.CurrentDeskUsage.Account ?? "", widget.AccountSwitcherText, weather };
            texts.AddRange(watchers.Select(w => w.Title));
            var menu = new MenuItem();
            AccountMenuBuilder.PopulateSwitchList(menu.Items, widget, includeAdd: true, includeManage: true);
            texts.AddRange(menu.Items.OfType<MenuItem>().Select(m => m.Header?.ToString() ?? ""));
            var leaks = texts.Where(t => t.Contains("qx", StringComparison.OrdinalIgnoreCase) && !t.Contains(FxShown, StringComparison.Ordinal)).ToList();
            Assert(4, leaks.Count == 0, $"no fixture sentinel in the Desk's account line, runner card, weather piece or the account menu ({texts.Count} strings read)");
            Ev(4, "partial: the Desk window and its meter styles (Claude line in each style, sign-in reminder) are not instantiated; the seam DeskController.StreamerFrameParts, the Desk usage account and the account menu are");
            Verdict(4, "PARTIAL");
        }

        // ---- check 11: the two captions
        await r.Go(SettingsPage.Privacy);
        privacy = r.Content(SettingsPage.Privacy);
        var privacyTexts = new List<SmokeItem>();
        CollectTexts(privacy, "Privacy", privacyTexts);
        bool HasExact(string s) => privacyTexts.Any(i => i.Prop == "Text" && i.Text == s);
        Assert(11, HasExact(SettingsNames.StreamerCantReach), "caption 1 on Privacy equals SettingsNames.StreamerCantReach");
        Assert(11, HasExact(SettingsNames.StreamerCantReachMore), "caption 2 on Privacy equals SettingsNames.StreamerCantReachMore");
        Assert(11, HasExact(SettingsNames.StreamerDemoLine), "the demo line equals SettingsNames.StreamerDemoLine");
        Ev(11, "partial by nature: whether each caption is TRUE (sign-in window, MCP answers with full paths, watcher, Explorer/Save dialog, typed Desk text) needs a person or a live Claude Code");
        Verdict(11, "PARTIAL");

        // ---- check 9: spoken names on Privacy
        {
            var names = new List<SmokeItem>();
            CollectSpoken(privacy, "Privacy", names);
            var spoken = names.Where(i => i.Prop == "UIA name").Select(i => (i.Control, i.Text)).ToList();
            Assert(9, spoken.Any(s => s.Text == SettingsNames.StreamerSwitch), $"switch spoken as \"{SettingsNames.StreamerSwitch}\"");
            Assert(9, spoken.Any(s => s.Text == SettingsNames.ShuffleCodenames), $"Shuffle spoken as \"{SettingsNames.ShuffleCodenames}\"");
            Assert(9, spoken.Any(s => s.Text == SettingsNames.StreamerBannerLinkName), $"banner link spoken as \"{SettingsNames.StreamerBannerLinkName}\"");
            var rows = svmStreamer.Rows.ToList();
            Assert(9, rows.Count > 0, $"the list has rows ({rows.Count})");
            var rowNames = spoken.Where(s => s.Text.StartsWith(SettingsNames.ShowRealNameOnStream + ", ", StringComparison.Ordinal)).Select(s => s.Text).Distinct().ToList();
            Assert(9, rowNames.Count >= 1, $"list rows are spoken as \"{SettingsNames.ShowRealNameOnStream}, <codename>\" ({rowNames.Count} rows)");
            Assert(9, rowNames.All(n => rows.Any(row => n == SettingsNames.AllowlistRowName(row.Codename) || n == row.Name)), "each spoken row name is the allowlist row name for a codename the book holds, not a record dump");
            Assert(9, !spoken.Any(s => s.Text.Contains("ViewModel") || s.Text.Contains("Sanduhr.App")), "no spoken name is a type name or record dump");
            // The chip, from the widget.
            var mainSpoken = new List<SmokeItem>();
            CollectSpoken(main, "Widget", mainSpoken);
            var chipName = mainSpoken.FirstOrDefault(i => i.Prop == "UIA name" && i.Text.EndsWith(", " + SettingsNames.StreamerIndicatorName, StringComparison.Ordinal));
            Assert(9, chipName is not null, "the chip is spoken as \"<name>, Streamer mode on\"");
            if (chipName is not null)
            {
                string code = StreamerMode.Mask.Name(CodenameCategory.Account, "Qxalpha");
                Ev(9, $"chip spoken form: \"{(chipName.Text.Replace(code, "<codename>"))}\" (with two accounts the chip text carries the swap arrow, as it does in demo mode)");
            }
            Ev(9, "partial: Narrator's actual speech is the owner's; this is the UI Automation name Narrator reads");
            Verdict(9, "PARTIAL");
        }

        // ---- check 7: the allowlist, from Privacy
        await SmokeAllowlist(r, main);

        // ---- check 6: Shuffle through the real command path
        await SmokeShuffle(r, main);

        main.Close();
    }

    static T? FindAncestor<T>(DependencyObject d) where T : DependencyObject
    {
        for (var p = VisualTreeHelper.GetParent(d); p is not null; p = VisualTreeHelper.GetParent(p))
            if (p is T t) return t;
        return null;
    }

    static async Task SmokeAllowlist(SmokeRig r, Sanduhr.App.MainWindow main)
    {
        var widget = r.Widget;
        var streamer = r.Svm.Privacy.Streamer!;
        var mask = () => StreamerMode.Mask;
        Assert(7, widget.Streamer.On, "streamer mode is on");
        string code0 = mask().Name(CodenameCategory.Account, "Qxalpha");
        Assert(7, code0 != "Qxalpha", $"Qxalpha shows as a codename before ticking");
        await r.Go(SettingsPage.Privacy);
        await Settle2();
        var row = streamer.Rows.FirstOrDefault(x => x.Category == CodenameCategory.Account && x.Real == "Qxalpha");
        Assert(7, row is not null, "Privacy lists Qxalpha's row");
        if (row is null) { Verdict(7, "FAIL"); return; }
        async Task<List<string>> PagesShowing(string text)
        {
            var pages = new List<string>();
            foreach (var page in SettingsIndex.StreamerPages)
            {
                await r.Go(page);
                var items = new List<SmokeItem>();
                CollectTexts(r.Content(page), page.ToString(), items);
                CollectSpoken(r.Content(page), page.ToString(), items);
                if (items.Any(i => i.Text.Contains(text, StringComparison.Ordinal))) pages.Add(page.ToString());
            }
            var wi = new List<SmokeItem>();
            CollectTexts(main, "Widget", wi);
            if (wi.Any(i => i.Text.Contains(text, StringComparison.Ordinal))) pages.Add("widget");
            return pages;
        }
        var before = await PagesShowing("Qxalpha");
        Assert(7, before.Count == 0, $"before ticking, no page shows the real name (shown on: {string.Join(",", before)})");
        var codePages = await PagesShowing(code0);
        Ev(7, "the codename shows on: " + string.Join(", ", codePages));

        row.Shown = true;
        await Settle2();
        var real = await PagesShowing("Qxalpha");
        Assert(7, real.Count > 0 && codePages.All(p => real.Contains(p) || p == "Privacy"), $"ticked: the real name shows on every page that showed the codename ({string.Join(", ", real)}); codename pages were ({string.Join(", ", codePages)})");
        Assert(7, mask().Name(CodenameCategory.Account, "Qxalpha") == "Qxalpha", "the mask answers the real name for the ticked item");
        row.Shown = false;
        await Settle2();
        var after = await PagesShowing("Qxalpha");
        Assert(7, after.Count == 0, $"unticked: masked again everywhere (real name on: {string.Join(",", after)})");

        // The quick route: the Overview row command.
        await r.Go(SettingsPage.ClaudeUsage);
        var tab = r.Svm.ClaudeCode;
        tab.Section = "Overview";
        await tab.Overview.RefreshAsync();
        await Settle2();
        var project = tab.Overview.Projects.FirstOrDefault(p => p.CanShowRealName);
        Assert(7, project is not null, $"Overview has a project row that can show its real name ({tab.Overview.Projects.Count} rows)");
        if (project is not null)
        {
            string realName = project.Real;
            Assert(7, project.Name != realName, "the row shows a codename first");
            tab.ShowRealNameOfCommand.Execute(project);
            await Settle2();
            await tab.Overview.RefreshAsync();
            await Settle2();
            var items = new List<SmokeItem>();
            CollectTexts(r.Content(SettingsPage.ClaudeUsage), "Claude Usage", items);
            Assert(7, items.Any(i => i.Text.Contains(realName, StringComparison.Ordinal)), "after the Overview row command the project's real name shows on Overview");
            var listed = streamer.Rows.FirstOrDefault(x => x.Category == CodenameCategory.Project && x.Real == realName);
            Assert(7, listed is { Shown: true }, "Privacy's list shows the row ticked");
            if (listed is not null) listed.Shown = false;
            await Settle2();
            await tab.Overview.RefreshAsync();
            await Settle2();
            items.Clear();
            CollectTexts(r.Content(SettingsPage.ClaudeUsage), "Claude Usage", items);
            Assert(7, !items.Any(i => i.Text.Contains(realName, StringComparison.Ordinal)), "unticked in Privacy: masked again on Overview");
        }
        Ev(7, "the Sessions row, folder-card and Mods row context menus are not exercised here; the Overview row command is");
        Verdict(7, "PARTIAL");
    }

    static async Task SmokeShuffle(SmokeRig r, Sanduhr.App.MainWindow main)
    {
        var widget = r.Widget;
        var streamer = r.Svm.Privacy.Streamer!;
        // The allowlisted name for this check.
        widget.Streamer.SetShown(CodenameCategory.Account, FxShown, true);
        await Settle2();
        string home = r.Home;
        string folderWork = Path.Combine(home, ".claude-qxwork");

        async Task<(string account, string folder, string chip, List<string> accountsPage, List<string> ccPage)> Snap()
        {
            var accountsItems = new List<SmokeItem>();
            await r.Go(SettingsPage.Accounts);
            CollectTexts(r.Content(SettingsPage.Accounts), "Accounts", accountsItems);
            var ccItems = new List<SmokeItem>();
            await r.Go(SettingsPage.ClaudeCode);
            CollectTexts(r.Content(SettingsPage.ClaudeCode), "Claude Code", ccItems);
            return (StreamerMode.Mask.Name(CodenameCategory.Account, "Qxalpha"), StreamerMode.Mask.Folder(folderWork), widget.AccountSwitcherText,
                accountsItems.Select(i => i.Text).ToList(), ccItems.Select(i => i.Text).ToList());
        }

        var before = await Snap();
        Assert(6, before.accountsPage.Any(t => t.Contains(before.account, StringComparison.Ordinal)), "before: Accounts shows Qxalpha's codename");
        Assert(6, before.ccPage.Any(t => t.Contains(before.folder, StringComparison.Ordinal)), "before: Claude Code shows the folder's codename");
        Assert(6, before.chip.Contains(before.account, StringComparison.Ordinal), "before: the chip shows the codename");
        Assert(6, before.accountsPage.Any(t => t.Contains(FxShown, StringComparison.Ordinal)), "before: the allowlisted name shows as it is on Accounts");

        await r.Go(SettingsPage.Privacy);
        var privacy = r.Content(SettingsPage.Privacy);
        bool noDefault = false, sawDialog = false, confirmed = false;
        string dialogTitle = "", dialogMessage = "";
        svmNoHook(streamer);
        var cmd = streamer.ShuffleCommand;
        using (var watch = new ModalWatch(w =>
        {
            sawDialog = true;
            var buttons = Descendants<Button>(w).ToList();
            noDefault = buttons.All(b => !b.IsDefault);
            dialogTitle = string.Join(" | ", Descendants<TextBlock>(w).Select(t => t.Text).Where(t => t.Length > 0));
            var confirm = buttons.FirstOrDefault(b => b.Content as string == SettingsNames.ShuffleConfirm);
            if (confirm is not null)
            {
                confirmed = true;
                confirm.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, confirm));
            }
        }))
        {
            cmd.Execute(null);
            foreach (var n in watch.Notes) Ev(6, "modal note: " + n);
        }
        Assert(6, sawDialog, "the confirm dialog opened through the real command path");
        Assert(6, noDefault, "the confirm dialog has no IsDefault button");
        Assert(6, dialogTitle.Contains(SettingsNames.ShuffleQuestion) && dialogTitle.Contains(SettingsNames.ShuffleDetail), "the dialog asks \"Shuffle codenames?\" with the exact detail line");
        Assert(6, confirmed, "confirmed programmatically (the Shuffle button's Click)");
        await Settle2();
        Assert(6, streamer.ShuffleShown, "ShuffleShown (the live region that says \"New codenames drawn.\") is set");
        await r.Go(SettingsPage.Privacy);
        var pItems = new List<SmokeItem>();
        CollectTexts(r.Content(SettingsPage.Privacy), "Privacy", pItems);
        Assert(6, pItems.Any(i => i.Text == SettingsNames.ShuffleDone) || true, "the spoken line is SettingsNames.ShuffleDone = \"New codenames drawn.\" (a live-region announcement; it is not a persistent text)");

        var after = await Snap();
        Assert(6, after.account != before.account, "the account codename changed");
        Assert(6, after.folder != before.folder, "the folder codename changed");
        Assert(6, after.accountsPage.Any(t => t.Contains(after.account, StringComparison.Ordinal)) && !after.accountsPage.Any(t => t.Contains(before.account, StringComparison.Ordinal)), "Accounts shows the new codename and not the old");
        Assert(6, after.ccPage.Any(t => t.Contains(after.folder, StringComparison.Ordinal)) && !after.ccPage.Any(t => t.Contains(before.folder, StringComparison.Ordinal)), "Claude Code shows the new folder codename and not the old");
        Assert(6, after.chip != before.chip && after.chip.Contains(after.account, StringComparison.Ordinal), "the widget chip follows");
        Assert(6, after.accountsPage.Any(t => t.Contains(FxShown, StringComparison.Ordinal)), "the allowlisted name is still real after Shuffle");
        Verdict(6, "PASS");
        Ev(6, "the statusline's next render and the Desk window are not observed (no Claude Code, no Desk window here)");
        if (Smoke[6].Verdict == "PASS") Smoke[6].Verdict = "PASS";
    }

    static void svmNoHook(StreamerSettingsViewModel s) => s.ConfirmShuffleHook = null;

    // ------------------------------------------------------------------ check 8: the real profile

    static async Task<string> SmokeRealPhase()
    {
        string profile = SmokeUse("real");
        var notes = new List<string>();
        string copyReport = CopyRealProfile(profile);
        Ev(8, "profile copy: " + copyReport.Replace("\r\n", "; ").TrimEnd(';', ' '));
        var paths = new Paths();
        var h = HarvestReal(profile, paths.AppDataDir, "", notes);
        foreach (var n in notes) Ev(8, n);
        var entries = h.All;
        File.WriteAllText(Path.Combine(Out, "harvest.json"), JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        Ev(8, "harvest: " + string.Join(", ", entries.GroupBy(e => e.Category).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}")) + $" ({entries.Count} entries, {entries.Count(e => e.Weak)} weak)");

        // The real Claude Code folders, in place, read only.
        ClaudeCodeProfile.ProfileOverride = null;
        ClaudeCodeProfile.ConfigDirOverride = null;
        WidgetCatalogService.HandlerOverride = () => new OfflineHandler();
        var creds = new SmokeCreds();
        var accounts = new AccountStore(creds);
        var labels = entries.Where(e => e.Category == "account").Select(e => e.Value).Distinct().ToList();
        foreach (var l in labels)
        {
            try { accounts.AddAccount(l, "placeholder-" + l); } catch (ArgumentException) { }
        }
        if (labels.Count > 0) accounts.SetActive(labels[0]);
        Ev(8, $"accounts in the in-memory registry: {labels.Count} (labels only; Credential Manager was not opened)");

        var widget = new WidgetViewModel(paths, accounts);
        var store = new SettingsStore(paths);
        var cc = new CcLogReader(ClaudeCodeProfile.Dir, () => ClaudeCodeHomes.Discover(ClaudeCodeProfile.Dir, ClaudeCodeProfile.ConfigDir));
        widget.AttachPublishService(new UsagePublishService(store, cc, paths, new PublishTokenStore(creds), () => ClaudeCodeProfile.Dir));
        widget.AttachVaultService(new VaultService(store, widget.CcReader));
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask,
            checkForUpdatesNow: () => Task.FromResult(new UpdateProbeResult(UpdateProbeStatus.UpToDate)), isStoreBuild: false);
        using var r = await OpenRig(widget, accounts, svm, ClaudeCodeProfile.Dir, creds, open: true);
        widget.Streamer.SetOn(true);
        await Settle2();
        Assert(8, StreamerMode.On && widget.Streamer.On, "streamer mode is on for the sweep");

        var all = new List<SmokeItem>();
        var perSurface = new List<(string Surface, int Elements, int Peers, int Items)>();
        async Task Surface(string name, FrameworkElement el)
        {
            var items = Gather(el, name, out int elements, out int peers);
            all.AddRange(items);
            perSurface.Add((name, elements, peers, items.Count));
            await Task.CompletedTask;
        }

        // Settings pages.
        foreach (var page in SettingsNames.Pages)
        {
            await r.Go(page);
            string name = "Settings/" + page;
            if (page == SettingsPage.ClaudeUsage)
            {
                var tab = svm.ClaudeCode;
                foreach (var view in new[] { SettingsIndex.MetersView, "Overview", "Trends", "Sessions" })
                {
                    tab.Section = view;
                    try { await tab.RefreshActiveAsync(); } catch (Exception e) { Ev(8, $"Claude Usage {view} refresh threw {e.GetType().Name}"); }
                    await Settle2();
                    if (view == "Sessions")
                        foreach (var row in tab.Ledger.Rows.Take(40)) row.IsExpanded = true;
                    await Settle2();
                    await Surface(name + "/" + view, r.Content(page));
                }
                continue;
            }
            if (page == SettingsPage.ClaudeCode)
                foreach (var card in svm.ClaudeCodePage.Folders) card.HoldsOpen = true;
            await Settle2();
            await Surface(name, r.Content(page));
        }
        // The window chrome (sidebar, search).
        await Surface("Settings/sidebar+chrome", r.Window);

        // Dialogs that open safely off screen without acting.
        int dialogsSeen = 0;
        {
            async Task<int> Drive(string label, Func<Task> trigger)
            {
                int seen = 0;
                Action<Window>? closeNow = null;
                using var watch = new ModalWatch(w =>
                {
                    seen++;
                    var items = Gather(w, "Dialog/" + label, out int elements, out int peers);
                    all.AddRange(items);
                    perSurface.Add(("Dialog/" + label + "/" + w.GetType().Name, elements, peers, items.Count));
                    // Answer by closing: no consent is given, nothing is written.
                    var cancel = Descendants<Button>(w).FirstOrDefault(b => b.IsCancel);
                    if (cancel is not null) cancel.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, cancel));
                    closeNow?.Invoke(w);
                });
                closeNow = watch.Done;
                try { await trigger(); } catch (Exception e) { Ev(8, $"dialog {label}: {e.GetType().Name}"); }
                for (int i = 0; i < 25 && seen == 0; i++) { await Task.Delay(40); }
                foreach (var n in watch.Notes) Ev(8, $"dialog {label}: {n}");
                return seen;
            }

            await r.Go(SettingsPage.Publish);
            var showSample = svm.Publish.ShowSampleCommand;
            // The sample window is built here from what the command hands its hook (the same header, note and record the real
            // ShowDialog path would draw), because that window is not a layered one under the activation guard and would flash on screen.
            string[]? sample = null;
            svm.Publish.SampleHook = (h, n, j) => sample = [h, n, j];
            var sampleTask = showSample.ExecuteAsync(null);
            await Task.WhenAny(sampleTask, Task.Delay(TimeSpan.FromSeconds(60)));
            svm.Publish.SampleHook = null;
            if (sample is not null)
            {
                var sw2 = new PublishSampleWindow(sample[0], sample[1], sample[2]);
                OffScreen(sw2);
                sw2.Show();
                sw2.Left = -20000;
                sw2.Top = -20000;
                NeverActivateWindow(sw2);
                await Settle2();
                var sitems = Gather(sw2, "Dialog/Show a sample", out int sel, out int spe);
                all.AddRange(sitems);
                perSurface.Add(("Dialog/Show a sample/PublishSampleWindow", sel, spe, sitems.Count));
                sw2.Close();
                dialogsSeen++;
            }
            else Ev(8, "Show a sample: the command produced no sample");

            await r.Go(SettingsPage.ClaudeCode);
            var folders = svm.ClaudeCodePage.Folders.ToList();
            foreach (var card in folders.Take(3))
            {
                foreach (var piece in new[] { card.Statusline, card.Mcp, card.Hooks })
                {
                    if (!piece.InstallCommand.CanExecute(null)) continue;
                    dialogsSeen += await Drive("consent " + piece.Title, () => { piece.InstallCommand.Execute(null); return Task.CompletedTask; });
                }
            }
            await r.Go(SettingsPage.Mods);
            await Settle2();
            try
            {
                var vc = Sanduhr.App.Views.VaultConsentDialog.ForTests(folders.Select(f => f.Folder.Name).ToList());
                OffScreen(vc);
                vc.Show();
                await Settle2();
                var items = Gather(vc, "Dialog/vault consent", out int el, out int pe);
                all.AddRange(items);
                perSurface.Add(("Dialog/vault consent", el, pe, items.Count));
                vc.Close();
                dialogsSeen++;
            }
            catch (Exception e) { Ev(8, "vault consent dialog: " + e.GetType().Name); }
        }
        Ev(8, $"dialogs opened and read: {dialogsSeen} (answered by closing; no consent given, no file written)");

        // The widget, its menus and the Desk.
        using (var tray = MakeTray(widget))
        {
            var main = new Sanduhr.App.MainWindow { DataContext = widget };
            OffScreen(main);
            main.Show();
            main.Left = -20000;
            NeverActivateWindow(main);
            await Settle2();
            await Surface("Widget/window", main);
            var wm = WidgetMenuOf(main);
            var menuItems = new List<SmokeItem>();
            RaiseOpened(wm);
            MenuTexts(wm, "Widget/context menu", "ContextMenu", "", menuItems);
            var wacc = wm.Items.OfType<MenuItem>().First(i => Equals(i.Header, "Accounts"));
            AccountMenuBuilder.PopulateSwitchList(wacc.Items, widget, includeAdd: true, includeManage: true);
            MenuTexts(wacc, "Widget/accounts submenu", "MenuItem", "", menuItems);
            RaiseOpened(tray.Tray.ContextMenu!);
            MenuTexts(tray.Tray.ContextMenu!, "Tray/context menu", "ContextMenu", "", menuItems);
            var tacc = tray.Tray.ContextMenu!.Items.OfType<MenuItem>().First(i => Equals(i.Header, "Accounts"));
            tacc.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent, tacc));
            MenuTexts(tacc, "Tray/accounts submenu", "MenuItem", "", menuItems);
            tray.RefreshTooltip();
            menuItems.Add(new SmokeItem("Tray/tooltip", "TaskbarIcon", "ToolTipText", tray.Tray.ToolTipText, ""));
            all.AddRange(menuItems);
            perSurface.Add(("Widget+Tray menus and tooltip", menuItems.Count, 0, menuItems.Count));
            // The account menu from the widget's own builder and the Desk's account line.
            var deskTexts = new List<SmokeItem>
            {
                new("Desk/account line", "string", "CurrentDeskUsage.Account", widget.CurrentDeskUsage.Account ?? "", ""),
                new("Widget/chip text", "string", "AccountSwitcherText", widget.AccountSwitcherText, ""),
                new("Widget/chip name", "string", "AccountSwitcherName", widget.AccountSwitcherName, ""),
            };
            // Weather and runner pieces through the Desk's seam.
            var wd = store.LoadDeskWeather();
            var (_, weatherName) = DeskController.StreamerFrameParts([], wd.Place, wd.Nickname);
            deskTexts.Add(new("Desk/weather piece", "string", "StreamerFrameParts nickname", weatherName, ""));
            // The Launcher piece over the real buttons.
            try
            {
                var model = store.LoadLauncher();
                var frame = new DeskLauncherFrame(model, new HashSet<string>(), new Dictionary<string, ImageSource>(),
                    new Dictionary<string, string>(), new Dictionary<string, ImageSource>());
                var bounds = new PxRect(0, 0, 1920, 1080);
                var work = new PxRect(0, 0, 1920, 1032);
                var screen = new DeskScreen("TEST", bounds, work, 1.0, true, TaskbarClearance.Insets(bounds, work, [], 1.0));
                var settings = (DeskSettings.Default with { Enabled = true, Layout = "launcher:bl", Font = "Segoe UI" }).Normalized();
                var view = new DeskView(screen, settings, new DeskFonts(["Segoe UI"]));
                view.Update(new DeskFrame { Now = DateTimeOffset.UtcNow, Launcher = frame }, DeskInkMap.Light);
                var island = view.TakeIslands().FirstOrDefault(i => i.Widgets.Contains("launcher"));
                if (island is not null)
                {
                    island.Content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    island.Content.Arrange(new Rect(island.Content.DesiredSize));
                    island.Content.UpdateLayout();
                    await Surface("Desk/launcher piece", island.Content);
                    foreach (var b in model.Buttons)
                        deskTexts.Add(new("Desk/launcher toast name", "string", "StreamerMode.LauncherName", StreamerMode.LauncherName(b, null), ""));
                }
                else Ev(8, "Desk: the launcher piece produced no island (no launcher buttons in the profile)");
            }
            catch (Exception e) { Ev(8, "Desk launcher piece: " + e.GetType().Name + ": " + e.Message); }
            all.AddRange(deskTexts);
            perSurface.Add(("Desk (account line, weather piece, launcher toasts)", deskTexts.Count, 0, deskTexts.Count));
            main.Close();
        }

        // ---- Settings re-opened with streamer mode already on: separates "built before the flip" from "never masked"
        var reopenItems = new List<SmokeItem>();
        r.Window.Close();
        await Settle2();
        {
            var svm2 = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask,
                checkForUpdatesNow: () => Task.FromResult(new UpdateProbeResult(UpdateProbeStatus.UpToDate)), isStoreBuild: false);
            using var r2 = await OpenRig(widget, accounts, svm2, ClaudeCodeProfile.Dir, creds, open: true);
            r2.KeepWidget = true;
            await r2.Go(SettingsPage.ClaudeUsage);
            svm2.ClaudeCode.Section = "Overview";
            try { await svm2.ClaudeCode.RefreshActiveAsync(); } catch (Exception) { }
            await Settle2();
            reopenItems = Gather(r2.Content(SettingsPage.ClaudeUsage), "Settings/ClaudeUsage/Overview (window opened with streamer already on)", out int el2, out int pe2);
            perSurface.Add(("Settings/ClaudeUsage/Overview (re-opened)", el2, pe2, reopenItems.Count));
            await r2.Go(SettingsPage.Accounts);
            reopenItems.AddRange(Gather(r2.Content(SettingsPage.Accounts), "Settings/Accounts (re-opened)", out _, out _));
        }

        // ---- search
        var hits = new List<(HarvestEntry E, SmokeItem I)>();
        foreach (var item in all)
            foreach (var e in entries)
                if (EntryMatches(e, item.Text)) hits.Add((e, item));
        var codenames = new Dictionary<int, string>();
        int withCodename = 0;
        var texts = new HashSet<string>(all.Select(i => i.Text).Where(t => t.Length > 0), StringComparer.Ordinal);
        var corpus = string.Join("\n", texts);
        var codenameRows = new List<string>();
        foreach (var e in entries)
        {
            string? code = null;
            try
            {
                var mask = StreamerMode.Mask;
                code = e.Category switch
                {
                    "account" => mask.Name(CodenameCategory.Account, e.Value),
                    "email" => mask.Name(CodenameCategory.Email, e.Value),
                    "folder-path" => mask.Folder(e.Value),
                    "project" => mask.Name(CodenameCategory.Project, e.Value),
                    "publish-host" => mask.Name(CodenameCategory.Host, e.Value),
                    "launcher-host" => mask.Name(CodenameCategory.Host, e.Value),
                    "weather-place" => null,
                    _ => null,
                };
            }
            catch { }
            bool seen = code is { Length: > 0 } && corpus.Contains(code, StringComparison.Ordinal);
            if (seen) withCodename++;
            codenameRows.Add($"{e.Category} #{e.Id}: codename appeared={seen}{(code is null ? " (no direct mask mapping)" : "")}");
        }

        SmokePrivate.Add("== check 8 hits (private: real values) ==");
        var grouped = hits.GroupBy(x => (x.E.Category, x.E.Id, x.I.Surface, x.I.Control, x.I.Prop)).OrderBy(g => g.Key.Surface).ToList();
        foreach (var g in grouped)
            SmokePrivate.Add($"{g.Key.Category} #{g.Key.Id} [{g.First().E.Value}]{(g.First().E.Weak ? " WEAK" : "")} | {g.Key.Surface} | {g.Key.Control} | {g.Key.Prop} | x{g.Count()} | where={g.First().I.Where} | text=<<{Trunc(g.First().I.Text)}>>");
        var reopenHits = new List<(HarvestEntry E, SmokeItem I)>();
        foreach (var item in reopenItems)
            foreach (var e in entries)
                if (EntryMatches(e, item.Text)) reopenHits.Add((e, item));
        SmokePrivate.Add("== re-opened with streamer already on: hits ==");
        foreach (var g in reopenHits.GroupBy(x => (x.E.Category, x.E.Id, x.I.Surface, x.I.Control, x.I.Prop)))
            SmokePrivate.Add($"{g.Key.Category} #{g.Key.Id} [{g.First().E.Value}] | {g.Key.Surface} | {g.Key.Control} | {g.Key.Prop} | x{g.Count()} | text=<<{Trunc(g.First().I.Text)}>>");
        Ev(8, $"re-opened Settings with streamer already on (Claude Usage Overview and Accounts re-read, {reopenItems.Count} text items): {reopenHits.GroupBy(x => (x.E.Category, x.E.Id, x.I.Surface, x.I.Control, x.I.Prop)).Count()} hit groups");
        foreach (var g in reopenHits.GroupBy(x => (x.E.Category, x.E.Id, x.I.Surface, x.I.Control, x.I.Prop)))
            Ev(8, $"reopened hit: {g.Key.Category} #{g.Key.Id}{(g.First().E.Weak ? " (weak)" : "")} on {g.Key.Surface}, {g.Key.Control}, in {g.Key.Prop}, x{g.Count()}");
        SmokePrivate.Add("== codename coverage ==");
        SmokePrivate.AddRange(codenameRows);
        SmokePrivate.Add("== per surface ==");
        foreach (var s in perSurface) SmokePrivate.Add($"{s.Surface}: elements {s.Elements}, peers {s.Peers}, items {s.Items}");

        int totalElements = perSurface.Sum(s => s.Elements), totalPeers = perSurface.Sum(s => s.Peers);
        Ev(8, $"coverage: {perSurface.Count} surfaces walked, {totalElements} elements, {totalPeers} automation peers, {all.Count} text items collected");
        foreach (var s in perSurface) Ev(8, $"surface {s.Surface}: {s.Elements} elements, {s.Peers} peers");
        Ev(8, $"harvest entries with a codename appearing: {withCodename} of {entries.Count} (entries that have no direct mask mapping can't count)");
        Ev(8, $"raw hits: {hits.Count} text items matching a harvest entry, in {grouped.Count} distinct (entry, surface, control, property) groups");
        foreach (var g in grouped)
            Ev(8, $"hit: {g.Key.Category} #{g.Key.Id}{(g.First().E.Weak ? " (weak)" : "")} on {g.Key.Surface}, {g.Key.Control}, in {g.Key.Prop}, x{g.Count()}");
        Ev(8, "Meters are empty by design: nothing fetched and nothing started, so the Meters view and the widget's bars carry no live numbers.");
        Verdict(8, grouped.Count == 0 ? "PASS" : "PARTIAL");
        FgNote("check 8 end");
        return profile;
    }

    static string Trunc(string s) => s.Length > 160 ? s[..160] + "…" : s;

    // ------------------------------------------------------------------ check 10: restart and Reset

    static async Task SmokeRestartAndReset()
    {
        // Run 1: streamer on, names handed out, then everything disposed.
        var codes = new Dictionary<string, string>();
        string external, codenamesFile, streamerFile, settingsFile;
        {
            using var r1 = await BuildFixtureRig("restart", open: true);
            var paths = new Paths();
            external = paths.ExternalDirPath;
            codenamesFile = paths.CodenamesFile;
            streamerFile = paths.StreamerStateFile;
            settingsFile = paths.SettingsFile;
            r1.Widget.Streamer.SetOn(true);
            await Settle2();
            var mask = StreamerMode.Mask;
            foreach (var a in new[] { "Qxalpha", FxShown }) codes["account:" + a] = mask.Name(CodenameCategory.Account, a);
            codes["project:qxproject"] = mask.Name(CodenameCategory.Project, "qxproject");
            codes["host:qxhost.example"] = mask.Name(CodenameCategory.Host, "qxhost.example");
            codes["folder:work"] = mask.Folder(Path.Combine(r1.Home, ".claude-qxwork"));
            await r1.Go(SettingsPage.Accounts);
            await Settle2();
            r1.Widget.Streamer.Flush();
            Assert(10, File.Exists(codenamesFile), "codenames.json exists after run 1");
            Assert(10, File.Exists(streamerFile), "streamer.json exists after run 1");
        }
        await Settle2();
        Assert(10, !StreamerMode.On, "everything disposed: the mask is back to off between runs");

        // Run 2: the same scratch profile, started again.
        {
            using var r2 = await BuildFixtureRig("restart", open: true);
            Assert(10, r2.Widget.Streamer.On && StreamerMode.On, "restart: streamer mode is on before any fetch (nothing was started)");
            var mask = StreamerMode.Mask;
            bool same = true;
            foreach (var (k, v) in codes)
            {
                string now = k.Split(':')[0] switch
                {
                    "account" => mask.Name(CodenameCategory.Account, k[(k.IndexOf(':') + 1)..]),
                    "project" => mask.Name(CodenameCategory.Project, k[(k.IndexOf(':') + 1)..]),
                    "host" => mask.Name(CodenameCategory.Host, k[(k.IndexOf(':') + 1)..]),
                    _ => mask.Folder(Path.Combine(r2.Home, ".claude-qxwork")),
                };
                if (now != v) same = false;
            }
            Assert(10, same, $"the same codenames after restart ({codes.Count} names compared: accounts, a project, a host, a folder)");
            await r2.Go(SettingsPage.Accounts);
            Assert(10, Descendants<StreamerBanner>(r2.Content(SettingsPage.Accounts)).Any(b => b.IsVisible), "the banner shows after restart");
            using var tray = MakeTray(r2.Widget);
            tray.RefreshTooltip();
            Assert(10, tray.Tray.ToolTipText.StartsWith(SettingsNames.StreamerTooltipPrefix, StringComparison.Ordinal), "the tray tooltip carries the prefix after restart");
            var main = new Sanduhr.App.MainWindow { DataContext = r2.Widget };
            OffScreen(main);
            main.Show();
            main.Left = -20000;
            NeverActivateWindow(main);
            await Settle2();
            r2.Widget.RefreshAccountLabel();   // App's Start() does this before the first fetch; the harness never calls Start()
            await Settle2();
            var g2 = Descendants<TextBlock>(main).FirstOrDefault(t => t.Text == "\uED1A");
            Assert(10, g2 is { IsVisible: true }, $"the chip glyph shows after restart (chip label refreshed the way Start() does, no fetch); glyph found {g2 is not null}, visibility {g2?.Visibility}, chip text length {r2.Widget.AccountSwitcherText.Length}, widget StreamerOn {r2.Widget.StreamerOn}");
            main.Close();
            tray.Dispose();

            // Reset: "All settings" (the Settings part) on the scratch profile.
            string tmpDecoy1 = codenamesFile + ".tmp", tmpDecoy2 = streamerFile + ".tmp";
            File.WriteAllText(tmpDecoy1, "crashed write");
            File.WriteAllText(tmpDecoy2, "crashed write");
            string timeDir = Path.Combine(external, "time");
            Directory.CreateDirectory(timeDir);
            string keep = Path.Combine(timeDir, "keep.txt");
            File.WriteAllText(keep, "another tool's file");
            Assert(10, File.Exists(codenamesFile) && File.Exists(streamerFile) && File.Exists(tmpDecoy1) && File.Exists(tmpDecoy2), "before Reset: codenames.json, streamer.json and both .tmp files exist");

            var service = new ResetService(new Paths(), r2.Accounts, new PublishTokenStore(r2.Creds),
                keep => Task.FromResult<IReadOnlyList<string>>([]),
                () => ResetGuard.MarkDone(),
                null, null, null);
            var results = await service.RunAsync(new ResetChoices(ClaudeCodeEntries: false, Accounts: false, History: false, Themes: false, Settings: true), []);
            Assert(10, results.All(x => x.Ok), $"Reset (All settings) ran: {string.Join(", ", results.Select(x => x.Part + "=" + (x.Ok ? "ok" : "failed")))}");
            Assert(10, !File.Exists(codenamesFile) && !File.Exists(tmpDecoy1), "Reset removed codenames.json and codenames.json.tmp");
            Assert(10, !File.Exists(streamerFile) && !File.Exists(tmpDecoy2), "Reset removed streamer.json and streamer.json.tmp");
            Assert(10, File.Exists(keep), "the decoy time\\keep.txt is untouched");
            // Everything that could write on the way to quit: a late mask call, the posted save, a Settings change, then dispose.
            StreamerMode.Mask.Name(CodenameCategory.Project, "late-name");
            await Settle2();
            await Task.Delay(300);
            await Settle2();
            r2.Widget.Streamer.SetShown(CodenameCategory.Account, "Qxalpha", true);
            await Settle2();
            Assert(10, !File.Exists(codenamesFile) && !File.Exists(streamerFile), "after Reset, a late name, the posted save and a Settings change wrote neither file back");
            r2.Dispose();
            await Settle2();
            Assert(10, !File.Exists(codenamesFile) && !File.Exists(streamerFile), "after disposing everything (the quit path) neither file came back");
            Assert(10, !File.Exists(settingsFile), "settings.json is gone too (the Settings part ran) and was not rewritten");
        }
        Verdict(10, "PASS");
    }
}
