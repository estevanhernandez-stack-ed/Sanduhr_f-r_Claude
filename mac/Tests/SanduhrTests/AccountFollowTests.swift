import Foundation
import Testing
@testable import Sanduhr

/// Following the account in use: every rule of `AccountFollow.decide`, on made-up readings and a
/// fixed clock. Nothing here fetches or touches a credential.
@Suite("Following the account in use")
struct AccountFollowTests {
    let now = Date(timeIntervalSince1970: 1_800_000_000)

    func minutes(_ m: Double) -> Date { now.addingTimeInterval(m * 60) }

    /// Readings at the given minutes before `now` (negative offsets), oldest first.
    func readings(_ pairs: [(Double, Double)]) -> [SessionReading] {
        pairs.map { SessionReading(at: minutes($0.0), utilization: $0.1) }
    }

    /// Following on, Personal active, Work signed in: Work rose in its latest check, Personal's
    /// fetches over the last 15 minutes stayed flat. A clear signal unless a test changes it.
    func clearSignal() -> AccountFollow.State {
        var s = AccountFollow.State()
        s.enabled = true
        s.active = "Personal"
        s.signedIn = ["Personal", "Work"]
        s.readings["Work"] = readings([(-16, 10), (-1, 14)])
        s.readings["Personal"] = readings([(-20, 40), (-10, 40), (-5, 40), (0, 40)])
        return s
    }

    @Test func aClearSignalSwitches() {
        #expect(AccountFollow.decide(clearSignal(), now: now) == .switchTo("Work"))
    }

    @Test func offByDefaultAndOffNeverSwitches() {
        #expect(AccountFollow.State().enabled == false)
        var s = clearSignal()
        s.enabled = false
        #expect(AccountFollow.decide(s, now: now) == .stay(.off))
        #expect(AccountFollow.othersInUse(s, now: now).isEmpty)
    }

    @Test func bothInUseStaysAndMarksTheOther() {
        var s = clearSignal()
        s.readings["Personal"] = readings([(-20, 40), (-10, 40), (-5, 42)])
        #expect(AccountFollow.decide(s, now: now) == .stay(.bothInUse))
        #expect(AccountFollow.othersInUse(s, now: now) == ["Work"])
    }

    @Test func theActiveRiseCountsOnlyInsideTheWindow() {
        var s = clearSignal()
        // Personal rose 20 minutes ago, then stayed flat for the whole window.
        s.readings["Personal"] = readings([(-25, 30), (-20, 40), (-10, 40), (-5, 40)])
        #expect(AccountFollow.decide(s, now: now) == .switchTo("Work"))
        // A rise from the reading just before the window to the first inside it counts.
        s.readings["Personal"] = readings([(-17, 40), (-12, 41)])
        #expect(AccountFollow.decide(s, now: now) == .stay(.bothInUse))
    }

    @Test func atMostOneAutomaticSwitchPer30Minutes() {
        var s = clearSignal()
        s.lastAutoSwitch = minutes(-29)
        #expect(AccountFollow.decide(s, now: now) == .stay(.tooSoon))
        s.lastAutoSwitch = minutes(-30)
        #expect(AccountFollow.decide(s, now: now) == .switchTo("Work"))
    }

    @Test func aManualSwitchPausesForThreeHours() {
        var s = clearSignal()
        // Picked Personal by hand 2 hours ago and kept using it until 20 minutes ago.
        s.pause = AccountFollow.Pause(account: "Personal", since: minutes(-120))
        s.readings["Personal"] = readings([(-120, 5), (-60, 20), (-20, 30), (-5, 30), (0, 30)])
        #expect(AccountFollow.isPaused(s, now: now))
        #expect(AccountFollow.decide(s, now: now) == .stay(.paused))
        // Three hours after the switch the pause is over, however busy the account was.
        s.pause = AccountFollow.Pause(account: "Personal", since: minutes(-180))
        #expect(!AccountFollow.isPaused(s, now: now))
        #expect(AccountFollow.decide(s, now: now) == .switchTo("Work"))
    }

    @Test func thePauseEndsOnceThePickedAccountIsIdle30Minutes() {
        var s = clearSignal()
        s.pause = AccountFollow.Pause(account: "Personal", since: minutes(-60))
        // Last rise 31 minutes ago: idle long enough.
        s.readings["Personal"] = readings([(-60, 5), (-31, 9), (-20, 9), (-5, 9)])
        #expect(!AccountFollow.isPaused(s, now: now))
        #expect(AccountFollow.decide(s, now: now) == .switchTo("Work"))
        // Last rise 29 minutes ago: still paused.
        s.readings["Personal"] = readings([(-60, 5), (-29, 9), (-5, 9)])
        #expect(AccountFollow.isPaused(s, now: now))
        // Never used since the switch: idle counts from the switch itself.
        s.pause = AccountFollow.Pause(account: "Personal", since: minutes(-20))
        s.readings["Personal"] = readings([(-50, 5), (-40, 9), (-10, 9)])
        #expect(AccountFollow.isPaused(s, now: now))
        s.pause = AccountFollow.Pause(account: "Personal", since: minutes(-30))
        #expect(!AccountFollow.isPaused(s, now: now))
    }

