namespace Sanduhr.Core;

/// <summary>The headline / subtitle / button text the recovery card shows for a
/// given <see cref="SignInReason"/> + <see cref="AccountOrigin"/>. A pure record
/// so the copy unit-tests without WPF.</summary>
public sealed record SignInPrompt(
    string Headline, string Subtitle, string PrimaryLabel, string SecondaryLabel);

/// <summary>
/// Maps a <see cref="SignInReason"/> (and the active account's
/// <see cref="AccountOrigin"/>) to the card copy. Centralised here (pure Core) so
/// the widget never hardcodes recovery wording and the strings are testable. The
/// first-run copy keeps the headline feature's promise ("no DevTools, no
/// copy-paste"); expired/blocked copy leads with the method that actually works
/// for the account — browser re-auth for embedded accounts, key paste for
/// manually-entered ones — with the other method as the secondary escape hatch.
/// Routing itself lives in <see cref="ReauthRouting"/>; the two tables must stay
/// in step (primary copy describes <see cref="ReauthRouting.Primary"/>).
/// </summary>
public static class SignInPromptCopy
{
    public static SignInPrompt For(SignInReason reason, AccountOrigin origin = AccountOrigin.Embedded)
        => (reason, origin) switch
        {
            (SignInReason.FirstRun, _) => new SignInPrompt(
                "Track your Claude usage",
                "Sign in once in a secure window. Sanduhr reads your usage automatically — no DevTools, no copy-paste.",
                "Sign in to Claude",
                "Paste a key instead"),
            (SignInReason.Expired, AccountOrigin.Manual) => new SignInPrompt(
                "Session expired",
                "Your pasted sessionKey stopped working. Paste a fresh one — this account's history stays put.",
                "Paste a new key",
                "Use browser sign-in instead"),
            (SignInReason.Expired, _) => new SignInPrompt(
                "Session expired",
                "Your sign-in timed out. Sign in again — it only takes a few seconds.",
                "Sign in again",
                "Paste a key instead"),
            (SignInReason.Blocked, AccountOrigin.Manual) => new SignInPrompt(
                "Connection challenged",
                "Cloudflare needs a fresh check. Paste a new sessionKey (and cf_clearance if you have it).",
                "Paste a new key",
                "Use browser sign-in instead"),
            (SignInReason.Blocked, _) => new SignInPrompt(
                "Connection challenged",
                "Cloudflare needs a fresh check. Sign in again to refresh it automatically.",
                "Sign in again",
                "Paste a key instead"),
            _ => throw new System.ArgumentOutOfRangeException(
                nameof(reason), reason, "No card copy for a non-prompt reason."),
        };
}
