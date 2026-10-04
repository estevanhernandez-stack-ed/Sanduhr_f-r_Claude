import Foundation

/// The Claude Code integrations Settings installs (items 49 and 50).
enum IntegrationKind: String, Codable, CaseIterable, Sendable {
    /// `mcpServers.sanduhr` in the folder's `.claude.json` (placement rule).
    case mcp
    /// `statusLine` in the folder's `settings.json`.
    case statusline
    /// The meters mod's folder in `env.CLAUDE_CODE_PLUGIN_DIRS` of the folder's `settings.json`.
    case meters

    var title: String {
        switch self {
        case .mcp: "MCP server"
        case .statusline: "Statusline"
        case .meters: "Meters above the prompt"
        }
    }

    /// The key Settings names when it says what it writes.
    var keyPath: String {
        switch self {
        case .mcp: "mcpServers.sanduhr"
        case .statusline: "statusLine"
        case .meters: "env.CLAUDE_CODE_PLUGIN_DIRS"
        }
    }

    /// Runs on python3 (the mod runs inside Claude Code).
    var needsPython: Bool { self != .meters }
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
        case .statusline, .meters: return (AccountData.normalized(folder) as NSString).appendingPathComponent("settings.json")
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
            return .object([
                JSONEdit.Pair("type", .string("command")),
                JSONEdit.Pair("command", .string(Self.statuslineCommand(
                    python: python, script: scripts.installedPath(IntegrationScripts.statuslineScript)))),
            ])
        case .meters:
            return .string(scripts.installedModPath)
        }
    }

    static func statuslineCommand(python: String, script: String) -> String {
        "\(shellQuoted(python)) \(shellQuoted(script))"
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

    /// A statusline that is only Sanduhr's script: `<python> <…/sanduhr_statusline.py>`. A
    /// command of someone's own that also calls the script (with `;`, `&&`, a pipe or a
    /// substitution) is theirs and never removed.
    static func isOurStatusline(_ value: Any?) -> Bool {
        guard let o = value as? [String: Any], let command = o["command"] as? String else { return false }
        let c = command.trimmingCharacters(in: .whitespaces)
        if [";", "&", "|", "$(", "`", "\n"].contains(where: { c.contains($0) }) { return false }
        let name = IntegrationScripts.statuslineScript
        return c.hasSuffix(name) || c.hasSuffix(name + "'") || c.hasSuffix(name + "\"")
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
        let value: Any?
        switch kind {
        case .meters:
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
        return exact == [scripts.installedModPath] && scripts.isCurrent && scripts.hasMod ? .installed : .outdated
    }

    /// Sanduhr's entry names the current scripts through the stable link, with a python3 that
    /// exists, and the scripts there are the app's.
    private func isCurrent(_ kind: IntegrationKind, _ value: Any) -> Bool {
        guard scripts.isCurrent, let o = value as? [String: Any] else { return false }
        let fm = FileManager.default
        switch kind {
        case .meters:
            return false
        case .mcp:
            guard let command = o["command"] as? String, fm.isExecutableFile(atPath: command),
                  let args = o["args"] as? [String] else { return false }
            return args == [scripts.installedPath(IntegrationScripts.mcpScript)]
        case .statusline:
            guard let command = o["command"] as? String else { return false }
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
        // The mod joins a list: nothing of anyone's is replaced.
        if kind == .meters { return nil }
        let file = configFile(kind, folder: folder)
        guard let data = FileManager.default.contents(atPath: file), (try? JSONEdit.root(data)) != nil else { return nil }
        let b = Array(data)
        guard let top = try? JSONEdit.topObject(b) else { return nil }
        let member: JSONEdit.Member?
        switch kind {
        case .meters:
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
    @discardableResult
    func install(_ kind: IntegrationKind, folder: String, python: String,
                 replaceOther: Bool = false) throws -> Outcome {
        do {
            try scripts.refresh()
        } catch IntegrationScripts.Failure.missingFromApp {
            throw Failure.scriptsMissing
        } catch {
            throw Failure.writeFailed(file: scripts.dir.path)
        }
        if kind == .meters && !scripts.hasMod { throw Failure.scriptsMissing }
        let file = configFile(kind, folder: folder)
        let value = entry(kind, python: python)
        var receipts = loadReceipts()
        for _ in 0..<3 {
            let original = try read(file)
            let plan = try malformedNamed(file) {
                try planInstall(kind, bytes: original ?? Array("{}\n".utf8), value: value,
                                prior: receipts.first { $0.file == file && $0.kind == kind },
                                createdFile: original == nil)
            }
            if let other = plan.other, !replaceOther { return .needsReplaceConsent(existing: other) }
            guard try commit(file, original: original, bytes: plan.bytes, backup: true) else { continue }
            var receipt = plan.receipt
            receipt.folder = AccountData.normalized(folder)
            receipt.file = file
            receipts.removeAll { $0.file == file && $0.kind == kind }
            receipts.append(receipt)
            saveReceipts(receipts)
            return .installed
        }
        throw Failure.writeFailed(file: file)
    }

    /// Runs `body`, naming `file` in a malformed-JSON failure.
    private func malformedNamed<T>(_ file: String, _ body: () throws -> T) throws -> T {
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

    private func planInstall(_ kind: IntegrationKind, bytes b: [UInt8], value: JSONEdit.Value,
                             prior: IntegrationReceipt?, createdFile: Bool) throws -> Plan {
        let root = try parse(b)
        let top = try JSONEdit.topObject(b)
        var receipt = IntegrationReceipt(folder: "", file: "", kind: kind, createdFile: createdFile)
        var result: [UInt8]
        var other: String?
        switch kind {
        case .meters:
            return try planInstallMeters(bytes: b, root: root, top: top, receipt: receipt, prior: prior)
        case .statusline:
            let existing = root[Self.statusLineKey]
            if let m = top.member(Self.statusLineKey) {
                if Self.isOurStatusline(existing) {
                    // Updating Sanduhr's own entry keeps what the first install recorded.
                    if let prior { receipt = prior }
                } else {
                    receipt.previous = String(decoding: JSONEdit.text(b, m), as: UTF8.self)
                    other = (existing as? [String: Any])?["command"] as? String
                        ?? receipt.previous
                }
            }
            let r = JSONEdit.set(b, in: top, key: Self.statusLineKey, value: value)
            if let inner = r.emptyInner { receipt.emptyInner = String(decoding: inner, as: UTF8.self) }
            result = r.bytes
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

    /// The mod's install: its folder joins `env.CLAUDE_CODE_PLUGIN_DIRS`, the object and the key
    /// made when missing, every other entry of the list left as it was.
    private func planInstallMeters(bytes b: [UInt8], root: [String: Any], top: JSONEdit.Object,
                                   receipt start: IntegrationReceipt, prior: IntegrationReceipt?) throws -> Plan {
        let ours = scripts.installedModPath
        var receipt = start
        var result: [UInt8]
        if let m = top.member(Self.envKey) {
            guard let env = root[Self.envKey] as? [String: Any] else { throw Failure.malformed(file: "") }
            let envObject = try JSONEdit.object(b, at: m.valueStart)
            if let km = envObject.member(Self.pluginDirsKey) {
                guard let list = env[Self.pluginDirsKey] as? String else { throw Failure.malformed(file: "") }
                if let prior {
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
        try verifyMeters(before: b, after: result, installed: true)
        return Plan(bytes: result, receipt: receipt, other: nil)
    }

    // MARK: Remove

    /// Takes Sanduhr's entry for `kind` out of `folder`'s config, undoing what Install did.
    /// An entry that isn't Sanduhr's stays. With nothing installed anywhere any more, the
    /// scripts go too.
    func remove(_ kind: IntegrationKind, folder: String) throws {
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
        try verifyMeters(before: b, after: result, installed: false)
        return result
    }

    // MARK: Checks

    private func parse(_ b: [UInt8]) throws -> [String: Any] {
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
        case .meters:
            // The mod's edits are checked by verifyMeters.
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
    private func verifyMeters(before: [UInt8], after: [UInt8], installed: Bool) throws {
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
        guard ours == (installed ? [scripts.installedModPath] : []) else { throw Failure.malformed(file: "") }
    }

    // MARK: Files

    /// The file's bytes, nil when it doesn't exist.
    private func read(_ file: String) throws -> [UInt8]? {
        guard FileManager.default.fileExists(atPath: file) else { return nil }
        guard let data = FileManager.default.contents(atPath: file) else { throw Failure.writeFailed(file: file) }
        return Array(data)
    }

    /// Writes `bytes` (nil deletes the file) unless the file changed since `original` was read;
    /// returns false then, for the caller to start over. With `backup` (Install), keeps a copy
    /// of the file before Sanduhr's first change; deletes that copy once the file is back to
    /// what it holds.
    private func commit(_ file: String, original: [UInt8]?, bytes: [UInt8]?, backup makeBackup: Bool) throws -> Bool {
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

    private func saveReceipts(_ receipts: [IntegrationReceipt]) {
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
    func installedCounts(folders: [String]) -> (mcp: Int, statusline: Int, meters: Int) {
        var all: [String] = []
        for f in folders + installedFolders() {
            let n = AccountData.normalized(f)
            if !all.contains(n) { all.append(n) }
        }
        return (all.filter { status(.mcp, folder: $0).isOurs }.count,
                all.filter { status(.statusline, folder: $0).isOurs }.count,
                all.filter { status(.meters, folder: $0).isOurs }.count)
    }

    /// The folders Sanduhr installed into (Settings lists them even when discovery doesn't).
    func installedFolders() -> [String] {
        var seen: [String] = []
        for r in loadReceipts() where !seen.contains(r.folder) { seen.append(r.folder) }
        return seen
    }
}
