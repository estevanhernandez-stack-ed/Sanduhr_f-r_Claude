import Foundation
import Testing
@testable import Sanduhr

/// What's New after an update (item 57): which cards show for a last-seen and a current version,
/// the launch rule, the defaults, and the shipped table.

@Suite("What's New")
struct WhatsNewTests {
    func card(_ version: String, _ id: String) -> WhatsNewCard {
        WhatsNewCard(version: version, id: id, title: id, body: "", art: .symbol("star"), destination: .general)
    }

    var table: [WhatsNewCard] {
        [card("2.4.0", "a1"), card("2.4.0", "a2"), card("2.5.0", "b1"), card("2.6.0", "c1"),
         card("2.6.0", "c2"), card("2.10.0", "d1")]
    }

    func ids(_ cards: [WhatsNewCard]) -> [String] { cards.map(\.id) }

    @Test func versionsCompareNumberByNumber() {
        #expect(WhatsNew.compare("2.10.0", "2.9.1") == .orderedDescending)
        #expect(WhatsNew.compare("2.5", "2.5.0") == .orderedSame)
        #expect(WhatsNew.compare("2.3.4", "2.4.0") == .orderedAscending)
        #expect(WhatsNew.compare("2.6.0-mac", "2.6.0") == .orderedSame)
        #expect(WhatsNew.compare(" 2.6.1 ", "2.6.0") == .orderedDescending)
    }

    @Test func newerThanLastSeenUpToCurrentNewestFirst() {
        #expect(ids(WhatsNew.cards(lastSeen: "2.5.0", current: "2.6.0", table: table)) == ["c1", "c2"])
        // Skipped versions show the cards they missed, newest release first, table order within one.
        #expect(ids(WhatsNew.cards(lastSeen: "2.3.4", current: "2.6.0", table: table)) == ["c1", "c2", "b1", "a1", "a2"])
        // A point release with no cards of its own still shows what it skipped.
        #expect(ids(WhatsNew.cards(lastSeen: "2.4.1", current: "2.6.2", table: table)) == ["c1", "c2", "b1"])
        // Nothing past the running version, even when the table already has it.
        #expect(ids(WhatsNew.cards(lastSeen: "2.4.0", current: "2.5.0", table: table)) == ["b1"])
        #expect(ids(WhatsNew.cards(lastSeen: "2.6.0", current: "2.10.0", table: table)) == ["d1"])
    }

    @Test func nothingWhenSeenOrOlder() {
        #expect(WhatsNew.cards(lastSeen: "2.6.0", current: "2.6.0", table: table).isEmpty)
        // A downgrade shows nothing.
        #expect(WhatsNew.cards(lastSeen: "2.6.0", current: "2.5.0", table: table).isEmpty)
        #expect(WhatsNew.cards(lastSeen: "2.3.4", current: "2.3.4", table: table).isEmpty)
    }

    @Test func noLastSeenCountsEveryReleaseUpToCurrent() {
        #expect(ids(WhatsNew.cards(lastSeen: nil, current: "2.5.0", table: table)) == ["b1", "a1", "a2"])
    }

    @Test func capped() {
        let many = (0..<12).map { card("2.6.0", "x\($0)") }
        #expect(WhatsNew.cards(lastSeen: "2.5.0", current: "2.6.0", table: many).count == WhatsNew.cap)
        #expect(WhatsNew.cap == 8)
        #expect(ids(WhatsNew.cards(lastSeen: "2.3.0", current: "2.6.0", limit: 2, table: table)) == ["c1", "c2"])
        // About and the menus show them all.
        #expect(WhatsNew.all(current: "2.6.0", table: many).count == 12)
        #expect(ids(WhatsNew.all(current: "2.6.0", table: table)) == ["c1", "c2", "b1", "a1", "a2"])
    }

    @Test func freshInstallRecordsAndShowsNothing() {
        let d = WhatsNew.atLaunch(lastSeen: nil, current: "2.6.0", fresh: true, onboarding: true, hidden: false, table: table)
        #expect(d == WhatsNew.LaunchDecision(show: [], record: true))
        let signedIn = WhatsNew.atLaunch(lastSeen: nil, current: "2.6.0", fresh: true, onboarding: false, hidden: false, table: table)
        #expect(signedIn == WhatsNew.LaunchDecision(show: [], record: true))
    }

