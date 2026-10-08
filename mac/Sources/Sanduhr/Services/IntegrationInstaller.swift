import Foundation

/// The Claude Code integrations Settings installs (items 49 to 51).
enum IntegrationKind: String, Codable, CaseIterable, Sendable {
    /// `mcpServers.sanduhr` in the folder's `.claude.json` (placement rule).
    case mcp
    /// `statusLine` in the folder's `settings.json`.
    case statusline
    /// The meters mod's folder in `env.CLAUDE_CODE_PLUGIN_DIRS` of the folder's `settings.json`.
    case meters
    /// Sanduhr's entries in `hooks.Notification` and `hooks.Stop` of the folder's
    /// `settings.json`: Claude Code posts `com.626labs.sanduhr.claude-code.…` and the notch glows.
    case hooks

    var title: String {
        switch self {
        case .mcp: "MCP server"
        case .statusline: "Statusline"
        case .meters: "Meters above the prompt"
        case .hooks: "Notch glow when Claude needs you"
        }
    }

    /// The key Settings names when it says what it writes.
    var keyPath: String {
        switch self {
        case .mcp: "mcpServers.sanduhr"
        case .statusline: "statusLine"
        case .meters: "env.CLAUDE_CODE_PLUGIN_DIRS"
        case .hooks: "hooks.Notification and hooks.Stop"
        }
    }

    /// Runs on python3 (the mod runs inside Claude Code; the hooks run `notifyutil`).
    var needsPython: Bool { self == .mcp || self == .statusline }

    /// Needs Sanduhr's integration scripts copied out of the app (the hooks need none).
    var needsScripts: Bool { self != .hooks }
}

/// What a folder has for one integration.
enum IntegrationStatus: Equatable, Sendable {
    case notInstalled
    /// Sanduhr's entry, naming the current scripts and a python3 that exists.
    case installed
    /// Sanduhr's entry, but naming other scripts (an older install, `install.sh`'s copies) or a
    /// python3 that is gone, or the scripts await their refresh: Install updates it.
    case outdated
    /// Someone else's entry under the same key (a statusline of their own, another `sanduhr`
    /// server). Install asks before replacing it.
    case other
    /// The config file isn't JSON Sanduhr can edit: left alone.
    case unreadable

    /// Sanduhr's entry is there (state.yaml counts these).
    var isOurs: Bool { self == .installed || self == .outdated }
}

/// What Sanduhr did to one file, so Remove undoes exactly that (item 49). Kept in
/// `integrations/installs.json` beside the scripts, owner-only; never logged or in state.yaml.
struct IntegrationReceipt: Codable, Equatable, Sendable {
    /// The Claude Code folder the user picked.
    var folder: String
    /// The config file written (the placement rule's answer for `folder`).
    var file: String
    var kind: IntegrationKind
    /// The file didn't exist: Remove deletes it again when nothing else was added.
    var createdFile = false
    /// `mcpServers` didn't exist: Remove takes it out again once it is empty.
    var createdParent = false
    /// What sat between the braces of the object Sanduhr's member went into when it was empty.
    var emptyInner: String?
    /// The value Sanduhr replaced (someone else's statusline or `sanduhr` entry), byte for byte:
    /// Remove puts it back. For the mod, the plugin folders list before Sanduhr's entry joined
    /// it: Remove puts those bytes back when the list is otherwise what it was.
    var previous: String?
    /// The mod: `CLAUDE_CODE_PLUGIN_DIRS` didn't exist, so Remove deletes it once only Sanduhr's
    /// entry is left. Optional so receipts written before the mod still read.
    var createdKey: Bool?
    /// The hooks (item 51): what Install made, so Remove takes out exactly that.
    var hooks: HookReceipt?
    /// The statusline (item 63): whether the user's line was replaced or combined with
    /// Sanduhr's. Optional so receipts written before Combine still read.
    var mode: StatuslineMode?
    /// The meters mod (item 64, slice 2): the stamped version the plugin folders entry names
    /// (`integrations/<stamp>/mods/sanduhr-meters`) when the Mods page switched it on; nil when
    /// the entry goes through the `current` link and follows the app's updates. Kept stamps
    /// survive refresh while a receipt names them.
    var pinned: String?
    /// The meters mod's switch (item 64, slice 2): what the Mods page wrote in
    /// `enabledPlugins`, so switching back (or Remove) undoes exactly that.
    var switched: SwitchReceipt?
}

/// One `enabledPlugins` member a Mods page switch wrote (item 64, slice 2), byte for byte.
struct SwitchReceipt: Codable, Equatable, Sendable {
    /// The key, `<name>@inline`.
    var key: String
    /// The value written: false switches the mod off, true overrides an off of the user's own.
    var value: Bool
    /// The member's value before, as text; nil when there was none (undo removes the member).
    var previous: String?
    /// `enabledPlugins` didn't exist: undo removes it once it is empty again.
    var createdParent = false
    /// What sat between the braces of the object the member went into when it was empty.
    var emptyInner: String?

    init(key: String, value: Bool, previous: String? = nil, createdParent: Bool = false, emptyInner: String? = nil) {
        self.key = key
        self.value = value
        self.previous = previous
        self.createdParent = createdParent
        self.emptyInner = emptyInner
    }

    /// A missing `createdParent` reads as false: one receipt short of a field must not make the
    /// whole receipts file unreadable (receipts decode as one array, all or nothing).
    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        key = try c.decode(String.self, forKey: .key)
        value = try c.decode(Bool.self, forKey: .value)
        previous = try c.decodeIfPresent(String.self, forKey: .previous)
        createdParent = try c.decodeIfPresent(Bool.self, forKey: .createdParent) ?? false
        emptyInner = try c.decodeIfPresent(String.self, forKey: .emptyInner)
    }
}

/// What the hooks' install made in `hooks` (item 51).
struct HookReceipt: Codable, Equatable, Sendable {
    /// The event lists Sanduhr created (`Notification`, `Stop`): Remove deletes them once only
    /// Sanduhr's entry was in them.
    var createdEvents: [String] = []
    /// What sat between the braces of `hooks` when it was empty before Sanduhr's first list.
    var hooksInner: String?
    /// Per event: what sat between the brackets of a list that was empty before Sanduhr's entry.
    var arrayInners: [String: String] = [:]
}

/// Installs and removes the Claude Code integrations in one chosen folder (item 49), the Mac
/// port of Windows' `McpIntegrationInstaller` and `StatuslineInstaller` without the `claude`
/// CLI.
///
/// The files are Claude Code's, so every write is careful: read and checked as JSON first (a
/// file that isn't a JSON object is never written, and the error says so); only Sanduhr's member
/// changes, spliced into the text so every other byte stays (`JSONEdit`); the result is parsed
/// again and compared with the original minus that member before anything is written; a copy of
/// the original is kept as `<file>.sanduhr-backup` before the first change; the new bytes go to
/// a temporary sibling renamed over the file, after checking the file didn't change meanwhile
/// (Claude Code rewrites `.claude.json` often; a change means start over, three tries). The
/// file's permissions are kept, and a symlinked file is written where it points.
///
/// Remove undoes what Install did, from the receipt: the member comes out with its separator,
/// an object Sanduhr filled from empty gets its old inside back, a parent or file Sanduhr
/// created goes, a replaced value comes back. With nothing else changed the file is byte for
/// byte what it was, and the backup (then identical) is deleted. An entry Sanduhr doesn't
/// recognize as its own is never removed.
///
/// Only the folder passed in is touched (and, for the default `~/.claude`, `~/.claude.json`,
/// where Claude Code keeps that folder's config). Paths never reach a log.
struct IntegrationInstaller {
    /// The home folder the placement rule is relative to.
    let home: String
    let scripts: IntegrationScripts

