import Foundation
import Testing
@testable import Sanduhr

/// Mac agent notifications, item 1: the signal file Claude Code's hook writes, and the turn state
/// built from it. Pure: made-up data, no files, no clocks.
@Suite("Agent turns")
struct AgentTurnsTests {
    let now = Date(timeIntervalSince1970: 1_791_018_000)
    func ms(_ offset: TimeInterval) -> Int64 { Int64((now.addingTimeInterval(offset).timeIntervalSince1970 * 1000).rounded()) }

    func file(_ event: String = "prompt", cwd: String? = "/work/alpha", session: String? = "s1",
              at: Int64? = nil, v: Int = 1) -> Data {
        var o: [String: Any] = ["v": v, "event": event, "at": at ?? ms(-1)]
        if let cwd { o["cwd"] = cwd }
        if let session { o["session"] = session }
        return try! JSONSerialization.data(withJSONObject: o)
    }

    func signal(_ event: String, _ offset: TimeInterval, session: String = "s1", cwd: String? = "/work/alpha") -> ClaudeCodeSignalFile.Signal {
        ClaudeCodeSignalFile.Signal(event: event, cwd: cwd, session: session, at: now.addingTimeInterval(offset))
    }

    /// `#require` cannot take a mutating call, so the mutation goes through here.
    func run(_ t: inout ClaudeCodeTurns, _ s: ClaudeCodeSignalFile.Signal, replay: Bool) -> AgentTurnEvent? {
        t.apply(s, replay: replay)
    }

    func turns() -> ClaudeCodeTurns { ClaudeCodeTurns(projectName: { URL(fileURLWithPath: $0).lastPathComponent }) }

    // MARK: Parsing

    @Test func aGoodFileParses() throws {
        let s = try #require(ClaudeCodeSignalFile.parse(file("waiting", cwd: "/work/alpha", session: "abc"), now: now))
        #expect(s.event == "waiting")
        #expect(s.cwd == "/work/alpha")
        #expect(s.session == "abc")
        #expect(abs(s.at.timeIntervalSince(now.addingTimeInterval(-1))) < 0.01)
    }

