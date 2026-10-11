import Foundation

/// Demo mode (item 72, slice 4): sample data on every surface, for screenshots that show no real
/// account, number, meeting or person. Turned on by the debug hook (`smoke do demo on`, or
/// `open "sanduhr://debug/action?name=demo&arg=on"` with the hooks on) and held in memory only:
/// nothing here is written to defaults, the Keychain, history, snapshot.json or any seat file.
///
/// While on, the widget, the menu bar, the Desk's Claude meters and Claude line, the notch's
/// Claude meters, Settings, Usage and Settings, Accounts show two sample accounts, Work (active)
/// and Personal, with made-up numbers and a made-up meter history; the Desk keeps its own demo
/// meetings and message (DeskModel.setDemo). Off puts the real state back and fetches again.
enum DemoMode {
    /// The sample accounts, the active one first. Names only: no key, no folder, no data choices.
    static let accounts = ["Work", "Personal"]

    /// The account demo mode starts on.
    static var active: String { accounts[0] }

    /// Every surface demo mode fills. `coverage` checks each one has a sample.
    enum Surface: String, CaseIterable {
        /// The widget's tier cards (and the menu bar, which reads the same numbers).
        case widget
        /// The Desk's Claude meters piece, a row per limit.
        case deskMeters
        /// The Desk's one-line Claude piece, starting with the account's name.
        case deskLine
        /// The notch's Claude meters ("5h 42%  wk 63%").
        case notchMeters
        /// Settings, Usage: the Meters charts, one line per sample account.
        case usage
        /// Settings, Accounts and the widget's chip: the two sample accounts.
        case accounts
    }

    /// Made-up numbers for a sample account: Work busy, Personal light. Reset times count from
    /// `now`, so pace ticks and countdowns look alive.
    static func usage(for account: String, now: Date = Date()) -> UsageResponse {
        let f = ISO8601DateFormatter()
        func tier(_ util: Double, resetsIn seconds: TimeInterval) -> TierUsage {
            TierUsage(utilization: util, resetsAt: f.string(from: now.addingTimeInterval(seconds)))
        }
        if account == accounts[1] {
            return UsageResponse(tiers: [.fiveHour: tier(12, resetsIn: 4 * 3600),
                                         .sevenDay: tier(31, resetsIn: 5 * 86_400)])
        }
        return UsageResponse(tiers: [.fiveHour: tier(42, resetsIn: 2 * 3600),
                                     .sevenDay: tier(63, resetsIn: 4 * 86_400),
                                     .sevenDaySonnet: tier(18, resetsIn: 4 * 86_400)])
    }

    /// What the Desk is handed in demo mode: the sample numbers, fresh, named by the account
    /// (two accounts, so the Claude line starts with the name).
    static func deskUsage(for account: String, now: Date = Date()) -> DeskUsage {
        DeskUsage(usage: usage(for: account, now: now), fetchedAt: now, account: account)
    }

    /// A made-up meter history for a sample account: a reading every 3 hours over 30 days, each
    /// limit rising through its window and dropping at the reset, ending at the sample numbers.
    static func history(for account: String, now: Date = Date()) -> HistoryStore.History {
        let iso = ISO8601DateFormatter()
        let latest = usage(for: account, now: now)
        let step: TimeInterval = 3 * 3600
        let count = Int(TimeInterval(MeterHistory.days) * 86_400 / step)
        var out: HistoryStore.History = [:]
        for (tier, t) in latest.tiers {
            guard let end = t.utilization else { continue }
            let period = tier.totalSeconds
            let points: [HistoryStore.Point] = (0..<count).map { i in
                let age = TimeInterval(count - 1 - i) * step
                // How far through its window the limit was at that reading, 0 to 1, ending at `end`.
                let phase = (age / period).truncatingRemainder(dividingBy: 1)
                // A small wiggle, nothing at the latest reading.
                let wiggle = (account == accounts[0] ? 6 : 2) * sin(age / step / 3)
                let value = min(100, max(0, end * (1 - phase) + wiggle))
                return HistoryStore.Point(t: iso.string(from: now.addingTimeInterval(-age)), v: (value * 10).rounded() / 10)
            }
            out[tier.rawValue] = points
        }
        return out
    }

    /// Every sample account's history, for Settings, Usage, Meters.
    static func histories(now: Date = Date()) -> [String: HistoryStore.History] {
        Dictionary(uniqueKeysWithValues: accounts.map { ($0, history(for: $0, now: now)) })
    }

