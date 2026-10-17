import Foundation

/// What Sanduhr did to one folder's settings.json for one kept mod (item 75, slice F2), so Off
/// undoes exactly that. Kept in `mods/installs.json` beside the kept copies, owner-only.
struct KeptModReceipt: Codable, Equatable, Sendable {
    var folder: String
    var file: String
    /// The kept mod's name: one entry per name per folder.
    var name: String
    /// The version the entry names now.
    var stamp: String
    /// `env` didn't exist: Off removes it once it is empty again.
    var createdParent = false
    /// `CLAUDE_CODE_PLUGIN_DIRS` didn't exist: Off removes it once only this entry is left.
    var createdKey = false
    /// What sat between the braces of the object the member went into when it was empty.
    var emptyInner: String?
    /// The plugin folders list before the entry joined it, as text: Off puts those bytes back
    /// when the list is otherwise what it was.
    var previous: String?
}

/// Where a kept mod stands in one folder.
enum KeptModState: Equatable, Sendable {
    case off
    /// The folder's plugin folders list names this kept version.
    case on
    /// It names another kept version of the same mod.
    case otherVersion(String)
}

/// Turns kept mods on and off per Claude Code folder (slice F2): the kept copy's folder joins or
/// leaves `env.CLAUDE_CODE_PLUGIN_DIRS` in the folder's settings.json, by receipt, every other
/// entry and byte left as it was. Writes go through the installer's checked commit.
extension KeptMods {
    private var ledger: URL { dir.appendingPathComponent("installs.json") }

    func loadReceipts() -> [KeptModReceipt] {
        guard let data = FileManager.default.contents(atPath: ledger.path) else { return [] }
        return (try? JSONDecoder().decode([KeptModReceipt].self, from: data)) ?? []
    }

    private func saveReceipts(_ receipts: [KeptModReceipt]) throws {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        do {
            try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
            if receipts.isEmpty {
                if FileManager.default.fileExists(atPath: ledger.path) { try FileManager.default.removeItem(at: ledger) }
                return
            }
            try HandoffFiles.writeOwnerOnlyOrThrow(encoder.encode(receipts), to: ledger)
        } catch {
            throw Failure.writeFailed
        }
    }

    /// The kept mod an entry of the plugin folders list names: `<dir>/<name>/<stamp>`.
    func keptEntry(_ entry: String) -> (name: String, stamp: String)? {
        let e = entry.trimmingCharacters(in: .whitespaces)
        let prefix = dir.path + "/"
        guard e.hasPrefix(prefix) else { return nil }
        let parts = e.dropFirst(prefix.count).split(separator: "/", omittingEmptySubsequences: false)
        guard parts.count == 2, Self.isName(String(parts[0])), IntegrationScripts.isStampName(String(parts[1])) else { return nil }
        return (String(parts[0]), String(parts[1]))
    }

    /// The folder's plugin folders list, split; empty without one.
    static func pluginDirs(_ root: [String: Any]) -> [String] {
        guard let env = root[IntegrationInstaller.envKey] as? [String: Any],
              let list = env[IntegrationInstaller.pluginDirsKey] as? String else { return [] }
        return list.components(separatedBy: IntegrationInstaller.pluginDirsSeparator)
    }

    func state(_ mod: KeptMod, folder: String, installer: IntegrationInstaller) -> KeptModState {
        let file = installer.configFile(.meters, folder: folder)
        guard let data = FileManager.default.contents(atPath: file), let root = try? JSONEdit.root(data) else { return .off }
        let stamps = Self.pluginDirs(root).compactMap(keptEntry).filter { $0.name == mod.name }.map(\.stamp)
        if stamps.contains(mod.stamp) { return .on }
        return stamps.first.map(KeptModState.otherVersion) ?? .off
    }

    /// The folders whose list names any version of `name`.
    func folders(using name: String) -> [String] {
        loadReceipts().filter { $0.name == name }.map(\.folder)
    }

