using System.IO;
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
    private (SourcesViewModel Vm, SettingsStore Store, Func<bool> ClaudeOn) Build(bool codexInstalled, bool claudeOn = false)
    {
        if (codexInstalled) Directory.CreateDirectory(CodexHome);
        var store = new SettingsStore(ScratchPaths());
        bool claude = claudeOn;
        var vm = new SourcesViewModel(store, () => claude,
            on =>
            {
                claude = on;
                store.SaveSetup((store.LoadSetup() ?? SetupChoices.FirstRun.ToSettings(null)).WithClaude(on));
            },
            () => CodexHomes.Discover(null, Profile));
        return (vm, store, () => claude);
    }

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
        vm.OnOffChanged = () => started++;
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
        vm.DisplayChanged = () => redraws++;
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
        store.SaveSetup(new SetupSettings(true, new Dictionary<string, bool> { ["claude"] = false, ["codex"] = true }));
        var vm = new SourcesViewModel(store, () => false, _ => { }, () => CodexHomes.Discover(null, Profile));
        var codex = vm.Rows.Single(r => r.Source == AgentSources.Codex);
        Assert.False(codex.Installed);
        Assert.True(codex.On);
        Assert.True(codex.CanToggle);
        codex.On = false;
        Assert.False(store.LoadSetup()!.CodexUsage);
        Assert.False(codex.CanToggle);
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
            Assert.Contains("Codex color", names);
            Assert.Contains("Claude color", names);
            Assert.Contains(SettingsNames.SourcesIntro, names);
            // Every control that takes a click or a key reads as words: On, Primary, the color box, Reset and Alerts, once per source.
            var page = (FrameworkElement)tab.Content;
            var controls = Walk(UIElementAutomationPeer.CreatePeerForElement(page))
                .Where(p => p.GetAutomationControlType() is AutomationControlType.Button or AutomationControlType.CheckBox
                    or AutomationControlType.RadioButton or AutomationControlType.Edit)
                .ToList();
            Assert.Equal(AgentSources.All.Count * 5, controls.Count);
            Assert.All(controls, p => Assert.False(string.IsNullOrWhiteSpace(p.GetName())));
        }
        finally { window.Close(); }
    });
}
