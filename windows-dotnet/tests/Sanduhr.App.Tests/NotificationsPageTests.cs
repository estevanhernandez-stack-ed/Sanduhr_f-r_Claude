using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.App.Tests;

/// <summary>Settings, Notifications (5.0 spec 4), over a scratch profile.</summary>
[Collection("SettingsWindow")]
public sealed class NotificationsPageTests : AppWindowFixture
{
    private (NotificationsViewModel Vm, SettingsStore Store) Build(Func<string, AgentRowState>? state = null)
    {
        var store = new SettingsStore(ScratchPaths());
        return (new NotificationsViewModel(store, state ?? (_ => AgentRowState.Ready)), store);
    }

    [Fact]
    public void It_opens_on_the_defaults_with_a_row_per_agent() => AppHost.Run(() =>
    {
        var (vm, _) = Build();
        Assert.Equal(["claude", "codex", "antigravity"], vm.Agents.Select(a => a.Agent));
        Assert.Equal(["Claude Code", "Codex", "Antigravity"], vm.Agents.Select(a => a.Name));
        Assert.All(vm.Agents, a => Assert.True(a.On));
        Assert.True(vm.Toast);
        Assert.True(vm.DeskCard);
        Assert.False(vm.GlowFinished);
        Assert.False(vm.GlowWaiting);
        Assert.False(vm.Flash);
        Assert.True(vm.SkipInFront);
        Assert.Equal(60, vm.Threshold.Seconds);
        Assert.Equal(["Every turn", "30 seconds", "1 minute", "2 minutes", "5 minutes"], NotificationsViewModel.ThresholdChoices.Select(c => c.Name));
    });

    [Fact]
    public void Every_change_saves_and_tells_the_app() => AppHost.Run(() =>
    {
        var (vm, store) = Build();
        int changed = 0;
        vm.Changed = () => changed++;

        vm.Toast = false;
        vm.GlowFinished = true;
        vm.Flash = true;
        vm.SkipInFront = false;
        vm.Threshold = NotificationsViewModel.ThresholdChoices.Single(c => c.Seconds == 300);

        var s = store.LoadAgentNotifications();
        Assert.False(s.Toast);
        Assert.True(s.GlowFinished);
        Assert.True(s.Flash);
        Assert.False(s.SkipInFront);
        Assert.Equal(300, s.ThresholdSeconds);
        Assert.Equal(5, changed);
    });

    [Fact]
    public void Reloading_does_not_save_or_tell_the_app() => AppHost.Run(() =>
    {
        var (vm, store) = Build();
        store.SaveAgentNotifications(NotificationSettings.Default with { Toast = false, ThresholdSeconds = 120 });
        int changed = 0;
        vm.Changed = () => changed++;
        vm.Reload();
        Assert.False(vm.Toast);
        Assert.Equal(120, vm.Threshold.Seconds);
        Assert.Equal(0, changed);
    });

    [Fact]
    public void Switching_an_agent_off_saves_it_and_clears_its_cards() => AppHost.Run(() =>
    {
        var (vm, store) = Build();
        var cleared = new List<string>();
        vm.AgentSwitchedOff = cleared.Add;

        vm.Agents.Single(a => a.Agent == "codex").On = false;

        Assert.False(store.LoadAgentNotifications().AgentOn("codex"));
        Assert.True(store.LoadAgentNotifications().AgentOn("claude"));
        Assert.Equal(["codex"], cleared);
    });

    [Fact]
    public void A_failed_save_leaves_the_agent_on_and_its_cards_alone() => AppHost.Run(() =>
    {
        var paths = ScratchPaths();
        System.IO.Directory.CreateDirectory(paths.AppDataDir);
        System.IO.File.WriteAllText(paths.SettingsFile, "{}");
        var store = new SettingsStore(paths);
        var vm = new NotificationsViewModel(store, _ => AgentRowState.Ready);
        var cleared = new List<string>();
        int changed = 0;
        vm.AgentSwitchedOff = cleared.Add;
        vm.Changed = () => changed++;
        var codex = vm.Agents.Single(a => a.Agent == "codex");

        using (new System.IO.FileStream(paths.SettingsFile, System.IO.FileMode.Open, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None))
            codex.On = false;

        Assert.True(codex.On);   // snapped back to what is saved
        Assert.Empty(cleared);
        Assert.Equal(0, changed);
        Assert.True(store.LoadAgentNotifications().AgentOn("codex"));
    });

