import Foundation

/// The companion's mood (FORMAT 5.5 `moods`, behaviour FORMAT 6).
enum CompanionMood: CaseIterable, Equatable, Hashable, Sendable {
    case content, focused, worried, exhausted, celebrate

    /// The `sprite.json` key for a mood.
    var key: String {
        switch self {
        case .content: "content"
        case .focused: "focused"
        case .worried: "worried"
        case .exhausted: "exhausted"
        case .celebrate: "celebrate"
        }
    }
}

/// A one-shot event the companion reacts to (FORMAT 5.5 `triggers`): its animation plays once, then
/// the mood resumes.
enum CompanionTrigger: CaseIterable, Equatable, Hashable, Sendable {
    case needsYou, jobPassed, nearLimit, idle

    /// The `sprite.json` key for a trigger.
    var key: String {
        switch self {
        case .needsYou: "needs_you"
        case .jobPassed: "job_passed"
        case .nearLimit: "near_limit"
        case .idle: "idle"
        }
    }
}

/// One limit as the companion reads it: which, how full (0 to 100), and when its window resets.
struct CompanionTierReading: Equatable, Sendable {
    let tier: Tier
    let utilization: Double
    let resetsAt: String?

    /// A limit that runs in a window with a reset time: the session and the weekly ones.
    var isWindowed: Bool {
        (tier == .fiveHour || tier.rawValue.hasPrefix("seven_day")) && parseISO(resetsAt) != nil
    }

    var resets: Date? { parseISO(resetsAt) }
}

/// The active account's limits as the companion's rules take them (Windows' `DeskUsage`).
struct CompanionUsage: Equatable, Sendable {
    static let empty = CompanionUsage(tiers: [], signInNeeded: false)

    var tiers: [CompanionTierReading]
    var signInNeeded: Bool

    init(tiers: [CompanionTierReading], signInNeeded: Bool = false) {
        self.tiers = tiers
        self.signInNeeded = signInNeeded
    }

    /// From a usage response: the tiers in `Tier.allCases` order, limited to `only` (the limits the
    /// Desk shows, when given) and to those with a reading.
    init(_ response: UsageResponse, only: [Tier]? = nil, signInNeeded: Bool = false) {
        var list: [CompanionTierReading] = []
        for tier in Tier.allCases where only?.contains(tier) ?? true {
            guard let t = response.tiers[tier], let u = t.utilization else { continue }
            list.append(CompanionTierReading(tier: tier, utilization: u, resetsAt: t.resetsAt))
        }
        self.init(tiers: list, signInNeeded: signInNeeded)
    }
}

/// Mood from the account's own pacing (spec 4, Windows' CompanionMoods): under pace is content, near
/// pace focused, weekly use at 85% or more or over pace worried, at the limit exhausted (it sleeps
/// until the reset). Celebrate is the reset itself, which needs memory of the last reading;
/// `CompanionBrain` adds it.
enum CompanionMoods {
    /// Weekly use at or above this is worried, whatever the pace.
    static let worriedWeeklyPercent = 85.0
    /// Within this many points of the pace line is near pace (the widget's "On pace", `paceInfo`).
    static let nearPacePoints = 5.0

    /// The mood the numbers alone give. Signed out or no numbers is content (the pet never frets
    /// over missing data). Any windowed limit at 100% or more is exhausted. Weekly at
    /// `worriedWeeklyPercent` or more, or any limit ahead of its pace line by `nearPacePoints` or
    /// more, is worried. A limit in use within `nearPacePoints` of its pace line is focused.
    /// Otherwise content. The pace line is `paceFrac`, the number `paceInfo` words as "On pace" or
    /// "N% ahead".
    static func fromUsage(_ usage: CompanionUsage, now: Date) -> CompanionMood {
        if usage.signInNeeded || usage.tiers.isEmpty { return .content }
        if usage.tiers.contains(where: { $0.isWindowed && $0.utilization >= 100 }) { return .exhausted }
        if usage.tiers.contains(where: { $0.tier == .sevenDay && $0.utilization >= worriedWeeklyPercent }) { return .worried }
        var near = false
        for t in usage.tiers {
            guard t.isWindowed, let frac = paceFrac(t.resetsAt, tier: t.tier, now: now) else { continue }
            let diff = t.utilization - frac * 100
            if diff >= nearPacePoints { return .worried }
            if t.utilization > 0, abs(diff) < nearPacePoints { near = true }
        }
        return near ? .focused : .content
    }
}