    static let serverName = "sanduhr"
    static let statusLineKey = "statusLine"
    static let serversKey = "mcpServers"
    static let backupSuffix = ".sanduhr-backup"
    static let receiptsName = "installs.json"
    static let envKey = "env"
    static let pluginDirsKey = "CLAUDE_CODE_PLUGIN_DIRS"
    /// Claude Code splits the plugin folders on the platform's path-list separator (Node's
    /// `path.delimiter`): `:` here, `;` on Windows.
    static let pluginDirsSeparator = ":"

    static let hooksKey = "hooks"
    /// The hook events Sanduhr adds an entry to, and the event each one reports.
    static let hookEvents: [(name: String, event: ClaudeCodeEvent)] = [("Notification", .waiting), ("Stop", .done)]
    /// The notifications that mean "waiting on you": a permission prompt, the idle reminder, a
    /// question from an MCP server. Not a sign-in notice or the others.
    static let waitingMatcher = "permission_prompt|idle_prompt|elicitation_dialog"
    /// Seconds Claude Code gives the hook; it returns at once (it runs in the background anyway).
    static let hookTimeout = 5

    static var standard: IntegrationInstaller {
        IntegrationInstaller(home: NSHomeDirectory(), scripts: .standard)
    }

    enum Failure: Error, Equatable {
        /// The config file isn't a JSON object, or the member Sanduhr edits isn't what it should
        /// be: nothing was written.
        case malformed(file: String)
        /// The scripts aren't inside this copy of the app.
        case scriptsMissing
        /// Reading or writing failed, or the file kept changing while Sanduhr tried.
        case writeFailed(file: String)
    }

    /// Install's answer.
    enum Outcome: Equatable {
        case installed
        /// Someone else's entry is there and `replaceOther` wasn't given: nothing was written.
        /// Carries that entry's text for the question.
        case needsReplaceConsent(existing: String)
    }

    // MARK: Paths and entries

    /// The file `kind` is written to for `folder`.
    func configFile(_ kind: IntegrationKind, folder: String) -> String {
        switch kind {
        case .mcp: return ClaudeCodeFolders.configFile(for: folder, home: home)
        case .statusline, .meters, .hooks: return (AccountData.normalized(folder) as NSString).appendingPathComponent("settings.json")
        }
    }

    /// The entry Install writes.
    func entry(_ kind: IntegrationKind, python: String) -> JSONEdit.Value {
        switch kind {
        case .mcp:
            return .object([
                JSONEdit.Pair("type", .string("stdio")),
                JSONEdit.Pair("command", .string(python)),
                JSONEdit.Pair("args", .array([.string(scripts.installedPath(IntegrationScripts.mcpScript))])),
            ])
        case .statusline:
            return statuslineEntry(python: python, chain: nil, join: .line, siblings: [])
        case .meters:
            return .string(scripts.installedModPath)
        case .hooks:
            return .object(Self.hookEvents.map { JSONEdit.Pair($0.name, .array([Self.hookGroup($0.name)])) })
        }
    }

    // MARK: The hooks (item 51)

    /// The command a hook runs: post Sanduhr's Darwin notification (ClaudeCodeSignal), and always
    /// exit 0 so Claude Code never reports a hook error. A post never launches anything: it
    /// reaches whichever Sanduhr is running, and nothing when none is. (Hooks before this opened
    /// `sanduhr://claude-code?event=…`, which LaunchServices hands to the registered app and
    /// launches it when it isn't running; isOurHookCommand still knows them, so they show as
    /// outdated and Install rewrites them in place.)
    ///
    /// The Stop hook also hands Sanduhr the session's background work (item 66), and only while
    /// a Sanduhr runs to read it (`pgrep`, so no report waits for a later launch) and "Show Claude
    /// Code's background work" is on (watchers.json says `"background":true`; Sanduhr writes it):
    /// `stopTasksScript` reads the hook's input and drops a small report into Sanduhr's folder,
    /// which the app reads and deletes at once. The notification is posted either way, so the
    /// glow behaves as before.
    static func hookCommand(_ event: ClaudeCodeEvent) -> String {
        let post = "/usr/bin/notifyutil -p \(ClaudeCodeSignal.name(event))"
        guard event == .done else { return "\(post) || true" }
        return "d=\"$HOME/Library/Application Support/Sanduhr\"; /usr/bin/pgrep -xq Sanduhr && "
            + "/usr/bin/grep -qs '\"background\":true' \"$d/\(WatcherStore.switchFile)\" && "
            + "/usr/bin/osascript -l JavaScript -e '\(stopTasksScript)' \"$d\" >/dev/null 2>&1; \(post) || true"
    }

    /// The Stop hook's report (item 66), JavaScript for Automation (`osascript`, on every Mac;
    /// python3 may not be). Run with Sanduhr's folder as its one argument and the hook's input on
    /// stdin, it keeps only the session id, the folder Claude Code runs with (CLAUDE_CONFIG_DIR,
    /// for the work tag) and, per background task (at most 20), its id, type, status, description
    /// (clipped to 120 characters) and workflow name. Never the shell command, never
    /// `last_assistant_message`, never the transcript path. Written owner-only to a hidden
    /// temporary name and renamed to `watch-stop-<ms>-<uuid>.json`, so the app never reads half a
    /// file. No single quotes: it sits in single quotes in the command.
    static let stopTasksScript = "function run(argv){ObjC.import(\"Foundation\");var d=argv[0];"
        + "var i=$.NSFileHandle.fileHandleWithStandardInput.readDataToEndOfFile;"
        + "var j=JSON.parse(ObjC.unwrap($.NSString.alloc.initWithDataEncoding(i,4)));"
        + "var s=function(v,n){return typeof v===\"string\"?v.slice(0,n):null};"
        + "var t=(Array.isArray(j.background_tasks)?j.background_tasks:[])"
        + ".filter(function(x){return x&&typeof x===\"object\"}).slice(0,20)"
        + ".map(function(x){return{id:s(x.id,64),type:s(x.type,20),status:s(x.status,20),"
        + "description:s(x.description,120),name:s(x.name,60)}});"
        + "var e=ObjC.unwrap($.NSProcessInfo.processInfo.environment.objectForKey(\"CLAUDE_CONFIG_DIR\"));"
        + "var o={schema_version:1,session:s(j.session_id,64),folder:typeof e===\"string\"?e:null,tasks:t};"
        + "var n=Date.now()+\"-\"+ObjC.unwrap($.NSUUID.UUID.UUIDString);var f=$.NSFileManager.defaultManager;"
        + "var p=d+\"/.\(WatcherStore.stopPrefix)\"+n+\".tmp\";"
        + "if(f.createFileAtPathContentsAttributes(p,$(JSON.stringify(o)).dataUsingEncoding(4),"
        + "$({NSFilePosixPermissions:384}))){f.moveItemAtPathToPathError(p,d+\"/\(WatcherStore.stopPrefix)\"+n+\".json\",null)}}"

