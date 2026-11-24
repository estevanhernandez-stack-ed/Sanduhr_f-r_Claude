using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.App.Tests;

/// <summary>Set up Sanduhr (setup spec, sections 2 and 4): the window, its ticks, what Continue and the close do.</summary>
[Collection("SettingsWindow")]
public sealed class SetupWindowTests : AppWindowFixture
{
    private sealed class Host : ISetupHost
    {
        public bool DrawersOn { get; set; }
        public bool DrawersEverSorted { get; set; } = true;
        public int DrawerFiles { get; set; }
        public bool AddOnsInstalled { get; set; }
        public bool UsageOn { get; set; }
        public bool HasAccount { get; set; } = true;
        public SetupDrawersOff TurnOffDrawers() { DrawersOn = false; return new(true, null); }
        public void TurnOnDrawers() => DrawersOn = true;
        public string? SortNow() => null;
        public void SetUsage(bool on) => UsageOn = on;
        public void ReloadDesk() { }
        public string? ApplyLook(string id) => null;
    }

    private SetupViewModel Model(SettingsStore store, bool firstRun, Host? host = null) =>
        new(new SetupService(store, host ?? new Host()), firstRun);

    [Fact]
    public void A_first_run_shows_the_default_ticks_and_every_control_has_a_name() => AppHost.Run(() =>
    {
        var vm = Model(new SettingsStore(ScratchPaths()), firstRun: true);
        Assert.True(vm.Desktop);
        Assert.True(vm.Drawers);
        Assert.False(vm.ClaudeUsage);
        Assert.Equal(SetupCopy.NotNow, vm.DismissLabel);
        Assert.Empty(vm.ChangeLines);
        var w = new SetupWindow(vm);
        OffScreen(w);
        w.Show();
        try
        {
            var names = NamesOf(w);
            Assert.Empty(LeaksIn(names));
            foreach (var expected in new[] { SetupCopy.Title, SetupCopy.DesktopTitle, SetupCopy.DrawersTitle, SetupCopy.UsageTitle,
                SetupCopy.DesignWithChat, SetupCopy.DesignWithChatCaption, SetupCopy.NotNow, SetupCopy.Continue })
                Assert.Contains(names, n => n == expected);
            // Every control that takes a click or a key reads as words, not as an empty name.
            var unnamed = Walk(UIElementAutomationPeer.CreatePeerForElement(w))
                .Where(p => p.GetAutomationControlType() is AutomationControlType.Button or AutomationControlType.CheckBox or AutomationControlType.Hyperlink)
                .Where(p => string.IsNullOrWhiteSpace(p.GetName()))
                .Select(p => p.GetType().Name)
                .ToList();
            Assert.Empty(unnamed);
            // Design it with an AI chat is a button of its own with a caption, not a link under a tick.
            Assert.True(w.FindName("DesignWithChatButton") is System.Windows.Controls.Button { Content: SetupCopy.DesignWithChat });
            Assert.True(w.FindName("DesignWithChatCaption") is System.Windows.Controls.TextBlock { Text: SetupCopy.DesignWithChatCaption });
            // Streamer mode: the screen is fixed copy. It draws no name, path or usage of anyone's.
            Assert.DoesNotContain(names, n => n.Contains(":\\"));
        }
        finally { w.Close(); }
    });

    [Fact]
    public void Ticking_usage_opens_the_source_list_with_the_trademark_line() => AppHost.Run(() =>
    {
        var vm = Model(new SettingsStore(ScratchPaths()), firstRun: true);
        var w = new SetupWindow(vm);
        OffScreen(w);
        w.Show();
        try
        {
            var before = new List<string>();
            CollectText(w, before);
            Assert.DoesNotContain(SetupCopy.ClaudeTrademark, before);
            vm.ClaudeUsage = true;
            w.UpdateLayout();
            Assert.True(w.FindName("UsageBox") is System.Windows.Controls.CheckBox { IsChecked: true });
            // The source and its trademark line are drawn (a text block is visible), not only bound.
            var shown = new List<string>();
            CollectText(w, shown);
            Assert.Contains(SetupCopy.ClaudeSource, shown);
            Assert.Contains(SetupCopy.ClaudeTrademark, shown);
        }
        finally { w.Close(); }
    });

