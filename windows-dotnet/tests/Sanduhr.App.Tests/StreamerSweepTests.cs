using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.App.Updates;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Settings v2, slice 7: the Done line, as a sweep of every Settings page and every surface outside it that a test can draw.
/// Every real name in the profile starts with "qx" (and nothing in Sanduhr's own copy does: <c>git grep -I -n -i qx -- windows-dotnet/src</c>
/// prints nothing), so "no real name appears" is "no qx in any visible text, spoken name or help text". The sweep also asserts that each
/// expected codename shows, so a page that renders nothing fails it instead of passing it.
/// </summary>
[Collection("SettingsWindow")]
public sealed class StreamerSweepTests : AppWindowFixture
{
    private const string Sentinel = "qx";
    private const string Shown = "Qxbeta";   // the one allowlisted name
    private const string Endpoint = "https://qxhost.example/ingest";

    // Edge shapes (Task 2): a name with a space, a name with an apostrophe, an email in a publish error.
    private const string SpaceProject = "qx proj";
    private const string ApostropheProject = "qx o'brien";
    private const string PublishError = "Rejected (400): qxuser@qxmail.com is not allowed";

    private sealed record Rig(SettingsWindow Window, SettingsViewModel Svm, WidgetViewModel Widget, NoActivate Guard, SettingsStore Store);

    private static void WriteTranscript(string path, string cwd, string skill)
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

    /// <summary>The widget's session-log reader joins folder names to the real user profile; point it at the scratch one so the sweep reads
    /// its own transcripts and nothing on the PC.</summary>
    private static void PointReaderAt(WidgetViewModel widget, string home) =>
        typeof(CcLogReader).GetField("_homeDir", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(widget.CcReader, home);

    private Rig Open()
    {
        var settings = JsonSerializer.Serialize(new
        {
            statusLine = new { type = "command", command = $"powershell -File \"{Path.Combine(Profile, ".claude-qxwork", "statusline.ps1")}\"" },
            enabledPlugins = new Dictionary<string, bool> { ["qxplugin@qxmarket"] = true },
        });
        Home(".claude-qxwork", settings);
        Home(".claude-qx home");
        // Three folders: ~\.claude (the fixture's), ~\.claude-qxwork and ~\.claude-qx home. Two accounts.
        var (widget, _) = ScratchWidget("Qxalpha", Shown);
        PointReaderAt(widget, Profile);
        widget.SetAccountFolder("Qxalpha", Path.Combine(Profile, ".claude-qxwork"));

        // Projects and skills come from the session logs: one transcript per project, three edge shapes.
        var projects = Path.Combine(Profile, ".claude-qxwork", "projects");
        WriteTranscript(Path.Combine(projects, "p1", "s1.jsonl"), Path.Combine(Profile, "qxproject"), "qxskill");
        WriteTranscript(Path.Combine(projects, "p2", "s2.jsonl"), Path.Combine(Profile, SpaceProject), "qxspace-skill");
        WriteTranscript(Path.Combine(projects, "p3", "s3.jsonl"), Path.Combine(Profile, ApostropheProject), "qxapostrophe-skill");

        var paths = ScratchPaths();
        var store = new SettingsStore(paths);
        store.SavePublishEndpointUrl(Endpoint);
        store.SavePublishAuthScheme(PublishAuthScheme.None);
        store.SavePublishRoots(new Dictionary<string, bool> { [".claude-qxwork"] = true });
        store.SavePublishAttempt(DateTimeOffset.Now, PublishError, null);
        var service = new UsagePublishService(store, new CcLogReader(Profile, () => [".claude-qxwork", ".claude-qx home"]), paths,
            new PublishTokenStore(new MemoryCredentials()), () => Profile);
        widget.AttachPublishService(service);

        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask,
            checkForUpdatesNow: () => Task.FromResult(new UpdateProbeResult(UpdateProbeStatus.UpToDate)), isStoreBuild: false);
        svm.WeatherPage.Picked = WeatherPlace.Create("Qxcity", "Texas", "United States", 32.75, -97.33);
        svm.WeatherPage.ShowResultsForTests([WeatherPlace.Create("Qxtown", null, "Germany", 50.1, 8.7)]);
        var window = new SettingsWindow(svm);
        var guard = NoActivate.Show(window);
        // The flip happens with the window open, as it does from the tray, the widget or Privacy: the pages rebuild their text on their own.
        widget.Streamer.SetOn(true);
        widget.Streamer.SetShown(CodenameCategory.Account, Shown, true);
        Pump();
        Pump();
        svm.Desk.CiRunnersStatus = App.RunnerStatusText(new RunnerStatus(RunnerStatusKind.Watching, ["qxrunner-1"]));   // App sets this on a change
        return new Rig(window, svm, widget, guard, store);
    }

