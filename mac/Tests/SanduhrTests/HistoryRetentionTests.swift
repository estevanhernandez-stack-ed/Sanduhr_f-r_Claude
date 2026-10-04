import Foundation
import Testing
@testable import Sanduhr

/// Item 43: 30 days of meter history per account, Windows' point cap, old files unchanged, the
/// sparklines' recent window, and the per-account Off. Temp folders and in-memory defaults only.
@Suite("Thirty days of meter history")
struct HistoryRetentionTests {
    static let now = Date(timeIntervalSince1970: 1_790_000_000)
    static let iso = ISO8601DateFormatter()

    /// `count` points five minutes apart, the last one `endingAgo` seconds before `now`.
    static func series(_ count: Int, endingAgo: TimeInterval = 300, value: Double = 10) -> [HistoryStore.Point] {
        (0..<count).map { i in
            let age = endingAgo + Double(count - 1 - i) * 300
            return HistoryStore.Point(t: iso.string(from: now.addingTimeInterval(-age)), v: value)
        }
    }

    static func write(_ json: String, _ name: String, in h: TempHistory) {
        try? Data(json.utf8).write(to: h.files.dir.appendingPathComponent(name))
    }

    @Test func keepsThirtyDaysNotTwentyFourPoints() {
        let h = TempHistory()
        defer { h.cleanUp() }
        h.files.save(["five_hour": Self.series(100)], account: "Work")
        let s = h.files.append(.fiveHour, utilization: 55, account: "Work", now: Self.now)
        #expect(s.count == 101)
        #expect(s.last?.v == 55)
    }

    @Test func trimsByAge() {
        let h = TempHistory()
        defer { h.cleanUp() }
        let day: TimeInterval = 24 * 60 * 60
        let old = HistoryStore.Point(t: Self.iso.string(from: Self.now.addingTimeInterval(-31 * day)), v: 1)
        let edge = HistoryStore.Point(t: Self.iso.string(from: Self.now.addingTimeInterval(-29 * day)), v: 2)
        h.files.save(["seven_day": [old, old, edge]], account: "Work")
        let s = h.files.append(.sevenDay, utilization: 3, account: "Work", now: Self.now)
        #expect(s.map(\.v) == [2, 3])
    }

    @Test func trimsByCountAtTheWindowsCap() {
        let h = TempHistory()
        defer { h.cleanUp() }
        // Readings closer than five minutes apart (manual refreshes) can reach the cap within 30 days.
        var full = Self.series(HistoryStore.maxPoints, endingAgo: 60)
        full[0] = HistoryStore.Point(t: full[0].t, v: 99)
        h.files.save(["five_hour": full], account: "Work")
        let s = h.files.append(.fiveHour, utilization: 7, account: "Work", now: Self.now)
        #expect(HistoryStore.maxPoints == 8640)
        #expect(s.count == 8640)
        #expect(s.first?.v == 10)          // the oldest point went
        #expect(s.last?.v == 7)
    }

    @Test func anOldTwentyFourPointFileLoadsUnchanged() throws {
        let h = TempHistory()
        defer { h.cleanUp() }
        let points = Self.series(24)
        let json = String(decoding: try JSONEncoder().encode(["five_hour": points]), as: UTF8.self)
        Self.write(json, "history.Personal.json", in: h)
        #expect(h.files.load("Personal")["five_hour"] == points)
        let s = h.files.append(.fiveHour, utilization: 1, account: "Personal", now: Self.now)
        #expect(Array(s.prefix(24)) == points)
        #expect(s.count == 25)
    }

