import Foundation
import Testing
@testable import Sanduhr

/// Watchers (item 66): the state machine, ordering, lost touch and fades, the request and Stop
/// report decoding (with fixtures of Claude Code's Stop hook input), the hook's own extractor run
/// for real against a temp folder, the store's file handoff, and placement on the notch and the
/// Desk. Temp folders and made-up accounts only.
@Suite("Watchers")
struct WatcherTests {
    let t0 = Date(timeIntervalSince1970: 1_790_000_000)

    private func start(_ id: String, title: String = "CI on main", total: Int? = nil, link: URL? = nil,
                       work: Bool = false, short: String? = nil) -> WatcherCommand {
        .start(id: id, title: title, link: link, total: total, work: work, short: short)
    }

    private func task(_ id: String, _ status: String = "running", type: String = "shell",
                      description: String = "Run tests", name: String? = nil) -> BackgroundTask {
        BackgroundTask(id: id, type: type, status: status, description: description, name: name)
    }

    // MARK: State machine

    @Test func startUpdateWaitAndEnd() {
        var b = WatcherBoard()
        let r1 = b.apply(start("w1", total: 12), now: t0)
        #expect(!r1)
        let w = b.watcher("a:w1")
        #expect(w?.state == .running && w?.done == 0 && w?.progress == "0/12" && w?.source == .agent)
        // Waiting fires the glow once, not again while it stays waiting.
        let r2 = b.apply(.update(id: "w1", done: 3, note: "lint passed", state: .waiting), now: t0 + 10)
        #expect(r2)
        let r3 = b.apply(.update(id: "w1", done: 4, note: nil, state: .waiting), now: t0 + 20)
        #expect(!r3)
        #expect(b.watcher("a:w1")?.note == "lint passed")
        #expect(b.watcher("a:w1")?.progress == "4/12")
        // An update without a state keeps the state.
        let r4 = b.apply(.update(id: "w1", done: 5, note: nil, state: nil), now: t0 + 25)
        #expect(!r4)
        #expect(b.watcher("a:w1")?.state == .waiting)
        let r5 = b.apply(.update(id: "w1", done: nil, note: nil, state: .running), now: t0 + 30)
        #expect(!r5)
        b.apply(.end(id: "w1", result: .passed, note: "all green"), now: t0 + 60)
        let done = b.watcher("a:w1")
        #expect(done?.state == .passed && done?.progress == "12/12" && done?.ended == t0 + 60)
        #expect(done?.elapsed(now: t0 + 600) == 60)
        // Ended: further updates and ends do nothing.
        let r6 = b.apply(.update(id: "w1", done: 1, note: "late", state: .waiting), now: t0 + 61)
        #expect(!r6)
        b.apply(.end(id: "w1", result: .failed, note: nil), now: t0 + 62)
        #expect(b.watcher("a:w1")?.state == .passed)
        // Unknown ids are ignored.
        let r7 = b.apply(.update(id: "nope", done: 1, note: nil, state: .waiting), now: t0)
        #expect(!r7)
        #expect(b.watchers.count == 1)
    }

    @Test func startingTheSameIdAgainReplacesIt() {
        var b = WatcherBoard()
        b.apply(start("w1", title: "one"), now: t0)
        b.apply(start("w1", title: "two"), now: t0 + 5)
        #expect(b.watchers.count == 1)
        #expect(b.watcher("a:w1")?.title == "two")
    }

    @Test func orderingByUrgencyThenRecency() {
        var b = WatcherBoard()
        for id in ["run-old", "run-new", "wait", "fail", "pass", "lost"] { b.apply(start(id), now: t0) }
        b.apply(.update(id: "run-old", done: nil, note: nil, state: nil), now: t0 + 100)
        b.apply(.update(id: "run-new", done: nil, note: nil, state: nil), now: t0 + 200)
        b.apply(.update(id: "wait", done: nil, note: nil, state: .waiting), now: t0 + 50)
        b.apply(.end(id: "fail", result: .failed, note: nil), now: t0 + 40)
        b.apply(.end(id: "pass", result: .passed, note: nil), now: t0 + 300)
        b.tick(now: t0 + 650)   // "lost" heard nothing for over 10 minutes; the others did
        #expect(b.ordered().map(\.id) == ["a:wait", "a:fail", "a:run-new", "a:run-old", "a:lost"])
        #expect(WatcherState.waiting.urgency < WatcherState.failed.urgency)
        #expect(WatcherState.failed.urgency < WatcherState.running.urgency)
        #expect(WatcherState.running.urgency < WatcherState.lostTouch.urgency)
        #expect(WatcherState.lostTouch.urgency < WatcherState.passed.urgency)
    }