    [Theory]
    [InlineData(AgentRowState.Ready, true, "")]
    [InlineData(AgentRowState.SourceOff, false, "Turn it on in Sources first.")]
    [InlineData(AgentRowState.HookMissing, false, "Needs the Claude Code hook. Install it on the Claude Code page.")]
    [InlineData(AgentRowState.HookOutdated, true,
        "The Claude Code hook here is an older one. Update it on the Claude Code page to get the notification and the Desk card.")]
    [InlineData(AgentRowState.HookUnreadable, false, "A Claude Code folder's settings can't be read. See the Claude Code page.")]
    public void A_row_says_what_stands_in_its_way(AgentRowState state, bool canToggle, string text) => AppHost.Run(() =>
    {
        var (vm, _) = Build(_ => state);
        var row = vm.Agents[0];
        Assert.Equal(canToggle, row.CanToggle);
        Assert.Equal(text, row.StateText);
        Assert.Equal(text.Length > 0, row.HasStateText);
    });

    [Fact]
    public void Each_agents_row_says_what_it_supports() => AppHost.Run(() =>
    {
        var (vm, _) = Build();
        Assert.Equal(
        [
            "Working, finished and waiting.", "Working and finished.", "Working, and finished about a minute and a half late.",
        ], vm.Agents.Select(a => a.Supports));
    });

    [Fact]
    public void Try_it_goes_to_the_app_and_the_glow_link_lands_on_the_picker() => AppHost.Run(() =>
    {
        var (vm, _) = Build();
        var tried = new List<AgentTurnKind>();
        vm.TryIt = tried.Add;

        vm.TryFinishedCommand.Execute(null);
        vm.TryWaitingCommand.Execute(null);

        Assert.Equal([AgentTurnKind.Finished, AgentTurnKind.Waiting], tried);
        Assert.Equal(SettingsPage.ClaudeCode, vm.GlowWhereLink.Page);
        Assert.Equal(SettingsIndex.GlowWhere, vm.GlowWhereLink.Anchor);
    });

    [Fact]
    public void Every_control_on_the_page_reads_as_words_and_the_trademarks_show() => AppHost.Run(() =>
    {
        var (vm, _) = Build();
        var window = new Window { Content = new NotificationsPage { DataContext = vm }, Width = 640, Height = 900 };
        OffScreen(window);
        window.Show();
        try
        {
            window.UpdateLayout();
            var names = NamesOf(window);
            Assert.Empty(LeaksIn(names));
            foreach (var name in new[]
                     {
                         "Notify me about Claude Code", "Notify me about Codex", "Notify me about Antigravity",
                         SettingsNames.NotifyToast, SettingsNames.NotifyDeskCard, SettingsNames.GlowWhenDone, SettingsNames.GlowWhenWaiting,
                         SettingsNames.NotifyFlash, SettingsNames.NotifyThreshold, SettingsNames.GlowNotInTerminal,
                         SettingsNames.NotifyGlowWhere, SettingsNames.NotifyTryFinished, SettingsNames.NotifyTryWaiting,
                         SettingsNames.NotificationsIntro, SettingsNames.TrademarkNotice, SettingsNames.CodexNotice, SettingsNames.AntigravityNotice,
                     })
                Assert.Contains(name, names);

            var controls = Walk(UIElementAutomationPeer.CreatePeerForElement((UIElement)window.Content))
                .Where(p => p.GetAutomationControlType() is AutomationControlType.Button or AutomationControlType.CheckBox
                    or AutomationControlType.ComboBox)
                .ToList();
            // Three agents; toast, card, two glows, flash, in front; the threshold; the link and two Try it. If the count is off,
            // print each peer's type and name before touching the number: a control with no name is what this test is for.
            Assert.Equal(3 + 6 + 1 + 3, controls.Count);
            Assert.All(controls, p => Assert.False(string.IsNullOrWhiteSpace(p.GetName())));
        }
        finally { window.Close(); }
    });