    @Test func aWindowsFileLoadsAndKeepsItsShape() throws {
        let h = TempHistory()
        defer { h.cleanUp() }
        let recent = Self.now.addingTimeInterval(-600)
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        let t = f.string(from: recent).replacingOccurrences(of: "Z", with: "000+00:00")
        let json = #"{"five_hour":[{"t":"\#(t)","v":42,"resets_at":"2026-10-04T18:00:00+00:00"}]}"#
        Self.write(json, "history.Work.json", in: h)
        #expect(HistoryStore.date(t) != nil)
        let loaded = h.files.load("Work")["five_hour"]
        #expect(loaded?.first?.v == 42)
        #expect(loaded?.first?.resetsAt == "2026-10-04T18:00:00+00:00")
        h.files.append(.fiveHour, utilization: 43, account: "Work", now: Self.now)
        let text = try String(contentsOf: h.files.url(for: "Work"), encoding: .utf8)
        #expect(text.contains(#""v":42"#))                         // whole numbers stay whole
        #expect(text.contains(#""resets_at":"2026-10-04T18:00:00+00:00""#))
        #expect(!text.contains("resets_at\":null"))               // the Mac's points add no key
        #expect(h.files.load("Work")["five_hour"]?.map(\.v) == [42, 43])
    }

    @Test func anUnreadableTimestampNeverDropsData() {
        let bad = HistoryStore.Point(t: "yesterday", v: 1)
        let s = HistoryStore.trim([bad] + Self.series(3), cutoff: Self.now)
        #expect(s.count == 4)
    }

    @Test func oneFetchIsOneWrite() {
        let h = TempHistory()
        defer { h.cleanUp() }
        let all = h.files.append([(.fiveHour, 10), (.sevenDay, 20), (.sevenDayOpus, 30)], account: "Work", now: Self.now)
        #expect(all.keys.sorted() == ["five_hour", "seven_day", "seven_day_opus"])
        #expect(h.files.load("Work") == all)
    }

    @Test func sparklinesDrawTheSameRecentWindow() {
        let h: HistoryStore.History = ["five_hour": Self.series(8640), "seven_day": Self.series(5)]
        let drawn = HistoryStore.sparklines(h)
        #expect(HistoryStore.sparklinePoints == 24)
        #expect(drawn["five_hour"] == Array(Self.series(8640).suffix(24)))
        #expect(drawn["seven_day"]?.count == 5)
    }

    /// The in-memory update after a fetch draws what reloading the file would.
    @Test func theSparklinesInMemoryMatchTheFile() {
        let h = TempHistory()
        defer { h.cleanUp() }
        h.files.save(["five_hour": Self.series(30)], account: "Work")
        let before = HistoryStore.sparklines(h.files.load("Work"))
        let readings: [(Tier, Double)] = [(.fiveHour, 61), (.sevenDay, 12)]
        let after = h.files.append(readings, account: "Work", now: Self.now)
        #expect(HistoryStore.sparklines(before, adding: readings, at: Self.now) == HistoryStore.sparklines(after))
    }

    /// A full file (four tiers at the cap, about 34,560 points) read, appended and written once,
    /// as a fetch does. Printed for the record; the bound only catches a gross regression.
    @Test func appendingToAFullFileStaysQuick() {
        let h = TempHistory()
        defer { h.cleanUp() }
        let full = Self.series(HistoryStore.maxPoints, endingAgo: 60)
        let tiers: [Tier] = [.fiveHour, .sevenDay, .sevenDaySonnet, .sevenDayOpus]
        var file: HistoryStore.History = [:]
        for t in tiers { file[t.rawValue] = full }
        h.files.save(file, account: "Work")
        let readings: [(Tier, Double)] = tiers.map { ($0, 50) }
        let clock = ContinuousClock()
        var runs: [Duration] = []
        for _ in 0..<3 {
            runs.append(clock.measure { h.files.append(readings, account: "Work", now: Self.now) })
        }
        let best = runs.min() ?? .zero
        let size = (try? FileManager.default.attributesOfItem(atPath: h.files.url(for: "Work").path)[.size] as? Int) ?? 0
        print("history append+save, full file (\(tiers.count) x \(HistoryStore.maxPoints) points, \(size / 1024) KB): best \(best), runs \(runs)")
        #expect(h.files.load("Work")["five_hour"]?.count == HistoryStore.maxPoints)
        #expect(best < .milliseconds(500))
    }
}