    /// How many samples each surface gets from these functions, as the surfaces compute them:
    /// the widget's visible tiers, the Desk's meter rows, the Claude line and the notch's short
    /// meters (1 each when present), the Usage page's chart rows for both accounts, the accounts.
    static func coverage(now: Date = Date(), hidden: Set<Tier> = []) -> [Surface: Int] {
        let numbers = usage(for: active, now: now)
        let desk = deskUsage(for: active, now: now)
        let rows = MeterHistoryChart.rows(histories: histories(now: now), accounts: accounts, window: .week,
                                          now: now, hidden: Set(hidden.map(\.rawValue)))
        let notch = NotchContent.meters.text(at: .right, meetings: [], meters: DeskClaudeText.compact(desk, now: now),
                                             message: nil, now: now)
        return [
            .widget: UsageViewModel.visibleTiers(numbers, hidden: hidden, compact: false).count,
            .deskMeters: DeskMeterRow.rows(from: MeterVisibility.visible(numbers, hidden: hidden), now: now).count,
            .deskLine: DeskClaudeText.line(desk) == nil ? 0 : 1,
            .notchMeters: notch == nil ? 0 : 1,
            .usage: rows.count,
            .accounts: accounts.count,
        ]
    }
}

/// What demo mode swaps on the view model: the numbers, the status, the accounts and the
/// sparklines. UsageViewModel captures and applies it; DemoSession decides it.
struct DemoShown: Equatable {
    var usage: UsageResponse?
    var lastUpdated: Date?
    var status: UsageViewModel.StatusMessage
    var labels: [String]
    var active: String?
    var signedIn: Set<String>
    var history: HistoryStore.History
}

/// Demo mode's state machine, pure so it is tested without the app: on keeps the real state and
/// shows a sample account, a pick or the chip's cycle moves among the samples only, an account
/// change made elsewhere while on updates the kept state, and off hands the kept state back
/// exactly as it then stands.
struct DemoSession {
    /// The real state while demo mode is on, nil while off.
    private(set) var real: DemoShown?

    var isOn: Bool { real != nil }

    /// A sample account as the surfaces show it.
    static func sample(_ label: String, now: Date) -> DemoShown {
        DemoShown(usage: DemoMode.usage(for: label, now: now), lastUpdated: now, status: .idle,
                  labels: DemoMode.accounts, active: label, signedIn: Set(DemoMode.accounts),
                  history: HistoryStore.sparklines(DemoMode.history(for: label, now: now)))
    }

    /// On: keeps `current` the first time (again on keeps what was kept) and shows Work, or the
    /// sample account already showing.
    mutating func start(_ current: DemoShown, now: Date) -> DemoShown {
        if real == nil {
            real = current
            return Self.sample(DemoMode.active, now: now)
        }
        let label = current.active.flatMap { DemoMode.accounts.contains($0) ? $0 : nil } ?? DemoMode.active
        return Self.sample(label, now: now)
    }

    /// A sample account picked by hand (Make Active, the Accounts submenu); nil when off or not a
    /// sample, so a real label never shows.
    func pick(_ label: String, now: Date) -> DemoShown? {
        guard isOn, DemoMode.accounts.contains(label) else { return nil }
        return Self.sample(label, now: now)
    }

    /// The widget chip's click: the next sample account, wrapping.
    func next(after active: String?, now: Date) -> DemoShown? {
        guard isOn else { return nil }
        let i = DemoMode.accounts.firstIndex(of: active ?? "") ?? -1
        return Self.sample(DemoMode.accounts[(i + 1) % DemoMode.accounts.count], now: now)
    }

    /// The registry changed while on (an account added, removed or signed out elsewhere): the
    /// kept state follows, the samples stay.
    mutating func accountsChanged(labels: [String], active: String?, signedIn: Set<String>) {
        real?.labels = labels
        real?.active = active
        real?.signedIn = signedIn
    }

    /// Off: the kept state, then nothing kept. Nil when it wasn't on.
    mutating func end() -> DemoShown? {
        defer { real = nil }
        return real
    }

    /// Whether a fetch's answer may land: the client it was made with is still the current one,
    /// and demo mode is off (one that lands under demo mode is dropped; off fetches afresh).
    static func answerApplies(sameClient: Bool, demo: Bool) -> Bool {
        sameClient && !demo
    }
}
