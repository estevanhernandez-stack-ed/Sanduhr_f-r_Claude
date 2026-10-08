import Foundation

/// Where Sanduhr's own mod stands in one Claude Code folder (item 64, slice 2), read from that
/// folder's settings.json and Sanduhr's receipt.
struct OwnModState: Equatable, Sendable {
    let folder: String
    /// settings.json is JSON Sanduhr can edit (or absent).
    var readable = true
    /// Sanduhr's entry in `env.CLAUDE_CODE_PLUGIN_DIRS`, as written; nil when the list has none.
    var entry: String?
    /// The stamped version the entry pins; nil when it goes through the `current` link.
    var pinned: String?
    /// `enabledPlugins["sanduhr-meters@inline"]` in this settings.json, when it is a true or false.
    var inline: Bool?
    /// What Sanduhr's switch wrote there, when it wrote it.
    var switched: Bool?
    /// A receipt of Sanduhr's covers this folder's mod (its entry or its switch).
    var hasReceipt = false

    var listed: Bool { entry != nil }
    /// Loads in new sessions, as far as this settings.json says.
    var isOn: Bool { listed && inline != false }
}

/// A higher-priority settings source that sets the mod's `enabledPlugins` key (item 64): Claude
/// Code merges `enabledPlugins` key by key, so a project's true beats the user's false.
struct ModOverride: Equatable, Sendable {
    enum Source: Equatable, Sendable {
        /// A project's `.claude/settings.json`.
        case project(String)
        /// A project's `.claude/settings.local.json`.
        case local(String)
        /// The organization's managed settings.
        case managed
    }

    let source: Source
    let value: Bool
}

/// Finds the settings sources above a Claude Code folder's own settings.json that set a key of
/// `enabledPlugins`: the managed settings file, and each project the folder's `.claude.json`
/// lists (its `.claude/settings.json` and `.claude/settings.local.json`). Reads files only.
enum ModOverrides {
    static let managedFile = "/Library/Application Support/ClaudeCode/managed-settings.json"
    /// Projects read at most, so a folder with years of history stays quick.
    static let projectLimit = 400

    static func find(key: String, folder: String, home: String, managed: String = managedFile) -> [ModOverride] {
        var out: [ModOverride] = []
        if let v = value(key, in: managed) { out.append(ModOverride(source: .managed, value: v)) }
        let config = ClaudeCodeFolders.configFile(for: folder, home: home)
        guard let data = FileManager.default.contents(atPath: config),
              let o = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any],
              let projects = o["projects"] as? [String: Any] else { return out }
        for project in projects.keys.sorted().prefix(projectLimit) {
            let dir = (project as NSString).appendingPathComponent(".claude")
            if let v = value(key, in: (dir as NSString).appendingPathComponent("settings.local.json")) {
                out.append(ModOverride(source: .local(project), value: v))
            }
            if let v = value(key, in: (dir as NSString).appendingPathComponent("settings.json")) {
                out.append(ModOverride(source: .project(project), value: v))
            }
        }
        return out
    }

    /// The sentence the Mods page shows after switching the mod `on` or off, nil when nothing
    /// above the folder's settings says otherwise.
    static func message(_ overrides: [ModOverride], on: Bool, home: String) -> String? {
        let against = overrides.filter { $0.value != on }
        guard let first = against.first else { return nil }
        let state = on ? "off" : "on"
        if against.contains(where: { $0.source == .managed }) {
            return "Your organization's managed settings keep it \(state)."
        }
        let projects = against.compactMap { o -> String? in
            switch o.source {
            case .project(let p), .local(let p): return p
            case .managed: return nil
            }
        }
        var unique: [String] = []
        for p in projects where !unique.contains(p) { unique.append(p) }
        let file = if case .local = first.source { "settings.local.json" } else { "settings.json" }
        let shown = ClaudeCodeFolders.Folder(path: AccountData.normalized(unique.first ?? "")).display(home: home)
        var text = "A project setting keeps it \(state) in \(shown) (.claude/\(file))"
        if unique.count > 1 { text += " and \(unique.count - 1) other project\(unique.count == 2 ? "" : "s")" }
        return text + "."
    }

    /// The key's value in one settings file, when it is a true or false there.
    static func value(_ key: String, in file: String) -> Bool? {
        guard let data = FileManager.default.contents(atPath: file),
              let o = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any],
              let enabled = o[IntegrationInstaller.enabledPluginsKey] as? [String: Any] else { return nil }
        return IntegrationInstaller.boolValue(enabled[key])
    }
}