    /// On: the kept copy joins the folder's plugin folders list, replacing another kept version
    /// of the same mod where it stands.
    func turnOn(_ mod: KeptMod, folder: String, installer: IntegrationInstaller) throws {
        let file = installer.configFile(.meters, folder: folder)
        let ours = path(mod)
        var receipts = loadReceipts()
        let prior = receipts.first { $0.file == file && $0.name == mod.name }
        for _ in 0..<3 {
            let original = try installer.read(file)
            let bytes = original ?? Array("{}\n".utf8)
            var receipt = KeptModReceipt(folder: AccountData.normalized(folder), file: file, name: mod.name, stamp: mod.stamp)
            let result: [UInt8]
            do {
                (result, receipt) = try planOn(bytes, ours: ours, name: mod.name, receipt: receipt, prior: prior, installer: installer)
            } catch {
                throw IntegrationInstaller.Failure.malformed(file: file)
            }
            guard try installer.commit(file, original: original, bytes: result, backup: true) else { continue }
            receipts.removeAll { $0.file == file && $0.name == mod.name }
            receipts.append(receipt)
            try saveReceipts(receipts)
            return
        }
        throw IntegrationInstaller.Failure.writeFailed(file: file)
    }

    /// Off: the kept mod's entry leaves the list, and what On made goes with it.
    func turnOff(_ name: String, folder: String, installer: IntegrationInstaller) throws {
        let file = installer.configFile(.meters, folder: folder)
        var receipts = loadReceipts()
        let receipt = receipts.first { $0.file == file && $0.name == name }
        var done = false
        for _ in 0..<3 {
            guard let original = try installer.read(file) else { done = true; break }
            let edit: [UInt8]?
            do { edit = try planOff(original, name: name, receipt: receipt, installer: installer) } catch {
                throw IntegrationInstaller.Failure.malformed(file: file)
            }
            guard let edit else { done = true; break }
            if try installer.commit(file, original: original, bytes: edit, backup: false) {
                done = true
                break
            }
        }
        guard done else { throw IntegrationInstaller.Failure.writeFailed(file: file) }
        receipts.removeAll { $0.file == file && $0.name == name }
        try saveReceipts(receipts)
    }

    // MARK: The edits

    private struct Malformed: Error {}

    /// The list with `ours` in it: another kept version of `name` replaced where it stands (later
    /// ones dropped), else `ours` last.
    func adding(_ ours: String, name: String, to value: String) -> String {
        let sep = IntegrationInstaller.pluginDirsSeparator
        let parts = value.components(separatedBy: sep)
        let isOld = { (p: String) in self.keptEntry(p)?.name == name }
        if parts.contains(where: isOld) {
            var out: [String] = []
            var placed = false
            for p in parts {
                if !isOld(p) {
                    out.append(p)
                } else if !placed {
                    out.append(ours)
                    placed = true
                }
            }
            return out.joined(separator: sep)
        }
        if value.trimmingCharacters(in: .whitespaces).isEmpty { return ours }
        return value + sep + ours
    }

    private func planOn(_ b: [UInt8], ours: String, name: String, receipt start: KeptModReceipt, prior: KeptModReceipt?,
                        installer: IntegrationInstaller) throws -> ([UInt8], KeptModReceipt) {
        let envKey = IntegrationInstaller.envKey, dirsKey = IntegrationInstaller.pluginDirsKey
        let root = try installer.parse(b)
        let top = try JSONEdit.topObject(b)
        var receipt = start
        var result: [UInt8]
        if let m = top.member(envKey) {
            guard let env = root[envKey] as? [String: Any] else { throw Malformed() }
            let envObject = try JSONEdit.object(b, at: m.valueStart)
            if let km = envObject.member(dirsKey) {
                guard let list = env[dirsKey] as? String else { throw Malformed() }
                // Replacing a version this folder already has keeps what the first On recorded.
                if let prior, list.components(separatedBy: IntegrationInstaller.pluginDirsSeparator)
                    .contains(where: { keptEntry($0)?.name == name }) {
                    receipt = prior
                    receipt.stamp = start.stamp
                } else {
                    receipt.previous = String(decoding: JSONEdit.text(b, km), as: UTF8.self)
                }
                let updated = adding(ours, name: name, to: list)
                result = updated == list ? b : JSONEdit.set(b, in: envObject, key: dirsKey, value: .string(updated)).bytes
            } else {
                let r = JSONEdit.set(b, in: envObject, key: dirsKey, value: .string(ours))
                receipt.createdKey = true
                receipt.emptyInner = r.emptyInner.map { String(decoding: $0, as: UTF8.self) }
                result = r.bytes
            }
        } else {
            let r = JSONEdit.set(b, in: top, key: envKey, value: .object([JSONEdit.Pair(dirsKey, .string(ours))]))
            receipt.createdParent = true
            receipt.createdKey = true
            receipt.emptyInner = r.emptyInner.map { String(decoding: $0, as: UTF8.self) }
            result = r.bytes
        }
        try verify(before: b, after: result, name: name, expect: ours, installer: installer)
        return (result, receipt)
    }