    private static IEnumerable<T> Below<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var deeper in Below<T>(child)) yield return deeper;
        }
    }

    [Fact]
    public void The_three_glow_switches_are_named_by_their_labels_and_the_link_lands_on_the_picker() => AppHost.Run(() =>
    {
        // P8, moved here from the Claude Code page in 5.0: Content and the spoken name are the same words, from SettingsNames.
        var (vm, _) = Build();
        var window = new Window { Content = new NotificationsPage { DataContext = vm }, Width = 640, Height = 900 };
        OffScreen(window);
        window.Show();
        try
        {
            window.UpdateLayout();
            var boxes = Below<System.Windows.Controls.CheckBox>(window).ToList();
            foreach (var label in new[] { SettingsNames.GlowWhenWaiting, SettingsNames.GlowWhenDone, SettingsNames.GlowNotInTerminal })
            {
                var box = Assert.Single(boxes, b => Equals(b.Content, label));
                Assert.Equal(label, AutomationProperties.GetName(box));
            }
            var link = Assert.Single(Below<System.Windows.Controls.Button>(window),
                b => AutomationProperties.GetName(b) == SettingsNames.NotifyGlowWhere);
            Assert.Equal(new SettingsLink(SettingsPage.ClaudeCode, SettingsIndex.GlowWhere), SettingsIndex.ParseLink(SettingsLinkTo.GetLink(link)));
        }
        finally { window.Close(); }
    });

    /// <summary>The 3.5 to 4.9 hook: one Notification group and one Stop group that touch a signal file.</summary>
    internal static string HooksFrom49()
    {
        static System.Text.Json.Nodes.JsonObject Group(string signal, string? matcher)
        {
            var g = new System.Text.Json.Nodes.JsonObject();
            if (matcher is not null) g["matcher"] = matcher;
            g["hooks"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject
            {
                ["type"] = "command", ["shell"] = "powershell", ["command"] = ClaudeCodeIntegrations.GlowHookCommand(signal),
                ["async"] = true, ["timeout"] = 5,
            });
            return g;
        }
        return new System.Text.Json.Nodes.JsonObject
        {
            ["hooks"] = new System.Text.Json.Nodes.JsonObject
            {
                ["Notification"] = new System.Text.Json.Nodes.JsonArray(Group(ClaudeCodeIntegrations.WaitingSignal, ClaudeCodeIntegrations.WaitingMatcher)),
                ["Stop"] = new System.Text.Json.Nodes.JsonArray(Group(ClaudeCodeIntegrations.DoneSignal, null)),
            },
        }.ToJsonString();
    }

    [Fact]
    public void Claude_Codes_row_reads_the_hook_from_the_folders_whether_or_not_the_Claude_Code_page_was_shown() => AppHost.Run(() =>
    {
        // Controller ruling 16: Settings opened straight to Notifications, with the Claude Code page never selected and its cards
        // not read again since, still says what each folder's settings.json holds.
        var (widget, _) = ScratchWidget("Personal");
        var work = Home(".claude-work");
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        var claude = svm.Notifications.Agents.Single(a => a.Agent == AgentSources.Claude);
        Assert.Equal(AgentRowState.HookMissing, svm.ClaudeCodePage.HookState());
        Assert.Equal(SettingsNames.NotifyHookMissing, claude.StateText);
        Assert.False(claude.CanToggle);

        // The current hook, written by the engine behind the Claude Code page's back.
        Assert.True(ClaudeCodeHost.For(widget).Integrations().Install(IntegrationKind.Hooks, work).Ok);
        svm.Notifications.Reload();
        Assert.False(svm.ClaudeCodePage.Folders.Single(f => f.Display == @"~\.claude-work").Hooks.IsInstalled);   // the card is stale
        Assert.Equal(AgentRowState.Ready, svm.ClaudeCodePage.HookState());
        Assert.Equal("", claude.StateText);
        Assert.True(claude.CanToggle);

        // The 3.5 to 4.9 hook.
        System.IO.File.WriteAllText(System.IO.Path.Combine(work, "settings.json"), HooksFrom49());
        svm.Notifications.Reload();
        Assert.Equal(AgentRowState.HookOutdated, svm.ClaudeCodePage.HookState());
        Assert.Equal(SettingsNames.NotifyHookOutdated, claude.StateText);
        Assert.True(claude.CanToggle);

        // Fix round 1: a folder whose settings.json is not JSON reads as unreadable, not as one without the hook; any folder with a
        // hook still wins over it.
        var broken = Home(".claude-broken", "{ nope");
        svm.Notifications.Reload();
        Assert.Equal(AgentRowState.HookOutdated, svm.ClaudeCodePage.HookState());
        System.IO.File.WriteAllText(System.IO.Path.Combine(work, "settings.json"), "{}");
        svm.Notifications.Reload();
        Assert.Equal(AgentRowState.HookUnreadable, svm.ClaudeCodePage.HookState());
        Assert.Equal(SettingsNames.NotifyHookUnreadable, claude.StateText);
        Assert.False(claude.CanToggle);
        Assert.True(ClaudeCodeHost.For(widget).Integrations().Install(IntegrationKind.Hooks, work).Ok);
        svm.Notifications.Reload();
        Assert.Equal(AgentRowState.Ready, svm.ClaudeCodePage.HookState());
        Assert.True(System.IO.Directory.Exists(broken));
    });
}
