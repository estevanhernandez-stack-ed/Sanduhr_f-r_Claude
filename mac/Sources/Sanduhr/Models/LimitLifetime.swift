import Foundation

/// Which limits are believed temporary, and when a hidden one comes back (item 42). Pure: the
/// numbers, the clock and the first-seen date come in as values, so every rule is tested on its own.
///
/// Only a temporary limit can be hidden. Session and Weekly — All Models never are. Another limit
/// is temporary when its reset is more than 8 days away (no weekly window does that), when it is
/// a known promo slot, or when it first appeared within the last 14 days and is not one of the
/// known model limits.
enum LimitLifetime {
    /// Never temporary, never hidden.
    static let permanent: Set<Tier> = [.fiveHour, .sevenDay]
    /// The model and product limits the widget knows: permanent unless their reset says otherwise.
    static let knownModelLimits: Set<Tier> = [
        .sevenDaySonnet, .sevenDayOpus, .sevenDayCowork, .sevenDayOmelette, .sevenDayOauthApps,
    ]
    /// Slots the server uses for promotions: always temporary.
    static let promoSlots: Set<Tier> = [.iguanaNecktie]
    /// A reset further off than this is no weekly window.
    static let longReset: TimeInterval = 8 * 86_400
    /// A limit seen for the first time within this long is still new.
    static let newWindow: TimeInterval = 14 * 86_400
    /// A hidden limit whose reset moved later than the one saved at hiding by more than this is
    /// in a new window, and shows again.
    static let newResetSlack: TimeInterval = 3_600
    /// A hidden limit whose utilization dropped by this many points or more since hiding was
    /// refilled, and shows again.
    static let refillPoints: Double = 10

    /// `known` is `knownModelLimits`; tests narrow it to stand in for a limit the widget has not
    /// met yet.
    static func isTemporary(tier: Tier, usage: TierUsage?, now: Date, firstSeen: Date?,
                            known: Set<Tier> = knownModelLimits) -> Bool {
        if permanent.contains(tier) { return false }
        if promoSlots.contains(tier) { return true }
        if let reset = parseISO(usage?.resetsAt), reset.timeIntervalSince(now) > longReset { return true }
        if let firstSeen, now.timeIntervalSince(firstSeen) < newWindow, !known.contains(tier) {
            return true
        }
        return false
    }

    /// What a limit read when it was hidden.
    struct HideRecord: Equatable {
        var resetsAt: String?
        var utilization: Double?

        init(resetsAt: String?, utilization: Double?) {
            self.resetsAt = resetsAt
            self.utilization = utilization
        }

        init(_ usage: TierUsage?) {
            self.init(resetsAt: usage?.resetsAt, utilization: usage?.utilization)
        }
    }

    /// Why a hidden limit shows again on its own.
    enum Return: String, Equatable {
        /// Its reset moved on: a new window.
        case newWindow = "new_window"
        /// Its utilization dropped by `refillPoints` or more.
        case refill
    }

    /// The reason a limit hidden with `record` shows again now that it reads `usage`, or nil
    /// while it stays hidden. A side that is missing (no reset, no utilization) decides nothing.
    static func shouldReturn(record: HideRecord, usage: TierUsage) -> Return? {
        if let then = parseISO(record.resetsAt), let now = parseISO(usage.resetsAt),
           now.timeIntervalSince(then) > newResetSlack {
            return .newWindow
        }
        if let then = record.utilization, let now = usage.utilization, then - now >= refillPoints {
            return .refill
        }
        return nil
    }
}
