namespace Sanduhr.App.Views;

/// <summary>
/// Outcome of the embedded "Sign in to Claude" flow. Discriminated union —
/// pattern-match to handle each case. Adapted from ROROROblox's
/// <c>CookieCaptureResult</c>; Sanduhr carries the saved account label on success
/// (the credential itself never leaves <c>CredentialStore</c> / Credential
/// Manager — it is never put on this result).
/// </summary>
public abstract record SignInResult
{
    private SignInResult() { }

    /// <summary>Captured a sessionKey and persisted it under <paramref name="Label"/>.</summary>
    public sealed record Success(string Label) : SignInResult;

    /// <summary>User closed the window before signing in.</summary>
    public sealed record Cancelled : SignInResult;

    /// <summary>The Evergreen WebView2 runtime is not installed.</summary>
    public sealed record RuntimeMissing : SignInResult;

    /// <summary>Capture failed; <paramref name="Message"/> is user-facing.</summary>
    public sealed record Failed(string Message) : SignInResult;

    /// <summary>The user chose to bounce from the manual-paste modal to the embedded
    /// sign-in flow ("Use the secure sign-in window instead").</summary>
    public sealed record UseEmbedded : SignInResult;

    /// <summary>The user chose to go straight to manual paste (e.g. from the Google-OAuth
    /// notice), skipping the "paste by hand?" confirm.</summary>
    public sealed record UseManual : SignInResult;
}
