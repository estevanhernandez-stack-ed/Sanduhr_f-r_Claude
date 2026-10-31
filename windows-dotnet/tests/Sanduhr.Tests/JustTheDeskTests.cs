using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>The sign-in reminder (S7 7.5) and the retired Just the Desk keys, which the setup migration still reads.</summary>
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
        Assert.Equal("desk_sign_in_reminder", SignInReminder.Key);
        Assert.True(SignInReminder.DefaultOn);
        Assert.Equal("Sign-in reminders on the Desk", SignInReminder.Title);
        Assert.Contains("“sign in to Sanduhr”", SignInReminder.Caption);
        Assert.EndsWith("The widget always says when you're not signed in.", SignInReminder.Caption);
    }
}
