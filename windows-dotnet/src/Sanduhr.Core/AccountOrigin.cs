namespace Sanduhr.Core;

/// <summary>
/// How an account's credentials were last captured. Drives reauth routing: a
/// manually-pasted key can't be refreshed by the embedded browser login (the
/// Google-SSO population in particular), so the recovery card leads with the
/// paste modal for Manual accounts. Persisted per label in the credential slot
/// <c>origin:{label}</c>; a missing slot reads as <see cref="Embedded"/> so
/// pre-WS-A accounts keep today's behavior.
/// </summary>
public enum AccountOrigin
{
    /// <summary>Captured by the embedded WebView2 claude.ai login.</summary>
    Embedded,

    /// <summary>Pasted by hand into the manual key modal.</summary>
    Manual,
}