    /// Sanduhr's entry in one event's list: a matcher group with one command hook, in the
    /// background (`async`) with a short timeout. `Notification` matches only the waiting kinds.
    static func hookGroup(_ eventName: String) -> JSONEdit.Value {
        let event = hookEvents.first { $0.name == eventName }?.event ?? .done
        let hook = JSONEdit.Value.object([
            JSONEdit.Pair("type", .string("command")),
            JSONEdit.Pair("command", .string(hookCommand(event))),
            JSONEdit.Pair("async", .bool(true)),
            JSONEdit.Pair("timeout", .int(hookTimeout)),
        ])
        var pairs: [JSONEdit.Pair] = []
        if event == .waiting { pairs.append(JSONEdit.Pair("matcher", .string(waitingMatcher))) }
        pairs.append(JSONEdit.Pair("hooks", .array([hook])))
        return .object(pairs)
    }

    /// One hook command is Sanduhr's: it posts Sanduhr's Claude Code notification, or (installs
    /// before that) opens Sanduhr's Claude Code link.
    static func isOurHookCommand(_ hook: Any?) -> Bool {
        guard let o = hook as? [String: Any], o["type"] as? String == "command",
              let command = o["command"] as? String else { return false }
        return command.contains("notifyutil -p \(ClaudeCodeSignal.prefix)")
            || command.contains("\(ClaudeCodeLink.scheme)://\(ClaudeCodeLink.host)?")
    }

    /// One matcher group is Sanduhr's: every hook in it is Sanduhr's command. A group of the
    /// user's that also runs Sanduhr's command beside their own is theirs and never removed.
    static func isOurHookGroup(_ group: Any?) -> Bool {
        guard let o = group as? [String: Any], let hooks = o["hooks"] as? [Any], !hooks.isEmpty else { return false }
        return hooks.allSatisfy(isOurHookCommand)
    }

    /// `s` as one shell word: bare when it is plain, else in single quotes.
    static func shellQuoted(_ s: String) -> String {
        let plain = CharacterSet(charactersIn: "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_./-+=:,@%")
        if !s.isEmpty, s.unicodeScalars.allSatisfy(plain.contains) { return s }
        return "'" + s.replacingOccurrences(of: "'", with: "'\\''") + "'"
    }

    // MARK: Recognizing Sanduhr's entries

    /// A `sanduhr` server entry Sanduhr wrote (here, by `install.sh` or `claude mcp add` with
    /// the script): its command or its one argument runs `sanduhr_mcp.py` (or Windows' launcher).
    static func isOurMCP(_ value: Any?) -> Bool {
        guard let o = value as? [String: Any] else { return false }
        let command = o["command"] as? String ?? ""
        let args = o["args"] as? [Any] ?? []
        if command.contains(IntegrationScripts.mcpScript) || command.lowercased().contains("sanduhr-mcp") { return true }
        return args.count == 1 && (args[0] as? String)?.hasSuffix(IntegrationScripts.mcpScript) == true
    }

    /// A statusline that is Sanduhr's: only `<python> <…/sanduhr_statusline.py>`, or that
    /// combined with the user's line by exactly the runner's flags (`parseStatusline`). A
    /// command of someone's own that also calls the script (with `;`, `&&`, a pipe or a
    /// substitution) is theirs and never removed.
    static func isOurStatusline(_ value: Any?) -> Bool {
        guard let o = value as? [String: Any], let command = o["command"] as? String else { return false }
        return parseStatusline(command) != nil
    }

    /// One entry of the plugin folders list that is Sanduhr's mod: the folder inside Sanduhr's
    /// integrations, through the link or a stamped folder. A copy of the mod elsewhere (a
    /// checkout, the user's own) is theirs.
    static func isOurModEntry(_ entry: String) -> Bool {
        let e = entry.trimmingCharacters(in: .whitespaces)
        return e.hasSuffix("/\(IntegrationScripts.modPath)") && e.contains("/Sanduhr/integrations/")
    }

    /// The plugin folders list with Sanduhr's mod in it: an older entry of Sanduhr's is replaced
    /// where it stands (later ones dropped), else the mod goes last. Every other entry stays,
    /// byte for byte.
    static func addingPluginDir(_ ours: String, to value: String) -> String {
        let parts = value.components(separatedBy: pluginDirsSeparator)
        if parts.contains(where: isOurModEntry) {
            var out: [String] = []
            var placed = false
            for p in parts {
                if !isOurModEntry(p) {
                    out.append(p)
                } else if !placed {
                    out.append(ours)
                    placed = true
                }
            }
            return out.joined(separator: pluginDirsSeparator)
        }
        if value.trimmingCharacters(in: .whitespaces).isEmpty { return ours }
        return value + pluginDirsSeparator + ours
    }

    /// The list without Sanduhr's mod, or nil when it holds none.
    static func removingPluginDir(from value: String) -> String? {
        let parts = value.components(separatedBy: pluginDirsSeparator)
        guard parts.contains(where: isOurModEntry) else { return nil }
        return parts.filter { !isOurModEntry($0) }.joined(separator: pluginDirsSeparator)
    }

    // MARK: Status

    /// What `folder` has for `kind`. Reads the one config file.
    func status(_ kind: IntegrationKind, folder: String) -> IntegrationStatus {
        let file = configFile(kind, folder: folder)
        guard FileManager.default.fileExists(atPath: file) else { return .notInstalled }
        guard let data = FileManager.default.contents(atPath: file),
              let root = try? JSONEdit.root(data) else { return .unreadable }
        if kind == .meters { return metersStatus(root) }
        if kind == .hooks { return hooksStatus(root) }
        let value: Any?
        switch kind {
        case .meters, .hooks:
            return .unreadable
        case .mcp:
            guard let servers = root[Self.serversKey] else { return .notInstalled }
            guard let s = servers as? [String: Any] else { return .unreadable }
            value = s[Self.serverName]
        case .statusline:
            value = root[Self.statusLineKey]
        }
        guard let value else { return .notInstalled }
        let ours = kind == .mcp ? Self.isOurMCP(value) : Self.isOurStatusline(value)
        guard ours else { return .other }
        return isCurrent(kind, value) ? .installed : .outdated
    }

    /// The mod in the plugin folders list: current when it names the link and the installed
    /// files are the app's.
    private func metersStatus(_ root: [String: Any]) -> IntegrationStatus {
        guard let envValue = root[Self.envKey] else { return .notInstalled }
        guard let env = envValue as? [String: Any] else { return .unreadable }
        guard let dirs = env[Self.pluginDirsKey] else { return .notInstalled }
        guard let list = dirs as? String else { return .unreadable }
        let parts = list.components(separatedBy: Self.pluginDirsSeparator)
        guard parts.contains(where: Self.isOurModEntry) else { return .notInstalled }
        let exact = parts.filter(Self.isOurModEntry).map { $0.trimmingCharacters(in: .whitespaces) }
        // A version the Mods page pinned is current while its folder is there: the Mods page
        // updates it, asking first when the new version can do more (item 64, slice 2).
        if exact.count == 1, let stamp = scripts.pinnedStamp(of: exact[0]),
           FileManager.default.fileExists(atPath: scripts.pinnedModPath(stamp)) {
            return .installed
        }
        return exact == [scripts.installedModPath] && scripts.isCurrent && scripts.hasMod ? .installed : .outdated
    }

