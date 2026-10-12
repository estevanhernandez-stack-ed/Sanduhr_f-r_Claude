namespace Sanduhr.Core;

/// <summary>Whether Just the Desk is on, and whether it was Just the Desk that turned the sign-in
/// reminder off (so signing in can turn it back on).</summary>
public sealed record JustTheDeskState(bool On, bool SilencedReminder)
{
    /// <summary>Not chosen.</summary>
    public static JustTheDeskState Off { get; } = new(false, false);
}

/// <summary>
/// Using the Desk with no Claude account (the Mac's <c>JustTheDesk</c>, S7 7.5). Choosing it turns the
/// Desk on and the sign-in reminder off; adding an account ends it and brings the reminder back, but
/// only when Just the Desk was what turned it off.
/// </summary>
public static class JustTheDesk
{
    /// <summary>settings.json key: Just the Desk is on.</summary>
    public const string Key = "just_the_desk";

    /// <summary>settings.json key: the reminder was on when Just the Desk was chosen.</summary>
    public const string SilencedKey = "just_the_desk_silenced_reminder";

    /// <summary>The first-run card's button.</summary>
    public const string ButtonTitle = "Just the Desk";

    /// <summary>The line under the button.</summary>
    public const string Caption =
        "No Claude account? Use the Desk on its own: a clock, a message and more on the desktop. Sign in any time from Settings, Accounts.";

    /// <summary>Chooses Just the Desk: on, the reminder off (remembered as silenced only when it was on;
    /// choosing twice keeps the first value), the Desk on.</summary>
    public static (JustTheDeskState State, bool ReminderOn, bool DeskEnabled) Choose(JustTheDeskState state, bool reminderOn)
    {
        bool silenced = state.On ? state.SilencedReminder : reminderOn;
        return (new JustTheDeskState(true, silenced), false, true);
    }

    /// <summary>Adding an account ends Just the Desk. Not on: nothing changes. On: off, and the reminder
    /// returns only if Just the Desk silenced it.</summary>
    public static (JustTheDeskState State, bool ReminderOn, bool Ended) EndBySignIn(JustTheDeskState state, bool reminderOn)
    {
        if (!state.On) return (state, reminderOn, false);
        return (JustTheDeskState.Off, state.SilencedReminder || reminderOn, true);
    }

    /// <summary>The first-run flow shows only with no account and Just the Desk not chosen.</summary>
    public static bool Onboarding(bool hasAccount, bool justTheDesk) => !hasAccount && !justTheDesk;

    /// <summary>The widget starts hidden (the tray is the way back) with Just the Desk and no account.</summary>
    public static bool WidgetStartsHidden(bool justTheDesk, bool hasAccount) => justTheDesk && !hasAccount;

    /// <summary>The first-run card shows with no account and Just the Desk not chosen.</summary>
    public static bool ShowsFirstRunCard(bool hasAccount, bool justTheDesk) => !hasAccount && !justTheDesk;
}

/// <summary>The Desk's sign-in reminder switch (S7 7.5).</summary>
public static class SignInReminder
{
    /// <summary>settings.json key.</summary>
    public const string Key = "desk_sign_in_reminder";

    /// <summary>Absent reads as on.</summary>
    public const bool DefaultOn = true;

    public const string Title = "Sign-in reminders on the Desk";

    public const string Caption =
        "Shows \u201Csign in to Sanduhr\u201D where your Claude meters would be while you're signed out or your session has expired. The widget always says when you're not signed in.";
}
