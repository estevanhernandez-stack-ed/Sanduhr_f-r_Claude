using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Port of the Mac's WhatsNewTests: the selection rules, the range label, the menu and the launch decision.</summary>
public class WhatsNewTests
{
    private static WhatsNewCard Card(string id, params string[] versions) =>
        new(versions, id, id, "Body.", "", WhatsNewPreview.None, SettingsPage.General);

    private static readonly IReadOnlyList<WhatsNewCard> Table =
    [
        Card("a1", "2.4.0"), Card("a2", "2.4.0"), Card("b1", "2.5.0"),
        Card("c1", "2.6.0"), Card("c2", "2.6.0"), Card("d1", "2.10.0"),
    ];

    private static string[] Ids(IEnumerable<WhatsNewCard> cards) => cards.Select(c => c.Id).ToArray();

    [Theory]
    [InlineData(null, null, 0, true)]
    [InlineData("", "", 0, true)]
    [InlineData("3.5.0", null, 0, false)]
    [InlineData(null, "3.5.0", 0, false)]
    [InlineData(null, null, 1, false)]
    public void IsFresh_NeedsNoRecordAndNoAccount(string? lastSeen, string? installed, int accounts, bool expected) =>
        Assert.Equal(expected, WhatsNew.IsFresh(lastSeen, installed, accounts));

    [Fact]
    public void Compare_NumbersNotStrings()
    {
        Assert.True(WhatsNew.Compare("2.10.0", "2.9.1") > 0);
        Assert.Equal(0, WhatsNew.Compare("2.5", "2.5.0"));
        Assert.Equal(0, WhatsNew.Compare("2.6.0-mac", "2.6.0"));
        Assert.True(WhatsNew.Compare(" 2.6.1 ", "2.6.0") > 0);
        Assert.True(WhatsNew.Compare("2.6.0", "2.6.1") < 0);
    }

    [Theory]
    [InlineData("2.5.0", "2.6.0", "c1,c2")]
    [InlineData("2.3.4", "2.6.0", "c1,c2,b1,a1,a2")]
    [InlineData("2.4.1", "2.6.2", "c1,c2,b1")]
    [InlineData("2.4.0", "2.5.0", "b1")]
    [InlineData("2.6.0", "2.10.0", "d1")]
    [InlineData(null, "2.5.0", "b1,a1,a2")]
    [InlineData("2.6.0", "2.5.0", "")]
    [InlineData("2.6.0", "2.6.0", "")]
    public void Cards_PicksTheMissedReleasesNewestFirst(string? lastSeen, string current, string expected)
    {
        var want = expected.Length == 0 ? [] : expected.Split(',');
        Assert.Equal(want, Ids(WhatsNew.Cards(lastSeen, current, table: Table)));
    }

    [Fact]
    public void Cards_CapsAtEightByDefault_AllIsUncapped()
    {
        var twelve = Enumerable.Range(1, 12).Select(i => Card($"x{i}", "3.0.0")).ToArray();
        var capped = WhatsNew.Cards("2.9.0", "3.0.0", table: twelve);
        Assert.Equal(WhatsNew.Cap, capped.Count);
        Assert.Equal(Enumerable.Range(1, 8).Select(i => $"x{i}"), Ids(capped));
        Assert.Equal(12, WhatsNew.All("3.0.0", twelve).Count);
        Assert.Equal(12, WhatsNew.Cards("2.9.0", "3.0.0", null, twelve).Count);
        Assert.Empty(WhatsNew.Cards("2.9.0", "3.0.0", -3, twelve));
    }

    [Fact]
    public void Cards_MultiVersionCardCountsByItsNewestShownVersion()
    {
        var table = new[] { Card("old", "2.5.0"), Card("span", "2.4.0", "2.6.0"), Card("mid", "2.5.0") };
        // span covers 2.6.0 and 2.4.0; with lastSeen 2.4.0 only 2.6.0 is shown, so it sorts first.
        Assert.Equal(["span", "old", "mid"], Ids(WhatsNew.Cards("2.4.0", "2.6.0", table: table)));
        // current 2.5.0: only 2.4.0 of span is not past current; lastSeen 2.3.0 shows it as 2.4.0, after the 2.5.0 cards.
        Assert.Equal(["old", "mid", "span"], Ids(WhatsNew.Cards("2.3.0", "2.5.0", table: table)));
        Assert.Equal(["2.6.0", "2.4.0"], table[1].Versions);
        Assert.Equal("2.6.0", table[1].Version);
    }

