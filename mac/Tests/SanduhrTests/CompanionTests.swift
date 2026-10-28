import Foundation
import Testing
@testable import Sanduhr

/// The companion's growth curve and memory, Sanduhr Time's day files, the sprite.json model and sheet
/// cutting (port of Windows' CompanionTests.cs). Skipped from the Windows set: the `Paths` case (the
/// Mac has no Paths type; the state file location is pinned below through `CompanionGrowthState.file`)
/// and `CompanionSettingsTests` (already ported with the settings, see WidgetsModelTests). Never reads
/// `~/.sanduhr`: every folder is a temp one.
enum CompanionTestKit {
    static func tempDir() -> URL {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("sanduhr-companion-" + UUID().uuidString.prefix(10))
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        return dir
    }

    static func day(_ date: String, minutes: Double = 30) -> CodingDay {
        CodingDay(date: CCLocalDay.parse(date)!, active: minutes * 60)
    }

    static func fixture(_ group: String, _ name: String) -> CompanionSprite {
        let data = try! Data(contentsOf: URL(fileURLWithPath: WidgetPackFixtures.root + "/\(group)/\(name)/sprite.json"))
        return CompanionSprite.parse(data)!
    }

    static func hourglassText() -> String {
        try! String(contentsOfFile: WidgetPackFixtures.root + "/packs/hourglass/sprite.json", encoding: .utf8)
    }
}

@Suite("Companion growth")
struct CompanionGrowthTests {
    @Test(arguments: [(0, 0.0), (-4, 0.0), (3, 0.10), (10, 0.35), (30, 0.75), (60, 1.0), (400, 1.0)])
    func theCurvePassesThroughItsKnots(days: Int, growth: Double) {
        #expect(abs(CompanionGrowth.fromDays(days) - growth) < 1e-6)
    }

    @Test func theCurveIsLinearBetweenKnotsAndNeverFalls() {
        #expect(abs(CompanionGrowth.fromDays(6) - (0.10 + 0.25 * 3 / 7)) < 1e-6)
        #expect(abs(CompanionGrowth.fromDays(15) - (0.35 + 0.40 * 5 / 20)) < 1e-6)
        var last = -1.0
        for d in 0...90 {
            let g = CompanionGrowth.fromDays(d)
            #expect(g >= last, "day \(d)")
            last = g
        }
    }

    @Test func theHourglassReachesStageTwoOnDayThreeStageThreeOnDayTenAndTheLastOnDayThirty() {
        let sprite = CompanionTestKit.fixture("packs", "hourglass")
        #expect(sprite.stages.map(\.minGrowth) == [0.0, 0.1, 0.35, 0.75])
        #expect(sprite.stages.map { CompanionGrowth.daysFor($0.minGrowth) } == [0, 3, 10, 30])
        #expect(sprite.stage(at: CompanionGrowth.fromDays(2)).id == "egg")
        #expect(sprite.stage(at: CompanionGrowth.fromDays(3)).id == "sprout")
        #expect(sprite.stage(at: CompanionGrowth.fromDays(9)).id == "sprout")
        #expect(sprite.stage(at: CompanionGrowth.fromDays(10)).id == "runner")
        #expect(sprite.stage(at: CompanionGrowth.fromDays(29)).id == "runner")
        #expect(sprite.stage(at: CompanionGrowth.fromDays(30)).id == "keeper")
    }

