import Foundation
import Testing
@testable import Sanduhr

/// Runner log lines (spec section 15): the lines captured on a Docker host (Fixtures/runners) and
/// the derived ones, each parsed to a job start, a job end, the runner coming online, or nothing.
/// Ported from Windows' RunnerLogTests.
@Suite("Runner log")
struct RunnerLogTests {
    private func fixture(_ name: String) throws -> [String] {
        let url = try #require(Bundle.module.url(forResource: "Fixtures/runners/\(name)", withExtension: "log"))
        return try String(contentsOf: url, encoding: .utf8).components(separatedBy: "\n")
            .map { $0.trimmingCharacters(in: CharacterSet(charactersIn: "\r")) }
            .filter { !$0.isEmpty && !$0.hasPrefix("#") }
    }

    private func utc(_ h: Int, _ m: Int, _ s: Int, day: Int = 22) -> Date {
        var cal = Calendar(identifier: .gregorian)
        cal.timeZone = TimeZone(identifier: "UTC")!
        return cal.date(from: DateComponents(year: 2026, month: 10, day: day, hour: h, minute: m, second: s))!
    }

    private let stamp = "2026-10-22T02:00:03.7Z 2026-10-22 02:00:03Z: "

    @Test func realSucceededRun() throws {
        let parsed = try fixture("host-alpha-succeeded").map(RunnerLog.parse)
        #expect(parsed == [
            .online(at: utc(1, 59, 10)),
            .jobStarted(job: "compile", at: utc(2, 0, 3)),
            .jobEnded(job: "compile", result: .passed, at: utc(2, 0, 18)),
            .jobStarted(job: "test", at: utc(2, 0, 19)),
            .jobEnded(job: "test", result: .passed, at: utc(2, 1, 58)),
        ])
    }

    @Test func realReconnectNoiseIsNothingButTheLastLine() throws {
        let parsed = try fixture("host-reconnect-noise").map(RunnerLog.parse)
        #expect(parsed.prefix(5).allSatisfy { $0 == nil })
        #expect(parsed[5] == .online(at: utc(18, 55, 55, day: 23)))
    }

    @Test(arguments: [
        ("Succeeded", WatcherState.passed), ("SucceededWithIssues", .passed),
        ("Failed", .failed), ("Abandoned", .failed),
        ("Canceled", .finished), ("Skipped", .finished), ("SomethingNew", .finished),
    ])
    func resultWords(word: String, expected: WatcherState) {
        #expect(RunnerLog.result(word) == expected)
    }

    @Test func realCanceledRun() throws {
        let parsed = try fixture("host-alpha-canceled").map(RunnerLog.parse)
        #expect(parsed == [
            .jobStarted(job: "test", at: utc(21, 37, 12, day: 23)),
            .jobEnded(job: "test", result: .finished, at: utc(21, 37, 36, day: 23)),
        ])
    }

    @Test func derivedLinesParse() throws {
        let ends: [(String, WatcherState)] = try fixture("derived").compactMap {
            if case let .jobEnded(job, result, _)? = RunnerLog.parse($0) { return (job, result) }
            return nil
        }
        let want: [(String, WatcherState)] = [
            ("build", .failed), ("docs", .passed), ("deploy", .finished), ("e2e", .failed),
            ("test (ubuntu-latest, 3.12)", .passed),
        ]
        #expect(ends.map(\.0) == want.map(\.0))
        #expect(ends.map(\.1) == want.map(\.1))
    }

    @Test func parsesAwkwardJobNames() {
        #expect(RunnerLog.parse(stamp + "Running job: deploy: prod (eu, 2)")
            == .jobStarted(job: "deploy: prod (eu, 2)", at: utc(2, 0, 3)))
        #expect(RunnerLog.parse(stamp + "Job Job x completed with result: y completed with result: Failed")
            == .jobEnded(job: "Job x completed with result: y", result: .failed, at: utc(2, 0, 3)))
    }

    @Test(arguments: [
        "",
        "Running job: compile", // no stamps
        "2026-10-22T02:00:03.7Z Running job: compile", // no runner stamp
        "2026-10-22T02:00:03.7Z 2026-10-22 02:00:03Z: Running job: ", // empty name
        "2026-10-22T02:00:03.7Z 2026-10-22 02:00:03Z: Runner reconnected.", // can happen mid-job
        "2026-10-22T02:00:03.7Z 2026-13-40 02:00:03Z: Listening for Jobs", // impossible date
    ])
    func nothing(line: String) {
        #expect(RunnerLog.parse(line) == nil)
    }

    @Test func longNamesAreClippedTheSameAtStartAndEnd() throws {
        let name = String(repeating: "a", count: 200)
        guard case let .jobStarted(startJob, _)? = RunnerLog.parse(stamp + "Running job: " + name),
              case let .jobEnded(endJob, _, _)? = RunnerLog.parse(stamp + "Job \(name) completed with result: Succeeded")
        else { Issue.record("did not parse"); return }
        #expect(startJob == endJob)
        #expect(startJob.count <= WatcherLimits.title)
    }

    @Test func trailingCarriageReturnIsIgnored() {
        #expect(RunnerLog.parse(stamp + "Running job: compile\r") == .jobStarted(job: "compile", at: utc(2, 0, 3)))
    }
}
