import Foundation

/// Which limits show (Settings, Desk, Meters, Show this limit). The session and the all-models
/// weekly limit always show; every other limit can be hidden, and shows until it is. Saved in the
/// desk suite beside the warning settings, one key per tier:
///   defaults write com.626labs.sanduhr.desk meterShow.iguana_necktie -bool false
/// A hidden limit leaves the widget's cards, the Desk meters and the alerts (and so the Desk
/// pulses and warnings). The menu bar never shows it anyway (MenuBarText); snapshot.json, the
/// statusline, the MCP server and the history keep every limit.
enum MeterVisibility {
    /// Never hidden.
    static let alwaysShown: Set<Tier> = [.fiveHour, .sevenDay]

    static func canHide(_ tier: Tier) -> Bool { !alwaysShown.contains(tier) }

    static func showKey(_ tier: Tier) -> String { "meterShow.\(tier.rawValue)" }

    /// The hidden tiers saved in `store`. An unset or unreadable key shows the limit, and the
    /// session and weekly limits show whatever is saved.
    static func hidden(in store: DefaultsStore) -> Set<Tier> {
        var out: Set<Tier> = []
        for tier in Tier.allCases where canHide(tier) {
            if let shown = store.object(forKey: showKey(tier)) as? Bool, !shown { out.insert(tier) }
        }
        return out
    }

    /// The usage with the hidden limits taken out: the one filter the widget, Desk and the alerts
    /// read, so a hidden limit is gone everywhere at once.
    static func visible(_ usage: UsageResponse?, hidden: Set<Tier>) -> UsageResponse? {
        guard var usage else { return nil }
        for tier in hidden where canHide(tier) { usage.tiers[tier] = nil }
        return usage
    }
}
