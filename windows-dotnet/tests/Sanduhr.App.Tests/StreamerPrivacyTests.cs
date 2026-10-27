using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using Sanduhr.App.Services;
using Sanduhr.App.Updates;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings v2, slice 7: Privacy, Streamer mode; the allowlist; the banner on every masking page; the window's one new subscription.</summary>
[Collection("SettingsWindow")]
public sealed class StreamerPrivacyTests : AppWindowFixture
{
    private (SettingsWindow Window, SettingsViewModel Svm, WidgetViewModel Widget, NoActivate Guard) Open()
    {
        var (widget, _) = ScratchWidget("Qxalpha", "Qxbeta");
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask,
            checkForUpdatesNow: () => Task.FromResult(new UpdateProbeResult(UpdateProbeStatus.UpToDate)), isStoreBuild: false);
        var window = new SettingsWindow(svm);
        return (window, svm, widget, NoActivate.Show(window));
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

    private static TabItem Tab(SettingsWindow window, SettingsPage page) =>
        ((TabControl)window.FindName("Tabs")).Items.OfType<TabItem>().Single(t => (string)t.Tag == page.ToString());

    [Fact]
    public void The_switch_on_Privacy_turns_streamer_mode_on_and_off() => AppHost.Run(() =>
    {
        var (window, svm, widget, guard) = Open();
        try
        {
            window.SelectPage(SettingsPage.Privacy, SettingsIndex.Streamer);
            Settle(window, guard);
            var box = Below<CheckBox>(window).Single(c => Spoken(c) == SettingsNames.StreamerSwitch);
            box.IsChecked = true;
            Assert.True(widget.Streamer.On);
            Assert.True(StreamerMode.On);
            box.IsChecked = false;
            Assert.False(widget.Streamer.On);
        }
        finally { window.Close(); widget.Dispose(); }
    });

    [Fact]
    public void Turning_it_on_from_the_tray_ticks_the_open_switch_and_reloads_the_names() => AppHost.Run(() =>
    {
        var (window, svm, widget, guard) = Open();
        try
        {
            window.SelectPage(SettingsPage.Privacy, SettingsIndex.Streamer);
            Settle(window, guard);
            int before = svm.NamesReloaded;
            widget.Streamer.SetOn(true);   // the tray's item writes the service, as the switch does
            Settle(window, guard);
            Assert.True(Below<CheckBox>(window).Single(c => Spoken(c) == SettingsNames.StreamerSwitch).IsChecked);
            Assert.Equal(before + 1, svm.NamesReloaded);
        }
        finally { window.Close(); widget.Dispose(); }
    });

