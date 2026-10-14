import AppKit
import Foundation

/// A mod Claude made in a session and Sanduhr kept (item 75, slice F): its files copied unchanged
/// into `…/Application Support/Sanduhr/mods/<name>/<stamp>/`, read-only, so Claude Code's cleanup
/// of the session's folder doesn't take it. Kept is all: no Claude Code folder loads it, and
/// nothing of it runs until Try in a Session.
struct KeptMod: Codable, Equatable, Identifiable, Sendable {
    /// The manifest's name.
    var name: String
    var version: String?
    var description = ""
    /// The first 12 hex digits of a SHA-256 over its files' names and bytes: its folder's name.
    var stamp: String
    var keptAt: Date
    /// The first 8 characters of the session that made it.
    var session: String
    var files: Int
    var bytes: Int

    var id: String { "\(name)/\(stamp)" }
}

/// Keeps, lists and forgets mods made in sessions. Reads only the session's folder; writes only
/// under its own folder.
struct KeptMods: Sendable {
    /// `…/Application Support/Sanduhr/mods`.
    let dir: URL

    static var standard: KeptMods {
        KeptMods(dir: HistoryStore.Files.standard.dir.appendingPathComponent("mods", isDirectory: true))
    }

    static let recordName = "kept.json"
    static let maxFiles = 200
    static let maxFileBytes = 1 << 20
    static let maxBytes = 4 << 20

    enum Failure: Error, Equatable {
        /// No `.claude-plugin/plugin.json`.
        case notAPlugin
        /// The manifest's name isn't lowercase letters, digits and hyphens.
        case badName(String)
        /// Sanduhr's own mod's name.
        case reserved
        /// A link inside it: Sanduhr copies files, never what a link points at.
        case hasLink(String)
        case tooBig
        case writeFailed

        var message: String {
            switch self {
            case .notAPlugin: "It has no .claude-plugin/plugin.json, so it isn't a plugin Sanduhr can keep."
            case .badName(let n): "Its name, \"\(n)\", isn't lowercase letters, digits and hyphens."
            case .reserved: "It's named like Sanduhr's own mod. Rename it in plugin.json, then keep it."
            case .hasLink(let p): "It holds a link (\(p)). Sanduhr keeps plain files only."
            case .tooBig: "It's larger than Sanduhr keeps: \(KeptMods.maxFiles) files, 1 MB each, 4 MB in all."
            case .writeFailed: "Sanduhr couldn't write the copy."
            }
        }
    }

    private var record: URL { dir.appendingPathComponent(Self.recordName) }

    /// The kept copy's folder.
    func path(_ mod: KeptMod) -> String {
        dir.appendingPathComponent(mod.name).appendingPathComponent(mod.stamp).path
    }

    /// The kept mods, newest first; only those whose folder is still there.
    func list() -> [KeptMod] {
        guard let data = FileManager.default.contents(atPath: record.path) else { return [] }
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        let mods = (try? decoder.decode([KeptMod].self, from: data)) ?? []
        return mods.filter { FileManager.default.fileExists(atPath: path($0)) }.sorted { $0.keptAt > $1.keptAt }
    }

