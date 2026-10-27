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

    /// The badge for a display name (`PlanBadge.name`), so demo mode's sample plan names map back
    /// to their riffs. Nil for a name no tier produces.
    static func badge(named name: String?) -> PlanBadge? {
        switch name {
        case "Max \u{00D7}20": return PlanBadge(name: "Max \u{00D7}20", riffs: max20xRiffs)
        case "Max": return PlanBadge(name: "Max", riffs: maxRiffs)
        case "Team": return PlanBadge(name: "Team", riffs: [])
        case "Pro": return PlanBadge(name: "Pro", riffs: [])
        default: return nil
        }
    }
}

/// The footer capsule's tooltip rotation. Pure: the view owns the clock.
enum PlanRiffs {
    /// Seconds between riffs.
    static let interval: TimeInterval = 4

    /// Rotation runs only with two or more riffs and Reduce Motion off.
    static func rotates(riffCount: Int, reduceMotion: Bool) -> Bool { riffCount >= 2 && !reduceMotion }

    /// The index after `index`, wrapping. 0 with no riffs; pinned to 0 with one riff or Reduce Motion.
    static func next(after index: Int, count: Int, reduceMotion: Bool = false) -> Int {
        guard rotates(riffCount: count, reduceMotion: reduceMotion) else { return 0 }
        return (index + 1) % count
    }

    /// The riff at `index` (wrapped into range), or the plan name when there are none.
    static func tooltip(name: String, riffs: [String], index: Int) -> String {
        guard !riffs.isEmpty else { return name }
        return riffs[((index % riffs.count) + riffs.count) % riffs.count]
    }

    /// The index a periodic tick `ticks` intervals after the start shows.
    static func index(atTick ticks: Int, count: Int, reduceMotion: Bool) -> Int {
        guard rotates(riffCount: count, reduceMotion: reduceMotion) else { return 0 }
        return ((ticks % count) + count) % count
    }
}