    /// The plugin folders entry for the mod: a pinned stamped version, else the `current` link.
    func modEntry(pinned: String?) -> String {
        pinned.map(scripts.pinnedModPath) ?? scripts.installedModPath
    }

    /// The hooks: current when each event's list holds exactly Sanduhr's entry as this version
    /// writes it, outdated when an entry of Sanduhr's is there in another form or only one list
    /// has it.
    private func hooksStatus(_ root: [String: Any]) -> IntegrationStatus {
        guard let value = root[Self.hooksKey] else { return .notInstalled }
        guard let hooks = value as? [String: Any] else { return .unreadable }
        var ours = false
        var current = true
        for e in Self.hookEvents {
            guard let listValue = hooks[e.name] else { current = false; continue }
            guard let list = listValue as? [Any] else { return .unreadable }
            let mine = list.filter(Self.isOurHookGroup)
            if !mine.isEmpty { ours = true }
            if mine.count != 1 || !NSArray(array: mine).isEqual(to: [Self.hookGroup(e.name).plain]) { current = false }
        }
        guard ours else { return .notInstalled }
        return current ? .installed : .outdated
    }

    /// Sanduhr's entry names the current scripts through the stable link, with a python3 that
    /// exists, and the scripts there are the app's.
    private func isCurrent(_ kind: IntegrationKind, _ value: Any) -> Bool {
        guard scripts.isCurrent, let o = value as? [String: Any] else { return false }
        let fm = FileManager.default
        switch kind {
        case .meters, .hooks:
            return false
        case .mcp:
            guard let command = o["command"] as? String, fm.isExecutableFile(atPath: command),
                  let args = o["args"] as? [String] else { return false }
            return args == [scripts.installedPath(IntegrationScripts.mcpScript)]
        case .statusline:
            guard let command = (o["command"] as? String).flatMap(Self.parseStatusline)?.base else { return false }
            let script = Self.shellQuoted(scripts.installedPath(IntegrationScripts.statuslineScript))
            guard command.hasSuffix(" " + script) else { return false }
            let python = String(command.dropLast(script.count + 1))
            let unquoted = python.hasPrefix("'") && python.hasSuffix("'") && python.count >= 2
                ? String(python.dropFirst().dropLast()).replacingOccurrences(of: "'\\''", with: "'") : python
            return fm.isExecutableFile(atPath: unquoted)
        }
    }

    /// The text of someone else's entry, for the replace question (nil when there is none).
    func otherEntry(_ kind: IntegrationKind, folder: String) -> String? {
        // The mod and the hooks join lists: nothing of anyone's is replaced.
        if kind == .meters || kind == .hooks { return nil }
        let file = configFile(kind, folder: folder)
        guard let data = FileManager.default.contents(atPath: file), (try? JSONEdit.root(data)) != nil else { return nil }
        let b = Array(data)
        guard let top = try? JSONEdit.topObject(b) else { return nil }
        let member: JSONEdit.Member?
        switch kind {
        case .meters, .hooks:
            return nil
        case .statusline:
            member = top.member(Self.statusLineKey)
        case .mcp:
            guard let m = top.member(Self.serversKey), let servers = try? JSONEdit.object(b, at: m.valueStart) else { return nil }
            member = servers.member(Self.serverName)
        }
        guard let member else { return nil }
        let text = JSONEdit.text(b, member)
        let parsed = try? JSONSerialization.jsonObject(with: Data(text), options: [.fragmentsAllowed])
        if kind == .statusline, let o = parsed as? [String: Any], let command = o["command"] as? String { return command }
        return String(decoding: text, as: UTF8.self)
    }

    // MARK: Install

    /// Writes Sanduhr's entry for `kind` into `folder`'s config, refreshing the scripts first.
    /// Someone else's entry is replaced only with `replaceOther`, and remembered for Remove.
    /// For the statusline, `combine` (item 63) keeps someone else's line instead: Sanduhr's
    /// command runs it and prints both, joined that way, keeping the segments `selection` picks
    /// (item 63b); it is remembered for Remove the same.
    @discardableResult
    func install(_ kind: IntegrationKind, folder: String, python: String,
                 replaceOther: Bool = false, combine: StatuslineJoin? = nil,
                 selection: StatuslineSelection = StatuslineSelection(), pinned: String? = nil) throws -> Outcome {
        if kind.needsScripts {
            do {
                try scripts.refresh()
            } catch IntegrationScripts.Failure.missingFromApp {
                throw Failure.scriptsMissing
            } catch {
                throw Failure.writeFailed(file: scripts.dir.path)
            }
        }
        if kind == .meters && !scripts.hasMod { throw Failure.scriptsMissing }
        let file = configFile(kind, folder: folder)
        let value = kind == .meters ? .string(modEntry(pinned: pinned)) : entry(kind, python: python)
        var receipts = loadReceipts()
        for _ in 0..<3 {
            let original = try read(file)
            let prior = receipts.first { $0.file == file && $0.kind == kind }
            let plan = try malformedNamed(file) {
                try planInstall(kind, bytes: original ?? Array("{}\n".utf8), value: value, python: python,
                                combine: combine, selection: selection, prior: prior,
                                createdFile: original == nil)
            }
            if let other = plan.other, !replaceOther, combine == nil { return .needsReplaceConsent(existing: other) }
            guard try commit(file, original: original, bytes: plan.bytes, backup: true) else { continue }
            var receipt = plan.receipt
            receipt.folder = AccountData.normalized(folder)
            receipt.file = file
            if kind == .meters {
                // The version the entry names now, and a switch made earlier, which stays.
                receipt.pinned = pinned
                receipt.switched = prior?.switched
            }
            receipts.removeAll { $0.file == file && $0.kind == kind }
            receipts.append(receipt)
            saveReceipts(receipts)
            return .installed
        }
        throw Failure.writeFailed(file: file)
    }

    /// Runs `body`, naming `file` in a malformed-JSON failure.
    func malformedNamed<T>(_ file: String, _ body: () throws -> T) throws -> T {
        do {
            return try body()
        } catch Failure.malformed {
            throw Failure.malformed(file: file)
        } catch is JSONEdit.Failure {
            throw Failure.malformed(file: file)
        }
    }

    /// Install's edit, worked out before anything is written. `other` is someone else's entry
    /// the edit replaces, for the question.
    private struct Plan {
        let bytes: [UInt8]
        let receipt: IntegrationReceipt
        let other: String?
    }

