import Foundation
import Observation

/// Opening a `.sanduhrpack` (item 76, slice B). The zip's central directory is checked before a
/// byte is unpacked (paths, links, sizes, counts, encryption); `ditto` unpacks it into a new
/// temporary folder; every file is checked against the manifest's size and SHA-256, and any file
/// it doesn't list refuses the pack. The mod inside is then held to `propose_mod`'s rules through
/// `ModProposal.decode`, so it is reviewed exactly as a proposal from Claude is.
enum ModPackReader {
    static let maxEntries = 256
    static let maxFileBytes = 2 << 20
    static let maxBytes = 20 << 20
    /// What a pack is read up to before its directory is: anything larger isn't a pack Sanduhr opens.
    static let maxPackBytes = 24 << 20

    /// A zip entry as the central directory describes it.
    struct ZipEntry: Equatable {
        var name: String
        var size: Int
        var isDirectory: Bool
    }

    /// Why a pack can't be opened, in words for the sheet.
    struct Refusal: Error, Equatable {
        var reasons: [String]
        init(_ reasons: [String]) { self.reasons = reasons }
        init(_ reason: String) { reasons = [reason] }
    }

    /// Files a Mac adds to a zip that aren't part of it (AppleDouble `._` files, `__MACOSX/`,
    /// Finder's `.DS_Store`): skipped, never unpacked into the mod.
    static func isLitter(_ path: String) -> Bool {
        let name = (path as NSString).lastPathComponent
        return path.hasPrefix("__MACOSX/") || name == ".DS_Store" || name.hasPrefix("._")
    }

    // MARK: The central directory

    /// Reads the zip's central directory and checks every entry, or says why not.
    static func entries(_ data: Data) throws -> [ZipEntry] {
        let bytes = [UInt8](data)
        func u16(_ i: Int) -> Int { Int(bytes[i]) | Int(bytes[i + 1]) << 8 }
        func u32(_ i: Int) -> Int { u16(i) | u16(i + 2) << 16 }
        guard bytes.count >= 22 else { throw Refusal("It isn't a zip file.") }
        // The end-of-central-directory record: the last one in the file's final 64 KB.
        var eocd = -1
        var i = bytes.count - 22
        let floor = max(0, bytes.count - 22 - 0xFFFF)
        while i >= floor {
            if u32(i) == 0x0605_4B50 { eocd = i; break }
            i -= 1
        }
        guard eocd >= 0 else { throw Refusal("It isn't a zip file.") }
        let count = u16(eocd + 10)
        let size = u32(eocd + 12)
        let offset = u32(eocd + 16)
        guard count != 0xFFFF, size != 0xFFFF_FFFF, offset != 0xFFFF_FFFF else {
            throw Refusal("It's a ZIP64 file, which no pack is.")
        }
        guard u16(eocd + 4) == 0, u16(eocd + 6) == 0 else { throw Refusal("It's split across files, which no pack is.") }
        guard count <= maxEntries else { throw Refusal("It holds \(count) entries; a pack holds at most \(maxEntries).") }
        guard offset + size <= eocd else { throw Refusal("Its zip directory is damaged.") }

        var out: [ZipEntry] = []
        var total = 0
        var p = offset
        var seen = Set<String>()
        for _ in 0..<count {
            guard p + 46 <= offset + size, u32(p) == 0x0201_4B50 else { throw Refusal("Its zip directory is damaged.") }
            let madeBy = u16(p + 4) >> 8
            let flags = u16(p + 8)
            let method = u16(p + 10)
            let uncompressed = u32(p + 24)
            let nameLength = u16(p + 28)
            let extraLength = u16(p + 30)
            let commentLength = u16(p + 32)
            let external = u32(p + 38)
            guard p + 46 + nameLength <= offset + size,
                  let name = String(bytes: bytes[(p + 46)..<(p + 46 + nameLength)], encoding: .utf8) else {
                throw Refusal("An entry's name isn't readable.")
            }
            p += 46 + nameLength + extraLength + commentLength
            if isLitter(name) { continue }
            let isDirectory = name.hasSuffix("/")
            let path = isDirectory ? String(name.dropLast()) : name
            if let problem = pathProblem(path) { throw Refusal("\(name.prefix(80)): \(problem)") }
            guard seen.insert(path.lowercased()).inserted else { throw Refusal("\(path) is in it twice.") }
            guard flags & 1 == 0 else { throw Refusal("\(path) is encrypted.") }
            guard method == 0 || method == 8 else { throw Refusal("\(path) is compressed in a way packs aren't.") }
            if madeBy == 3 {   // Unix: the file type is in the high half of the external attributes.
                let type = (external >> 16) & 0o170000
                guard type == 0 || type == 0o100000 || type == 0o040000 else {
                    throw Refusal("\(path) is a link or a special file; a pack holds plain files only.")
                }
            }
            if !isDirectory {
                guard uncompressed <= maxFileBytes else { throw Refusal("\(path) is larger than \(maxFileBytes >> 20) MB.") }
                total += uncompressed
                guard total <= maxBytes else { throw Refusal("It unpacks to more than \(maxBytes >> 20) MB.") }
            }
            out.append(ZipEntry(name: path, size: uncompressed, isDirectory: isDirectory))
        }
        return out
    }

