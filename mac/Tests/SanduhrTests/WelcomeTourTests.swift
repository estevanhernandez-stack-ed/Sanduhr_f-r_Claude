import Foundation
import Testing
@testable import Sanduhr

/// The welcome tour (item 61): which steps show for a Mac, who sees it once, What's New kept out
/// of its launch, the choices it writes, Show me, and the copy.

@Suite("Welcome tour steps")
struct WelcomeTourStepTests {
    func ids(_ steps: [[TourCard]]) -> [[String]] { steps.map { $0.map(\.id) } }

    @Test func fiveStepsWithANotch() {
        let steps = WelcomeTour.steps(features: [.notch])
        #expect(ids(steps) == [["paced"], ["desk"], ["menu-bar", "notch"], ["accounts"], ["claude-code"]])
        #expect(steps.map { $0[0].step } == [1, 2, 3, 4, 5])
    }

    @Test func withoutANotchStepThreeIsTheMenuBarAlone() {
        let steps = WelcomeTour.steps(features: [])
        #expect(ids(steps) == [["paced"], ["desk"], ["menu-bar"], ["accounts"], ["claude-code"]])
        #expect(steps.count == 5)
    }

    @Test func aStepWhoseOnlyCardNeedsAMissingFeatureHides() {
        let table = [
            TourCard(id: "a", step: 1, title: "a", body: "", art: .symbol("star"), showMe: .widget),
            TourCard(id: "b", step: 2, title: "b", body: "", art: .symbol("star"), showMe: .widget, requires: .notch),
            TourCard(id: "c", step: 3, title: "c", body: "", art: .symbol("star"), showMe: .widget),
        ]
        #expect(ids(WelcomeTour.steps(features: [], table: table)) == [["a"], ["c"]])
        #expect(ids(WelcomeTour.steps(features: [.notch], table: table)) == [["a"], ["b"], ["c"]])
    }

    @Test func cardsSortByStepThenTableOrder() {
        let table = [
            TourCard(id: "late", step: 2, title: "", body: "", art: .symbol("star"), showMe: .widget),
            TourCard(id: "early", step: 1, title: "", body: "", art: .symbol("star"), showMe: .widget),
            TourCard(id: "late-2", step: 2, title: "", body: "", art: .symbol("star"), showMe: .widget),
        ]
        #expect(ids(WelcomeTour.steps(features: [], table: table)) == [["early"], ["late", "late-2"]])
    }

    @Test func stepText() {
        #expect(WelcomeTour.stepText(0, of: 5) == "1 of 5")
        #expect(WelcomeTour.stepText(4, of: 5) == "5 of 5")
    }

    @Test func titlesAndChoices() {
        let byID = Dictionary(uniqueKeysWithValues: WelcomeTour.table.map { ($0.id, $0) })
        #expect(byID["paced"]?.title == "Your limits, paced")
        #expect(byID["desk"]?.title == "On your desktop")
        #expect(byID["menu-bar"]?.title == "At a glance")
        #expect(byID["accounts"]?.title == "More than one account")
        #expect(byID["claude-code"]?.title == "Claude Code, connected")
        #expect(byID["desk"]?.choices == [.desk, .matchDesk])
        #expect(byID["menu-bar"]?.choices == [.menuBar])
        // Only steps 2 and 3 ask anything; nothing installs from the tour.
        #expect(WelcomeTour.table.filter { !$0.choices.isEmpty }.map(\.id) == ["desk", "menu-bar"])
        #expect(byID["notch"]?.requires == .notch)
        #expect(WelcomeTour.table.filter { $0.requires != nil }.map(\.id) == ["notch"])
    }

    @MainActor @Test func showMeDestinations() {
        let dest = Dictionary(uniqueKeysWithValues: WelcomeTour.table.map { ($0.id, $0.showMe) })
        #expect(dest == [
            "paced": .widget, "desk": .settings(.deskLayout), "menu-bar": .settings(.general),
            "notch": .settings(.notch), "accounts": .settings(.credentials),
            "claude-code": .settings(.integrations),
        ])
        #expect(TourCardView.showMeHelp(.widget) == "Shows the widget")
        #expect(TourCardView.showMeHelp(.settings(.integrations)) == "Opens Settings, Claude Code")
    }

    @Test func copyIsShortAndPlain() {
        let banned = ["seamless", "powerful", "unlock", "leverage", "delightful"]
        #expect(Set(WelcomeTour.table.map(\.id)).count == WelcomeTour.table.count)
        for c in WelcomeTour.table {
            #expect(!c.title.isEmpty && !c.body.isEmpty, "\(c.id)")
            #expect(c.title.count <= 48, "\(c.id) title")
            #expect(c.body.count <= 200, "\(c.id) body")
            #expect(c.body.hasSuffix("."), "\(c.id) ends with a period")
            #expect(!c.title.hasSuffix("."), "\(c.id) title has no period")
            let sentences = c.body.split(separator: ".").filter { !$0.trimmingCharacters(in: .whitespaces).isEmpty }
            #expect((1...2).contains(sentences.count), "\(c.id) sentences")
            #expect(!c.body.contains("\u{2014}"), "\(c.id) has no em-dash")
            for word in banned { #expect(!c.body.lowercased().contains(word), "\(c.id): \(word)") }
        }
        #expect(WelcomeTour.againLine == "You can take the tour again from About or the menus.")
    }
}