    [Fact]
    public void RangeLabel_SingleRangeAndNone()
    {
        Assert.Equal("New in 2.6.0", WhatsNew.RangeLabel(WhatsNew.Cards("2.5.0", "2.6.0", table: Table), "2.5.0", "2.6.0"));
        Assert.Equal("New in 2.4.0 – 2.6.0", WhatsNew.RangeLabel(WhatsNew.Cards("2.3.4", "2.6.0", table: Table), "2.3.4", "2.6.0"));
        Assert.Equal("New in 2.4.0 – 2.5.0", WhatsNew.RangeLabel(WhatsNew.Cards(null, "2.5.0", table: Table), null, "2.5.0"));
        Assert.Null(WhatsNew.RangeLabel([], null, "2.6.0"));
        Assert.Null(WhatsNew.RangeLabel(WhatsNew.Cards("2.6.0", "2.6.0", table: Table), "2.6.0", "2.6.0"));
    }

    [Fact]
    public void ForMenu_HighlightsOnTheInstalledVersion()
    {
        var (cards, header) = WhatsNew.ForMenu("2.6.0", "2.6.0", Table);
        Assert.Equal(["c1", "c2"], Ids(cards));
        Assert.Equal("Highlights in 2.6.0", header);
    }

    [Fact]
    public void ForMenu_EveryCardWhenUpdatedOrUnknown()
    {
        var (cards, header) = WhatsNew.ForMenu("2.6.0", "2.5.0", Table);
        Assert.Equal(["c1", "c2", "b1", "a1", "a2"], Ids(cards));
        Assert.Equal("New in 2.4.0 – 2.6.0", header);
        Assert.Equal(Ids(cards), Ids(WhatsNew.ForMenu("2.6.0", null, Table).Cards));
    }

    [Fact]
    public void ForMenu_InstalledVersionWithoutCardsFallsBack()
    {
        var (cards, header) = WhatsNew.ForMenu("2.7.0", "2.7.0", Table);
        Assert.Equal(["c1", "c2", "b1", "a1", "a2"], Ids(cards));
        Assert.Equal("New in 2.4.0 – 2.7.0", header);
        var (none, fallback) = WhatsNew.ForMenu("2.3.0", "2.3.0", Table);
        Assert.Empty(none);
        Assert.Equal("Version 2.3.0", fallback);
    }

    [Fact]
    public void AtLaunch_FreshRecordsAndShowsNothing()
    {
        var d = WhatsNew.AtLaunch(null, "2.6.0", fresh: true, onboarding: false, hidden: false, table: Table);
        Assert.Empty(d.Show);
        Assert.True(d.Record);
    }

    [Fact]
    public void AtLaunch_OnboardingAndTourWait()
    {
        var o = WhatsNew.AtLaunch("2.5.0", "2.6.0", fresh: false, onboarding: true, hidden: false, table: Table);
        Assert.Empty(o.Show);
        Assert.False(o.Record);
        var t = WhatsNew.AtLaunch("2.5.0", "2.6.0", fresh: false, onboarding: false, hidden: false, tour: true, table: Table);
        Assert.Empty(t.Show);
        Assert.False(t.Record);
    }

    [Fact]
    public void AtLaunch_HiddenOnlyRecords()
    {
        var d = WhatsNew.AtLaunch("2.5.0", "2.6.0", fresh: false, onboarding: false, hidden: true, table: Table);
        Assert.Empty(d.Show);
        Assert.True(d.Record);
    }

    [Fact]
    public void AtLaunch_NormalUpdateShowsAndRecords()
    {
        var d = WhatsNew.AtLaunch("2.5.0", "2.6.0", fresh: false, onboarding: false, hidden: false, table: Table);
        Assert.Equal(["c1", "c2"], Ids(d.Show));
        Assert.True(d.Record);
        var fromNone = WhatsNew.AtLaunch(null, "2.5.0", fresh: false, onboarding: false, hidden: false, table: Table);
        Assert.Equal(["b1", "a1", "a2"], Ids(fromNone.Show));
        Assert.True(fromNone.Record);
    }

    [Fact]
    public void AtLaunch_SameVersionShowsAndRecordsNothing()
    {
        var d = WhatsNew.AtLaunch("2.6.0", "2.6.0", fresh: false, onboarding: false, hidden: false, table: Table);
        Assert.Empty(d.Show);
        Assert.False(d.Record);
    }
}
