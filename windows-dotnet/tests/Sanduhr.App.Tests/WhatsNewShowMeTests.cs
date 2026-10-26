using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

public sealed class WhatsNewShowMeTests
{
    /// <summary>The 3.8.0 cards keep SettingsPage.Widgets as their destination; Show me opens where the index sends it, and the tooltip says so.</summary>
    [Fact]
    public void Each_380_cards_tooltip_names_the_page_it_opens()
    {
        var expected = new Dictionary<string, string>
        {
            ["widgets"] = "Opens Settings, Mods & Widgets",
            ["companion"] = "Opens Settings, Desk",
            ["claude-widgets"] = "Opens Settings, Mods & Widgets",
        };
        foreach (var (id, tip) in expected)
        {
            var card = WhatsNewCards.Table.Single(c => c.Id == id);
            Assert.Equal(tip, new WhatsNewCardItem(card, null).ShowMeToolTip);
        }
    }
}