    private func planOff(_ b: [UInt8], name: String, receipt: KeptModReceipt?, installer: IntegrationInstaller) throws -> [UInt8]? {
        let envKey = IntegrationInstaller.envKey, dirsKey = IntegrationInstaller.pluginDirsKey
        let sep = IntegrationInstaller.pluginDirsSeparator
        let root = try installer.parse(b)
        let top = try JSONEdit.topObject(b)
        guard let m = top.member(envKey), let env = root[envKey] as? [String: Any], let list = env[dirsKey] as? String else {
            return nil
        }
        let parts = list.components(separatedBy: sep)
        guard parts.contains(where: { keptEntry($0)?.name == name }) else { return nil }
        let remaining = parts.filter { keptEntry($0)?.name != name }.joined(separator: sep)
        let envObject = try JSONEdit.object(b, at: m.valueStart)
        let inner = receipt?.emptyInner.map { Array($0.utf8) }
        var result: [UInt8]
        if remaining.trimmingCharacters(in: .whitespaces).isEmpty && receipt?.createdKey == true {
            if receipt?.createdParent == true {
                result = JSONEdit.remove(b, in: envObject, key: dirsKey)
                let after = try JSONEdit.topObject(result)
                if let em = after.member(envKey), (try JSONEdit.object(result, at: em.valueStart)).members.isEmpty {
                    result = JSONEdit.remove(result, in: after, key: envKey, emptyInner: inner)
                }
            } else {
                result = JSONEdit.remove(b, in: envObject, key: dirsKey, emptyInner: inner)
            }
        } else if let previous = receipt?.previous,
                  let was = try? JSONSerialization.jsonObject(with: Data(previous.utf8), options: [.fragmentsAllowed]) as? String,
                  was == remaining {
            result = JSONEdit.restore(b, in: envObject, key: dirsKey, previous: Array(previous.utf8))
        } else {
            result = JSONEdit.set(b, in: envObject, key: dirsKey, value: .string(remaining)).bytes
        }
        try verify(before: b, after: result, name: name, expect: nil, installer: installer)
        return result
    }

    /// The edit changed this mod's entry and nothing else: the files agree once the list (and an
    /// `env` left empty) is set aside, the list's other entries are the same in the same order,
    /// and the mod's entry is `expect` once, or gone.
    private func verify(before: [UInt8], after: [UInt8], name: String, expect: String?, installer: IntegrationInstaller) throws {
        let envKey = IntegrationInstaller.envKey, dirsKey = IntegrationInstaller.pluginDirsKey
        let old = try installer.parse(before)
        guard let new = try? JSONEdit.root(Data(after)) else { throw Malformed() }
        func split(_ root: [String: Any]) -> (rest: [String: Any], dirs: [String]) {
            var rest = root
            var env = rest.removeValue(forKey: envKey) as? [String: Any]
            let list = env?.removeValue(forKey: dirsKey) as? String
            if let env, !env.isEmpty { rest[envKey] = env }
            return (rest, list.map { $0.components(separatedBy: IntegrationInstaller.pluginDirsSeparator) } ?? [])
        }
        let (oldRest, oldDirs) = split(old)
        let (newRest, newDirs) = split(new)
        guard NSDictionary(dictionary: newRest).isEqual(to: oldRest) else { throw Malformed() }
        let others = { (dirs: [String]) in
            dirs.filter { self.keptEntry($0)?.name != name && !$0.trimmingCharacters(in: .whitespaces).isEmpty }
        }
        guard others(oldDirs) == others(newDirs) else { throw Malformed() }
        let ours = newDirs.filter { keptEntry($0)?.name == name }.map { $0.trimmingCharacters(in: .whitespaces) }
        guard ours == (expect.map { [$0] } ?? []) else { throw Malformed() }
    }
}
