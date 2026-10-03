import Foundation

/// One meter's warning setting (Settings, Desk, Meters): a switch, the fill that counts as
/// nearly full, and how far away the reset must still be. Saved in the desk suite per tier:
///   defaults write com.626labs.sanduhr.desk meterWarn.seven_day.on -bool true
///   defaults write com.626labs.sanduhr.desk meterWarn.seven_day.pct -float 90
///   defaults write com.626labs.sanduhr.desk meterWarn.seven_day.minReset -float 86400
/// An unset key takes the tier's default.
struct MeterWarningSettings: Equatable {
    var enabled: Bool
    /// Percent of the limit, 0 to 100; the row warns at or above it.
    var threshold: Double
    /// Seconds; the row warns only while the reset is further away than this.
    var minReset: TimeInterval

    static let hour: TimeInterval = 3600
    static let day: TimeInterval = 86400
    static let defaultThreshold: Double = 90

    /// Weekly limits warn at 90% with more than a day to go; the session is off, and when turned
    /// on warns at 90% with more than an hour to go.
    static func standard(for tier: Tier) -> MeterWarningSettings {
        tier == .fiveHour
            ? MeterWarningSettings(enabled: false, threshold: defaultThreshold, minReset: hour)
            : MeterWarningSettings(enabled: true, threshold: defaultThreshold, minReset: day)
    }

    static func onKey(_ tier: Tier) -> String { "meterWarn.\(tier.rawValue).on" }
    static func thresholdKey(_ tier: Tier) -> String { "meterWarn.\(tier.rawValue).pct" }
    static func minResetKey(_ tier: Tier) -> String { "meterWarn.\(tier.rawValue).minReset" }

    /// The saved setting for `tier`, its default where a key is unset or unreadable.
    static func saved(_ tier: Tier, in store: DefaultsStore) -> MeterWarningSettings {
        var s = standard(for: tier)
        if let on = store.object(forKey: onKey(tier)) as? Bool { s.enabled = on }
        if let pct = (store.object(forKey: thresholdKey(tier)) as? NSNumber)?.doubleValue { s.threshold = pct }
        if let secs = (store.object(forKey: minResetKey(tier)) as? NSNumber)?.doubleValue { s.minReset = secs }
        return s
    }
}

/// When a Desk meter row turns red. Pure, so every edge is tested.
enum MeterWarning {
    /// True when the switch is on, `percent` is at or above the threshold, and the reset is more
    /// than `minReset` away. A missing reset time counts as far away.
    static func isWarning(percent: Double, resetsAt: Date?, settings: MeterWarningSettings, now: Date) -> Bool {
        guard settings.enabled, percent >= settings.threshold else { return false }
        guard let resetsAt else { return true }
        return resetsAt.timeIntervalSince(now) > settings.minReset
    }

    /// The choices for "only while the reset is more than", in seconds, with their names.
    static let minResetChoices: [(seconds: TimeInterval, name: String)] = {
        let minute: TimeInterval = 60
        let hour = MeterWarningSettings.hour
        let day = MeterWarningSettings.day
        var out: [(seconds: TimeInterval, name: String)] = []
        out.append((15 * minute, "15 minutes"))
        out.append((30 * minute, "30 minutes"))
        out.append((hour, "1 hour"))
        out.append((3 * hour, "3 hours"))
        out.append((6 * hour, "6 hours"))
        out.append((12 * hour, "12 hours"))
        out.append((day, "1 day"))
        out.append((2 * day, "2 days"))
        out.append((3 * day, "3 days"))
        return out
    }()
}
