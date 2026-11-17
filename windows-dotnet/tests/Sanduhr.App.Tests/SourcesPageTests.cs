using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.App.Tests;

/// <summary>Settings, Usage, Sources (agent layer): one row per source with on, primary, color and alerts, over a scratch profile.</summary>
[Collection("SettingsWindow")]
public sealed class SourcesPageTests : AppWindowFixture
{
    private static readonly string[] SourceIds = [.. AgentSources.All];

    private string CodexHome => Path.Combine(Profile, ".codex");

    /// <summary>A view model over scratch folders: Claude's switch is a local flag saved to setup the way the widget saves it, and
    /// Codex is installed only when its home folder exists under the scratch profile.</summary>
    private (SourcesViewModel Vm, SettingsStore Store, Func<bool> ClaudeOn) Build(bool codexInstalled, bool claudeOn = false, bool deskOn = true)
    {
        if (codexInstalled) Directory.CreateDirectory(CodexHome);
        var store = new SettingsStore(ScratchPaths());
        store.SaveDesk(store.LoadDesk() with { Enabled = deskOn });   // with the Desk on, switching every source off still leaves something shown
        bool claude = claudeOn;
        var vm = new SourcesViewModel(store, () => claude,
            on =>
            {
                claude = on;
                store.SaveSetup((store.LoadSetup() ?? SetupChoices.FirstRun.ToSettings(null)).WithClaude(on));
            },
            () => CodexHomes.Discover(null, Profile), () => false, _ => { });
        return (vm, store, () => claude);
    }

    private (SourcesViewModel Vm, SettingsStore Store) NewSources(Func<bool> antigravityInstalled, Action<string> agentVaultOn)
    {
        var store = new SettingsStore(ScratchPaths());
        store.SaveDesk(store.LoadDesk() with { Enabled = true });
        var vm = new SourcesViewModel(store, () => false, _ => { }, () => CodexHomes.Discover(null, Profile), antigravityInstalled, agentVaultOn);
        return (vm, store);
    }

    [Fact]
    public void Antigravity_is_an_activity_row_off_by_default_and_never_a_primary_choice() => AppHost.Run(() =>
    {
        var calls = new List<string>();
        var (vm, store) = NewSources(antigravityInstalled: () => true, agentVaultOn: calls.Add);
        var row = Assert.Single(vm.ActivityRows);
        Assert.Equal("antigravity", row.Source);
        Assert.False(row.On);
        Assert.DoesNotContain(vm.Rows, r => r.Source == "antigravity");

        row.On = true;

        Assert.Equal(new[] { "antigravity" }, calls);
        Assert.True(store.LoadActivity()["antigravity"]);
    });

    [Fact]
    public void Without_the_cli_the_row_is_disabled_and_says_nothing_found_yet() => AppHost.Run(() =>
    {
        var (vm, _) = NewSources(antigravityInstalled: () => false, agentVaultOn: _ => { });
        var row = Assert.Single(vm.ActivityRows);
        Assert.False(row.Installed);
        Assert.Equal(SettingsNames.NothingFoundYet, row.Caption);
    });

    [Fact]
    public void A_checked_Antigravity_box_stays_enabled_when_its_cli_folder_goes_so_it_can_be_turned_off() => AppHost.Run(() =>
    {
        bool installed = true;
        var (vm, store) = NewSources(antigravityInstalled: () => installed, agentVaultOn: _ => { });
        var row = Assert.Single(vm.ActivityRows);
        row.On = true;
        var changed = new List<string?>();
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        installed = false;
        vm.Reload();

        Assert.False(row.Installed);
        Assert.True(row.CanToggle);
        Assert.Contains(nameof(ActivityRowViewModel.CanToggle), changed);
        row.On = false;
        Assert.False(store.LoadActivity()["antigravity"]);
        Assert.False(row.CanToggle);
    });