    @Test(arguments: [(0.0, 0), (0.05, 2), (0.5, 18), (0.9, 48), (1.0, 60), (1.5, 60)])
    func anyPacksThresholdMapsToTheFirstDayTheCurveReachesIt(growth: Double, days: Int) {
        #expect(CompanionGrowth.daysFor(growth) == days)
        #expect(CompanionGrowth.fromDays(days) + 1e-9 >= min(growth, 1))
        if days > 0 { #expect(CompanionGrowth.fromDays(days - 1) < growth) }
    }

    @Test func theStageIsTheHighestThresholdReached() {
        let mins = [0, 0.1, 0.35, 0.75]
        #expect(CompanionGrowth.stageIndex(mins, 0) == 0)
        #expect(CompanionGrowth.stageIndex(mins, 0.0999) == 0)
        #expect(CompanionGrowth.stageIndex(mins, 0.1) == 1)
        #expect(CompanionGrowth.stageIndex(mins, 0.74) == 2)
        #expect(CompanionGrowth.stageIndex(mins, 1) == 3)
        #expect(CompanionGrowth.stageIndex([0], 1) == 0)
    }

    @Test func growthNeverGoesBackwardsAndDaysAccumulateAcrossSources() {
        typealias K = CompanionTestKit
        var s = CompanionGrowthState.empty.observe([K.day("2026-10-01"), K.day("2026-10-02"), K.day("2026-10-03")])
        #expect(abs(s.growth - 0.10) < 1e-6)
        #expect(s.days.count == 3)

        // The source forgot the old days (Claude Code's logs are pruned): the remembered ones still count.
        s = s.observe([K.day("2026-10-04")])
        #expect(s.days.count == 4)
        #expect(abs(s.growth - CompanionGrowth.fromDays(4)) < 1e-6)

        // A day seen twice is one day.
        #expect(s.observe([K.day("2026-10-04"), K.day("2026-10-04")]).days.count == 4)

        // A held value above the curve is kept: never backwards.
        let high = CompanionGrowthState(growth: 0.8, days: [], active: 0).observe([K.day("2026-10-01")])
        #expect(abs(high.growth - 0.8) < 1e-6)
    }

    @Test func usageNeverEntersGrowth() {
        typealias K = CompanionTestKit
        let calm = CompanionGrowthState.empty.observe([K.day("2026-10-01", minutes: 20), K.day("2026-10-02", minutes: 20)])
        let heavy = CompanionGrowthState.empty.observe([K.day("2026-10-01", minutes: 600), K.day("2026-10-02", minutes: 600)])
        #expect(calm.growth == heavy.growth)
        #expect(heavy.active > calm.active)
    }

    @Test func theMemoryRoundTripsAndABrokenFileReadsAsEmpty() throws {
        typealias K = CompanionTestKit
        let dir = K.tempDir()
        defer { try? FileManager.default.removeItem(at: dir) }
        let s = CompanionGrowthState.empty.observe([K.day("2026-10-01", minutes: 90), K.day("2026-09-30", minutes: 30)])
        let file = CompanionGrowthState.file(in: dir)
        #expect(file.lastPathComponent == "companion.json")
        #expect(s.save(file))
        let back = CompanionGrowthState.load(file)
        #expect(abs(back.growth - s.growth) < 1e-6)
        #expect(back.days == s.days)
        #expect(back.active == 7200)
        let json = try String(contentsOf: file, encoding: .utf8)
        #expect(json.contains("\"2026-09-30\""))
        #expect(!json.lowercased().contains("token"))

        #expect(CompanionGrowthState.parse(Data("not json".utf8)) == .empty)
        let odd = CompanionGrowthState.parse(Data(#"{"growth": 7, "days": ["nope", 3, "2026-10-01"]}"#.utf8))
        #expect(odd.growth == 1.0)
        #expect(odd.days.count == 1)
        #expect(CompanionGrowthState.load(dir.appendingPathComponent("absent.json")).growth == 0)
    }

    @Test func nothingIsWrittenAfterReset() {
        let dir = CompanionTestKit.tempDir()
        defer { try? FileManager.default.removeItem(at: dir) }
        let file = CompanionGrowthState.file(in: dir)
        ResetGate.$testOverride.withValue(true) {
            #expect(CompanionGrowthState.empty.observe([CompanionTestKit.day("2026-10-01")]).save(file) == false)
        }
        #expect(!FileManager.default.fileExists(atPath: file.path))
    }

    @Test func theReaderPicksTheSourceFoldsItInAndSavesOnlyWhenSomethingMoved() throws {
        typealias K = CompanionTestKit
        let dir = K.tempDir(), time = dir.appendingPathComponent("days")
        defer { try? FileManager.default.removeItem(at: dir) }
        let stateFile = CompanionGrowthState.file(in: dir)
        let reader = CompanionGrowthReader(timeDaysDir: time, stateFile: stateFile) { [K.day("2026-10-01", minutes: 0), K.day("2026-10-02", minutes: 0)] }

        // No Sanduhr Time folder: the Claude Code days.
        var state = reader.refresh(from: reader.loadState())
        #expect(state.days.count == 2)
        #expect(FileManager.default.fileExists(atPath: stateFile.path))

        // Nothing new: the file is not touched again.
        let stamp = try FileManager.default.attributesOfItem(atPath: stateFile.path)[.modificationDate] as? Date
        Thread.sleep(forTimeInterval: 0.05)
        state = reader.refresh(from: state)
        #expect(try FileManager.default.attributesOfItem(atPath: stateFile.path)[.modificationDate] as? Date == stamp)

        // Once Sanduhr Time has written, it is the source (the remembered days stay).
        try FileManager.default.createDirectory(at: time, withIntermediateDirectories: true)
        try Data(SanduhrTimeDayFixtures.v2.utf8).write(to: time.appendingPathComponent("2026-10-06.json"))
        state = reader.refresh(from: state)
        #expect(state.days.count == 3)
        #expect(state.days.contains(CCLocalDay.parse("2026-10-06")!))
    }

    @Test func claudeCodeActivityGivesDatesOnly() throws {
        let root = CompanionTestKit.tempDir()
        defer { try? FileManager.default.removeItem(at: root) }
        let project = root.appendingPathComponent("projects/p1", isDirectory: true)
        try FileManager.default.createDirectory(at: project, withIntermediateDirectories: true)
        var cal = Calendar(identifier: .gregorian)
        cal.timeZone = TimeZone(identifier: "UTC")!
        let now = ISO8601DateFormatter().date(from: "2026-10-10T12:00:00Z")!
        let line = { (ts: String) in
            #"{"type":"assistant","timestamp":"\#(ts)","message":{"model":"claude-sonnet-5-5","usage":{"input_tokens":5,"output_tokens":7}}}"# + "\n"
        }
        try Data((line("2026-10-08T10:00:00Z") + line("2026-10-08T11:00:00Z") + line("2026-10-09T09:00:00Z")).utf8)
            .write(to: project.appendingPathComponent("s.jsonl"))
        let days = CompanionGrowthReader.claudeCodeDays([CCLogReader(root: root.path)], daysBack: 60, now: now, calendar: cal)
        #expect(days.map(\.date.isoText) == ["2026-10-08", "2026-10-09"])
        #expect(days.allSatisfy { $0.active == 0 })
    }
}

enum SanduhrTimeDayFixtures {
    static let v1 = #"{"v":1,"date":"2026-10-05","machine":"m","generatedAt":"2026-10-05T23:00:00Z","totals":{"youMs":1000,"claudeMs":2000,"bothMs":500,"totalMs":3500,"agentMs":2500,"linesYou":3,"linesClaude":9},"projects":[],"caveats":{}}"#
    static let v2 = #"{"v":2,"date":"2026-10-06","machine":"m","generatedAt":"2026-10-06T23:00:00Z","totals":{"youMs":60000,"agentsMs":120000,"bothMs":0,"totalMs":180000,"agentMs":120000,"linesYou":0,"linesAgents":0,"byAgent":{}},"projects":[],"caveats":{}}"#
}

@Suite("Sanduhr Time day files")
struct SanduhrTimeDaysTests {
    @Test func aV1AndAV2DayFileEachGiveTheirDateAndTotalTime() {
        let v1 = SanduhrTimeDays.parse(Data(SanduhrTimeDayFixtures.v1.utf8))
        #expect(v1?.date == CCLocalDay(year: 2026, month: 10, day: 5))
        #expect(v1?.active == 3.5)
        let v2 = SanduhrTimeDays.parse(Data(SanduhrTimeDayFixtures.v2.utf8))
        #expect(v2?.date == CCLocalDay(year: 2026, month: 10, day: 6))
        #expect(v2?.active == 180)

        // A v1 file without totalMs is summed from its parts; v2 reads agentsMs instead of claudeMs.
        let parts = SanduhrTimeDays.parse(Data(#"{"v":1,"date":"2026-10-07","totals":{"youMs":10,"claudeMs":20,"bothMs":5}}"#.utf8))
        #expect(parts?.active == 0.035)
        let v2parts = SanduhrTimeDays.parse(Data(#"{"v":2,"date":"2026-10-07","totals":{"youMs":10,"claudeMs":999,"agentsMs":20}}"#.utf8))
        #expect(v2parts?.active == 0.03)
    }

    @Test(arguments: [
        #"{"v":3,"date":"2026-10-05","totals":{"totalMs":100}}"#,
        #"{"v":2,"date":"10/05/2026","totals":{"totalMs":100}}"#,
        #"{"v":2,"date":"2026-13-45","totals":{"totalMs":100}}"#,
        #"{"v":2,"date":"2026-10-05","totals":{"totalMs":0,"youMs":0}}"#,
        #"{"v":2,"date":"2026-10-05"}"#,
        #"{"v":true,"date":"2026-10-05","totals":{"totalMs":100}}"#,
        "[1,2]",
        "not json",
    ])
    func aFileThatIsNotADayWithTimeIsNotACodingDay(json: String) {
        #expect(SanduhrTimeDays.parse(Data(json.utf8)) == nil)
    }

    @Test func aLongDayIsCappedAtTwentyFourHours() {
        let d = SanduhrTimeDays.parse(Data(#"{"v":2,"date":"2026-10-05","totals":{"totalMs":900000000}}"#.utf8))
        #expect(d?.active == 86_400)
    }

    @Test func theDaysFolderIsReadByDateNamedFilesOnlyAndAbsentIsEmpty() throws {
        let dir = CompanionTestKit.tempDir()
        defer { try? FileManager.default.removeItem(at: dir) }
        let none = dir.appendingPathComponent("none")
        #expect(SanduhrTimeDays.read(none).isEmpty)
        #expect(!SanduhrTimeDays.present(none))
        let write = { (name: String, text: String) in try Data(text.utf8).write(to: dir.appendingPathComponent(name)) }
        try write("2026-10-05.json", SanduhrTimeDayFixtures.v1)
        try write("2026-10-06.json", SanduhrTimeDayFixtures.v2)
        try write("2026-10-07.json", #"{"v":2,"date":"2026-10-07","totals":{"totalMs":0}}"#)
        try write("notes.json", SanduhrTimeDayFixtures.v2)
        let days = SanduhrTimeDays.read(dir)
        #expect(SanduhrTimeDays.present(dir))
        #expect(days.map(\.date) == [CCLocalDay(year: 2026, month: 10, day: 5), CCLocalDay(year: 2026, month: 10, day: 6)])
    }

    @Test func theReaderOpensOnlyWhatChanged() throws {
        let dir = CompanionTestKit.tempDir()
        defer { try? FileManager.default.removeItem(at: dir) }
        try Data(SanduhrTimeDayFixtures.v1.utf8).write(to: dir.appendingPathComponent("2026-10-05.json"))
        try Data(SanduhrTimeDayFixtures.v2.utf8).write(to: dir.appendingPathComponent("2026-10-06.json"))
        let reader = SanduhrTimeDaysReader()
        #expect(reader.read(dir).count == 2)
        #expect(reader.opened == 2)
        #expect(reader.read(dir).count == 2)
        #expect(reader.opened == 0)
        try Data(SanduhrTimeDayFixtures.v1.utf8 + Data(" ".utf8)).write(to: dir.appendingPathComponent("2026-10-05.json"))
        #expect(reader.read(dir).count == 2)
        #expect(reader.opened == 1)
    }
}

@Suite("Companion sprite")
struct CompanionSpriteTests {
    typealias K = CompanionTestKit

    @Test func theHourglassReadsWithItsMappingRoamAndMixedCellSizes() {
        let s = K.fixture("packs", "hourglass")
        #expect(s.scale == 2)
        #expect(s.fps == 8)
        #expect(s.stages.map(\.id) == ["egg", "sprout", "runner", "keeper"])
        #expect(s.stages[0].frameWidth == 32 && s.stages[0].frameHeight == 32)
        #expect(s.stages[3].frameWidth == 40 && s.stages[3].frameHeight == 40)
        #expect(s.moods[.content] == "sit")
        #expect(s.moods[.exhausted] == "sleep")
        #expect(s.triggers[.needsYou] == "pop")
        #expect(s.triggers[.idle] == "nap")
        #expect(s.edges == [.bottom, .left, .right])
        #expect(s.speedPxPerSecond == 30)
        #expect(s.maxFrame == 27)
        #expect(s.frames("walk") == [0, 1, 2, 3])
        #expect(s.frames("dance").isEmpty)
    }

    @Test func aSingleSheetSpriteIsOneStageAtTheTopLevelCellSize() {
        let s = K.fixture("good", "good-sprite")
        #expect(s.stages.count == 1)
        #expect(s.stages[0].id == CompanionSprite.singleStageId)
        #expect(s.stages[0].minGrowth == 0)
        #expect(s.stage(at: 1) == s.stages[0])
    }

    @Test func stageSelectionWithAPixelSheetStageAndALargerPngStage() {
        let s = K.fixture("good", "good-sprite-pixel-stages")
        let one = s.stage(at: 0.49)
        #expect(one.id == "one")
        #expect(one.isPixelSheet)
        #expect(one.frameWidth == 8 && one.frameHeight == 8)
        let two = s.stage(at: 0.5)
        #expect(two.id == "two")
        #expect(!two.isPixelSheet)
        #expect(two.frameWidth == 16 && two.frameHeight == 16)

        let store = WidgetStore(root: FileManager.default.temporaryDirectory.appendingPathComponent("w"))
        #expect(CompanionSheet.drawnFile(store: store, packId: "good-sprite-pixel-stages", stage: one)
            == store.rasterPath("good-sprite-pixel-stages", "sprites/one.json"))
        #expect(CompanionSheet.drawnFile(store: store, packId: "good-sprite-pixel-stages", stage: two).path
            .hasSuffix("good-sprite-pixel-stages/sprites/two.png"))
    }

    @Test(arguments: [#"{"moods":{}}"#, "not json"])
    func aSpriteThatIsNotUsableReadsAsNil(json: String) {
        #expect(CompanionSprite.parse(Data(json.utf8)) == nil)
    }

    @Test func aMappingToAnAnimationThatIsMissingOrAMissingTriggerReadsAsNil() {
        let good = K.hourglassText()
        #expect(CompanionSprite.parse(Data(good.utf8)) != nil)
        #expect(CompanionSprite.parse(Data(good.replacingOccurrences(of: "\"content\": \"sit\"", with: "\"content\": \"dance\"").utf8)) == nil)
        #expect(CompanionSprite.parse(Data(good.replacingOccurrences(of: "\"idle\": \"nap\"", with: "\"idle_x\": \"nap\"").utf8)) == nil)
        #expect(CompanionSprite.parse(Data(good.replacingOccurrences(of: "\"min_growth\": 0.35", with: "\"min_growth\": 0.05").utf8)) == nil)
    }

    @Test(arguments: [(0, 0, 0), (6, 192, 0), (7, 0, 32), (27, 192, 96)])
    func aPngFrameIsTheIthCellRowMajor(index: Int, x: Int, y: Int) {
        #expect(CompanionSheet.cell(index, sheetWidth: 224, sheetHeight: 128, frameWidth: 32, frameHeight: 32) == PxRect(x: x, y: y, width: 32, height: 32))
    }

    @Test func aCellPastTheSheetOrASheetThatIsNotWholeCellsHasNoFrame() {
        #expect(CompanionSheet.cell(28, sheetWidth: 224, sheetHeight: 128, frameWidth: 32, frameHeight: 32) == nil)
        #expect(CompanionSheet.cell(-1, sheetWidth: 224, sheetHeight: 128, frameWidth: 32, frameHeight: 32) == nil)
        #expect(CompanionSheet.cell(0, sheetWidth: 225, sheetHeight: 128, frameWidth: 32, frameHeight: 32) == nil)
        #expect(CompanionSheet.cell(0, sheetWidth: 224, sheetHeight: 100, frameWidth: 32, frameHeight: 32) == nil)
        #expect(CompanionSheet.cell(1, sheetWidth: 280, sheetHeight: 160, frameWidth: 40, frameHeight: 40) == PxRect(x: 40, y: 0, width: 40, height: 40))
    }

    @Test func theCellIsAlsoAContentsRectInUnitCoordinates() {
        let r = CompanionSheet.contentsRect(7, sheetWidth: 224, sheetHeight: 128, frameWidth: 32, frameHeight: 32)!
        #expect(r == CGRect(x: 0, y: 0.25, width: 32.0 / 224, height: 0.25))
        let flipped = CompanionSheet.contentsRect(7, sheetWidth: 224, sheetHeight: 128, frameWidth: 32, frameHeight: 32, originAtBottom: true)!
        #expect(flipped == CGRect(x: 0, y: 0.5, width: 32.0 / 224, height: 0.25))
        #expect(CompanionSheet.contentsRect(28, sheetWidth: 224, sheetHeight: 128, frameWidth: 32, frameHeight: 32) == nil)
    }

    @Test func aRasterizedPixelSheetIsCutByTheSameRuleAsAPng() throws {
        // 20 frames of 8 x 8, each a different color at its own pixel: rasterized 16 to a row.
        let palette = (0..<16).map { "\"#" + String(format: "%02x", $0 * 15 + 1) + "2040\"" }
        let frames = (0..<20).map { i -> String in
            let rows = (0..<8).map { y -> String in
                y == i % 8
                    ? "\"" + String(repeating: ".", count: i % 8) + String(i % 16, radix: 16) + String(repeating: ".", count: 7 - i % 8) + "\""
                    : "\"........\""
            }
            return "[" + rows.joined(separator: ",") + "]"
        }
        let json = "{\"palette\":[\(palette.joined(separator: ","))],\"frame_w\":8,\"frame_h\":8,\"frames\":[\(frames.joined(separator: ","))]}"
        let image = try PixelSheet.rasterize(Data(json.utf8)).get()
        #expect(image.width == 128 && image.height == 16)
        for i in [0, 15, 16, 19] {
            let cell = try #require(CompanionSheet.cell(i, sheetWidth: image.width, sheetHeight: image.height, frameWidth: 8, frameHeight: 8))
            var cut = [UInt8](repeating: 0, count: 8 * 8 * 4)
            for y in 0..<8 {
                let from = ((cell.y + y) * image.width + cell.x) * 4
                cut.replaceSubrange(y * 32..<y * 32 + 32, with: image.rgba[from..<from + 32])
            }
            #expect(image.frame(i) == cut)
            let at = ((i % 8) * 8 + i % 8) * 4
            #expect(cut[at + 3] == 255)
        }
        #expect(CompanionSheet.cell(32, sheetWidth: image.width, sheetHeight: image.height, frameWidth: 8, frameHeight: 8) == nil)
    }
}