    /// <summary>Decision 15: the window's one subscription to the long-lived service is gone after Close.</summary>
    [Fact]
    public void Closing_settings_detaches_it_from_the_service() => AppHost.Run(() =>
    {
        var (window, svm, widget, guard) = Open();
        try
        {
            Settle(window, guard);
            int heard = 0;
            svm.Privacy.Streamer!.NamesChanged += () => heard++;
            window.Close();
            int reloaded = svm.NamesReloaded;
            widget.Streamer.SetOn(true);
            Assert.Equal(0, heard);
            Assert.Equal(reloaded, svm.NamesReloaded);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void Shuffle_asks_first_and_cancel_changes_nothing() => AppHost.Run(() =>
    {
        var (window, svm, widget, guard) = Open();
        try
        {
            widget.Streamer.SetOn(true);
            var code = StreamerMode.Mask.Name(CodenameCategory.Account, "Qxalpha");
            var streamer = svm.Privacy.Streamer!;
            streamer.ConfirmShuffleHook = () => false;
            streamer.ShuffleCommand.Execute(null);
            Assert.Equal(code, StreamerMode.Mask.Name(CodenameCategory.Account, "Qxalpha"));
            streamer.ConfirmShuffleHook = () => true;
            streamer.ShuffleCommand.Execute(null);
            Assert.NotEqual(code, StreamerMode.Mask.Name(CodenameCategory.Account, "Qxalpha"));
        }
        finally { window.Close(); widget.Dispose(); }
    });

    /// <summary>Spec, the allowlist: while on, the list shows codenames only; off, each codename beside its real name.</summary>
    [Fact]
    public void The_allowlist_shows_codenames_only_while_on() => AppHost.Run(() =>
    {
        var (window, svm, widget, guard) = Open();
        try
        {
            widget.Streamer.SetOn(true);
            var code = StreamerMode.Mask.Name(CodenameCategory.Account, "Qxalpha");
            var streamer = svm.Privacy.Streamer!;
            streamer.Read();
            var row = streamer.Rows.Single(r => r.Category == CodenameCategory.Account && r.Real == "Qxalpha");
            Assert.Equal(code, row.Text);
            Assert.Equal(SettingsNames.AllowlistRowName(code), row.Name);
            Assert.All(streamer.Rows, r => Assert.DoesNotContain("qx", r.Text + r.Name, StringComparison.OrdinalIgnoreCase));

            widget.Streamer.SetOn(false);
            row = streamer.Rows.Single(r => r.Category == CodenameCategory.Account && r.Real == "Qxalpha");
            Assert.Equal(SettingsNames.AllowlistRowOff(code, "Qxalpha"), row.Text);
        }
        finally { window.Close(); widget.Dispose(); }
    });

    [Fact]
    public void Ticking_a_row_shows_that_name_and_keeps_the_row() => AppHost.Run(() =>
    {
        var (window, svm, widget, guard) = Open();
        try
        {
            widget.Streamer.SetOn(true);
            StreamerMode.Mask.Name(CodenameCategory.Account, "Qxalpha");
            var streamer = svm.Privacy.Streamer!;
            streamer.Read();
            var row = streamer.Rows.Single(r => r.Real == "Qxalpha");
            row.Shown = true;
            Assert.Equal("Qxalpha", StreamerMode.Mask.Name(CodenameCategory.Account, "Qxalpha"));
            Assert.Same(row, streamer.Rows.Single(r => r.Real == "Qxalpha"));   // updated in place: the focus stays on it
            row.Shown = false;
            Assert.NotEqual("Qxalpha", StreamerMode.Mask.Name(CodenameCategory.Account, "Qxalpha"));
        }
        finally { window.Close(); widget.Dispose(); }
    });

    /// <summary>Decision 14: the banner shows on every masking page while on, and on none while off; the set is SettingsIndex.StreamerPages.</summary>
    [Fact]
    public void The_banner_shows_on_every_masking_page_and_only_while_on() => AppHost.Run(() =>
    {
        var (window, svm, widget, guard) = Open();
        try
        {
            var shown = new List<SettingsPage>();
            foreach (var page in SettingsNames.Pages)
            {
                window.SelectPage(page, null);
                Settle(window, guard);
                Assert.DoesNotContain(Below<StreamerBanner>((DependencyObject)Tab(window, page).Content), b => b.IsVisible);
            }
            widget.Streamer.SetOn(true);
            foreach (var page in SettingsNames.Pages)
            {
                window.SelectPage(page, null);
                Settle(window, guard);
                if (Below<StreamerBanner>((DependencyObject)Tab(window, page).Content).Any(b => b.IsVisible))
                    shown.Add(page);
            }
            Assert.Equal(SettingsIndex.StreamerPages.Order(), shown.Order());
        }
        finally { window.Close(); widget.Dispose(); }
    });

    [Fact]
    public void The_banners_link_lands_on_streamer_mode_and_is_spoken_with_it() => AppHost.Run(() =>
    {
        var (window, svm, widget, guard) = Open();
        try
        {
            widget.Streamer.SetOn(true);
            window.SelectPage(SettingsPage.Accounts, null);
            Settle(window, guard);
            var banner = Below<StreamerBanner>((DependencyObject)Tab(window, SettingsPage.Accounts).Content).Single(b => b.IsVisible);
            var link = Below<Button>(banner).Single();
            Assert.Equal(SettingsNames.StreamerBannerLinkName, Spoken(link));
            link.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Settle(window, guard);
            Assert.Equal(new SettingsLink(SettingsPage.Privacy, SettingsIndex.Streamer), window.Place);
        }
        finally { window.Close(); widget.Dispose(); }
    });

    /// <summary>The carry's order on Privacy: banner, jump links, Streamer mode, then What leaves this PC.</summary>
    [Fact]
    public void Privacy_puts_the_banner_then_the_jump_links_then_streamer_mode_first() => AppHost.Run(() =>
    {
        var (window, svm, widget, guard) = Open();
        try
        {
            widget.Streamer.SetOn(true);
            window.SelectPage(SettingsPage.Privacy, null);
            Settle(window, guard);
            var body = (Panel)((FrameworkElement)Tab(window, SettingsPage.Privacy).Content).FindName("PageBody");
            var kids = body.Children.OfType<FrameworkElement>().ToList();
            int banner = kids.FindIndex(k => k is StreamerBanner);
            int jumps = kids.FindIndex(k => k is SectionJumpLinks);
            int streamer = kids.FindIndex(k => SettingsAnchor.GetId(k) == SettingsIndex.Streamer);
            int network = kids.FindIndex(k => SettingsAnchor.GetId(k) == SettingsIndex.Network);
            Assert.True(banner >= 0 && banner < jumps && jumps < streamer && streamer < network, $"{banner} {jumps} {streamer} {network}");
        }
        finally { window.Close(); widget.Dispose(); }
    });

    /// <summary>Controller ruling: the banner appears and disappears at runtime, so it is a live region on a flag that flips.</summary>
    [Fact]
    public void The_banner_is_a_live_region_on_a_flag_that_flips() => AppHost.Run(() =>
    {
        var (window, svm, widget, guard) = Open();
        var announced = new List<string>();
        try
        {
            window.SelectPage(SettingsPage.Accounts, null);
            Settle(window, guard);
            var banner = Below<StreamerBanner>((DependencyObject)Tab(window, SettingsPage.Accounts).Content).Single();
            var line = Below<TextBlock>(banner).Single();
            Assert.False(LiveRegion.GetShown(line));
            LiveRegion.Raised = e => announced.Add((e as TextBlock)?.Text ?? "");
            widget.Streamer.SetOn(true);
            Settle(window, guard);
            Assert.True(LiveRegion.GetShown(line));
            Assert.Equal([SettingsNames.StreamerBanner], announced);
            widget.Streamer.SetOn(false);
            Settle(window, guard);
            Assert.False(LiveRegion.GetShown(line));
            Assert.Single(announced);
        }
        finally { LiveRegion.Raised = null; window.Close(); widget.Dispose(); }
    });

    /// <summary>Preflight P14: a confirmed Shuffle is announced; a cancelled one says nothing.</summary>
    [Fact]
    public void A_confirmed_shuffle_says_so_and_a_cancelled_one_does_not() => AppHost.Run(() =>
    {
        var (window, svm, widget, guard) = Open();
        try
        {
            widget.Streamer.SetOn(true);
            var streamer = svm.Privacy.Streamer!;
            Assert.False(streamer.ShuffleShown);
            streamer.ConfirmShuffleHook = () => false;
            streamer.ShuffleCommand.Execute(null);
            Assert.False(streamer.ShuffleShown);
            streamer.ConfirmShuffleHook = () => true;
            streamer.ShuffleCommand.Execute(null);
            Assert.True(streamer.ShuffleShown);
        }
        finally { window.Close(); widget.Dispose(); }
    });

    /// <summary>Spec: the allowlist reads "Dune Fox · ~\.claude-work" while off, grouped by category, the category heading on the first row only.</summary>
    [Fact]
    public void Rows_read_codename_alone_while_on_codename_and_real_name_while_off_and_group_by_category() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Qxalpha");
        try
        {
            widget.Streamer.SetOn(true);
            StreamerMode.Mask.Name(CodenameCategory.Account, "Qxalpha");
            StreamerMode.Mask.Name(CodenameCategory.Account, "Qxbeta");
            var folder = StreamerMode.Mask.Folder(@"C:\Qxhome\.claude-work");
            var vm = new StreamerSettingsViewModel(widget.Streamer, _ => @"~\.claude-work");
            try
            {
                Assert.Equal(2, vm.Rows.Count(r => r.Category == CodenameCategory.Account));
                Assert.Single(vm.Rows.Where(r => r.StartsCategory && r.Category == CodenameCategory.Account));
                Assert.Equal(vm.Rows.Select(r => r.Category).Distinct().Count(),
                    vm.Rows.Select(r => r.Category).Aggregate(new List<CodenameCategory>(), (l, c) => { if (l.Count == 0 || l[^1] != c) l.Add(c); return l; }).Count);
                var folderRow = vm.Rows.Single(r => r.Category == CodenameCategory.Folder && r.Codename == folder);
                Assert.Equal(folder, folderRow.Text);
                widget.Streamer.SetOn(false);
                vm.Read();   // not attached to a window: the window's attach is what listens
                Assert.Equal(folder + @" · ~\.claude-work", folderRow.Text);
                var row = vm.Rows.First(r => r.Real == "Qxalpha");
                Assert.Equal(SettingsNames.AllowlistRowOff(row.Codename, "Qxalpha"), row.Text);
            }
            finally { vm.Detach(); }
        }
        finally { widget.Dispose(); }
    });
}
