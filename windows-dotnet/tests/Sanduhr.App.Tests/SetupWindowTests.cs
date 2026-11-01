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
            Assert.DoesNotContain(SetupCopy.ClaudeSource, shown);   // the source row itself needs usage ticked
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
}
