namespace Sanduhr.Core;

/// <summary>Whether Just the Desk is on, and whether it was Just the Desk that turned the sign-in
/// reminder off (so signing in can turn it back on).</summary>
public sealed record JustTheDeskState(bool On, bool SilencedReminder)
{
    /// <summary>Not chosen.</summary>
    public static JustTheDeskState Off { get; } = new(false, false);
}

/// <summary>
/// The retired Just the Desk mode's keys. Setup replaced the mode (setup spec, section 1); the keys stay in the file, and the
/// migration reads them once (<see cref="SetupMigration"/>).
/// </summary>
public static class JustTheDesk
{
    /// <summary>settings.json key: Just the Desk is on.</summary>
    public const string Key = "just_the_desk";

    /// <summary>settings.json key: the reminder was on when Just the Desk was chosen.</summary>
    public const string SilencedKey = "just_the_desk_silenced_reminder";
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
