import Foundation
import Testing
@testable import Sanduhr

/// Settings v2, slice 4 (first-run pieces, 2.14.0): a fresh install says it isn't signed in
/// instead of "Connecting…", the footer's model link waits for numbers, the tour taken before
/// sign-in points at signing in, and What's New on the installed version shows its Highlights.
@Suite("First run")
struct FirstRunTests {
    @Test func notSignedInSaysSoAndNeedsSignIn() {
        let s = UsageViewModel.StatusMessage.notSignedIn
        #expect(s.text == "Not signed in — sign in")
        #expect(s.needsSignIn)
        #expect(!s.isError)
        #expect(UsageViewModel.StatusMessage.connecting.text == "Connecting…")
        #expect(!UsageViewModel.StatusMessage.connecting.needsSignIn)
    }

    @Test func modelLinkWaitsForNumbersAndASignIn() {
        #expect(FooterView.showsModelLink(hasNumbers: true, needsSignIn: false))
        #expect(!FooterView.showsModelLink(hasNumbers: false, needsSignIn: false))
        #expect(!FooterView.showsModelLink(hasNumbers: true, needsSignIn: true))
        #expect(!FooterView.showsModelLink(hasNumbers: false, needsSignIn: true))
    }

    @Test func theTourBeforeSignInPointsAtSigningIn() {
        let signedIn = WelcomeTour.steps(features: [.notch])
        let signedOut = WelcomeTour.steps(features: [.notch], signedIn: false)
        #expect(signedIn.count == signedOut.count)
        let first = signedOut[0][0]
        #expect(first.body.hasPrefix("Sign in to see your meters here."))
        #expect(first.showMe == .settings(.credentials))
        #expect(first.showMeTitle == "Sign In")
        #expect(first.title == signedIn[0][0].title)
        #expect(signedIn[0][0].showMeTitle == "Show me")
        // Every later step as written.
        #expect(Array(signedOut.dropFirst()) == Array(signedIn.dropFirst()))
        #expect(WelcomeTourView.title == "Sanduhr Tour")
    }

    @Test func whatsNewOnTheInstalledVersionShowsItsHighlights() {
        let table = WhatsNew.table
        let current = "2.13.0"
        let fresh = WhatsNew.forMenu(current: current, installed: current, table: table)
        #expect(fresh.header == "Highlights in 2.13.0")
        #expect(fresh.cards.map(\.id) == ["meter-history"])
        // Updated since the install, or installed before 2.14.0 (nothing recorded): every card.
        for installed in ["2.12.0", nil] as [String?] {
            let every = WhatsNew.forMenu(current: current, installed: installed, table: table)
            #expect(every.cards.count == WhatsNew.all(current: current, table: table).count)
            #expect(every.header.hasPrefix("New in 2.4.0"))
        }
        // A version without cards of its own (a patch) shows every card.
        let patch = WhatsNew.forMenu(current: "2.10.1", installed: "2.10.1", table: table)
        #expect(patch.header.hasPrefix("New in"))
        #expect(!patch.cards.isEmpty)
    }

    @Test func theInstalledVersionRoundTrips() {
        let d = MemoryDefaults()
        #expect(WhatsNew.installed(in: d) == nil)
        WhatsNew.recordInstalled("2.14.0", in: d)
        #expect(WhatsNew.installed(in: d) == "2.14.0")
        #expect(d.values[WhatsNew.installedKey] as? String == "2.14.0")
    }
}

/// Before the first key, the Desk asks to sign in, not to sign in again; the notch's meters wing
/// shows its short sign-in line instead of an empty wing (Settings v2 F25).
@Suite("First run on the Desk and notch")
struct FirstRunDeskTests {
    @Test func theDeskLineAsksToSignInNotAgain() {
        var first = DeskUsage()
        first.signInNeeded = true
        first.firstSignIn = true
        #expect(DeskClaudeText.line(first) == "claude   sign in to Sanduhr")
        var expired = DeskUsage()
        expired.signInNeeded = true
        #expect(DeskClaudeText.line(expired) == "claude   sign in again in Sanduhr")
        #expect(DeskClaudeText.signInLine(first: true) == DeskClaudeText.compactSignIn)
    }

    @Test func theMetersWingShowsTheSignInLine() {
        var first = DeskUsage()
        first.signInNeeded = true
        first.firstSignIn = true
        #expect(DeskClaudeText.compact(first) == "sign in to Sanduhr")
        #expect(NotchContent.meters.text(at: .right, meetings: [], meters: DeskClaudeText.compact(first),
                                         message: nil, now: Date()) == "sign in to Sanduhr")
        // Before this release a fresh install was "Connecting…", which needed no sign-in: no line.
        #expect(DeskClaudeText.compact(DeskUsage()) == nil)
    }
}

/// Alerts, "Sign-in reminders on the Desk and notch" (SignInReminder, 2026-10-10): off, neither
/// "sign in to Sanduhr" nor "sign in again in Sanduhr" is written; the last numbers still show.
@Suite("Sign-in reminder switch")
struct SignInReminderTests {
    @Test func onByDefaultAndRoundTrips() {
        let d = MemoryDefaults()
        #expect(SignInReminder.isOn(in: d))
        SignInReminder.set(false, in: d)
        #expect(!SignInReminder.isOn(in: d))
        #expect(d.values[SignInReminder.key] as? Bool == false)
    }

    @Test func offWritesNoSignInLineEitherWay() {
        for first in [true, false] {
            var u = DeskUsage()
            u.signInNeeded = true
            u.firstSignIn = first
            u.signInReminder = false
            #expect(DeskClaudeText.line(u) == nil)
            #expect(DeskClaudeText.compact(u) == nil)
            #expect(NotchContent.meters.text(at: .right, meetings: [], meters: DeskClaudeText.compact(u),
                                             message: nil, now: Date()) == nil)
        }
    }

    @Test func offAnExpiredSessionKeepsItsLastNumbersDimmed() {
        let now = Date()
        var u = DeskUsage()
        u.usage = UsageResponse(tiers: [.fiveHour: TierUsage(utilization: 42, resetsAt: nil)])
        u.fetchedAt = now
        u.signInNeeded = true
        u.signInReminder = false
        #expect(DeskClaudeText.line(u) == "claude   42% session")
        #expect(u.isStale(now: now))                       // drawn dimmed, and the notch drops it
        #expect(DeskClaudeText.compact(u, now: now) == nil)
        u.signInReminder = true
        #expect(DeskClaudeText.line(u) == "claude   sign in again in Sanduhr")
    }

    @Test func searchFindsIt() {
        #expect(SettingsAnchor.entries(on: .alerts).contains { $0.title == "Sign-in reminders" })
    }
}
