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
}