    @Test func updateShowsOnceAndRecords() {
        let d = WhatsNew.atLaunch(lastSeen: "2.5.0", current: "2.6.0", fresh: false, onboarding: false, hidden: false, table: table)
        #expect(ids(d.show) == ["c1", "c2"])
        #expect(d.record)
        // The next launch, with the version recorded, shows nothing and writes nothing.
        let again = WhatsNew.atLaunch(lastSeen: "2.6.0", current: "2.6.0", fresh: false, onboarding: false, hidden: false, table: table)
        #expect(again == WhatsNew.LaunchDecision(show: [], record: false))
    }

    @Test func updateFromBeforeWhatsNewShowsWhatArrivedUpToNow() {
        let d = WhatsNew.atLaunch(lastSeen: nil, current: "2.6.0", fresh: false, onboarding: false, hidden: false, table: table)
        #expect(ids(d.show) == ["c1", "c2", "b1", "a1", "a2"])
        #expect(d.record)
    }

    @Test func onboardingWaits() {
        // No session key: onboarding is up, so nothing shows and nothing is recorded.
        let d = WhatsNew.atLaunch(lastSeen: "2.5.0", current: "2.6.0", fresh: false, onboarding: true, hidden: false, table: table)
        #expect(d == WhatsNew.LaunchDecision(show: [], record: false))
    }

    @Test func dontShowAfterUpdatesOnlyRecords() {
        let d = WhatsNew.atLaunch(lastSeen: "2.5.0", current: "2.6.0", fresh: false, onboarding: false, hidden: true, table: table)
        #expect(d == WhatsNew.LaunchDecision(show: [], record: true))
    }

    @Test func defaults() {
        let d = MemoryDefaults()
        #expect(WhatsNew.lastSeen(in: d) == nil)
        #expect(!WhatsNew.hidden(in: d))
        d.set("", forKey: WhatsNew.lastSeenKey)
        #expect(WhatsNew.lastSeen(in: d) == nil)
        WhatsNew.record("2.6.0", in: d)
        #expect(d.object(forKey: "whatsNewLastSeen") as? String == "2.6.0")
        #expect(WhatsNew.lastSeen(in: d) == "2.6.0")
        WhatsNew.setHidden(true, in: d)
        #expect(d.object(forKey: "whatsNewHideAfterUpdates") as? Bool == true)
    }

    @Test func pendingCountsWhatTheNextLaunchWouldShow() {
        let d = MemoryDefaults()
        WhatsNew.record("2.3.4", in: d)
        #expect(WhatsNew.pending(in: d, current: "2.5.0") == WhatsNew.cards(lastSeen: "2.3.4", current: "2.5.0").count)
        #expect(WhatsNew.pending(in: d, current: "2.5.0") > 0)
        WhatsNew.setHidden(true, in: d)
        #expect(WhatsNew.pending(in: d, current: "2.5.0") == 0)
        WhatsNew.setHidden(false, in: d)
        WhatsNew.record("2.5.0", in: d)
        #expect(WhatsNew.pending(in: d, current: "2.5.0") == 0)
    }

