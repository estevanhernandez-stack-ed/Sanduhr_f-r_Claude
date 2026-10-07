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
        let releases: Set<String> = ["2.4.0", "2.5.0", "2.6.0", "2.7.0", "2.8.0", "2.9.0"]
        #expect(Set(table.map(\.version)) == releases)
        #expect(table.count == 18)
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
            "message-editor": .message, "desk-layout": .deskLayout, "settings-previews": .notch,
            "estefont-pro": .deskLook, "mods-page": .mods, "camera-mic": .notch,
            "watchers": .integrations, "combine-statusline": .integrations,
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
}