    @Test func lostTouchAfterTheWindowAndBackOnAnUpdate() {
        var b = WatcherBoard()
        b.apply(start("w1"), now: t0)
        let r8 = b.tick(now: t0 + 600)
        #expect(!r8)
        #expect(b.watcher("a:w1")?.state == .running)
        let r9 = b.tick(now: t0 + 601)
        #expect(r9)
        #expect(b.watcher("a:w1")?.state == .lostTouch)
        // Lost touch stays until dismissed or heard from again.
        b.tick(now: t0 + 86_400)
        #expect(b.watcher("a:w1")?.state == .lostTouch)
        b.apply(.update(id: "w1", done: nil, note: "back", state: nil), now: t0 + 86_401)
        #expect(b.watcher("a:w1")?.state == .running)
        #expect(b.nextChange() == t0 + 86_401 + 601)
    }

    @Test func automaticWatchersLoseTouchOnlyAfterAnHourWithoutAStop() {
        var b = WatcherBoard()
        b.applyStop(StopReport(session: "s1", folder: nil, tasks: [task("t1")]), work: false, now: t0)
        b.tick(now: t0 + 3000)
        #expect(b.watcher("b:s1:t1")?.state == .running)
        b.tick(now: t0 + 3601)
        #expect(b.watcher("b:s1:t1")?.state == .lostTouch)
    }

    @Test func passedAndFinishedFadeFailedStays() {
        var b = WatcherBoard()
        b.apply(start("p"), now: t0)
        b.apply(start("f"), now: t0)
        b.apply(.end(id: "p", result: .passed, note: nil), now: t0 + 10)
        b.apply(.end(id: "f", result: .failed, note: nil), now: t0 + 10)
        #expect(b.nextChange() == t0 + 16)
        let r10 = b.tick(now: t0 + 15.9)
        #expect(!r10)
        let r11 = b.tick(now: t0 + 16)
        #expect(r11)
        #expect(b.watchers.map(\.id) == ["a:f"])
        b.tick(now: t0 + 86_400)
        #expect(b.watchers.map(\.id) == ["a:f"])
        #expect(b.nextChange() == nil)
        b.dismiss("a:f")
        #expect(b.watchers.isEmpty)
    }

    @Test func dismissAllAndClearBySource() {
        var b = WatcherBoard()
        b.apply(start("w1"), now: t0)
        b.applyStop(StopReport(session: "s", folder: nil, tasks: [task("t")]), work: false, now: t0)
        b.clear(.agent)
        #expect(b.watchers.map(\.source) == [.automatic])
        b.dismissAll()
        #expect(b.watchers.isEmpty)
    }

    @Test func capacityDropsTheLeastUrgentOldest() {
        var b = WatcherBoard()
        b.apply(start("first"), now: t0)
        b.apply(.update(id: "first", done: nil, note: nil, state: .waiting), now: t0)
        for i in 0..<WatcherBoard.capacity { b.apply(start("w\(i)"), now: t0 + Double(i + 1)) }
        #expect(b.watchers.count == WatcherBoard.capacity)
        #expect(b.watcher("a:first") != nil)         // waiting is the most urgent: kept
        #expect(b.watcher("a:w0") == nil)            // the oldest running one went
    }

    @Test func demoHidesWorkWatchersOnly() {
        var b = WatcherBoard()
        b.apply(start("home"), now: t0)
        b.apply(start("job", work: true), now: t0)
        #expect(WatcherBoard.shown(b.ordered(), demo: false).count == 2)
        #expect(WatcherBoard.shown(b.ordered(), demo: true).map(\.id) == ["a:home"])
    }

    // MARK: Automatic watchers from Stop

    private func fixture(_ name: String) throws -> Data {
        let url = try #require(Bundle.module.url(forResource: name, withExtension: "json", subdirectory: "Fixtures/watchers"))
        return try Data(contentsOf: url)
    }

