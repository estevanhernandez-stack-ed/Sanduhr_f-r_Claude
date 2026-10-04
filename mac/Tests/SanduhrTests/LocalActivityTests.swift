import Foundation
import Testing
@testable import Sanduhr

@Suite("Live Claude Code activity")
struct LocalActivityTests {
    /// A home with one session that burned in the opus, sonnet and haiku tiers since `base`.
    static func home() -> TempHome {
        let h = TempHome()
        let b = CCLogs.base
        CCLogs.write(h, [
            CCLogs.event(CCLogs.iso(b.addingTimeInterval(-60)), "claude-opus-4-7", 400, 400),   // before
            CCLogs.event(CCLogs.iso(b.addingTimeInterval(60)), "claude-opus-4-7", 1000, 499),
            CCLogs.event(CCLogs.iso(b.addingTimeInterval(90)), "claude-sonnet-4-6", 10, 20),
            CCLogs.event(CCLogs.iso(b.addingTimeInterval(120)), "claude-haiku-4-5", 5, 5),
            CCLogs.event(CCLogs.iso(b.addingTimeInterval(150)), "gpt-4o", 7, 7),
        ])
        return h
    }

    static func choices(_ activity: ActivityChoice, folder: String?) -> AccountDataChoices {
        var c = AccountDataChoices()
        c.activity = activity
        c.folder = folder
        return c
    }

    @Test func notTrackedOpensNothing() {
        let h = Self.home()
        defer { h.cleanUp() }
        let spy = SpyCCFileSystem()
        let source = LocalBurnSource(fileSystem: spy)
        #expect(source.scan(Self.choices(.off, folder: h.at(".claude")), since: CCLogs.base) == nil)
        #expect(spy.calls.isEmpty)
    }

    @Test func noLinkedFolderOpensNothing() {
        let spy = SpyCCFileSystem()
        let source = LocalBurnSource(fileSystem: spy)
        #expect(source.scan(Self.choices(.live, folder: nil), since: CCLogs.base) == nil)
        #expect(source.scan(Self.choices(.record, folder: ""), since: CCLogs.base) == nil)
        #expect(spy.calls.isEmpty)
    }

    @Test func liveAndRecordReadTheLinkedFolder() {
        let h = Self.home()
        defer { h.cleanUp() }
        for activity in [ActivityChoice.live, .record] {
            let spy = SpyCCFileSystem()
            let burn = LocalBurnSource(fileSystem: spy)
                .scan(Self.choices(activity, folder: h.at(".claude")), since: CCLogs.base)
            #expect(burn?.byTier == ["seven_day_opus": 1499, "seven_day_sonnet": 30, "seven_day": 10])
            #expect(burn?.total == Int64(1553))   // 1,499 + 30 + 10, and 14 unmapped
            #expect(burn?.events == 4)
            #expect(burn?.tokens(for: .sevenDayOpus) == 1499)
            #expect(burn?.tokens(for: .fiveHour) == 0)
            #expect(spy.reads.allSatisfy { $0.path.hasPrefix(h.at(".claude/projects/")) })
        }
    }

    @Test func onlyTheLinkedFolderIsRead() {
        let h = Self.home()
        defer { h.cleanUp() }
        CCLogs.write(h, root: ".claude-other", [CCLogs.event(CCLogs.iso(CCLogs.base.addingTimeInterval(60)),
                                                              "claude-opus-4-7", 9, 9)])
        let spy = SpyCCFileSystem()
        let burn = LocalBurnSource(fileSystem: spy)
            .scan(Self.choices(.live, folder: h.at(".claude-other")), since: CCLogs.base)
        #expect(burn?.total == 18)
        #expect(spy.reads.allSatisfy { $0.path.hasPrefix(h.at(".claude-other/")) })
    }

    @Test func turningActivityOffStopsReading() {
        let h = Self.home()
        defer { h.cleanUp() }
        let spy = SpyCCFileSystem()
        let source = LocalBurnSource(fileSystem: spy)
        #expect(source.scan(Self.choices(.live, folder: h.at(".claude")), since: CCLogs.base) != nil)
        let before = spy.calls.count
        #expect(source.scan(Self.choices(.off, folder: h.at(".claude")), since: CCLogs.base) == nil)
        #expect(spy.calls.count == before)
    }

    /// The badge as the card shows it: "+1.5k" for 1,499 opus tokens.
    @Test func badgeText() {
        #expect("+" + TokenFormat.compact(1499) == "+1.5k")
    }

    @Test func stateYAMLCarriesCountsOnly() {
        let yaml = YAMLEmitter.emit(.object([("local_activity", DebugState.localActivityYAML(reading: true, events: 4))]))
        #expect(yaml == "local_activity:\n  reading: true\n  events: 4\n")
    }
}