    [Fact]
    public void A_change_line_that_names_Claude_Code_shows_the_trademark_line_with_usage_unticked() => AppHost.Run(() =>
    {
        var store = new SettingsStore(ScratchPaths());
        store.SaveSetup(new SetupSettings(true, new Dictionary<string, bool> { ["claude"] = true }));
        store.SaveDesk(store.LoadDesk() with { Enabled = true });
        var vm = Model(store, firstRun: false, new Host { UsageOn = true, AddOnsInstalled = true, HasAccount = false });
        Assert.True(vm.ShowTrademark);                       // usage ticked
        vm.ClaudeUsage = false;
        Assert.Equal([SetupCopy.AddOnsStayLine], vm.ChangeLines);
        Assert.True(vm.ShowTrademark);                       // the add-ons line names Claude Code
        var w = new SetupWindow(vm);
        OffScreen(w);
        w.Show();
        try
        {
            w.UpdateLayout();
            var shown = new List<string>();
            CollectText(w, shown);
            Assert.Contains(SetupCopy.ClaudeTrademark, shown);
            Assert.True(w.FindName("UsageBox") is System.Windows.Controls.CheckBox { IsChecked: false });   // Claude's own tick is off; the line is the add-ons'
        }
        finally { w.Close(); }
    });

    [Fact]
    public void No_trademark_line_when_Claude_is_not_named() => AppHost.Run(() =>
    {
        var store = new SettingsStore(ScratchPaths());
        store.SaveSetup(new SetupSettings(true, new Dictionary<string, bool> { ["claude"] = false }));
        store.SaveDesk(store.LoadDesk() with { Enabled = true });
        var vm = Model(store, firstRun: false, new Host { AddOnsInstalled = true });
        Assert.False(vm.ShowTrademark);
        vm.Desktop = false;                                  // a change line that does not name Claude
        Assert.Contains(SetupCopy.DesktopOffLine, vm.ChangeLines);
        Assert.False(vm.ShowTrademark);
    });

    private static void CollectText(DependencyObject root, List<string> into)
    {
        if (root is System.Windows.Controls.TextBlock { IsVisible: true } tb) into.Add(tb.Text);
        int n = root is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetChildrenCount(root) : 0;
        for (int i = 0; i < n; i++)
            CollectText(System.Windows.Media.VisualTreeHelper.GetChild(root, i), into);
    }

    [Fact]
    public void Nothing_ticked_disables_Continue_with_the_reason() => AppHost.Run(() =>
    {
        var vm = Model(new SettingsStore(ScratchPaths()), firstRun: true);
        vm.Desktop = false;
        vm.Drawers = false;
        Assert.False(vm.CanContinue);
        Assert.False(vm.ContinueCommand.CanExecute(null));
        Assert.Equal(SetupCopy.PickOne, vm.Blocked);
        vm.Drawers = true;
        Assert.True(vm.CanContinue);
        Assert.True(vm.ContinueCommand.CanExecute(null));
        Assert.Null(vm.Blocked);
    });

    [Fact]
    public void Closing_a_first_run_without_Continue_saves_nothing() => AppHost.Run(() =>
    {
        var store = new SettingsStore(ScratchPaths());
        var vm = Model(store, firstRun: true);
        var w = new SetupWindow(vm);
        OffScreen(w);
        w.Show();
        w.Close();   // the window's X: the same as Not now
        Assert.Null(store.LoadSetup());
        Assert.False(vm.Saved);
    });

    [Fact]
    public void Not_now_closes_the_window_and_saves_nothing() => AppHost.Run(() =>
    {
        var store = new SettingsStore(ScratchPaths());
        var vm = Model(store, firstRun: true);
        var w = new SetupWindow(vm);
        OffScreen(w);
        w.Show();
        bool closed = false;
        w.Closed += (_, _) => closed = true;
        vm.DismissCommand.Execute(null);
        Assert.True(closed);
        Assert.Null(store.LoadSetup());
        Assert.False(vm.Saved);
    });

