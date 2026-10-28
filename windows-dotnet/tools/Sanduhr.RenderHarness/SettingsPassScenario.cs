using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

// The Settings pass scenario (settings-pass): evidence for a persona usability pass over Settings, written to
// <out>/settings-pass/. Per page: PNGs at 800 and 600 high (and 720 and 520 for Alerts and Publish usage, 680 wide
// for Desk), scrolled section by section when the page scrolls; an automation-tree dump; the keyboard tab order.
// Then: Settings search for a list of queries and where each result lands; every What's New card, tour card and
// in-app "open Settings at X" destination and whether its section is visible without scrolling; a contrast table.
//
// Same rules as every scenario here: off screen, never App, never Start(), demo accounts only, no network. It also
// never activates a window: keyboard focus is routed with window messages to the off-screen window, so the
// foreground window of the person at the PC does not change (the log records the foreground handle before and after).
//
//   --scenario settings-pass [--light] [--pages General,Alerts]   (--pages limits which pages run; default all)
//
// The automation dump walks the window's AutomationPeer tree in process (the tree a screen reader gets from UI
// Automation), because a UI Automation client on the UI thread of the process it reads can deadlock.
public static partial class Program
{
    static string[] PassPages = [];
    /// <summary>--parts: pages, tab, focus, search, links, contrast (default all). tab and focus move keyboard focus inside the off-screen window.</summary>
    public static string[] PassParts = [];
    static bool Part(string name) => PassParts.Length == 0 || PassParts.Contains(name);

    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value);

    static IntPtr PassForeground0;

    sealed record PassUnit(string Dir, SettingsPage Page, string? View, string Label);

    static void PassWrite(string rel, string text)
    {
        string path = Path.Combine(Out, "settings-pass", rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        Log.Add($"txt settings-pass/{rel.Replace('\\', '/')}");
    }

    static void PassPng(FrameworkElement el, string rel)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(Out, "settings-pass", rel))!);
        Save(el, "settings-pass/" + (rel.EndsWith(".png") ? rel[..^4] : rel).Replace('\\', '/'));
    }

    static string Fmt(Rect r) => $"{r.X:0},{r.Y:0} {r.Width:0}x{r.Height:0}";

    // ---- window plumbing ----

    sealed class PassCtx
    {
        public required Window Sw;
        public required TabControl Tabs;
        public required SettingsViewModel Svm;
        public required MethodInfo Select;
        public required FrameworkElement Root;     // the window's content
        public required ContentPresenter Host;     // the selected page's host
        public bool? KeyboardWorks;
    }

    static TabItem TabOf(PassCtx c, SettingsPage page) =>
        c.Tabs.Items.OfType<TabItem>().First(t => t.Tag is string s && s == page.ToString());

    static async Task Settle2()
    {
        await Settle();
        await Task.Delay(250);
        await Settle();
    }

    static List<ScrollViewer> VisibleScrollers(DependencyObject host) =>
        [.. Descendants<ScrollViewer>(host).Where(s => s.IsVisible && s.ActualHeight > 0)];

    static void ScrollAllToTop(DependencyObject host)
    {
        foreach (var sv in Descendants<ScrollViewer>(host))
            sv.ScrollToTop();
    }

    /// <summary>The outermost scroller that actually scrolls and is page sized.</summary>
    static ScrollViewer? MainScroller(DependencyObject host)
    {
        var all = VisibleScrollers(host);
        var cand = all.Where(s => s.ScrollableHeight > 1 && s.ViewportHeight >= 150).ToList();
        return cand.FirstOrDefault(s => !cand.Any(o => !ReferenceEquals(o, s) && IsAncestor(o, s)));
    }

    static bool IsAncestor(DependencyObject maybeAncestor, DependencyObject d)
    {
        for (var p = VisualTreeHelper.GetParent(d); p is not null; p = VisualTreeHelper.GetParent(p))
            if (ReferenceEquals(p, maybeAncestor)) return true;
        return false;
    }

    static string ScrollerNotes(PassCtx c)
    {
        var all = VisibleScrollers(c.Host);
        var main = MainScroller(c.Host);
        var sb = new StringBuilder();
        sb.AppendLine($"page host {c.Host.ActualWidth:0}x{c.Host.ActualHeight:0} DIP; window {c.Sw.ActualWidth:0}x{c.Sw.ActualHeight:0}");
        sb.AppendLine($"visible scrollers: {all.Count}; scrolling now: {all.Count(s => s.ScrollableHeight > 1)}");
        foreach (var s in all)
        {
            Rect r = s.TransformToAncestor(c.Host).TransformBounds(new Rect(s.RenderSize));
            sb.AppendLine($"  scroller at {Fmt(r)} viewport {s.ViewportHeight:0} extent {s.ExtentHeight:0} scrollable {s.ScrollableHeight:0}{(ReferenceEquals(s, main) ? "  <- main" : "")}");
        }
        return sb.ToString();
    }

    // ---- keyboard routing without activating anything ----

    /// <summary>Marks the window as one that never becomes the foreground window (WS_EX_NOACTIVATE, WS_EX_TOOLWINDOW),
    /// so routing keyboard focus inside it cannot take the foreground from the person's game.</summary>
    static void NeverActivate(Window w)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        const int GWL_EXSTYLE = -20;
        long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, (IntPtr)(ex | 0x08000000L | 0x80L));
    }

    /// <summary>Stops the run, after handing the foreground back, if the harness window ever became the foreground window.</summary>
    static void GuardForeground(PassCtx c)
    {
        var mine = new WindowInteropHelper(c.Sw).Handle;
        if (GetForegroundWindow() != mine) return;
        SetForegroundWindow(PassForeground0);
        throw new InvalidOperationException("The harness window became the foreground window; stopped so the person's foreground window is left alone. Run again without the keyboard parts: --parts pages,search,links,contrast leaves out tab, focus and anything that moves keyboard focus.");
    }

    /// <summary>Lets WPF route keyboard focus inside the off-screen window: WM_SETFOCUS only. WM_ACTIVATE is left out: in the test
    /// host it made the window the foreground window (measured in 6af89bf), and focus routes without it. NoActivate (App.Tests)
    /// follows the same rule.</summary>
    static void RouteKeyboard(PassCtx c)
    {
        var hwnd = new WindowInteropHelper(c.Sw).Handle;
        const int WM_SETFOCUS = 0x0007;
        SendMessage(hwnd, WM_SETFOCUS, IntPtr.Zero, IntPtr.Zero);
    }

    static bool TryKeyboardFocus(PassCtx c, IInputElement el)
    {
        Keyboard.Focus(el);
        GuardForeground(c);
        return ReferenceEquals(Keyboard.FocusedElement, el);
    }

    // ---- automation dump ----

    static string PeerName(UIElement? el) =>
        el is null ? "" : UIElementAutomationPeer.CreatePeerForElement(el)?.GetName() ?? "";

    static string ViewStatus(FrameworkElement el, FrameworkElement root, FrameworkElement host)
    {
        if (!el.IsVisible || el.ActualWidth <= 0 || el.ActualHeight <= 0)
            return "hidden";
        Rect r;
        try { r = el.TransformToAncestor(root).TransformBounds(new Rect(el.RenderSize)); }
        catch { return "?"; }
        Rect clip = new Rect(0, 0, root.ActualWidth, root.ActualHeight);
        for (var p = VisualTreeHelper.GetParent(el); p is not null; p = VisualTreeHelper.GetParent(p))
        {
            if (p is ScrollViewer sv && sv.IsVisible)
            {
                Rect v = sv.TransformToAncestor(root).TransformBounds(new Rect(sv.RenderSize));
                clip.Intersect(v);
            }
        }
        // Inside the page host only for page elements; sidebar elements are measured against the window.
        Rect inter = Rect.Intersect(r, clip);
        if (inter.IsEmpty || inter.Width <= 0 || inter.Height <= 0)
            return "out";
        double frac = inter.Width * inter.Height / Math.Max(1, r.Width * r.Height);
        return frac >= 0.98 ? "in" : "partial";
    }

    static void DumpPeer(AutomationPeer p, int depth, StringBuilder sb, PassCtx c, Point origin, double dpi, int[] count)
    {
        if (depth > 40 || count[0] > 4000) return;
        count[0]++;
        string type = p.GetAutomationControlType().ToString();
        string name = "", id = "", help = "";
        bool enabled = false, offscreen = false, focusable = false;
        Rect rect = Rect.Empty;
        try
        {
            name = p.GetName() ?? "";
            id = p.GetAutomationId() ?? "";
            help = p.GetHelpText() ?? "";
            enabled = p.IsEnabled();
            offscreen = p.IsOffscreen();
            focusable = p.IsKeyboardFocusable();
            rect = p.GetBoundingRectangle();
        }
        catch { }
        string rel = rect.IsEmpty ? "-" : $"{(rect.X - origin.X) / dpi:0},{(rect.Y - origin.Y) / dpi:0} {rect.Width / dpi:0}x{rect.Height / dpi:0}";
        string view = "-", tabstop = "-", anchor = "";
        if (p is UIElementAutomationPeer { Owner: FrameworkElement fe })
        {
            view = ViewStatus(fe, c.Root, c.Host);
            if (fe is Control ctl) tabstop = ctl.IsTabStop ? "tabstop" : "no-tabstop";
            anchor = Sanduhr.App.Views.SettingsAnchor.GetId(fe) is { } a ? a : "";
        }
        sb.Append(new string(' ', depth * 2))
          .Append($"[{type}] name=\"{name}\" id=\"{id}\" enabled={enabled} offscreen={offscreen} rect={rel} inview={view} keyboard-focusable={focusable} {tabstop}");
        if (anchor.Length > 0) sb.Append($" anchor={anchor}");
        if (help.Length > 0) sb.Append($" help=\"{help}\"");
        sb.AppendLine();
        List<AutomationPeer>? kids = null;
        try { kids = p.GetChildren(); } catch { }
        if (kids is null) return;
        foreach (var k in kids)
            DumpPeer(k, depth + 1, sb, c, origin, dpi, count);
    }

    static string AutomationDump(PassCtx c, string heading)
    {
        var dpi = VisualTreeHelper.GetDpi(c.Sw).DpiScaleX;
        Point origin = c.Root.PointToScreen(new Point(0, 0));
        var sb = new StringBuilder();
        sb.AppendLine(heading);
        sb.AppendLine($"window content {c.Root.ActualWidth:0}x{c.Root.ActualHeight:0} DIP, DPI scale {dpi:0.00}; rects are DIP relative to the window's content");
        sb.AppendLine("inview: in = fully inside every scroll viewport and the window; partial; out = clipped away (scroll or window edge); hidden");
        var peer = UIElementAutomationPeer.CreatePeerForElement(c.Sw);
        if (peer is null) { sb.AppendLine("(no peer)"); return sb.ToString(); }
        DumpPeer(peer, 0, sb, c, origin, dpi, new int[1]);
        return sb.ToString();
    }

    // ---- tab order ----

    static string Describe(PassCtx c, IInputElement? e)
    {
        if (e is System.Windows.Documents.Hyperlink link)
        {
            string label = string.Concat(link.Inlines.OfType<System.Windows.Documents.Run>().Select(r => r.Text));
            string where = IsInsideVisual(link, c.Host) ? "page" : "outside page (sidebar or window)";
            return $"Hyperlink text=\"{Trim(label)}\" -> {link.NavigateUri} [{where}]";
        }
        if (e is not FrameworkElement fe) return e is null ? "(none)" : e.GetType().Name;
        string name = PeerName(fe);
        string aid = AutomationProperties.GetAutomationId(fe);
        string region = IsInsideVisual(fe, c.Host) ? "page" : "outside page (sidebar or window)";
        string view = ViewStatus(fe, c.Root, c.Host);
        Rect r = Rect.Empty;
        try { if (fe.IsVisible) r = fe.TransformToAncestor(c.Root).TransformBounds(new Rect(fe.RenderSize)); } catch { }
        string text = fe is TextBox tb ? $" text=\"{Trim(tb.Text)}\"" : "";
        return $"{fe.GetType().Name} name=\"{name}\" id=\"{aid}\" at {(r.IsEmpty ? "-" : Fmt(r))} inview={view} [{region}]{text}";
    }

    static string Trim(string s) => s.Length > 30 ? s[..30] + "..." : s;

    static bool IsInsideVisual(DependencyObject d, DependencyObject root)
    {
        for (DependencyObject? x = d; x is not null; x = (x is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(x) : null) ?? LogicalTreeHelper.GetParent(x))
            if (ReferenceEquals(x, root)) return true;
        return false;
    }

    static UIElement? FirstFocus(DependencyObject root)
    {
        if (root is Control { Focusable: true, IsVisible: true, IsEnabled: true, IsTabStop: true } el)
            return el;
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
            if (FirstFocus(VisualTreeHelper.GetChild(root, i)) is { } f) return f;
        return null;
    }

    static async Task<string> TabOrder(PassCtx c, string dir, bool shots)
    {
        var sb = new StringBuilder();
        var start = FirstFocus(c.Host);
        if (start is null)
        {
            sb.AppendLine("The page has no keyboard-focusable control.");
            return sb.ToString();
        }
        if (!TryKeyboardFocus(c, start))
        {
            RouteKeyboard(c);
            await Settle();
            TryKeyboardFocus(c, start);
        }
        if (!ReferenceEquals(Keyboard.FocusedElement, start))
        {
            sb.AppendLine("Keyboard focus could not be set on the off-screen window (no activation allowed). Tab order not recorded.");
            c.KeyboardWorks = false;
            return sb.ToString();
        }
        c.KeyboardWorks = true;
        sb.AppendLine("Tab pressed from the page's first focusable control; each line is the control focus lands on.");
        var seen = new HashSet<IInputElement>();
        IInputElement? cur = start;
        int step = 0, shot = 0;
        while (cur is not null && step < 200)
        {
            step++;
            seen.Add(cur);
            sb.AppendLine($"{step,3}. {Describe(c, cur)}");
            if (shots && shot < 4 && cur is DependencyObject)
            {
                await Settle();
                PassPng(c.Root, $"{dir}/focus-{++shot:00}.png");
            }
            var before = cur;
            var next = new TraversalRequest(FocusNavigationDirection.Next);
            if (cur is UIElement ue) ue.MoveFocus(next);
            else if (cur is FrameworkContentElement fce) fce.MoveFocus(next);
            else { sb.AppendLine("(focus is on an element that cannot move focus: end)"); break; }
            GuardForeground(c);
            await Dispatcher.Yield(DispatcherPriority.Background);
            cur = Keyboard.FocusedElement;
            if (cur is null || ReferenceEquals(cur, before))
            {
                sb.AppendLine("(focus did not move: end)");
                break;
            }
            if (seen.Contains(cur))
            {
                sb.AppendLine($"(back to step {seen.ToList().IndexOf(cur) + 1}: loop closed after {step} stops)");
                break;
            }
        }
        if (step >= 200) sb.AppendLine("(stopped at 200 steps)");
        return sb.ToString();
    }

    // ---- landing probes ----

    sealed record Landing(string Page, string? Anchor, int Height, string Found, string Before, string After, string Scroll, string Focus);

    static string TargetStatus(PassCtx c, FrameworkElement? target, out string detail)
    {
        detail = "";
        if (target is null) return "no element marked";
        if (!target.IsVisible || target.ActualHeight <= 0) return "marked but hidden or collapsed";
        Rect r = target.TransformToAncestor(c.Host).TransformBounds(new Rect(target.RenderSize));
        Rect clip = new Rect(0, 0, c.Host.ActualWidth, c.Host.ActualHeight);
        for (var p = VisualTreeHelper.GetParent(target); p is not null; p = VisualTreeHelper.GetParent(p))
            if (p is ScrollViewer sv && sv.IsVisible)
                clip.Intersect(sv.TransformToAncestor(c.Host).TransformBounds(new Rect(sv.RenderSize)));
        detail = $"top {r.Y:0} height {r.Height:0} of a {c.Host.ActualHeight:0} high page";
        bool topIn = !clip.IsEmpty && r.Y >= clip.Y - 1 && r.Y < clip.Bottom - 8;
        var inter = Rect.Intersect(r, clip);
        if (clip.IsEmpty || inter.IsEmpty || inter.Height <= 0) return $"below the fold ({detail})";
        if (inter.Height >= r.Height - 2) return $"fully in view ({detail})";
        return topIn ? $"top in view, rest below ({detail})" : $"partly in view, top cut off ({detail})";
    }

    static async Task SelectUnitTop(PassCtx c, SettingsPage page, string? view)
    {
        // Go to a different page first so the page is realized fresh, then choose its Claude Usage view.
        var other = TabOf(c, page == SettingsPage.General ? SettingsPage.Accounts : SettingsPage.General);
        c.Tabs.SelectedItem = other;
        await Settle();
        if (page == SettingsPage.ClaudeUsage && view is not null)
            c.Svm.ClaudeCode.Section = view;
        if (page == SettingsPage.Themes)
            c.Svm.Themes.IsStudioMode = true;
        c.Tabs.SelectedItem = TabOf(c, page);
        await Settle2();
        ScrollAllToTop(c.Host);
        await Settle();
    }

    static readonly Dictionary<string, Landing> LandingCache = new();

    static async Task<Landing> Probe(PassCtx c, SettingsPage page, string? anchor, int height)
    {
        string key = $"{page}|{anchor}|{height}";
        if (LandingCache.TryGetValue(key, out var cached)) return cached;
        var entry = SettingsIndex.Find(page, anchor);
        c.Sw.Height = height;
        await Settle();
        await SelectUnitTop(c, page, entry?.Subsection);
        if (page == SettingsPage.Themes && anchor == SettingsIndex.Paste)
            c.Svm.Themes.IsStudioMode = false;
        await Settle();
        var tab = TabOf(c, page);
        FrameworkElement? target = anchor is null || tab.Content is not DependencyObject content
            ? null : Sanduhr.App.Views.SettingsAnchor.Find(content, anchor);
        string before = anchor is null ? "page top" : TargetStatus(c, target, out _);

        c.Select.Invoke(c.Sw, [page, anchor]);
        await Settle2();
        target = anchor is null || tab.Content is not DependencyObject content2
            ? null : Sanduhr.App.Views.SettingsAnchor.Find(content2, anchor);
        string after = anchor is null ? "page top" : TargetStatus(c, target, out _);
        var main = MainScroller(c.Host);
        string scroll = main is null ? "no scroller" : $"offset {main.VerticalOffset:0} of {main.ScrollableHeight:0}";
        string focus = Keyboard.FocusedElement is FrameworkElement ? Describe(c, Keyboard.FocusedElement) : "(no keyboard focus)";
        string found = entry is null && anchor is not null ? "anchor not in the index" : (target is null && anchor is not null ? "no element marked" : "ok");
        var l = new Landing(SettingsNames.PageTitle(page), anchor, height, found, before, after, scroll, focus);
        LandingCache[key] = l;
        return l;
    }

    // ---- the scenario ----

    static async Task RenderSettingsPass(WidgetViewModel vm, Assembly asm, bool light)
    {
        var foreground0 = GetForegroundWindow();
        PassForeground0 = foreground0;
        Sanduhr.App.Services.WidgetCatalogService.HandlerOverride = () => new OfflineHandler();
        var svm = new SettingsViewModel(vm, () => Task.CompletedTask, _ => Task.CompletedTask);
        AssertOnlySampleAccounts(svm);
        var swType = asm.GetType("Sanduhr.App.Views.SettingsWindow")!;
        var sw = (Window)Activator.CreateInstance(swType, svm)!;
        OffScreen(sw);
        sw.Show();
        sw.Left = -20000;
        sw.Top = -20000;
        await Settle2();

        var tabs = (TabControl)sw.FindName("Tabs");
        tabs.ApplyTemplate();
        var host = (ContentPresenter)tabs.Template.FindName("PART_SelectedContentHost", tabs);
        var c = new PassCtx
        {
            Sw = sw, Tabs = tabs, Svm = svm, Select = swType.GetMethod("SelectPage")!,
            Root = (FrameworkElement)sw.Content, Host = host,
        };
        Log.Add($"settings-pass: window {sw.ActualWidth:0}x{sw.ActualHeight:0}, dpi scale {VisualTreeHelper.GetDpi(sw).DpiScaleX:0.00}");
        NeverActivate(sw);
        GuardForeground(c);
        if (Part("tab") || Part("focus"))
        {
            RouteKeyboard(c);
            GuardForeground(c);
        }
        await Settle();

        var units = new List<PassUnit>();
        if (!Part("pages")) PassPages = ["-none-"];
        int index = 0;
        foreach (var item in tabs.Items.OfType<TabItem>())
        {
            string header = HeaderText(item.Header);
            var page = Enum.Parse<SettingsPage>((string)item.Tag);
            string dir = $"{index:00}-{Slug(header)}";
            if (page == SettingsPage.ClaudeUsage)
            {
                units.Add(new PassUnit(dir + "-meters", page, SettingsIndex.MetersView, header + " (Meters view)"));
                units.Add(new PassUnit(dir + "-overview", page, SettingsIndex.OverviewView, header + " (Overview view)"));
                units.Add(new PassUnit(dir + "-trends", page, SettingsIndex.TrendsView, header + " (Trends view)"));
                units.Add(new PassUnit(dir + "-sessions", page, SettingsIndex.SessionsView, header + " (Sessions view)"));
            }
            else
            {
                units.Add(new PassUnit(dir, page, null, header));
            }
            index++;
        }
        if (PassPages.Length > 0)
            units = [.. units.Where(u => PassPages.Any(p => u.Dir.Contains(Slug(p), StringComparison.OrdinalIgnoreCase)))];

        // Sidebar once, with the search box and Quit, at the default size.
        sw.Width = 780; sw.Height = 800;
        await Settle2();
        if (Part("pages"))
        PassWrite("sidebar-automation.txt", AutomationDump(c, "Whole-window automation tree with General selected (sidebar parts first, then the page)"));

        var summary = new StringBuilder();
        summary.AppendLine("unit\tpage-heights\tscroller@800\tscroller@600\tfiles");
        foreach (var u in units)
        {
            var heights = new List<(string Name, int W, int H)> { ("h800", 780, 800), ("h600", 780, 600) };
            if (u.Page is SettingsPage.Alerts or SettingsPage.Publish)
            {
                heights.Add(("h720", 780, 720));
                heights.Add(("h520", 780, 520));
            }
            if (u.Page is SettingsPage.Desk)
                heights.Add(("w680-h800", 680, 800));

            var notes = new StringBuilder();
            notes.AppendLine($"{u.Label}");
            string scroll800 = "", scroll600 = "";
            foreach (var (vname, w, h) in heights)
            {
                sw.Width = w; sw.Height = h;
                await Settle();
                await SelectUnitTop(c, u.Page, u.View);
                notes.AppendLine();
                notes.AppendLine($"== {vname}: window {w}x{h} ==");
                notes.Append(ScrollerNotes(c));
                var main = MainScroller(c.Host);
                string d = $"{u.Dir}/{vname}";
                PassPng(c.Root, $"{d}/page.png");
                if (main is not null)
                {
                    double step = Math.Max(120, main.ViewportHeight * 0.85);
                    int n = 0;
                    for (double off = 0; n < 30; off += step)
                    {
                        off = Math.Min(off, main.ScrollableHeight);
                        main.ScrollToVerticalOffset(off);
                        await Settle();
                        PassPng(c.Root, $"{d}/page-section-{++n:00}.png");
                        if (off >= main.ScrollableHeight - 0.5) break;
                    }
                    notes.AppendLine($"scrolled through in {n} sections (page-section-01 is the top)");
                    main.ScrollToTop();
                    await Settle();
                }
                else
                {
                    var all = VisibleScrollers(c.Host);
                    notes.AppendLine(all.Count == 0
                        ? "this page has NO scroller: whatever does not fit the window is cut off (see page-tall.png for the whole page)"
                        : "no page-level scroller; inner scrollers only (listed above)");
                }
                if (vname == "h800") scroll800 = main is null ? "none" : $"{main.ScrollableHeight:0} scrollable";
                if (vname == "h600") scroll600 = main is null ? "none" : $"{main.ScrollableHeight:0} scrollable";
                PassWrite($"{d}/automation.txt", AutomationDump(c, $"{u.Label}, window {w}x{h}"));
            }

            // The whole page at 1900 high, to see what a short window cuts off.
            sw.Width = 780; sw.Height = 1900;
            await Settle();
            await SelectUnitTop(c, u.Page, u.View);
            PassPng(c.Root, $"{u.Dir}/page-tall.png");
            {
                var dump = AutomationDump(c, $"{u.Label}, window 780x1900 (everything laid out)");
                PassWrite($"{u.Dir}/automation-tall.txt", dump);
                double bottom = 0;
                // Leaf content only (text and controls): panels and borders stretch to the host and say nothing.
                foreach (var el in Descendants<FrameworkElement>(c.Host).Where(e => e.IsVisible && e.ActualHeight > 0 && (e is TextBlock || e is Control) && e is not ScrollViewer && e is not ScrollBar && e is not TabItem))
                {
                    try { bottom = Math.Max(bottom, el.TransformToAncestor(c.Host).TransformBounds(new Rect(el.RenderSize)).Bottom); } catch { }
                }
                notes.AppendLine();
                notes.AppendLine($"natural content bottom at 1900 high: about {bottom:0} DIP below the top of the page host (host is {c.Host.ActualHeight:0} high at 1900)");
            }

            // Tab order and focus rings at the default size.
            sw.Width = 780; sw.Height = 800;
            await Settle();
            await SelectUnitTop(c, u.Page, u.View);
            if (Part("tab"))
                PassWrite($"{u.Dir}/taborder.txt", await TabOrder(c, u.Dir, shots: true));
            ScrollAllToTop(c.Host);
            Keyboard.ClearFocus();
            PassWrite($"{u.Dir}/notes.txt", notes.ToString());
            summary.AppendLine($"{u.Dir}\t{string.Join(",", heights.Select(h => h.Name))}\t{scroll800}\t{scroll600}\t{u.Dir}/");
        }
        if (Part("pages")) PassWrite("pages-summary.txt", summary.ToString());

        // Alerts with Enable alerts off: do the sub-controls dim?
        if (Part("pages") && (PassPages.Length == 0 || PassPages.Any(p => Slug(p) == "alerts")))
        {
            sw.Width = 780; sw.Height = 800;
            await Settle();
            await SelectUnitTop(c, SettingsPage.Alerts, null);
            bool was = svm.Alerts.AlertsEnabled;
            svm.Alerts.AlertsEnabled = false;
            await Settle2();
            PassPng(c.Root, "probe/alerts-enable-off.png");
            PassWrite("probe/alerts-enable-off-automation.txt", AutomationDump(c, "Alerts with Enable alerts off"));
            svm.Alerts.AlertsEnabled = was;
            await Settle2();
        }

        if (Part("focus"))
            await PassFocus(c);
        if (PassPages.Length == 0 || !Part("pages"))
        {
            if (Part("search")) await PassSearch(c);
            if (Part("links")) await PassLinks(c);
            if (Part("contrast")) PassContrast();
            if (Part("menucheck")) PassMenuCheck(light);
            if (Part("dialog")) PassDialog(c, light);
        }
        Log.Add($"settings-pass: foreground window before {foreground0}, after {GetForegroundWindow()}; keyboard routing worked: {(c.KeyboardWorks is { } k ? k.ToString() : "not run")}");

        sw.Close();
        await Settle();
    }

    // ---- focus rings on the sidebar, pills and link-style buttons ----

    static async Task PassFocus(PassCtx c)
    {
        c.Sw.Width = 780; c.Sw.Height = 800;
        await Settle();
        var sb = new StringBuilder();
        sb.AppendLine("Keyboard focus placed on one control at a time, the window content saved after each (probe/focus-*.png).");
        async Task Shot(string name, SettingsPage page, Func<UIElement?> find, string? view = null)
        {
            await SelectUnitTop(c, page, view);
            var el = find();
            if (el is null) { sb.AppendLine($"{name}: control not found"); return; }
            bool ok = TryKeyboardFocus(c, el);
            await Settle();
            PassPng(c.Root, $"probe/focus-{name}.png");
            var fg = GetForegroundWindow();
            var mine = new WindowInteropHelper(c.Sw).Handle;
            sb.AppendLine($"{name}: focus set={ok}; {Describe(c, Keyboard.FocusedElement)}; foreground {(fg == mine ? "IS THE HARNESS WINDOW" : "unchanged or another window")} ({fg}, harness {mine})");
        }
        UIElement? ByName(DependencyObject root, string contains) =>
            Descendants<Control>(root).FirstOrDefault(e => e.IsVisible && e.Focusable && PeerName(e).Contains(contains, StringComparison.OrdinalIgnoreCase));
        await Shot("sidebar-search-box", SettingsPage.General, () => c.Tabs.Template.FindName("SearchBox", c.Tabs) as UIElement);
        await Shot("sidebar-selected-page", SettingsPage.Alerts, () => TabOf(c, SettingsPage.Alerts));
        await Shot("sidebar-other-page", SettingsPage.Alerts, () => TabOf(c, SettingsPage.Mods));
        await Shot("sidebar-quit", SettingsPage.General, () => c.Tabs.Template.FindName("QuitButton", c.Tabs) as UIElement);
        await Shot("title-close", SettingsPage.General, () => Descendants<Button>(c.Root).FirstOrDefault(b => PeerName(b) == "✕"));
        await Shot("meters-week-pill", SettingsPage.ClaudeUsage, () => ByName(c.Host, "Week"), SettingsIndex.MetersView);
        await Shot("about-link-button", SettingsPage.About, () => ByName(c.Host, "Velopack project page"));
        await Shot("about-flat-button", SettingsPage.About, () => ByName(c.Host, "What's New"));
        await Shot("about-updates-switch", SettingsPage.About, () => ByName(c.Host, SettingsNames.CheckForUpdatesDaily));
        await Shot("privacy-link-button", SettingsPage.Privacy, () => ByName(c.Host, NetworkDisclosure.All[0].OffLinkName));
        await Shot("privacy-streamer-switch", SettingsPage.Privacy, () => ByName(c.Host, SettingsNames.StreamerSwitch));
        await Shot("desk-combo", SettingsPage.Desk, () => Descendants<ComboBox>(c.Host).FirstOrDefault(b => b.IsVisible && b.IsEnabled));
        await Shot("themes-first-control", SettingsPage.Themes, () => FirstFocus(c.Host));
        Keyboard.ClearFocus();
        PassWrite("probe/focus.txt", sb.ToString());
    }

    // ---- search ----

    static readonly string[] PassQueries =
    [
        "mods", "widgets", "catalog", "statusline", "meters", "watchers", "glow", "font", "theme", "history",
        "vault", "reset", "demo", "accounts", "update", "docker", "CI runners", "privacy", "publish", "shortcut",
    ];

    static async Task PassSearch(PassCtx c)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Settings search: for each query, the sidebar results in order, the row Enter opens, and where each result lands (window 780x800).");
        sb.AppendLine("A result line: [page row] or [section] title -> page / anchor : landing. Landing = state at the page top before the jump -> state after the jump.");
        c.Sw.Width = 780; c.Sw.Height = 800;
        foreach (var q in PassQueries)
        {
            c.Svm.Search.Clear();
            await Settle();
            c.Svm.Search.Query = q;
            await Settle2();
            PassPng(c.Root, $"search/{Slug(q)}-results.png");
            var rows = c.Svm.Search.Results.ToList();
            var first = c.Svm.Search.First;
            sb.AppendLine();
            sb.AppendLine($"## \"{q}\": {rows.Count} rows ({rows.Count(r => r.IsPage)} pages, {rows.Count(r => !r.IsPage)} sections)");
            if (rows.Count == 0) sb.AppendLine("  No setting by that name.");
            sb.AppendLine($"  Enter opens: {(first is null ? "(nothing)" : $"{first.Title} on {SettingsNames.PageTitle(first.Page)}{(first.Anchor is null ? "" : " / " + first.Anchor)}")}");
            var list = new List<SettingsSearchRow>(rows);
            foreach (var row in list)
            {
                if (row.Anchor == SettingsIndex.Quit)
                {
                    sb.AppendLine($"  [section] {row.Title} -> General / quit : focuses the Quit Sanduhr button at the sidebar foot (no page jump)");
                    continue;
                }
                var l = await Probe(c, row.Page, row.Anchor, 800);
                string kind = row.IsPage ? "page row" : "section ";
                sb.AppendLine($"  [{kind}] {row.Title} -> {l.Page}{(row.Anchor is null ? "" : " / " + row.Anchor)} : {l.Before} -> {l.After}; {l.Scroll}; {l.Found}");
            }
            // The picture of what Enter lands on.
            if (first is not null && first.Anchor != SettingsIndex.Quit)
            {
                c.Svm.Search.Clear();
                await Settle();
                c.Svm.Search.Query = q;
                await Settle();
                await SelectUnitTop(c, first.Page, SettingsIndex.Find(first.Page, first.Anchor)?.Subsection);
                if (first.Page == SettingsPage.Themes && first.Anchor == SettingsIndex.Paste) c.Svm.Themes.IsStudioMode = false;
                c.Select.Invoke(c.Sw, [first.Page, first.Anchor]);
                await Settle2();
                PassPng(c.Root, $"search/{Slug(q)}-enter-lands.png");
            }
        }
        c.Svm.Search.Clear();
        PassWrite("search.txt", sb.ToString());
    }

    // ---- Show me and deep links ----

    static async Task PassLinks(PassCtx c)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Where every in-app way into Settings lands. 'top view' lists, for a page-only link, the page's sections that are visible without scrolling at 800 and at 600 high.");

        async Task<string> TopView(SettingsPage page, int height)
        {
            var sectionsIn = new List<string>();
            var sectionsBelow = new List<string>();
            foreach (var e in SettingsIndex.EntriesOn(page).Where(e => e.Anchor != SettingsIndex.Quit))
            {
                var l = await Probe(c, page, e.Anchor, height);
                (l.Before.StartsWith("fully") || l.Before.StartsWith("top in view") ? sectionsIn : sectionsBelow).Add($"{e.Title} ({e.Anchor}: {l.Before})");
            }
            return $"  in view at {height}: {(sectionsIn.Count == 0 ? "-" : string.Join("; ", sectionsIn))}\n  not in view at {height}: {(sectionsBelow.Count == 0 ? "-" : string.Join("; ", sectionsBelow))}";
        }

        // Full anchor table first.
        var table = new StringBuilder();
        table.AppendLine("Every anchor in the index, with the page open at its top and the window 780 wide: is the section visible without scrolling, and what the jump does.");
        table.AppendLine("page / anchor | height | at the page top | after SelectPage(page, anchor) | scroll | marker");
        foreach (int h in new[] { 800, 600 })
            foreach (var e in SettingsIndex.Anchors.Where(a => a.Anchor != SettingsIndex.Quit))
            {
                var l = await Probe(c, e.Page, e.Anchor, h);
                table.AppendLine($"{l.Page} / {e.Anchor} ({e.Title}) | {h} | {l.Before} | {l.After} | {l.Scroll} | {l.Found}");
            }
        PassWrite("anchors-landing.txt", table.ToString());

        sb.AppendLine();
        sb.AppendLine("## What's New cards (Show me opens Settings at the card's page and anchor; CARD-LANDING is the target after the jump)");
        foreach (var card in WhatsNewCards.Table)
        {
            sb.AppendLine($"- {card.Version} {card.Id}: \"{card.Title}\" -> {(card.Destination == SettingsPage.Widget ? "the widget (not Settings)" : SettingsNames.PageTitle(card.Destination) + " / " + card.Anchor)}");
            sb.AppendLine($"  body: {card.Body}");
            if (card.Destination != SettingsPage.Widget)
            {
                // The same parse the --settings switch runs.
                var link = SettingsIndex.ParseLink($"{SettingsIndex.PageId(card.Destination)}#{card.Anchor}")!;
                var l8 = await Probe(c, link.Page, link.Anchor, 800);
                var l6 = await Probe(c, link.Page, link.Anchor, 600);
                sb.AppendLine($"  CARD-LANDING {card.Id} at 800: {l8.After}; {l8.Found}");
                sb.AppendLine($"  at 600: {l6.After}");
            }
        }
        sb.AppendLine();
        sb.AppendLine("## Welcome tour cards (Show me; TOUR-LANDING is the target after the jump)");
        foreach (var (set, cards) in new[] { ("tour", WelcomeTour.Table), ("tour, Just the Desk", WelcomeTour.DeskOnly) })
            foreach (var t in cards)
            {
                sb.AppendLine($"- {set} step {t.Step} {t.Id}: \"{t.Card.Title}\" [{t.ShowMeTitle}] -> {(t.Card.Destination == SettingsPage.Widget ? "the widget (not Settings)" : SettingsNames.PageTitle(t.Card.Destination) + " / " + t.Card.Anchor)}");
                sb.AppendLine($"  body: {t.Card.Body}");
                if (t.Card.Destination != SettingsPage.Widget)
                {
                    var link = SettingsIndex.ParseLink($"{SettingsIndex.PageId(t.Card.Destination)}#{t.Card.Anchor}")!;
                    var l8 = await Probe(c, link.Page, link.Anchor, 800);
                    sb.AppendLine($"  TOUR-LANDING {set} {t.Id} at 800: {l8.After}; {l8.Found}");
                }
            }
        sb.AppendLine();
        sb.AppendLine("## In-app entry points that open Settings (from the source; each is a SelectPage call, so each is reproduced here)");
        var entries = new (string Where, SettingsPage Page, string? Anchor)[]
        {
            ("Tray menu / widget menu: Open Settings (first open this session)", SettingsPage.General, null),
            ("Meter card right-click: Alerts link (TierCard)", SettingsPage.Alerts, SettingsIndex.MenuAnchor(SettingsPage.Alerts)),
            ("Account menu (widget chip, tray): the Accounts link", SettingsPage.Accounts, SettingsIndex.MenuAnchor(SettingsPage.Accounts)),
            ("Desk right-click: Edit messages", SettingsPage.DeskMessage, SettingsIndex.MenuAnchor(SettingsPage.DeskMessage)),
            ("Desk right-click: Desk settings", SettingsPage.Desk, SettingsIndex.MenuAnchor(SettingsPage.Desk)),
            ("Desk right-click on a Launcher button: Launcher settings…", SettingsPage.Launcher, SettingsIndex.MenuAnchor(SettingsPage.Launcher)),
            ("Launcher piece: a missing app's click (App.OnLauncherMissing)", SettingsPage.Launcher, SettingsIndex.MenuAnchor(SettingsPage.Launcher)),
        };
        foreach (var (where, page, anchor) in entries)
        {
            var l = await Probe(c, page, anchor, 800);
            var l6 = await Probe(c, page, anchor, 600);
            sb.AppendLine($"- {where} -> {SettingsNames.PageTitle(page)}{(anchor is null ? "" : " / " + anchor)}");
            if (anchor is null)
                sb.AppendLine(await TopView(page, 800));
            sb.AppendLine($"  at 800: {l.Before} -> {l.After}; {l.Scroll}; {l.Found}");
            sb.AppendLine($"  at 600: {l6.Before} -> {l6.After}; {l6.Scroll}");
        }
        sb.AppendLine();
        sb.AppendLine("## Other jumps inside Settings");
        sb.AppendLine("- Search result Enter or click: SelectPage(page, anchor), covered in search.txt.");
        sb.AppendLine("- Claude Usage calendar day click: opens the Sessions ledger scoped to that day (needs a click on the calendar; live pass).");
        PassWrite("deeplinks.txt", sb.ToString());
    }

    // ---- off-screen element renders: the menu check mark and the Quit question ----

    static BitmapSource RenderAt(FrameworkElement el, double scale, string rel)
    {
        el.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        el.Arrange(new Rect(el.DesiredSize));
        el.UpdateLayout();
        int w = (int)Math.Ceiling(el.ActualWidth * scale), h = (int)Math.Ceiling(el.ActualHeight * scale);
        var rtb = new RenderTargetBitmap(Math.Max(1, w), Math.Max(1, h), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        rtb.Render(el);
        string path = Path.Combine(Out, "settings-pass", rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using (var f = File.Create(path)) enc.Save(f);
        Log.Add($"png settings-pass/{rel.Replace('\\', '/')} {w}x{h}");
        return rtb;
    }

    static byte[] Pixels(BitmapSource b)
    {
        var px = new byte[b.PixelWidth * b.PixelHeight * 4];
        b.CopyPixels(px, b.PixelWidth * 4, 0);
        return px;
    }

    /// <summary>A leaf MenuItem checked and unchecked, in a Border (never a popup, which can take activation), at 4x.
    /// The mark is the difference between the two renders; the first glyph is the leftmost ink of the unchecked one.</summary>
    static void PassMenuCheck(bool light)
    {
        string tag = light ? "light" : "dark";
        const double S = 4;
        BitmapSource Host(bool isChecked)
        {
            var item = new MenuItem { Header = SettingsNames.CompactMode, IsCheckable = true, IsChecked = isChecked, Width = 220 };
            var host = new Border { Padding = new Thickness(6), Child = item };
            host.SetResourceReference(Border.BackgroundProperty, "Sanduhr.Brush.GlassChrome");
            return RenderAt(host, S, $"probe/menucheck-{tag}-{(isChecked ? "checked" : "unchecked")}.png");
        }
        var on = Host(true);
        var off = Host(false);
        var a = Pixels(on); var b = Pixels(off);
        int w = on.PixelWidth, h = on.PixelHeight;
        int mL = w, mR = -1, mT = h, mB = -1, gL = w;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                int d = Math.Abs(a[i] - b[i]) + Math.Abs(a[i + 1] - b[i + 1]) + Math.Abs(a[i + 2] - b[i + 2]);
                if (d > 24) { mL = Math.Min(mL, x); mR = Math.Max(mR, x); mT = Math.Min(mT, y); mB = Math.Max(mB, y); }
            }
        // The unchecked render has the header text only; its background is the host's corner pixel.
        int bgI = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                int d = Math.Abs(b[i] - b[bgI]) + Math.Abs(b[i + 1] - b[bgI + 1]) + Math.Abs(b[i + 2] - b[bgI + 2]);
                if (d > 60) gL = Math.Min(gL, x);
            }
        var sb = new StringBuilder();
        sb.AppendLine($"Menu check mark, {tag} theme, 4x. Bitmap {w}x{h} px; the host Border has 6 DIP padding, so the item's left edge is at 24 px.");
        if (mR < 0)
            sb.AppendLine("NO MARK INK FOUND: the checked and unchecked renders are identical.");
        else
        {
            sb.AppendLine($"mark ink box (DIP, from the item's left edge): x {(mL / S) - 6:0.00} to {((mR + 1) / S) - 6:0.00}, y {mT / S:0.00} to {(mB + 1) / S:0.00}; {(mR + 1 - mL) / S:0.00} wide, {(mB + 1 - mT) / S:0.00} high");
            sb.AppendLine($"first glyph of the header starts at {(gL / S) - 6:0.00} DIP; clear space between mark and glyph {(gL - (mR + 1)) / S:0.00} DIP");
        }
        PassWrite($"probe/menucheck-{tag}.txt", sb.ToString());
    }

    /// <summary>The Quit question (ThemedDialog, Quit / Cancel) shown off screen without activation and rendered at 2x;
    /// the labels' contrast is measured from the pixels, and the primary button's from each built-in accent.</summary>
    static void PassDialog(PassCtx c, bool light)
    {
        string tag = light ? "light" : "dark";
        const double S = 2;
        var type = typeof(Sanduhr.App.Views.ThemedDialog);
        var dlg = (Window)Activator.CreateInstance(type, BindingFlags.NonPublic | BindingFlags.Instance, null,
            [SettingsNames.QuitSanduhr, SettingsViewModel.QuitQuestion, MessageBoxButton.YesNo, Sanduhr.App.Views.ThemedDialogKind.Info, false], null)!;
        var primary = (Button)dlg.FindName("PrimaryButton");
        var secondary = (Button)dlg.FindName("SecondaryButton");
        primary.Content = "Quit"; secondary.Content = "Cancel";
        OffScreen(dlg);
        new WindowInteropHelper(dlg).EnsureHandle();
        NeverActivate(dlg);
        dlg.Show();
        dlg.Left = -20000; dlg.Top = -20000;
        dlg.UpdateLayout();
        var root = (FrameworkElement)dlg.Content;
        var bmp = RenderAt(root, S, $"probe/quit-dialog-{tag}.png");
        GuardForeground(c);
        var px = Pixels(bmp);
        int bw = bmp.PixelWidth;

        (double Ratio, string Fg, string Bg) Measure(Button btn)
        {
            var r = btn.TransformToAncestor(root).TransformBounds(new Rect(btn.RenderSize));
            int x0 = (int)(r.Left * S) + 3, x1 = (int)(r.Right * S) - 3, y0 = (int)(r.Top * S) + 3, y1 = (int)(r.Bottom * S) - 3;
            var counts = new Dictionary<int, int>();
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    int i = (y * bw + x) * 4;
                    int k = (px[i + 2] << 16) | (px[i + 1] << 8) | px[i];
                    counts[k] = counts.GetValueOrDefault(k) + 1;
                }
            int bgK = counts.OrderByDescending(p => p.Value).First().Key;
            Color bg = Color.FromRgb((byte)(bgK >> 16), (byte)(bgK >> 8), (byte)bgK);
            Color fg = bg; double best = 0;
            foreach (var k in counts.Keys)
            {
                var col = Color.FromRgb((byte)(k >> 16), (byte)(k >> 8), (byte)k);
                double rr = Ratio(col, bg);
                if (rr > best) { best = rr; fg = col; }
            }
            return (best, fg.ToString(), bg.ToString());
        }
        var p = Measure(primary); var s = Measure(secondary);
        var sb = new StringBuilder();
        sb.AppendLine($"Quit question (ThemedDialog), {tag} theme, 2x. Measured from the rendered pixels (strongest ink against the button's own fill; antialiasing can only understate it).");
        sb.AppendLine($"primary \"Quit\": text {p.Fg} on {p.Bg}: {p.Ratio:0.0}:1");
        sb.AppendLine($"secondary \"Cancel\": text {s.Fg} on {s.Bg}: {s.Ratio:0.0}:1");
        sb.AppendLine();
        sb.AppendLine("Primary button (#0d0d0d text) on each theme's accent, computed:");
        var themes = ThemeCatalog.BuiltInOrder.Select(k => (k, ThemeCatalog.BuiltIns[k].Accent)).ToList();
        var j = JsonNode.Parse(LightThemeJson)!;
        themes.Add(("harness-light", j["accent"]!.GetValue<string>()));
        foreach (var (k, accent) in themes)
            sb.AppendLine($"  {k,-28} accent {accent}  {Ratio(Hex("#0d0d0d"), Hex(accent)):0.0}:1");
        PassWrite($"probe/quit-dialog-{tag}.txt", sb.ToString());
        dlg.Close();
    }

    // ---- contrast ----

    static double Lum(Color c)
    {
        static double L(byte v) { double s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * L(c.R) + 0.7152 * L(c.G) + 0.0722 * L(c.B);
    }

    static double Ratio(Color a, Color b)
    {
        double x = Lum(a), y = Lum(b);
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    static Color Hex(string s) => (Color)ColorConverter.ConvertFromString(s);

    static Color Over(Color top, double alpha, Color under) => Color.FromRgb(
        (byte)Math.Round(top.R * alpha + under.R * (1 - alpha)),
        (byte)Math.Round(top.G * alpha + under.G * (1 - alpha)),
        (byte)Math.Round(top.B * alpha + under.B * (1 - alpha)));

    static void PassContrast()
    {
        var defs = new List<(string Key, ThemeDefinition Def)>();
        foreach (var k in ThemeCatalog.BuiltInOrder)
            defs.Add((k, ThemeCatalog.BuiltIns[k]));
        var j = JsonNode.Parse(LightThemeJson)!;
        string S(string f) => j[f]!.GetValue<string>();
        defs.Add(("harness-light (the --light theme of this harness)", new ThemeDefinition
        {
            Name = "Harness Light", Bg = S("bg"), Glass = S("glass"), GlassOnMica = S("glass_on_mica"), TitleBg = S("title_bg"),
            Border = S("border"), Text = S("text"), TextSecondary = S("text_secondary"), TextDim = S("text_dim"),
            TextMuted = S("text_muted"), Accent = S("accent"), BarBg = S("bar_bg"), FooterBg = S("footer_bg"),
            PaceMarker = S("pace_marker"), Sparkline = S("sparkline"), GlassAlpha = j["glass_alpha"]!.GetValue<double>(),
        }));
        var sb = new StringBuilder();
        sb.AppendLine("WCAG contrast ratios of Settings text brushes on the surfaces Settings paints (ThemeContrast.SettingsSurfaces, slice 8):");
        sb.AppendLine("the page card (Glass), a well inside a page (Bg) and the sidebar (TitleBg at the chrome alpha over Bg). The window is opaque: no Mica.");
        sb.AppendLine($"Thresholds: {ThemeContrast.TextMin} for body text (TextDim's floor is {ThemeContrast.DimFloor}), {ThemeContrast.NonTextMin} for indicators. Marked LOW below 4.5.");
        foreach (var (key, d) in defs)
        {
            var surfaces = ThemeContrast.SettingsSurfaces(d);
            sb.AppendLine();
            sb.AppendLine($"## {key} ({string.Join("; ", surfaces.Select(s => $"{s.Name} {ThemeContrast.Hex(s.Color)}"))})");
            foreach (var (name, hex) in new[] { ("Text", d.Text), ("TextSecondary", d.TextSecondary), ("TextDim", d.TextDim), ("TextMuted", d.TextMuted), ("Accent", d.Accent), ("PaceMarker", d.PaceMarker), ("SelectedLabel", ThemeContrast.SelectedLabel(d)) })
            {
                var parts = surfaces.Select(s => (s.Name, R: ThemeContrast.Ratio(hex, s.Color))).ToList();
                double worst = parts.Min(p => p.R);
                sb.AppendLine($"  {name,-14} {hex}  " + string.Join("  ", parts.Select(p => $"{p.Name} {p.R,5:0.0}")) + (worst < 4.5 ? "  LOW" : ""));
            }
            sb.AppendLine($"  Border vs page {ThemeContrast.Ratio(d.Border, surfaces[0].Color):0.0} (non-text, wants 3.0); Accent vs page {ThemeContrast.Ratio(d.Accent, surfaces[0].Color):0.0} (focus ring and the selected row's bar, wants 3.0)");
        }
        PassWrite("contrast.txt", sb.ToString());
    }
}