    [Fact]
    public void An_Antigravity_switch_that_cannot_be_saved_snaps_back() => AppHost.Run(() =>
    {
        var calls = new List<string>();
        var (vm, _) = NewSources(antigravityInstalled: () => true, agentVaultOn: calls.Add);
        var row = Assert.Single(vm.ActivityRows);
        var settings = ScratchPaths().SettingsFile;
        File.SetAttributes(settings, File.GetAttributes(settings) | FileAttributes.ReadOnly);
        try
        {
            row.On = true;
            Assert.False(row.On);   // reloaded from what is saved
            Assert.Empty(calls);    // the archive was not turned on for a switch that did not save
        }
        finally { File.SetAttributes(settings, File.GetAttributes(settings) & ~FileAttributes.ReadOnly); }
    });

    [Fact]
    public void The_Codex_and_Antigravity_rows_say_their_token_counts_are_archived() => AppHost.Run(() =>
    {
        var (vm, _) = NewSources(antigravityInstalled: () => true, agentVaultOn: _ => { });
        Assert.Equal(SettingsNames.AgentArchiveCaption, vm.Rows.Single(r => r.Source == AgentSources.Codex).ArchiveCaption);
        Assert.Null(vm.Rows.Single(r => r.Source == AgentSources.Claude).ArchiveCaption);
        var agy = Assert.Single(vm.ActivityRows);
        Assert.Equal(SettingsNames.AgentArchiveCaption, agy.ArchiveCaption);
        Assert.Contains(SettingsNames.AgentArchiveCaption, agy.HelpText);
        Assert.StartsWith(agy.Caption, agy.HelpText);
    });

    [Fact]
    public void Rows_follow_AgentSources_order() => AppHost.Run(() =>
    {
        var (vm, _, _) = Build(codexInstalled: true);
        Assert.Equal(SourceIds, vm.Rows.Select(r => r.Source).ToArray());
        Assert.Equal(["Claude", "Codex"], vm.Rows.Select(r => r.Name).ToArray());
    });

    [Fact]
    public void Codex_without_a_codex_folder_is_off_and_says_it_is_not_installed() => AppHost.Run(() =>
    {
        var (vm, _, _) = Build(codexInstalled: false);
        var codex = vm.Rows.Single(r => r.Source == AgentSources.Codex);
        Assert.False(codex.Installed);
        Assert.False(codex.On);
        Assert.False(codex.CanToggle);
        Assert.Equal("Codex isn't installed on this PC.", codex.NotInstalledText);
        var claude = vm.Rows.Single(r => r.Source == AgentSources.Claude);
        Assert.True(claude.Installed);
        Assert.Null(claude.NotInstalledText);
    });

    [Fact]
    public void Turning_Codex_on_writes_setup_usage_codex_and_asks_App_to_start_it() => AppHost.Run(() =>
    {
        var (vm, store, _) = Build(codexInstalled: true);
        int started = 0;
        vm.OnOffChanged = (_, _) => started++;
        var codex = vm.Rows.Single(r => r.Source == AgentSources.Codex);
        Assert.True(codex.CanToggle);
        codex.On = true;
        Assert.True(store.LoadSetup()!.CodexUsage);
        Assert.Equal(1, started);
        codex.On = false;
        Assert.False(store.LoadSetup()!.CodexUsage);
        Assert.Equal(2, started);
    });

    [Fact]
    public void Turning_Claude_on_goes_through_the_widgets_own_switch() => AppHost.Run(() =>
    {
        var (vm, store, claudeOn) = Build(codexInstalled: true);
        int redraws = 0;
        vm.OnOffChanged = (_, _) => redraws++;
        vm.Rows.Single(r => r.Source == AgentSources.Claude).On = true;
        Assert.True(claudeOn());
        Assert.True(store.LoadSetup()!.ClaudeUsage);
        Assert.Equal(1, redraws);
    });