    private func planInstall(_ kind: IntegrationKind, bytes b: [UInt8], value: JSONEdit.Value, python: String,
                             combine: StatuslineJoin?, selection: StatuslineSelection, prior: IntegrationReceipt?,
                             createdFile: Bool) throws -> Plan {
        let root = try parse(b)
        let top = try JSONEdit.topObject(b)
        var receipt = IntegrationReceipt(folder: "", file: "", kind: kind, createdFile: createdFile)
        var result: [UInt8]
        var other: String?
        switch kind {
        case .meters:
            return try planInstallMeters(bytes: b, root: root, top: top, ours: (value.plain as? String) ?? "",
                                         receipt: receipt, prior: prior)
        case .hooks:
            return try planInstallHooks(bytes: b, root: root, top: top, receipt: receipt, prior: prior)
        case .statusline:
            return try planInstallStatusline(bytes: b, root: root, top: top, python: python, combine: combine,
                                             selection: selection, receipt: receipt, prior: prior)
        case .mcp:
            if let m = top.member(Self.serversKey) {
                guard root[Self.serversKey] is [String: Any] else { throw Failure.malformed(file: "") }
                let servers = try JSONEdit.object(b, at: m.valueStart)
                if let s = servers.member(Self.serverName) {
                    let existing = (root[Self.serversKey] as? [String: Any])?[Self.serverName]
                    if Self.isOurMCP(existing) {
                        if let prior { receipt = prior }
                    } else {
                        receipt.previous = String(decoding: JSONEdit.text(b, s), as: UTF8.self)
                        other = receipt.previous
                    }
                }
                let r = JSONEdit.set(b, in: servers, key: Self.serverName, value: value)
                if let inner = r.emptyInner { receipt.emptyInner = String(decoding: inner, as: UTF8.self) }
                result = r.bytes
            } else {
                let r = JSONEdit.set(b, in: top, key: Self.serversKey,
                                     value: .object([JSONEdit.Pair(Self.serverName, value)]))
                receipt.createdParent = true
                if let inner = r.emptyInner { receipt.emptyInner = String(decoding: inner, as: UTF8.self) }
                result = r.bytes
            }
        }
        try verify(kind, before: b, after: result, expect: value.plain)
        return Plan(bytes: result, receipt: receipt, other: other)
    }