    [Fact]
    public void Reopened_later_it_shows_what_is_on_and_lists_what_unticking_does() => AppHost.Run(() =>
    {
        var store = new SettingsStore(ScratchPaths());
        store.SaveSetup(new SetupSettings(true, new Dictionary<string, bool> { ["claude"] = true }));
        store.SaveDesk(store.LoadDesk() with { Enabled = true });
        var vm = Model(store, firstRun: false, new Host { UsageOn = true });
        Assert.True(vm.Desktop);
        Assert.False(vm.Drawers);
        Assert.True(vm.ClaudeUsage);
        Assert.Equal(SetupCopy.Cancel, vm.DismissLabel);
        Assert.Empty(vm.ChangeLines);
        vm.ClaudeUsage = false;
        Assert.Contains(SetupCopy.UsageOffLine, vm.ChangeLines);
        Assert.Contains(SetupCopy.UsageOffLine, vm.ChangeText);   // one text a screen reader reads
        vm.Desktop = false;
        Assert.Contains(SetupCopy.DesktopOffLine, vm.ChangeLines);
        vm.Desktop = true;
        Assert.DoesNotContain(SetupCopy.DesktopOffLine, vm.ChangeLines);
    });

    [Fact]
    public void Cancel_after_changing_ticks_saves_nothing() => AppHost.Run(() =>
    {
        var store = new SettingsStore(ScratchPaths());
        store.SaveSetup(new SetupSettings(true, new Dictionary<string, bool> { ["claude"] = true }));
        var vm = Model(store, firstRun: false, new Host { UsageOn = true });
        var w = new SetupWindow(vm);
        OffScreen(w);
        w.Show();
        vm.ClaudeUsage = false;
        vm.DismissCommand.Execute(null);
        Assert.True(store.LoadSetup()!.ClaudeUsage);
        Assert.False(vm.Saved);
    });

    [Fact]
    public void Continue_applies_closes_the_window_and_raises_Applied() => AppHost.Run(() =>
    {
        var store = new SettingsStore(ScratchPaths());
        var vm = Model(store, firstRun: true);
        var w = new SetupWindow(vm);
        OffScreen(w);
        w.Show();
        bool closed = false;
        w.Closed += (_, _) => closed = true;
        SetupResult? got = null;
        vm.Applied += r => got = r;
        vm.ContinueCommand.Execute(null);
        Assert.NotNull(got);
        Assert.True(closed);
        Assert.True(vm.Saved);
        Assert.True(store.LoadSetup()!.Desktop);
        Assert.False(store.LoadSetup()!.ClaudeUsage);
        Assert.True(store.LoadDesk().Enabled);
    });

    [Fact]
    public void A_save_that_fails_keeps_the_window_open_with_the_line() => AppHost.Run(() =>
    {
        var store = new SettingsStore(ScratchPaths());
        store.SaveDesk(store.LoadDesk() with { Enabled = true });   // the settings file exists
        var file = ScratchPaths().SettingsFile;
        Assert.True(File.Exists(file));
        var vm = Model(store, firstRun: true);
        var w = new SetupWindow(vm);
        OffScreen(w);
        w.Show();
        bool closed = false;
        w.Closed += (_, _) => closed = true;
        bool applied = false;
        vm.Applied += _ => applied = true;
        File.SetAttributes(file, FileAttributes.ReadOnly);
        try
        {
            vm.ContinueCommand.Execute(null);
            Assert.False(applied);
            Assert.False(closed);
            Assert.False(vm.Saved);
            Assert.Contains(SetupCopy.SaveFailed, vm.ResultLines);
            Assert.Contains(NamesOf(w), n => n == SetupCopy.WhatHappened);
        }
        finally
        {
            File.SetAttributes(file, FileAttributes.Normal);
            w.Close();
        }
    });

    [Fact]
    public void A_first_run_with_an_account_already_signed_in_opens_with_usage_ticked() => AppHost.Run(() =>
    {
        var store = new SettingsStore(ScratchPaths());
        var vm = new SetupViewModel(new SetupService(store, new Host()), firstRun: true, hasAccount: () => true);
        Assert.True(vm.ClaudeUsage);
        Assert.True(vm.Desktop);
        Assert.True(vm.Drawers);
    });