    [Fact]
    public void Making_Codex_primary_then_turning_it_off_leaves_Claude_primary() => AppHost.Run(() =>
    {
        var (vm, store, _) = Build(codexInstalled: true);
        var codex = vm.Rows.Single(r => r.Source == AgentSources.Codex);
        codex.On = true;
        codex.IsPrimary = true;
        Assert.Equal(AgentSources.Codex, store.LoadSources().Primary);
        Assert.True(codex.IsPrimary);
        codex.On = false;
        Assert.Equal(AgentSources.Claude, store.LoadSources().Primary);
        Assert.False(codex.IsPrimary);
        Assert.True(vm.Rows.Single(r => r.Source == AgentSources.Claude).IsPrimary);
    });

    [Fact]
    public void Primary_moves_to_the_other_source_that_is_on_when_the_primary_goes_off() => AppHost.Run(() =>
    {
        var (vm, store, _) = Build(codexInstalled: true, claudeOn: true);
        var claude = vm.Rows.Single(r => r.Source == AgentSources.Claude);
        var codex = vm.Rows.Single(r => r.Source == AgentSources.Codex);
        codex.On = true;
        Assert.Equal(AgentSources.Claude, store.LoadSources().Primary);
        claude.On = false;
        Assert.Equal(AgentSources.Codex, store.LoadSources().Primary);
        Assert.True(codex.IsPrimary);
        Assert.False(claude.CanBePrimary);
    });

    [Fact]
    public void A_source_that_is_off_cannot_be_made_primary() => AppHost.Run(() =>
    {
        var (vm, store, _) = Build(codexInstalled: true, claudeOn: true);
        var codex = vm.Rows.Single(r => r.Source == AgentSources.Codex);
        Assert.False(codex.CanBePrimary);
        codex.IsPrimary = true;
        Assert.Equal(AgentSources.Claude, store.LoadSources().Primary);
        Assert.False(codex.IsPrimary);
    });

    [Fact]
    public void A_color_that_is_not_hex_is_refused_with_the_line_and_a_hex_one_is_saved() => AppHost.Run(() =>
    {
        var (vm, store, _) = Build(codexInstalled: true);
        var codex = vm.Rows.Single(r => r.Source == AgentSources.Codex);
        string before = codex.Color;
        codex.Color = "teal";
        Assert.Equal("Use a color like #2FB5A3.", codex.ColorMessage);
        Assert.True(codex.HasColorMessage);
        Assert.Equal(before, codex.Color);   // the old value stays
        Assert.False(store.LoadSources().Colors.ContainsKey(AgentSources.Codex));
        codex.Color = "#2FB5A3";
        Assert.Equal("", codex.ColorMessage);
        Assert.Equal("#2FB5A3", store.LoadSources().Colors[AgentSources.Codex]);
        codex.Color = "#112233";
        Assert.Equal("#112233", store.LoadSources().ColorOf(AgentSources.Codex));
    });

    [Fact]
    public void A_refused_color_keeps_the_color_saved_before_it() => AppHost.Run(() =>
    {
        var (vm, store, _) = Build(codexInstalled: true);
        var codex = vm.Rows.Single(r => r.Source == AgentSources.Codex);
        codex.Color = "#112233";
        codex.Color = "#12345";
        Assert.Equal("#112233", codex.Color);
        Assert.Equal("#112233", store.LoadSources().ColorOf(AgentSources.Codex));
    });

    [Fact]
    public void Reset_puts_the_source_color_back_to_its_default() => AppHost.Run(() =>
    {
        var (vm, store, _) = Build(codexInstalled: true);
        var codex = vm.Rows.Single(r => r.Source == AgentSources.Codex);
        codex.Color = "#112233";
        int redraws = 0;
        vm.DisplayChanged = () => redraws++;
        Assert.True(codex.ResetColor.CanExecute(null));
        codex.ResetColor.Execute(null);
        Assert.Equal(AgentSources.DefaultColor(AgentSources.Codex), codex.Color);
        Assert.False(store.LoadSources().Colors.ContainsKey(AgentSources.Codex));
        Assert.Equal(1, redraws);
    });

