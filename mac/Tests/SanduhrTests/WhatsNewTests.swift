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

    @Test func versionLabel() {
        #expect(card("2.6.0", "x").versionLabel == "New in 2.6")
        #expect(card("2.6.1", "x").versionLabel == "New in 2.6.1")
    }
}

@Suite("What's New cards")
struct WhatsNewTableTests {
    let table = WhatsNew.table

    @Test func idsUnique() {
        #expect(Set(table.map(\.id)).count == table.count)
    }

    @Test func releasesSinceTwoFour() {
        #expect(Set(table.map(\.version)) == ["2.4.0", "2.5.0", "2.6.0"])
        #expect(table.filter { $0.version == "2.6.0" }.map(\.id)
                == ["now-playing", "desk-messages", "claude-themes", "dock-aware-desk", "estefont"])
        #expect(table.filter { $0.version == "2.5.0" }.map(\.id) == ["usage-page", "integrations", "notch-glow"])
        #expect(table.filter { $0.version == "2.4.0" }.map(\.id) == ["accounts", "follow", "menu-bar", "hide-limits"])
    }

    @Test func eachCardIsShortAndPlain() {
        let banned = ["seamless", "powerful", "unlock", "leverage", "delightful"]
        for c in table {
            #expect(!c.title.isEmpty && !c.body.isEmpty, "\(c.id)")
            #expect(c.title.count <= 48, "\(c.id) title")
            #expect(c.body.count <= 180, "\(c.id) body")
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
            "now-playing": .nowPlaying, "desk-messages": .message, "claude-themes": .themes,
            "dock-aware-desk": .deskLayout, "estefont": .deskLook,
            "usage-page": .usage, "integrations": .integrations, "notch-glow": .notch,
            "accounts": .credentials, "follow": .credentials, "menu-bar": .general, "hide-limits": .deskMeters,
        ])
    }

    @Test func updatingFromTwoFiveShowsTwoSix() {
        #expect(WhatsNew.cards(lastSeen: "2.5.0", current: "2.6.0").map(\.version) == Array(repeating: "2.6.0", count: 5))
        // From before 2.4: capped, newest first, so 2.6 and 2.5 fill the window.
        let fromOld = WhatsNew.cards(lastSeen: "2.3.4", current: "2.6.0")
        #expect(fromOld.count == 8)
        #expect(fromOld.first?.version == "2.6.0")
        #expect(fromOld.last?.version == "2.5.0")
    }
}
