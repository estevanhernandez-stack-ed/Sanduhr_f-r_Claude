import Foundation

/// The Claude Code integration scripts (item 49): shipped inside the app at
/// `Contents/Resources/integrations/`, copied to `~/Library/Application Support/Sanduhr/integrations/`
/// for Claude Code to run.
///
/// **Stamped folders behind one stable link.** Each set of scripts lands in a folder named for
/// its stamp (the first 12 hex digits of a SHA-256 over the scripts' names and bytes), and
/// `integrations/current` is a symlink to the folder in use. Claude Code's settings name
/// `…/integrations/current/<script>`, so an app update never rewrites anyone's config: it adds a
/// new stamped folder and swaps the link. This is Windows' versioned-folder answer
/// (`McpIntegrationInstaller`): there a running session pins the exe, so a new version goes into
/// a new folder; here nothing is locked, but a session starting mid-update must never read half
/// a script, and a new folder plus one `rename(2)` of the link means it sees the old set or the
/// new one, whole. The stamp follows the scripts' content, so an update that leaves them alone
/// changes nothing on disk.
///
/// Stamped folders are pruned only at launch (`refreshIfInstalled`): a Claude Code session loads
/// the mod from the folder its settings named when it started, and keeps reading it, so a folder
/// swapped out or unpinned while the app runs (Update on the Mods page) stays until the next
/// launch. Then every stamped folder goes except the current one, the one that launch swapped
/// out, a version a receipt pins (item 64, slice 2) and any a known settings.json still names
/// (`…/integrations/<stamp>/`). Python reads a script whole when it starts, so a running server or
/// statusline never needs its folder again.
/// The flat copies `install.sh` makes beside them (`integrations/sanduhr_*.py`) follow the app too:
/// a caller that names them directly, such as a seat's own statusline script, would otherwise run
/// the copy from the day `install.sh` last ran, from before seats, and show the active account in
/// every folder. Each refresh turns a flat copy that is a Sanduhr script into a link to
/// `current/<script>`; `removeAll` turns those links back into files. A flat file that isn't a
/// Sanduhr script, or a link to anything else, is left alone.
///
/// The Claude Code mod (item 50) rides in the same stamped folder, under `mods/sanduhr-meters/`:
/// its files join the stamp, and Claude Code loads it from `…/integrations/current/mods/sanduhr-meters`.
/// Claude Code writes its type declarations into a mod folder it loads
/// (`.claude-plugin/types/`); the stamp covers only the files the app ships, so those never make
/// a folder look altered.
struct IntegrationScripts {
    static let mcpScript = "sanduhr_mcp.py"
    static let statuslineScript = "sanduhr_statusline.py"
    static let names = [mcpScript, statuslineScript]
    static let currentName = "current"
    static let modsName = "mods"
    static let modName = "sanduhr-meters"
    /// The mod's folder inside a stamped folder.
    static let modPath = "\(modsName)/\(modName)"

    /// The scripts as the app ships them; nil where they are missing (a `swift run` build).
    let source: URL?
    /// `…/Application Support/Sanduhr/integrations`.
    let dir: URL

    static var standard: IntegrationScripts {
        IntegrationScripts(
            source: Bundle.main.resourceURL?.appendingPathComponent("integrations", isDirectory: true),
            dir: HistoryStore.Files.standard.dir.appendingPathComponent("integrations", isDirectory: true))
    }

    enum Failure: Error, Equatable {
        /// The app has no scripts inside (a development build run without build.sh).
        case missingFromApp
        case writeFailed
    }

    /// The stable link Claude Code's settings name.
    var current: URL { dir.appendingPathComponent(Self.currentName) }

    /// The path written into Claude Code's settings for `name`.
    func installedPath(_ name: String) -> String { current.appendingPathComponent(name).path }

    /// The mod's folder through the stable link: the entry in Claude Code's plugin folders.
    var installedModPath: String { current.appendingPathComponent(Self.modPath).path }

