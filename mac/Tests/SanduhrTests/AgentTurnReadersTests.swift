import Foundation
import Testing
@testable import Sanduhr

/// Mac agent notifications, item 2: the Codex and Antigravity turn readers, ported from the
/// Windows readers' tests. Pure: made-up lines, no files, no clocks.
@Suite("Agent turn readers")
struct AgentTurnReadersTests {
    static let t0 = "2026-11-17T20:00:00.000Z"
    static let t2m = "2026-11-17T20:02:04.000Z"
    let base = Date(timeIntervalSince1970: 1_794_000_000)

    func data(_ o: [String: Any]) -> Data { try! JSONSerialization.data(withJSONObject: o) }

    // MARK: Codex

    func rollout(_ timestamp: String, _ type: String, _ payload: [String: Any]) -> Data {
        data(["timestamp": timestamp, "type": type, "payload": payload])
    }

    func meta(parent: String? = nil, forkedFrom: String? = nil, cwd: String = "/work/alpha") -> Data {
        var p: [String: Any] = ["id": "s", "cwd": cwd]
        if let parent { p["parent_thread_id"] = parent }
        if let forkedFrom { p["forked_from_id"] = forkedFrom }
        return rollout(Self.t0, "session_meta", p)
    }

    func context(_ cwd: String) -> Data { rollout(Self.t0, "turn_context", ["cwd": cwd, "model": "m"]) }

    func event(_ timestamp: String, _ kind: String, _ more: [String: Any] = [:]) -> Data {
        var p: [String: Any] = ["type": kind, "turn_id": "t1"]
        for (k, v) in more { p[k] = v }
        return rollout(timestamp, "event_msg", p)
    }

    func codex() -> CodexTurnReader { CodexTurnReader(projectName: { URL(fileURLWithPath: $0).lastPathComponent }) }

    func date(_ iso: String) -> Date { CCTimestamp.parse(iso)! }