    @Test func stopHookInputParsesToTheFewFieldsKept() throws {
        let r = try #require(WatcherRequest.decodeStop(try fixture("stop-in-flight")))
        #expect(r.session == "6f1d2c3b-0a9e-4b8c-9d7e-112233445566")
        #expect(r.folder == nil)   // a Stop hook input names no folder; the hook's report does
        #expect(r.tasks.map(\.id) == ["bash_1", "agent_7", "mon_2", "wf_3"])
        #expect(r.tasks.map(\.title) == ["Run the full test suite", "Review the auth changes", "Watch the deploy log", "release-train"])
        #expect(r.tasks.map(\.note) == ["background shell", "background subagent", "background monitor", "workflow release-train"])
        #expect(r.tasks.map(\.state) == [.running, .running, .running, .running])
        // Nothing of the command or the conversation survives the parse.
        let kept = "\(r)"
        #expect(!kept.contains("SECRET"))
        #expect(!kept.contains("npm test"))
    }

    @Test func aTaskThatLeavesTheListEndsAsFinished() throws {
        var b = WatcherBoard()
        let first = try #require(WatcherRequest.decodeStop(try fixture("stop-in-flight")))
        let r12 = b.applyStop(first, work: false, now: t0)
        #expect(r12)
        #expect(b.watchers.count == 4)
        #expect(b.watchers.allSatisfy { $0.source == .automatic && $0.state == .running })
        let second = try #require(WatcherRequest.decodeStop(try fixture("stop-one-left")))
        let r13 = b.applyStop(second, work: false, now: t0 + 30)
        #expect(r13)
        let s = "6f1d2c3b-0a9e-4b8c-9d7e-112233445566"
        #expect(b.watcher("b:\(s):bash_1")?.state == .running)
        #expect(b.watcher("b:\(s):bash_1")?.touched == t0 + 30)
        #expect(b.watcher("b:\(s):agent_7")?.state == .finished)
        #expect(b.watcher("b:\(s):wf_3")?.ended == t0 + 30)
        // Another session's Stop leaves this one alone.
        b.applyStop(StopReport(session: "other", folder: nil, tasks: []), work: false, now: t0 + 31)
        #expect(b.watcher("b:\(s):bash_1")?.state == .running)
        let idle = try #require(WatcherRequest.decodeStop(try fixture("stop-idle")))
        b.applyStop(idle, work: false, now: t0 + 40)
        #expect(b.watcher("b:\(s):bash_1")?.state == .finished)
        // Finished fades like passed.
        b.tick(now: t0 + 46)
        #expect(b.watchers.isEmpty)
    }

    @Test func endedStatusesInTheListEndTheWatcher() {
        var b = WatcherBoard()
        b.applyStop(StopReport(session: "s", folder: nil, tasks: [task("a"), task("b")]), work: true, now: t0)
        #expect(b.watchers.allSatisfy { $0.work })
        b.applyStop(StopReport(session: "s", folder: nil, tasks: [task("a", "failed"), task("b", "killed")]),
                    work: false, now: t0 + 5)
        #expect(b.watcher("b:s:a")?.state == .failed)
        #expect(b.watcher("b:s:b")?.state == .finished)
        // A task first seen already ended is not put up.
        b.applyStop(StopReport(session: "s", folder: nil, tasks: [task("c", "completed")]), work: false, now: t0 + 6)
        #expect(b.watcher("b:s:c") == nil)
    }

