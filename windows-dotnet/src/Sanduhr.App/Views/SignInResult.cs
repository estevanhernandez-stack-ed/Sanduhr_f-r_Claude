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
}