    @Test func aCardSpanningReleasesShowsWhenAnyOfThemIsNew() {
        let merged = WhatsNewCard(versions: ["2.5.0", "2.6.0"], id: "m", title: "m", body: "",
                                  art: .symbol("star"), destination: .general)
        #expect(merged.versions == ["2.6.0", "2.5.0"])
        #expect(merged.version == "2.6.0")
        let t = [card("2.4.0", "a1"), merged, card("2.6.0", "c1")]
        // Seen 2.5.0: its 2.6.0 part is new.
        #expect(ids(WhatsNew.cards(lastSeen: "2.5.0", current: "2.6.0", table: t)) == ["m", "c1"])
        // Running 2.5.0: its 2.5.0 part is, and it sorts with 2.5.0.
        #expect(ids(WhatsNew.cards(lastSeen: "2.3.4", current: "2.5.0", table: t)) == ["m", "a1"])
        #expect(WhatsNew.cards(lastSeen: "2.6.0", current: "2.6.0", table: t).isEmpty)
        #expect(WhatsNew.rangeLabel(WhatsNew.cards(lastSeen: "2.4.0", current: "2.6.0", table: t),
                                    lastSeen: "2.4.0", current: "2.6.0") == "New in 2.5.0 \u{2013} 2.6.0")
    }

    @Test func oneHeaderForTheReleasesShown() {
        func label(_ lastSeen: String?, _ current: String) -> String? {
            WhatsNew.rangeLabel(WhatsNew.cards(lastSeen: lastSeen, current: current, table: table),
                                lastSeen: lastSeen, current: current)
        }
        #expect(label("2.5.0", "2.6.0") == "New in 2.6.0")
        #expect(label("2.3.4", "2.6.0") == "New in 2.4.0 – 2.6.0")
        #expect(label("2.4.1", "2.6.2") == "New in 2.5.0 – 2.6.2")
        #expect(label(nil, "2.5.0") == "New in 2.4.0 – 2.5.0")
        #expect(label("2.6.0", "2.6.0") == nil)
        // About and the menus: the full range up to the running version.
        #expect(WhatsNew.rangeLabel(WhatsNew.all(current: "2.6.0", table: table), lastSeen: nil,
                                    current: "2.6.0") == "New in 2.4.0 – 2.6.0")
    }
}

@Suite("What's New cards")
struct WhatsNewTableTests {
    let table = WhatsNew.table

    @Test func idsUnique() {
        #expect(Set(table.map(\.id)).count == table.count)
    }