    /// Why a path inside a pack is refused, nil when it is fine.
    static func pathProblem(_ path: String) -> String? {
        guard !path.isEmpty, !path.hasPrefix("/"), !path.contains("\\"), !path.contains("\u{0}") else {
            return "paths in a pack are relative"
        }
        let parts = path.split(separator: "/", omittingEmptySubsequences: false)
        if parts.contains(where: { $0.isEmpty || $0 == "." || $0 == ".." }) { return "a path must not hold '.', '..' or '//'" }
        return nil
    }

    // MARK: Reading the pack

    /// What a pack holds once it's checked: the mod as `propose_mod` would send it.
    struct Opened: Equatable {
        var proposal: ModProposal
        /// The stamp Keep would give it, to tell a mod already kept.
        var stamp: String?
    }

    /// A pack read and verified: its manifest, and every file it lists with its bytes. Packs are
    /// small (20 MB at most), so the files are held in memory and nothing stays on disk.
    struct Unpacked: @unchecked Sendable {
        var manifest: [String: Any]
        var files: [String: Data]
        var kind: String { manifest["kind"] as? String ?? "" }
        /// Exported from a folder marked work (item 76: allowed, and said on import elsewhere).
        var fromWork: Bool { manifest["from"] as? String == "work" }
    }

    /// Checks and unpacks the pack at `url` into a temporary folder, verifies every file it lists
    /// against its size and SHA-256, refuses files it doesn't list, and returns them. Leaves
    /// nothing behind.
    static func read(_ url: URL) throws -> Unpacked {
        let fm = FileManager.default
        let attrs = try? fm.attributesOfItem(atPath: url.path)
        guard let attrs, attrs[.type] as? FileAttributeType == .typeRegular else { throw Refusal("Sanduhr can't read that file.") }
        guard (attrs[.size] as? NSNumber)?.intValue ?? 0 <= maxPackBytes else {
            throw Refusal("It's larger than \(maxPackBytes >> 20) MB, which no pack is.")
        }
        guard let data = fm.contents(atPath: url.path) else { throw Refusal("Sanduhr can't read that file.") }
        let listed = try entries(data)

        let work = fm.temporaryDirectory.appendingPathComponent("sanduhr-open-\(UUID().uuidString)", isDirectory: true)
        defer { try? fm.removeItem(at: work) }
        let root = work.appendingPathComponent("pack", isDirectory: true)
        // ditto reads the copy checked above, not the file again, so it can't change in between.
        let copy = work.appendingPathComponent("pack.zip")
        do {
            try fm.createDirectory(at: work, withIntermediateDirectories: true)
            try data.write(to: copy)
        } catch {
            throw Refusal("Sanduhr couldn't unpack it.")
        }
        guard ProcessRunner.run("/usr/bin/ditto", ["-x", "-k", "--norsrc", "--noextattr", "--noacl", copy.path, root.path],
                                timeout: ModPack.dittoTimeout).ok else {
            throw Refusal("Sanduhr couldn't unpack it.")
        }
        // What's on disk is what the directory listed: plain files, no more, no larger.
        let found = try unpacked(root)
        let expected = Set(listed.filter { !$0.isDirectory }.map(\.name))
        guard Set(found.keys) == expected else { throw Refusal("It unpacked to other files than it lists.") }

        guard let manifestData = fm.contents(atPath: root.appendingPathComponent(ModPack.manifestName).path),
              let manifest = (try? JSONSerialization.jsonObject(with: manifestData)) as? [String: Any] else {
            throw Refusal("It has no \(ModPack.manifestName), so it isn't a Sanduhr pack.")
        }
        guard manifest["schema"] as? Int == 1 else { throw Refusal("It's a pack of a newer kind. Update Sanduhr to open it.") }
        guard let listedFiles = manifest["files"] as? [[String: Any]] else {
            throw Refusal("Its \(ModPack.manifestName) is damaged.")
        }

        var reasons: [String] = []
        var files: [String: Data] = [:]
        for f in listedFiles {
            guard let path = f["path"] as? String, let bytes = f["bytes"] as? Int, let sha = f["sha256"] as? String else {
                reasons.append("A file in its manifest is missing its path, size or hash.")
                continue
            }
            guard pathProblem(path) == nil, path != ModPack.manifestName,
                  let content = fm.contents(atPath: root.appendingPathComponent(path).path) else {
                reasons.append("\(path) is listed but missing.")
                continue
            }
            let hash = Digest.sha256Hex(content)
            guard content.count == bytes, hash == sha else {
                reasons.append("\(path) doesn't match its manifest: the pack was changed or damaged.")
                continue
            }
            files[path] = content
        }
        let extra = expected.subtracting(files.keys).subtracting([ModPack.manifestName, ModPack.marketplacePath])
        if reasons.isEmpty, !extra.isEmpty {
            reasons.append("It holds files its manifest doesn't list: \(extra.sorted().prefix(5).joined(separator: ", ")).")
        }
        guard reasons.isEmpty else { throw Refusal(Array(reasons.prefix(20))) }
        return Unpacked(manifest: manifest, files: files)
    }

