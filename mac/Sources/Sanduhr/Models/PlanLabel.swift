import Foundation

/// A resolved subscription tier: the store-safe name the badge shows, plus tongue-in-cheek riffs
/// for a rotating tooltip (the view's concern; the model only carries them).
struct PlanBadge: Equatable, Sendable {
    let name: String
    let riffs: [String]
}

/// Subscription tier label, ported 1:1 from `windows-dotnet/src/Sanduhr.Core/PlanLabel.cs`. Pure.
///
/// Matching is a defensive substring check on `rate_limit_tier`, gated on a Claude subscription:
/// only `default_claude_max_20x` and the prepaid tiers have been seen directly, the rest are
/// best guesses. API and prepaid orgs resolve to nil, so they show no badge.
enum PlanLabel {
    private static let max20xRiffs = ["Plaid Max", "Maximum Overdrive", "Ridiculous Speed", "Galaxy Brain Max"]
    private static let maxRiffs = ["Maximum Effort", "Max Headroom", "Big Max"]
    private static let subscriptionCapabilities: Set<String> = ["claude_max", "claude_pro", "claude_team"]

    /// With no billing type and no capabilities there is nothing to gate on, so a tier-only
    /// caller is trusted. Otherwise a subscription needs positive evidence.
    private static func isSubscription(billingType: String?, capabilities: [String]) -> Bool {
        if billingType == nil && capabilities.isEmpty { return true }
        if billingType == "stripe_subscription" { return true }
        return capabilities.contains { subscriptionCapabilities.contains($0) }
    }

    static func resolve(rateLimitTier: String?, billingType: String? = nil,
                        capabilities: [String]? = nil) -> PlanBadge? {
        guard let rateLimitTier, !rateLimitTier.isEmpty else { return nil }
        guard isSubscription(billingType: billingType, capabilities: capabilities ?? []) else { return nil }

        let tier = rateLimitTier.lowercased()
        if tier.contains("max_20x") { return PlanBadge(name: "Max \u{00D7}20", riffs: max20xRiffs) }
        if tier.contains("max") { return PlanBadge(name: "Max", riffs: maxRiffs) }
        if tier.contains("team") { return PlanBadge(name: "Team", riffs: []) }
        if tier.contains("pro") { return PlanBadge(name: "Pro", riffs: []) }
        return nil
    }

    static func resolve(_ org: Organization) -> PlanBadge? {
        resolve(rateLimitTier: org.rateLimitTier, billingType: org.billingType,
                capabilities: org.capabilities)
    }
}