    @Test func releasesSinceTwoFour() {
        let releases: Set<String> = ["2.4.0", "2.5.0", "2.6.0", "2.7.0", "2.8.0", "2.9.0", "2.10.0", "2.11.0", "2.12.0", "2.13.0", "2.14.0", "2.15.0", "2.16.0", "2.17.0", "2.18.0", "2.19.0", "2.20.0", "2.21.0", "2.22.0", "2.23.0", "2.24.0", "2.25.0", "2.26.0", "2.27.0", "2.28.0", "2.29.0", "2.30.0", "2.31.0", "2.32.0", "2.33.0", "2.34.0", "2.35.0", "2.36.0", "2.37.0", "2.38.0"]
        #expect(Set(table.map(\.version)) == releases)
        #expect(table.count == 75)
        #expect(table.filter { $0.version == "2.23.0" }.map(\.id) == ["watch-words"])
        #expect(table.filter { $0.version == "2.24.0" }.map(\.id) == ["marketplace"])
        #expect(table.filter { $0.version == "2.25.0" }.map(\.id) == ["typed-name-blue"])
        #expect(table.filter { $0.version == "2.38.0" }.map(\.id) == ["system-piece", "weather-piece", "ci-runners"])
        #expect(table.filter { $0.version == "2.37.0" }.map(\.id) == ["claude-desk", "slant-and-styles", "font-packs"])
        #expect(table.filter { $0.version == "2.36.0" }.map(\.id) == ["claude-widgets", "own-art"])
        #expect(table.filter { $0.version == "2.35.0" }.map(\.id) == ["widgets-catalog", "desk-companion", "oled-care"])
        #expect(table.filter { $0.version == "2.34.0" }.map(\.id) == ["plan-label"])
        #expect(table.filter { $0.version == "2.33.0" }.map(\.id) == ["lock-screen-line"])
        #expect(table.filter { $0.version == "2.32.0" }.map(\.id) == ["session-card-up"])
        #expect(table.filter { $0.version == "2.31.0" }.map(\.id) == ["breath-layer"])
        #expect(table.filter { $0.version == "2.30.0" }.map(\.id) == ["pill-indicators", "idle-cpu"])
        #expect(table.filter { $0.version == "2.29.0" }.map(\.id) == ["desk-only-tour"])
        #expect(table.filter { $0.version == "2.28.0" }.map(\.id) == ["no-notch-pill"])
        #expect(table.filter { $0.version == "2.27.0" }.map(\.id) == ["desk-under-files", "mods-first-run"])
        #expect(table.filter { $0.version == "2.26.0" }.map(\.id) == ["carry-more"])
        #expect(table.filter { $0.version == "2.22.0" }.map(\.id) == ["any-agent"])
        #expect(table.filter { $0.version == "2.21.0" }.map(\.id) == ["share-mod", "config-pack", "try-proposal"])
        #expect(table.filter { $0.version == "2.20.0" }.map(\.id) == ["new-mod", "mod-studio", "compact-catalog", "notch-keeps-settings"])
        #expect(table.filter { $0.version == "2.19.0" }.map(\.id) == ["account-folder", "seat-statusline", "hide-hourglass"])
        #expect(table.filter { $0.version == "2.18.0" }.map(\.id) == ["mods-catalog", "session-load", "compare-seats"])
        #expect(table.filter { $0.version == "2.17.0" }.map(\.id) == ["reset-sanduhr", "signed-out-clarity", "mods-folders"])
        #expect(table.filter { $0.version == "2.16.0" }.map(\.id) == ["per-seat-meters"])
        #expect(table.filter { $0.version == "2.15.0" }.map(\.id) == ["just-the-desk"])
        #expect(table.filter { $0.version == "2.14.0" }.map(\.id) == ["shortcut-app-menus", "sign-in-reminders"])
        #expect(table.filter { $0.version == "2.13.0" }.map(\.id) == ["meter-history"])
        #expect(table.filter { $0.version == "2.12.0" }.map(\.id) == ["shortcut-awareness"])
        #expect(table.filter { $0.version == "2.11.0" }.map(\.id)
                == ["settings-pages", "settings-search", "shortcut-keys", "account-status"])
        #expect(table.filter { $0.version == "2.10.0" }.map(\.id)
                == ["arrange-desk", "above-the-prompt", "song-looks", "desk-clicks", "mod-switches"])
        #expect(table.filter { $0.version == "2.9.0" }.map(\.id)
                == ["message-editor", "desk-layout", "settings-previews", "estefont-pro", "mods-page", "camera-mic"])
        #expect(table.filter { $0.version == "2.8.0" }.map(\.id) == ["watchers", "combine-statusline"])
        #expect(table.filter { $0.version == "2.7.0" }.map(\.id) == ["sign-in", "tour", "now-playing"])
        #expect(table.filter { $0.version == "2.6.0" }.map(\.id)
                == ["claude-suggests", "dock-aware-desk", "estefont"])
        #expect(table.filter { $0.version == "2.5.0" }.map(\.id) == ["usage-page", "integrations"])
        #expect(table.filter { $0.version == "2.4.0" }.map(\.id) == ["accounts", "menu-bar"])
        // Now playing spans 2.6.0 (the feature) and 2.7.0 (what shows when nothing plays).
        #expect(table.first { $0.id == "now-playing" }?.versions == ["2.7.0", "2.6.0"])
        // A card's releases are all in the table's range, newest (its array's) first.
        for c in table {
            #expect(!c.versions.isEmpty && c.versions.allSatisfy { releases.contains($0) }, "\(c.id)")
        }
    }

    @Test func eachCardIsShortAndPlain() {
        let banned = ["seamless", "powerful", "unlock", "leverage", "delightful"]
        for c in table {
            #expect(!c.title.isEmpty && !c.body.isEmpty, "\(c.id)")
            #expect(c.title.count <= 48, "\(c.id) title")
            #expect(c.body.count <= 200, "\(c.id) body")
            #expect(c.body.hasSuffix("."), "\(c.id) ends with a period")
            #expect(!c.title.hasSuffix("."), "\(c.id) title has no period")
            // One or two sentences.
            let sentences = c.body.split(separator: ".").filter { !$0.trimmingCharacters(in: .whitespaces).isEmpty }
            #expect((1...2).contains(sentences.count), "\(c.id) sentences")
            for word in banned { #expect(!c.body.lowercased().contains(word), "\(c.id): \(word)") }
        }
    }

    @Test func showMeLandsOnTheRightPage() {
        let dest = Dictionary(uniqueKeysWithValues: table.map { ($0.id, $0.destination) })
        #expect(dest == [
            "system-piece": .deskLayout, "weather-piece": .weather, "ci-runners": .watchers,
            "claude-desk": .deskLayout, "slant-and-styles": .deskLayout, "font-packs": .widgets,
            "claude-widgets": .widgets, "own-art": .widgets, "widgets-catalog": .widgets, "desk-companion": .deskLayout, "oled-care": .deskLayout,
            "plan-label": .credentials, "lock-screen-line": .lockScreen, "session-card-up": .mods, "breath-layer": .general, "pill-indicators": .notch, "idle-cpu": .usage,
            "desk-only-tour": .deskLayout, "no-notch-pill": .notch,
            "desk-under-files": .deskLayout, "mods-first-run": .mods,
            "carry-more": .mods, "typed-name-blue": .mods, "marketplace": .mods, "watch-words": .mods, "any-agent": .mods,
            "share-mod": .mods, "config-pack": .mods, "try-proposal": .mods,
            "new-mod": .mods, "mod-studio": .mods, "compact-catalog": .mods, "notch-keeps-settings": .notch,
            "account-folder": .credentials, "seat-statusline": .mods, "hide-hourglass": .general,
            "mods-catalog": .mods, "session-load": .mods, "compare-seats": .mods,
            "reset-sanduhr": .about, "signed-out-clarity": .credentials, "mods-folders": .mods,
            "per-seat-meters": .credentials, "just-the-desk": .deskLayout, "shortcut-app-menus": .general, "sign-in-reminders": .alerts,
            "meter-history": .usage, "shortcut-awareness": .general,
            "settings-pages": .general, "settings-search": .general, "shortcut-keys": .general,
            "account-status": .credentials,
            "arrange-desk": .deskLayout, "above-the-prompt": .mods, "song-looks": .nowPlaying,
            "desk-clicks": .deskLayout, "mod-switches": .mods,
            "message-editor": .message, "desk-layout": .deskLayout, "settings-previews": .notch,
            "estefont-pro": .deskLook, "mods-page": .mods, "camera-mic": .notch,
            "watchers": .watchers, "combine-statusline": .integrations,
            "sign-in": .credentials, "tour": .about,
            "now-playing": .nowPlaying, "claude-suggests": .message,
            "dock-aware-desk": .deskLayout, "estefont": .deskLook,
            "usage-page": .usage, "integrations": .integrations,
            "accounts": .credentials, "menu-bar": .general,
        ])
    }

    @Test func updatingFromTwoFiveShowsTwoSixUnderOneHeader() {
        let fromTwoFive = WhatsNew.cards(lastSeen: "2.5.0", current: "2.6.0")
        #expect(fromTwoFive.map(\.id) == ["now-playing", "claude-suggests", "dock-aware-desk", "estefont"])
        #expect(WhatsNew.rangeLabel(fromTwoFive, lastSeen: "2.5.0", current: "2.6.0") == "New in 2.6.0")
    }

    @Test func updatingToTwoSeven() {
        let fromTwoSix = WhatsNew.cards(lastSeen: "2.6.0", current: "2.7.0")
        #expect(fromTwoSix.map(\.id) == ["sign-in", "tour", "now-playing"])
        #expect(WhatsNew.rangeLabel(fromTwoSix, lastSeen: "2.6.0", current: "2.7.0") == "New in 2.7.0")
        // From before 2.4: the cap keeps the newest eight, so 2.4's two cards drop.
        let fromOld = WhatsNew.cards(lastSeen: "2.3.4", current: "2.7.0")
        #expect(fromOld.count == WhatsNew.cap)
        #expect(fromOld.first?.id == "sign-in")
        #expect(!fromOld.contains { $0.version == "2.4.0" })
        #expect(WhatsNew.rangeLabel(fromOld, lastSeen: "2.3.4", current: "2.7.0") == "New in 2.5.0 – 2.7.0")
    }

    @Test func updatingToTwoEight() {
        let fromTwoSeven = WhatsNew.cards(lastSeen: "2.7.0", current: "2.8.0")
        #expect(fromTwoSeven.map(\.id) == ["watchers", "combine-statusline"])
        #expect(WhatsNew.rangeLabel(fromTwoSeven, lastSeen: "2.7.0", current: "2.8.0") == "New in 2.8.0")
    }

    @Test func updatingToTwoNine() {
        let fromTwoEight = WhatsNew.cards(lastSeen: "2.8.0", current: "2.9.0")
        #expect(fromTwoEight.map(\.id)
                == ["message-editor", "desk-layout", "settings-previews", "estefont-pro", "mods-page", "camera-mic"])
        #expect(WhatsNew.rangeLabel(fromTwoEight, lastSeen: "2.8.0", current: "2.9.0") == "New in 2.9.0")
    }

    @Test func updatingToTwoTen() {
        let fromTwoNine = WhatsNew.cards(lastSeen: "2.9.0", current: "2.10.0")
        #expect(fromTwoNine.map(\.id) == ["arrange-desk", "above-the-prompt", "song-looks", "desk-clicks", "mod-switches"])
        #expect(WhatsNew.rangeLabel(fromTwoNine, lastSeen: "2.9.0", current: "2.10.0") == "New in 2.10.0")
    }

    @Test func updatingToTwoEleven() {
        let fromTwoTen = WhatsNew.cards(lastSeen: "2.10.1", current: "2.11.0")
        #expect(fromTwoTen.map(\.id) == ["settings-pages", "settings-search", "shortcut-keys", "account-status"])
        #expect(WhatsNew.rangeLabel(fromTwoTen, lastSeen: "2.10.1", current: "2.11.0") == "New in 2.11.0")
    }

    @Test func updatingToTwoTwelve() {
        let fromTwoEleven = WhatsNew.cards(lastSeen: "2.11.0", current: "2.12.0")
        #expect(fromTwoEleven.map(\.id) == ["shortcut-awareness"])
        #expect(WhatsNew.rangeLabel(fromTwoEleven, lastSeen: "2.11.0", current: "2.12.0") == "New in 2.12.0")
    }

    @Test func updatingToTwoThirteen() {
        let fromTwoTwelve = WhatsNew.cards(lastSeen: "2.12.0", current: "2.13.0")
        #expect(fromTwoTwelve.map(\.id) == ["meter-history"])
        #expect(WhatsNew.rangeLabel(fromTwoTwelve, lastSeen: "2.12.0", current: "2.13.0") == "New in 2.13.0")
    }

    @Test func updatingToTwoFourteen() {
        let fromTwoThirteen = WhatsNew.cards(lastSeen: "2.13.0", current: "2.14.0")
        #expect(fromTwoThirteen.map(\.id) == ["shortcut-app-menus", "sign-in-reminders"])
        #expect(WhatsNew.rangeLabel(fromTwoThirteen, lastSeen: "2.13.0", current: "2.14.0") == "New in 2.14.0")
    }

    @Test func updatingToTwoFifteen() {
        let fromTwoFourteen = WhatsNew.cards(lastSeen: "2.14.0", current: "2.15.0")
        #expect(fromTwoFourteen.map(\.id) == ["just-the-desk"])
        #expect(WhatsNew.rangeLabel(fromTwoFourteen, lastSeen: "2.14.0", current: "2.15.0") == "New in 2.15.0")
    }

    @Test func updatingToTwoSixteen() {
        let fromTwoFifteen = WhatsNew.cards(lastSeen: "2.15.0", current: "2.16.0")
        #expect(fromTwoFifteen.map(\.id) == ["per-seat-meters"])
        #expect(WhatsNew.rangeLabel(fromTwoFifteen, lastSeen: "2.15.0", current: "2.16.0") == "New in 2.16.0")
    }

    @Test func updatingToTwoSeventeen() {
        let fromTwoSixteen = WhatsNew.cards(lastSeen: "2.16.0", current: "2.17.0")
        #expect(fromTwoSixteen.map(\.id) == ["reset-sanduhr", "signed-out-clarity", "mods-folders"])
        #expect(WhatsNew.rangeLabel(fromTwoSixteen, lastSeen: "2.16.0", current: "2.17.0") == "New in 2.17.0")
    }

    @Test func updatingToTwoEighteen() {
        let fromTwoSeventeen = WhatsNew.cards(lastSeen: "2.17.0", current: "2.18.0")
        #expect(fromTwoSeventeen.map(\.id) == ["mods-catalog", "session-load", "compare-seats"])
        #expect(WhatsNew.rangeLabel(fromTwoSeventeen, lastSeen: "2.17.0", current: "2.18.0") == "New in 2.18.0")
    }

    @Test func updatingToTwoNineteen() {
        let fromTwoEighteen = WhatsNew.cards(lastSeen: "2.18.0", current: "2.19.0")
        #expect(fromTwoEighteen.map(\.id) == ["account-folder", "seat-statusline", "hide-hourglass"])
        #expect(WhatsNew.rangeLabel(fromTwoEighteen, lastSeen: "2.18.0", current: "2.19.0") == "New in 2.19.0")
    }

    @Test func updatingToTwoTwenty() {
        let fromTwoNineteen = WhatsNew.cards(lastSeen: "2.19.0", current: "2.20.0")
        #expect(fromTwoNineteen.map(\.id) == ["new-mod", "mod-studio", "compact-catalog", "notch-keeps-settings"])
        #expect(WhatsNew.rangeLabel(fromTwoNineteen, lastSeen: "2.19.0", current: "2.20.0") == "New in 2.20.0")
    }

    @Test func updatingToTwoTwentyOne() {
        let fromTwoTwenty = WhatsNew.cards(lastSeen: "2.20.0", current: "2.21.0")
        #expect(fromTwoTwenty.map(\.id) == ["share-mod", "config-pack", "try-proposal"])
        #expect(WhatsNew.rangeLabel(fromTwoTwenty, lastSeen: "2.20.0", current: "2.21.0") == "New in 2.21.0")
    }

    @Test func updatingToTwoTwentyTwo() {
        let fromTwoTwentyOne = WhatsNew.cards(lastSeen: "2.21.0", current: "2.22.0")
        #expect(fromTwoTwentyOne.map(\.id) == ["any-agent"])
        #expect(WhatsNew.rangeLabel(fromTwoTwentyOne, lastSeen: "2.21.0", current: "2.22.0") == "New in 2.22.0")
    }

    @Test func updatingToTwoTwentySix() {
        let fromTwoTwentyFive = WhatsNew.cards(lastSeen: "2.25.1", current: "2.26.0")
        #expect(fromTwoTwentyFive.map(\.id) == ["carry-more"])
        #expect(WhatsNew.rangeLabel(fromTwoTwentyFive, lastSeen: "2.25.1", current: "2.26.0") == "New in 2.26.0")
    }

    @Test func updatingToTwoThirtyEight() {
        let fromTwoThirtySeven = WhatsNew.cards(lastSeen: "2.37.0", current: "2.38.0")
        #expect(fromTwoThirtySeven.map(\.id) == ["system-piece", "weather-piece", "ci-runners"])
        #expect(WhatsNew.rangeLabel(fromTwoThirtySeven, lastSeen: "2.37.0", current: "2.38.0") == "New in 2.38.0")
    }

    @Test func updatingToTwoThirtySeven() {
        let fromTwoThirtySix = WhatsNew.cards(lastSeen: "2.36.0", current: "2.37.0")
        #expect(fromTwoThirtySix.map(\.id) == ["claude-desk", "slant-and-styles", "font-packs"])
        #expect(WhatsNew.rangeLabel(fromTwoThirtySix, lastSeen: "2.36.0", current: "2.37.0") == "New in 2.37.0")
    }

    @Test func updatingToTwoThirtySix() {
        let fromTwoThirtyFive = WhatsNew.cards(lastSeen: "2.35.0", current: "2.36.0")
        #expect(fromTwoThirtyFive.map(\.id) == ["claude-widgets", "own-art"])
        #expect(WhatsNew.rangeLabel(fromTwoThirtyFive, lastSeen: "2.35.0", current: "2.36.0") == "New in 2.36.0")
    }

    @Test func updatingToTwoThirtyFive() {
        let fromTwoThirtyFour = WhatsNew.cards(lastSeen: "2.34.0", current: "2.35.0")
        #expect(fromTwoThirtyFour.map(\.id) == ["widgets-catalog", "desk-companion", "oled-care"])
        #expect(WhatsNew.rangeLabel(fromTwoThirtyFour, lastSeen: "2.34.0", current: "2.35.0") == "New in 2.35.0")
    }

    @Test func updatingToTwoThirtyFour() {
        let fromTwoThirtyThree = WhatsNew.cards(lastSeen: "2.33.1", current: "2.34.0")
        #expect(fromTwoThirtyThree.map(\.id) == ["plan-label"])
        #expect(WhatsNew.rangeLabel(fromTwoThirtyThree, lastSeen: "2.33.1", current: "2.34.0") == "New in 2.34.0")
    }

    @Test func updatingToTwoThirtyThree() {
        let fromTwoThirtyTwo = WhatsNew.cards(lastSeen: "2.32.0", current: "2.33.0")
        #expect(fromTwoThirtyTwo.map(\.id) == ["lock-screen-line"])
        #expect(WhatsNew.rangeLabel(fromTwoThirtyTwo, lastSeen: "2.32.0", current: "2.33.0") == "New in 2.33.0")
    }

    @Test func updatingToTwoThirtyTwo() {
        let fromTwoThirtyOne = WhatsNew.cards(lastSeen: "2.31.0", current: "2.32.0")
        #expect(fromTwoThirtyOne.map(\.id) == ["session-card-up"])
        #expect(WhatsNew.rangeLabel(fromTwoThirtyOne, lastSeen: "2.31.0", current: "2.32.0") == "New in 2.32.0")
    }

    @Test func updatingToTwoThirtyOne() {
        let fromTwoThirty = WhatsNew.cards(lastSeen: "2.30.0", current: "2.31.0")
        #expect(fromTwoThirty.map(\.id) == ["breath-layer"])
        #expect(WhatsNew.rangeLabel(fromTwoThirty, lastSeen: "2.30.0", current: "2.31.0") == "New in 2.31.0")
    }

    @Test func updatingToTwoThirty() {
        let fromTwoTwentyNine = WhatsNew.cards(lastSeen: "2.29.0", current: "2.30.0")
        #expect(fromTwoTwentyNine.map(\.id) == ["pill-indicators", "idle-cpu"])
        #expect(WhatsNew.rangeLabel(fromTwoTwentyNine, lastSeen: "2.29.0", current: "2.30.0") == "New in 2.30.0")
    }

    @Test func updatingToTwoTwentyNine() {
        let fromTwoTwentyEight = WhatsNew.cards(lastSeen: "2.28.0", current: "2.29.0")
        #expect(fromTwoTwentyEight.map(\.id) == ["desk-only-tour"])
        #expect(WhatsNew.rangeLabel(fromTwoTwentyEight, lastSeen: "2.28.0", current: "2.29.0") == "New in 2.29.0")
    }

    @Test func updatingToTwoTwentyEight() {
        let fromTwoTwentySeven = WhatsNew.cards(lastSeen: "2.27.0", current: "2.28.0")
        #expect(fromTwoTwentySeven.map(\.id) == ["no-notch-pill"])
        #expect(WhatsNew.rangeLabel(fromTwoTwentySeven, lastSeen: "2.27.0", current: "2.28.0") == "New in 2.28.0")
    }

    @Test func updatingToTwoTwentySeven() {
        let fromTwoTwentySix = WhatsNew.cards(lastSeen: "2.26.1", current: "2.27.0")
        #expect(fromTwoTwentySix.map(\.id) == ["desk-under-files", "mods-first-run"])
        #expect(WhatsNew.rangeLabel(fromTwoTwentySix, lastSeen: "2.26.1", current: "2.27.0") == "New in 2.27.0")
    }

    @Test func updatingToTwoTwentyFive() {
        let fromTwoTwentyFour = WhatsNew.cards(lastSeen: "2.24.1", current: "2.25.0")
        #expect(fromTwoTwentyFour.map(\.id) == ["typed-name-blue"])
        #expect(WhatsNew.rangeLabel(fromTwoTwentyFour, lastSeen: "2.24.1", current: "2.25.0") == "New in 2.25.0")
    }

    @Test func updatingToTwoTwentyFour() {
        let fromTwoTwentyThree = WhatsNew.cards(lastSeen: "2.23.0", current: "2.24.0")
        #expect(fromTwoTwentyThree.map(\.id) == ["marketplace"])
        #expect(WhatsNew.rangeLabel(fromTwoTwentyThree, lastSeen: "2.23.0", current: "2.24.0") == "New in 2.24.0")
    }

    @Test func updatingToTwoTwentyThree() {
        let fromTwoTwentyTwo = WhatsNew.cards(lastSeen: "2.22.0", current: "2.23.0")
        #expect(fromTwoTwentyTwo.map(\.id) == ["watch-words"])
        #expect(WhatsNew.rangeLabel(fromTwoTwentyTwo, lastSeen: "2.22.0", current: "2.23.0") == "New in 2.23.0")
    }
}
