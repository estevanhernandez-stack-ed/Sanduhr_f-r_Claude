import Foundation

/// What a card draws above its title: an SF Symbol, or one of a few small live previews drawn by
/// WhatsNewWindow (the Desk message line, a theme card, the font, the notch wing, the menu bar).
enum WhatsNewArt: Equatable {
    case symbol(String)
    case preview(WhatsNewPreview)
}

/// The live previews a card can ask for, by id.
enum WhatsNewPreview: String, Equatable {
    case deskMessage, theme, font, nowPlaying, menuBar
}

/// One highlight of a release: what changed, in a sentence or two, and where "Show me" goes.
struct WhatsNewCard: Equatable, Identifiable {
    /// The release that brought it ("2.6.0").
    let version: String
    /// Unique across the table, for SwiftUI and the tests.
    let id: String
    let title: String
    let body: String
    let art: WhatsNewArt
    /// The Settings page "Show me" opens.
    let destination: SettingsSection

    /// "New in 2.6": the release, without a trailing ".0".
    var versionLabel: String {
        let parts = version.split(separator: ".")
        let shown = parts.count == 3 && parts[2] == "0" ? parts.prefix(2) : parts[...]
        return "New in " + shown.joined(separator: ".")
    }
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
    /// first and in table order within one, at most `limit`. `lastSeen` nil (an update from a
    /// version before What's New) counts every release up to `current`.
    static func cards(lastSeen: String?, current: String, limit: Int? = cap,
                      table: [WhatsNewCard] = WhatsNew.table) -> [WhatsNewCard] {
        let picked = table.filter { card in
            guard compare(card.version, current) != .orderedDescending else { return false }
            guard let lastSeen else { return true }
            return compare(card.version, lastSeen) == .orderedDescending
        }
        let newestFirst = picked.enumerated().sorted { a, b in
            let order = compare(a.element.version, b.element.version)
            return order == .orderedSame ? a.offset < b.offset : order == .orderedDescending
        }.map(\.element)
        guard let limit else { return newestFirst }
        return Array(newestFirst.prefix(max(0, limit)))
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
    /// later launch. Otherwise the missed cards show once and the version is recorded; with Don't
    /// show after updates on, it is only recorded.
    static func atLaunch(lastSeen: String?, current: String, fresh: Bool, onboarding: Bool,
                         hidden: Bool, table: [WhatsNewCard] = WhatsNew.table) -> LaunchDecision {
        if fresh { return LaunchDecision(record: true) }
        if onboarding { return LaunchDecision() }
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
