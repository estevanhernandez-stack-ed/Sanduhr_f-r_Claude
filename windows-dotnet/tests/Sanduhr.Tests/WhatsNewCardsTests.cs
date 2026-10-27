using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Pins the Windows card table's house rules: unique ids, short plain bodies, real destinations.</summary>
public class WhatsNewCardsTests
{
    private static IReadOnlyList<WhatsNewCard> Table => WhatsNewCards.Table;

    [Fact]
    public void Table_HasEveryReleaseNewestFirst()
    {
        Assert.Equal(
            ["settings-rearranged", "streamer-mode",
             "ci-runners",
             "widgets", "companion", "claude-widgets", "restart-to-update", "desk-fold", "weather-nickname", "livelier-bars", "meter-styles",
             "desk-design", "launcher", "per-monitor", "song-looks", "oled-care", "saved-designs",
             "now-playing-steps-aside", "watchers", "suggested-messages", "desk-plate", "arrange-hide", "statusline-label",
             "desk", "settings-redesigned", "claude-code-page",
             "shortcut-keys", "tray-shows", "limits-come-and-go", "every-folder", "steady-sparklines", "whats-new"],
            Table.Select(c => c.Id));
    }

    /// <summary>4.0.0 (spec, Outside Settings, same release): "Settings, rearranged" lands on What leaves this PC, Streamer mode on its own section.</summary>
    [Fact]
    public void Release400_ShowsItsTwoCardsAfterTheUpdate()
    {
        var shown = WhatsNew.Cards("3.9.0", "4.0.0");
        Assert.Equal(["settings-rearranged", "streamer-mode"], shown.Select(c => c.Id));
        Assert.All(shown, c => Assert.Equal("4.0.0", c.Version));
        Assert.All(shown, c => Assert.Equal(SettingsPage.Privacy, c.Destination));
        Assert.Equal(SettingsIndex.Network, shown[0].Anchor);
        Assert.Equal(SettingsIndex.Streamer, shown[1].Anchor);
    }

    [Fact]
    public void Release400_CardsNameThePlaceInTheSettingsForm()
    {
        Assert.Equal("Settings, rearranged", Table.Single(c => c.Id == "settings-rearranged").Title);
        Assert.Equal(SettingsNames.StreamerSection, Table.Single(c => c.Id == "streamer-mode").Title);
        Assert.Contains("Settings, Privacy, Streamer mode", Table.Single(c => c.Id == "streamer-mode").Body);
        Assert.Contains("Settings, Privacy", Table.Single(c => c.Id == "settings-rearranged").Body);
    }

    [Fact]
    public void Release390_StillShowsOnlyItsOwnCardOnThe390Build()
    {
        Assert.Equal(["ci-runners"], WhatsNew.Cards("3.8.0", "3.9.0").Select(c => c.Id));
    }

    /// <summary>The Store goes 3.8.0 to 3.9.0: the one CI watchers card, and nothing from 3.8.0 again.</summary>
    [Fact]
    public void Release390_ShowsOnlyItsOwnCardAfterTheUpdate()
    {
        var shown = WhatsNew.Cards("3.8.0", "3.9.0");
        Assert.Equal(["ci-runners"], shown.Select(c => c.Id));
        Assert.All(shown, c => Assert.Equal("3.9.0", c.Version));
    }

    /// <summary>A Setup.exe install from the 3.7.2 patch sees the 3.9.0 card first; the cap of eight keeps seven of 3.8.0's.</summary>
    [Fact]
    public void Release390_FromTheSetupExePatch_LeadsAndCapsAtEight()
    {
        var shown = WhatsNew.Cards("3.7.2", "3.9.0");
        Assert.Equal(WhatsNew.Cap, shown.Count);
        Assert.Equal("ci-runners", shown[0].Id);
        Assert.Equal(Release380Ids.Take(7), shown.Skip(1).Select(c => c.Id));
    }

    [Fact]
    public void Release390_ShowMeOpensTheWatchersPage() =>
        Assert.Equal(SettingsPage.Watchers, Table.Single(c => c.Id == "ci-runners").Destination);

    [Fact]
    public void Release390_CardNamesTheSwitchByItsSettingsName() =>
        Assert.Contains(SettingsNames.CiRunnersSwitch, Table.Single(c => c.Id == "ci-runners").Body);

    [Fact]
    public void Release390_MenuShowsItsOwnHighlights()
    {
        var (menu, header) = WhatsNew.ForMenu("3.9.0", "3.9.0");
        Assert.Equal(["ci-runners"], menu.Select(c => c.Id));
        Assert.Equal("Highlights in 3.9.0", header);
    }

