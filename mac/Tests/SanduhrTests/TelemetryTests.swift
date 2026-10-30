import Foundation
import Testing
@testable import Sanduhr

/// The system piece (2.38.0 item 1), ported from Windows' TelemetryTests (the CPU, memory and disk
/// math and the rows), plus the Mac sampler's lifecycle and the `telemetry` layout word.
@Suite("Desk system piece")
struct TelemetryTests {
    // MARK: CPU

    @Test func cpuIsTheBusyShareOfTheDeltasKernelIncludesIdle() {
        let prev = CpuTimes(idle: 1000, kernel: 2000, user: 500)
        // dIdle 600, dKernel 800, dUser 200: busy = (800-600)+200 = 400 of 1000.
        let next = CpuTimes(idle: 1600, kernel: 2800, user: 700)
        #expect(abs(Telemetry.cpuPercent(prev, next) - 40) < 1e-6)
    }

    @Test func cpuFullyIdleIsZeroAndFullyBusyIsHundred() {
        #expect(Telemetry.cpuPercent(CpuTimes(idle: 0, kernel: 0, user: 0), CpuTimes(idle: 100, kernel: 100, user: 0)) == 0)
        #expect(abs(Telemetry.cpuPercent(CpuTimes(idle: 0, kernel: 0, user: 0), CpuTimes(idle: 0, kernel: 50, user: 50)) - 100) < 1e-6)
    }

    @Test func cpuZeroOrNegativeTotalIsZero() {
        let t = CpuTimes(idle: 5, kernel: 10, user: 5)
        #expect(Telemetry.cpuPercent(t, t) == 0)
        #expect(Telemetry.cpuPercent(CpuTimes(idle: 100, kernel: 200, user: 100), CpuTimes(idle: 100, kernel: 150, user: 100)) == 0)
    }

    @Test func cpuIdleAheadOfKernelClampsToZero() {
        #expect(Telemetry.cpuPercent(CpuTimes(idle: 0, kernel: 0, user: 0), CpuTimes(idle: 500, kernel: 100, user: 0)) == 0)
    }

    @Test func cpuHandlesLargeTicks() {
        let big: UInt64 = 10_000_000_000_000
        let p = Telemetry.cpuPercent(CpuTimes(idle: big, kernel: big * 2, user: big),
                                     CpuTimes(idle: big + 50, kernel: big * 2 + 100, user: big))
        #expect(abs(p - 50) < 1e-6)
    }

    // MARK: Memory and disk

    @Test(arguments: [(UInt32(0), 0.0), (42, 42), (100, 100), (250, 100)])
    func memoryLoadIsClamped(_ c: (UInt32, Double)) {
        #expect(Telemetry.memoryPercent(c.0) == c.1)
    }

    @Test func memoryIsUsedAgainstPhysical() {
        #expect(Telemetry.memoryPercent(used: 8, physical: 16) == 50)
        #expect(Telemetry.memoryPercent(used: 20, physical: 16) == 100)
        #expect(Telemetry.memoryPercent(used: 5, physical: 0) == 0)
    }

    @Test func diskIsTheUsedShare() {
        #expect(abs(Telemetry.diskUsedPercent(total: 1000, free: 250) - 75) < 1e-6)
        #expect(Telemetry.diskUsedPercent(total: 0, free: 0) == 0)
        #expect(Telemetry.diskUsedPercent(total: 100, free: 500) == 0)
    }

    // MARK: Rows

    @Test func rowsAreCpuRamDiskInOrderRoundedHalfAwayFromZero() {
        let rows = Telemetry.rows(TelemetrySample(cpu: 12.5, ram: 49.4, disk: 100, at: .distantPast))
        #expect(rows.map(\.label) == ["CPU", "RAM", "Disk"])
        #expect(rows.map(\.key) == ["CPU", "RAM", "Disk"])
        #expect(rows[0].percent == 13)
        #expect(abs(rows[0].fill - 0.13) < 1e-9)
        #expect(rows[1].percent == 49)
        #expect(rows[2].percent == 100)
        #expect(rows[2].fill == 1)
    }

    @Test func rowsClampAndTreatNaNAsZero() {
        let rows = Telemetry.rows(TelemetrySample(cpu: .nan, ram: 140, disk: -5, at: .distantPast))
        #expect(rows.map(\.percent) == [0, 100, 0])
    }

    @Test func theIntervalIsFiveSeconds() {
        #expect(Telemetry.interval == 5)
    }

    @Test func gaugesCarryTheRowsWithDistinctIdsAndNoPaceOrWarning() {
        let gauges = DeskMeterStyleModel.telemetryGauges(Telemetry.demoSample())
        #expect(gauges.map(\.label) == ["CPU", "RAM", "Disk"])
        #expect(Set(gauges.map(\.id)).count == 3)
        #expect(gauges.allSatisfy { $0.pace == nil && !$0.warning })
        #expect(gauges.map(\.percent) == Telemetry.rows(Telemetry.demoSample()).map(\.percent))
    }

