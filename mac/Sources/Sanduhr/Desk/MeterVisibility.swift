import Foundation

/// Which limits show (Settings, Desk, Meters, Show this limit; a limit's two-finger menu). Only a
/// limit believed temporary (LimitLifetime) can be hidden; the session, the all-models weekly
/// limit and the known model limits always show. Saved in the desk suite beside the warning
/// settings:
///   meterShow.<tier>    Bool, false while hidden (unset or true: shown)
///     defaults write com.626labs.sanduhr.desk meterShow.iguana_necktie -bool false
///   meterHidden.<tier>  what the limit read when it was hidden: a dictionary with "resetsAt"
///                       (the server's ISO string) and "utilization" (Double), each when known
///   limitFirstSeen      when each limit was first reported: [tier raw value: Date], written on
///                       first sighting and never overwritten
/// A hidden limit with no record (hidden before item 42, or by `defaults write`) stays hidden and
/// takes the next numbers as its record. It shows again on its own in a new window or after a
/// refill (LimitLifetime.shouldReturn), and at once if it is no longer believed temporary.
/// A hidden limit leaves the widget's cards, the Desk meters and the alerts (and so the Desk
/// pulses and warnings). The menu bar never shows it anyway (MenuBarText); snapshot.json, the
/// statusline, the MCP server and the history keep every limit.
enum MeterVisibility {
    static func showKey(_ tier: Tier) -> String { "meterShow.\(tier.rawValue)" }
    static func recordKey(_ tier: Tier) -> String { "meterHidden.\(tier.rawValue)" }
    static let firstSeenKey = "limitFirstSeen"

    /// The hidden tiers saved in `store`. An unset or unreadable key shows the limit, and the
    /// permanent limits show whatever is saved.
    static func hidden(in store: DefaultsStore) -> Set<Tier> {
        var out: Set<Tier> = []
        for tier in Tier.allCases where !LimitLifetime.permanent.contains(tier) {
            if let shown = store.object(forKey: showKey(tier)) as? Bool, !shown { out.insert(tier) }
        }
        return out
    }

    /// The usage with the hidden limits taken out: the one filter the widget, Desk and the alerts
    /// read, so a hidden limit is gone everywhere at once.
    static func visible(_ usage: UsageResponse?, hidden: Set<Tier>) -> UsageResponse? {
        guard var usage else { return nil }
        for tier in hidden where !LimitLifetime.permanent.contains(tier) { usage.tiers[tier] = nil }
        return usage
    }

    // MARK: Temporary limits

    /// When each limit was first reported, as saved.
    static func firstSeen(in store: DefaultsStore) -> [Tier: Date] {
        guard let saved = store.object(forKey: firstSeenKey) as? [String: Date] else { return [:] }
        var out: [Tier: Date] = [:]
        for (raw, date) in saved {
            if let tier = Tier(rawValue: raw) { out[tier] = date }
        }
        return out
    }

    /// Notes today as the first sighting of every limit in `usage` not seen before; an earlier
    /// date is never overwritten. Writes only when something is new.
    static func recordFirstSeen(_ usage: UsageResponse, now: Date, in store: DefaultsStore) {
        var saved = store.object(forKey: firstSeenKey) as? [String: Date] ?? [:]
        var changed = false
        for tier in Tier.allCases where usage.tiers[tier] != nil && saved[tier.rawValue] == nil {
            saved[tier.rawValue] = now
            changed = true
        }
        if changed { store.set(saved, forKey: firstSeenKey) }
    }

    /// Hide is offered for `tier`: it is believed temporary with these numbers (LimitLifetime).
    static func canHide(_ tier: Tier, usage: UsageResponse?, now: Date, store: DefaultsStore) -> Bool {
        LimitLifetime.isTemporary(tier: tier, usage: usage?.tiers[tier], now: now,
                                  firstSeen: firstSeen(in: store)[tier])
    }

    /// The limits in `usage` believed temporary, the ones Hide is offered for.
    static func temporary(_ usage: UsageResponse?, now: Date, store: DefaultsStore) -> Set<Tier> {
        guard let usage else { return [] }
        let seen = firstSeen(in: store)
        var out: Set<Tier> = []
        for (tier, tu) in usage.tiers
        where LimitLifetime.isTemporary(tier: tier, usage: tu, now: now, firstSeen: seen[tier]) {
            out.insert(tier)
        }
        return out
    }

    // MARK: Hiding and showing

    /// Hides `tier` with what it reads now (`usage`), when it is temporary. False when it is not,
    /// and nothing is written.
    @discardableResult
    static func hide(_ tier: Tier, usage: UsageResponse?, now: Date, store: DefaultsStore) -> Bool {
        guard canHide(tier, usage: usage, now: now, store: store) else { return false }
        store.set(false, forKey: showKey(tier))
        store.set(encode(LimitLifetime.HideRecord(usage?.tiers[tier])), forKey: recordKey(tier))
        return true
    }

    /// Shows `tier` again: its hide and its record are cleared.
    static func show(_ tier: Tier, store: DefaultsStore) {
        store.set(nil, forKey: showKey(tier))
        store.set(nil, forKey: recordKey(tier))
    }

    /// What `tier` read when it was hidden, nil when no record was saved.
    static func record(_ tier: Tier, in store: DefaultsStore) -> LimitLifetime.HideRecord? {
        guard let d = store.object(forKey: recordKey(tier)) as? [String: Any] else { return nil }
        return LimitLifetime.HideRecord(resetsAt: d["resetsAt"] as? String,
                                        utilization: (d["utilization"] as? NSNumber)?.doubleValue)
    }

    private static func encode(_ record: LimitLifetime.HideRecord) -> [String: Any] {
        var d: [String: Any] = [:]
        if let resetsAt = record.resetsAt { d["resetsAt"] = resetsAt }
        if let utilization = record.utilization { d["utilization"] = utilization }
        return d
    }

    /// Why a hidden limit showed again.
    enum Shown: String, Equatable {
        /// No longer believed temporary (or never hideable: the session and weekly limits).
        case notTemporary = "not_temporary"
        case newWindow = "new_window"
        case refill
    }

    /// Runs on every usage update, before the numbers are shown: notes first sightings, then
    /// shows again every hidden limit that is no longer temporary, or whose window reset or that
    /// refilled since it was hidden. A hidden limit without a record takes these numbers as its
    /// record. A limit the numbers leave out is left as it is. Returns what showed again, and why.
    @discardableResult
    static func reconcile(_ usage: UsageResponse, now: Date, store: DefaultsStore) -> [Tier: Shown] {
        recordFirstSeen(usage, now: now, in: store)
        let seen = firstSeen(in: store)
        var shown: [Tier: Shown] = [:]
        for tier in Tier.allCases {
            guard let saved = store.object(forKey: showKey(tier)) as? Bool, !saved else { continue }
            if LimitLifetime.permanent.contains(tier) {
                show(tier, store: store)
                shown[tier] = .notTemporary
                continue
            }
            guard let tu = usage.tiers[tier] else { continue }
            if !LimitLifetime.isTemporary(tier: tier, usage: tu, now: now, firstSeen: seen[tier]) {
                show(tier, store: store)
                shown[tier] = .notTemporary
            } else if let record = record(tier, in: store) {
                if let why = LimitLifetime.shouldReturn(record: record, usage: tu) {
                    show(tier, store: store)
                    shown[tier] = why == .newWindow ? .newWindow : .refill
                }
            } else {
                store.set(encode(LimitLifetime.HideRecord(tu)), forKey: recordKey(tier))
            }
        }
        return shown
    }
}
