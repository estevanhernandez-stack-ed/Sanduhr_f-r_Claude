import Darwin
import Foundation

/// Which seat each running Claude Code process belongs to (item 71, slice 0; open question 4).
/// Slice 3 blocks a CLI write only while a session of *that* seat runs, and slice 1's seat
/// picker shows a "running" dot.
///
/// macOS hands a process's launch arguments and environment to another process of the same user
/// through `sysctl(KERN_PROCARGS2)` (what `ps eww` prints). Checked against Claude Code 2.1.296:
/// a `claude` started with `CLAUDE_CONFIG_DIR` set shows it there. Only that one variable is
/// kept; every other variable is dropped while parsing and never stored or logged. Limits: it is
/// the environment at launch (a seat's own settings can't move it: `CLAUDE_CONFIG_DIR` is
/// ignored in a settings `env` block outside user settings, and a user settings file lives in
/// the seat already), and processes of other users or protected ones don't answer, so a caller
/// that must be safe treats "couldn't read" as "may be any seat".
enum ClaudeProcesses {
    struct Running: Equatable, Sendable {
        let pid: Int32
        /// The seat folder, standardized; `~/.claude` when `CLAUDE_CONFIG_DIR` is unset or empty.
        let seat: String
    }

    /// What one process's launch says, parsed from a `KERN_PROCARGS2` buffer. The arguments
    /// are only looked at to tell Claude Code apart, then dropped; of the environment, only
    /// `CLAUDE_CONFIG_DIR` is kept.
    struct Parsed: Equatable, Sendable {
        let executable: String
        let isClaudeCode: Bool
        /// `CLAUDE_CONFIG_DIR` as launched, nil when unset or empty.
        let configDir: String?
    }

    /// Parses a `KERN_PROCARGS2` buffer: argc (4 bytes, host order), the executable path with
    /// NUL padding to the next 8 bytes (measured over every process of a user on macOS 26: the
    /// padding always ends there, so an empty argv[0] after it isn't swallowed), argc arguments,
    /// then the environment, each NUL-terminated. Nil for a buffer that isn't that shape: too
    /// short, a huge argc, a string with no NUL, padding that isn't NUL.
    static func parse(_ buffer: [UInt8]) -> Parsed? {
        guard buffer.count >= 4 else { return nil }
        let argc = buffer.withUnsafeBytes { Int($0.loadUnaligned(as: Int32.self)) }
        guard argc >= 0, argc < 1 << 16 else { return nil }
        var i = 4
        func string() -> String? {
            let start = i
            while i < buffer.count, buffer[i] != 0 { i += 1 }
            guard i < buffer.count else { return nil }
            defer { i += 1 }
            return String(decoding: buffer[start..<i], as: UTF8.self)
        }
        guard let executable = string(), !executable.isEmpty else { return nil }
        let padded = 4 + ((executable.utf8.count + 1 + 7) / 8) * 8
        guard padded <= buffer.count, buffer[i..<padded].allSatisfy({ $0 == 0 }) else { return nil }
        i = padded
        var arguments: [String] = []
        for _ in 0..<argc {
            guard let a = string() else { return nil }
            arguments.append(a)
        }
        let prefix = "CLAUDE_CONFIG_DIR="
        var configDir: String?
        while i < buffer.count, let e = string(), !e.isEmpty {
            if e.hasPrefix(prefix) {
                let v = String(e.dropFirst(prefix.count))
                configDir = v.isEmpty ? nil : v
            }
        }
        return Parsed(executable: executable, isClaudeCode: isClaudeCode(executable: executable, arguments: arguments),
                      configDir: configDir)
    }

    /// Script launchers whose argv[1] names the script they run.
    static let launchers: Set<String> = ["node", "bun", "deno"]

    /// Whether a process is Claude Code: the native `claude` binary (by its path or argv[0]), a
    /// launcher running a `claude` script (argv[1], as `node /usr/local/bin/claude`), or Node
    /// running the npm package. Claude Desktop's own `Claude` app is not.
    static func isClaudeCode(executable: String, arguments: [String]) -> Bool {
        let exe = (executable as NSString).lastPathComponent
        let arg0 = ((arguments.first ?? "") as NSString).lastPathComponent
        if exe == "claude" || arg0 == "claude" { return true }
        // A script launcher only: `vim ~/bin/claude` is not Claude Code.
        if launchers.contains(exe) || launchers.contains(arg0), arguments.count > 1,
           (arguments[1] as NSString).lastPathComponent == "claude" { return true }
        return arguments.dropFirst().prefix(2).contains { $0.contains("@anthropic-ai/claude-code/") }
    }

    /// The seat a Claude Code process with `configDir` uses.
    static func seat(configDir: String?, home: String) -> String {
        guard let configDir, !configDir.isEmpty else {
            return (AccountData.normalized(home) as NSString).appendingPathComponent(ClaudeCodeFolders.defaultName)
        }
        let expanded = configDir.hasPrefix("~/") ? (home as NSString).appendingPathComponent(String(configDir.dropFirst(2))) : configDir
        return AccountData.normalized(expanded)
    }

    /// The running Claude Code processes of this user, with their seats, and how many processes
    /// couldn't be read (a careful caller treats any of those as "a session may be running").
    static func running(home: String = NSHomeDirectory()) -> (sessions: [Running], unreadable: Int) {
        var sessions: [Running] = []
        var unreadable = 0
        let me = getpid()
        for pid in allPIDs() where pid > 0 && pid != me {
            guard let buffer = procargs(pid) else {
                if ownedByMe(pid) { unreadable += 1 }
                continue
            }
            guard let parsed = parse(buffer), parsed.isClaudeCode else { continue }
            sessions.append(Running(pid: pid, seat: seat(configDir: parsed.configDir, home: home)))
        }
        return (sessions, unreadable)
    }

    static func allPIDs() -> [Int32] {
        let count = proc_listallpids(nil, 0)
        guard count > 0 else { return [] }
        var pids = [Int32](repeating: 0, count: Int(count) + 64)
        let n = pids.withUnsafeMutableBufferPointer {
            proc_listallpids($0.baseAddress, Int32($0.count * MemoryLayout<Int32>.size))
        }
        return n > 0 ? Array(pids.prefix(Int(n))) : []
    }

    /// The `KERN_PROCARGS2` buffer of `pid`, nil when the kernel won't hand it over.
    static func procargs(_ pid: Int32) -> [UInt8]? {
        var mib: [Int32] = [CTL_KERN, KERN_ARGMAX]
        var argmax: Int32 = 0
        var size = MemoryLayout<Int32>.size
        guard sysctl(&mib, 2, &argmax, &size, nil, 0) == 0, argmax > 0 else { return nil }
        var buffer = [UInt8](repeating: 0, count: Int(argmax))
        var length = buffer.count
        mib = [CTL_KERN, KERN_PROCARGS2, pid]
        let ok = buffer.withUnsafeMutableBytes { sysctl(&mib, 3, $0.baseAddress, &length, nil, 0) } == 0
        guard ok, length > 0 else { return nil }
        return Array(buffer.prefix(length))
    }

    private static func ownedByMe(_ pid: Int32) -> Bool {
        var info = proc_bsdshortinfo()
        let size = Int32(MemoryLayout<proc_bsdshortinfo>.size)
        guard proc_pidinfo(pid, PROC_PIDT_SHORTBSDINFO, 0, &info, size) == size else { return false }
        return info.pbsi_uid == getuid()
    }
}
