using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using Sanduhr.App.Services;
using Sanduhr.App.Updates;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings v2, slice 6: About, Updates. The switch writes "update_check" where the checker reads it; Check now asks once with the
/// switch off and says what it found, aloud; the Store build shows its line instead.</summary>
[Collection("SettingsWindow")]
public sealed class AboutUpdatesTests : AppWindowFixture
{
    private (SettingsWindow Window, SettingsViewModel Svm, WidgetViewModel Widget, NoActivate Guard) Open(
        Func<Task<UpdateProbeResult>>? checkNow = null, bool isStoreBuild = false, Func<string?>? armBuildUrl = null)
    {
        var (widget, _) = ScratchWidget("Personal");
        // The tests say what the machine is; null is "this is not an x64 build on an Arm PC", whatever PC runs them.
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask,
            checkForUpdatesNow: checkNow, isStoreBuild: isStoreBuild, armBuildUrl: armBuildUrl ?? (() => null));
        var window = new SettingsWindow(svm);
        var guard = NoActivate.Show(window);
        window.SelectPage(SettingsPage.About, SettingsIndex.Updates);
        Settle(window, guard);
        return (window, svm, widget, guard);
    }

    private static void Settle(SettingsWindow window, NoActivate guard)
    {
        window.UpdateLayout();
        Pump();
        window.UpdateLayout();
        Pump();
        guard.Check();
    }

    private static IEnumerable<T> Below<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var deeper in Below<T>(child)) yield return deeper;
        }
    }

    private static string Spoken(UIElement e) => UIElementAutomationPeer.CreatePeerForElement(e)?.GetName() ?? "";

    private static T Named<T>(DependencyObject root, string name) where T : UIElement => Below<T>(root).Single(e => Spoken(e) == name);

    [Fact]
    public void The_switch_writes_update_check_where_the_checker_reads_it() => AppHost.Run(() =>
    {
        var (window, _, widget, guard) = Open();
        try
        {
            var box = Named<CheckBox>(window, SettingsNames.CheckForUpdatesDaily);
            Assert.True(box.IsVisible);
            Assert.True(box.IsChecked);
            box.IsChecked = false;
            // App builds the checker over new SettingsStore(new Paths()).LoadUpdateCheck: the same file.
            Assert.False(new SettingsStore(new Paths()).LoadUpdateCheck());
        }
        finally { window.Close(); widget.Dispose(); }
    });

    [Fact]
    public void The_switch_reads_the_file_again_on_each_About_visit() => AppHost.Run(() =>
    {
        var (window, svm, widget, guard) = Open();
        try
        {
            Assert.True(svm.AboutUpdates.CheckDaily);
            new SettingsStore(new Paths()).SaveUpdateCheck(false);
            window.SelectPage(SettingsPage.General);
            Settle(window, guard);
            window.SelectPage(SettingsPage.About, SettingsIndex.Updates);
            Settle(window, guard);
            Assert.False(svm.AboutUpdates.CheckDaily);
            Assert.False(Named<CheckBox>(window, SettingsNames.CheckForUpdatesDaily).IsChecked);
        }
        finally { window.Close(); widget.Dispose(); }
    });

    [Fact]
    public void Check_now_asks_with_the_switch_off_and_says_what_it_found_aloud() => AppHost.Run(() =>
    {
        int asked = 0;
        var raised = new List<UIElement>();
        LiveRegion.Raised = raised.Add;
        var (window, svm, widget, guard) = Open(() =>
        {
            asked++;
            return Task.FromResult(new UpdateProbeResult(UpdateProbeStatus.UpToDate));
        });
        try
        {
            Named<CheckBox>(window, SettingsNames.CheckForUpdatesDaily).IsChecked = false;
            var button = Named<Button>(window, SettingsNames.CheckNow);
            Assert.True(button.IsEnabled);
            button.Command.Execute(null);
            Settle(window, guard);

            Assert.Equal(1, asked);
            Assert.Equal(SettingsNames.UpdateUpToDate, svm.AboutUpdates.Status);
            Assert.Contains(raised, e => e is TextBlock { Text: var text } && text == SettingsNames.UpdateUpToDate);
        }
        finally { LiveRegion.Raised = null; window.Close(); widget.Dispose(); }
    });

    [Fact]
    public void The_status_flag_flips_for_each_new_message_so_the_line_is_announced_again() => AppHost.Run(() =>
    {
        var flips = new List<bool>();
        var (window, svm, widget, guard) = Open(() => Task.FromResult(new UpdateProbeResult(UpdateProbeStatus.UpToDate)));
        try
        {
            svm.AboutUpdates.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AboutUpdatesViewModel.StatusShown))
                    flips.Add(svm.AboutUpdates.StatusShown);
            };
            Assert.False(svm.AboutUpdates.StatusShown);
            svm.AboutUpdates.CheckNowCommand.Execute(null);
            Settle(window, guard);
            svm.AboutUpdates.CheckNowCommand.Execute(null);
            Settle(window, guard);
            // Four messages (Checking…, the answer, twice): the first rises from false, each later one drops and rises again.
            Assert.Equal([true, false, true, false, true, false, true], flips);
            Assert.True(svm.AboutUpdates.StatusShown);
            var line = Below<TextBlock>(window).Single(t => t.Text == SettingsNames.UpdateUpToDate);
            Assert.True(LiveRegion.GetShown(line));
        }
        finally { window.Close(); widget.Dispose(); }
    });

    [Fact]
    public void A_found_update_names_its_version() => AppHost.Run(() =>
    {
        var (window, svm, widget, guard) = Open(() => Task.FromResult(new UpdateProbeResult(UpdateProbeStatus.Ready, "4.0.1")));
        try
        {
            svm.AboutUpdates.CheckNowCommand.Execute(null);
            Settle(window, guard);
            Assert.Equal(SettingsNames.UpdateReady("4.0.1"), svm.AboutUpdates.Status);
        }
        finally { window.Close(); widget.Dispose(); }
    });

    [Fact]
    public void Without_a_checker_Check_now_is_dimmed_not_dead() => AppHost.Run(() =>
    {
        var (window, _, widget, _) = Open();
        try { Assert.False(Named<Button>(window, SettingsNames.CheckNow).IsEnabled); }
        finally { window.Close(); widget.Dispose(); }
    });

    [Fact]
    public void Check_nows_caption_is_one_visible_line_not_also_the_buttons_help_text() => AppHost.Run(() =>
    {
        var (window, _, widget, _) = Open();
        try
        {
            Assert.Equal("", AutomationProperties.GetHelpText(Named<Button>(window, SettingsNames.CheckNow)));
            Assert.Single(Below<TextBlock>(window), t => t.IsVisible && t.Text == SettingsNames.CheckNowCaption);
        }
        finally { window.Close(); widget.Dispose(); }
    });

    [Fact]
    public void The_Store_build_shows_its_line_instead_of_the_switch() => AppHost.Run(() =>
    {
        var (window, _, widget, _) = Open(isStoreBuild: true);
        try
        {
            Assert.DoesNotContain(Below<CheckBox>(window), c => c.IsVisible && Spoken(c) == SettingsNames.CheckForUpdatesDaily);
            Assert.DoesNotContain(Below<Button>(window), b => b.IsVisible && Spoken(b) == SettingsNames.CheckNow);
            Assert.Contains(Below<TextBlock>(window), t => t.IsVisible && t.Text == SettingsNames.StoreKeepsUpToDate);
        }
        finally { window.Close(); widget.Dispose(); }
    });

    [Fact]
    public void The_GitHub_build_hides_the_Store_line_and_the_build_is_told_by_the_rule_the_Store_service_uses() => AppHost.Run(() =>
    {
        var (window, svm, widget, _) = Open(isStoreBuild: false);
        try
        {
            Assert.Contains(Below<CheckBox>(window), c => c.IsVisible && Spoken(c) == SettingsNames.CheckForUpdatesDaily);
            Assert.DoesNotContain(Below<TextBlock>(window), t => t.IsVisible && t.Text == SettingsNames.StoreKeepsUpToDate);
            Assert.False(svm.AboutUpdates.IsStoreBuild);
            Assert.True(svm.AboutUpdates.IsGitHubBuild);
            // No override: StoreUpdateService's own rule, Startup.IsPackaged() (an unpackaged test host is the GitHub build).
            var (w2, _) = ScratchWidget("Personal");
            using (w2)
            {
                var defaulted = new SettingsViewModel(w2, () => Task.CompletedTask, _ => Task.CompletedTask);
                Assert.Equal(Startup.IsPackaged(), defaulted.AboutUpdates.IsStoreBuild);
            }
        }
        finally { window.Close(); widget.Dispose(); }
    });

    private const string ArmUrl = "https://example.invalid/releases?q=v5&expanded=true";

    [Fact]
    public void An_x64_build_on_an_Arm_pc_shows_the_line_and_the_link() => AppHost.Run(() =>
    {
        var (window, svm, widget, _) = Open(armBuildUrl: () => ArmUrl);
        try
        {
            Assert.True(svm.AboutUpdates.ArmBuildAvailable);
            var line = Below<TextBlock>(window).Single(t => t.Text == ArmBuildNotice.AboutLine);
            Assert.True(line.IsVisible);
            var button = Named<Button>(window, ArmBuildNotice.GetText);
            Assert.True(button.IsVisible);
            Assert.Same(svm.AboutUpdates.GetArmBuildCommand, button.Command);
        }
        finally { window.Close(); widget.Dispose(); }
    });

    [Fact]
    public void Any_other_pc_shows_neither() => AppHost.Run(() =>
    {
        var (window, svm, widget, _) = Open();
        try
        {
            Assert.False(svm.AboutUpdates.ArmBuildAvailable);
            Assert.DoesNotContain(Below<TextBlock>(window), t => t.Text == ArmBuildNotice.AboutLine && t.IsVisible);
            Assert.DoesNotContain(Below<Button>(window), b => Spoken(b) == ArmBuildNotice.GetText && b.IsVisible);
        }
        finally { window.Close(); widget.Dispose(); }
    });

    [Fact]
    public void The_Store_build_never_shows_it() => AppHost.Run(() =>
    {
        var (window, svm, widget, _) = Open(isStoreBuild: true, armBuildUrl: () => ArmUrl);
        try
        {
            Assert.False(svm.AboutUpdates.ArmBuildAvailable);
            Assert.DoesNotContain(Below<Button>(window), b => Spoken(b) == ArmBuildNotice.GetText && b.IsVisible);
        }
        finally { window.Close(); widget.Dispose(); }
    });

    [Fact]
    public void The_link_opens_the_release_list() => AppHost.Run(() =>
    {
        // Open() first: it points the settings paths at this test's scratch folder.
        var (window, _, widget, _) = Open();
        try
        {
            string? opened = null;
            var vm = new AboutUpdatesViewModel(new SettingsStore(new Paths()), isStoreBuild: false, checkNow: null,
                armBuildUrl: ArmUrl, openLink: url => opened = url);
            vm.GetArmBuildCommand.Execute(null);
            Assert.Equal(ArmUrl, opened);
        }
        finally { window.Close(); widget.Dispose(); }
    });

    [Fact]
    public void The_shown_flag_round_trips() => AppHost.Run(() =>
    {
        var (window, _, widget, _) = Open();
        try
        {
            Assert.False(new SettingsStore(new Paths()).LoadArmBuildNoticeShown());
            new SettingsStore(new Paths()).SaveArmBuildNoticeShown();
            Assert.True(new SettingsStore(new Paths()).LoadArmBuildNoticeShown());
        }
        finally { window.Close(); widget.Dispose(); }
    });
}