    // MARK: The sampler

    /// A scheduler that records what the sampler asked of it and fires on demand.
    private final class FakeClock {
        var scheduled = 0
        var cancelled = 0
        var intervals: [TimeInterval] = []
        var fire: (() -> Void)?
        var schedule: TelemetrySampler.Schedule {
            { [self] interval, fire in
                scheduled += 1
                intervals.append(interval)
                self.fire = fire
                return { [self] in cancelled += 1; self.fire = nil }
            }
        }
    }

    private final class FakeCounters {
        var cpu: [CpuTimes] = []
        var reads = 0
        var source: TelemetrySampler.Source {
            .init(cpu: { [self] in reads += 1; return cpu.isEmpty ? nil : cpu.removeFirst() },
                  memory: { (used: 4, physical: 16) },
                  disk: { (total: 1000, free: 400) })
        }
    }

    @Test func noTimerExistsUntilStartedAndNoneAfterStop() {
        let clock = FakeClock()
        let sampler = TelemetrySampler(source: FakeCounters().source, schedule: clock.schedule)
        #expect(!sampler.isRunning)
        #expect(clock.scheduled == 0)

        sampler.start()
        #expect(sampler.isRunning)
        #expect(clock.scheduled == 1)
        #expect(clock.intervals == [5])

        sampler.stop()
        #expect(!sampler.isRunning)
        #expect(clock.cancelled == 1)
        #expect(clock.fire == nil)
        #expect(sampler.latest == nil)
    }

    @Test func startingTwiceKeepsOneTimerAndStoppingTwiceCancelsOnce() {
        let clock = FakeClock()
        let sampler = TelemetrySampler(source: FakeCounters().source, schedule: clock.schedule)
        sampler.start()
        sampler.start()
        #expect(clock.scheduled == 1)
        sampler.stop()
        sampler.stop()
        #expect(clock.cancelled == 1)
    }

    @Test func aStoppedSamplerReadsNothingMore() {
        let clock = FakeClock()
        let counters = FakeCounters()
        counters.cpu = [CpuTimes(idle: 0, kernel: 0, user: 0)]
        let sampler = TelemetrySampler(source: counters.source, schedule: clock.schedule)
        sampler.start()
        let afterStart = counters.reads
        sampler.stop()
        #expect(clock.fire == nil)   // nothing left to tick
        #expect(counters.reads == afterStart)
    }

    @Test func theFirstSampleHasNoCpuAndTheNextOnesDo() {
        let clock = FakeClock()
        let counters = FakeCounters()
        counters.cpu = [CpuTimes(idle: 1000, kernel: 2000, user: 500), CpuTimes(idle: 1600, kernel: 2800, user: 700)]
        let sampler = TelemetrySampler(source: counters.source, schedule: clock.schedule,
                                       now: { Date(timeIntervalSince1970: 100) })
        var seen: [TelemetrySample?] = []
        sampler.onSample = { seen.append($0) }
        sampler.start()
        #expect(sampler.latest?.cpu == 0)
        #expect(sampler.latest?.ram == 25)
        #expect(sampler.latest?.disk == 60)

        clock.fire?()
        #expect(abs((sampler.latest?.cpu ?? -1) - 40) < 1e-6)
        #expect(sampler.latest?.at == Date(timeIntervalSince1970: 100))

        sampler.stop()
        #expect(seen.count == 3)
        #expect(seen.last! == nil)
    }

    @Test func aFailedCpuReadKeepsTheLastFigure() {
        let clock = FakeClock()
        let counters = FakeCounters()
        counters.cpu = [CpuTimes(idle: 0, kernel: 0, user: 0), CpuTimes(idle: 0, kernel: 50, user: 50)]
        let sampler = TelemetrySampler(source: counters.source, schedule: clock.schedule)
        sampler.start()
        clock.fire?()
        #expect(sampler.latest?.cpu == 100)
        clock.fire?()   // the counters have run out: read fails
        #expect(sampler.latest?.cpu == 100)
    }

    @Test func theLiveReadersAnswerOnThisMachine() throws {
        let cpu = try #require(TelemetryReader.cpuTimes())
        #expect(cpu.kernel >= cpu.idle)
        let mem = try #require(TelemetryReader.memory())
        #expect(mem.used > 0 && mem.physical > 0)
        let disk = try #require(TelemetryReader.disk())
        #expect(disk.total > 0 && disk.free <= disk.total)
    }

    // MARK: The model starts it only while the Desk shows the piece

    private func model(_ clock: FakeClock) -> DeskModel {
        let m = DeskModel()
        m.telemetrySampler = TelemetrySampler(source: FakeCounters().source, schedule: clock.schedule)
        return m
    }