@Suite("Welcome tour shown once")
struct WelcomeTourOnceTests {
    @Test func freshInstallWithNothingIsPending() {
        #expect(WelcomeTour.decide(existing: nil, fresh: true, hasKey: false, accounts: 0, lastSeen: nil) == .pending)
    }

    @Test func neverOnUpdateOrWithAKeyAccountsOrWhatsNew() {
        #expect(WelcomeTour.decide(existing: nil, fresh: false, hasKey: false, accounts: 0, lastSeen: nil) == .notOffered)
        #expect(WelcomeTour.decide(existing: nil, fresh: true, hasKey: true, accounts: 0, lastSeen: nil) == .notOffered)
        #expect(WelcomeTour.decide(existing: nil, fresh: true, hasKey: false, accounts: 1, lastSeen: nil) == .notOffered)
        #expect(WelcomeTour.decide(existing: nil, fresh: true, hasKey: false, accounts: 0, lastSeen: "2.6.0") == .notOffered)
    }

    @Test func decidedOnce() {
        // A later launch is never fresh; a pending tour stays pending until finished or skipped.
        #expect(WelcomeTour.decide(existing: .pending, fresh: false, hasKey: true, accounts: 1, lastSeen: "2.6.0") == .pending)
        #expect(WelcomeTour.decide(existing: .notOffered, fresh: true, hasKey: false, accounts: 0, lastSeen: nil) == .notOffered)
        #expect(WelcomeTour.decide(existing: .skipped, fresh: true, hasKey: false, accounts: 0, lastSeen: nil) == .skipped)
    }

    @Test func atLaunchWritesTheDecision() {
        let d = MemoryDefaults()
        #expect(WelcomeTour.state(in: d) == nil)
        #expect(WelcomeTour.atLaunch(fresh: true, hasKey: false, accounts: 0, lastSeen: nil, in: d) == .pending)
        #expect(d.object(forKey: "welcomeTourState") as? String == "pending")
        // Signed in by the next launch: still pending.
        #expect(WelcomeTour.atLaunch(fresh: false, hasKey: true, accounts: 1, lastSeen: "2.6.0", in: d) == .pending)
        let updated = MemoryDefaults()
        #expect(WelcomeTour.atLaunch(fresh: false, hasKey: true, accounts: 1, lastSeen: "2.5.0", in: updated) == .notOffered)
        #expect(!WelcomeTour.done(in: updated))
    }

    @Test func showsAfterTheFirstSuccessfulFetchOnceALaunch() {
        #expect(!WelcomeTour.showsAfterFetch(state: .pending, fetched: false, offeredThisLaunch: false))
        #expect(WelcomeTour.showsAfterFetch(state: .pending, fetched: true, offeredThisLaunch: false))
        #expect(!WelcomeTour.showsAfterFetch(state: .pending, fetched: true, offeredThisLaunch: true))
        for state in [WelcomeTour.State.finished, .skipped, .notOffered] {
            #expect(!WelcomeTour.showsAfterFetch(state: state, fetched: true, offeredThisLaunch: false))
        }
        #expect(!WelcomeTour.showsAfterFetch(state: nil, fetched: true, offeredThisLaunch: false))
    }

    @Test func finishAndSkipAreDoneAndRecordWhatsNew() {
        for finished in [true, false] {
            let d = MemoryDefaults()
            WelcomeTour.atLaunch(fresh: true, hasKey: false, accounts: 0, lastSeen: nil, in: d)
            WelcomeTour.end(finished, current: "2.7.0", in: d)
            #expect(WelcomeTour.state(in: d) == (finished ? .finished : .skipped))
            #expect(WelcomeTour.done(in: d))
            #expect(WhatsNew.lastSeen(in: d) == "2.7.0")
            // So the next update shows only newer cards.
            #expect(WhatsNew.cards(lastSeen: WhatsNew.lastSeen(in: d), current: "2.7.0").isEmpty)
        }
    }