    [Fact]
    public void An_account_added_while_the_screen_is_up_ticks_usage_at_Continue_if_the_tick_was_never_touched() => AppHost.Run(() =>
    {
        var store = new SettingsStore(ScratchPaths());
        bool account = false;
        var host = new Host { UsageOn = true };   // the widget loads usage on before any setup
        var vm = new SetupViewModel(new SetupService(store, host), firstRun: true, hasAccount: () => account);
        Assert.False(vm.ClaudeUsage);
        account = true;   // signed in from the widget's card
        vm.ContinueCommand.Execute(null);
        Assert.True(vm.ClaudeUsage);
        Assert.True(store.LoadSetup()!.ClaudeUsage);
    });

    [Fact]
    public void An_account_added_while_the_screen_is_up_does_not_override_a_tick_the_person_touched() => AppHost.Run(() =>
    {
        var store = new SettingsStore(ScratchPaths());
        bool account = false;
        var vm = new SetupViewModel(new SetupService(store, new Host()), firstRun: true, hasAccount: () => account);
        vm.ClaudeUsage = true;
        vm.ClaudeUsage = false;   // they looked at it and chose no
        account = true;
        vm.ContinueCommand.Execute(null);
        Assert.False(vm.ClaudeUsage);
        Assert.False(store.LoadSetup()!.ClaudeUsage);
    });

    [Fact]
    public void Design_it_with_an_AI_chat_asks_the_app_to_open_it() => AppHost.Run(() =>
    {
        var vm = Model(new SettingsStore(ScratchPaths()), firstRun: true);
        int asked = 0;
        vm.DesignWithChatRequested = () => asked++;
        vm.DesignWithChatCommand.Execute(null);
        Assert.Equal(1, asked);
    });

    [Fact]
    public void A_stored_Codex_tick_survives_a_setup_save_that_only_changes_Desktop() => AppHost.Run(() =>
    {
        var store = new SettingsStore(ScratchPaths());
        store.SaveSetup(new SetupSettings(true, new Dictionary<string, bool> { ["claude"] = false, ["codex"] = true }));
        store.SaveDesk(store.LoadDesk() with { Enabled = true });
        var vm = new SetupViewModel(new SetupService(store, new Host()), firstRun: false, codexInstalled: () => true);
        Assert.True(vm.CodexUsage);                  // seeded from the saved setup
        vm.Desktop = false;
        vm.ContinueCommand.Execute(null);
        Assert.True(vm.Saved);
        Assert.True(store.LoadSetup()!.CodexUsage);
        Assert.False(store.LoadSetup()!.Desktop);
    });

    [Fact]
    public void A_stored_Codex_tick_survives_a_setup_save_when_Codex_is_no_longer_installed_and_can_be_cleared() => AppHost.Run(() =>
    {
        var store = new SettingsStore(ScratchPaths());
        store.SaveSetup(new SetupSettings(true, new Dictionary<string, bool> { ["claude"] = false, ["codex"] = true }));
        store.SaveDesk(store.LoadDesk() with { Enabled = true });
        var vm = new SetupViewModel(new SetupService(store, new Host()), firstRun: false, codexInstalled: () => false);
        Assert.True(vm.CodexUsage);
        Assert.True(vm.CodexEnabled);                // ticked from before: it can still be cleared
        Assert.Equal(SetupCopy.CodexNotInstalled, vm.CodexNotInstalledText);
        vm.Desktop = false;
        vm.ContinueCommand.Execute(null);
        Assert.True(store.LoadSetup()!.CodexUsage);
        vm.CodexUsage = false;
        Assert.False(vm.CodexEnabled);               // cleared and not installed: greyed
        Assert.Equal([SetupCopy.CodexOffLine], vm.ChangeLines);
    });

