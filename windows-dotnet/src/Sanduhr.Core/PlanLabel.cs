namespace Sanduhr.Core;

/// <summary>
/// A resolved subscription tier: a clean, store-safe display name plus a
/// collection of tongue-in-cheek "easter-egg" riffs. The visible footer
/// badge always shows <see cref="Name"/>; the <see cref="Riffs"/> ride in
/// the rotating tooltip (a View concern — the model just carries the data).
/// </summary>
public sealed class PlanBadge
{
    public string Name { get; }

    /// <summary>
    /// Tooltip riff set for this tier (may be empty for Pro/Team). Each
    /// <see cref="PlanLabel.Resolve"/> call returns a fresh copy, so a caller
    /// can never mutate the module's internal template (parity with Python's
    /// <c>test_riffs_are_copies_not_shared_state</c>).
    /// </summary>
    public IReadOnlyList<string> Riffs { get; }

    public PlanBadge(string name, IReadOnlyList<string> riffs)
    {
        Name = name;
        Riffs = riffs;
    }
}

/// <summary>
/// Subscription-tier label mapping for the footer tier badge, ported 1:1
/// from the Python build's <c>plan.py</c> (PR #25). Pure — no WPF, no IO.
///
/// Maps the <c>rate_limit_tier</c> field from the claude.ai
/// <c>/api/organizations</c> response to a clean display name. Parsing is
/// defensive: a substring match gated on a Claude subscription, because only
/// <c>default_claude_max_20x</c> (Max x20) and the prepaid/API tiers have
/// been observed directly; the other plan strings are best-guess. API /
/// prepaid orgs render no badge.
/// </summary>
public static class PlanLabel
{
    // Easter-egg riffs per tier. The visible badge always shows the clean
    // name; these ride in the rotating tooltip. Only the Max tiers carry
    // riffs today — Pro/Team show the plain name (empty list, easily
    // extended later).
    private static readonly string[] Max20xRiffs =
    {
        "Plaid Max",
        "Maximum Overdrive",
        "Ridiculous Speed",
        "Galaxy Brain Max",
    };

    private static readonly string[] Max5xRiffs =
    {
        "Maximum Effort",
        "Max Headroom",
        "Big Max",
    };

    private static readonly string[] NoRiffs = System.Array.Empty<string>();

    // Capability strings that positively identify a claude.ai subscription.
    private static readonly string[] SubscriptionCapabilities =
    {
        "claude_max",
        "claude_pro",
        "claude_team",
    };

    /// <summary>
    /// Distinguish a claude.ai subscription from a prepaid/API console org.
    /// When billing or capability info is present we require positive evidence
    /// of a subscription. When BOTH are absent (older callers that pass only
    /// the tier string) we don't block — best-effort parsing off the tier
    /// string alone.
    /// </summary>
    private static bool IsSubscription(string? billingType, IReadOnlyList<string> capabilities)
    {
        if (billingType is null && capabilities.Count == 0)
            return true; // no signal to gate on — be lenient
        if (billingType == "stripe_subscription")
            return true;
        foreach (var c in capabilities)
        {
            if (System.Array.IndexOf(SubscriptionCapabilities, c) >= 0)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Resolve an org's <paramref name="rateLimitTier"/> to a
    /// <see cref="PlanBadge"/>, or <c>null</c> when the account is not a
    /// recognized claude.ai subscription (the footer badge is then hidden).
    /// Prepaid / API orgs (e.g. <c>auto_prepaid_tier_0</c> with
    /// <c>billing_type == "prepaid"</c>) always resolve to <c>null</c>.
    /// </summary>
    public static PlanBadge? Resolve(
        string? rateLimitTier,
        string? billingType = null,
        IReadOnlyList<string>? capabilities = null)
    {
        if (string.IsNullOrEmpty(rateLimitTier))
            return null;
        if (!IsSubscription(billingType, capabilities ?? NoRiffs))
            return null;

        var tier = rateLimitTier.ToLowerInvariant();
        if (tier.Contains("max_20x"))
            // "Max ×20" — × is the multiplication sign; escaped so the
            // exact codepoint survives regardless of how the file is decoded.
            return new PlanBadge("Max ×20", new List<string>(Max20xRiffs));
        if (tier.Contains("max"))
            return new PlanBadge("Max", new List<string>(Max5xRiffs));
        if (tier.Contains("team"))
            return new PlanBadge("Team", new List<string>(NoRiffs));
        if (tier.Contains("pro"))
            return new PlanBadge("Pro", new List<string>(NoRiffs));
        return null;
    }
}