    @Test func stopReportShapeAndLimits() {
        let long = String(repeating: "x", count: 300)
        let json = """
        {"schema_version":1,"session":"s-1","folder":"/Users/x/.claude-work","tasks":[
          {"id":"t1","type":"shell","status":"running","description":"\(long)","name":null},
          {"id":"bad id with spaces","type":"shell","status":"running","description":"x"},
          {"id":"t2","type":"workflow","status":"pending","description":"","name":"deploy"},
          "not an object"]}
        """
        let r = WatcherRequest.decodeStop(Data(json.utf8))
        #expect(r?.folder == "/Users/x/.claude-work")
        #expect(r?.tasks.map(\.id) == ["t1", "t2"])
        #expect(r?.tasks.first?.title.count == WatcherLimits.title)
        #expect(r?.tasks.first?.title.hasSuffix("…") == true)
        #expect(WatcherRequest.decodeStop(Data(#"{"schema_version":2,"session":"s","tasks":[]}"#.utf8)) == nil)
        #expect(WatcherRequest.decodeStop(Data(#"{"tasks":[]}"#.utf8)) == nil)
        #expect(WatcherRequest.decodeStop(Data(#"{"session":"s","folder":"relative/path","tasks":[]}"#.utf8))?.folder == nil)
        let many = (0..<30).map { #"{"id":"t\#($0)","type":"shell","status":"running","description":"d"}"# }
        let big = WatcherRequest.decodeStop(Data(#"{"session":"s","tasks":[\#(many.joined(separator: ","))]}"#.utf8))
        #expect(big?.tasks.count == WatcherBoard.tasksPerStop)
    }

    /// The installed Stop hook's extractor, run for real with osascript on a fixture: only the
    /// session, the folder and the few task fields reach the file, owner-only, never the command
    /// or the conversation.
    @Test func theHooksExtractorWritesOnlyTheMinimalReport() throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("sanduhr-watch-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: dir) }
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/usr/bin/osascript")
        p.arguments = ["-l", "JavaScript", "-e", IntegrationInstaller.stopTasksScript, dir.path]
        p.environment = ["CLAUDE_CONFIG_DIR": "/Users/someone/.claude-work", "PATH": "/usr/bin:/bin"]
        let input = Pipe()
        p.standardInput = input
        p.standardOutput = FileHandle.nullDevice
        p.standardError = FileHandle.nullDevice
        try p.run()
        input.fileHandleForWriting.write(try fixture("stop-in-flight"))
        try input.fileHandleForWriting.close()
        p.waitUntilExit()
        #expect(p.terminationStatus == 0)
        let names = try FileManager.default.contentsOfDirectory(atPath: dir.path)
        #expect(names.count == 1)
        let name = try #require(names.first)
        #expect(name.hasPrefix(WatcherStore.stopPrefix) && name.hasSuffix(".json"))
        let path = dir.appendingPathComponent(name).path
        let mode = (try FileManager.default.attributesOfItem(atPath: path)[.posixPermissions] as? NSNumber)?.intValue
        #expect(mode == 0o600)
        let data = try Data(contentsOf: URL(fileURLWithPath: path))
        let text = String(decoding: data, as: UTF8.self)
        for secret in ["SECRET", "npm test", "command", "transcript", "last_assistant", "code-reviewer", "/Users/someone/app"] {
            #expect(!text.contains(secret), "leaked \(secret)")
        }
        let r = try #require(WatcherRequest.decodeStop(data))
        #expect(r.folder == "/Users/someone/.claude-work")
        #expect(r.tasks.count == 4)
        #expect(r.session == "6f1d2c3b-0a9e-4b8c-9d7e-112233445566")
    }

    // MARK: Agent requests

    @Test func requestsRoundTripAndAreCheckedAgain() {
        let link = URL(string: "https://github.com/o/r/actions/runs/1")
        for command in [start("w0123456789ab", total: 12, link: link, work: true),
                        .update(id: "w0123456789ab", done: 3, note: "lint", state: .waiting),
                        .end(id: "w0123456789ab", result: .failed, note: "2 failed")] as [WatcherCommand] {
            let d = WatcherRequest.decode(WatcherRequest.json(command, folder: "/f", at: t0), now: t0 + 1)
            #expect(d?.command == command)
        }
        #expect(WatcherRequest.decode(WatcherRequest.json(start("w1"), folder: "/f", at: t0), now: t0)?.folder == "/f")
        // Stale, from the future, or broken: dropped.
        #expect(WatcherRequest.decode(WatcherRequest.json(start("w1"), at: t0), now: t0 + 601) == nil)
        #expect(WatcherRequest.decode(WatcherRequest.json(start("w1"), at: t0 + 120), now: t0) == nil)
        #expect(WatcherRequest.decode(Data("nope".utf8), now: t0) == nil)
        func raw(_ o: [String: Any]) -> Data {
            var all: [String: Any] = ["schema_version": 1, "requested_at": HandoffFiles.stamp(t0), "id": "w1"]
            all.merge(o) { $1 }
            return try! JSONSerialization.data(withJSONObject: all)
        }
        #expect(WatcherRequest.decode(raw(["op": "start", "title": "  "]), now: t0) == nil)
        #expect(WatcherRequest.decode(raw(["op": "explode", "title": "x"]), now: t0) == nil)
        #expect(WatcherRequest.decode(raw(["op": "update", "state": "passed"]), now: t0) == nil)
        #expect(WatcherRequest.decode(raw(["op": "end", "result": "finished"]), now: t0) == nil)
        #expect(WatcherRequest.decode(raw(["op": "start", "title": "x", "id": "../etc"]), now: t0) == nil)
        #expect(WatcherRequest.decode(raw(["op": "start", "title": "x", "schema_version": 2]), now: t0) == nil)
        // A bad link or total is dropped, the card still goes up; a long title is clipped.
        let d = WatcherRequest.decode(raw(["op": "start", "title": String(repeating: "t", count: 200),
                                           "link": "http://example.com", "total": 0]), now: t0)
        guard case let .start(_, title, link2, total, work, _)? = d?.command else {
            Issue.record("no start")
            return
        }
        #expect(title.count == WatcherLimits.title && link2 == nil && total == nil && !work)
        #expect(WatcherRequest.decode(raw(["op": "start", "title": "x", "total": true]), now: t0)?.command
                == start("w1", title: "x"))
    }

    @Test func linksAreHttpsOnly() {
        #expect(WatcherLimits.link("https://example.com/x?y=1") != nil)
        for bad in ["http://example.com", "javascript:alert(1)", "file:///etc/passwd", "https://", "https://u:p@example.com",
                    "sanduhr://debug/x", "https://" + String(repeating: "a", count: 2100)] {
            #expect(WatcherLimits.link(bad) == nil, "\(bad.prefix(30))")
        }
        #expect(WatcherLimits.line("a\nb\tc", cap: 10) == "a b c")
        #expect(WatcherLimits.line("   ", cap: 10) == nil)
        #expect(WatcherLimits.line("abcdefghijk", cap: 5) == "abcd…")
    }

    // MARK: The store's file handoff

    @MainActor
    @Test func theStoreReadsAndDeletesFilesAndHonorsTheSwitches() throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("sanduhr-watch-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: dir) }
        var agents = false
        var background = false
        var glows = 0
        var changes = 0
        let now = t0
        let store = WatcherStore(support: dir, agents: { agents }, background: { background },
                                 isWork: { $0 == "/Users/x/.claude-work" }, now: { now })
        store.onWaiting = { _ in glows += 1 }
        store.onChange = { changes += 1 }
        store.applySwitches()
        #expect(try Data(contentsOf: dir.appendingPathComponent("watchers.json"))
                == WatcherStore.switchesJSON(agents: false, background: false))

        func drop(_ name: String, _ data: Data) {
            FileManager.default.createFile(atPath: dir.appendingPathComponent(name).path, contents: data)
        }
        let stop = Data(#"{"schema_version":1,"session":"s","folder":"/Users/x/.claude-work","tasks":[{"id":"t","type":"shell","status":"running","description":"Build"}]}"#.utf8)
        drop("watch-request-0000000000001-000001-w1.json",
             WatcherRequest.json(start("w1"), folder: "/Users/x/.claude-work", at: now))
        drop("watch-stop-0000000000001-x.json", stop)
        store.check()
        // Off: both deleted unread.
        #expect(store.board.watchers.isEmpty)
        #expect(try FileManager.default.contentsOfDirectory(atPath: dir.path) == ["watchers.json"])

        agents = true
        background = true
        store.applySwitches()
        #expect(try Data(contentsOf: dir.appendingPathComponent("watchers.json"))
                == WatcherStore.switchesJSON(agents: true, background: true))
        drop("watch-request-0000000000002-000002-w1.json", WatcherRequest.json(start("w1"), folder: "/Users/x/.claude-work", at: now))
        drop("watch-request-0000000000003-000003-w1.json",
             WatcherRequest.json(.update(id: "w1", done: nil, note: nil, state: .waiting), at: now))
        drop("watch-request-0000000000004-000004-w2.json", WatcherRequest.json(start("w2"), folder: "/elsewhere", at: now))
        drop("watch-stop-0000000000005-y.json", stop)
        drop(".watch-request-half.json.tmp", Data("{".utf8))
        store.check()
        #expect(store.board.watchers.count == 3)
        #expect(store.board.watcher("a:w1")?.state == .waiting)
        #expect(store.board.watcher("a:w1")?.work == true)      // linked to a work account
        #expect(store.board.watcher("a:w2")?.work == false)
        #expect(store.board.watcher("b:s:t")?.work == true)
        #expect(glows == 1)
        #expect(store.ordered.first?.id == "a:w1")
        // Read files are gone; a half-written temporary is left for its writer.
        #expect(try FileManager.default.contentsOfDirectory(atPath: dir.path).sorted()
                == [".watch-request-half.json.tmp", "watchers.json"])

        // Turning a switch off clears its watchers.
        background = false
        store.applySwitches()
        #expect(store.board.watchers.allSatisfy { $0.source == .agent })
        store.dismiss("a:w2")
        #expect(store.board.watchers.map(\.id) == ["a:w1"])
        store.dismissAll()
        #expect(store.board.watchers.isEmpty)
        #expect(changes > 0)
    }