    @Test func aCodexTurnStartsAndFinishesWithTheLoggedDuration() throws {
        var r = codex()
        #expect(r.read(lines: [meta()], file: "rollout-a.jsonl").isEmpty)
        let started = r.read(lines: [event(Self.t0, "task_started")], file: "rollout-a.jsonl")
        #expect(started == [AgentTurnEvent(agent: .codex, kind: .started, project: "alpha", session: "rollout-a",
                                           at: date(Self.t0), duration: nil)])
        #expect(r.turnOpen)

        let done = r.read(lines: [event(Self.t2m, "task_complete", ["duration_ms": 124000])], file: "rollout-a.jsonl")
        let e = try #require(done.first)
        #expect(e.kind == .finished)
        #expect(e.duration == 124)
        #expect(e.at == date(Self.t2m))
        #expect(!r.turnOpen)
    }

    @Test func aWholeTurnInOneBatchGivesBothEventsInOrder() {
        var r = codex()
        let out = r.read(lines: [meta(), event(Self.t0, "task_started"), event(Self.t2m, "task_complete", ["duration_ms": 124000])],
                         file: "rollout-a.jsonl")
        #expect(out.map(\.kind) == [.started, .finished])
    }

    @Test func withoutALoggedDurationItIsTheTimeBetweenTheMarkers() throws {
        var r = codex()
        let out = r.read(lines: [meta(), event(Self.t0, "task_started"), event(Self.t2m, "task_complete")], file: "rollout-a.jsonl")
        #expect(try #require(out.last).duration == 124)
    }

    @Test func aCompletionStampedBeforeItsStartHasNoDuration() throws {
        var r = codex()
        let out = r.read(lines: [meta(), event(Self.t2m, "task_started"), event(Self.t0, "task_complete")], file: "rollout-a.jsonl")
        let e = try #require(out.last)
        #expect(e.kind == .finished)
        #expect(e.duration == nil)
    }

    @Test func anAbortedTurnIsStoppedWithTheTimeBetweenTheMarkers() throws {
        var r = codex()
        let out = r.read(lines: [meta(), event(Self.t0, "task_started"), event(Self.t2m, "turn_aborted")], file: "rollout-a.jsonl")
        let e = try #require(out.last)
        #expect(e.kind == .stopped)
        #expect(e.duration == 124)
        #expect(!r.turnOpen)
    }

    @Test func theProjectIsTheTurnContextsFolder() throws {
        var r = codex()
        let out = r.read(lines: [meta(cwd: "/work/alpha"), context("/work/beta"), event(Self.t0, "task_started")],
                         file: "rollout-a.jsonl")
        #expect(try #require(out.first).project == "beta")
    }

    @Test func theDefaultProjectNameIsTheDisplayName() throws {
        var r = CodexTurnReader()
        let out = r.read(lines: [meta(cwd: "/work/gamma"), event(Self.t0, "task_started")], file: "rollout-a.jsonl")
        #expect(try #require(out.first).project == CCProjectName.displayName("/work/gamma"))
    }

    @Test func aTurnWithNoFolderHasNoProject() throws {
        var r = codex()
        let out = r.read(lines: [event(Self.t0, "task_started")], file: "rollout-a.jsonl")
        #expect(try #require(out.first).project == nil)
    }

    @Test func theSessionIsTheFileNameWithoutItsExtension() throws {
        var r = codex()
        let a = r.read(lines: [event(Self.t0, "task_started")], file: "rollout-2026-11-17T20-00-00-abc.jsonl")
        #expect(try #require(a.first).session == "rollout-2026-11-17T20-00-00-abc")
        var r2 = codex()
        let b = r2.read(lines: [event(Self.t0, "task_started")], file: "/sessions/2026/11/17/rollout-x.jsonl")
        #expect(try #require(b.first).session == "rollout-x")
    }

    @Test(arguments: [("parent-thread", String?.none), (String?.none, "forked-from")] as [(String?, String?)])
    func aSubAgentRolloutRaisesNothing(parent: String?, forkedFrom: String?) {
        var r = codex()
        _ = r.read(lines: [meta(parent: parent, forkedFrom: forkedFrom)], file: "rollout-child.jsonl")
        #expect(r.isChild)
        let out = r.read(lines: [event(Self.t0, "task_started"), event(Self.t2m, "task_complete", ["duration_ms": 124000])],
                         file: "rollout-child.jsonl")
        #expect(out.isEmpty)
    }

    /// A forked rollout's second session_meta is its parent's copy, without the keys (seen 2026-11-17).
    @Test func theChildMarkSticksAfterALaterMetaWithoutIt() {
        var r = codex()
        _ = r.read(lines: [meta(parent: "parent-thread"), meta()], file: "rollout-child.jsonl")
        #expect(r.isChild)
        #expect(r.read(lines: [event(Self.t0, "task_started")], file: "rollout-child.jsonl").isEmpty)
    }

    @Test func aTopLevelRolloutIsNotAChild() {
        var r = codex()
        _ = r.read(lines: [meta()], file: "rollout-a.jsonl")
        #expect(!r.isChild)
    }

    @Test func theAgentsLastMessageNeverReachesAnEvent() throws {
        var r = codex()
        let out = r.read(lines: [meta(), event(Self.t0, "task_started"),
                                 event(Self.t2m, "task_complete", ["duration_ms": 1000, "last_agent_message": "SECRET-REPLY"])],
                         file: "rollout-a.jsonl")
        #expect(out.count == 2)
        for e in out { #expect(!String(describing: e).contains("SECRET")) }
    }

    @Test(arguments: [
        "",
        "not json",
        "{\"type\":\"event_msg\"}",
        "{\"timestamp\":\"nope\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\"}}",
        "{\"timestamp\":\"2026-11-17T20:00:00Z\",\"type\":\"response_item\",\"payload\":{\"type\":\"message\",\"content\":\"task_complete\"}}",
        "{\"timestamp\":\"2026-11-17T20:00:00Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\"",
    ])
    func aCodexLineItCannotUseIsSkipped(line: String) {
        var r = codex()
        #expect(r.read(lines: [Data(line.utf8)], file: "rollout-a.jsonl").isEmpty)
        #expect(!r.turnOpen)
    }

    @Test func aTornLineIsSkippedAndTheNextOneStillReads() throws {
        var r = codex()
        let torn = Data(String(decoding: event(Self.t0, "task_started"), as: UTF8.self).dropLast(8).utf8)
        let out = r.read(lines: [torn, event(Self.t0, "task_started")], file: "rollout-a.jsonl")
        #expect(out.map(\.kind) == [.started])
    }

    @Test func aLoggedDurationTooLargeForATimeSpanFallsBackToTheMarkers() throws {
        var r = codex()
        let out = r.read(lines: [meta(), event(Self.t0, "task_started"), event(Self.t2m, "task_complete", ["duration_ms": 1e300])],
                         file: "rollout-a.jsonl")
        let e = try #require(out.last)
        #expect(e.kind == .finished)
        #expect(e.duration == 124)
        #expect(!r.turnOpen)
    }

    @Test func aNegativeOrBooleanLoggedDurationFallsBackToTheMarkers() throws {
        for bad: Any in [-5, true] {
            var r = codex()
            let out = r.read(lines: [event(Self.t0, "task_started"), event(Self.t2m, "task_complete", ["duration_ms": bad])],
                             file: "rollout-a.jsonl")
            #expect(try #require(out.last).duration == 124)
        }
    }

    @Test func aTurnStateCarriesAcrossBatches() throws {
        var r = codex()
        _ = r.read(lines: [event(Self.t0, "task_started")], file: "rollout-a.jsonl")
        let out = r.read(lines: [event(Self.t2m, "task_complete")], file: "rollout-a.jsonl")
        #expect(try #require(out.first).duration == 124)
    }

    // MARK: Antigravity

    func step(_ at: Date, _ type: String, status: String = "DONE", toolCalls: Bool = false, content: String? = nil) -> Data {
        var o: [String: Any] = ["type": type, "status": status, "created_at": VaultTime.iso(at), "step_index": 1, "source": "MODEL"]
        if toolCalls { o["tool_calls"] = [["name": "SECRET-TOOL"]] }
        if let content { o["content"] = content }
        return data(o)
    }

    func ag() -> AntigravityTurnReader { AntigravityTurnReader(projectName: { URL(fileURLWithPath: $0).lastPathComponent }) }

    func feed(_ r: inout AntigravityTurnReader, _ line: Data, project: String? = nil, now: Date) -> [AgentTurnEvent] {
        r.read(lines: [line], conversation: "conv-1", project: project, now: now)
    }

    @Test func aUserInputStartsATurn() {
        var r = ag()
        let e = feed(&r, step(base, "USER_INPUT"), project: "/work/alpha", now: base)
        #expect(e == [AgentTurnEvent(agent: .antigravity, kind: .started, project: "alpha", session: "conv-1",
                                     at: VaultTime.parse(VaultTime.iso(base))!, duration: nil)])
        #expect(r.turnOpen)
    }

    @Test func aFinalLookingStepFinishesAfterNinetyQuietSeconds() throws {
        var r = ag()
        _ = feed(&r, step(base, "USER_INPUT"), project: "/work/alpha", now: base)
        let end = base.addingTimeInterval(200)
        #expect(feed(&r, step(end, "PLANNER_RESPONSE"), now: end).isEmpty)
        #expect(r.dueAt == end.addingTimeInterval(AntigravityTurnReader.quiet))
        #expect(r.tick(now: end.addingTimeInterval(89)).isEmpty)

        let done = r.tick(now: end.addingTimeInterval(90))
        let e = try #require(done.first)
        #expect(done.count == 1)
        #expect(e.kind == .finished)
        #expect(e.agent == .antigravity)
        #expect(e.session == "conv-1")
        #expect(e.project == "alpha")
        #expect(abs(e.at.timeIntervalSince(end)) < 0.001)
        #expect(abs(try #require(e.duration) - 200) < 0.001)
        #expect(!r.turnOpen)
        #expect(r.dueAt == nil)
        #expect(r.tick(now: end.addingTimeInterval(500)).isEmpty)
    }

    @Test func aStepThatAsksForAToolIsNotAnEnding() {
        var r = ag()
        _ = feed(&r, step(base, "USER_INPUT"), now: base)
        let t = base.addingTimeInterval(5)
        _ = feed(&r, step(t, "PLANNER_RESPONSE", toolCalls: true), now: t)
        #expect(r.dueAt == nil)
        #expect(r.tick(now: base.addingTimeInterval(900)).isEmpty)
    }

    /// 19 of 341 mid-turn steps looked final and were followed by a SYSTEM_MESSAGE 5 to 83 s later (2026-11-17).
    @Test(arguments: [5, 83])
    func aLookAlikeEndingFollowedByMoreWorkRaisesNothing(gap: Int) {
        var r = ag()
        _ = feed(&r, step(base, "USER_INPUT"), now: base)
        let final = base.addingTimeInterval(30)
        _ = feed(&r, step(final, "PLANNER_RESPONSE"), now: final)
        let next = final.addingTimeInterval(TimeInterval(gap))
        #expect(feed(&r, step(next, "SYSTEM_MESSAGE"), now: next).isEmpty)
        #expect(r.dueAt == nil)
        #expect(r.tick(now: next.addingTimeInterval(900)).isEmpty)
        #expect(r.turnOpen)
    }

    @Test func aLineThatWasNotReadStillCancelsAPendingEnd() {
        var r = ag()
        _ = feed(&r, step(base, "USER_INPUT"), now: base)
        let final = base.addingTimeInterval(30)
        _ = feed(&r, step(final, "PLANNER_RESPONSE"), now: final)
        #expect(r.dueAt != nil)
        r.activity()
        #expect(r.dueAt == nil)
    }

    @Test func anUnparseableLineStillCancelsAPendingEnd() {
        var r = ag()
        _ = feed(&r, step(base, "USER_INPUT"), now: base)
        let final = base.addingTimeInterval(30)
        _ = feed(&r, step(final, "PLANNER_RESPONSE"), now: final)
        #expect(feed(&r, Data("not json".utf8), now: final.addingTimeInterval(1)).isEmpty)
        #expect(r.dueAt == nil)
    }

    @Test func aRunningOrFailedPlannerStepIsNotAnEnding() {
        var r = ag()
        _ = feed(&r, step(base, "USER_INPUT"), now: base)
        _ = feed(&r, step(base.addingTimeInterval(5), "PLANNER_RESPONSE", status: "RUNNING"), now: base.addingTimeInterval(5))
        #expect(r.dueAt == nil)
        _ = feed(&r, step(base.addingTimeInterval(6), "GENERIC", status: "ERROR"), now: base.addingTimeInterval(6))
        #expect(r.dueAt == nil)
    }

    @Test func anEndingStampedBeforeItsStartHasNoDuration() throws {
        var r = ag()
        _ = feed(&r, step(base.addingTimeInterval(50), "USER_INPUT"), now: base)
        _ = feed(&r, step(base, "PLANNER_RESPONSE"), now: base.addingTimeInterval(60))
        let e = try #require(r.tick(now: base.addingTimeInterval(200)).first)
        #expect(e.duration == nil)
    }

    @Test func anEndingWithNoStartSeenStillFinishesWithNoDuration() throws {
        var r = ag()
        _ = feed(&r, step(base, "PLANNER_RESPONSE"), now: base)
        let e = try #require(r.tick(now: base.addingTimeInterval(90)).first)
        #expect(e.kind == .finished)
        #expect(e.duration == nil)
    }

    @Test func settleDropsAPendingEndAndClosesTheTurnSilently() {
        var r = ag()
        _ = feed(&r, step(base, "USER_INPUT"), now: base)
        _ = feed(&r, step(base.addingTimeInterval(30), "PLANNER_RESPONSE"), now: base.addingTimeInterval(30))
        r.settle()
        #expect(r.dueAt == nil)
        #expect(!r.turnOpen)
        #expect(r.tick(now: base.addingTimeInterval(900)).isEmpty)
    }

    @Test func settleLeavesATurnThatIsStillWorkingOpen() {
        var r = ag()
        _ = feed(&r, step(base, "USER_INPUT"), now: base)
        _ = feed(&r, step(base.addingTimeInterval(5), "PLANNER_RESPONSE", toolCalls: true), now: base.addingTimeInterval(5))
        r.settle()
        #expect(r.turnOpen)
    }

    @Test func aSecondTurnAfterAFinishStartsAgain() throws {
        var r = ag()
        _ = feed(&r, step(base, "USER_INPUT"), now: base)
        _ = feed(&r, step(base.addingTimeInterval(10), "PLANNER_RESPONSE"), now: base.addingTimeInterval(10))
        #expect(r.tick(now: base.addingTimeInterval(100)).count == 1)
        let again = base.addingTimeInterval(300)
        #expect(feed(&r, step(again, "USER_INPUT"), now: again).map(\.kind) == [.started])
        #expect(r.turnOpen)
    }

    @Test func stepTextAndToolNamesNeverReachAnEvent() throws {
        var r = ag()
        var all = feed(&r, step(base, "USER_INPUT", content: "SECRET-PROMPT"), now: base)
        all += feed(&r, step(base.addingTimeInterval(5), "PLANNER_RESPONSE", toolCalls: true, content: "SECRET-REPLY"),
                    now: base.addingTimeInterval(5))
        all += feed(&r, step(base.addingTimeInterval(9), "PLANNER_RESPONSE", content: "SECRET-REPLY"), now: base.addingTimeInterval(9))
        all += r.tick(now: base.addingTimeInterval(200))
        #expect(all.map(\.kind) == [.started, .finished])
        for e in all { #expect(!String(describing: e).contains("SECRET")) }
    }

    @Test(arguments: ["", "not json", "[1,2]", "{\"type\":\"USER_INPUT\"}", "{\"type\":\"USER_INPUT\",\"created_at\":\"nope\"}"])
    func anAntigravityLineItCannotUseIsSkipped(line: String) {
        var r = ag()
        #expect(feed(&r, Data(line.utf8), now: base).isEmpty)
        #expect(!r.turnOpen)
    }

    @Test func aBatchOfLinesIsReadInOrderWithTheNowOfTheBatch() throws {
        var r = ag()
        let out = r.read(lines: [step(base, "USER_INPUT"), step(base.addingTimeInterval(20), "PLANNER_RESPONSE")],
                         conversation: "conv-1", project: nil, now: base.addingTimeInterval(21))
        #expect(out.map(\.kind) == [.started])
        #expect(r.dueAt == base.addingTimeInterval(21 + AntigravityTurnReader.quiet))
    }
}