    /// Keep's stamp for files held in memory: the first 12 hex digits of a SHA-256 over each
    /// name, a zero byte and its bytes, in name order, as `IntegrationScripts.stamp` hashes a folder.
    static func stamp(_ files: [String: Data]) -> String {
        var parts: [Data] = []
        for name in files.keys.sorted() {
            parts += [Data(name.utf8), Data([0]), files[name] ?? Data()]
        }
        return Digest.sha256Hex(parts, prefix: 12)
    }

    /// The mod under `prefix` (`mod/` in a mod pack, `mods/<name>/` in a config pack) as
    /// `propose_mod` would send it, held to its rules by the same decoder.
    static func mod(in files: [String: Data], prefix: String, doc: [String: Any], now: Date) throws -> Opened {
        var modFiles: [[String: String]] = []
        var raw: [String: Data] = [:]
        for (path, content) in files where path.hasPrefix(prefix) {
            guard let text = String(data: content, encoding: .utf8) else { throw Refusal("\(path) isn't text.") }
            let rel = String(path.dropFirst(prefix.count))
            modFiles.append(["path": rel, "text": text])
            raw[rel] = content
        }
        modFiles.sort { $0["path"]! < $1["path"]! }
        let name = doc["name"] as? String ?? ""
        var summary = (doc["description"] as? String ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        summary = summary.components(separatedBy: .newlines).first ?? ""
        if summary.isEmpty { summary = "Opened from a pack." }
        if summary.count > ModProposal.maxSummary { summary = String(summary.prefix(ModProposal.maxSummary - 1)) + "…" }
        var request: [String: Any] = [
            "schema_version": 1, "id": UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased(),
            "requested_at": HandoffFiles.stamp(now), "name": name, "summary": summary, "files": modFiles,
        ]
        if let v = doc["version"] as? String { request["version"] = v }
        guard let requestData = try? JSONSerialization.data(withJSONObject: request) else { throw Refusal("Sanduhr couldn't read the mod.") }
        switch ModProposal.decode(requestData, now: now) {
        case .proposal(let p): return Opened(proposal: p, stamp: stamp(raw))
        case .refused(_, let why): throw Refusal(why)
        case .discard: throw Refusal("Sanduhr couldn't read the mod.")
        }
    }

    /// Opens a mod pack: read and verified, then its mod under `mod/`.
    static func open(_ url: URL, now: Date = Date()) throws -> Opened {
        let pack = try read(url)
        guard pack.kind == "mod" else {
            throw Refusal(pack.kind == "config" ? "It's a config pack: open it from By Seat's Open Config Pack…, or double-click it."
                                                : "It isn't a kind of pack this Sanduhr opens.")
        }
        guard let doc = pack.manifest["mod"] as? [String: Any] else { throw Refusal("Its \(ModPack.manifestName) is damaged.") }
        let outside = pack.files.keys.filter { !$0.hasPrefix(ModPack.modFolder + "/") }
        guard outside.isEmpty else { throw Refusal("\(outside.sorted()[0]) isn't under \(ModPack.modFolder)/.") }
        return try mod(in: pack.files, prefix: ModPack.modFolder + "/", doc: doc, now: now)
    }

    /// Every regular file under `root`, relative, with its size. A link or special file refuses.
    private static func unpacked(_ root: URL) throws -> [String: Int] {
        guard let walk = FileManager.default.enumerator(atPath: root.path) else { return [:] }
        var out: [String: Int] = [:]
        while let rel = walk.nextObject() as? String {
            guard let attrs = walk.fileAttributes, let type = attrs[.type] as? FileAttributeType else { continue }
            if isLitter(rel) { continue }
            switch type {
            case .typeDirectory: continue
            case .typeRegular: out[rel] = (attrs[.size] as? NSNumber)?.intValue ?? 0
            default: throw Refusal("\(rel) is a link or a special file; a pack holds plain files only.")
            }
        }
        return out
    }
}

/// Packs opened from Finder (a double-click, or a drop on Sanduhr's icon) wait here until Mods &
/// Config opens New Mod on them.
@MainActor @Observable
final class PackInbox {
    static let shared = PackInbox()
    var urls: [URL] = []

    /// Takes every waiting pack.
    func take() -> [URL] {
        defer { urls = [] }
        return urls
    }
}
