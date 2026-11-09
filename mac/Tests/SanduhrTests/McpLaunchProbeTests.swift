import Foundation
import Testing
@testable import Sanduhr

/// #144 on the Mac (port of Windows #470): after an install, the registered command is started the way Claude Code starts
/// it and must answer `initialize` as Sanduhr. The fake servers are small shell scripts in a temp folder.
@Suite("MCP launch probe", .serialized)
struct McpLaunchProbeTests {
    private final class Scratch {
        let dir: URL
        init() {
            dir = FileManager.default.temporaryDirectory.appendingPathComponent("sanduhr-mcp-probe-\(UUID().uuidString)")
            try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        }
        deinit { try? FileManager.default.removeItem(at: dir) }

        /// An executable script that reads its input and prints `body`'s output.
        func server(_ body: String) -> String {
            let path = dir.appendingPathComponent("server-\(UUID().uuidString).sh").path
            try? ("#!/bin/sh\n" + body + "\n").write(toFile: path, atomically: true, encoding: .utf8)
            chmod(path, 0o755)
            return path
        }
    }

    private static let sanduhrAnswer =
        #"{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-06-18","serverInfo":{"name":"sanduhr","version":"9.9.9"}}}"#

    @Test func aServerThatAnswersAsSanduhrIsOkWithItsVersion() {
        let s = Scratch()
        let server = s.server("read line; echo '\(Self.sanduhrAnswer)'")
        #expect(McpLaunchProbe.check(command: server, args: [], timeout: 10) == .answered("9.9.9"))
    }

