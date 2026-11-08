import Foundation

/// What starting the installed MCP server the way Claude Code does showed.
struct McpProbeResult: Equatable, Sendable {
    /// The server started and answered `initialize` as Sanduhr.
    var ok: Bool
    /// The server's version from that answer, when it gave one.
    var version: String?
    /// Why it is not OK, in words for the status line (no paths).
    var reason: String?

    static func answered(_ version: String?) -> McpProbeResult { McpProbeResult(ok: true, version: version, reason: nil) }
    static func failed(_ reason: String) -> McpProbeResult { McpProbeResult(ok: false, version: nil, reason: reason) }
}

/// A registration in `.claude.json` proves nothing (#144, the Mac port of Windows #470). A command that can't start fails
/// at spawn, before any handshake, so Claude Code lists the server neither as connected nor as failed, and a session reads
/// that as "Sanduhr has no MCP". After an install this runs the registered command exactly as Claude Code will (the
/// `command` and `args` of the `sanduhr` entry, stdio piped), sends one `initialize`, and judges the reply, so a server that
/// can't start is reported the moment it is installed. The server is Sanduhr's own, local, with no network; it reads to the
/// end of its input, answers, and exits, and `ProcessRunner` stops it if it doesn't.
enum McpLaunchProbe {
    static let defaultTimeout: TimeInterval = 15

    /// Test seam: replaces the real check (tests of the install flow never start a process).
    static var override: ((_ command: String, _ args: [String]) -> McpProbeResult)?

    static let initialize = #"{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","#
        + #""capabilities":{},"clientInfo":{"name":"sanduhr-install-check","version":"1"}}}"#

    // The status line after an install. `place` is the folder as Settings shows it.
    static func checking(_ place: String) -> String { "MCP server installed for \(place). Checking that it starts\u{2026}" }
    static func answering(_ place: String, version: String?) -> String {
        let v = version.map { $0.isEmpty ? "" : " (version \($0))" } ?? ""
        return "MCP server installed for \(place) and answering\(v). Claude Code sessions started from now on use it."
    }
    static func notAnswering(_ place: String, reason: String) -> String {
        "MCP server installed for \(place), but it doesn't start: \(reason). Claude Code won't see it until this is fixed. "
            + "Install again, or check that Python 3 runs."
    }

    // The reasons, in the status line's words. None names a path.
    static let noPython = "the Python 3 it runs is missing"
    static let noServerFile = "the server file it runs is missing"
    static let didNotStart = "macOS couldn't start it"
    static func exited(_ code: Int32) -> String { "it stopped at once (exit code \(code))" }
    static func noAnswer(_ seconds: TimeInterval) -> String { "it didn't answer within \(Int(seconds)) seconds" }
    static let notMCP = "it answered with something that isn't MCP"
    static let wrongServer = "a different server answered"

    static func check(command: String, args: [String]) -> McpProbeResult {
        if let fake = override { return fake(command, args) }
        return check(command: command, args: args, timeout: defaultTimeout)
    }

    static func check(command: String, args: [String], timeout: TimeInterval) -> McpProbeResult {
        let fm = FileManager.default
        guard fm.isExecutableFile(atPath: command) else { return .failed(noPython) }
        if let script = args.first, script.hasSuffix(".py"), !fm.fileExists(atPath: script) { return .failed(noServerFile) }

        // network: local (Sanduhr's own MCP server on this Mac; the server has no network by contract)
        let run = ProcessRunner.run(command, args, stdin: Data((initialize + "\n").utf8), timeout: timeout)
        if run.launchFailed { return .failed(didNotStart) }
        if run.timedOut { return .failed(noAnswer(timeout)) }
        // stderr is never shown: it can name a path.
        for line in run.stdout.split(whereSeparator: \.isNewline) {
            if let verdict = answer(String(line)) { return verdict }
            // Anything else before the answer (a notification) is not the answer: keep reading.
        }
        return .failed(exited(run.status))
    }

    /// The verdict on one stdout line: the reply to id 1, or nil when the line is something else.
    static func answer(_ line: String) -> McpProbeResult? {
        let trimmed = line.trimmingCharacters(in: .whitespaces)
        if trimmed.isEmpty { return nil }
        guard let data = trimmed.data(using: .utf8),
              let frame = try? JSONSerialization.jsonObject(with: data) else { return .failed(notMCP) }
        guard let obj = frame as? [String: Any] else { return .failed(notMCP) }
        let id: String? = (obj["id"] as? NSNumber)?.stringValue ?? obj["id"] as? String
        guard id == "1" else { return nil }
        guard let result = obj["result"] as? [String: Any], let info = result["serverInfo"] as? [String: Any],
              info["name"] as? String == "sanduhr" else { return .failed(wrongServer) }
        return .answered(info["version"] as? String)
    }
}