/// What an update of a pinned version found.
enum ModUpdateOutcome: Equatable, Sendable {
    /// The entry already names the app's version, or follows the `current` link.
    case upToDate
    /// The entry now names the new version.
    case updated
    /// The new version can do things the old one couldn't: nothing was changed.
    case needsConsent(added: [String])
}

/// Sanduhr's own mod on the Mods page (item 64, slice 2): an on/off switch per Claude Code
/// folder, Update between pinned versions and Remove, each through the receipt in
/// `installs.json` so it undoes byte for byte.
///
/// **Off** writes `enabledPlugins["sanduhr-meters@inline"]: false` into the folder's
/// settings.json (the documented switch for a plugin folder mod), keeping the list entry, the
/// version and the mod's own options; the receipt's `switched` records the member as it was.
/// **On** undoes that switch; where the user's own false is there it writes true (recording the
/// false); where the list has no entry it adds one pinned to the app's stamped version (the
/// receipt's `pinned`). **Remove** is Integrations' Remove: the switch first, then the entry.
extension IntegrationInstaller {
    static let enabledPluginsKey = "enabledPlugins"
    static var ownModKey: String { "\(IntegrationScripts.modName)@inline" }

    /// A JSON true or false (never a number that bridges to one).
    static func boolValue(_ value: Any?) -> Bool? {
        guard let n = value as? NSNumber, CFGetTypeID(n) == CFBooleanGetTypeID() else { return nil }
        return n.boolValue
    }

    /// The meters receipt for `file`.
    private func metersReceipt(_ receipts: [IntegrationReceipt], file: String) -> Int? {
        receipts.firstIndex { $0.file == file && $0.kind == .meters }
    }

    func ownModState(folder: String) -> OwnModState {
        let normalized = AccountData.normalized(folder)
        let file = configFile(.meters, folder: folder)
        var state = OwnModState(folder: normalized)
        let receipts = loadReceipts()
        if let i = metersReceipt(receipts, file: file) {
            state.hasReceipt = true
            state.switched = receipts[i].switched?.value
        }
        guard let data = FileManager.default.contents(atPath: file) else { return state }
        guard let root = try? JSONEdit.root(data) else {
            state.readable = false
            return state
        }
        if let env = root[Self.envKey] as? [String: Any], let list = env[Self.pluginDirsKey] as? String {
            state.entry = list.components(separatedBy: Self.pluginDirsSeparator)
                .map { $0.trimmingCharacters(in: .whitespaces) }.first(where: Self.isOurModEntry)
            state.pinned = state.entry.flatMap(scripts.pinnedStamp(of:))
        }
        if let enabled = root[Self.enabledPluginsKey] as? [String: Any] {
            state.inline = Self.boolValue(enabled[Self.ownModKey])
        }
        return state
    }

    /// Switches Sanduhr's mod on or off for `folder`.
    func switchOwnMod(on: Bool, folder: String) throws {
        let state = ownModState(folder: folder)
        guard state.readable else { throw Failure.malformed(file: configFile(.meters, folder: folder)) }
        if on {
            if state.switched == false {
                try undoModSwitch(folder: folder)
            } else if state.inline == false {
                try writeModSwitch(true, folder: folder)
            }
            if !state.listed {
                guard scripts.hasMod else { throw Failure.scriptsMissing }
                let stamp: String
                do { stamp = try scripts.refresh() } catch { throw Failure.scriptsMissing }
                try install(.meters, folder: folder, python: "", pinned: stamp)
            }
        } else {
            guard state.listed else { return }
            if state.switched == true {
                // Sanduhr's true overrode the user's own false: putting that back turns it off.
                try undoModSwitch(folder: folder)
                if ownModState(folder: folder).isOn { try writeModSwitch(false, folder: folder) }
            } else if state.inline != false {
                try writeModSwitch(false, folder: folder)
            }
        }
    }

