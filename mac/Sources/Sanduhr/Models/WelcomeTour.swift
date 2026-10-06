import Foundation

/// What a tour card draws above its title: an SF Symbol, or one of the tour's small previews
/// (WelcomeTourWindow), most drawn from data already fetched.
enum TourArt: Equatable {
    case symbol(String)
    case preview(TourPreview)
}

/// The tour's previews, by id. `meters`, `deskCorner` and `notch` draw the user's own numbers
/// when there are some; `menuBar` is What's New's preview.
enum TourPreview: String, Equatable {
    case meters, deskCorner, notch, menuBar, accountChip
}

/// Where a card's Show me goes. Unlike What's New's, it leaves the tour open.
enum TourDestination: Equatable {
    /// The widget, shown and brought forward.
    case widget
    case settings(SettingsSection)
}

/// A real setting a card lets you change in place, written the moment it is chosen.
enum TourChoice: String, Equatable {
    /// Desk on or off (Settings, General, Surfaces).
    case desk
    /// The widget's theme: Match Desk, or back to the theme before it.
    case matchDesk
    /// What the menu bar shows (Settings, General, Menu bar).
    case menuBar
}

/// What a card needs from this Mac to show. A card whose feature is missing hides itself.
enum TourFeature: String, Equatable {
    /// A screen with a camera notch.
    case notch
}

/// One card of the welcome tour (item 61). A step has one card, or a pair.
struct TourCard: Equatable, Identifiable {
    /// Unique across the table, for SwiftUI and the tests.
    let id: String
    /// 1-based; the window counts the steps that have a card showing.
    let step: Int
    let title: String
    let body: String
    let art: TourArt
    let showMe: TourDestination
    var choices: [TourChoice] = []
    var requires: TourFeature?
}

/// The welcome tour (item 61): the steps, who sees it once, and the choices it writes. Pure
/// apart from the defaults stores, which tests hand in.
enum WelcomeTour {
    /// Standard defaults: where this Mac stands with the tour. Absent until the first launch of
    /// a build with the tour has decided.
    static let stateKey = "welcomeTourState"
    /// Standard defaults: the theme in use when the tour switched on Match Desk, so switching it
    /// off there goes back to it.
    static let themeBeforeMatchDeskKey = "welcomeTourThemeBeforeMatchDesk"
    /// The last step's line.
    static let againLine = "You can take the tour again from About or the menus."

    enum State: String, Equatable {
        /// A fresh install: shows after the first successful fetch, in this launch or a later one.
        case pending
        case finished
        case skipped
        /// Never offered: an update, or a Mac that already had a key, accounts or What's New.
        case notOffered
    }

    // MARK: Which cards

    /// The cards that show with `features`, in step and table order.
    static func cards(features: Set<TourFeature>, table: [TourCard] = WelcomeTour.table) -> [TourCard] {
        table.filter { card in card.requires.map(features.contains) ?? true }
            .enumerated().sorted { a, b in
                a.element.step == b.element.step ? a.offset < b.offset : a.element.step < b.element.step
            }.map(\.element)
    }

    /// The steps that have a card showing, in order, each with its cards.
    static func steps(features: Set<TourFeature>, table: [TourCard] = WelcomeTour.table) -> [[TourCard]] {
        var out: [[TourCard]] = []
        for card in cards(features: features, table: table) {
            if let last = out.last?.first, last.step == card.step {
                out[out.count - 1].append(card)
            } else {
                out.append([card])
            }
        }
        return out
    }

    /// "1 of 5": `index` is 0-based.
    static func stepText(_ index: Int, of count: Int) -> String {
        "\(index + 1) of \(count)"
    }

    // MARK: Shown once

    static func state(in d: DefaultsStore = UserDefaults.standard) -> State? {
        (d.object(forKey: stateKey) as? String).flatMap(State.init(rawValue:))
    }

    /// Finished or skipped.
    static func done(in d: DefaultsStore = UserDefaults.standard) -> Bool {
        [.finished, .skipped].contains(state(in: d))
    }

    /// Decided once, at the first launch of a build with the tour, before What's New records its
    /// version: pending only on a fresh install with no key, no accounts and no What's New
    /// last-seen version. Anything already decided stays as it is.
    static func decide(existing: State?, fresh: Bool, hasKey: Bool, accounts: Int,
                       lastSeen: String?) -> State {
        if let existing { return existing }
        return fresh && !hasKey && accounts == 0 && lastSeen == nil ? .pending : .notOffered
    }

    /// `decide`, written to `d`. Returns the state.
    @discardableResult
    static func atLaunch(fresh: Bool, hasKey: Bool, accounts: Int, lastSeen: String?,
                         in d: DefaultsStore = UserDefaults.standard) -> State {
        let existing = state(in: d)
        let decided = decide(existing: existing, fresh: fresh, hasKey: hasKey, accounts: accounts, lastSeen: lastSeen)
        if existing != decided { d.set(decided.rawValue, forKey: stateKey) }
        return decided
    }

    /// After an update from the view model: show the tour now? Only while pending, on a
    /// successful fetch, once a launch.
    static func showsAfterFetch(state: State?, fetched: Bool, offeredThisLaunch: Bool) -> Bool {
        state == .pending && fetched && !offeredThisLaunch
    }

    /// Finish or Skip the Tour (Escape and the close button count as Skip). Only a pending tour
    /// records anything: it becomes `how`, and What's New's last-seen becomes `current`, so the
    /// next update shows only newer cards. A tour reopened later changes nothing.
    static func end(_ finished: Bool, current: String, in d: DefaultsStore = UserDefaults.standard) {
        guard state(in: d) == .pending else { return }
        d.set((finished ? State.finished : .skipped).rawValue, forKey: stateKey)
        WhatsNew.record(current, in: d)
    }

    // MARK: Choices

    /// Desk's switch, in the Desk suite.
    static func deskOn(in desk: DefaultsStore = UserDefaults.desk) -> Bool {
        desk.bool(forKey: DeskController.enabledKey)
    }

    static func setDesk(_ on: Bool, in desk: DefaultsStore = UserDefaults.desk) {
        desk.set(on, forKey: DeskController.enabledKey)
    }

    static func matchDesk(themeID: String) -> Bool { themeID == DeskThemeMapping.id }

    /// The theme to pick for Match Desk `on` while `current` is in use. On remembers `current`;
    /// off goes back to the remembered theme (the default theme when there is none) and forgets it.
    static func theme(matchDesk on: Bool, current: String, fallback: String,
                      in d: DefaultsStore = UserDefaults.standard) -> String {
        if on {
            if !matchDesk(themeID: current) { d.set(current, forKey: themeBeforeMatchDeskKey) }
            return DeskThemeMapping.id
        }
        guard matchDesk(themeID: current) else { return current }
        let before = (d.object(forKey: themeBeforeMatchDeskKey) as? String).flatMap { $0.isEmpty ? nil : $0 }
        d.set(nil, forKey: themeBeforeMatchDeskKey)
        guard let before, !matchDesk(themeID: before) else { return fallback }
        return before
    }
}