    @Test func theSamplerRunsOnlyWhileTheDeskShowsThePlacedPiece() {
        let clock = FakeClock()
        let m = model(clock)
        let placed = "message:tl telemetry:tr"

        m.applyTelemetry(running: true, layout: placed)
        #expect(m.telemetrySampler.isRunning)
        #expect(m.telemetry != nil)
        #expect(clock.scheduled == 1)

        // The same layout again (most defaults changes) does nothing.
        m.applyTelemetry(running: true, layout: placed)
        #expect(clock.scheduled == 1)

        // The Desk hides: the timer is cancelled and the reading is gone.
        m.applyTelemetry(running: false, layout: placed)
        #expect(!m.telemetrySampler.isRunning)
        #expect(clock.cancelled == 1)
        #expect(clock.fire == nil)
        #expect(m.telemetry == nil)

        // Shown again: a fresh timer.
        m.applyTelemetry(running: true, layout: placed)
        #expect(clock.scheduled == 2)

        // The piece comes off the layout while the Desk runs: stopped too.
        m.applyTelemetry(running: true, layout: DeskLayout.standard)
        #expect(!m.telemetrySampler.isRunning)
        #expect(clock.cancelled == 2)
    }

    @Test func aDeskWithoutThePieceNeverStartsASampler() {
        let clock = FakeClock()
        let m = model(clock)
        m.applyTelemetry(running: true, layout: DeskLayout.standard)
        #expect(clock.scheduled == 0)
        #expect(m.telemetry == nil)
    }

    @Test func demoModeShowsMadeUpReadingsAndNoSampler() {
        let clock = FakeClock()
        let m = model(clock)
        m.applyTelemetry(running: true, layout: "telemetry:tr")
        m.setDemo(true)
        #expect(!m.telemetrySampler.isRunning)
        #expect(m.telemetry == Telemetry.demoSample(now: m.telemetry!.at))
        m.setDemo(false)
        #expect(m.telemetry == nil || m.telemetrySampler.isRunning)
    }

    // MARK: The layout word

    @Test func theWordIsKnownAfterNowPlayingAndTakesTheTopRight() {
        #expect(DeskArrangement.known == ["message", "clock", "claude", "meters", "nowPlaying", "telemetry",
                                          "watchers", "meetings", "companion"])
        #expect(DeskArrangement.defaultAnchor("telemetry") == .tr)
        #expect(DeskLayout.widgets.first { $0.key == "telemetry" }?.name == "System")
        var a = DeskArrangement(DeskLayout.standard)
        a.show("telemetry")
        #expect(a.placement("telemetry")?.anchor == .tr)
        #expect(DeskArrangement("telemetry:zz").placement("telemetry")?.anchor == .tr)   // unknown anchor: the default
    }

    @Test func theWordTakesAScaleATiltAndAStyle() {
        let a = DeskArrangement("message:tl telemetry:mr:1.2:18:slim")
        let p = a.placement("telemetry")
        #expect(p?.anchor == .mr)
        #expect(p?.scale == 1.2)
        #expect(p?.tilt == 18)
        #expect(p?.style == .slim)
        #expect(a.string == "message:tl telemetry:mr:1.2:18:slim")
        #expect(DeskArrangement("telemetry:tr:1:0:ticks").string == "telemetry:tr:1:0:ticks")
        #expect(DeskArrangement("telemetry:tr").string == "telemetry:tr")
    }

    @Test func theStylePickerShowsForTheSystemPieceWhenPlaced() {
        #expect(DeskMeterStyleSettings.shows("telemetry", in: "telemetry:tr"))
        #expect(!DeskMeterStyleSettings.shows("telemetry", in: DeskLayout.standard))
        #expect(DeskMeterStyles.refusal(widget: "telemetry", word: "ring") == nil)
        #expect(DeskMeterStyles.refusal(widget: "telemetry", word: "donut") != nil)
    }

    @Test func existingLayoutsStillRoundTripByteForByte() {
        let layouts = [
            DeskLayout.standard,
            "message:tl clock:bl claude:bl meetings:bl",
            "clock:bl:1:-18 claude:bl:1:-18 meters:tr:1.2",
            "message:tc:1.4 clock:bl:0.8:6 claude:br:1:0:ring meters:ml:1.1:0:ticks nowPlaying:mr watchers:bc meetings:bl companion:br",
            "meters:tr:1:0:slim claude:tl:1:0:ring",
        ]
        for layout in layouts { #expect(DeskArrangement(layout).string == layout, "\(layout)") }
    }

    @Test func aLayoutWithoutTheWordNeverGainsIt() {
        #expect(!DeskArrangement(DeskLayout.standard).string.contains("telemetry"))
        #expect(DeskArrangement(DeskLayout.standard).placement("telemetry") == nil)
        #expect(DeskArrangement(DeskLayout.standard).hidden.contains("telemetry"))
    }

    @Test func aDesignStillCannotCarryTheSystemPieceYet() {
        let json = #"{"layout":{"telemetry":{"anchor":"tr"}}}"#
        let v = DeskDesign.validate(TJ.parse(json), context: DeskDesignContext(installedFonts: []))
        #expect(v.status.word == "rejected")
    }
}