    @Test func aNotificationBeforeTheAnswerIsReadPast() {
        let s = Scratch()
        let server = s.server(#"read line; echo '{"jsonrpc":"2.0","method":"notifications/message","params":{}}'; echo '\#(Self.sanduhrAnswer)'"#)
        #expect(McpLaunchProbe.check(command: server, args: [], timeout: 10).ok)
    }

    @Test func aDifferentServerAnsweringIsNotOk() {
        let s = Scratch()
        let server = s.server(#"read line; echo '{"jsonrpc":"2.0","id":1,"result":{"serverInfo":{"name":"other","version":"1"}}}'"#)
        #expect(McpLaunchProbe.check(command: server, args: [], timeout: 10) == .failed(McpLaunchProbe.wrongServer))
    }

    @Test func somethingThatIsNotMCPIsSaid() {
        let s = Scratch()
        let server = s.server("read line; echo 'Traceback (most recent call last):'")
        #expect(McpLaunchProbe.check(command: server, args: [], timeout: 10) == .failed(McpLaunchProbe.notMCP))
    }

    @Test func aServerThatStopsAtOnceSaysItsExitCode() {
        let s = Scratch()
        let server = s.server("exit 3")
        #expect(McpLaunchProbe.check(command: server, args: [], timeout: 10) == .failed(McpLaunchProbe.exited(3)))
    }

    @Test func aMissingPythonSaysSoWithoutStartingAnything() {
        let s = Scratch()
        let gone = s.dir.appendingPathComponent("python3").path
        #expect(McpLaunchProbe.check(command: gone, args: [], timeout: 10) == .failed(McpLaunchProbe.noPython))
    }

    @Test func aMissingServerFileSaysSo() {
        let s = Scratch()
        let python = s.server("echo should not run; exit 9")
        let script = s.dir.appendingPathComponent("sanduhr_mcp.py").path
        #expect(McpLaunchProbe.check(command: python, args: [script], timeout: 10) == .failed(McpLaunchProbe.noServerFile))
    }

    @Test func aServerThatNeverAnswersTimesOutAndIsStopped() {
        let s = Scratch()
        // exec: the sleep is the process the probe stops, so nothing is left running after the test.
        let sleeper = s.server("exec sleep 30")
        let start = Date()
        #expect(McpLaunchProbe.check(command: sleeper, args: [], timeout: 1) == .failed(McpLaunchProbe.noAnswer(1)))
        #expect(Date().timeIntervalSince(start) < 10, "the probe waited past its timeout")
    }

    @Test(arguments: [
        (#"{"jsonrpc":"2.0","id":1,"result":{"serverInfo":{"name":"sanduhr","version":"1.2"}}}"#, true as Bool?),
        (#"{"jsonrpc":"2.0","id":"1","result":{"serverInfo":{"name":"sanduhr"}}}"#, true),
        (#"{"jsonrpc":"2.0","id":2,"result":{}}"#, nil),
        (#"{"jsonrpc":"2.0","method":"notifications/x"}"#, nil),
        ("   ", nil),
        (#"{"jsonrpc":"2.0","id":1,"error":{"code":-32601,"message":"no"}}"#, false),
        ("[1,2,3]", false),
        ("not json", false),
    ])
    func oneLineIsJudgedAsTheAnswerOrSkipped(line: String, ok: Bool?) {
        #expect(McpLaunchProbe.answer(line)?.ok == ok)
    }

    @Test func noSentenceNamesAPathOrUsesAnEmDash() {
        let lines = [McpLaunchProbe.noPython, McpLaunchProbe.noServerFile, McpLaunchProbe.didNotStart, McpLaunchProbe.exited(1),
                     McpLaunchProbe.noAnswer(15), McpLaunchProbe.notMCP, McpLaunchProbe.wrongServer,
                     McpLaunchProbe.checking("~/.claude"), McpLaunchProbe.answering("~/.claude", version: "1"),
                     McpLaunchProbe.answering("~/.claude", version: nil),
                     McpLaunchProbe.notAnswering("~/.claude", reason: McpLaunchProbe.noPython)]
        for line in lines {
            #expect(!line.contains("/Users/") && !line.contains("\u{2014}"), "\(line)")
        }
        #expect(McpLaunchProbe.answering("~/.claude", version: "0.5.0-mac")
                == "MCP server installed for ~/.claude and answering (version 0.5.0-mac). Claude Code sessions started from now on use it.")
    }

    @Test @MainActor func settingsSaysWhatTheCheckFound() async {
        let model = IntegrationsModel()
        defer { McpLaunchProbe.override = nil }
        McpLaunchProbe.override = { _, args in
            #expect(args.count == 1 && args[0].hasSuffix("/sanduhr_mcp.py"))
            return .answered("0.5.0-mac")
        }
        await model.checkMCPStarts(folder: NSHomeDirectory() + "/.claude", python: "/usr/bin/python3")
        #expect(model.note?.isError == false)
        #expect(model.note?.text.contains("and answering (version 0.5.0-mac)") == true)

        McpLaunchProbe.override = { _, _ in .failed(McpLaunchProbe.noPython) }
        await model.checkMCPStarts(folder: NSHomeDirectory() + "/.claude", python: "/usr/bin/python3")
        #expect(model.note?.isError == true)
        #expect(model.note?.text.contains("but it doesn't start: the Python 3 it runs is missing.") == true)
    }

    // MARK: - Check on an installed row (2.42.3)

    @Test func theStubWithNoDeveloperToolsBehindItIsNeverStarted() {
        guard FileManager.default.isExecutableFile(atPath: PythonFinder.systemStub) else { return }
        // Were it started, the stub would open Apple's install dialog; the probe says Python is missing instead.
        #expect(McpLaunchProbe.check(command: PythonFinder.systemStub, args: [], timeout: 1, stubUsable: { false })
                == .failed(McpLaunchProbe.noPython))
    }

    @Test func aUsableStubIsNotStoppedByTheGuard() {
        // A usable stub passes the guard; the server file check after it answers here, before anything starts.
        guard FileManager.default.isExecutableFile(atPath: PythonFinder.systemStub) else { return }
        let gone = FileManager.default.temporaryDirectory.appendingPathComponent("no-\(UUID().uuidString)/sanduhr_mcp.py").path
        #expect(McpLaunchProbe.check(command: PythonFinder.systemStub, args: [gone], timeout: 1, stubUsable: { true })
                == .failed(McpLaunchProbe.noServerFile))
    }

    @Test func theStubIsUsableOnlyWhenTheDeveloperFolderHoldsAPython() {
        let stub = PythonFinder.systemStub
        let cltPython = "/Library/Developer/CommandLineTools/usr/bin/python3"
        #expect(PythonFinder.stubUsable(fileExists: { $0 == stub || $0 == cltPython }, developerDir: nil))
        #expect(!PythonFinder.stubUsable(fileExists: { $0 == stub }, developerDir: nil))
        #expect(!PythonFinder.stubUsable(fileExists: { $0 == stub || $0 == cltPython }, developerDir: "/elsewhere"))
    }

    @Test func theInstalledSentencesSayWhatToDoForEachRow() {
        #expect(McpLaunchProbe.checkingInstalled("~/.claude") == "Checking that the MCP server for ~/.claude starts\u{2026}")
        #expect(McpLaunchProbe.answeringInstalled("~/.claude", version: "0.5.0-mac")
                == "The MCP server for ~/.claude starts and answers (version 0.5.0-mac). Claude Code sessions started from now on use it.")
        #expect(McpLaunchProbe.answeringInstalled("~/.claude", version: nil)
                == "The MCP server for ~/.claude starts and answers. Claude Code sessions started from now on use it.")
        #expect(McpLaunchProbe.notAnsweringInstalled("~/.claude", reason: McpLaunchProbe.exited(3), outdated: true)
                == "The MCP server for ~/.claude doesn't start: it stopped at once (exit code 3). Click Update to point it at this version's server.")
        #expect(McpLaunchProbe.notAnsweringInstalled("~/.claude", reason: McpLaunchProbe.notMCP, outdated: false)
                .hasSuffix("Remove it and install again, or check that Python 3 runs."))
        for line in [McpLaunchProbe.checkingInstalled("~/.claude"), McpLaunchProbe.answeringInstalled("~/.claude", version: "1"),
                     McpLaunchProbe.notAnsweringInstalled("~/.claude", reason: McpLaunchProbe.noPython, outdated: true),
                     McpLaunchProbe.notAnsweringInstalled("~/.claude", reason: McpLaunchProbe.noPython, outdated: false),
                     McpLaunchProbe.noEntry("~/.claude")] {
            #expect(!line.contains("/Users/") && !line.contains("\u{2014}"), "\(line)")
        }
    }