    private static void Settle(Window window, NoActivate guard)
    {
        window.UpdateLayout();
        Pump();
        window.UpdateLayout();
        Pump();
        guard.Check();
    }

    private static void Await(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        {
            Pump();
            Thread.Sleep(10);
        }
        Assert.True(task.IsCompleted, "a read never finished");
        task.GetAwaiter().GetResult();
    }

    /// <summary>Every piece of text that is drawn under <paramref name="root"/>: TextBlocks (and their runs), text boxes, string content,
    /// tooltips and list items as their ToString. Elements that are not visible draw nothing, so they and what is under them are skipped.</summary>
    private static IEnumerable<string> TextsOf(DependencyObject root)
    {
        foreach (var t in TextsOfCore(root)) yield return t;
    }

    /// <summary>Where each text was read, for the failure message (a leak names its element, not just its words).</summary>
    private static readonly Dictionary<string, string> Origins = new(StringComparer.Ordinal);

    private static IEnumerable<string> Ancestors(DependencyObject d)
    {
        for (var p = VisualTreeHelper.GetParent(d); p is not null; p = VisualTreeHelper.GetParent(p))
            yield return p.GetType().Name + (p is FrameworkElement { Name.Length: > 0 } fe ? $"'{fe.Name}'" : "");
    }

    private static string Note(string? text, DependencyObject from, string how)
    {
        text ??= "";
        if (text.Contains(Sentinel, StringComparison.OrdinalIgnoreCase) || text.Contains(Environment.UserName, StringComparison.OrdinalIgnoreCase))
            Origins[text] = $"{how} on {from.GetType().Name}" + (from is FrameworkElement { Name.Length: > 0 } fe ? $" '{fe.Name}'" : "")
                + " in " + string.Join(" < ", Ancestors(from).Take(6)) + (from is FrameworkElement { DataContext: { } dc } ? $" (data {dc.GetType().Name})" : "");
        return text;
    }