    @Test func aResetIsNotUse() {
        var s = clearSignal()
        s.readings["Work"] = readings([(-16, 85), (-1, 2)])
        #expect(!AccountFollow.inUseInLatestCheck(s.readings["Work"]!, now: now))
        #expect(AccountFollow.decide(s, now: now) == .stay(.noSignal))
        // An unchanged session is not use either.
        s.readings["Work"] = readings([(-16, 20), (-1, 20)])
        #expect(AccountFollow.decide(s, now: now) == .stay(.noSignal))
        // The active account resetting is not use: Work still wins.
        s = clearSignal()
        s.readings["Personal"] = readings([(-12, 90), (-6, 1)])
        #expect(AccountFollow.decide(s, now: now) == .switchTo("Work"))
        // A reset then a climb is use.
        s.readings["Personal"] = readings([(-12, 90), (-6, 1), (-1, 3)])
        #expect(AccountFollow.decide(s, now: now) == .stay(.bothInUse))
    }

    @Test func oneCheckIsOnlyABaseline() {
        var s = clearSignal()
        s.readings["Work"] = readings([(-1, 14)])
        #expect(AccountFollow.decide(s, now: now) == .stay(.noSignal))
    }

    @Test func aStaleCheckIsNoSignal() {
        var s = clearSignal()
        s.readings["Work"] = readings([(-40, 10), (-21, 14)])
        #expect(AccountFollow.decide(s, now: now) == .stay(.noSignal))
        s.readings["Work"] = readings([(-35, 10), (-20, 14)])
        #expect(AccountFollow.decide(s, now: now) == .switchTo("Work"))
    }

    @Test func neverWhileSignedOutSwitchingOrWithOneSignedInAccount() {
        var s = clearSignal()
        s.activeSignedOut = true
        #expect(AccountFollow.decide(s, now: now) == .stay(.notReady))
        s = clearSignal()
        s.switching = true
        #expect(AccountFollow.decide(s, now: now) == .stay(.notReady))
        s = clearSignal()
        s.signedIn = ["Personal"]
        #expect(AccountFollow.decide(s, now: now) == .stay(.notReady))
        s = clearSignal()
        s.active = nil
        #expect(AccountFollow.decide(s, now: now) == .stay(.notReady))
    }

    @Test func aSignedOutAccountIsNeverACandidate() {
        var s = clearSignal()
        s.signedIn = ["Personal", "Home"]
        #expect(AccountFollow.decide(s, now: now) == .stay(.noSignal))
        #expect(AccountFollow.othersInUse(s, now: now).isEmpty)
    }

    @Test func twoOthersInUseIsNotAClearSignal() {
        var s = clearSignal()
        s.signedIn = ["Personal", "Work", "Team"]
        s.readings["Team"] = readings([(-16, 1), (-2, 6)])
        #expect(AccountFollow.decide(s, now: now) == .stay(.unclear))
        #expect(AccountFollow.othersInUse(s, now: now) == ["Work", "Team"])
    }

    @Test func theActiveAccountIsNeverMarkedInUse() {
        var s = clearSignal()
        s.readings["Personal"] = readings([(-16, 1), (-1, 6)])
        #expect(AccountFollow.othersInUse(s, now: now) == ["Work"])
    }

    @Test func recordKeepsOrderAndDropsOldReadings() {
        var s = AccountFollow.State()
        s.record("Work", utilization: 1, at: minutes(-300))
        s.record("Work", utilization: 2, at: minutes(-10))
        s.record("Work", utilization: 3, at: now)
        #expect(s.readings["Work"]?.map(\.utilization) == [2, 3])
    }

    @Test func renameAndForgetCarryTheReadingsAndThePause() {
        var s = clearSignal()
        s.pause = AccountFollow.Pause(account: "Work", since: now)
        s.rename("Work", to: "Office")
        #expect(s.readings["Work"] == nil)
        #expect(s.readings["Office"]?.count == 2)
        #expect(s.pause?.account == "Office")
        s.forget("Office")
        #expect(s.readings["Office"] == nil)
        #expect(s.pause == nil)
    }

    @Test func theTimingsAreTheSpecs() {
        #expect(AccountFollow.checkInterval == 15 * 60)
        #expect(AccountFollow.switchSpacing == 30 * 60)
        #expect(AccountFollow.pauseLength == 3 * 60 * 60)
        #expect(AccountFollow.pauseIdle == 30 * 60)
        #expect(AccountFollow.noteLength == 60)
    }
}
