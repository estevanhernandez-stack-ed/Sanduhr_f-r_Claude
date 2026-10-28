using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Settings v2, slice 7, decision 16: right-click a name on a Claude Usage table and "Show the real name on stream" shows that one name.
/// Found by the Task 15 sweep: the rows reached the view model with <c>AncestorType=TabItem</c>, which a row made from a template cannot
/// find (the tab's content is not under its TabItem in the visual tree), so the menu item had no command and did nothing.
/// </summary>
[Collection("SettingsWindow")]
public sealed class StreamerContextMenuTests : AppWindowFixture
{
    private static VaultSessionInfo Session() =>
        new("u1", ".claude", "C--proj-qxproject", "qxproject", @"C:\proj\qxproject",
            DateTimeOffset.UtcNow.AddHours(-3), DateTimeOffset.UtcNow.AddHours(-2),
            600, new Dictionary<string, long> { ["claude-fable-5"] = 600 }, new Dictionary<string, long> { ["qxskill"] = 50L },
            new Dictionary<string, VaultDayBucket> { [DateTime.Now.ToString("yyyy-MM-dd")] = new() { Total = 600 } },
            null, 0, 0);

    private static IEnumerable<FrameworkElement> RowsWithMenus(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement { ContextMenu: not null } fe && fe.DataContext is BreakdownRow or LedgerRowViewModel)
                yield return fe;
            foreach (var deeper in RowsWithMenus(child)) yield return deeper;
        }
    }

    private void EveryRowOffersTheQuickRoute(string section, int expectedRows)
    {
        var (widget, _) = ScratchWidget("Qxalpha");
        SettingsWindow? window = null;
        try
        {
            var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask,
                checkForUpdatesNow: () => Task.FromResult(new Sanduhr.App.Updates.UpdateProbeResult(Sanduhr.App.Updates.UpdateProbeStatus.UpToDate)), isStoreBuild: false);
            window = new SettingsWindow(svm);
            var guard = NoActivate.Show(window);
            widget.Streamer.SetOn(true);
            Pump();
            var tab = svm.ClaudeCode;
            tab.Overview.ShowTopForTests([("qxproject", 5000L), ("qxsecond", 400L)], [("qxskill", 900L)]);
            tab.Trends.ShowTopForTests([("qxproject", 7000L), ("qxsecond", 100L)]);
            tab.Ledger.PresentForTests([Session()]);
            window.SelectPage(SettingsPage.ClaudeUsage, null);
            tab.Section = section;
            window.UpdateLayout();
            Pump();
            window.UpdateLayout();
            guard.Check();

            var tabContent = (DependencyObject)((TabControl)window.FindName("Tabs")).Items.OfType<TabItem>().Single(t => (string)t.Tag == "ClaudeUsage").Content;
            var rows = RowsWithMenus(tabContent).Where(r => r.IsVisible).ToList();
            Assert.Equal(expectedRows, rows.Count);
            foreach (var row in rows)
            {
                var menu = row.ContextMenu!;
                menu.PlacementTarget = row;
                menu.IsOpen = true;
                Pump();
                try
                {
                    var item = Assert.Single(menu.Items.OfType<MenuItem>());
                    Assert.True(item.Command is not null, $"{section}: \"{item.Header}\" has no command, so choosing it does nothing");
                    Assert.True(item.Command!.CanExecute(item.CommandParameter), $"{section}: \"{item.Header}\" cannot run");
                }
                finally { menu.IsOpen = false; }
            }

            // Choosing the first row's item shows that one real name.
            var first = rows[0];
            first.ContextMenu!.PlacementTarget = first;
            first.ContextMenu.IsOpen = true;
            Pump();
            var chosen = first.ContextMenu.Items.OfType<MenuItem>().Single();
            string? real = first.DataContext switch { BreakdownRow b => b.Real, LedgerRowViewModel l => l.ProjectKeyForStreamer, _ => null };
            var category = first.DataContext is BreakdownRow br ? br.Category : CodenameCategory.Project;
            Assert.True(StreamerMode.Mask.Hides(category, real!));
            chosen.Command!.Execute(chosen.CommandParameter);
            first.ContextMenu.IsOpen = false;
            Assert.False(StreamerMode.Mask.Hides(category, real!), $"{section}: choosing the item did not show \"{real}\"");
        }
        finally { window?.Close(); widget.Dispose(); }
    }

    [Fact]
    public void Overviews_project_and_skill_rows_offer_show_real_name_and_it_works() => AppHost.Run(() => EveryRowOffersTheQuickRoute("Overview", 3));

    [Fact]
    public void Trends_top_projects_offer_show_real_name_and_it_works() => AppHost.Run(() => EveryRowOffersTheQuickRoute("Trends", 2));

    [Fact]
    public void A_sessions_row_offers_show_real_name_and_it_works() => AppHost.Run(() => EveryRowOffersTheQuickRoute("Sessions", 1));
}