    /// The statusline's install. Someone else's line is kept as `previous` and either replaced
    /// or, with `combine`, chained inside Sanduhr's command (unwrapped to their own command when
    /// it is itself Sanduhr's). Sanduhr's own entry, alone or combined, is rebuilt from its
    /// shape: an update keeps the chained command, the join and the picks. Either way the value carries the
    /// line's padding, refresh interval and vim choice.
    private func planInstallStatusline(bytes b: [UInt8], root: [String: Any], top: JSONEdit.Object, python: String,
                                       combine: StatuslineJoin?, selection picked: StatuslineSelection,
                                       receipt start: IntegrationReceipt,
                                       prior: IntegrationReceipt?) throws -> Plan {
        var receipt = start
        var other: String?
        var chain: String?
        var join = StatuslineJoin.line
        var selection = StatuslineSelection()
        let existing = root[Self.statusLineKey]
        let command = (existing as? [String: Any])?["command"] as? String
        if let m = top.member(Self.statusLineKey) {
            if let ours = command.flatMap(Self.parseStatusline) {
                // Updating Sanduhr's own entry keeps what the first install recorded.
                if let prior { receipt = prior }
                if let inner = ours.chain, let foreign = Self.innermostForeign(inner) {
                    chain = foreign
                    join = ours.join ?? .line
                    selection = ours.selection
                }
                if receipt.previous != nil || chain != nil { receipt.mode = chain == nil ? .replace : .combine }
            } else {
                receipt.previous = String(decoding: JSONEdit.text(b, m), as: UTF8.self)
                other = command ?? receipt.previous
                if let combine, let command, !command.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                    chain = command
                    join = combine
                    selection = picked
                }
                receipt.mode = chain == nil ? .replace : .combine
            }
        }
        let value = statuslineEntry(python: python, chain: chain, join: join, siblings: Self.carriedSiblings(existing),
                                    selection: selection)
        let r = JSONEdit.set(b, in: top, key: Self.statusLineKey, value: value)
        if let inner = r.emptyInner { receipt.emptyInner = String(decoding: inner, as: UTF8.self) }
        try verify(.statusline, before: b, after: r.bytes, expect: value.plain)
        return Plan(bytes: r.bytes, receipt: receipt, other: other)
    }

    /// The mod's install: its folder joins `env.CLAUDE_CODE_PLUGIN_DIRS`, the object and the key
    /// made when missing, every other entry of the list left as it was.
    private func planInstallMeters(bytes b: [UInt8], root: [String: Any], top: JSONEdit.Object, ours: String,
                                   receipt start: IntegrationReceipt, prior: IntegrationReceipt?) throws -> Plan {
        var receipt = start
        var result: [UInt8]
        if let m = top.member(Self.envKey) {
            guard let env = root[Self.envKey] as? [String: Any] else { throw Failure.malformed(file: "") }
            let envObject = try JSONEdit.object(b, at: m.valueStart)
            if let km = envObject.member(Self.pluginDirsKey) {
                guard let list = env[Self.pluginDirsKey] as? String else { throw Failure.malformed(file: "") }
                // Updating Sanduhr's own entry keeps what the first install recorded; a receipt
                // holding only a switch recorded no list, so this install records it.
                if let prior, Self.removingPluginDir(from: list) != nil {
                    receipt = prior
                } else {
                    receipt.previous = String(decoding: JSONEdit.text(b, km), as: UTF8.self)
                }
                let updated = Self.addingPluginDir(ours, to: list)
                result = updated == list ? b
                    : JSONEdit.set(b, in: envObject, key: Self.pluginDirsKey, value: .string(updated)).bytes
            } else {
                let r = JSONEdit.set(b, in: envObject, key: Self.pluginDirsKey, value: .string(ours))
                receipt.createdKey = true
                if let inner = r.emptyInner { receipt.emptyInner = String(decoding: inner, as: UTF8.self) }
                result = r.bytes
            }
        } else {
            let r = JSONEdit.set(b, in: top, key: Self.envKey,
                                 value: .object([JSONEdit.Pair(Self.pluginDirsKey, .string(ours))]))
            receipt.createdParent = true
            receipt.createdKey = true
            if let inner = r.emptyInner { receipt.emptyInner = String(decoding: inner, as: UTF8.self) }
            result = r.bytes
        }
        try verifyMeters(before: b, after: result, ours: ours)
        return Plan(bytes: result, receipt: receipt, other: nil)
    }

    /// The hooks' install: Sanduhr's entry joins the `Notification` and `Stop` lists, `hooks`
    /// and the lists made when missing, an older entry of Sanduhr's replaced where it stands,
    /// every other entry and hook left as it was.
    private func planInstallHooks(bytes b: [UInt8], root: [String: Any], top: JSONEdit.Object,
                                  receipt start: IntegrationReceipt, prior: IntegrationReceipt?) throws -> Plan {
        var receipt = start
        var made = HookReceipt()
        var result = b
        if top.member(Self.hooksKey) == nil {
            let r = JSONEdit.set(b, in: top, key: Self.hooksKey, value: entry(.hooks, python: ""))
            receipt.createdParent = true
            if let inner = r.emptyInner { receipt.emptyInner = String(decoding: inner, as: UTF8.self) }
            made.createdEvents = Self.hookEvents.map(\.name)
            result = r.bytes
        } else {
            guard let hooks = root[Self.hooksKey] as? [String: Any] else { throw Failure.malformed(file: "") }
            // Updating Sanduhr's own entries keeps what the first install recorded.
            if let prior, Self.hookEvents.contains(where: { (hooks[$0.name] as? [Any])?.contains(where: Self.isOurHookGroup) == true }) {
                receipt = prior
                made = prior.hooks ?? HookReceipt()
            }
            for e in Self.hookEvents {
                result = try addHookGroup(e.name, to: result, made: &made)
            }
        }
        receipt.hooks = made
        try verifyHooks(before: b, after: result, installed: true)
        return Plan(bytes: result, receipt: receipt, other: nil)
    }

    /// `hooks` of a file that has one, as an object.
    private func hooksObject(_ b: [UInt8]) throws -> JSONEdit.Object? {
        let top = try JSONEdit.topObject(b)
        guard let m = top.member(Self.hooksKey) else { return nil }
        do { return try JSONEdit.object(b, at: m.valueStart) } catch { throw Failure.malformed(file: "") }
    }

    /// One event's list in `hooks`, nil when there is none; malformed when it isn't a list.
    private func hookList(_ b: [UInt8], _ hooks: JSONEdit.Object, _ name: String) throws -> JSONEdit.ArrayValue? {
        guard let m = hooks.member(name) else { return nil }
        do { return try JSONEdit.array(b, at: m.valueStart) } catch { throw Failure.malformed(file: "") }
    }

    /// The indexes of Sanduhr's entries in a list.
    private static func ourIndexes(_ b: [UInt8], _ list: JSONEdit.ArrayValue) -> [Int] {
        list.elements.indices.filter { isOurHookGroup(JSONEdit.parsed(b, list.elements[$0])) }
    }

    /// Puts Sanduhr's entry into one event's list: the first entry of Sanduhr's is rewritten in
    /// place and any more of them dropped, else the entry goes last; the list is made when
    /// missing. What it made goes into `made`.
    private func addHookGroup(_ name: String, to b: [UInt8], made: inout HookReceipt) throws -> [UInt8] {
        guard let hooks = try hooksObject(b) else { throw Failure.malformed(file: "") }
        let group = Self.hookGroup(name)
        guard let list = try hookList(b, hooks, name) else {
            let r = JSONEdit.set(b, in: hooks, key: name, value: .array([group]))
            if let inner = r.emptyInner, made.hooksInner == nil { made.hooksInner = String(decoding: inner, as: UTF8.self) }
            if !made.createdEvents.contains(name) { made.createdEvents.append(name) }
            return r.bytes
        }
        let mine = Self.ourIndexes(b, list)
        guard let first = mine.first else {
            let r = JSONEdit.append(b, in: list, value: group)
            if let inner = r.emptyInner, made.arrayInners[name] == nil {
                made.arrayInners[name] = String(decoding: inner, as: UTF8.self)
            }
            return r.bytes
        }
        var out = b
        // Extra copies of Sanduhr's entry go, last first, so the earlier offsets hold.
        for i in mine.dropFirst().reversed() {
            guard let h = try hooksObject(out), let l = try hookList(out, h, name) else { break }
            out = JSONEdit.removeElement(out, in: l, index: i)
        }
        guard let h = try hooksObject(out), let l = try hookList(out, h, name) else { throw Failure.malformed(file: "") }
        let current = JSONEdit.parsed(out, l.elements[first])
        if let current, NSArray(array: [current]).isEqual(to: [group.plain]) { return out }
        return JSONEdit.replaceElement(out, in: l, index: first, value: group)
    }

    // MARK: Remove

    /// Takes Sanduhr's entry for `kind` out of `folder`'s config, undoing what Install did.
    /// An entry that isn't Sanduhr's stays. With nothing installed anywhere any more, the
    /// scripts go too.
    func remove(_ kind: IntegrationKind, folder: String) throws {
        // The meters mod's switch comes out first, so the folder is back to what it was.
        if kind == .meters { try undoModSwitch(folder: folder) }
        let file = configFile(kind, folder: folder)
        var receipts = loadReceipts()
        let receipt = receipts.first { $0.file == file && $0.kind == kind }
        var done = false
        for _ in 0..<3 {
            guard let original = try read(file) else { done = true; break }
            guard let edit = try malformedNamed(file, { try planRemove(kind, bytes: original, receipt: receipt) }) else {
                done = true
                break
            }
            let delete = receipt?.createdFile == true && (try? JSONEdit.topObject(edit))?.members.isEmpty == true
            if try commit(file, original: original, bytes: delete ? nil : edit, backup: false) {
                done = true
                break
            }
        }
        guard done else { throw Failure.writeFailed(file: file) }
        receipts.removeAll { $0.file == file && $0.kind == kind }
        saveReceipts(receipts)
        if receipts.isEmpty { scripts.removeAll() }
    }

    /// Remove's edit, or nil when there is nothing of Sanduhr's to take out.
    private func planRemove(_ kind: IntegrationKind, bytes b: [UInt8], receipt: IntegrationReceipt?) throws -> [UInt8]? {
        let root = try parse(b)
        let top = try JSONEdit.topObject(b)
        let restored = receipt?.previous.map { Array($0.utf8) }
        var result: [UInt8]
        var expect: Any?
        switch kind {
        case .meters:
            return try planRemoveMeters(bytes: b, root: root, top: top, receipt: receipt)
        case .hooks:
            return try planRemoveHooks(bytes: b, root: root, top: top, receipt: receipt)
        case .statusline:
            guard top.member(Self.statusLineKey) != nil, Self.isOurStatusline(root[Self.statusLineKey]) else { return nil }
            if let restored {
                result = JSONEdit.restore(b, in: top, key: Self.statusLineKey, previous: restored)
                expect = try? JSONSerialization.jsonObject(with: Data(restored), options: [.fragmentsAllowed])
            } else {
                result = JSONEdit.remove(b, in: top, key: Self.statusLineKey,
                                         emptyInner: receipt?.emptyInner.map { Array($0.utf8) })
            }
        case .mcp:
            guard let m = top.member(Self.serversKey), let serversValue = root[Self.serversKey] as? [String: Any],
                  Self.isOurMCP(serversValue[Self.serverName]) else { return nil }
            let servers = try JSONEdit.object(b, at: m.valueStart)
            if let restored {
                result = JSONEdit.restore(b, in: servers, key: Self.serverName, previous: restored)
                expect = try? JSONSerialization.jsonObject(with: Data(restored), options: [.fragmentsAllowed])
            } else if receipt?.createdParent == true {
                result = JSONEdit.remove(b, in: servers, key: Self.serverName)
                // The parent Sanduhr added goes too, once nothing else is in it.
                let after = try JSONEdit.topObject(result)
                if let pm = after.member(Self.serversKey),
                   (try JSONEdit.object(result, at: pm.valueStart)).members.isEmpty {
                    result = JSONEdit.remove(result, in: after, key: Self.serversKey,
                                             emptyInner: receipt?.emptyInner.map { Array($0.utf8) })
                }
            } else {
                result = JSONEdit.remove(b, in: servers, key: Self.serverName,
                                         emptyInner: receipt?.emptyInner.map { Array($0.utf8) })
            }
        }
        try verify(kind, before: b, after: result, expect: expect)
        return result
    }

    /// The mod's remove: only Sanduhr's entry leaves the list. The key goes when Sanduhr made it
    /// and nothing else is in it, the `env` object too when Sanduhr made that; a list that is
    /// back to what it was gets its original bytes.
    private func planRemoveMeters(bytes b: [UInt8], root: [String: Any], top: JSONEdit.Object,
                                  receipt: IntegrationReceipt?) throws -> [UInt8]? {
        guard let m = top.member(Self.envKey), let env = root[Self.envKey] as? [String: Any],
              let list = env[Self.pluginDirsKey] as? String,
              let remaining = Self.removingPluginDir(from: list) else { return nil }
        let envObject = try JSONEdit.object(b, at: m.valueStart)
        let inner = receipt?.emptyInner.map { Array($0.utf8) }
        var result: [UInt8]
        let isEmpty = remaining.trimmingCharacters(in: .whitespaces).isEmpty
        if isEmpty && (receipt?.createdKey ?? (receipt?.previous == nil)) {
            if receipt?.createdParent == true {
                result = JSONEdit.remove(b, in: envObject, key: Self.pluginDirsKey)
                let after = try JSONEdit.topObject(result)
                if let em = after.member(Self.envKey), (try JSONEdit.object(result, at: em.valueStart)).members.isEmpty {
                    result = JSONEdit.remove(result, in: after, key: Self.envKey, emptyInner: inner)
                }
            } else {
                result = JSONEdit.remove(b, in: envObject, key: Self.pluginDirsKey, emptyInner: inner)
            }
        } else if let previous = receipt?.previous,
                  let was = try? JSONSerialization.jsonObject(with: Data(previous.utf8), options: [.fragmentsAllowed]) as? String,
                  was == remaining {
            result = JSONEdit.restore(b, in: envObject, key: Self.pluginDirsKey, previous: Array(previous.utf8))
        } else {
            result = JSONEdit.set(b, in: envObject, key: Self.pluginDirsKey, value: .string(remaining)).bytes
        }
        try verifyMeters(before: b, after: result, ours: nil)
        return result
    }

    /// The hooks' remove: only Sanduhr's entries leave the two lists. A list Sanduhr made goes
    /// once nothing else is in it, a list that was empty gets its old inside back, and `hooks`
    /// goes when Sanduhr made it and nothing else is in it. Lists are emptied in the reverse of
    /// the order Install filled them, so each emptied object gets back exactly what it held.
    private func planRemoveHooks(bytes b: [UInt8], root: [String: Any], top: JSONEdit.Object,
                                 receipt: IntegrationReceipt?) throws -> [UInt8]? {
        guard let hooks = root[Self.hooksKey] as? [String: Any],
              Self.hookEvents.contains(where: { (hooks[$0.name] as? [Any])?.contains(where: Self.isOurHookGroup) == true })
        else { return nil }
        let made = receipt?.hooks
        var result = b
        for e in Self.hookEvents.reversed() {
            guard let h = try hooksObject(result), let list = try hookList(result, h, e.name) else { continue }
            let mine = Self.ourIndexes(result, list)
            guard !mine.isEmpty else { continue }
            let inner = made?.arrayInners[e.name].map { Array($0.utf8) }
            for i in mine.reversed() {
                guard let h2 = try hooksObject(result), let l = try hookList(result, h2, e.name) else { break }
                result = JSONEdit.removeElement(result, in: l, index: i, emptyInner: inner)
            }
            guard made?.createdEvents.contains(e.name) == true,
                  let h3 = try hooksObject(result), let l = try hookList(result, h3, e.name),
                  l.elements.isEmpty else { continue }
            result = JSONEdit.remove(result, in: h3, key: e.name, emptyInner: made?.hooksInner.map { Array($0.utf8) })
        }
        if receipt?.createdParent == true, let h = try hooksObject(result), h.members.isEmpty {
            result = JSONEdit.remove(result, in: try JSONEdit.topObject(result), key: Self.hooksKey,
                                     emptyInner: receipt?.emptyInner.map { Array($0.utf8) })
        }
        try verifyHooks(before: b, after: result, installed: false)
        return result
    }

    // MARK: Checks

    func parse(_ b: [UInt8]) throws -> [String: Any] {
        do { return try JSONEdit.root(Data(b)) } catch { throw Failure.malformed(file: "") }
    }

    /// The edit changed Sanduhr's member and nothing else: the parsed result minus that member
    /// equals the original minus it, and the member holds `expect` (absent for nil). For the
    /// server entry, `mcpServers` may appear or go only when it holds nothing else.
    private func verify(_ kind: IntegrationKind, before: [UInt8], after: [UInt8], expect: Any?) throws {
        let old = try parse(before)
        guard var new = try? JSONEdit.root(Data(after)) else { throw Failure.malformed(file: "") }
        var oldRest = old
        let got: Any?
        switch kind {
        case .meters, .hooks:
            // The mod's and the hooks' edits are checked by verifyMeters and verifyHooks.
            throw Failure.malformed(file: "")
        case .statusline:
            got = new.removeValue(forKey: Self.statusLineKey)
            oldRest.removeValue(forKey: Self.statusLineKey)
        case .mcp:
            var newServers = new.removeValue(forKey: Self.serversKey) as? [String: Any] ?? [:]
            var oldServers = oldRest.removeValue(forKey: Self.serversKey) as? [String: Any] ?? [:]
            got = newServers.removeValue(forKey: Self.serverName)
            oldServers.removeValue(forKey: Self.serverName)
            guard NSDictionary(dictionary: newServers).isEqual(to: oldServers) else { throw Failure.malformed(file: "") }
        }
        guard NSDictionary(dictionary: new).isEqual(to: oldRest) else { throw Failure.malformed(file: "") }
        switch (got, expect) {
        case (nil, nil): return
        case (let g?, let e?):
            guard NSArray(array: [g]).isEqual(to: [e]) else { throw Failure.malformed(file: "") }
        default:
            throw Failure.malformed(file: "")
        }
    }

    /// The mod's edit changed the plugin folders list and nothing else: the parsed files agree
    /// once `env.CLAUDE_CODE_PLUGIN_DIRS` (and an `env` left empty) is set aside, the list's other
    /// entries are the same in the same order, and Sanduhr's entry is there once (Install) or
    /// gone (Remove).
    private func verifyMeters(before: [UInt8], after: [UInt8], ours expected: String?) throws {
        let old = try parse(before)
        guard let new = try? JSONEdit.root(Data(after)) else { throw Failure.malformed(file: "") }
        func split(_ root: [String: Any]) -> (rest: [String: Any], dirs: [String]) {
            var rest = root
            var env = rest.removeValue(forKey: Self.envKey) as? [String: Any]
            let list = env?.removeValue(forKey: Self.pluginDirsKey) as? String
            if let env, !env.isEmpty { rest[Self.envKey] = env }
            return (rest, list.map { $0.components(separatedBy: Self.pluginDirsSeparator) } ?? [])
        }
        let (oldRest, oldDirs) = split(old)
        let (newRest, newDirs) = split(new)
        guard NSDictionary(dictionary: newRest).isEqual(to: oldRest) else { throw Failure.malformed(file: "") }
        let others = { (dirs: [String]) in
            dirs.filter { !Self.isOurModEntry($0) && !$0.trimmingCharacters(in: .whitespaces).isEmpty }
        }
        guard others(oldDirs) == others(newDirs) else { throw Failure.malformed(file: "") }
        let ours = newDirs.filter(Self.isOurModEntry).map { $0.trimmingCharacters(in: .whitespaces) }
        guard ours == (expected.map { [$0] } ?? []) else { throw Failure.malformed(file: "") }
    }

    /// The hooks' edit changed Sanduhr's entries and nothing else: the parsed files agree once
    /// `hooks` is set aside; in `hooks`, every other event is the same; in the two lists, the
    /// other entries are the same in the same order; and each list holds Sanduhr's entry once
    /// (Install) or not at all (Remove). A list or `hooks` Sanduhr made or took out counts as
    /// empty.
    private func verifyHooks(before: [UInt8], after: [UInt8], installed: Bool) throws {
        let old = try parse(before)
        guard let new = try? JSONEdit.root(Data(after)) else { throw Failure.malformed(file: "") }
        func split(_ root: [String: Any]) throws -> (rest: [String: Any], others: [String: Any], lists: [String: [Any]]) {
            var rest = root
            let hooksValue = rest.removeValue(forKey: Self.hooksKey)
            var hooks: [String: Any] = [:]
            if let hooksValue {
                guard let h = hooksValue as? [String: Any] else { throw Failure.malformed(file: "") }
                hooks = h
            }
            var lists: [String: [Any]] = [:]
            for e in Self.hookEvents {
                let v = hooks.removeValue(forKey: e.name)
                if v != nil, !(v is [Any]) { throw Failure.malformed(file: "") }
                lists[e.name] = v as? [Any] ?? []
            }
            return (rest, hooks, lists)
        }
        let (oldRest, oldOthers, oldLists) = try split(old)
        let (newRest, newOthers, newLists) = try split(new)
        guard NSDictionary(dictionary: newRest).isEqual(to: oldRest),
              NSDictionary(dictionary: newOthers).isEqual(to: oldOthers) else { throw Failure.malformed(file: "") }
        for e in Self.hookEvents {
            let o = oldLists[e.name] ?? [], n = newLists[e.name] ?? []
            guard NSArray(array: o.filter { !Self.isOurHookGroup($0) }).isEqual(to: n.filter { !Self.isOurHookGroup($0) })
            else { throw Failure.malformed(file: "") }
            let mine = n.filter(Self.isOurHookGroup)
            let want: [Any] = installed ? [Self.hookGroup(e.name).plain] : []
            guard NSArray(array: mine).isEqual(to: want) else { throw Failure.malformed(file: "") }
        }
    }

    // MARK: Files

    /// The file's bytes, nil when it doesn't exist.
    func read(_ file: String) throws -> [UInt8]? {
        guard FileManager.default.fileExists(atPath: file) else { return nil }
        guard let data = FileManager.default.contents(atPath: file) else { throw Failure.writeFailed(file: file) }
        return Array(data)
    }

    /// Writes `bytes` (nil deletes the file) unless the file changed since `original` was read;
    /// returns false then, for the caller to start over. With `backup` (Install), keeps a copy
    /// of the file before Sanduhr's first change; deletes that copy once the file is back to
    /// what it holds.
    func commit(_ file: String, original: [UInt8]?, bytes: [UInt8]?, backup makeBackup: Bool) throws -> Bool {
        let fm = FileManager.default
        let target = fm.fileExists(atPath: file) ? (file as NSString).resolvingSymlinksInPath : file
        let backup = file + Self.backupSuffix
        // The re-read: Claude Code may have written meanwhile.
        let now = fm.contents(atPath: target).map(Array.init)
        guard now == original else { return false }
        do {
            if makeBackup, let original, !fm.fileExists(atPath: backup) {
                try Data(original).write(to: URL(fileURLWithPath: backup))
                if let perms = (try? fm.attributesOfItem(atPath: target))?[.posixPermissions] {
                    try? fm.setAttributes([.posixPermissions: perms], ofItemAtPath: backup)
                }
            }
            if let bytes {
                try atomicWrite(Data(bytes), to: target, newFilePermissions: file.hasSuffix(".claude.json") ? 0o600 : 0o644)
            } else {
                try fm.removeItem(atPath: target)
            }
        } catch {
            NSLog("Sanduhr integration write failed (\(type(of: error)))")
            throw Failure.writeFailed(file: file)
        }
        // Back to what it was before Sanduhr: the backup has nothing more to offer.
        if let kept = fm.contents(atPath: backup) {
            let current = fm.contents(atPath: target)
            if current.map(Array.init) == Array(kept) { try? fm.removeItem(atPath: backup) }
        }
        return true
    }

    /// A temporary sibling, given the file's permissions, renamed over it.
    private func atomicWrite(_ data: Data, to path: String, newFilePermissions: Int) throws {
        let fm = FileManager.default
        let dir = (path as NSString).deletingLastPathComponent
        let name = (path as NSString).lastPathComponent
        let tmp = (dir as NSString).appendingPathComponent(".\(name).sanduhr-\(UUID().uuidString).tmp")
        let perms = (try? fm.attributesOfItem(atPath: path))?[.posixPermissions] as? Int ?? newFilePermissions
        guard fm.createFile(atPath: tmp, contents: data, attributes: [.posixPermissions: perms]) else {
            throw CocoaError(.fileWriteUnknown)
        }
        guard rename(tmp, path) == 0 else {
            unlink(tmp)
            throw POSIXError(POSIXErrorCode(rawValue: errno) ?? .EIO)
        }
    }

    // MARK: Receipts

    var receiptsURL: URL { scripts.dir.appendingPathComponent(Self.receiptsName) }

    func loadReceipts() -> [IntegrationReceipt] {
        guard let data = try? Data(contentsOf: receiptsURL) else { return [] }
        return (try? JSONDecoder().decode([IntegrationReceipt].self, from: data)) ?? []
    }

    func saveReceipts(_ receipts: [IntegrationReceipt]) {
        let fm = FileManager.default
        if receipts.isEmpty {
            try? fm.removeItem(at: receiptsURL)
            return
        }
        let enc = JSONEncoder()
        enc.outputFormatting = [.sortedKeys, .prettyPrinted, .withoutEscapingSlashes]
        guard let data = try? enc.encode(receipts) else { return }
        try? fm.createDirectory(at: scripts.dir, withIntermediateDirectories: true)
        try? atomicWrite(data, to: receiptsURL.path, newFilePermissions: 0o600)
    }

    /// state.yaml's `integrations:`: how many of `folders` (plus the ones installed into) hold
    /// Sanduhr's entry, current or outdated. Counts only, never a path.
    func installedCounts(folders: [String]) -> (mcp: Int, statusline: Int, meters: Int, hooks: Int) {
        var all: [String] = []
        for f in folders + installedFolders() {
            let n = AccountData.normalized(f)
            if !all.contains(n) { all.append(n) }
        }
        return (all.filter { status(.mcp, folder: $0).isOurs }.count,
                all.filter { status(.statusline, folder: $0).isOurs }.count,
                all.filter { status(.meters, folder: $0).isOurs }.count,
                all.filter { status(.hooks, folder: $0).isOurs }.count)
    }

    /// The folders Sanduhr installed into (Settings lists them even when discovery doesn't).
    func installedFolders() -> [String] {
        var seen: [String] = []
        for r in loadReceipts() where !seen.contains(r.folder) { seen.append(r.folder) }
        return seen
    }
}