/// The companion's memory between readings (pure: no clock of its own, no IO). It turns the usage
/// readings, the watchers and the user's idle time into the mood and the one-shot triggers of FORMAT 6:
/// - `celebrate`: a weekly limit, or a limit that was at 100%, rolled into a new window (its reset
///   time moved on by an hour or more). Held for `celebrateFor`, then the numbers' mood again.
/// - `needsYou`: a watcher went to waiting on you. `jobPassed`: a watcher went to passed.
/// - `nearLimit`: a limit crossed its Alerts line (session or weekly) or 100%, upward, since the
///   last reading. The first reading only sets the baseline.
/// - `idle`: no input for `idleAfter`. Fires once, again only after input returns.
///
/// Windows' watcher states map one to one: Mac `WatcherState` has the same six (running, waiting,
/// passed, failed, finished, lostTouch), and only `waiting` and `passed` fire.
final class CompanionBrain {
    static let idleAfter: TimeInterval = 20 * 60
    static let celebrateFor: TimeInterval = 10 * 60
    /// A reset time that moves on by less than this is the API's jitter, not a new window.
    private static let rollover: TimeInterval = 3600

    private var tiers: [Tier: (utilization: Double, resets: Date?)] = [:]
    private var usageSeen = false
    private var watchers: [String: WatcherState] = [:]
    private var watchersSeen = false
    private var idleFired = false
    private var celebrateUntil: Date?
    private var numbers: CompanionMood = .content

    init() {}

    /// The mood now: celebrate while a reset is fresh, otherwise what the last reading's numbers say.
    func mood(at now: Date) -> CompanionMood {
        if let until = celebrateUntil, now < until { return .celebrate }
        return numbers
    }

    /// A new reading. Returns `.nearLimit` when a limit crossed its line (or 100%) upward. The
    /// lines are Settings > Alerts: `sessionLine` for the session limit, `weeklyLine` for the others.
    func observeUsage(_ usage: CompanionUsage, sessionLine: Double, weeklyLine: Double, now: Date) -> [CompanionTrigger] {
        var fired: [CompanionTrigger] = []
        numbers = CompanionMoods.fromUsage(usage, now: now)
        if usage.signInNeeded { return fired }
        for t in usage.tiers {
            let resets = t.resets
            let line = t.tier == .fiveHour ? sessionLine : weeklyLine
            let thresholds = Set([line, 100].filter { $0 > 0 && $0 <= 100 })
            if let was = tiers[t.tier] {
                let rolled = was.resets.flatMap { a in resets.map { $0.timeIntervalSince(a) >= Self.rollover } } ?? false
                if rolled {
                    if t.tier == .sevenDay || was.utilization >= 100 { celebrateUntil = now.addingTimeInterval(Self.celebrateFor) }
                } else if usageSeen, thresholds.contains(where: { was.utilization < $0 && t.utilization >= $0 }) {
                    if !fired.contains(.nearLimit) { fired.append(.nearLimit) }
                }
            }
            tiers[t.tier] = (t.utilization, resets)
        }
        usageSeen = true
        // A fresh reset outranks the numbers only while they are calm: a new window that is already at the limit sleeps.
        if numbers == .exhausted { celebrateUntil = nil }
        return fired
    }

    /// `observeUsage` with the user's saved alert lines.
    func observeUsage(_ usage: CompanionUsage, alerts: AlertSettings, now: Date) -> [CompanionTrigger] {
        observeUsage(usage, sessionLine: alerts.sessionLine, weeklyLine: alerts.weeklyLine, now: now)
    }

    /// The watchers now. Returns the triggers for watchers that changed into waiting or passed since
    /// the last call (the first call only sets the baseline).
    func observeWatchers(_ list: [Watcher]) -> [CompanionTrigger] {
        var fired: [CompanionTrigger] = []
        var now: [String: WatcherState] = [:]
        for w in list { now[w.id] = w.state }
        if watchersSeen {
            for w in list {
                guard watchers[w.id] != w.state else { continue }
                if w.state == .waiting, !fired.contains(.needsYou) { fired.append(.needsYou) }
                if w.state == .passed, !fired.contains(.jobPassed) { fired.append(.jobPassed) }
            }
        }
        watchers = now
        watchersSeen = true
        return fired
    }

    /// Seconds since the user's last input (`IdleClock`). `.idle` once it reaches `idleAfter`; armed
    /// again by new input. An unreadable clock (nil) changes nothing.
    func observeActivity(idleSeconds: TimeInterval?) -> CompanionTrigger? {
        guard let idleSeconds else { return nil }
        if idleSeconds < Self.idleAfter {
            idleFired = false
            return nil
        }
        if idleFired { return nil }
        idleFired = true
        return .idle
    }
}