    [Fact]
    public void Codex_is_a_tick_beside_Claude_and_never_asks_to_sign_in() => AppHost.Run(() =>
    {
        var store = new SettingsStore(ScratchPaths());
        var vm = new SetupViewModel(new SetupService(store, new Host()), firstRun: true, codexInstalled: () => true);
        var w = new SetupWindow(vm);
        OffScreen(w);
        w.Show();
        try
        {
            var codex = Assert.IsType<System.Windows.Controls.CheckBox>(w.FindName("CodexUsageBox"));
            var claude = Assert.IsType<System.Windows.Controls.CheckBox>(w.FindName("UsageBox"));
            Assert.True(codex.IsEnabled);
            Assert.Equal(SetupCopy.CodexSource, System.Windows.Automation.AutomationProperties.GetName(codex));
            Assert.Equal(SetupCopy.ClaudeSource, System.Windows.Automation.AutomationProperties.GetName(claude));
            codex.IsChecked = true;
            Assert.True(vm.CodexUsage);
            Assert.False(vm.ClaudeUsage);
            Assert.False(vm.ShowTrademark);           // Claude is not named, so no trademark line
            vm.ContinueCommand.Execute(null);
            Assert.True(store.LoadSetup()!.CodexUsage);
            Assert.False(store.LoadSetup()!.ClaudeUsage);
        }
        finally { w.Close(); }
    });

    [Fact]
    public void The_Codex_disclaimer_shows_beside_its_tile_ticked_or_not() => AppHost.Run(() =>
    {
        var vm = new SetupViewModel(new SetupService(new SettingsStore(ScratchPaths()), new Host()), firstRun: true, codexInstalled: () => true);
        var w = new SetupWindow(vm);
        OffScreen(w);
        w.Show();
        try
        {
            var before = new List<string>();
            CollectText(w, before);
            Assert.Contains(SetupCopy.CodexTrademark, before);   // the tile is on the screen, so its notice is
            vm.CodexUsage = true;
            w.UpdateLayout();
            var after = new List<string>();
            CollectText(w, after);
            Assert.Contains(SetupCopy.CodexTrademark, after);
            Assert.Contains("OpenAI", SetupCopy.CodexTrademark);
        }
        finally { w.Close(); }
    });

    [Fact]
    public void Codex_not_installed_is_greyed_with_its_line_on_the_setup_screen() => AppHost.Run(() =>
    {
        var vm = new SetupViewModel(new SetupService(new SettingsStore(ScratchPaths()), new Host()), firstRun: true, codexInstalled: () => false);
        var w = new SetupWindow(vm);
        OffScreen(w);
        w.Show();
        try
        {
            var codex = Assert.IsType<System.Windows.Controls.CheckBox>(w.FindName("CodexUsageBox"));
            Assert.False(codex.IsEnabled);
            Assert.False(vm.CodexUsage);
            var shown = new List<string>();
            CollectText(w, shown);
            Assert.Contains(SetupCopy.CodexNotInstalled, shown);
        }
        finally { w.Close(); }
    });