    private static readonly string[] Release380Ids =
        ["widgets", "companion", "claude-widgets", "restart-to-update", "desk-fold", "weather-nickname", "livelier-bars", "meter-styles"];

    /// <summary>The Store goes 3.7.1 to 3.8.0; a Setup.exe install comes from 3.7.2, a patch with no cards.
    /// Both see the eight 3.8.0 cards and nothing older.</summary>
    [Theory]
    [InlineData("3.7.1")]
    [InlineData("3.7.2")]
    public void Release380_ShowsOnlyItsOwnSevenCardsAfterTheUpdate(string lastSeen)
    {
        var shown = WhatsNew.Cards(lastSeen, "3.8.0");
        Assert.Equal(Release380Ids, shown.Select(c => c.Id));
        Assert.All(shown, c => Assert.Equal("3.8.0", c.Version));
    }

    /// <summary>From the Store's 3.5.0 era (an install that skipped 3.7.1): the eight 3.8.0 cards fill the cap, so no older card shows.</summary>
    [Fact]
    public void Release380_FromAnOlderVersion_ShowsItsEightAndFillsTheCap()
    {
        var shown = WhatsNew.Cards("3.5.0", "3.8.0");
        Assert.Equal(WhatsNew.Cap, shown.Count);
        Assert.Equal(Release380Ids, shown.Select(c => c.Id));
    }

    [Theory]
    [InlineData("widgets", SettingsPage.Widgets)]
    [InlineData("companion", SettingsPage.Widgets)]
    [InlineData("claude-widgets", SettingsPage.Widgets)]
    [InlineData("restart-to-update", SettingsPage.About)]
    [InlineData("desk-fold", SettingsPage.Desk)]
    [InlineData("meter-styles", SettingsPage.Desk)]
    [InlineData("weather-nickname", SettingsPage.Weather)]
    [InlineData("livelier-bars", SettingsPage.NowPlaying)]
    public void Release380_ShowMeOpensTheirPage(string id, SettingsPage page) =>
        Assert.Equal(page, Table.Single(c => c.Id == id).Destination);

    [Fact]
    public void Release380_MenuShowsItsOwnHighlights()
    {
        var (menu, header) = WhatsNew.ForMenu("3.8.0", "3.8.0");
        Assert.Equal(Release380Ids, menu.Select(c => c.Id));
        Assert.Equal("Highlights in 3.8.0", header);
    }

    [Fact]
    public void Release371_ShowsOnlyItsOwnSixCardsAfterTheUpdate()
    {
        var shown = WhatsNew.Cards("3.6.0", "3.7.1");
        Assert.Equal(["desk-design", "launcher", "per-monitor", "song-looks", "oled-care", "saved-designs"], shown.Select(c => c.Id));
        Assert.All(shown, c => Assert.Equal("3.7.1", c.Version));
    }

    /// <summary>The Store jumps 3.5.0 to 3.7.1: the six 3.7.1 cards lead, the cap of eight keeps two of 3.6.0's.</summary>
    [Fact]
    public void Release371_FromTheStoreVersion_LeadsWithItsSixAndCapsAtEight()
    {
        var shown = WhatsNew.Cards("3.5.0", "3.7.1");
        Assert.Equal(WhatsNew.Cap, shown.Count);
        Assert.Equal(6, shown.Count(c => c.Version == "3.7.1"));
        Assert.All(shown.Take(6), c => Assert.Equal("3.7.1", c.Version));
    }

    [Theory]
    [InlineData("desk-design", SettingsPage.Desk)]
    [InlineData("launcher", SettingsPage.Desk)]
    [InlineData("per-monitor", SettingsPage.Desk)]
    [InlineData("song-looks", SettingsPage.NowPlaying)]
    [InlineData("oled-care", SettingsPage.Desk)]
    [InlineData("saved-designs", SettingsPage.Desk)]
    public void Release371_ShowMeOpensTheirPage(string id, SettingsPage page) =>
        Assert.Equal(page, Table.Single(c => c.Id == id).Destination);

    [Fact]
    public void Release360_ShowsOnlyItsOwnSixCardsAfterTheUpdate()
    {
        var shown = WhatsNew.Cards("3.5.0", "3.6.0");
        Assert.Equal(["now-playing-steps-aside", "watchers", "suggested-messages", "desk-plate", "arrange-hide", "statusline-label"], shown.Select(c => c.Id));
        Assert.All(shown, c => Assert.Equal("3.6.0", c.Version));
    }

