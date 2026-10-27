using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using Sanduhr.App.Services;
using Sanduhr.App.Updates;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Settings v2, slice 8 (spec, Reset; R12; decisions 6, 13 and 14): the folder list under Reset's Claude Code part. Rows show folders
/// as every page does (codenames while streamer mode hides them) and key on the real folder; a streamer flip renames them through
/// ReloadNames; each line shows only in the state it is true for.
/// </summary>
[Collection("SettingsWindow")]
public sealed class ResetWindowFoldersTests : AppWindowFixture
{
    private static void NoSentinel(string where, string? text) =>
        Assert.False((text ?? "").Contains("qx", StringComparison.OrdinalIgnoreCase), $"{where}: \"{text}\"");

    /// <summary>Two discovered folders with receipts (statusline and meters in one, hooks in the other) and one added by hand. The
    /// widget is disposed here if building the rest throws (preflight P15).</summary>
    private (WidgetViewModel Widget, string Keep, string Other, string Added) Make()
    {
        var (widget, _) = ScratchWidget("Qxalpha");
        try
        {
            string keep = Home(".claude-qxkeep"), other = Home(".claude-qxother");
            string added = Path.Combine(Root, "elsewhere", "qxadded");
            Directory.CreateDirectory(added);
            new SettingsStore(ScratchPaths()).SaveCcFoldersAdded([added]);
            var file = Path.Combine(widget.SanduhrAppDataDir, ResetPlan.IntegrationsName, ResetPlan.ReceiptsFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            new ReceiptStore(file).Save(
            [
                new IntegrationReceipt { Kind = IntegrationKind.Statusline, Folder = keep, File = Path.Combine(keep, "settings.json") },
                new IntegrationReceipt { Kind = IntegrationKind.Meters, Folder = keep, File = Path.Combine(keep, "settings.json") },
                new IntegrationReceipt { Kind = IntegrationKind.Hooks, Folder = other, File = Path.Combine(other, "settings.json") },
                new IntegrationReceipt { Kind = IntegrationKind.Meters, Folder = added, File = Path.Combine(added, "settings.json") },
            ]);
            return (widget, keep, other, added);
        }
        catch
        {
            widget.Dispose();
            throw;
        }
    }

    [Fact]
    public void The_list_holds_each_folder_with_Sanduhrs_entries_and_its_kinds() => AppHost.Run(() =>
    {
        var (widget, keep, other, added) = Make();
        try
        {
            var list = ResetFolderListViewModel.Read(widget);
            Assert.False(list.ReceiptsUnreadable);
            Assert.Equal(new[] { keep, other, added }, list.Rows.Select(r => r.Path));
            Assert.Equal("statusline, meters mod", list.Rows[0].Kinds);
            Assert.Equal("glow hook", list.Rows[1].Kinds);
            Assert.True(list.Rows[0].Discovered);
            Assert.False(list.Rows[2].Discovered);
            Assert.True(list.Rows[2].Added);
            Assert.False(list.Rows[0].Added);
            Assert.All(list.Rows, r => Assert.True(r.Undo));   // owner taste 5: all ticked
            Assert.Equal("~\\.claude-qxkeep", list.Rows[0].Shown);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void With_streamer_mode_on_a_row_shows_its_codename_and_the_choice_keeps_the_real_folder() => AppHost.Run(() =>
    {
        var (widget, keep, _, _) = Make();
        try
        {
            widget.Streamer.SetOn(true);
            var list = ResetFolderListViewModel.Read(widget);
            Assert.All(list.Rows.Take(2), r => NoSentinel("row", r.Shown + r.ToString()));
            Assert.All(list.Rows.Take(2), r => Assert.True(Codenames.IsCodename(r.Shown), r.Shown));

            var window = new ResetWindow(null, list);
            OffScreen(window);
            window.Show();
            try
            {
                var box = Boxes(window).Single(b => (string)b.Content == list.Rows[0].Shown);
                box.IsChecked = false;   // through the binding, as a click would
                Assert.Equal(new[] { keep }, window.KeepFolders);   // the real folder, never the codename
                Assert.Equal(new[] { IntegrationKind.Statusline, IntegrationKind.Meters }, list.Kept.Single().Kinds);   // and its kinds, for the per-kind keep
                Assert.All(NamesOf(window), n => NoSentinel("spoken", n));
            }
            finally { window.Close(); }
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void Reset_hands_the_service_the_real_folder_and_its_kinds_of_an_unticked_row_while_streaming() => AppHost.Run(() =>
    {
        var (widget, keep, _, _) = Make();
        try
        {
            widget.Streamer.SetOn(true);
            var list = ResetFolderListViewModel.Read(widget);
            IReadOnlyCollection<ResetFolder>? asked = null;
            var service = new ResetService(ScratchPaths(), new Sanduhr.Core.AccountStore(new MemoryCredentials()),
                new PublishTokenStore(new MemoryCredentials()),
                k => { asked = k; return Task.FromResult<IReadOnlyList<string>>([]); }, () => { });
            var window = new ResetWindow(service, list);
            OffScreen(window);
            window.Show();
            try
            {
                foreach (var name in new[] { "AccountsBox", "HistoryBox", "ThemesBox", "SettingsBox" })
                    ((CheckBox)window.FindName(name)).IsChecked = false;
                Boxes(window).First().IsChecked = false;
                var reset = (Button)window.FindName("ResetButton");
                ((IInvokeProvider)new ButtonAutomationPeer(reset).GetPattern(PatternInterface.Invoke)).Invoke();
                for (int i = 0; i < 50 && asked is null; i++) { Pump(); Thread.Sleep(20); }
                Assert.NotNull(asked);
                var kept = Assert.Single(asked!);
                Assert.Equal(keep, kept.Path);
                Assert.Equal(new[] { IntegrationKind.Statusline, IntegrationKind.Meters }, kept.Kinds);
            }
            finally { window.Close(); }
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void A_streamer_flip_while_Reset_is_open_renames_the_rows() => AppHost.Run(() =>
    {
        var (widget, _, _, _) = Make();
        try
        {
            var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask,
                checkForUpdatesNow: () => Task.FromResult(new UpdateProbeResult(UpdateProbeStatus.UpToDate)), isStoreBuild: false);
            var list = ResetFolderListViewModel.Read(widget);
            Assert.Equal("~\\.claude-qxkeep", list.Rows[0].Shown);   // off: the real name, so the check below is not vacuous
            svm.ResetFolders = list;

            widget.Streamer.SetOn(true);
            svm.ReloadNames();   // what StreamerSettingsViewModel does on the service's Changed

            Assert.All(list.Rows.Take(2), r => NoSentinel("row after the flip", r.Shown));
            Assert.All(list.Rows.Take(2), r => Assert.True(Codenames.IsCodename(r.Shown), r.Shown));
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void Each_line_shows_only_in_the_state_it_is_true_for() => AppHost.Run(() =>
    {
        var (widget, _, _, _) = Make();
        try
        {
            var list = ResetFolderListViewModel.Read(widget);

            Assert.Equal(default, list.LinesFor(part: true, accounts: false, settings: false));   // every row ticked: nothing kept

            list.Rows[1].Undo = false;   // keep the hooks-only folder: no meters, no MCP, discovered
            var l = list.LinesFor(part: true, accounts: true, settings: true);
            Assert.True(l.Kept);
            Assert.False(l.KeptPartOff);
            Assert.False(l.KeptSignIn);   // it has no meters
            Assert.False(l.KeptSettings);
            Assert.False(l.KeptMcpSettings);   // and no MCP server
            Assert.False(l.KeptAdded);

            list.Rows[0].Undo = false;   // and the one with meters
            l = list.LinesFor(part: true, accounts: true, settings: false);
            Assert.True(l.KeptSignIn);
            Assert.False(l.KeptSettings);

            list.Rows[2].Undo = false;   // and the one added by hand
            l = list.LinesFor(part: true, accounts: false, settings: true);
            Assert.True(l.KeptSettings);
            Assert.True(l.KeptAdded);
            Assert.False(l.KeptSignIn);
            l = list.LinesFor(part: true, accounts: false, settings: false);
            Assert.False(l.KeptAdded);   // cc_folders_added stays without the Settings part

            foreach (var r in list.Rows) r.Undo = true;
            l = list.LinesFor(part: false, accounts: false, settings: false);   // the part off keeps every folder, and says so at the part
            Assert.True(l.KeptPartOff);
            Assert.False(l.Kept);
            Assert.False(l.NoFolders);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void A_receipt_only_folder_that_is_not_in_the_added_list_does_not_say_it_leaves_the_page() => AppHost.Run(() =>
    {
        var folder = new ResetFolder(Path.Combine(Root, "gone"), [IntegrationKind.Meters]);
        var list = ResetFolderListViewModel.For([new ResetFolderRow(folder, discovered: false, added: false)], unreadable: false);
        list.Rows[0].Undo = false;
        Assert.False(list.LinesFor(true, true, true).KeptAdded);
    });

    [Fact]
    public void The_MCP_line_shows_exactly_when_the_service_says_a_kept_server_loses_its_settings() => AppHost.Run(() =>
    {
        var mcp = new ResetFolder(Path.Combine(Root, "mcp"), [IntegrationKind.Mcp]);
        var hooks = new ResetFolder(Path.Combine(Root, "hooks"), [IntegrationKind.Hooks]);
        var list = ResetFolderListViewModel.For([new ResetFolderRow(mcp, true), new ResetFolderRow(hooks, true)], unreadable: false);

        Assert.False(list.LinesFor(true, false, true).KeptMcpSettings);   // both ticked: nothing kept
        list.Rows[1].Undo = false;
        Assert.False(list.LinesFor(true, false, true).KeptMcpSettings);   // a kept folder without MCP
        list.Rows[0].Undo = false;
        Assert.True(list.LinesFor(true, false, true).KeptMcpSettings);
        Assert.False(list.LinesFor(true, false, false).KeptMcpSettings);   // Settings unticked: the switch stays
        Assert.Equal(ResetService.KeptMcpLosesItsSettings(new ResetChoices(true, false, false, false, true), list.Kept), list.LinesFor(true, false, true).KeptMcpSettings);
        Assert.Equal(ResetService.KeptMcpLosesItsSettings(new ResetChoices(true, false, false, false, false), list.Kept), list.LinesFor(true, false, false).KeptMcpSettings);

        var window = new ResetWindow(null, list);
        OffScreen(window);
        window.Show();
        try
        {
            Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("KeptMcpSettingsText")).Visibility);
            ((CheckBox)window.FindName("SettingsBox")).IsChecked = false;
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("KeptMcpSettingsText")).Visibility);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void The_part_unticked_dims_the_rows_keeps_their_ticks_and_swaps_the_line() => AppHost.Run(() =>
    {
        var (widget, _, _, _) = Make();
        try
        {
            var list = ResetFolderListViewModel.Read(widget);
            var window = new ResetWindow(null, list);
            OffScreen(window);
            window.Show();
            try
            {
                var part = (CheckBox)window.FindName("ClaudeCodeBox");
                Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("ClaudeCodeWarning")).Visibility);
                Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("PartOffText")).Visibility);
                Assert.True(((UIElement)window.FindName("FolderList")).IsEnabled);

                part.IsChecked = false;
                Assert.False(((UIElement)window.FindName("FolderList")).IsEnabled);   // dimmed
                Assert.All(list.Rows, r => Assert.True(r.Undo));   // ticks kept
                Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("PartOffText")).Visibility);
                Assert.Equal(SettingsNames.ResetFoldersPartOff, ((TextBlock)window.FindName("PartOffText")).Text);
                Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("ClaudeCodeWarning")).Visibility);   // instead, not as well
                Assert.Empty(window.KeepFolders);

                part.IsChecked = true;
                Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("PartOffText")).Visibility);
                Assert.True(((UIElement)window.FindName("FolderList")).IsEnabled);
                list.Rows[0].Undo = false;
                Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("ClaudeCodeWarning")).Visibility);
            }
            finally { window.Close(); }
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void The_old_warning_about_unchecked_entries_is_gone() =>
        Assert.DoesNotContain("can't be removed from Sanduhr later",
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Sanduhr.App", "Views", "ResetWindow.xaml")));

    [Fact]
    public void An_empty_list_says_so_and_unreadable_receipts_say_why() => AppHost.Run(() =>
    {
        var empty = ResetFolderListViewModel.For([], unreadable: false);
        Assert.Equal(default(ResetFolderLines) with { NoFolders = true }, empty.LinesFor(true, true, true));
        var unreadable = ResetFolderListViewModel.For([], unreadable: true);
        Assert.Equal(default(ResetFolderLines) with { Unreadable = true }, unreadable.LinesFor(true, true, true));

        var window = new ResetWindow(null, unreadable);
        OffScreen(window);
        window.Show();
        try
        {
            Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("UnreadableText")).Visibility);
            Assert.Equal(SettingsNames.ResetReceiptsUnreadable, ((TextBlock)window.FindName("UnreadableText")).Text);
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("NoFoldersText")).Visibility);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Reset_is_off_when_the_only_part_left_would_change_nothing() => AppHost.Run(() =>
    {
        var (widget, _, _, _) = Make();
        try
        {
            var list = ResetFolderListViewModel.Read(widget);
            var window = new ResetWindow(null, list);
            OffScreen(window);
            window.Show();
            try
            {
                foreach (var name in new[] { "AccountsBox", "HistoryBox", "ThemesBox", "SettingsBox" })
                    ((CheckBox)window.FindName(name)).IsChecked = false;
                var reset = (Button)window.FindName("ResetButton");
                Assert.True(reset.IsEnabled);
                foreach (var box in Boxes(window)) box.IsChecked = false;
                Assert.False(reset.IsEnabled);   // the part is ticked, but no folder would change
                Assert.False(window.EffectiveChoices.ClaudeCodeEntries);
                Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("ClaudeCodeWarning")).Visibility);
                Boxes(window).First().IsChecked = true;
                Assert.True(reset.IsEnabled);
            }
            finally { window.Close(); }
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void A_row_speaks_its_shown_name_with_its_kinds_as_help() => AppHost.Run(() =>
    {
        var (widget, _, _, _) = Make();
        try
        {
            var list = ResetFolderListViewModel.Read(widget);
            var window = new ResetWindow(null, list);
            OffScreen(window);
            window.Show();
            try
            {
                var boxes = Boxes(window).ToList();
                Assert.Equal(list.Rows.Count, boxes.Count);
                for (int i = 0; i < boxes.Count; i++)
                {
                    Assert.Equal(list.Rows[i].Shown, System.Windows.Automation.AutomationProperties.GetName(boxes[i]));
                    Assert.Equal(list.Rows[i].Kinds, System.Windows.Automation.AutomationProperties.GetHelpText(boxes[i]));
                }
                Assert.Equal(SettingsNames.ResetFoldersName,
                    System.Windows.Automation.AutomationProperties.GetName((FrameworkElement)window.FindName("FolderList")));
                var names = NamesOf(window);
                Assert.DoesNotContain(names, Leaks);
            }
            finally { window.Close(); }
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void A_line_that_appears_is_announced() => AppHost.Run(() =>
    {
        var (widget, _, _, _) = Make();
        var raised = new List<string>();
        try
        {
            var list = ResetFolderListViewModel.Read(widget);
            var window = new ResetWindow(null, list);
            OffScreen(window);
            window.Show();
            LiveRegion.Raised = el => raised.Add(((FrameworkElement)el).Name);
            try
            {
                list.Rows[0].Undo = false;   // the kept line turns on
                Pump();
                Assert.Contains("ClaudeCodeWarning", raised);
            }
            finally { LiveRegion.Raised = null; window.Close(); }
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void With_many_folders_the_list_scrolls_and_Reset_stays_inside_the_window() => AppHost.Run(() =>
    {
        var rows = Enumerable.Range(0, 15)
            .Select(i => new ResetFolderRow(new ResetFolder(Path.Combine(Root, $"home{i}"), [IntegrationKind.Statusline, IntegrationKind.Meters]), true))
            .ToList();
        var window = new ResetWindow(null, ResetFolderListViewModel.For(rows, unreadable: false));
        OffScreen(window);
        window.Show();
        try
        {
            window.UpdateLayout();
            var scroller = (ScrollViewer)window.FindName("FolderScroller");
            Assert.True(scroller.ActualHeight <= 200.5, $"the list is {scroller.ActualHeight} DIP tall");
            Assert.True(scroller.ScrollableHeight > 0, "15 rows should overflow the cap");
            Assert.False(scroller.Focusable);
            var reset = (Button)window.FindName("ResetButton");
            var bottom = reset.TranslatePoint(new Point(0, reset.ActualHeight), window).Y;
            Assert.True(bottom <= window.ActualHeight, $"Reset ends at {bottom}, the window is {window.ActualHeight} tall");
            Assert.True(window.ActualHeight < 700, $"the dialog grew to {window.ActualHeight}");
            // The dark slim bar, not the light default.
            Assert.True(scroller.Resources.Contains(typeof(System.Windows.Controls.Primitives.ScrollBar)));
        }
        finally { window.Close(); }
    });

    private static IEnumerable<CheckBox> Boxes(ResetWindow window)
    {
        window.UpdateLayout();
        var list = (ItemsControl)window.FindName("FolderList");
        for (int i = 0; i < list.Items.Count; i++)
            if (list.ItemContainerGenerator.ContainerFromIndex(i) is DependencyObject c && Descendant<CheckBox>(c) is { } box)
                yield return box;
    }

    private static T? Descendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T t) return t;
            if (Descendant<T>(child) is { } found) return found;
        }
        return null;
    }
}