    /// Moves a pinned entry to the app's version. `added` names what the new version's folder
    /// can do that the old one's couldn't (the validator's calls, or the static scan); unless
    /// `confirmed`, anything there stops the update for the question.
    func updateOwnMod(folder: String, confirmed: Bool,
                      added: (_ old: String, _ new: String) -> [String]) throws -> ModUpdateOutcome {
        let state = ownModState(folder: folder)
        guard state.listed, let old = state.pinned else { return .upToDate }
        guard scripts.hasMod, let new = scripts.bundledStamp else { throw Failure.scriptsMissing }
        guard new != old else { return .upToDate }
        do { try scripts.refresh() } catch { throw Failure.scriptsMissing }
        let more = added(scripts.pinnedModPath(old), scripts.pinnedModPath(new))
        if !more.isEmpty && !confirmed { return .needsConsent(added: more) }
        try install(.meters, folder: folder, python: "", pinned: new)
        // The old version goes once no receipt pins it.
        if let current = scripts.installedStamp { scripts.prune(keep: [current] + scripts.pinnedStamps) }
        return .updated
    }

    /// Takes out what the switch wrote, back to the member's old bytes. A member someone changed
    /// since is left as it is; the switch's record goes either way.
    func undoModSwitch(folder: String) throws {
        let file = configFile(.meters, folder: folder)
        var receipts = loadReceipts()
        guard let i = metersReceipt(receipts, file: file), let made = receipts[i].switched else { return }
        var done = false
        for _ in 0..<3 {
            guard let original = try read(file) else { done = true; break }
            guard let edit = try malformedNamed(file, { try planUndoSwitch(bytes: original, made) }) else {
                done = true
                break
            }
            if try commit(file, original: original, bytes: edit, backup: false) {
                done = true
                break
            }
        }
        guard done else { throw Failure.writeFailed(file: file) }
        receipts[i].switched = nil
        // A receipt that held only the switch has nothing left to undo.
        if !receipts[i].recordsList { receipts.remove(at: i) }
        saveReceipts(receipts)
    }

    /// Writes `enabledPlugins["sanduhr-meters@inline"] = value` and records what was there.
    private func writeModSwitch(_ value: Bool, folder: String) throws {
        let file = configFile(.meters, folder: folder)
        var receipts = loadReceipts()
        for _ in 0..<3 {
            guard let original = try read(file) else { throw Failure.malformed(file: file) }
            let (bytes, made) = try malformedNamed(file) { try planSwitch(bytes: original, value: value) }
            guard try commit(file, original: original, bytes: bytes, backup: true) else { continue }
            if let i = metersReceipt(receipts, file: file) {
                receipts[i].switched = made
            } else {
                receipts.append(IntegrationReceipt(folder: AccountData.normalized(folder), file: file, kind: .meters,
                                                   switched: made))
            }
            saveReceipts(receipts)
            return
        }
        throw Failure.writeFailed(file: file)
    }

    private func planSwitch(bytes b: [UInt8], value: Bool) throws -> ([UInt8], SwitchReceipt) {
        let root = try parse(b)
        let top = try JSONEdit.topObject(b)
        var made = SwitchReceipt(key: Self.ownModKey, value: value)
        let result: [UInt8]
        if let m = top.member(Self.enabledPluginsKey) {
            guard root[Self.enabledPluginsKey] is [String: Any] else { throw Failure.malformed(file: "") }
            let r = JSONEdit.set(b, in: try JSONEdit.object(b, at: m.valueStart), key: made.key, value: .bool(value))
            made.previous = r.previous.map { String(decoding: $0, as: UTF8.self) }
            made.emptyInner = r.emptyInner.map { String(decoding: $0, as: UTF8.self) }
            result = r.bytes
        } else {
            let r = JSONEdit.set(b, in: top, key: Self.enabledPluginsKey,
                                 value: .object([JSONEdit.Pair(made.key, .bool(value))]))
            made.createdParent = true
            made.emptyInner = r.emptyInner.map { String(decoding: $0, as: UTF8.self) }
            result = r.bytes
        }
        try verifySwitch(before: b, after: result, key: made.key, expect: value)
        return (result, made)
    }

