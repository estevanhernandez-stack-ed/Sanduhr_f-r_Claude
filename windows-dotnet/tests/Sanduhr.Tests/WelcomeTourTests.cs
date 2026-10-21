using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Port of the Mac's WelcomeTourTests: the steps, the shown-once state, the copy and What's New's wait.</summary>
public class WelcomeTourTests
{
    private static readonly IReadOnlySet<TourFeature> None = new HashSet<TourFeature>();
    private static readonly IReadOnlySet<TourFeature> WithDesk = new HashSet<TourFeature> { TourFeature.Desk };

    private static TourCard Make(string id, int step, TourFeature? requires = null) =>
        new(step, new WhatsNewCard([], id, id, "Body.", "", WhatsNewPreview.None, SettingsPage.Widget),
            Requires: requires);

    private static string[][] Ids(IEnumerable<IReadOnlyList<TourCard>> steps) =>
        steps.Select(s => s.Select(c => c.Id).ToArray()).ToArray();

    private static TourCard ById(string id) => WelcomeTour.Table.Single(c => c.Id == id);

    [Fact]
    public void DefaultTable_IsFiveStepsNumberedOneToFive()
    {
        var expected = new[] { new[] { "paced" }, ["desk"], ["tray"], ["accounts"], ["claude-code"] };
        foreach (var features in new[] { None, WithDesk })
        {
            var steps = WelcomeTour.Steps(features);
            Assert.Equal(expected, Ids(steps));
            Assert.Equal([1, 2, 3, 4, 5], steps.Select(s => s[0].Step).ToArray());
        }
    }

    [Fact]
    public void AStepWhoseOnlyCardNeedsAMissingFeature_Hides()
    {
        var table = new[] { Make("a", 1), Make("b", 2, TourFeature.Desk), Make("c", 3) };
        Assert.Equal(new[] { new[] { "a" }, ["c"] }, Ids(WelcomeTour.Steps(None, table)));
        Assert.Equal(new[] { new[] { "a" }, ["b"], ["c"] }, Ids(WelcomeTour.Steps(WithDesk, table)));
    }

    [Fact]
    public void Cards_SortByStepThenTableOrder()
    {
        var table = new[] { Make("late", 2), Make("early", 1), Make("late-2", 2) };
        Assert.Equal(new[] { new[] { "early" }, ["late", "late-2"] }, Ids(WelcomeTour.Steps(None, table)));
    }

    [Fact]
    public void StepText_IsOneBased()
    {
        Assert.Equal("1 of 4", WelcomeTour.StepText(0, 4));
        Assert.Equal("4 of 4", WelcomeTour.StepText(3, 4));
    }

    [Fact]
    public void TitlesAndChoices()
    {
        Assert.Equal("Your limits, paced", ById("paced").Card.Title);
        Assert.Equal("At a glance", ById("tray").Card.Title);
        Assert.Equal("More than one account", ById("accounts").Card.Title);
        Assert.Equal("Claude Code, connected", ById("claude-code").Card.Title);
        Assert.Equal("On your desktop", ById("desk").Card.Title);
        Assert.Equal(TourChoice.TrayMode, ById("tray").Choice);
        Assert.Equal(["tray"], WelcomeTour.Table.Where(c => c.Choice != TourChoice.None).Select(c => c.Id).ToArray());
        Assert.DoesNotContain(WelcomeTour.Table, c => c.Requires is not null);
    }

    [Fact]
    public void ShowMeDestinations()
    {
        Assert.Equal(SettingsPage.Widget, ById("paced").Card.Destination);
        Assert.Equal(SettingsPage.Desk, ById("desk").Card.Destination);
        Assert.Equal(SettingsPage.General, ById("tray").Card.Destination);
        Assert.Equal(SettingsPage.Accounts, ById("accounts").Card.Destination);
        Assert.Equal(SettingsPage.ClaudeCode, ById("claude-code").Card.Destination);
    }

    [Fact]
    public void DeskOnly_IsThreeStepsEndingInSignIn()
    {
        var steps = WelcomeTour.Steps(None, WelcomeTour.DeskOnly);
        Assert.Equal(new[] { new[] { "desk" }, ["tray"], ["sign-in"] }, Ids(steps));
        Assert.Equal([1, 2, 3], steps.Select(s => s[0].Step).ToArray());
        Assert.DoesNotContain(WelcomeTour.DeskOnly, c => c.Id is "paced" or "accounts" or "claude-code");
    }

