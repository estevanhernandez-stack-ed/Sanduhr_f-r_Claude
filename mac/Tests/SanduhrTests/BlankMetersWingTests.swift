import Foundation
import Testing
@testable import Sanduhr

/// 2026-10-11, a live dev build: two accounts listed as signed in, no key in the file store, and the
/// notch's meters wing stayed a blank black slab. The wing holds its width only while a fetch can
/// come, for a bounded time; with no key the meters read as signed out.
@Suite("The meters wing never stays blank")
@MainActor
struct BlankMetersWingTests {
    let now = Date(timeIntervalSince1970: 1_000_000)

    func layout(_ model: DeskModel, at time: Date) -> NotchWingsView.Layout {
        NotchWingsView.layout(model: model, now: time, wings: 36, showText: true, left: .time, right: .meters,
                              idle: .automatic, font: "Helvetica", notchHeight: 32)
    }

    @Test func listedSignedInButNoKeyIsNotPending() {
        let input = DeskUsage(keyPresent: false)
        #expect(!input.metersPending(waitingSince: nil, now: now))
        // Read as signed out: the sign-in line (reminders on) or nothing, never a held blank.
        #expect(input.settled.signInNeeded)
        let model = DeskModel()
        model.update(input, now: now)
        #expect(!model.metersPending(now: now))
        #expect(model.signInNeeded)
        // With the reminder on, the wing says so; off, it folds.
        var on = input.settled
        on.signInReminder = true
        #expect(DeskClaudeText.compact(on, now: now) == DeskClaudeText.compactSignIn)
        #expect(DeskClaudeText.line(on) != nil)
        model.claudeCompact = nil
        #expect(layout(model, at: now).right == 0)
    }

    @Test func aKeyWaitingForItsFirstFetchIsPending() {
        let input = DeskUsage(keyPresent: true)
        #expect(!input.settled.signInNeeded)
        let model = DeskModel()
        model.update(input, now: now)
        #expect(model.metersPending(now: now))
        #expect(layout(model, at: now).right >= 36)
        // Before the widget has said (launch), it waits too.
        #expect(DeskUsage().metersPending(waitingSince: now, now: now))
    }

    @Test func pendingTimesOut() {
        let model = DeskModel()
        model.update(DeskUsage(keyPresent: true), now: now)
        let later = now.addingTimeInterval(DeskUsage.firstFetchWait + 1)
        #expect(!model.metersPending(now: later))
        #expect(layout(model, at: later).right == 0)
        // A later update of the same wait keeps the first start: no endless renewal.
        model.update(DeskUsage(keyPresent: true), now: later)
        #expect(!model.metersPending(now: later))
        // Numbers arrive: no wait; a new account's wait starts afresh.
        model.update(DeskUsage(usage: DemoMode.usage(for: "Work", now: later), fetchedAt: later, keyPresent: true), now: later)
        #expect(!model.metersPending(now: later))
        model.update(DeskUsage(keyPresent: true), now: later)
        #expect(model.metersPending(now: later))
    }

    @Test func aSwitchStillHoldsAndNumbersStillShow() {
        // A veiled switch holds whatever the key says; old numbers with no key stay as they are.
        #expect(DeskUsage(veiled: true, keyPresent: false).metersPending(waitingSince: nil, now: now))
        let kept = DeskUsage(usage: DemoMode.usage(for: "Work", now: now), fetchedAt: now, keyPresent: false)
        #expect(!kept.settled.signInNeeded)
    }

    @Test func usageNotesSavedHistory() {
        #expect(SignedOut.savedHistoryLine == "Signed out on this Mac — showing saved history.")
    }
}
