using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>The Drawers page's place in Settings (drawers plan 2, Task 11).</summary>
public sealed class SettingsDrawersPageTests
{
    [Fact]
    public void Drawers_is_a_Desktop_page_after_the_Launcher()
    {
        var desktop = SettingsNames.Sidebar.Single(g => g.Pages.Contains(SettingsPage.Desk));
        var pages = desktop.Pages.ToList();
        Assert.Equal(pages.IndexOf(SettingsPage.Launcher) + 1, pages.IndexOf(SettingsPage.Drawers));
        Assert.Equal("Drawers", SettingsNames.PageTitle(SettingsPage.Drawers));
        Assert.Equal("Drawers settings…", SettingsNames.LinkTitle(SettingsPage.Drawers));
    }

    [Fact]
    public void A_drawer_piece_opens_the_Drawers_page() =>
        Assert.Equal(SettingsPage.Drawers, SettingsNames.DeskPiecePage("drawer.screenshots"));

    [Fact]
    public void Search_finds_the_page_and_its_sections()
    {
        Assert.Contains(SettingsIndex.Search("sort my desktop"), h => h.Entry.Page == SettingsPage.Drawers && h.Entry.Anchor == SettingsIndex.DrawersSort);
        Assert.Contains(SettingsIndex.Search("tidy"), h => h.Entry.Page == SettingsPage.Drawers);
        Assert.Equal(SettingsIndex.DrawersList, SettingsIndex.MenuAnchor(SettingsPage.Drawers));
        Assert.Contains(SettingsPage.Drawers, SettingsIndex.StreamerPages);
    }
}