    [Fact]
    public void DeskOnly_ReusesTheDeskAndTrayCardsAndRewritesTheTrayBody()
    {
        var desk = WelcomeTour.DeskOnly.Single(c => c.Id == "desk");
        Assert.Equal(ById("desk").Card, desk.Card);
        var tray = WelcomeTour.DeskOnly.Single(c => c.Id == "tray");
        Assert.Equal(ById("tray").Card.Title, tray.Card.Title);
        Assert.Equal(ById("tray").Card.Preview, tray.Card.Preview);
        Assert.Equal(ById("tray").Choice, tray.Choice);
        Assert.NotEqual(ById("tray").Card.Body, tray.Card.Body);
    }

    [Fact]
    public void DeskOnly_SignInCardOpensAccounts()
    {
        var card = WelcomeTour.DeskOnly.Single(c => c.Id == "sign-in");
        Assert.Equal("Sign in any time", card.Card.Title);
        Assert.Equal(SettingsPage.Accounts, card.Card.Destination);
        Assert.Equal("Accounts", card.ShowMeTitle);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StepsFor_JustTheDeskPicksTheDeskOnlyTour(bool signedIn)
    {
        Assert.Equal(new[] { new[] { "desk" }, ["tray"], ["sign-in"] },
            Ids(WelcomeTour.StepsFor(true, None, signedIn)));
        Assert.Equal(Ids(WelcomeTour.Steps(None, signedIn: signedIn)),
            Ids(WelcomeTour.StepsFor(false, None, signedIn)));
        // The Desk-only tour has no meters step to sign out of: its desk card never turns into a sign-in.
        Assert.Equal(WelcomeTour.DeskOnly[0], WelcomeTour.StepsFor(true, None, signedIn)[0][0]);
    }

    [Theory]
    [InlineData(TourState.Pending, false, true)]
    [InlineData(TourState.Pending, true, false)]
    [InlineData(TourState.Finished, false, false)]
    [InlineData(TourState.Skipped, false, false)]
    [InlineData(TourState.NotOffered, false, false)]
    public void ShowsForJustTheDesk_OnlyPendingOncePerLaunch(TourState state, bool offered, bool expected) =>
        Assert.Equal(expected, WelcomeTour.ShowsForJustTheDesk(state, offered));

    [Fact]
    public void ShowsForJustTheDesk_NoStateIsNotOffered() =>
        Assert.False(WelcomeTour.ShowsForJustTheDesk(null, false));

    [Fact]
    public void CopyIsShortAndPlain()
    {
        var banned = new[] { "seamless", "powerful", "unlock", "leverage", "delightful" };
        Assert.Equal(WelcomeTour.Table.Count, WelcomeTour.Table.Select(c => c.Id).Distinct().Count());
        foreach (var c in WelcomeTour.Table.Concat(WelcomeTour.DeskOnly))
        {
            var title = c.Card.Title;
            var body = c.Card.Body;
            Assert.False(string.IsNullOrEmpty(title) || string.IsNullOrEmpty(body), c.Id);
            Assert.True(title.Length <= 48, $"{c.Id} title");
            Assert.True(body.Length <= 200, $"{c.Id} body");
            Assert.EndsWith(".", body);
            Assert.False(title.EndsWith('.'), $"{c.Id} title has no period");
            var sentences = body.Split('.').Count(s => !string.IsNullOrWhiteSpace(s));
            Assert.InRange(sentences, 1, 2);
            Assert.DoesNotContain("—", body);
            foreach (var word in banned) Assert.DoesNotContain(word, body.ToLowerInvariant());
        }
        Assert.Equal("You can take the tour again from Settings, About, or the tray menu.", WelcomeTour.AgainLine);
    }

    [Fact]
    public void SignedOut_ChangesOnlyStepOne()
    {
        var paced = ById("paced");
        var out1 = WelcomeTour.SignedOut(paced);
        Assert.Equal("Sign in to see your meters here. Each bar is one of your limits, with a tick where an even pace would put you.", out1.Card.Body);
        Assert.Equal(SettingsPage.Accounts, out1.Card.Destination);
        Assert.Equal("Sign in", out1.ShowMeTitle);
        Assert.Equal(paced.Id, out1.Id);
        Assert.Equal(paced.Card.Title, out1.Card.Title);
        Assert.Equal(paced.Card.Glyph, out1.Card.Glyph);
        Assert.Equal(paced.Card.Preview, out1.Card.Preview);
        Assert.Equal(paced.Step, out1.Step);
        Assert.Equal(paced.Choice, out1.Choice);
        Assert.Equal(paced.Requires, out1.Requires);
        foreach (var other in WelcomeTour.Table.Where(c => c.Step != 1))
            Assert.Equal(other, WelcomeTour.SignedOut(other));
    }

    [Fact]
    public void SignedOutSteps_ChangeOnlyTheFirstStep()
    {
        var signedIn = WelcomeTour.Steps(None);
        var signedOut = WelcomeTour.Steps(None, signedIn: false);
        Assert.Equal(Ids(signedIn), Ids(signedOut));
        Assert.NotEqual(signedIn[0][0], signedOut[0][0]);
        Assert.Equal("Sign in", signedOut[0][0].ShowMeTitle);
        for (var i = 1; i < signedIn.Count; i++) Assert.Equal(signedIn[i], signedOut[i]);
    }

    [Fact]
    public void Decide_FreshInstallWithNothingIsPending() =>
        Assert.Equal(TourState.Pending, WelcomeTour.Decide(null, true, 0, null));

    [Fact]
    public void Decide_NeverOnUpdateOrWithAccountsOrWhatsNew()
    {
        Assert.Equal(TourState.NotOffered, WelcomeTour.Decide(null, false, 0, null));
        Assert.Equal(TourState.NotOffered, WelcomeTour.Decide(null, true, 1, null));
        Assert.Equal(TourState.NotOffered, WelcomeTour.Decide(null, true, 0, "3.4.0"));
    }

    [Theory]
    [InlineData(TourState.Pending)]
    [InlineData(TourState.NotOffered)]
    [InlineData(TourState.Skipped)]
    [InlineData(TourState.Finished)]
    public void Decide_ExistingStateStandsWhateverTheInputs(TourState existing)
    {
        Assert.Equal(existing, WelcomeTour.Decide(existing, false, 1, "3.4.0"));
        Assert.Equal(existing, WelcomeTour.Decide(existing, true, 0, null));
    }

    [Fact]
    public void ShowsAfterFetch_OnlyPendingAfterAFetchOncePerLaunch()
    {
        Assert.False(WelcomeTour.ShowsAfterFetch(TourState.Pending, false, false));
        Assert.True(WelcomeTour.ShowsAfterFetch(TourState.Pending, true, false));
        Assert.False(WelcomeTour.ShowsAfterFetch(TourState.Pending, true, true));
        foreach (var state in new[] { TourState.Finished, TourState.Skipped, TourState.NotOffered })
            Assert.False(WelcomeTour.ShowsAfterFetch(state, true, false));
        Assert.False(WelcomeTour.ShowsAfterFetch(null, true, false));
    }

    [Fact]
    public void End_PendingFinishesOrSkips()
    {
        Assert.Equal(TourState.Finished, WelcomeTour.End(TourState.Pending, true));
        Assert.Equal(TourState.Skipped, WelcomeTour.End(TourState.Pending, false));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void End_AnythingButPendingRecordsNothing(bool finished)
    {
        Assert.Null(WelcomeTour.End(TourState.Finished, finished));
        Assert.Null(WelcomeTour.End(TourState.Skipped, finished));
        Assert.Null(WelcomeTour.End(TourState.NotOffered, finished));
        Assert.Null(WelcomeTour.End(null, finished));
    }

    [Theory]
    [InlineData(TourState.Pending, "pending")]
    [InlineData(TourState.Finished, "finished")]
    [InlineData(TourState.Skipped, "skipped")]
    [InlineData(TourState.NotOffered, "not_offered")]
    public void FormatAndParse_RoundTrip(TourState state, string text)
    {
        Assert.Equal(text, WelcomeTour.Format(state));
        Assert.Equal(state, WelcomeTour.Parse(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Pending")]
    [InlineData("junk")]
    public void Parse_AnythingElseIsNull(string? value) => Assert.Null(WelcomeTour.Parse(value));

    [Fact]
    public void WhatsNewWaitsInTheTourLaunch()
    {
        IReadOnlyList<WhatsNewCard> table =
            [WhatsNewCard.Of("2.6.0", "x", "x", "Body.", "", WhatsNewPreview.None, SettingsPage.General)];

        var waits = WhatsNew.AtLaunch("2.5.0", "2.6.0", fresh: false, onboarding: false, hidden: false,
            tour: true, table: table);
        Assert.Empty(waits.Show);
        Assert.False(waits.Record);

        var without = WhatsNew.AtLaunch("2.5.0", "2.6.0", fresh: false, onboarding: false, hidden: false,
            table: table);
        Assert.Equal(["x"], without.Show.Select(c => c.Id).ToArray());

        var fresh = WhatsNew.AtLaunch(null, "2.6.0", fresh: true, onboarding: true, hidden: false,
            tour: true, table: table);
        Assert.Empty(fresh.Show);
        Assert.True(fresh.Record);
    }
}