    [Fact]
    public void Settings_General_has_Set_up_Sanduhr_and_it_asks_the_app_to_open_it() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget();
        try
        {
            var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
            bool asked = false;
            svm.SetupRequested = () => asked = true;
            svm.OpenSetupCommand.Execute(null);
            Assert.True(asked);
            Assert.Equal(SetupCopy.Title, svm.SetupTitle);
        }
        finally { widget.Dispose(); }
    });

    // ---- the Start with a look strip and the window's height (5.0 desk looks, task 12 review) ----

    private static string SixLookJson(string id, string name) => $$"""
        {
          "schema_version": 1, "id": "{{id}}", "name": "{{name}}", "blurb": "A test look.", "font_fallback": "Segoe UI",
          "design": {
            "layout": {
              "clock": { "anchor": "tc", "size": 1.3 },
              "message": { "hidden": true }, "claude": { "hidden": true }, "meters": { "hidden": true }, "nowPlaying": { "hidden": true },
              "telemetry": { "hidden": true }, "weather": { "hidden": true }, "watchers": { "hidden": true }, "launcher": { "hidden": true },
              "glow": { "hidden": true }, "companion": { "hidden": true }
            },
            "font": "Georgia", "ink": "auto", "plate": false, "shadow": true, "glow": false, "follows_theme": false
          }
        }
        """;

    /// <summary>A setup window over six test looks (the real catalog's count), shown off screen; the service is disposed with it.</summary>
    private void WithSixLooks(Action<SetupWindow> body) => AppHost.Run(() =>
    {
        var paths = ScratchPaths();
        var store = new SettingsStore(paths);
        string[] names = ["Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta"];
        using var service = new Sanduhr.App.Desk.DeskDesignService(paths, store)
        {
            LookCatalog = Sanduhr.Core.DeskLookCatalog.Load(names.Select(n => (n.ToLowerInvariant(), SixLookJson(n.ToLowerInvariant(), n))).ToArray()),
            InstalledFonts = () => ["Georgia", "Segoe UI"],
            Monitors = () => [],
            DrawersReviewing = () => false,
        };
        service.LookWallpaper = () => null;
        var vm = new SetupViewModel(new SetupService(store, new Host()), true, hasAccount: () => false, codexInstalled: () => false, looks: () => service);
        Assert.Equal(6, vm.Looks.Count);
        var w = new SetupWindow(vm);
        OffScreen(w);
        try { body(w); }
        finally { w.Close(); }
    });

    private static T Named<T>(Window w, string name) where T : class =>
        (w.FindName(name) as T) ?? throw new InvalidOperationException(name + " is not in the window");

    private static bool IsInside(DependencyObject? node, DependencyObject ancestor)
    {
        for (; node is not null; node = System.Windows.Media.VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node))
            if (ReferenceEquals(node, ancestor)) return true;
        return false;
    }

    [Fact]
    public void The_window_is_never_taller_than_the_work_area() => WithSixLooks(w =>
    {
        Assert.Equal(SystemParameters.WorkArea.Height, w.MaxHeight);
    });

    [Fact]
    public void Continue_sits_below_the_scroller_and_the_tiles_and_the_looks_row_scroll_with_it() => WithSixLooks(w =>
    {
        w.Show();
        var scroller = Named<System.Windows.Controls.ScrollViewer>(w, "SetupScroller");
        Assert.False(scroller.Focusable);
        Assert.False(IsInside(Named<System.Windows.Controls.Button>(w, "ContinueButton"), scroller));
        Assert.False(IsInside(Named<System.Windows.Controls.Button>(w, "DismissButton"), scroller));
        foreach (var inside in new[] { "DesktopBox", "DrawersBox", "UsageBox", "CodexUsageBox", "StartWithALookRow" })
            Assert.True(IsInside((DependencyObject)w.FindName(inside), scroller), inside);
    });

    [Fact]
    public void Six_looks_are_one_strip_a_single_row_that_scrolls_sideways() => WithSixLooks(w =>
    {
        w.Show();
        w.UpdateLayout();
        var strip = Named<System.Windows.Controls.ScrollViewer>(w, "LooksStrip");
        Assert.False(strip.Focusable);
        Assert.Equal(System.Windows.Controls.ScrollBarVisibility.Auto, strip.HorizontalScrollBarVisibility);
        Assert.Equal(System.Windows.Controls.ScrollBarVisibility.Disabled, strip.VerticalScrollBarVisibility);
        var row = Named<System.Windows.Controls.StackPanel>(w, "StartWithALookRow");
        Assert.True(row.ActualHeight < 240, $"the row is {row.ActualHeight} DIPs tall");
        Assert.True(strip.ScrollableWidth > 0, "six cards are wider than the window, so the strip scrolls");
        Assert.True(HasPanel<System.Windows.Controls.StackPanel>(strip, p => p.Orientation == System.Windows.Controls.Orientation.Horizontal && p.Children.Count == 6));
        Assert.False(HasPanel<System.Windows.Controls.WrapPanel>(strip, _ => true));
    });

    [Fact]
    public void The_strip_is_one_Tab_stop_and_a_named_group() => WithSixLooks(w =>
    {
        w.Show();
        w.UpdateLayout();
        var row = Assert.IsType<DeskLooksRow>(Named<System.Windows.Controls.ScrollViewer>(w, "LooksStrip").Content);
        var peer = UIElementAutomationPeer.CreatePeerForElement(row);
        Assert.Equal(AutomationControlType.Group, peer.GetAutomationControlType());
        Assert.Equal("Ready-made looks", peer.GetName());
        Assert.Equal(System.Windows.Input.KeyboardNavigationMode.Once, System.Windows.Input.KeyboardNavigation.GetTabNavigation(row));
        Assert.Equal(System.Windows.Input.KeyboardNavigationMode.Contained, System.Windows.Input.KeyboardNavigation.GetDirectionalNavigation(row));
    });

    private static bool HasPanel<TPanel>(DependencyObject root, Func<TPanel, bool> match) where TPanel : System.Windows.Controls.Panel
    {
        if (root is TPanel p && match(p)) return true;
        int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
            if (HasPanel(System.Windows.Media.VisualTreeHelper.GetChild(root, i), match)) return true;
        return false;
    }

    [Fact]
    public void With_a_small_cap_the_window_obeys_it_the_tiles_scroll_and_Continue_stays_in_view() => WithSixLooks(w =>
    {
        w.MaxHeight = 500;
        w.Show();
        w.UpdateLayout();
        var scroller = Named<System.Windows.Controls.ScrollViewer>(w, "SetupScroller");
        var cont = Named<System.Windows.Controls.Button>(w, "ContinueButton");
        Assert.True(w.ActualHeight <= 500, $"the window is {w.ActualHeight} DIPs tall");
        Assert.True(scroller.ScrollableHeight > 0);
        var content = (FrameworkElement)w.Content;
        double bottom = cont.TranslatePoint(new Point(0, cont.ActualHeight), content).Y;
        Assert.True(bottom <= content.ActualHeight + 0.5, $"Continue ends at {bottom} of {content.ActualHeight}");
    });

    [Fact]
    public void On_a_small_screen_every_scroll_bar_part_has_a_name() => WithSixLooks(w =>
    {
        w.MaxHeight = 500;
        w.Show();
        w.UpdateLayout();
        Assert.True(Named<System.Windows.Controls.ScrollViewer>(w, "SetupScroller").ScrollableHeight > 0, "the tiles scroll, so their bar shows");
        Assert.True(Named<System.Windows.Controls.ScrollViewer>(w, "LooksStrip").ScrollableWidth > 0, "the strip overflows, so its bar shows");
        var clickable = Walk(UIElementAutomationPeer.CreatePeerForElement(w))
            .Where(p => p.GetAutomationControlType() is AutomationControlType.Button or AutomationControlType.CheckBox or AutomationControlType.Hyperlink)
            .ToList();
        Assert.Empty(clickable.Where(p => string.IsNullOrWhiteSpace(p.GetName())).Select(p => p.GetType().Name));
        var names = clickable.Select(p => p.GetName()).ToList();
        foreach (var part in new[] { SettingsNames.ScrollPageUp, SettingsNames.ScrollPageDown, SettingsNames.ScrollPageLeft, SettingsNames.ScrollPageRight })
            Assert.Contains(part, names);
    });

    [Fact]
    public void A_first_run_with_a_capped_window_still_names_every_control() => AppHost.Run(() =>
    {
        var vm = Model(new SettingsStore(ScratchPaths()), firstRun: true);
        var w = new SetupWindow(vm) { MaxHeight = 300 };
        OffScreen(w);
        w.Show();
        try
        {
            w.UpdateLayout();
            Assert.True(Named<System.Windows.Controls.ScrollViewer>(w, "SetupScroller").ScrollableHeight > 0, "the bar shows");
            var unnamed = Walk(UIElementAutomationPeer.CreatePeerForElement(w))
                .Where(p => p.GetAutomationControlType() is AutomationControlType.Button or AutomationControlType.CheckBox or AutomationControlType.Hyperlink)
                .Where(p => string.IsNullOrWhiteSpace(p.GetName()))
                .Select(p => p.GetType().Name).ToList();
            Assert.Empty(unnamed);
        }
        finally { w.Close(); }
    });

    [Fact]
    public void The_wheel_over_the_strip_scrolls_it_sideways_and_is_not_swallowed() => WithSixLooks(w =>
    {
        w.Show();
        w.UpdateLayout();
        var strip = Named<System.Windows.Controls.ScrollViewer>(w, "LooksStrip");
        Assert.True(strip.ScrollableWidth > 0);
        Assert.Equal(0, strip.HorizontalOffset);
        var wheel = new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, -120)
        {
            RoutedEvent = UIElement.PreviewMouseWheelEvent,
            Source = strip,
        };
        strip.RaiseEvent(wheel);
        w.UpdateLayout();   // the scroll content applies a requested offset on its next arrange
        Assert.True(strip.HorizontalOffset > 0, "a negative delta moves the strip to the right");
        Assert.True(wheel.Handled);
    });
}
