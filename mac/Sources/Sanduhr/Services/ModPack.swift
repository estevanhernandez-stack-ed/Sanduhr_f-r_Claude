import CryptoKit
import Foundation

/// A `.sanduhrpack` (item 76, `docs/config-transfer-spec.md`): one zip that carries a kept mod to
/// another Mac or person. Slice A writes mod packs. A pack is also a one-plugin marketplace, so
/// `claude plugin marketplace add <unzipped folder>` installs it without Sanduhr.
enum ModPack {
    static let fileExtension = "sanduhrpack"
    static let manifestName = "sanduhr-pack.json"
    static let marketplacePath = ".claude-plugin/marketplace.json"
    /// The plugin folder inside the pack: the marketplace's `source`.
    static let modFolder = "mod"

    enum Failure: Error, Equatable {
        /// The kept copy's files couldn't be read (gone, or no longer a plugin Sanduhr keeps).
        case unreadable
        case writeFailed

        var message: String {
            switch self {
            case .unreadable: "Sanduhr couldn't read the kept copy's files. Check it again, or keep it again."
            case .writeFailed: "Sanduhr couldn't write the pack."
            }
        }
    }

    /// One file as the manifest lists it.
    struct Entry: Equatable {
        var path: String
        var bytes: Int
        var sha256: String
    }

    /// The default file name: `<name>-<version>.sanduhrpack`.
    static func fileName(_ mod: KeptMod) -> String {
        let version = mod.version.map { "-" + $0.replacingOccurrences(of: "/", with: "-") } ?? ""
        return mod.name + version + "." + fileExtension
    }

    /// The marketplace name inside the pack. Claude Code names an install `<plugin>@<marketplace>`.
    static func marketplaceName(_ mod: KeptMod) -> String { mod.name + "-pack" }

    /// The pack's files under `mod/`, with their sizes and hashes: what `KeptMods.files` keeps.
    static func entries(in folder: URL) throws -> [Entry] {
        guard let files = try? KeptMods.files(in: folder) else { throw Failure.unreadable }
        return try files.map { rel in
            guard let data = FileManager.default.contents(atPath: folder.appendingPathComponent(rel).path) else {
                throw Failure.unreadable
            }
            let hash = SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined()
            return Entry(path: modFolder + "/" + rel, bytes: data.count, sha256: hash)
        }
    }

    /// `sanduhr-pack.json`. Names no machine, user, seat or folder: only the mod and its check.
    static func manifest(_ mod: KeptMod, entries: [Entry], check: ModCheckReport?, version: String,
                         now: Date) -> [String: Any] {
        var modDoc: [String: Any] = ["name": mod.name, "description": mod.description, "stamp": mod.stamp]
        if let v = mod.version { modDoc["version"] = v }
        if let check {
            modDoc["check"] = ["passed": check.passed, "risk": check.risk.title, "reasons": check.reasons,
                               "hooks": check.hooks, "calls": check.calls]
        }
        return [
            "schema": 1,
            "kind": "mod",
            "created_at": HandoffFiles.stamp(now),
            "created_by": "Sanduhr " + version,
            "label": mod.name,
            "mod": modDoc,
            "files": entries.map { ["path": $0.path, "bytes": $0.bytes, "sha256": $0.sha256] },
        ]
    }

    /// The one-plugin marketplace Claude Code reads: name, owner and a plugin whose source is `./mod`.
    static func marketplace(_ mod: KeptMod) -> [String: Any] {
        var plugin: [String: Any] = ["name": mod.name, "source": "./" + modFolder, "description": mod.description]
        if let v = mod.version { plugin["version"] = v }
        return [
            "name": marketplaceName(mod),
            "description": "\(mod.name), shared from Sanduhr.",
            "owner": ["name": "Shared with Sanduhr"],
            "plugins": [plugin],
        ]
    }

    /// Writes the pack for the kept copy at `folder` to `destination`, replacing a file there.
    /// Builds the tree in a temporary folder, zips it with `ditto`, then moves the zip into place,
    /// so a failure leaves nothing half-written at the destination.
    static func write(_ mod: KeptMod, from folder: URL, to destination: URL, check: ModCheckReport?,
                      version: String = Self.appVersion, now: Date = Date()) throws {
        let fm = FileManager.default
        let entries = try entries(in: folder)
        let work = fm.temporaryDirectory.appendingPathComponent("sanduhr-pack-\(UUID().uuidString)", isDirectory: true)
        defer { try? fm.removeItem(at: work) }
        let tree = work.appendingPathComponent("pack", isDirectory: true)
        do {
            try fm.createDirectory(at: tree.appendingPathComponent(".claude-plugin"), withIntermediateDirectories: true)
            for entry in entries {
                let rel = String(entry.path.dropFirst(modFolder.count + 1))
                let target = tree.appendingPathComponent(entry.path)
                try fm.createDirectory(at: target.deletingLastPathComponent(), withIntermediateDirectories: true)
                try fm.copyItem(at: folder.appendingPathComponent(rel), to: target)
                // Kept copies are read-only; the pack's files shouldn't be on the other side.
                try fm.setAttributes([.posixPermissions: 0o644], ofItemAtPath: target.path)
            }
            try json(manifest(mod, entries: entries, check: check, version: version, now: now))
                .write(to: tree.appendingPathComponent(manifestName))
            try json(marketplace(mod)).write(to: tree.appendingPathComponent(marketplacePath))
        } catch {
            throw Failure.writeFailed
        }
        let zip = work.appendingPathComponent("pack.zip")
        guard run("/usr/bin/ditto", ["-c", "-k", "--norsrc", "--noextattr", "--noacl", tree.path, zip.path]) else {
            throw Failure.writeFailed
        }
        do {
            if fm.fileExists(atPath: destination.path) {
                _ = try fm.replaceItemAt(destination, withItemAt: zip)
            } else {
                try fm.moveItem(at: zip, to: destination)
            }
        } catch {
            throw Failure.writeFailed
        }
    }

    static var appVersion: String {
        Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "dev"
    }

    private static func json(_ object: [String: Any]) throws -> Data {
        var data = try JSONSerialization.data(withJSONObject: object, options: [.prettyPrinted, .sortedKeys,
                                                                               .withoutEscapingSlashes])
        data.append(0x0A)
        return data
    }

    private static func run(_ tool: String, _ args: [String]) -> Bool {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: tool)
        p.arguments = args
        p.standardOutput = FileHandle.nullDevice
        p.standardError = FileHandle.nullDevice
        do { try p.run() } catch { return false }
        p.waitUntilExit()
        return p.terminationStatus == 0
    }
}