    /// A Stop's report written long before Sanduhr read it (no Sanduhr ran then) is the
    /// session's state from back then: deleted unread. A fresh one is read.
    @MainActor
    @Test func aStaleStopReportIsDeletedUnread() throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("sanduhr-watch-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: dir) }
        let now = Date()
        let store = WatcherStore(support: dir, agents: { false }, background: { true }, now: { now })
        let stop = Data(#"{"schema_version":1,"session":"s","folder":null,"tasks":[{"id":"t","type":"shell","status":"running","description":"Build"}]}"#.utf8)
        let old = dir.appendingPathComponent("watch-stop-0000000000001-old.json")
        FileManager.default.createFile(atPath: old.path, contents: stop)
        try FileManager.default.setAttributes([.modificationDate: now.addingTimeInterval(-WatcherRequest.maxAge - 60)],
                                              ofItemAtPath: old.path)
        #expect(WatcherStore.isStale(old, now: now))
        store.check()
        #expect(store.board.watchers.isEmpty)
        #expect(!FileManager.default.fileExists(atPath: old.path))
        let fresh = dir.appendingPathComponent("watch-stop-0000000000002-new.json")
        FileManager.default.createFile(atPath: fresh.path, contents: stop)
        #expect(!WatcherStore.isStale(fresh, now: now))
        store.check()
        #expect(store.board.watcher("b:s:t") != nil)
        #expect(!FileManager.default.fileExists(atPath: fresh.path))
    }

    @MainActor
    @Test func theDebugWatcherGoesThroughTheSameDecoding() throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("sanduhr-watch-\(UUID().uuidString)")
        defer { try? FileManager.default.removeItem(at: dir) }
        let store = WatcherStore(support: dir, agents: { false }, background: { false })
        store.debug(.start)
        #expect(store.board.watcher("a:\(WatcherStore.testID)")?.link?.absoluteString == "https://example.com")
        store.debug(.wait)
        #expect(store.board.watcher("a:\(WatcherStore.testID)")?.state == .waiting)
        store.debug(.fail)
        #expect(store.board.watcher("a:\(WatcherStore.testID)")?.state == .failed)
        store.debug(.clear)
        #expect(store.board.watchers.isEmpty)
        guard case .success(.watchTest(.pass)) = DebugLink.action("watch-test", arg: "PASS") else {
            Issue.record("watch-test arg=pass should parse")
            return
        }
        guard case .failure = DebugLink.action("watch-test", arg: nil) else {
            Issue.record("watch-test without arg should fail")
            return
        }
    }

