using System.Windows;
using System.Windows.Controls;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>The Drawers page on a real SettingsWindow over scratch folders (drawers plan 2, Task 11): it opens from the sidebar and from
/// the menu anchor, and every section is marked.</summary>
[Collection("SettingsWindow")]
public sealed class DrawersPageTests : AppWindowFixture
{
    [Fact]
    public void The_Drawers_page_opens_at_Your_drawers_and_marks_every_section() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        var window = new SettingsWindow(svm);
        var guard = NoActivate.Show(window);
        try
        {
            window.SelectPage(SettingsPage.Drawers, SettingsIndex.MenuAnchor(SettingsPage.Drawers));
            window.UpdateLayout();
            Pump();
            guard.Check();
            var tab = ((TabControl)window.FindName("Tabs")).Items.OfType<TabItem>().Single(t => (string)t.Tag == "Drawers");
            Assert.True(tab.IsSelected);
            foreach (var anchor in new[] { SettingsIndex.Drawers, SettingsIndex.DrawersFolder, SettingsIndex.DrawersSort, SettingsIndex.DrawersRules,
                SettingsIndex.DrawersList, SettingsIndex.DrawersRecent })
                Assert.NotNull(SettingsAnchor.Find((DependencyObject)tab.Content, anchor));
        }
        finally { window.Close(); }
    });
}