    /// A temp home with `~/.claude` and a `.claude.json` holding `servers`' JSON under `mcpServers`.
    private static func rig(servers: String?) -> IntegrationRig {
        let r = IntegrationRig()
        r.home.dir(".claude/projects")
        if let servers { r.home.file(".claude.json", "{\n  \"mcpServers\": \(servers)\n}\n") }
        return r
    }

    @Test func theRegisteredEntryIsReadAsTheFileHasIt() {
        let r = Self.rig(servers: #"{"sanduhr": {"type": "stdio", "command": "/somewhere/else/python3", "args": ["/x/sanduhr_mcp.py"]}}"#)
        defer { r.cleanUp() }
        // A moved Python is checked as registered, not as Install would write it now.
        let launch = r.installer.registeredMCPLaunch(folder: r.home.at(".claude"))
        #expect(launch?.command == "/somewhere/else/python3")
        #expect(launch?.args == ["/x/sanduhr_mcp.py"])
    }

    @Test(arguments: [
        nil,
        #"{"other": {"command": "node", "args": ["x.js"]}}"#,
        #"{"sanduhr": {"command": "node", "args": ["their-server.js"]}}"#,
        #"{"sanduhr": {"command": 7, "args": ["/x/sanduhr_mcp.py"]}}"#,
        #"[]"#,
    ] as [String?])
    func thereIsNoRegisteredEntryOfSanduhrsToCheck(servers: String?) {
        let r = Self.rig(servers: servers)
        defer { r.cleanUp() }
        #expect(r.installer.registeredMCPLaunch(folder: r.home.at(".claude")) == nil)
    }

    @Test func aStubEntryWithNoDeveloperToolsReadsOutdatedSoUpdateFixesIt() throws {
        let r = Self.rig(servers: nil)
        defer { r.cleanUp() }
        let folder = r.home.at(".claude")
        guard FileManager.default.isExecutableFile(atPath: PythonFinder.systemStub) else { return }
        var installer = r.installer
        #expect(try installer.install(.mcp, folder: folder, python: PythonFinder.systemStub) == .installed)
        installer.stubUsable = { true }
        #expect(installer.status(.mcp, folder: folder) == .installed)
        installer.stubUsable = { false }
        #expect(installer.status(.mcp, folder: folder) == .outdated)
    }

    @Test @MainActor func checkStartsTheRegisteredEntryAndSaysWhatItHeard() async throws {
        let r = Self.rig(servers: nil)
        defer { r.cleanUp() }
        let folder = r.home.at(".claude")
        #expect(try r.installer.install(.mcp, folder: folder, python: r.python) == .installed)
        let model = IntegrationsModel()
        model.checkInstaller = r.installer
        defer { McpLaunchProbe.override = nil }

        McpLaunchProbe.override = { command, args in
            #expect(command == r.python)
            #expect(args == [r.support.path + "/current/sanduhr_mcp.py"])
            return .answered("0.5.0-mac")
        }
        await model.checkMCP(folder: folder, linked: [])
        #expect(model.busy == nil)
        #expect(model.note?.isError == false)
        #expect(model.note?.text.contains("starts and answers (version 0.5.0-mac)") == true)

        McpLaunchProbe.override = { _, _ in .failed(McpLaunchProbe.exited(3)) }
        await model.checkMCP(folder: folder, linked: [])
        #expect(model.note?.isError == true)
        #expect(model.note?.text.contains("doesn't start: it stopped at once (exit code 3).") == true)
        // The row is Installed (the rig's python exists), so the advice is to install again, not Update.
        #expect(model.note?.text.contains("Remove it and install again") == true)
    }

    @Test @MainActor func checkGivesTheUpdateAdviceOnAnOutdatedRow() async {
        let r = Self.rig(servers: #"{"sanduhr": {"type": "stdio", "command": "/bin/echo", "args": ["/old/place/sanduhr_mcp.py"]}}"#)
        defer { r.cleanUp() }
        let model = IntegrationsModel()
        model.checkInstaller = r.installer
        defer { McpLaunchProbe.override = nil }
        McpLaunchProbe.override = { _, _ in .failed(McpLaunchProbe.noServerFile) }
        await model.checkMCP(folder: r.home.at(".claude"), linked: [])
        #expect(model.note?.text.hasSuffix("Click Update to point it at this version's server.") == true)
    }

    @Test @MainActor func checkOnAnEntryRemovedSinceTheLoadSaysThereWasNothingToCheck() async {
        let r = Self.rig(servers: "{}")
        defer { r.cleanUp() }
        let model = IntegrationsModel()
        model.checkInstaller = r.installer
        defer { McpLaunchProbe.override = nil }
        McpLaunchProbe.override = { _, _ in
            Issue.record("nothing should start")
            return .answered(nil)
        }
        await model.checkMCP(folder: r.home.at(".claude"), linked: [])
        #expect(model.note?.isError == true)
        #expect(model.note?.text.contains("isn't registered for") == true)
        #expect(model.busy == nil)
    }

    /// The real server, through the real interpreter, answers as Sanduhr: the shape Claude Code runs.
    @Test func theRealServerAnswers() throws {
        let python = ["/usr/bin/python3", "/opt/homebrew/bin/python3", "/usr/local/bin/python3"]
            .first { FileManager.default.isExecutableFile(atPath: $0) }
        let script = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent()
            .deletingLastPathComponent().appendingPathComponent("integrations/sanduhr_mcp.py").path
        guard let python, FileManager.default.fileExists(atPath: script) else { return }
        let result = McpLaunchProbe.check(command: python, args: [script], timeout: 30)
        #expect(result.ok, "\(result)")
        #expect(result.version?.hasSuffix("-mac") == true)
    }

    /// The same, read back through a registered entry: what Check starts.
    @Test func theRealServerAnswersThroughARegisteredEntry() throws {
        let python = ["/opt/homebrew/bin/python3", "/usr/local/bin/python3", "/usr/bin/python3"]
            .first { FileManager.default.isExecutableFile(atPath: $0) && ($0 != PythonFinder.systemStub || PythonFinder.stubUsable()) }
        let script = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent()
            .deletingLastPathComponent().appendingPathComponent("integrations/sanduhr_mcp.py").path
        guard let python, FileManager.default.fileExists(atPath: script) else { return }
        let entry = try JSONSerialization.data(withJSONObject: ["sanduhr": ["type": "stdio", "command": python, "args": [script]]])
        let r = Self.rig(servers: String(decoding: entry, as: UTF8.self))
        defer { r.cleanUp() }
        let launch = try #require(r.installer.registeredMCPLaunch(folder: r.home.at(".claude")))
        let result = McpLaunchProbe.check(command: launch.command, args: launch.args, timeout: 30)
        #expect(result.ok, "\(result)")
    }
}