    /// A stamped version's mod folder: what the Mods page pins an entry to (item 64, slice 2).
    func pinnedModPath(_ stamp: String) -> String {
        dir.appendingPathComponent(stamp).appendingPathComponent(Self.modPath).path
    }

    /// The stamp a plugin folders entry pins, nil for the `current` link or anything else.
    func pinnedStamp(of entry: String) -> String? {
        let e = entry.trimmingCharacters(in: .whitespaces)
        let suffix = "/" + Self.modPath
        guard e.hasSuffix(suffix) else { return nil }
        let stampDir = String(e.dropLast(suffix.count))
        let stamp = (stampDir as NSString).lastPathComponent
        guard Self.isStampName(stamp), (stampDir as NSString).deletingLastPathComponent == dir.path else { return nil }
        return stamp
    }

    /// The stamps receipts pin (`installs.json`'s `pinned`): refresh keeps them.
    var pinnedStamps: [String] {
        receipts.records.compactMap(\.pinned).filter(Self.isStampName)
    }

    /// `installs.json` read record by record (item 71, slice 0).
    var receipts: ReceiptLog.Decoded<IntegrationReceipt> {
        guard let data = try? Data(contentsOf: dir.appendingPathComponent(IntegrationInstaller.receiptsName)) else {
            return ReceiptLog.Decoded()
        }
        return ReceiptLog.decode(IntegrationReceipt.self, from: data)
    }

    /// Every receipt is readable: none was skipped, the file parses, and no unreadable receipts
    /// file was set aside beside it. While this is false, nothing that receipts might still name
    /// (a stamped folder, the scripts) is deleted.
    var receiptsAllReadable: Bool {
        guard receipts.skipped == 0 else { return false }
        let names = (try? FileManager.default.contentsOfDirectory(atPath: dir.path)) ?? []
        return !names.contains { $0.hasPrefix(IntegrationInstaller.receiptsName) && $0.hasSuffix(ReceiptLog.asideSuffix) }
    }

    /// The app carries the mod (its manifest is there).
    var hasMod: Bool {
        guard let source else { return false }
        return FileManager.default.fileExists(
            atPath: source.appendingPathComponent("\(Self.modPath)/.claude-plugin/plugin.json").path)
    }

    // MARK: Stamps

    /// The files a stamped folder holds, relative to it: the two scripts, then every file of the
    /// mods folder in path order, without what Claude Code writes there or the mod's tests.
    static func files(in folder: URL) -> [String] {
        var out = names
        let mods = folder.appendingPathComponent(modsName, isDirectory: true)
        guard let walk = FileManager.default.enumerator(atPath: mods.path) else { return out }
        var found: [String] = []
        while let rel = walk.nextObject() as? String {
            let path = "\(modsName)/\(rel)"
            var isDir: ObjCBool = false
            guard FileManager.default.fileExists(atPath: folder.appendingPathComponent(path).path, isDirectory: &isDir),
                  !isDir.boolValue, isShipped(path) else { continue }
            found.append(path)
        }
        out += found.sorted()
        return out
    }

    /// A mod file the app ships: not Claude Code's generated types, a test, the repo's ignore file or
    /// Finder's litter (build.sh leaves the same out).
    static func isShipped(_ path: String) -> Bool {
        let name = (path as NSString).lastPathComponent
        if path.contains("/.claude-plugin/types/") || name == ".DS_Store" || name == ".gitignore" { return false }
        return !(name.hasSuffix(".test.ts") || name.hasSuffix(".test.tsx"))
    }

    /// The stamp of a folder holding `files` (the app's list), or nil when one is missing.
    static func stamp(of folder: URL, files: [String]) -> String? {
        var parts: [Data] = []
        for name in files {
            guard let data = try? Data(contentsOf: folder.appendingPathComponent(name)) else { return nil }
            parts += [Data(name.utf8), Data([0]), data]
        }
        return Digest.sha256Hex(parts, prefix: 12)
    }

    /// The stamp of a folder over the files it holds itself.
    static func stamp(of folder: URL) -> String? { stamp(of: folder, files: files(in: folder)) }

    /// The stamp of the scripts inside the app.
    var bundledStamp: String? { source.flatMap(Self.stamp(of:)) }