    @Test func allThreeEventsParseAndOthersDoNot() {
        for e in ["prompt", "waiting", "done"] { #expect(ClaudeCodeSignalFile.parse(file(e), now: now) != nil) }
        #expect(ClaudeCodeSignalFile.parse(file("stop"), now: now) == nil)
        #expect(ClaudeCodeSignalFile.parse(file("DONE"), now: now) == nil)
    }

    @Test func aMissingSessionReadsAsTheNoSessionMarker() throws {
        let s = try #require(ClaudeCodeSignalFile.parse(file(session: nil), now: now))
        #expect(s.session == ClaudeCodeSignalFile.noSession)
        let blank = try #require(ClaudeCodeSignalFile.parse(file(session: "  "), now: now))
        #expect(blank.session == ClaudeCodeSignalFile.noSession)
    }

    @Test func aMissingOrBlankCwdIsNoProject() throws {
        #expect(try #require(ClaudeCodeSignalFile.parse(file(cwd: nil), now: now)).cwd == nil)
        #expect(try #require(ClaudeCodeSignalFile.parse(file(cwd: " "), now: now)).cwd == nil)
    }

    @Test func oversizedIsDropped() {
        let big = Data(repeating: 0x20, count: ClaudeCodeSignalFile.maxBytes) + file()
        #expect(big.count > ClaudeCodeSignalFile.maxBytes)
        #expect(ClaudeCodeSignalFile.parse(big, now: now) == nil)
        #expect(ClaudeCodeSignalFile.parse(Data(), now: now) == nil)
    }

    @Test func staleIsDroppedAndTheEdgeIsTen() {
        #expect(ClaudeCodeSignalFile.maxAge == 600)
        #expect(ClaudeCodeSignalFile.parse(file(at: ms(-599)), now: now) != nil)
        #expect(ClaudeCodeSignalFile.parse(file(at: ms(-601)), now: now) == nil)
    }

    @Test func aStampFromTheFutureIsDropped() {
        #expect(ClaudeCodeSignalFile.parse(file(at: ms(30)), now: now) != nil)
        #expect(ClaudeCodeSignalFile.parse(file(at: ms(120)), now: now) == nil)
    }

    @Test func wrongVersionOrJunkIsDropped() {
        #expect(ClaudeCodeSignalFile.parse(file(v: 2), now: now) == nil)
        #expect(ClaudeCodeSignalFile.parse(Data("not json".utf8), now: now) == nil)
        #expect(ClaudeCodeSignalFile.parse(Data("[1,2]".utf8), now: now) == nil)
        #expect(ClaudeCodeSignalFile.parse(Data("{\"v\":1,\"event\":\"done\"}".utf8), now: now) == nil)
    }

    @Test func aLongSessionIsDroppedAndALongCwdIsNoProject() throws {
        #expect(ClaudeCodeSignalFile.parse(file(session: String(repeating: "x", count: 129)), now: now) == nil)
        #expect(ClaudeCodeSignalFile.parse(file(session: String(repeating: "x", count: 128)), now: now) != nil)
        let long = try #require(ClaudeCodeSignalFile.parse(file(cwd: "/" + String(repeating: "a", count: 1024)), now: now))
        #expect(long.cwd == nil)
    }

    @Test func nonAsciiSpacesAndQuotesInCwdSurvive() throws {
        let cwd = "/work/Caf\u{E9} \"proj\" 'n' \u{65E5}\u{672C}"
        let s = try #require(ClaudeCodeSignalFile.parse(file(cwd: cwd), now: now))
        #expect(s.cwd == cwd)
        var t = ClaudeCodeTurns(projectName: { URL(fileURLWithPath: $0).lastPathComponent })
        let e = try #require(run(&t, s, replay: false))
        #expect(e.project == "Caf\u{E9} \"proj\" 'n' \u{65E5}\u{672C}")
    }

    // MARK: Turns

    @Test func aPromptStartsATurn() throws {
        var t = turns()
        let e = try #require(run(&t, signal("prompt", 0), replay: false))
        #expect(e.kind == .started)
        #expect(e.agent == .claude)
        #expect(e.project == "alpha")
        #expect(e.session == "s1")
        #expect(e.duration == nil)
    }

    @Test func doneFinishesWithTheTimeSinceThePrompt() throws {
        var t = turns()
        _ = run(&t, signal("prompt", 0), replay: false)
        let e = try #require(run(&t, signal("done", 125), replay: false))
        #expect(e.kind == .finished)
        #expect(e.duration == 125)
        #expect(e.at == now.addingTimeInterval(125))
    }

    @Test func doneWithNoPromptHasNoDuration() throws {
        var t = turns()
        let e = try #require(run(&t, signal("done", 5), replay: false))
        #expect(e.kind == .finished)
        #expect(e.duration == nil)
    }

    @Test func aSecondDoneForTheSameTurnHasNoDuration() throws {
        var t = turns()
        _ = run(&t, signal("prompt", 0), replay: false)
        _ = run(&t, signal("done", 60), replay: false)
        #expect(try #require(run(&t, signal("done", 61), replay: false)).duration == nil)
    }

    @Test func aClockSetBackGivesNoDuration() throws {
        var t = turns()
        _ = run(&t, signal("prompt", 100), replay: false)
        #expect(try #require(run(&t, signal("done", 40), replay: false)).duration == nil)
    }

    @Test func waitingInsideATurnIsWaitingAndOutsideItIsIdle() throws {
        var t = turns()
        #expect(try #require(run(&t, signal("waiting", 0), replay: false)).kind == .idle)
        _ = run(&t, signal("prompt", 1), replay: false)
        let w = try #require(run(&t, signal("waiting", 30), replay: false))
        #expect(w.kind == .waiting)
        #expect(w.duration == nil)
        // The turn is still open after a wait, so a second one is a wait too.
        #expect(try #require(run(&t, signal("waiting", 60), replay: false)).kind == .waiting)
        _ = run(&t, signal("done", 90), replay: false)
        #expect(try #require(run(&t, signal("waiting", 150), replay: false)).kind == .idle)
    }

    @Test func twoSessionsInOneFolderAreTwoTurns() throws {
        var t = turns()
        _ = run(&t, signal("prompt", 0, session: "a"), replay: false)
        _ = run(&t, signal("prompt", 40, session: "b"), replay: false)
        let b = try #require(run(&t, signal("done", 100, session: "b"), replay: false))
        let a = try #require(run(&t, signal("done", 130, session: "a"), replay: false))
        #expect(b.duration == 60)
        #expect(b.session == "b")
        #expect(a.duration == 130)
        #expect(a.session == "a")
        #expect(a.project == b.project)
    }

    @Test func aReplayRecordsAndRaisesNothing() throws {
        var t = turns()
        #expect(run(&t, signal("prompt", 0), replay: true) == nil)
        #expect(run(&t, signal("waiting", 10), replay: true) == nil)
        // The start was recorded: a finish after launch has its duration.
        let e = try #require(run(&t, signal("done", 90), replay: false))
        #expect(e.duration == 90)
        // A replayed done closes the turn, so a later wait is idle.
        _ = run(&t, signal("prompt", 100), replay: true)
        #expect(run(&t, signal("done", 110), replay: true) == nil)
        #expect(try #require(run(&t, signal("waiting", 120), replay: false)).kind == .idle)
    }

    @Test func aReplayedWaitingInAnOpenTurnStaysQuiet() {
        var t = turns()
        _ = run(&t, signal("prompt", 0), replay: true)
        #expect(run(&t, signal("waiting", 5), replay: true) == nil)
    }

    @Test func noCwdIsNoProject() throws {
        var t = turns()
        let e = try #require(run(&t, signal("prompt", 0, cwd: nil), replay: false))
        #expect(e.project == nil)
    }

    @Test func startsAreKeptForAtMostSixtyFourSessions() throws {
        var t = turns()
        for i in 0..<(ClaudeCodeTurns.maxSessions + 5) {
            _ = run(&t, signal("prompt", Double(i), session: "s\(i)"), replay: false)
        }
        // The oldest were dropped: no duration. The newest is kept.
        #expect(try #require(run(&t, signal("done", 500, session: "s0"), replay: false)).duration == nil)
        let last = ClaudeCodeTurns.maxSessions + 4
        #expect(try #require(run(&t, signal("done", 500, session: "s\(last)"), replay: false)).duration == 500 - Double(last))
    }

    @Test func theDefaultProjectNameIsTheBasenameOfAFolderThatIsNoRepo() throws {
        var t = ClaudeCodeTurns()
        let e = try #require(run(&t, signal("prompt", 0, cwd: "/no-such-root-247/float-demo"), replay: false))
        #expect(e.project == "float-demo")
    }
}
