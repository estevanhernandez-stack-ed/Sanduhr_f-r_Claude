namespace Sanduhr.Core;

/// <summary>
/// Why the widget is showing a sign-in recovery card. Drives the card's copy and
/// which action its primary button fires (first-run add-account vs. in-place
/// re-auth of the active account). Runtime-missing is deliberately NOT here — that
/// is a transient <c>SignInCoordinator</c>-flow state, never a persistent widget
/// state.
/// </summary>
public enum SignInReason
{
    /// <summary>No card — a valid key is in use (or no auth surface is needed).</summary>
    None,

    /// <summary>No stored key — the first-run "Track your Claude usage" prompt.</summary>
    FirstRun,

    /// <summary>A stored key was rejected (401 / session timeout) — re-auth in place.</summary>
    Expired,

    /// <summary>Cloudflare challenged the request (403) — re-auth to refresh clearance.</summary>
    Blocked,
}
