import Foundation

/// What a card draws above its title: an SF Symbol, or one of a few small live previews drawn by
/// WhatsNewWindow (the Desk message line, the font, the notch wing, the menu bar).
enum WhatsNewArt: Equatable {
    case symbol(String)
    case preview(WhatsNewPreview)
}

/// The live previews a card can ask for, by id.
enum WhatsNewPreview: String, Equatable {
    case deskMessage, font, nowPlaying, menuBar
}

/// One highlight: what changed, in a sentence or two, and where "Show me" goes. A card may cover
/// features of more than one release; it sits with the newest of them in the table.
struct WhatsNewCard: Equatable, Identifiable {
    /// Every release it covers ("2.6.0"), newest first.
    let versions: [String]
    /// Unique across the table, for SwiftUI and the tests.
    let id: String
    let title: String
    let body: String
    let art: WhatsNewArt
    /// The Settings page "Show me" opens.
    let destination: SettingsSection

    init(versions: [String], id: String, title: String, body: String, art: WhatsNewArt,
         destination: SettingsSection) {
        self.versions = versions.sorted { WhatsNew.compare($0, $1) == .orderedDescending }
        self.id = id
        self.title = title
        self.body = body
        self.art = art
        self.destination = destination
    }

    /// A card of one release.
    init(version: String, id: String, title: String, body: String, art: WhatsNewArt,
         destination: SettingsSection) {
        self.init(versions: [version], id: id, title: title, body: body, art: art, destination: destination)
    }

    /// The newest release it covers.
    var version: String { versions.first ?? "" }
}

/// What's New after an update (item 57): the release highlights, which of them to show, and the
/// two defaults behind it. Pure apart from the defaults store, which tests hand in.
enum WhatsNew {
    /// Standard defaults: the last version whose cards were shown, or recorded as seen.
    static let lastSeenKey = "whatsNewLastSeen"
    /// Standard defaults: Don't show after updates. Off by default; only the automatic showing
    /// listens to it, never About or the menus.
    static let hideKey = "whatsNewHideAfterUpdates"
    /// Most cards shown automatically after an update, newest first.
    static let cap = 8

    // MARK: Which cards

    /// The cards of releases newer than `lastSeen`, up to and including `current`, newest release
    /// first and in table order within one, at most `limit`. A card counts when any release it
    /// covers does, and sorts by the newest of those. `lastSeen` nil (an update from a version
    /// before What's New) counts every release up to `current`.
    static func cards(lastSeen: String?, current: String, limit: Int? = cap,
                      table: [WhatsNewCard] = WhatsNew.table) -> [WhatsNewCard] {
        let picked = table.compactMap { card -> (card: WhatsNewCard, newest: String)? in
            guard let newest = shown(card, lastSeen: lastSeen, current: current).first else { return nil }
            return (card, newest)
        }
        let newestFirst = picked.enumerated().sorted { a, b in
            let order = compare(a.element.newest, b.element.newest)
            return order == .orderedSame ? a.offset < b.offset : order == .orderedDescending
        }.map(\.element.card)
        guard let limit else { return newestFirst }
        return Array(newestFirst.prefix(max(0, limit)))
    }

    /// The releases of `card` newer than `lastSeen` and not past `current`, newest first.
    static func shown(_ card: WhatsNewCard, lastSeen: String?, current: String) -> [String] {
        card.versions.filter { v in
            guard compare(v, current) != .orderedDescending else { return false }
            guard let lastSeen else { return true }
            return compare(v, lastSeen) == .orderedDescending
        }
    }

    /// The window's one header line for `cards`: "New in 2.4.0 – 2.6.0", from the oldest release
    /// they show to `current`, or "New in 2.6.0" when that is the only one. Nil with no cards.
    static func rangeLabel(_ cards: [WhatsNewCard], lastSeen: String?, current: String) -> String? {
        let versions = cards.flatMap { shown($0, lastSeen: lastSeen, current: current) }
        guard let oldest = versions.min(by: { compare($0, $1) == .orderedAscending }) else { return nil }
        if compare(oldest, current) == .orderedSame { return "New in \(current)" }
        return "New in \(oldest) \u{2013} \(current)"
    }

    /// Every card up to `current`, newest first, for What's New… in About and the menus.
    static func all(current: String, table: [WhatsNewCard] = WhatsNew.table) -> [WhatsNewCard] {
        cards(lastSeen: nil, current: current, limit: nil, table: table)
    }

    /// Dotted versions compared number by number ("2.10.0" after "2.9.1"); a missing part is 0
    /// and anything after a part's leading digits ("0-mac", "1b") is ignored.
    static func compare(_ a: String, _ b: String) -> ComparisonResult {
        let x = numbers(a), y = numbers(b)
        for i in 0..<max(x.count, y.count) {
            let l = i < x.count ? x[i] : 0, r = i < y.count ? y[i] : 0
            if l != r { return l < r ? .orderedAscending : .orderedDescending }
        }
        return .orderedSame
    }

    private static func numbers(_ v: String) -> [Int] {
        v.trimmingCharacters(in: .whitespaces).split(separator: ".").map { part in
            Int(part.prefix { $0.isNumber }) ?? 0
        }
    }

    // MARK: At launch

    /// What a launch does about What's New: the cards to show (none for nothing) and whether to
    /// record `current` as seen.
    struct LaunchDecision: Equatable {
        var show: [WhatsNewCard] = []
        var record = false
    }

    /// A fresh install's first launch records the version and shows nothing (onboarding covers
    /// it). While onboarding is up (no session key yet) nothing happens: the cards wait for a
    /// later launch. While the welcome tour is pending (`tour`, item 61) nothing happens either:
    /// the tour covers it, and records the version when it is finished or skipped. Otherwise the
    /// missed cards show once and the version is recorded; with Don't show after updates on, it
    /// is only recorded.
    static func atLaunch(lastSeen: String?, current: String, fresh: Bool, onboarding: Bool,
                         hidden: Bool, tour: Bool = false,
                         table: [WhatsNewCard] = WhatsNew.table) -> LaunchDecision {
        if fresh { return LaunchDecision(record: true) }
        if onboarding || tour { return LaunchDecision() }
        let missed = cards(lastSeen: lastSeen, current: current, table: table)
        return LaunchDecision(show: hidden ? [] : missed, record: lastSeen != current)
    }

    /// The saved last-seen version, nil when none.
    static func lastSeen(in d: DefaultsStore = UserDefaults.standard) -> String? {
        (d.object(forKey: lastSeenKey) as? String).flatMap { $0.isEmpty ? nil : $0 }
    }

    static func record(_ version: String, in d: DefaultsStore = UserDefaults.standard) {
        d.set(version, forKey: lastSeenKey)
    }

    static func hidden(in d: DefaultsStore = UserDefaults.standard) -> Bool {
        d.bool(forKey: hideKey)
    }

    static func setHidden(_ on: Bool, in d: DefaultsStore = UserDefaults.standard) {
        d.set(on, forKey: hideKey)
    }

    /// state.yaml's `whats_new.pending`: how many cards the next launch would show.
    static func pending(in d: DefaultsStore = UserDefaults.standard, current: String) -> Int {
        hidden(in: d) ? 0 : cards(lastSeen: lastSeen(in: d), current: current).count
    }
}