    [Fact]
    public void Alerts_save_per_source_and_default_to_on() => AppHost.Run(() =>
    {
        var (vm, store, _) = Build(codexInstalled: true);
        var codex = vm.Rows.Single(r => r.Source == AgentSources.Codex);
        Assert.True(codex.AlertsOn);
        codex.AlertsOn = false;
        Assert.False(store.LoadSources().AlertsOn(AgentSources.Codex));
        Assert.True(store.LoadSources().AlertsOn(AgentSources.Claude));
        vm.Reload();
        Assert.False(codex.AlertsOn);
    });

    [Fact]
    public void Codex_left_on_from_before_can_still_be_turned_off_when_it_is_no_longer_installed() => AppHost.Run(() =>
    {
        var store = new SettingsStore(ScratchPaths());
        store.SaveDesk(store.LoadDesk() with { Enabled = true });
        store.SaveSetup(new SetupSettings(true, new Dictionary<string, bool> { ["claude"] = false, ["codex"] = true }));
        var vm = new SourcesViewModel(store, () => false, _ => { }, () => CodexHomes.Discover(null, Profile), () => false, _ => { });
        var codex = vm.Rows.Single(r => r.Source == AgentSources.Codex);
        Assert.False(codex.Installed);
        Assert.True(codex.On);
        Assert.True(codex.CanToggle);
        codex.On = false;
        Assert.False(store.LoadSetup()!.CodexUsage);
        Assert.False(codex.CanToggle);
    });

    [Fact]
    public void The_last_source_cannot_be_turned_off_while_the_Desk_is_off_and_says_why() => AppHost.Run(() =>
    {
        var (vm, store, _) = Build(codexInstalled: true, deskOn: false);
        var codex = vm.Rows.Single(r => r.Source == AgentSources.Codex);
        codex.On = true;
        Assert.Equal("", vm.Message);
        int changed = 0;
        vm.OnOffChanged = (_, _) => changed++;
        codex.On = false;
        Pump();
        Assert.Equal("Keep one on, or turn on the Desk first.", vm.Message);
        Assert.True(vm.HasMessage);
        Assert.True(store.LoadSetup()!.CodexUsage);   // nothing was saved
        Assert.True(codex.On);                        // the switch is back on
        Assert.Equal(0, changed);
        // Another source on lets it go; the line clears.
        vm.Rows.Single(r => r.Source == AgentSources.Claude).On = true;
        codex.On = false;
        Assert.Equal("", vm.Message);
        Assert.False(store.LoadSetup()!.CodexUsage);
    });

    [Fact]
    public void With_the_Desk_on_the_last_source_can_be_turned_off() => AppHost.Run(() =>
    {
        var (vm, store, _) = Build(codexInstalled: true, deskOn: true);
        var codex = vm.Rows.Single(r => r.Source == AgentSources.Codex);
        codex.On = true;
        codex.On = false;
        Assert.Equal("", vm.Message);
        Assert.False(store.LoadSetup()!.CodexUsage);
    });

    [Fact]
    public void Switching_a_source_calls_App_once_so_it_starts_or_stops_the_source_and_shows_or_hides_the_widget() => AppHost.Run(() =>
    {
        var (vm, _, _) = Build(codexInstalled: true);
        var seen = new List<(bool Was, bool Now)>();
        vm.OnOffChanged = (was, now) => seen.Add((was, now));
        var claude = vm.Rows.Single(r => r.Source == AgentSources.Claude);
        var codex = vm.Rows.Single(r => r.Source == AgentSources.Codex);
        claude.On = true;
        codex.On = true;
        codex.On = false;
        claude.On = false;
        Assert.Equal([(false, true), (true, true), (true, true), (true, false)], seen);
    });

    [Theory]
    [InlineData(false, true, WidgetVisibility.Show)]
    [InlineData(true, false, WidgetVisibility.Hide)]
    [InlineData(true, true, WidgetVisibility.Leave)]
    [InlineData(false, false, WidgetVisibility.Leave)]
    public void Only_a_transition_shows_or_hides_the_widget(bool was, bool now, WidgetVisibility expected) =>
        Assert.Equal(expected, SourcesViewModel.VisibilityFor(was, now));

