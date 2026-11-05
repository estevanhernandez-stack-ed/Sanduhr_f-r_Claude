import Foundation
import Testing
@testable import Sanduhr

/// A host that records what the service asked of it, in order.
@MainActor
private final class FakeHost: SetupHost {
    var calls: [String] = []
    var drawersOn = false
    var drawersEverSorted = false
    var drawerFiles = 0
    var addOnsInstalled = false
    var usageOn = false
    var hasAccount = true
    var offResult = SetupDrawersOff(off: true, line: nil)
    var sortResult: String?

    func turnOffDrawers() -> SetupDrawersOff {
        calls.append("drawersOff")
        if offResult.off { drawersOn = false }
        return offResult
    }
    func turnOnDrawers() { calls.append("drawersOn"); drawersOn = true }
    func sortNow() -> String? { calls.append("sort"); return sortResult }
    func setUsage(_ on: Bool) { calls.append("usage:\(on)"); usageOn = on }
    func reloadDesk() { calls.append("reload") }
}

@MainActor
@Suite("Setup service")
struct SetupServiceTests {
    private let setupStore = MemoryDefaults()
    private let deskStore = MemoryDefaults()
    private let host = FakeHost()
    private var failSave = FailFlag()

    private final class FailFlag { var on = false }

    private func service() -> SetupService {
        let flag = failSave
        let base = SetupStorage.defaults(setup: setupStore, desk: deskStore)
        var storage = base
        let host = self.host
        storage.save = { setup, deskRuns in
            host.calls.append("save")
            if flag.on { throw SetupStorage.SaveError() }
            try base.save(setup, deskRuns)
        }
        return SetupService(storage: storage, host: host)
    }

    private func choices(_ d: Bool, _ w: Bool, _ u: Bool) -> SetupChoices {
        SetupChoices(desktop: d, drawers: w, claudeUsage: u)
    }

    @Test func appliesInWindowsOrder() {
        host.drawersOn = true
        host.usageOn = false
        let result = service().apply(choices(true, false, true))
        #expect(host.calls == ["drawersOff", "save", "usage:true", "reload"])
        #expect(result.now == choices(true, false, true))
        #expect(result.reviewOpened == false)
        #expect(deskStore.bool(forKey: DeskController.enabledKey))
    }

    @Test func turningDrawersOnSavesThenUsageThenOnThenReloadThenSort() {
        host.sortResult = nil
        let result = service().apply(choices(false, true, true))
        #expect(host.calls == ["save", "usage:true", "drawersOn", "reload", "sort"])
        #expect(result.reviewOpened)
        #expect(result.lines == [SetupCopy.drawersFirstOnLine])
        #expect(SetupStore.load(from: setupStore) == SetupSettings(desktop: false, usage: ["claude": true]))
        #expect(deskStore.bool(forKey: DeskController.enabledKey))
    }

    @Test func refusedTurnOffKeepsDrawersWhileTheRestApplies() {
        host.drawersOn = true
        host.offResult = SetupDrawersOff(off: false, line: nil)
        host.usageOn = true
        let result = service().apply(choices(true, false, false))
        #expect(result.now == choices(true, true, false))
        #expect(host.drawersOn)
        #expect(host.calls == ["drawersOff", "save", "usage:false", "reload"])
        #expect(SetupStore.load(from: setupStore) == SetupSettings(desktop: true, usage: ["claude": false]))
        #expect(deskStore.bool(forKey: DeskController.enabledKey))
        #expect(!host.calls.contains("drawersOn"))
    }

    @Test func aFailedReturnSaysItsLineAndKeepsDrawers() {
        host.drawersOn = true
        host.offResult = SetupDrawersOff(off: false, line: "Nothing could be put back.")
        let result = service().apply(choices(false, false, true))
        #expect(result.lines == ["Nothing could be put back."])
        #expect(result.now.drawers)
    }

    @Test func aSaveFailureStopsBeforeUsage() {
        failSave.on = true
        let result = service().apply(choices(true, true, true))
        #expect(host.calls == ["save"])
        #expect(result.lines == [SetupCopy.saveFailed])
        #expect(!result.reviewOpened)
        #expect(SetupStore.load(from: setupStore) == nil)
    }

    @Test func aSaveFailureAfterATurnOffReloadsTheDesk() {
        failSave.on = true
        host.drawersOn = true
        let result = service().apply(choices(true, false, true))
        #expect(host.calls == ["drawersOff", "save", "reload"])
        #expect(result.lines == [SetupCopy.saveFailed])
    }

    @Test func reviewOnlyOnTheFirstTurnOn() {
        let first = service().apply(choices(true, true, false))
        #expect(first.reviewOpened)
        #expect(host.calls.filter { $0 == "sort" }.count == 1)
        // Drawers already on: applying again opens nothing.
        host.calls = []
        let again = service().apply(choices(true, true, false))
        #expect(!again.reviewOpened)
        #expect(!host.calls.contains("sort"))
    }

    @Test func noReviewWhenARunWasAcceptedBefore() {
        host.drawersEverSorted = true
        let result = service().apply(choices(true, true, false))
        #expect(!result.reviewOpened)
        #expect(!host.calls.contains("sort"))
        #expect(host.calls.contains("drawersOn"))
    }

    @Test func aReviewThatCouldNotOpenSaysWhyExceptNothingToSort() {
        host.sortResult = "Sanduhr can't see your Desktop."
        let denied = service().apply(choices(true, true, false))
        #expect(!denied.reviewOpened)
        #expect(denied.lines.last == "Sanduhr can't see your Desktop.")

        let quiet = FakeHost()
        quiet.sortResult = SetupCopy.nothingToSort
        let r = SetupService(storage: .defaults(setup: MemoryDefaults(), desk: MemoryDefaults()), host: quiet)
            .apply(choices(true, true, false))
        #expect(!r.reviewOpened)
        #expect(r.lines == [SetupCopy.drawersFirstOnLine])
    }

    @Test func aBlockedChoiceChangesNothing() {
        host.drawersOn = true
        let result = service().apply(choices(false, false, false))
        #expect(host.calls.isEmpty)
        #expect(result.lines == [SetupCopy.pickOne])
        #expect(result.now.drawers)
        #expect(SetupStore.load(from: setupStore) == nil)
    }

    @Test func previewSaysWhatUntickingChanges() {
        setupStore.set(SetupSettings(desktop: true, usage: ["claude": true]).encode(), forKey: SetupSettings.key)
        deskStore.set(true, forKey: DeskController.enabledKey)
        host.drawersOn = true
        host.drawerFiles = 3
        host.usageOn = true
        let svc = service()
        #expect(svc.current() == choices(true, true, true))
        #expect(svc.preview(choices(false, false, false)) == [SetupCopy.desktopOffLine, SetupCopy.drawersOffLine(3), SetupCopy.usageOffLine])
    }
}