    @Test func workAccountsTagTheirFolder() {
        let d = MemoryDefaults()
        let home = "/Users/x"
        AccountData.link("/Users/x/.claude-work", to: "Work", in: d)
        AccountData.link("/Users/x/.claude", to: "Home", in: d)
        #expect(!WatcherStore.isWorkFolder("/Users/x/.claude-work", home: home, in: d))
        AccountData.setWork(true, for: "Work", in: d)
        #expect(AccountData.choices(for: "Work", in: d).work)
        #expect(WatcherStore.isWorkFolder("/Users/x/.claude-work", home: home, in: d))
        #expect(WatcherStore.isWorkFolder("/Users/x/.claude-work/", home: home, in: d))
        #expect(!WatcherStore.isWorkFolder(nil, home: home, in: d))   // the default folder is Home's
        #expect(!WatcherStore.isWorkFolder("/Users/x/.claude-other", home: home, in: d))
        AccountData.setWork(false, for: "Work", in: d)
        #expect(!WatcherStore.isWorkFolder("/Users/x/.claude-work", home: home, in: d))
    }

    // MARK: Placement and drawing

    private func watchers(_ n: Int) -> [Watcher] {
        var b = WatcherBoard()
        for i in 0..<n { b.apply(start("w\(i)", title: "Deploy the production cluster \(i)", total: 10), now: t0 + Double(i)) }
        return b.ordered()
    }

