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
