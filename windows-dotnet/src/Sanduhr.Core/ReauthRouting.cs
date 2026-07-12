namespace Sanduhr.Core;

/// <summary>Which auth flow a recovery-card button should launch.</summary>
public enum AuthFlow
{
    /// <summary>Embedded browser login that ADDS an account (first-run / add).</summary>
    EmbeddedAdd,

    /// <summary>Manual key paste that ADDS an account.</summary>
    ManualAdd,

    /// <summary>Embedded browser login overwriting the account IN PLACE.</summary>
    EmbeddedReauth,

    /// <summary>Manual key paste overwriting the account IN PLACE.</summary>
    ManualReauth,
}

/// <summary>
/// The recovery card's routing table, pure so it truth-table tests without WPF
/// (same pattern as <see cref="SignInPromptCopy"/>). Primary follows the
/// account's origin — an embedded account re-auths in the browser, a
/// manually-pasted account re-pastes (the browser login can't help the
/// Google-SSO population that was forced onto manual entry). Secondary is
/// always the other method, still in place; only FirstRun keeps add semantics.
/// </summary>
public static class ReauthRouting
{
    public static AuthFlow Primary(SignInReason reason, AccountOrigin origin) => reason switch
    {
        SignInReason.Expired or SignInReason.Blocked =>
            origin == AccountOrigin.Manual ? AuthFlow.ManualReauth : AuthFlow.EmbeddedReauth,
        _ => AuthFlow.EmbeddedAdd,
    };

    public static AuthFlow Secondary(SignInReason reason, AccountOrigin origin) => reason switch
    {
        SignInReason.Expired or SignInReason.Blocked =>
            origin == AccountOrigin.Manual ? AuthFlow.EmbeddedReauth : AuthFlow.ManualReauth,
        _ => AuthFlow.ManualAdd,
    };
}