    @Test func notchPlacesShowTheMostUrgentOrStandAside() {
        for place in [NotchContent.Place.left, .right, .strip] {
            #expect(NotchContent.effective(.watchers, at: place, hasLine: false, idle: .automatic, hasWatcher: false) == place.fallback)
            #expect(NotchContent.effective(.watchers, at: place, hasLine: false, idle: .time, hasWatcher: true) == .watchers)
            #expect(NotchContent.effective(.watchers, at: place, nowPlaying: nil, idle: .time, watchers: watchers(1)) == .watchers)
            #expect(NotchContent.effective(.watchers, at: place, nowPlaying: nil, idle: .time, watchers: []) == place.fallback)
        }
        #expect(NotchContent.watchers.label == "Watchers")
        let three = watchers(3)
        let now = t0 + 245
        // The newest is first among equals; the wing clips the title, the strip keeps more of it.
        // At rest the short line; during the intro the full one, on the wings and the strip alike.
        #expect(NotchContent.watchers.text(at: .right, meetings: [], meters: nil, message: nil, watchers: three, now: now)
                == "Deploy · 0/10 +2")
        #expect(NotchContent.watchers.text(at: .strip, meetings: [], meters: nil, message: nil, watchers: three,
                                           watcherIntro: true, now: now)
                == "Deploy the production cluster 2 · 4m · 0/10 +2")
        #expect(NotchContent.watchers.text(at: .left, meetings: [], meters: nil, message: nil, watchers: [], now: now) == nil)
    }

    @Test func elapsedReadsShort() {
        #expect(WatcherText.elapsed(12.7) == "12s")
        #expect(WatcherText.elapsed(240) == "4m")
        #expect(WatcherText.elapsed(3600 + 5 * 60) == "1h 05m")
        var w = watchers(1)[0]
        #expect(WatcherText.deskNote(w) == nil)
        w.state = .waiting
        #expect(WatcherText.deskNote(w) == "waiting on you")
        w.note = "needs a look"
        #expect(WatcherText.deskNote(w) == "needs a look")
        #expect(WatcherText.spoken(w, now: t0 + 60) == "Deploy the production cluster 0, waiting on you, 1m, 0/10, needs a look")
    }

    @Test func placementsAndTheLayoutElement() {
        #expect(DeskLayout.widgets.contains { $0.key == "watchers" && $0.name == "Watchers" })
        #expect(DeskLayout.placed("watchers:tr clock:bl").contains("watchers"))
        #expect(WatcherPlacement.places(notch: true, wingText: true, chinText: true, chin: 26, left: .watchers,
                                        right: .meters, strip: .watchers, layoutPlaced: ["watchers"])
                == ["left", "strip", "desk"])
        // The island off or its text off: the notch places don't count.
        #expect(WatcherPlacement.places(notch: false, wingText: true, chinText: true, chin: 26, left: .watchers,
                                        right: .watchers, strip: .watchers, layoutPlaced: []) == [])
        #expect(WatcherPlacement.places(notch: true, wingText: false, chinText: true, chin: 0, left: .watchers,
                                        right: .watchers, strip: .watchers, layoutPlaced: []) == [])
    }

    @Test func deskRowsAndTheStripAreClickAreas() {
        let shown = watchers(5)
        var input = DeskElements.Input()
        input.placed = ["watchers"]
        input.watcherRows = shown.prefix(WatcherPlacement.deskRows).map(\.id)
        for (i, w) in shown.prefix(4).enumerated() {
            input.watcherRowFrames[w.id] = CGRect(x: 60, y: 100 + CGFloat(i) * 40, width: 300, height: 30)
        }
        input.watcherStrip = true
        input.stripWatcherFrame = CGRect(x: 700, y: 34, width: 200, height: 24)
        let elements = DeskElements.build(input)
        let rows = elements.filter { $0.kind == .watcher }
        #expect(rows.map(\.key) == ["0", "1", "2", "3", "strip"])
        #expect(DeskFrameCheck.problem(elements, window: CGSize(width: 1512, height: 982)) == nil)
        let hit = DeskHitTest.element(at: CGPoint(x: 100, y: 145), in: elements)
        #expect(hit?.kind == .watcher && hit?.key == "1")
        #expect(DeskHitTest.hasMenu(hit))
        #expect(DeskHitTest.watcher(hit, in: shown)?.id == shown[1].id)
        let strip = DeskHitTest.element(at: CGPoint(x: 750, y: 40), in: elements)
        #expect(DeskHitTest.watcher(strip, in: shown)?.id == shown[0].id)
        #expect(DeskHitTest.watcher(DeskElement(kind: .watcher, key: "9", frame: .zero), in: shown) == nil)
        #expect(DeskHitTest.watcher(DeskElement(kind: .meters, key: "0", frame: .zero), in: shown) == nil)
        // Not placed: no rows.
        #expect(!DeskElements.build(DeskElements.Input()).contains { $0.kind == .watcher })
    }

    @Test func stateYAMLCountsStatesAndPlacesOnly() {
        let w = WatchersDebug(count: 2, states: [.waiting, .running], placements: ["right", "desk"],
                              agents: true, background: false, intro: true)
        let yaml = YAMLEmitter.emit(.object([("watchers", DebugState.watchersYAML(w))]))
        #expect(yaml == "watchers:\n  count: 2\n  states:\n    - waiting\n    - running\n  placements:\n    - right\n    - desk\n  agents: true\n  background: false\n  intro: true\n")
    }
}