    /// The undo's edit, nil when the member isn't what the switch wrote any more.
    private func planUndoSwitch(bytes b: [UInt8], _ made: SwitchReceipt) throws -> [UInt8]? {
        let root = try parse(b)
        let top = try JSONEdit.topObject(b)
        guard let m = top.member(Self.enabledPluginsKey), let enabled = root[Self.enabledPluginsKey] as? [String: Any],
              Self.boolValue(enabled[made.key]) == made.value else { return nil }
        let object = try JSONEdit.object(b, at: m.valueStart)
        let inner = made.emptyInner.map { Array($0.utf8) }
        var result: [UInt8]
        var expect: Any?
        if let previous = made.previous {
            result = JSONEdit.restore(b, in: object, key: made.key, previous: Array(previous.utf8))
            expect = try? JSONSerialization.jsonObject(with: Data(previous.utf8), options: [.fragmentsAllowed])
        } else if made.createdParent {
            result = JSONEdit.remove(b, in: object, key: made.key)
            let after = try JSONEdit.topObject(result)
            if let em = after.member(Self.enabledPluginsKey),
               (try JSONEdit.object(result, at: em.valueStart)).members.isEmpty {
                result = JSONEdit.remove(result, in: after, key: Self.enabledPluginsKey, emptyInner: inner)
            }
        } else {
            result = JSONEdit.remove(b, in: object, key: made.key, emptyInner: inner)
        }
        try verifySwitch(before: b, after: result, key: made.key, expect: expect)
        return result
    }

    /// The switch's edit changed that one member and nothing else (an `enabledPlugins` left
    /// empty counts as absent), and the member holds `expect` (absent for nil).
    private func verifySwitch(before: [UInt8], after: [UInt8], key: String, expect: Any?) throws {
        let old = try parse(before)
        guard let new = try? JSONEdit.root(Data(after)) else { throw Failure.malformed(file: "") }
        func split(_ root: [String: Any]) -> (rest: [String: Any], others: [String: Any], value: Any?) {
            var rest = root
            var enabled = rest.removeValue(forKey: Self.enabledPluginsKey) as? [String: Any] ?? [:]
            let value = enabled.removeValue(forKey: key)
            return (rest, enabled, value)
        }
        let (oldRest, oldOthers, _) = split(old)
        let (newRest, newOthers, got) = split(new)
        guard NSDictionary(dictionary: newRest).isEqual(to: oldRest),
              NSDictionary(dictionary: newOthers).isEqual(to: oldOthers) else { throw Failure.malformed(file: "") }
        switch (got, expect) {
        case (nil, nil): return
        case (let g?, let e?):
            guard NSArray(array: [g]).isEqual(to: [e]) else { throw Failure.malformed(file: "") }
        default:
            throw Failure.malformed(file: "")
        }
    }
}

extension IntegrationReceipt {
    /// It records an entry Install put in the plugin folders list (a key it made, or the list
    /// before), not only a Mods page switch.
    var recordsList: Bool { previous != nil || createdKey != nil }
}

/// What a version of Sanduhr's mod can do, for Update's question (item 64, slice 2).
enum ModCapabilities {
    /// What `new` can do that `old` couldn't. With both validator reports, the `$` calls and the
    /// gating hooks they list; otherwise (no `claude`, or a report missing) the static scan's
    /// capabilities for both, so the two sides are always read the same way.
    static func added(old: String, new: String, validate: (String) -> ModCheckReport?) -> [String] {
        if let a = validate(old), let b = validate(new) {
            let have = Set(a.calls + a.gating.map { "gating " + $0 })
            return (b.calls + b.gating.map { "gating " + $0 }).filter { !have.contains($0) }
                .reduce(into: [String]()) { if !$0.contains($1) { $0.append($1) } }
        }
        func scan(_ path: String) -> [String] {
            let dir = URL(fileURLWithPath: path)
            guard FileManager.default.fileExists(atPath: dir.path) else { return [] }
            return ModInventory.touches(dir, isMod: true).capabilities.map(\.title)
        }
        let have = Set(scan(old))
        return scan(new).filter { !have.contains($0) }
    }
}