    @Test func reopeningChangesNothing() {
        let d = MemoryDefaults()
        WelcomeTour.atLaunch(fresh: true, hasKey: false, accounts: 0, lastSeen: nil, in: d)
        WelcomeTour.end(false, current: "2.7.0", in: d)
        // Reopened from About, then finished: still skipped, What's New untouched.
        WhatsNew.record("2.6.0", in: d)
        WelcomeTour.end(true, current: "2.8.0", in: d)
        #expect(WelcomeTour.state(in: d) == .skipped)
        #expect(WhatsNew.lastSeen(in: d) == "2.6.0")
        // Never offered: reopening and finishing records nothing.
        let updated = MemoryDefaults()
        WelcomeTour.atLaunch(fresh: false, hasKey: true, accounts: 1, lastSeen: "2.6.0", in: updated)
        WelcomeTour.end(true, current: "2.7.0", in: updated)
        #expect(WelcomeTour.state(in: updated) == .notOffered)
        #expect(updated.object(forKey: WhatsNew.lastSeenKey) == nil)
    }

    @Test func whatsNewWaitsInTheTourLaunch() {
        let table = [WhatsNewCard(version: "2.6.0", id: "x", title: "x", body: "", art: .symbol("star"), destination: .general)]
        let d = WhatsNew.atLaunch(lastSeen: "2.5.0", current: "2.6.0", fresh: false, onboarding: false,
                                  hidden: false, tour: true, table: table)
        #expect(d == WhatsNew.LaunchDecision(show: [], record: false))
        // Without the tour the same launch shows the card.
        let without = WhatsNew.atLaunch(lastSeen: "2.5.0", current: "2.6.0", fresh: false, onboarding: false,
                                        hidden: false, table: table)
        #expect(without.show.map(\.id) == ["x"])
        // A fresh install's first launch still records the version.
        let fresh = WhatsNew.atLaunch(lastSeen: nil, current: "2.6.0", fresh: true, onboarding: true,
                                      hidden: false, tour: true, table: table)
        #expect(fresh == WhatsNew.LaunchDecision(show: [], record: true))
    }
}

@Suite("Welcome tour choices")
struct WelcomeTourChoiceTests {
    @Test func deskRoundTrips() {
        let desk = MemoryDefaults()
        #expect(!WelcomeTour.deskOn(in: desk))
        WelcomeTour.setDesk(true, in: desk)
        #expect(desk.object(forKey: DeskController.enabledKey) as? Bool == true)
        #expect(WelcomeTour.deskOn(in: desk))
        WelcomeTour.setDesk(false, in: desk)
        #expect(!WelcomeTour.deskOn(in: desk))
    }

    @Test func matchDeskOnAndOffRestoresTheThemeBefore() {
        let d = MemoryDefaults()
        let on = WelcomeTour.theme(matchDesk: true, current: "aurora", fallback: "obsidian", in: d)
        #expect(on == DeskThemeMapping.id)
        #expect(WelcomeTour.matchDesk(themeID: on))
        #expect(d.object(forKey: WelcomeTour.themeBeforeMatchDeskKey) as? String == "aurora")
        // Back and on again keeps the theme from before Match Desk, not Match Desk itself.
        #expect(WelcomeTour.theme(matchDesk: true, current: on, fallback: "obsidian", in: d) == on)
        #expect(d.object(forKey: WelcomeTour.themeBeforeMatchDeskKey) as? String == "aurora")
        #expect(WelcomeTour.theme(matchDesk: false, current: on, fallback: "obsidian", in: d) == "aurora")
        #expect(d.object(forKey: WelcomeTour.themeBeforeMatchDeskKey) == nil)
    }

    @Test func matchDeskOffWithNothingRememberedUsesTheFallback() {
        let d = MemoryDefaults()
        #expect(WelcomeTour.theme(matchDesk: false, current: DeskThemeMapping.id, fallback: "obsidian", in: d) == "obsidian")
        // Off while another theme is in use changes nothing.
        #expect(WelcomeTour.theme(matchDesk: false, current: "aurora", fallback: "obsidian", in: d) == "aurora")
    }

    @Test func menuBarRoundTrips() {
        let d = MemoryDefaults()
        #expect(MenuBarMode.saved(in: d) == .higher)
        for mode in MenuBarMode.allCases {
            MenuBarMode.save(mode, in: d)
            #expect(MenuBarMode.saved(in: d) == mode)
        }
        #expect(d.object(forKey: "menuBarMode") as? String == "rotate")
    }
}