/// The watchers' state marks next to the camera indicator (item 67): the camera's red dot is the
/// only red dot, so failed draws a red triangle, and no dot is red.
@Suite("Watcher marks")
struct WatcherMarkTests {
    static let states: [WatcherState] = [.running, .waiting, .passed, .failed, .finished, .lostTouch]

    @Test func failedIsATriangleTheRestAreDots() {
        #expect(WatcherLook.mark(.failed) == .triangle)
        #expect(WatcherLook.mark(.passed) == .check)
        for state in [WatcherState.running, .waiting, .finished, .lostTouch] {
            #expect(WatcherLook.mark(state) == .dot)
        }
    }

    /// Red: the red channel well above both others.
    static func isRed(_ hex: String) -> Bool {
        let v = Int(hex, radix: 16) ?? 0
        let r = (v >> 16) & 0xff, g = (v >> 8) & 0xff, b = v & 0xff
        return r > 180 && g < 140 && b < 140
    }

    @Test func noWatcherDrawsARedDot() {
        for state in Self.states where WatcherLook.mark(state) != .triangle {
            #expect(!Self.isRed(WatcherLook.hex(state)), "\(state) must not be red")
        }
        // Waiting on you is amber, failed's triangle red, like the camera dot.
        #expect(WatcherLook.hex(.waiting) == "fbbf24")
        #expect(Self.isRed(WatcherLook.hex(.failed)))
        #expect(Self.isRed("ff3b30"))
    }

    @Test func voiceOverSaysFailed() {
        var b = WatcherBoard()
        let t0 = Date(timeIntervalSince1970: 1_000)
        b.apply(.start(id: "f", title: "Deploy", link: nil, total: nil, work: false, short: nil), now: t0)
        b.apply(.end(id: "f", result: .failed, note: nil), now: t0 + 10)
        guard let w = b.ordered().first else { Issue.record("no watcher"); return }
        #expect(WatcherText.spoken(w, now: t0 + 10).contains("failed"))
        #expect(WatcherState.failed.label == "failed")
    }
}
