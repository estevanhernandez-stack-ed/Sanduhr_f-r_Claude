import Testing
import Foundation
@testable import Sanduhr

@Suite("Lock screen line writer (#137)")
struct LockLineWriterTests {
    private let now = Date(timeIntervalSince1970: 1_800_000_000)

    private func usage(_ pairs: [(Tier, Double, String?)]) -> UsageResponse {
        var r = UsageResponse()
        for (t, u, reset) in pairs { r.tiers[t] = TierUsage(utilization: u, resetsAt: reset) }
        return r
    }

    private func inputs(_ u: UsageResponse?, updated: Date?, normal: Bool = true) -> LockLine.Inputs {
        LockLineWriter.inputs(usage: u, lastUpdated: updated, statusNormal: normal, now: now,
                              prefix: "", show: LockLine.Slots(), waitingCount: 0)
    }

    @Test func staleRule() {
        let u = usage([(.fiveHour, 29, nil)])
        #expect(inputs(u, updated: nil).usageStale)
        #expect(!inputs(u, updated: now.addingTimeInterval(-19 * 60)).usageStale)
        #expect(inputs(u, updated: now.addingTimeInterval(-21 * 60)).usageStale)
        #expect(inputs(u, updated: now, normal: false).usageStale)
    }

    @Test func statusNormalIsIdleAndRefreshingOnly() {
        #expect(LockLineWriter.isNormal(.idle))
        #expect(LockLineWriter.isNormal(.refreshing))
        #expect(!LockLineWriter.isNormal(.signedOut))
        #expect(!LockLineWriter.isNormal(.error("x", isAuth: false)))
    }

    @Test func highestTierWinsWithItsReset() {
        let u = usage([(.fiveHour, 29, "2026-10-25T18:00:00Z"), (.sevenDay, 64.9, "2026-10-28T09:00:00Z"),
                       (.sevenDayOpus, 12, "2026-10-27T09:00:00Z")])
        let i = inputs(u, updated: now)
        #expect(i.usagePercent == 64)
        #expect(i.resetsAt == ISO8601.parse("2026-10-28T09:00:00Z"))
        #expect(inputs(nil, updated: now).usagePercent == nil)
    }

    @Test func passesPrefixSlotsAndWaitingThrough() {
        let i = LockLineWriter.inputs(usage: nil, lastUpdated: now, statusNormal: true, now: now, prefix: "Este",
                                      show: LockLine.Slots(usage: false, reset: true, waiting: false), waitingCount: 2)
        #expect(i.prefix == "Este" && i.waitingCount == 2)
        #expect(i.show == LockLine.Slots(usage: false, reset: true, waiting: false))
    }

    // MARK: Writing

    private final class Disk {
        var files: [URL: String] = [:]
        var writes = 0
    }

    private func writer(_ disk: Disk) -> LockLineWriter {
        LockLineWriter(target: URL(fileURLWithPath: "/tmp/lock-line-test.txt"),
                       writeFile: { text, url in disk.files[url] = text; disk.writes += 1 },
                       readFile: { disk.files[$0] })
    }

    @Test func writesOnlyOnChange() {
        let disk = Disk(); let w = writer(disk)
        w.write("Claude 29%"); w.write("Claude 29%")
        #expect(disk.writes == 1)
        w.write("Claude 30%")
        #expect(disk.writes == 2)
    }

    @Test func offWritesEmptyOnce() {
        let disk = Disk(); let w = writer(disk)
        w.write("Claude 29%")
        w.clear(); w.clear()
        #expect(disk.writes == 2)
        #expect(disk.files.values.first == "")
    }

    @Test func offWithNoFileWritesNothing() {
        let disk = Disk(); let w = writer(disk)
        w.clear()
        #expect(disk.writes == 0)
    }

    @Test func demoWritesNothingAndOffClears() {
        #expect(LockLineWriter.plan(on: true, demo: true) == .nothing)
        #expect(LockLineWriter.plan(on: false, demo: true) == .nothing)
        #expect(LockLineWriter.plan(on: true, demo: false) == .compose)
        #expect(LockLineWriter.plan(on: false, demo: false) == .clear)
    }

    @Test func resetGateWritesNothing() {
        let disk = Disk(); let w = writer(disk)
        ResetGate.$testOverride.withValue(true) { w.write("Claude 29%") }
        #expect(disk.writes == 0)
    }
}
