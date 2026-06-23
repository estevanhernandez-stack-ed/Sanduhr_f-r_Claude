namespace Sanduhr.Core;

/// <summary>The headline / subtitle / primary-button text the recovery card shows
/// for a given <see cref="SignInReason"/>. A pure record so the copy unit-tests
/// without WPF.</summary>
public sealed record SignInPrompt(string Headline, string Subtitle, string PrimaryLabel);

/// <summary>
/// Maps a <see cref="SignInReason"/> to the card copy. Centralised here (pure Core)
/// so the widget never hardcodes recovery wording and the strings are testable. The
/// first-run copy keeps the headline feature's promise ("no DevTools, no copy-paste");
/// the expired/blocked copy points at the same embedded re-auth, never key paste.
/// </summary>
public static class SignInPromptCopy
{
    public static SignInPrompt For(SignInReason reason) => reason switch
    {
        SignInReason.FirstRun => new SignInPrompt(
            "Track your Claude usage",
            "Sign in once in a secure window. Sanduhr reads your usage automatically — no DevTools, no copy-paste.",
            "Sign in to Claude"),
        SignInReason.Expired => new SignInPrompt(
            "Session expired",
            "Your sign-in timed out. Sign in again — it only takes a few seconds.",
            "Sign in again"),
        SignInReason.Blocked => new SignInPrompt(
            "Connection challenged",
            "Cloudflare needs a fresh check. Sign in again to refresh it automatically.",
            "Sign in again"),
        _ => throw new System.ArgumentOutOfRangeException(
            nameof(reason), reason, "No card copy for a non-prompt reason."),
    };
}
