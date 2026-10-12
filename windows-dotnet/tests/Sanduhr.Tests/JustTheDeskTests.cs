using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>Just the Desk and the sign-in reminder (S7 7.5; the Mac's JustTheDeskTests plus the Windows rules).</summary>
public class JustTheDeskTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void OffUntilChosen()
    {
        Assert.False(JustTheDeskState.Off.On);
        Assert.False(JustTheDeskState.Off.SilencedReminder);
    }

    [Fact]
    public void ChoosingTurnsTheDeskOnAndTheReminderOff()
    {
        var r = JustTheDesk.Choose(JustTheDeskState.Off, reminderOn: true);
        Assert.True(r.State.On);
        Assert.True(r.State.SilencedReminder);
        Assert.False(r.ReminderOn);
        Assert.True(r.DeskEnabled);
    }

    [Fact]
    public void ChoosingWithTheReminderAlreadyOffSilencesNothing()
    {
        var r = JustTheDesk.Choose(JustTheDeskState.Off, reminderOn: false);
        Assert.True(r.State.On);
        Assert.False(r.State.SilencedReminder);
        Assert.False(r.ReminderOn);
    }

    [Fact]
    public void ChoosingTwiceKeepsTheFirstSilencedValue()
    {
        var first = JustTheDesk.Choose(JustTheDeskState.Off, reminderOn: true);
        var second = JustTheDesk.Choose(first.State, first.ReminderOn);
        Assert.True(second.State.SilencedReminder);
        Assert.False(second.ReminderOn);
    }

    [Fact]
    public void SigningInEndsItAndBringsTheReminderBack()
    {
        var chosen = JustTheDesk.Choose(JustTheDeskState.Off, reminderOn: true);
        var ended = JustTheDesk.EndBySignIn(chosen.State, chosen.ReminderOn);
        Assert.True(ended.Ended);
        Assert.Equal(JustTheDeskState.Off, ended.State);
        Assert.True(ended.ReminderOn);
    }

    [Fact]
    public void AReminderTheyHadTurnedOffStaysOff()
    {
        var chosen = JustTheDesk.Choose(JustTheDeskState.Off, reminderOn: false);
        var ended = JustTheDesk.EndBySignIn(chosen.State, chosen.ReminderOn);
        Assert.True(ended.Ended);
        Assert.False(ended.ReminderOn);
    }

    [Fact]
    public void SigningInWhenNotOnChangesNothing()
    {
        var r = JustTheDesk.EndBySignIn(JustTheDeskState.Off, reminderOn: false);
        Assert.False(r.Ended);
        Assert.Equal(JustTheDeskState.Off, r.State);
        Assert.False(r.ReminderOn);
        Assert.True(JustTheDesk.EndBySignIn(JustTheDeskState.Off, reminderOn: true).ReminderOn);
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    public void OnboardingAndTheFirstRunCardNeedNoAccountAndNoChoice(bool hasAccount, bool justTheDesk, bool expected)
    {
        Assert.Equal(expected, JustTheDesk.Onboarding(hasAccount, justTheDesk));
        Assert.Equal(expected, JustTheDesk.ShowsFirstRunCard(hasAccount, justTheDesk));
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    public void TheWidgetStartsHiddenOnlyWithJustTheDeskAndNoAccount(bool justTheDesk, bool hasAccount, bool expected) =>
        Assert.Equal(expected, JustTheDesk.WidgetStartsHidden(justTheDesk, hasAccount));

    [Fact]
    public void TheDeskDrawsNoSignInLineWithTheReminderOff()
    {
        var on = new DeskUsage([], null, true, true, null);
        var off = on with { SignInReminder = false };
        Assert.Equal("sign in to Sanduhr", DeskClaudeText.GetParts(on)!.Rest);
        Assert.Null(DeskClaudeText.GetParts(off));
        Assert.Null(DeskClaudeText.Line(off));
        Assert.True(DeskMeters.SignInLineShown(on));
        Assert.False(DeskMeters.SignInLineShown(off));
        Assert.Empty(DeskMeters.Rows(on, Now));
        Assert.Empty(DeskMeters.Rows(off, Now));
    }

    [Fact]
    public void TheReminderIsOnByDefaultAndSilentWhenNothingIsNeeded()
    {
        Assert.True(DeskUsage.Empty.SignInReminder);
        Assert.False(DeskMeters.SignInLineShown(DeskUsage.Empty));
        Assert.False(DeskMeters.SignInLineShown(DeskUsage.Empty with { SignInReminder = false }));
    }

    [Fact]
    public void TheReminderOffDoesNotHideRealNumbers()
    {
        var u = new DeskUsage([new DeskTier(TierModel.FiveHour, 7, null)], Now, false, false, null, SignInReminder: false);
        Assert.Equal("claude   7% session", DeskClaudeText.Line(u));
    }

    [Fact]
    public void KeysAndCopy()
    {
        Assert.Equal("just_the_desk", JustTheDesk.Key);
        Assert.Equal("just_the_desk_silenced_reminder", JustTheDesk.SilencedKey);
        Assert.Equal("Just the Desk", JustTheDesk.ButtonTitle);
        Assert.Equal(
            "No Claude account? Use the Desk on its own: a clock, a message and more on the desktop. Sign in any time from Settings, Accounts.",
            JustTheDesk.Caption);
        Assert.Equal("desk_sign_in_reminder", SignInReminder.Key);
        Assert.True(SignInReminder.DefaultOn);
        Assert.Equal("Sign-in reminders on the Desk", SignInReminder.Title);
        Assert.Contains("“sign in to Sanduhr”", SignInReminder.Caption);
        Assert.EndsWith("The widget always says when you're not signed in.", SignInReminder.Caption);
    }
}