    [Fact]
    public void A_repeated_refusal_blanks_the_line_and_says_it_again_once_and_a_visit_clears_it() => AppHost.Run(() =>
    {
        var (vm, _, _) = Build(codexInstalled: true, deskOn: false);
        var codex = vm.Rows.Single(r => r.Source == AgentSources.Codex);
        codex.On = true;
        var seen = new List<string>();
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(SourcesViewModel.Message)) seen.Add(vm.Message); };
        codex.On = false;
        Pump();
        Assert.Equal([SettingsNames.SourcesKeepOne], seen);   // set once: no blank first
        codex.On = false;
        Pump();
        Assert.Equal([SettingsNames.SourcesKeepOne, "", SettingsNames.SourcesKeepOne], seen);   // blank now, set after
        vm.Reload();
        Assert.Equal("", vm.Message);
    });

    [Fact]
    public void The_Sources_live_lines_are_collapsed_while_empty_and_announce_when_they_fill() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        var window = new SettingsWindow(svm);
        OffScreen(window);
        window.Show();
        try
        {
            window.SelectPage(SettingsPage.UsageSources);
            window.UpdateLayout();
            Pump();
            var tab = ((TabControl)window.FindName("Tabs")).Items.OfType<TabItem>().Single(t => (string)t.Tag == "UsageSources");
            var live = new List<TextBlock>();
            void Collect(DependencyObject d)
            {
                if (d is TextBlock { } tb && AutomationProperties.GetLiveSetting(tb) == AutomationLiveSetting.Assertive) live.Add(tb);
                for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); i++)
                    Collect(System.Windows.Media.VisualTreeHelper.GetChild(d, i));
            }
            Collect((DependencyObject)tab.Content);
            Assert.Equal(1 + AgentSources.All.Count, live.Count);   // the page line and one color line per source
            // Empty, a line is collapsed: Narrator stopped on each blank one as "text" (4.8.0 hand pass). Visible-when-empty was the
            // earlier fix for a collapsed line that never spoke; LiveRegion.Announces now raises the event once layout shows it.
            Assert.All(live, tb =>
            {
                Assert.Equal(Visibility.Collapsed, tb.Visibility);
                Assert.Equal("", tb.Text);
                Assert.True(LiveRegion.GetAnnounces(tb));
            });
        }
        finally { window.Close(); }
    });

    [Fact]
    public void A_refused_color_reads_as_the_saved_one_in_the_real_box_with_the_line_showing() => AppHost.Run(() =>
    {
        Directory.CreateDirectory(CodexHome);
        var (widget, _) = ScratchWidget("Personal");
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        var window = new SettingsWindow(svm);
        OffScreen(window);
        window.Show();
        try
        {
            window.SelectPage(SettingsPage.UsageSources);
            window.UpdateLayout();
            Pump();
            var tab = ((TabControl)window.FindName("Tabs")).Items.OfType<TabItem>().Single(t => (string)t.Tag == "UsageSources");
            var box = FindBox((DependencyObject)tab.Content, "Codex color");
            string saved = box.Text;
            Assert.Equal(AgentSources.DefaultColor(AgentSources.Codex), saved);

            box.Text = "teal";
            box.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Pump();
            Assert.Equal(saved, box.Text);
            var row = svm.Sources.Rows.Single(r => r.Source == AgentSources.Codex);
            Assert.Equal(SettingsNames.SourcesColorRefused, row.ColorMessage);
            Assert.Contains(SettingsNames.SourcesColorRefused, NamesOf(window));

            // A valid entry with spaces shows as it was saved, trimmed, and the line goes.
            box.Text = " #112233 ";
            box.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Pump();
            Assert.Equal("#112233", box.Text);
            Assert.Equal("", row.ColorMessage);
            Assert.Equal("#112233", new SettingsStore(new Paths()).LoadSources().ColorOf(AgentSources.Codex));
        }
        finally { window.Close(); }
    });

    private static TextBox FindBox(DependencyObject root, string name)
    {
        var found = FindBoxOrNull(root, name);
        return found ?? throw new InvalidOperationException("no box named " + name);
    }

    private static TextBox? FindBoxOrNull(DependencyObject root, string name)
    {
        if (root is TextBox t && AutomationProperties.GetName(t) == name) return t;
        int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
            if (FindBoxOrNull(System.Windows.Media.VisualTreeHelper.GetChild(root, i), name) is { } hit) return hit;
        return null;
    }

    [Fact]
    public void Claude_alerts_off_on_the_Sources_page_deliver_nothing_and_on_deliver() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        var store = new SettingsStore(ScratchPaths());
        var delivered = new List<AlertEvent>();
        widget.AlertDelivery = (e, _, _, _) => delivered.Add(e);
        JsonObject Data(int pct) => new() { ["five_hour"] = new JsonObject { ["utilization"] = (double)pct, ["resets_at"] = "2030-01-01T00:00:00+00:00" } };

        store.SaveSources(store.LoadSources() with { Alerts = new Dictionary<string, bool> { [AgentSources.Claude] = false } });
        widget.EvaluateAlerts(Data(85), DateTimeOffset.UtcNow);
        Assert.Empty(delivered);

        // The same crossing with alerts on (the default) is delivered.
        store.SaveSources(store.LoadSources() with { Alerts = new Dictionary<string, bool> { [AgentSources.Claude] = true } });
        var (other, _) = ScratchWidget("Other");
        other.AlertDelivery = (e, _, _, _) => delivered.Add(e);
        other.EvaluateAlerts(Data(85), DateTimeOffset.UtcNow);
        Assert.Equal(AlertKind.Warn, Assert.Single(delivered).Kind);
    });

    [Fact]
    public void The_Sources_page_opens_under_Usage_with_every_control_named() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        var window = new SettingsWindow(svm);
        OffScreen(window);
        window.Show();
        try
        {
            window.SelectPage(SettingsPage.UsageSources);
            window.UpdateLayout();
            Pump();
            var tabs = ((TabControl)window.FindName("Tabs")).Items.OfType<TabItem>().ToList();
            var tab = tabs.Single(t => (string)t.Tag == "UsageSources");
            Assert.True(tab.IsSelected);
            Assert.Equal("Usage", SettingsSidebar.GetGroupHeader(tab));
            Assert.NotNull(SettingsAnchor.Find((DependencyObject)tab.Content, SettingsIndex.SourcesList));

            var names = NamesOf(window);
            Assert.Empty(LeaksIn(names));
            foreach (var name in new[] { "Codex color", "Claude color", "Track Claude", "Track Codex", "Claude primary", "Codex primary",
                "Claude alerts", "Codex alerts", "Reset Claude color", "Reset Codex color" })
                Assert.Contains(name, names);
            // Store 10.1.4.4: the page names Claude and Codex, so both disclaimers are on it.
            Assert.Contains(SettingsNames.TrademarkNotice, names);
            Assert.Contains(SettingsNames.CodexNotice, names);
            Assert.Contains(SettingsNames.AntigravityNotice, names);
            Assert.Contains(SettingsNames.SourcesIntro, names);
            // Every control that takes a click or a key reads as words: On, Primary, the color box, Reset and Alerts, once per source, plus one switch per activity-only source.
            var page = (FrameworkElement)tab.Content;
            var controls = Walk(UIElementAutomationPeer.CreatePeerForElement(page))
                .Where(p => p.GetAutomationControlType() is AutomationControlType.Button or AutomationControlType.CheckBox
                    or AutomationControlType.RadioButton or AutomationControlType.Edit)
                .ToList();
            Assert.Equal(AgentSources.All.Count * 5 + AgentSources.ActivityOnly.Count, controls.Count);
            Assert.All(controls, p => Assert.False(string.IsNullOrWhiteSpace(p.GetName())));
        }
        finally { window.Close(); }
    });
}