    [Theory]
    [InlineData("watchers", SettingsPage.Desk)]
    [InlineData("suggested-messages", SettingsPage.DeskMessage)]
    [InlineData("statusline-label", SettingsPage.ClaudeCode)]
    public void Release360_ShowMeOpensTheirPage(string id, SettingsPage page) =>
        Assert.Equal(page, Table.Single(c => c.Id == id).Destination);

    [Fact]
    public void Release350_LeadsWithTheDeskSettingsAndClaudeCode()
    {
        var shown = WhatsNew.Cards("3.4.3", "3.5.0");
        Assert.Equal(["desk", "settings-redesigned", "claude-code-page"], shown.Take(3).Select(c => c.Id));
        Assert.All(shown, c => Assert.Equal("3.5.0", c.Version));
    }

    [Theory]
    [InlineData("desk", SettingsPage.Desk)]
    [InlineData("settings-redesigned", SettingsPage.General)]
    [InlineData("claude-code-page", SettingsPage.ClaudeCode)]
    public void Release350_LeadCards_ShowMeOpensTheirPage(string id, SettingsPage page) =>
        Assert.Equal(page, Table.Single(c => c.Id == id).Destination);

    [Fact]
    public void DeskCard_SaysOffByDefaultAndNamesJustTheDesk()
    {
        var desk = Table.Single(c => c.Id == "desk");
        Assert.Contains("off until you turn it on", desk.Body);
        Assert.Contains("Just the Desk", desk.Body);
    }

    /// <summary>Nine 3.5.0 cards against the cap of eight: the update screen drops the last one,
    /// the What's New card itself, and the tray menu's view still lists all nine.</summary>
    [Fact]
    public void Release350_AtLaunchCapsAtEight_MenuShowsAll()
    {
        var atLaunch = WhatsNew.Cards("3.4.3", "3.5.0");
        Assert.Equal(WhatsNew.Cap, atLaunch.Count);
        Assert.DoesNotContain(atLaunch, c => c.Id == "whats-new");
        var (menu, header) = WhatsNew.ForMenu("3.5.0", "3.5.0");
        Assert.Equal(Table.Count(c => c.Version == "3.5.0"), menu.Count);
        Assert.Equal("Highlights in 3.5.0", header);
    }

    [Fact]
    public void Ids_AreUnique() => Assert.Equal(Table.Count, Table.Select(c => c.Id).Distinct().Count());

    [Fact]
    public void Titles_AreNotEmpty() => Assert.All(Table, c => Assert.False(string.IsNullOrWhiteSpace(c.Title)));

    [Fact]
    public void Bodies_AreShortPlainSentences()
    {
        foreach (var c in Table)
        {
            Assert.True(c.Body.Length <= 200, $"{c.Id} is {c.Body.Length} chars");
            Assert.EndsWith(".", c.Body);
            Assert.DoesNotContain('—', c.Body);
            var sentences = System.Text.RegularExpressions.Regex.Matches(c.Body, @"\. ").Count + 1;
            Assert.True(sentences <= 2, $"{c.Id} has {sentences} sentences");
        }
    }

    [Fact]
    public void Versions_AreThreePartNumeric()
    {
        foreach (var c in Table)
        {
            Assert.NotEmpty(c.Versions);
            foreach (var v in c.Versions)
            {
                var parts = v.Split('.');
                Assert.Equal(3, parts.Length);
                Assert.All(parts, p => Assert.True(int.TryParse(p, out _), $"{c.Id}: {v}"));
                Assert.Equal(0, WhatsNew.Compare(v, v));
            }
        }
    }

    [Fact]
    public void Destinations_AreDefinedPages() =>
        Assert.All(Table, c => Assert.True(Enum.IsDefined(c.Destination), c.Id));

    [Fact]
    public void Glyphs_AreNotEmpty() => Assert.All(Table, c => Assert.False(string.IsNullOrEmpty(c.Glyph)));

    /// <summary>A glyph is one Segoe Fluent Icons character (the font's codepoints sit in the
    /// Private Use Area), never a codepoint spelled out as text.</summary>
    [Fact]
    public void Glyphs_AreOnePrivateUseCharacter() => Assert.All(Table, c =>
    {
        Assert.Equal(1, c.Glyph.Length);
        Assert.InRange((int)c.Glyph[0], 0xE000, 0xF8FF);
    });
}