    private static IEnumerable<string> TextsOfCore(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is UIElement { Visibility: not Visibility.Visible }) continue;
            switch (child)
            {
                case TextBlock t:
                    yield return Note(t.Text, t, "text");
                    foreach (var run in t.Inlines.OfType<Run>()) yield return Note(run.Text, t, "run");
                    break;
                case TextBox b:
                    yield return Note(b.Text, b, "textbox");
                    break;
                case ContentControl c when c.Content is string s:
                    yield return Note(s, c, "content");
                    break;
                case ItemsControl list:
                    foreach (var item in list.Items) yield return Note(item?.ToString(), list, $"item {item?.GetType().Name}");
                    break;
            }
            if (child is FrameworkElement fe)
            {
                if (fe.ToolTip is string tip) yield return Note(tip, fe, "tooltip");
                else if (fe.ToolTip is ToolTip { Content: string tipContent }) yield return Note(tipContent, fe, "tooltip");
                yield return Note(AutomationProperties.GetName(fe), fe, "AutomationProperties.Name");
                yield return Note(AutomationProperties.GetHelpText(fe), fe, "AutomationProperties.HelpText");
            }
            foreach (var deeper in TextsOf(child)) yield return deeper;
        }
    }

    /// <summary>What a screen reader gets: names and help text of every peer under <paramref name="root"/>.</summary>
    private static IEnumerable<string> SpokenOf(FrameworkElement root)
    {
        NamesOf(root);   // realizes every list item first
        foreach (var peer in Walk(UIElementAutomationPeer.CreatePeerForElement(root)))
        {
            string name, help;
            try { name = peer.GetName() ?? ""; }
            catch (NullReferenceException) { name = ""; }   // a stale item peer
            try { help = peer.GetHelpText() ?? ""; }
            catch (NullReferenceException) { help = ""; }
            NoteSpoken(name, peer, "name");
            NoteSpoken(help, peer, "help text");
            yield return name;
            yield return help;
        }
    }

    private static void NoteSpoken(string text, AutomationPeer peer, string how)
    {
        if (text.Contains(Sentinel, StringComparison.OrdinalIgnoreCase) || text.Contains(Environment.UserName, StringComparison.OrdinalIgnoreCase))
            Origins[text] = $"spoken {how} of {peer.GetType().Name}";
    }

    private static List<string> Leaks(IEnumerable<string> texts) =>
        [.. texts.Where(t => t is not null)
            .Select(t => t.Replace(Shown, "", StringComparison.Ordinal))
            .Select(t => new { Text = t, Rest = PublicCopy.Aggregate(t, (acc, p) => acc.Replace(p, "", StringComparison.OrdinalIgnoreCase)) })
            .Where(x => x.Rest.Contains(Sentinel, StringComparison.OrdinalIgnoreCase)
                        || x.Rest.Contains(Environment.UserName, StringComparison.OrdinalIgnoreCase))
            .Select(x => Origins.TryGetValue(x.Text, out var o) ? $"{x.Text}   <{o}>" : x.Text)
            .Distinct()];

    /// <summary>Sanduhr's own public copy, which names its author. The owner's Windows user name ("estev") is a prefix of his public name, so
    /// the user-name check would flag the copyright line and the project's own links on his machine; they are not a real name of the profile
    /// under test.</summary>
    private static readonly string[] PublicCopy = ["Estevan Hernandez", "estevanhernandez-stack-ed"];

    private static TabItem Tab(SettingsWindow window, SettingsPage page) =>
        ((TabControl)window.FindName("Tabs")).Items.OfType<TabItem>().Single(t => (string)t.Tag == page.ToString());

    private static void AssertSeen(HashSet<string> seen, string code, string where) =>
        Assert.True(seen.Any(t => t.Contains(code, StringComparison.Ordinal)), $"\"{code}\" never showed on {where}: the sweep saw nothing it should have");

    /// <summary>Done (spec, slice 7), Settings: every page, every visible text, spoken name and help text.</summary>
    [Fact]
    public void No_real_name_appears_on_any_settings_page_but_the_allowlisted_one() => AppHost.Run(() =>
    {
        var r = Open();
        try
        {
            var leaks = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int walked = 0;
            foreach (var page in SettingsNames.Pages)
            {
                r.Window.SelectPage(page, null);
                Settle(r.Window, r.Guard);
                if (page == SettingsPage.ClaudeCode)
                    foreach (var card in r.Svm.ClaudeCodePage.Folders) card.HoldsOpen = true;   // the folder report too
                if (page == SettingsPage.ClaudeUsage)
                {
                    var tab = r.Svm.ClaudeCode;
                    // Overview reads the session logs, so projects and skills render; Trends and Sessions read the archive, which this
                    // test cannot fill with a vault, so they take the same names through the views' own entry points.
                    tab.Section = "Overview";
                    Await(tab.Overview.RefreshAsync());
                    Settle(r.Window, r.Guard);
                    CollectFrom(r, page, leaks, seen);
                    tab.Trends.ShowTopForTests([("qxproject", 7000L), (SpaceProject, 900L), (ApostropheProject, 80L)]);
                    tab.Section = "Trends";
                    Settle(r.Window, r.Guard);
                    CollectFrom(r, page, leaks, seen);
                    tab.Ledger.PresentForTests(
                    [
                        Session("qxproject", "C--Users-qxuser-qxproject", Path.Combine(Profile, "qxproject"), "u1", "qxskill"),
                        Session(SpaceProject, "C--Users-qxuser-qx-proj", Path.Combine(Profile, SpaceProject), "u2", "qxspace-skill"),
                        Session(ApostropheProject, "C--Users-qxuser-qx-o-brien", Path.Combine(Profile, ApostropheProject), "u3", "qxapostrophe-skill"),
                    ]);
                    foreach (var row in tab.Ledger.Rows) row.IsExpanded = true;
                    tab.Section = "Sessions";
                    Settle(r.Window, r.Guard);
                    CollectFrom(r, page, leaks, seen);
                    tab.Section = SettingsIndex.MetersView;
                    Settle(r.Window, r.Guard);
                }
                CollectFrom(r, page, leaks, seen);
                walked++;
            }
            Assert.True(leaks.Count == 0, "Real names on screen:\n" + string.Join("\n", leaks));
            Assert.Equal(SettingsNames.Pages.Count, walked);

            // Not vacuous: each codename shows where its page shows the name, and the allowlisted name shows as it is.
            var mask = StreamerMode.Mask;
            AssertSeen(seen, mask.Name(CodenameCategory.Account, "Qxalpha"), "Accounts");
            AssertSeen(seen, mask.Folder(Path.Combine(Profile, ".claude-qxwork")), "Claude Code");
            AssertSeen(seen, mask.Folder(Path.Combine(Profile, ".claude-qx home")), "Claude Code");
            AssertSeen(seen, mask.Folder(Path.Combine(Profile, ".claude")), "Claude Code");
            AssertSeen(seen, mask.Name(CodenameCategory.Runner, "qxrunner-1"), "Watchers");
            AssertSeen(seen, mask.Name(CodenameCategory.City, r.Svm.WeatherPage.Picked!.Label), "Weather");
            AssertSeen(seen, mask.Name(CodenameCategory.City, WeatherPlace.Create("Qxtown", null, "Germany", 50.1, 8.7).Label), "Weather (places found)");
            AssertSeen(seen, mask.Name(CodenameCategory.Host, "qxhost.example"), "Publish usage");
            foreach (var project in new[] { "qxproject", SpaceProject, ApostropheProject })
                AssertSeen(seen, mask.Name(CodenameCategory.Project, project), "Claude Usage");
            AssertSeen(seen, mask.Name(CodenameCategory.Skill, "qxskill"), "Claude Usage");
            AssertSeen(seen, "Rejected (400)", "Publish usage (the error with an email in it)");
            AssertSeen(seen, Shown, "Accounts");
            AssertSeen(seen, mask.AddOn("qxplugin@qxmarket").Split('@')[0], "Mods & Widgets");
        }
        finally { r.Window.Close(); r.Widget.Dispose(); }
    });

    private void CollectFrom(Rig r, SettingsPage page, List<string> leaks, HashSet<string> seen)
    {
        var content = (FrameworkElement)Tab(r.Window, page).Content;
        var texts = TextsOf(content).Concat(SpokenOf(content)).ToList();
        leaks.AddRange(Leaks(texts).Select(l => $"{page}: {l}"));
        foreach (var t in texts) seen.Add(t);
    }

    private static VaultSessionInfo Session(string project, string projectKey, string cwd, string uuid, string skill) =>
        new(uuid, ".claude-qxwork", projectKey, project, cwd,
            DateTimeOffset.UtcNow.AddHours(-3), DateTimeOffset.UtcNow.AddHours(-2),
            600, new Dictionary<string, long> { ["claude-fable-5"] = 600 },
            new Dictionary<string, long> { [skill] = 50L },
            new Dictionary<string, VaultDayBucket> { [DateTime.Now.ToString("yyyy-MM-dd")] = new() { Total = 600 } },
            null, 0, 0);

    /// <summary>Done, outside Settings: the widget with its context menu and chip, the tray (menu item through its click handler, then the
    /// tooltip), both account menus, the Desk's Claude line, runner cards, the weather name and the Launcher piece.</summary>
    [Fact]
    public void No_real_name_appears_on_the_widget_the_tray_or_the_desk() => AppHost.Run(() =>
    {
        var r = Open();
        MainWindow? main = null;
        try
        {
            var widget = r.Widget;
            var texts = new List<string> { widget.AccountSwitcherText, widget.AccountSwitcherName };
            var menu = new MenuItem();
            AccountMenuBuilder.PopulateSwitchList(menu.Items, widget, includeAdd: true, includeManage: true);
            texts.AddRange(menu.Items.OfType<MenuItem>().Select(m => m.Header?.ToString() ?? ""));
            texts.Add(widget.CurrentDeskUsage.Account ?? "");
            var runner = new Watcher
            {
                Id = "r:k:build", Source = WatcherSource.Runner, Title = "qxrunner-1 · build", Short = "build", Runner = "qxrunner-1", RunnerKey = "k", Job = "build", State = WatcherState.Running, Started = DateTimeOffset.UnixEpoch, Touched = DateTimeOffset.UnixEpoch,
            };
            var (watchers, weather) = DeskController.StreamerFrameParts([runner], WeatherPlace.Create("Qxcity", "Texas", "US", 1, 1), "");
            texts.AddRange(watchers.Select(w => w.Title));
            texts.Add(weather);
            Assert.Empty(Leaks(texts));
            Assert.Contains(SettingsNames.StreamerIndicatorName, widget.AccountSwitcherName);

            // The widget itself: its chip, its spoken names and its right-click menu.
            main = new MainWindow { DataContext = widget };
            var guard = NoActivate.Show(main);
            Settle(main, guard);
            var drawn = TextsOf(main).Concat(SpokenOf(main)).ToList();
            Assert.Empty(Leaks(drawn));
            Assert.Contains(drawn, t => t.Contains(SettingsNames.StreamerIndicatorName, StringComparison.Ordinal));
            Assert.Contains(drawn, t => t.Contains(StreamerMode.Mask.Name(CodenameCategory.Account, "Qxalpha"), StringComparison.Ordinal));

            // The widget's streamer toggle, through its real click handler: off, then on again.
            var contextMenu = WidgetMenu(main);
            var toggle = contextMenu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, SettingsNames.StreamerMenuItemHeader));
            Assert.True(widget.Streamer.On);
            contextMenu.PlacementTarget = main.RootBorder;
            contextMenu.IsOpen = true;   // Opened syncs the check from the switch, as it does for a person
            Pump();
            Assert.True(toggle.IsChecked);
            Click(toggle);
            Assert.False(widget.Streamer.On, $"checked now {toggle.IsChecked}, enabled {toggle.IsEnabled}, vm {main.DataContext?.GetType().Name}");
            Settle(main, guard);
            Assert.Contains("Qxalpha", widget.AccountSwitcherText);
            contextMenu.IsOpen = false;
            contextMenu.IsOpen = true;
            Pump();
            Assert.False(toggle.IsChecked);
            Click(toggle);
            Assert.True(widget.Streamer.On);
            contextMenu.IsOpen = false;
            Settle(main, guard);
            Assert.Empty(Leaks(TextsOf(main).Concat(SpokenOf(main))));
            var accounts = contextMenu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "Accounts"));
            AccountMenuBuilder.PopulateSwitchList(accounts.Items, widget, includeAdd: true, includeManage: true);
            Assert.Empty(Leaks(accounts.Items.OfType<MenuItem>().Select(m => m.Header?.ToString() ?? "")));
        }
        finally { main?.Close(); r.Window.Close(); r.Widget.Dispose(); }
    });

    private static ContextMenu WidgetMenu(MainWindow main)
    {
        typeof(MainWindow).GetMethod("EnsureWidgetContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null);
        var menu = main.RootBorder.ContextMenu;
        Assert.NotNull(menu);
        return menu!;
    }

    /// <summary>A click as the user makes it on a checkable item: the check flips, then Click is raised and the handler reads the check.</summary>
    private static void Click(MenuItem item)
    {
        item.IsChecked = !item.IsChecked;
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item));
    }

    /// <summary>Task 7: the tray's streamer item, its real handler, and App's tooltip text after the change.</summary>
    [Fact]
    public void The_trays_streamer_item_flips_the_switch_and_the_tooltip_follows() => AppHost.Run(() =>
    {
        var r = Open();
        var app = (Sanduhr.App.App)Application.Current;
        const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(Sanduhr.App.App);
        var vmField = type.GetField("_vm", F)!;
        var trayField = type.GetField("_tray", F)!;
        var oldVm = vmField.GetValue(app);
        var oldTray = trayField.GetValue(app);
        Hardcodet.Wpf.TaskbarNotification.TaskbarIcon? tray = null;
        Action? changed = null;
        try
        {
            r.Widget.Streamer.SetOn(false);
            vmField.SetValue(app, r.Widget);
            type.GetMethod("SetupTray", F)!.Invoke(app, null);
            tray = (Hardcodet.Wpf.TaskbarNotification.TaskbarIcon)trayField.GetValue(app)!;
            changed = (Action)Delegate.CreateDelegate(typeof(Action), app, type.GetMethod("OnStreamerChanged", F)!);
            r.Widget.Streamer.Changed += changed;
            var item = tray.ContextMenu!.Items.OfType<MenuItem>().Single(i => Equals(i.Header, SettingsNames.StreamerMenuItemHeader));
            var onTray = type.GetMethod("OnTrayReadingChanged", F)!;
            onTray.Invoke(app, [null]);
            Assert.DoesNotContain(SettingsNames.StreamerTooltipPrefix, tray.ToolTipText);

            tray.ContextMenu.IsOpen = true;   // Opened syncs the check from the switch
            Pump();
            Assert.False(item.IsChecked);
            Click(item);   // on
            Assert.True(r.Widget.Streamer.On);
            Assert.StartsWith(SettingsNames.StreamerTooltipPrefix, tray.ToolTipText);
            Assert.Empty(Leaks([tray.ToolTipText, .. tray.ContextMenu.Items.OfType<MenuItem>().Select(m => m.Header?.ToString() ?? "")]));
            tray.ContextMenu.IsOpen = false;
            tray.ContextMenu.IsOpen = true;
            Pump();
            Assert.True(item.IsChecked);
            Click(item);   // off
            tray.ContextMenu.IsOpen = false;
            Assert.False(r.Widget.Streamer.On);
            Assert.DoesNotContain(SettingsNames.StreamerTooltipPrefix, tray.ToolTipText);
        }
        finally
        {
            if (changed is not null) r.Widget.Streamer.Changed -= changed;
            vmField.SetValue(app, oldVm);
            tray?.Dispose();
            trayField.SetValue(app, oldTray);
            r.Window.Close();
            r.Widget.Dispose();
        }
    });

    /// <summary>The controller fix e8b58c32: an unlabelled Launcher link on the Desk piece, and the name its failure toast carries.</summary>
    [Fact]
    public void An_unlabelled_launcher_link_shows_no_real_host_on_the_desk_or_in_its_failure_toast() => AppHost.Run(() =>
    {
        var r = Open();
        try
        {
            var link = LauncherButton.Create(LauncherKind.Link, "https://qxowner.dev/notes")!;
            var raw = LauncherButton.Create(LauncherKind.App, Path.Combine(Profile, "tools", "tool.exe"), null, null, null)!;
            var model = new LauncherModel(LauncherOrientation.Row, [link, raw]);
            var frame = new DeskLauncherFrame(model, new HashSet<string>(), new Dictionary<string, ImageSource>(),
                new Dictionary<string, string>(), new Dictionary<string, ImageSource>());
            var bounds = new PxRect(0, 0, 1920, 1080);
            var work = new PxRect(0, 0, 1920, 1032);
            var screen = new DeskScreen("TEST", bounds, work, 1.0, true, TaskbarClearance.Insets(bounds, work, [], 1.0));
            var settings = (DeskSettings.Default with { Enabled = true, Layout = "launcher:bl", Font = "Segoe UI" }).Normalized();
            var view = new DeskView(screen, settings, new DeskFonts(["Segoe UI"]));
            view.Update(new DeskFrame { Now = DateTimeOffset.UtcNow, Launcher = frame }, DeskInkMap.Light);
            var island = view.TakeIslands().Single(i => i.Widgets.Contains("launcher"));
            island.Content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            island.Content.Arrange(new Rect(island.Content.DesiredSize));
            island.Content.UpdateLayout();

            var spoken = SpokenOf(island.Content).Concat(TextsOf(island.Content)).ToList();
            Assert.Contains(spoken, t => t.Contains(StreamerMode.Mask.Name(CodenameCategory.Host, "qxowner.dev"), StringComparison.Ordinal));
            Assert.Empty(Leaks(spoken));
            // The toast reads "Couldn't open {name}" with this name.
            Assert.Empty(Leaks([StreamerMode.LauncherName(link, null), StreamerMode.LauncherName(raw, null)]));
        }
        finally { r.Window.Close(); r.Widget.Dispose(); }
    });

    /// <summary>Mods &amp; Widgets (Task 10): the installed plugin's row, spoken name and switch names, at both RowName sites.</summary>
    [Fact]
    public void The_mods_page_draws_the_plugin_by_codename_in_both_lists() => AppHost.Run(() =>
    {
        var r = Open();
        try
        {
            r.Window.SelectPage(SettingsPage.Mods, null);
            Settle(r.Window, r.Guard);
            Pump();
            var content = (FrameworkElement)Tab(r.Window, SettingsPage.Mods).Content;
            var all = TextsOf(content).Concat(SpokenOf(content)).ToList();
            Assert.Empty(Leaks(all));
            var plugin = StreamerMode.Mask.AddOn("qxplugin@qxmarket");
            Assert.Contains(all, t => t.Contains(plugin.Split('@')[0], StringComparison.Ordinal));
        }
        finally { r.Window.Close(); r.Widget.Dispose(); }
    });

    /// <summary>Allowlisted names are the exception, and the switch off shows everything as it was.</summary>
    [Fact]
    public void Off_every_page_shows_the_real_names_again() => AppHost.Run(() =>
    {
        var r = Open();
        try
        {
            r.Widget.Streamer.SetOn(false);
            r.Window.SelectPage(SettingsPage.Accounts, null);
            Settle(r.Window, r.Guard);
            Assert.Contains(TextsOf((DependencyObject)Tab(r.Window, SettingsPage.Accounts).Content), t => t.Contains("Qxalpha", StringComparison.Ordinal));
        }
        finally { r.Window.Close(); r.Widget.Dispose(); }
    });
}