    /// The stamp `current` points at, or nil without a link.
    var installedStamp: String? {
        guard let target = try? FileManager.default.destinationOfSymbolicLink(atPath: current.path) else { return nil }
        return (target as NSString).lastPathComponent
    }

    /// An install exists: the link is there (the launch refresh runs only then).
    var isInstalled: Bool { installedStamp != nil }

    /// The installed scripts match the app's.
    var isCurrent: Bool { bundledStamp != nil && installedStamp == bundledStamp }

    static func isStampName(_ name: String) -> Bool {
        name.count == 12 && name.allSatisfy { $0.isHexDigit && !$0.isUppercase }
    }

    // MARK: Refresh

    /// Puts the app's scripts in their stamped folder and points `current` at it. Returns the
    /// stamp. No stamped folder is deleted here (only leftovers of an interrupted refresh): a
    /// running session may still load from one; `refreshIfInstalled` prunes at launch.
    @discardableResult
    func refresh() throws -> String {
        try refresh(prune: nil)
    }

    /// `refresh()`, then, with `prune`, the launch prune over those settings files.
    private func refresh(prune settingsFiles: [String]?) throws -> String {
        guard let source else { throw Failure.missingFromApp }
        let files = Self.files(in: source)
        guard let stamp = Self.stamp(of: source, files: files) else { throw Failure.missingFromApp }
        let fm = FileManager.default
        do {
            try fm.createDirectory(at: dir, withIntermediateDirectories: true)
            let target = dir.appendingPathComponent(stamp, isDirectory: true)
            if Self.stamp(of: target, files: files) != stamp {
                // Missing or altered: build it beside, then move it into place whole.
                let staging = dir.appendingPathComponent(".staging-\(UUID().uuidString)", isDirectory: true)
                try fm.createDirectory(at: staging, withIntermediateDirectories: true)
                for name in files {
                    let dest = staging.appendingPathComponent(name)
                    try fm.createDirectory(at: dest.deletingLastPathComponent(), withIntermediateDirectories: true)
                    try fm.copyItem(at: source.appendingPathComponent(name), to: dest)
                    // The scripts run; the mod's files are read.
                    let mode = Self.names.contains(name) ? 0o755 : 0o644
                    try fm.setAttributes([.posixPermissions: mode], ofItemAtPath: dest.path)
                }
                try? fm.removeItem(at: target)
                try fm.moveItem(at: staging, to: target)
            }
            let replaced = installedStamp
            if replaced != stamp {
                let link = dir.appendingPathComponent(".current-\(UUID().uuidString)")
                try fm.createSymbolicLink(atPath: link.path, withDestinationPath: stamp)
                guard rename(link.path, current.path) == 0 else {
                    unlink(link.path)
                    throw Failure.writeFailed
                }
            }
            linkFlatCopies()
            // A receipt Sanduhr can't read may pin a stamp or name a settings file: no prune then.
            if let settingsFiles, receiptsAllReadable {
                prune(keep: [stamp, replaced].compactMap { $0 } + pinnedStamps + Self.referencedStamps(in: settingsFiles))
            } else {
                pruneLeftovers()
            }
            return stamp
        } catch let f as Failure {
            NSLog("Sanduhr integrations refresh failed (\(f))")
            throw f
        } catch {
            NSLog("Sanduhr integrations refresh failed (\(type(of: error)))")
            throw Failure.writeFailed
        }
    }

    /// The launch refresh: only where an install exists, and the one place stamped folders are
    /// pruned. `settingsFiles` are the settings a stamp may still be named in (the receipts' files
    /// by default). Failures are logged by type only.
    func refreshIfInstalled(settingsFiles: [String]? = nil) {
        guard isInstalled else { return }
        _ = try? refresh(prune: settingsFiles ?? receiptFiles)
    }

    /// The config files the receipts name.
    var receiptFiles: [String] {
        Array(Set(receipts.records.map(\.file))).sorted()
    }