@Suite("Meter history per account")
struct MeterHistoryTests {
    @Test func thirtyDaysUnlessSwitchedOff() {
        let d = MemoryDefaults()
        #expect(MeterHistory.isOn("Work", in: d))
        #expect(MeterHistory.days("Work", in: d) == 30)
        MeterHistory.set(false, for: "Work", in: d)
        #expect(!MeterHistory.isOn("Work", in: d))
        #expect(MeterHistory.days("Work", in: d) == 0)
        #expect(MeterHistory.isOn("Home", in: d))
        MeterHistory.set(true, for: "Work", in: d)
        #expect(d.object(forKey: MeterHistory.offKey) == nil)
    }

    @Test func theLegacyLaunchIsPersonals() {
        let d = MemoryDefaults()
        MeterHistory.set(false, for: "Personal", in: d)
        #expect(!MeterHistory.isOn(nil, in: d))
    }

    @Test func offStopsRecordingAndKeepsWhatWasThere() {
        let h = TempHistory()
        defer { h.cleanUp() }
        let d = MemoryDefaults()
        let now = HistoryRetentionTests.now
        #expect(HistoryStore.record([(.fiveHour, 10)], account: "Work", defaults: d, files: h.files, now: now))
        MeterHistory.set(false, for: "Work", in: d)
        #expect(!HistoryStore.record([(.fiveHour, 20)], account: "Work", defaults: d, files: h.files, now: now))
        HistoryStore.io.sync {}
        #expect(h.files.load("Work")["five_hour"]?.map(\.v) == [10])
        // Erasing (the confirmation's Erase History) deletes the file; nothing comes back while off.
        h.files.delete("Work")
        HistoryStore.record([(.fiveHour, 30)], account: "Work", defaults: d, files: h.files, now: now)
        HistoryStore.io.sync {}
        #expect(h.names.isEmpty)
        // Other accounts keep recording.
        #expect(HistoryStore.record([(.fiveHour, 40)], account: "Home", defaults: d, files: h.files, now: now))
        HistoryStore.io.sync {}
        #expect(h.names == ["history.Home.json"])
    }

    @Test func queuedWritesLandBeforeARenameOrRemove() {
        let h = TempHistory()
        defer { h.cleanUp() }
        let d = MemoryDefaults()
        HistoryStore.record([(.fiveHour, 10)], account: "Work", defaults: d, files: h.files)
        h.files.rename("Work", to: "Office")
        #expect(h.names == ["history.Office.json"])
        HistoryStore.record([(.fiveHour, 20)], account: "Office", defaults: d, files: h.files)
        h.files.delete("Office")
        #expect(h.names.isEmpty)
    }

    @Test func theChoiceFollowsRenameAndRemove() throws {
        let h = TempHistory()
        defer { h.cleanUp() }
        let f = RegistryFixture(keychain: ["sessionKey:Work": "sk-w", "sessionKey:Home": "sk-h"], labels: ["Home", "Work"])
        let registry = AccountRegistry(backend: f.keychain, stores: f.registry.stores, defaults: f.defaults,
                                       history: h.files)
        MeterHistory.set(false, for: "Work", in: f.defaults)
        try registry.rename("Work", to: "Office")
        #expect(MeterHistory.offLabels(in: f.defaults) == ["Office"])
        #expect(MeterHistory.isOn("Work", in: f.defaults))
        registry.remove("Office")
        #expect(MeterHistory.offLabels(in: f.defaults).isEmpty)
        // Sign Out keeps the choice, like the history.
        MeterHistory.set(false, for: "Home", in: f.defaults)
        registry.signOut("Home")
        #expect(!MeterHistory.isOn("Home", in: f.defaults))
    }
}
