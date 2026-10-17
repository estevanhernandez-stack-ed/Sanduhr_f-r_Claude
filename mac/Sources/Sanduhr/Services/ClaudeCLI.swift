import Foundation

/// Item 71 slice 3's runner, as item 76 slice E uses it: plugins and MCP servers written into a
/// seat only through Claude Code's own CLI, under that seat's `CLAUDE_CONFIG_DIR`. Only the
/// commands in `Command` can run; never `-y`, so a plugin that installs by running a command of
/// its marketplace's is refused and shown, not accepted.
struct ClaudeCLI: Sendable {
    /// The commands Sanduhr runs. Nothing else is built.
    enum Command: Equatable, Sendable {
        case marketplaceList
        case marketplaceAdd(source: String)
        case marketplaceRemove(name: String)
        case pluginList
        case pluginInstall(id: String)
        case pluginUninstall(id: String)
        case mcpAddJSON(name: String, json: String)
        case mcpRemove(name: String)

        var arguments: [String] {
            switch self {
            case .marketplaceList: ["plugin", "marketplace", "list", "--json"]
            case .marketplaceAdd(let s): ["plugin", "marketplace", "add", s, "--scope", "user", "--json"]
            case .marketplaceRemove(let n): ["plugin", "marketplace", "remove", n, "--scope", "user", "--json"]
            case .pluginList: ["plugin", "list", "--json"]
            case .pluginInstall(let id): ["plugin", "install", id, "--scope", "user", "--json"]
            case .pluginUninstall(let id): ["plugin", "uninstall", id, "--scope", "user", "--json"]
            case .mcpAddJSON(let n, let j): ["mcp", "add-json", "-s", "user", n, j]
            case .mcpRemove(let n): ["mcp", "remove", "-s", "user", n]
            }
        }

        /// Network commands get longer: a marketplace add clones a repo.
        var timeout: TimeInterval {
            switch self {
            case .marketplaceAdd, .pluginInstall: 180
            default: 30
            }
        }
    }

    struct Result: Equatable, Sendable {
        var status: Int32
        var out: String
        var err: String
        var timedOut = false

        var ok: Bool { status == 0 && !timedOut }

        /// The last line of stdout as a JSON object (`--json` prints one result line last).
        var json: [String: Any]? {
            guard let line = out.split(separator: "\n").last,
                  let o = try? JSONSerialization.jsonObject(with: Data(line.utf8)) as? [String: Any] else { return nil }
            return o
        }

        /// What to say when it failed: the CLI's message, else its first stderr line.
        var reason: String {
            if timedOut { return "it didn't finish in time and was stopped" }
            if let m = json?["message"] as? String, !m.isEmpty { return m.split(separator: "\n").last.map(String.init) ?? m }
            let line = (err.isEmpty ? out : err).split(separator: "\n").first.map(String.init) ?? ""
            return line.isEmpty ? "exit \(status)" : String(line.prefix(300))
        }
    }

    let claude: String
    let home: String

    init(claude: String, home: String = NSHomeDirectory()) {
        self.claude = claude
        self.home = AccountData.normalized(home)
    }

    /// The environment for `seat`: this process's, without anything Claude Code reads to pick a
    /// folder or load plugins, then `CLAUDE_CONFIG_DIR` for any seat but the default `~/.claude`
    /// (whose `.claude.json` sits in the home folder only when the variable is unset).
    func environment(seat: String, base: [String: String] = ProcessInfo.processInfo.environment) -> [String: String] {
        var env = base.filter { !$0.key.hasPrefix("CLAUDE_") && $0.key != "CLAUDECODE" }
        let bins = [(claude as NSString).deletingLastPathComponent, "/opt/homebrew/bin", "/usr/local/bin", "/usr/bin", "/bin"]
        env["PATH"] = (bins + [env["PATH"] ?? ""]).filter { !$0.isEmpty }.joined(separator: ":")
        env["NO_COLOR"] = "1"
        let root = AccountData.normalized(seat)
        if root != AccountData.normalized((home as NSString).appendingPathComponent(".claude")) {
            env["CLAUDE_CONFIG_DIR"] = root
        }
        return env
    }

    /// Runs `command` for `seat`, stdin closed, stopped after its timeout.
    func run(_ command: Command, seat: String) -> Result {
        let r = ProcessRunner.run(claude, command.arguments, cwd: URL(fileURLWithPath: NSTemporaryDirectory()),
                                  environment: environment(seat: seat), timeout: command.timeout)
        if r.launchFailed { return Result(status: -1, out: "", err: "Couldn't start \(claude).") }
        if r.timedOut { return Result(status: -1, out: "", err: "", timedOut: true) }
        return Result(status: r.status, out: r.stdout, err: r.stderr)
    }
}
