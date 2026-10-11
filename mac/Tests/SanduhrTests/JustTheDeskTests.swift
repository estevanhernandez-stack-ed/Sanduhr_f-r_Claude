import Foundation
import Testing
@testable import Sanduhr

/// "Just the Desk" (BACKLOG #4): chosen on the welcome sheet by someone without a Claude account,
/// ended by their first successful sign-in. Every test uses in-memory stores.
@Suite("Just the Desk")
struct JustTheDeskTests {
    @Test func offUntilChosen() {
        let s = MemoryDefaults()
        #expect(!JustTheDesk.isOn(in: s))
        #expect(JustTheDesk.showsWelcome(hasSessionKey: false, in: s))
        #expect(!JustTheDesk.showsWelcome(hasSessionKey: true, in: s))
        #expect(!JustTheDesk.signInSettled(awaitingSignIn: true, in: s))
        #expect(JustTheDesk.signInSettled(awaitingSignIn: false, in: s))
    }

    @Test func choosingTurnsTheDeskOnAndTheReminderOff() {
        let s = MemoryDefaults(), d = MemoryDefaults()
        JustTheDesk.choose(standard: s, desk: d)
        #expect(JustTheDesk.isOn(in: s))
        #expect(d.values[DeskController.enabledKey] as? Bool == true)
        #expect(!SignInReminder.isOn(in: d))
        #expect(s.values[JustTheDesk.silencedKey] as? Bool == true)
        // No welcome sheet, and the widget no longer waits for a sign-in.
        #expect(!JustTheDesk.showsWelcome(hasSessionKey: false, in: s))
        #expect(JustTheDesk.signInSettled(awaitingSignIn: true, in: s))
    }

    @Test func signingInEndsItAndBringsTheReminderBack() {
        let s = MemoryDefaults(), d = MemoryDefaults()
        JustTheDesk.choose(standard: s, desk: d)
        #expect(JustTheDesk.endBySignIn(standard: s, desk: d))
        #expect(!JustTheDesk.isOn(in: s))
        #expect(SignInReminder.isOn(in: d))
        #expect(s.values[JustTheDesk.key] == nil && s.values[JustTheDesk.silencedKey] == nil)
        // Once only.
        #expect(!JustTheDesk.endBySignIn(standard: s, desk: d))
        #expect(d.values[DeskController.enabledKey] as? Bool == true)      // the Desk stays on
    }

    @Test func aReminderTheyHadTurnedOffStaysOff() {
        let s = MemoryDefaults(), d = MemoryDefaults()
        SignInReminder.set(false, in: d)
        JustTheDesk.choose(standard: s, desk: d)
        #expect(s.values[JustTheDesk.silencedKey] == nil)
        JustTheDesk.endBySignIn(standard: s, desk: d)
        #expect(!SignInReminder.isOn(in: d))
    }

    @Test func theWidgetFollowsItsChoiceInsteadOfWaitingForSignIn() {
        // A fresh install's choice is Hidden while Desk is on; Just the Desk turns the Desk on.
        let settled = JustTheDesk.signInSettled(awaitingSignIn: true, in: {
            let s = MemoryDefaults(); s.set(true, forKey: JustTheDesk.key); return s
        }())
        #expect(WidgetVisibilityRule.shouldShow(setting: .whileDeskOff, deskOn: true, hasSessionKey: settled,
                                                event: .justTheDesk) == false)
        #expect(WidgetVisibilityRule.shouldShow(setting: .onRequest, deskOn: true, hasSessionKey: settled,
                                                event: .launch) == false)
        // "Always shown" is left as it is.
        #expect(WidgetVisibilityRule.shouldShow(setting: .always, deskOn: true, hasSessionKey: settled,
                                                event: .justTheDesk) == nil)
        // Without Just the Desk the widget still shows for sign-in.
        #expect(WidgetVisibilityRule.shouldShow(setting: .whileDeskOff, deskOn: true, hasSessionKey: false,
                                                event: .launch) == true)
    }

    @Test func theDeskDrawsNoSignInLine() {
        var u = DeskUsage()
        u.signInNeeded = true
        u.firstSignIn = true
        u.signInReminder = false
        #expect(DeskClaudeText.line(u) == nil)
        #expect(DeskClaudeText.compact(u) == nil)
    }

    @Test func copy() {
        #expect(JustTheDesk.buttonTitle == "Just the Desk")
        #expect(JustTheDesk.caption.contains("Sign in any time from Settings, Accounts."))
    }
}

/// Alerts and Claude Code say plainly that they start once you sign in (BACKLOG #4).
@Suite("Pages that need an account")
struct NeedsAccountNoteTests {
    @Test func showsOnlyWithoutASignIn() {
        #expect(NeedsAccountNote.shows(.notSignedIn))
        #expect(NeedsAccountNote.shows(.signedOut))
        for s: UsageViewModel.StatusMessage in [.idle, .refreshing, .connecting, .noTiers, .switching,
                                                .error("Session expired — sign in again", isAuth: true)] {
            #expect(!NeedsAccountNote.shows(s))
        }
        #expect(SettingsSection.credentials.linkTitle == "Accounts Settings…")
    }
}