    private func save(_ mods: [KeptMod]) throws {
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        guard let data = try? encoder.encode(mods) else { throw Failure.writeFailed }
        do {
            try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
            try data.write(to: record, options: .atomic)
            try FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: record.path)
        } catch {
            throw Failure.writeFailed
        }
    }

    // MARK: Reading a session's mod

    static func isName(_ s: String) -> Bool {
        s.range(of: #"^[a-z0-9][a-z0-9-]{0,63}$"#, options: .regularExpression) != nil
    }

    /// The files a mod is kept with, relative and sorted: every plain file except what Claude
    /// Code writes there (its types), installed packages and Finder's litter. A link anywhere
    /// refuses the whole mod.
    static func files(in folder: URL) throws -> [String] {
        let fm = FileManager.default
        guard let walk = fm.enumerator(atPath: folder.path) else { throw Failure.notAPlugin }
        var out: [String] = []
        var total = 0
        while let rel = walk.nextObject() as? String {
            let name = (rel as NSString).lastPathComponent
            if rel.hasPrefix(".claude-plugin/types") || name == "node_modules" || name == ".git" {
                walk.skipDescendants()
                continue
            }
            guard let attrs = walk.fileAttributes, let type = attrs[.type] as? FileAttributeType else { continue }
            if type == .typeSymbolicLink { throw Failure.hasLink(rel) }
            guard type == .typeRegular, name != ".DS_Store" else { continue }
            let size = (attrs[.size] as? NSNumber)?.intValue ?? 0
            total += size
            out.append(rel)
            if out.count > maxFiles || size > maxFileBytes || total > maxBytes { throw Failure.tooBig }
        }
        guard out.contains(".claude-plugin/plugin.json") else { throw Failure.notAPlugin }
        return out.sorted()
    }

    /// The manifest's name, version and description.
    static func manifest(_ folder: URL) throws -> (name: String, version: String?, description: String) {
        let m = ModStatusEntries.manifest(folder)
        guard let name = m["name"] as? String else { throw Failure.notAPlugin }
        guard isName(name) else { throw Failure.badName(name) }
        guard name != IntegrationScripts.modName else { throw Failure.reserved }
        return (name, m["version"] as? String, m["description"] as? String ?? "")
    }

    /// The stamp a session's mod would be kept under, nil when it can't be kept.
    static func stamp(of folder: URL) -> String? {
        guard let files = try? files(in: folder) else { return nil }
        return IntegrationScripts.stamp(of: folder, files: files)
    }

    /// The kept copy of the mod at `source` as it is now, if one exists.
    func kept(_ source: String) -> KeptMod? {
        let url = URL(fileURLWithPath: source)
        guard let stamp = Self.stamp(of: url), let name = try? Self.manifest(url).name else { return nil }
        return list().first { $0.name == name && $0.stamp == stamp }
    }

    // MARK: Keep and forget

    /// Copies the mod at `source` (made in `session`) into its stamped folder, read-only, and
    /// records it. Keeping the same files again returns the copy already kept.
    @discardableResult
    func keep(_ source: String, session: String, now: Date = Date()) throws -> KeptMod {
        let src = URL(fileURLWithPath: source)
        let files = try Self.files(in: src)
        let m = try Self.manifest(src)
        guard let stamp = IntegrationScripts.stamp(of: src, files: files) else { throw Failure.writeFailed }
        var mods = list()
        if let same = mods.first(where: { $0.name == m.name && $0.stamp == stamp }) { return same }
        let fm = FileManager.default
        let parent = dir.appendingPathComponent(m.name, isDirectory: true)
        let target = parent.appendingPathComponent(stamp, isDirectory: true)
        let staging = parent.appendingPathComponent(".staging-\(UUID().uuidString)", isDirectory: true)
        var bytes = 0
        do {
            try fm.createDirectory(at: staging, withIntermediateDirectories: true)
            for rel in files {
                let to = staging.appendingPathComponent(rel)
                try fm.createDirectory(at: to.deletingLastPathComponent(), withIntermediateDirectories: true)
                let data = try Data(contentsOf: src.appendingPathComponent(rel))
                bytes += data.count
                try data.write(to: to)
                try fm.setAttributes([.posixPermissions: 0o444], ofItemAtPath: to.path)
            }
            // The copy is what was read: a file changed while copying fails the keep.
            guard IntegrationScripts.stamp(of: staging, files: files) == stamp else { throw Failure.writeFailed }
            if fm.fileExists(atPath: target.path) { try Self.delete(target) }
            try fm.moveItem(at: staging, to: target)
        } catch {
            try? Self.delete(staging)
            throw (error as? Failure) ?? Failure.writeFailed
        }
        let kept = KeptMod(name: m.name, version: m.version, description: m.description, stamp: stamp, keptAt: now,
                           session: String(session.prefix(8)), files: files.count, bytes: bytes)
        mods.append(kept)
        try save(mods)
        return kept
    }

    /// Deletes a kept copy and its record.
    func forget(_ mod: KeptMod) throws {
        let target = URL(fileURLWithPath: path(mod))
        if FileManager.default.fileExists(atPath: target.path) { try Self.delete(target) }
        let parent = target.deletingLastPathComponent()
        if (try? FileManager.default.contentsOfDirectory(atPath: parent.path))?.isEmpty == true {
            try? FileManager.default.removeItem(at: parent)
        }
        try save(list().filter { $0.id != mod.id })
    }

    /// Removes a folder whose files are read-only.
    private static func delete(_ folder: URL) throws {
        let fm = FileManager.default
        if let walk = fm.enumerator(atPath: folder.path) {
            while let rel = walk.nextObject() as? String {
                guard (walk.fileAttributes?[.type] as? FileAttributeType) == .typeRegular else { continue }
                try? fm.setAttributes([.posixPermissions: 0o644], ofItemAtPath: folder.appendingPathComponent(rel).path)
            }
        }
        do { try fm.removeItem(at: folder) } catch { throw Failure.writeFailed }
    }

    // MARK: Try in a session

    /// The script Try in a Session opens in Terminal: an empty folder, and Claude Code with the
    /// kept copy loaded for that session only.
    static func tryScript(claude: String, mod: String, folder: String) -> String {
        let q = IntegrationInstaller.shellQuoted
        return """
        #!/bin/zsh
        cd \(q(folder)) || exit 1
        exec \(q(claude)) --plugin-dir \(q(mod))

        """
    }

    /// Writes the script into a new empty folder under the temporary folder and opens it in
    /// Terminal. Returns the folder.
    @discardableResult
    func trySession(_ mod: KeptMod, claude: String) throws -> URL {
        let fm = FileManager.default
        let folder = fm.temporaryDirectory.appendingPathComponent("sanduhr-try-\(mod.name)-\(UUID().uuidString.prefix(8))",
                                                                  isDirectory: true)
        let script = folder.appendingPathComponent("try.command")
        do {
            try fm.createDirectory(at: folder, withIntermediateDirectories: true)
            try Self.tryScript(claude: claude, mod: path(mod), folder: folder.path).write(to: script, atomically: true, encoding: .utf8)
            try fm.setAttributes([.posixPermissions: 0o700], ofItemAtPath: script.path)
        } catch {
            throw Failure.writeFailed
        }
        let config = NSWorkspace.OpenConfiguration()
        if let terminal = NSWorkspace.shared.urlForApplication(withBundleIdentifier: "com.apple.Terminal") {
            NSWorkspace.shared.open([script], withApplicationAt: terminal, configuration: config)
        } else {
            NSWorkspace.shared.open(script)
        }
        return folder
    }
}
