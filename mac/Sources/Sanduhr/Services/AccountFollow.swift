import Foundation

/// One five-hour session reading of an account: its utilization when it was fetched.
struct SessionReading: Equatable, Sendable {
    var at: Date
    var utilization: Double
}

/// Following the account in use (item 36, spec "Following the account in use"): with the
/// setting on, Sanduhr switches to the account whose session meter is climbing.
///
/// The decision is pure: the per-account readings, the last automatic switch, the manual pause
/// and the clock go in, a decision comes out, so every rule is a unit test. AccountFollower is
/// the thin shell that takes the readings and acts on the decision.
enum AccountFollow {
    /// The inactive accounts are checked this often; the active one keeps its 5-minute fetches,
    /// and its rise is read over the same window.
    static let checkInterval: TimeInterval = 15 * 60
    /// At most one automatic switch in this long.
    static let switchSpacing: TimeInterval = 30 * 60
    /// A manual switch pauses following for this long at most...
    static let pauseLength: TimeInterval = 3 * 60 * 60
    /// ...or until the account picked has been idle this long.
    static let pauseIdle: TimeInterval = 30 * 60
    /// "Work (in use)" shows on the chip and the Desk line this long after an automatic switch.
    static let noteLength: TimeInterval = 60
    /// An account's latest check counts only while it is this fresh (one check and some slack).
    static let freshness: TimeInterval = checkInterval + 5 * 60
    /// Readings older than this are dropped: nothing looks back further.
    static let keep: TimeInterval = pauseLength + checkInterval

    /// A manual switch to `account` at `since`.
    struct Pause: Equatable, Sendable {
        var account: String
        var since: Date
    }

    /// Everything the decision looks at.
    struct State: Equatable, Sendable {
        var enabled = false
        var active: String?
        /// The accounts with a session key, the active one included when it has one.
        var signedIn: Set<String> = []
        /// The active account shows the signed-out state.
        var activeSignedOut = false
        /// A switch is already running ("Switching account…").
        var switching = false
        /// Each account's session readings, oldest first.
        var readings: [String: [SessionReading]] = [:]
        var lastAutoSwitch: Date?
        var pause: Pause?

        /// Adds a reading and drops the ones nothing looks at any more.
        mutating func record(_ label: String, utilization: Double, at now: Date) {
            var list = readings[label, default: []].filter { now.timeIntervalSince($0.at) <= AccountFollow.keep }
            list.append(SessionReading(at: now, utilization: utilization))
            readings[label] = list
        }

        /// A renamed account keeps its readings and its pause.
        mutating func rename(_ old: String, to new: String) {
            if let list = readings.removeValue(forKey: old) { readings[new] = list }
            if pause?.account == old { pause?.account = new }
        }

        mutating func forget(_ label: String) {
            readings.removeValue(forKey: label)
            if pause?.account == label { pause = nil }
        }
    }

    enum Decision: Equatable {
        case stay(Reason)
        case switchTo(String)
    }

    enum Reason: Equatable {
        /// The setting is off.
        case off
        /// Signed out, a switch running, or fewer than two signed-in accounts.
        case notReady
        /// A manual switch paused following.
        case paused
        /// An automatic switch happened less than 30 minutes ago.
        case tooSoon
        /// No other account was in use in its latest check.
        case noSignal
        /// The active account was in use too.
        case bothInUse
        /// More than one other account was in use: not a clear signal.
        case unclear
    }

    // MARK: Use

    /// Whether the session rose between any two consecutive readings from `since` on (the last
    /// reading before `since` is the baseline). Any rise counts; a drop, a reset, is not use.
    static func rose(_ readings: [SessionReading], since: Date) -> Bool {
        let baseline = readings.lastIndex { $0.at < since } ?? 0
        let window = Array(readings[baseline...])
        return zip(window, window.dropFirst()).contains { $1.utilization > $0.utilization }
    }

    /// When the session last rose: the time of the later reading of the last rising pair.
    static func lastRise(_ readings: [SessionReading]) -> Date? {
        guard readings.count > 1 else { return nil }
        let i = readings.indices.dropFirst().last { readings[$0].utilization > readings[$0 - 1].utilization }
        return i.map { readings[$0].at }
    }

    /// An inactive account was in use in its latest check: its session rose since the check
    /// before, and the latest check is fresh.
    static func inUseInLatestCheck(_ readings: [SessionReading], now: Date) -> Bool {
        guard readings.count > 1, let last = readings.last,
              now.timeIntervalSince(last.at) <= freshness else { return false }
        return last.utilization > readings[readings.count - 2].utilization
    }

    /// The active account was in use over the last check window, read from its normal fetches.
    static func activeInUse(_ state: State, now: Date) -> Bool {
        guard let active = state.active else { return false }
        return rose(state.readings[active] ?? [], since: now.addingTimeInterval(-checkInterval))
    }

    /// The other signed-in accounts seen in use in their latest check, for the menu's
    /// "· in use" and the chip's dot. Empty with the setting off.
    static func othersInUse(_ state: State, now: Date) -> Set<String> {
        guard state.enabled else { return [] }
        return Set(state.signedIn.filter { label in
            label != state.active && inUseInLatestCheck(state.readings[label] ?? [], now: now)
        })
    }

    /// A manual switch pauses following for 3 hours, or until the account picked has been idle
    /// for 30 minutes (no rise since the switch or its last rise), whichever comes first.
    static func isPaused(_ state: State, now: Date) -> Bool {
        guard let pause = state.pause else { return false }
        guard now.timeIntervalSince(pause.since) < pauseLength else { return false }
        let rise = lastRise(state.readings[pause.account] ?? []) ?? pause.since
        let lastActivity = max(pause.since, rise)
        return now.timeIntervalSince(lastActivity) < pauseIdle
    }

    // MARK: Decision

    /// Switch only on a clear signal: exactly one other signed-in account was in use in its
    /// latest check and the active one was not in use over that window, with following on, not
    /// paused, and no automatic switch in the last 30 minutes.
    static func decide(_ state: State, now: Date) -> Decision {
        guard state.enabled else { return .stay(.off) }
        guard let active = state.active, !state.activeSignedOut, !state.switching,
              state.signedIn.count > 1 else { return .stay(.notReady) }
        if isPaused(state, now: now) { return .stay(.paused) }
        if let last = state.lastAutoSwitch, now.timeIntervalSince(last) < switchSpacing {
            return .stay(.tooSoon)
        }
        let candidates = othersInUse(state, now: now).subtracting([active])
        guard let candidate = candidates.first else { return .stay(.noSignal) }
        if activeInUse(state, now: now) { return .stay(.bothInUse) }
        guard candidates.count == 1 else { return .stay(.unclear) }
        return .switchTo(candidate)
    }
}