    /// The stamps named as `…/integrations/<stamp>/` in any of `files`.
    static func referencedStamps(in files: [String]) -> [String] {
        var out = Set<String>()
        for file in files {
            guard let text = try? String(contentsOfFile: file, encoding: .utf8) else { continue }
            var rest = Substring(text)
            while let r = rest.range(of: "integrations/") {
                let stamp = String(rest[r.upperBound...].prefix(12))
                if isStampName(stamp) { out.insert(stamp) }
                rest = rest[r.upperBound...]
            }
        }
        return out.sorted()
    }

    /// Deletes what an interrupted refresh left (never a stamped folder).
    private func pruneLeftovers() {
        let fm = FileManager.default
        guard let names = try? fm.contentsOfDirectory(atPath: dir.path) else { return }
        for name in names where name.hasPrefix(".staging-") || name.hasPrefix(".current-") || name.hasPrefix(".flat-") {
            try? fm.removeItem(at: dir.appendingPathComponent(name))
        }
    }

    /// Deletes stamped folders other than `keep`, and leftovers of an interrupted refresh.
    func prune(keep: [String]) {
        let fm = FileManager.default
        guard let names = try? fm.contentsOfDirectory(atPath: dir.path) else { return }
        for name in names where !keep.contains(name) {
            if Self.isStampName(name) || name.hasPrefix(".staging-") || name.hasPrefix(".current-") || name.hasPrefix(".flat-") {
                try? fm.removeItem(at: dir.appendingPathComponent(name))
            }
        }
    }

    // MARK: Flat copies

    /// The link a flat copy becomes: relative, so it follows the `current` swap.
    static func flatTarget(_ name: String) -> String { "\(currentName)/\(name)" }

    /// A flat copy that is one of Sanduhr's scripts: a regular file naming Sanduhr near its top.
    static func isSanduhrScript(_ url: URL) -> Bool {
        guard let attrs = try? FileManager.default.attributesOfItem(atPath: url.path),
              attrs[.type] as? FileAttributeType == .typeRegular,
              let handle = try? FileHandle(forReadingFrom: url) else { return false }
        defer { try? handle.close() }
        let head = (try? handle.read(upToCount: 512)) ?? Data()
        return String(decoding: head, as: UTF8.self).lowercased().contains("sanduhr")
    }

    /// Turns each flat copy that is a Sanduhr script into a link to `current/<script>`, by a
    /// `rename(2)` over it so a caller sees the old file or the link, whole.
    func linkFlatCopies() {
        let fm = FileManager.default
        for name in Self.names {
            let flat = dir.appendingPathComponent(name)
            guard Self.isSanduhrScript(flat) else { continue }
            let link = dir.appendingPathComponent(".flat-\(UUID().uuidString)")
            guard (try? fm.createSymbolicLink(atPath: link.path, withDestinationPath: Self.flatTarget(name))) != nil else { continue }
            if rename(link.path, flat.path) != 0 { unlink(link.path) }
        }
    }

    /// Turns flat links back into files holding the current script, so a caller that names them
    /// keeps working once the stamped folders are gone.
    private func unlinkFlatCopies() {
        let fm = FileManager.default
        for name in Self.names {
            let flat = dir.appendingPathComponent(name)
            guard (try? fm.destinationOfSymbolicLink(atPath: flat.path)) == Self.flatTarget(name),
                  let data = fm.contents(atPath: flat.path) else { continue }
            let temp = dir.appendingPathComponent(".flat-\(UUID().uuidString)")
            guard fm.createFile(atPath: temp.path, contents: data, attributes: [.posixPermissions: 0o755]) else { continue }
            if rename(temp.path, flat.path) != 0 { unlink(temp.path) }
        }
    }

    /// Removes the link and every stamped folder (nothing is installed in any folder any more).
    /// Flat copies stay, as files.
    func removeAll() {
        unlinkFlatCopies()
        unlink(current.path)
        prune(keep: [])
        let fm = FileManager.default
        if let left = try? fm.contentsOfDirectory(atPath: dir.path), left.isEmpty {
            try? fm.removeItem(at: dir)
        }
    }
}
