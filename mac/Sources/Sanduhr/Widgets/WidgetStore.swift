import Foundation

/// Where an installed pack came from (spec 2). A pack keeps its source: an id installed from one
/// source is never replaced by a pack from another.
enum WidgetSource: String, Equatable, Sendable {
    /// The signed `sanduhr-widgets` catalog.
    case catalog
    /// Made with Claude (a later release), installed on Apply. Listed as "made here".
    case claude
    /// The user's own art (a later release). Listed as "made here".
    case local
}

/// One row of `widgets.json`.
struct InstalledWidget: Equatable, Sendable {
    let id: String
    let version: String
    let source: WidgetSource
    let installedAt: Date
    let enabled: Bool

    /// Claude-made and the user's own packs are local-only, listed as "made here".
    var madeHere: Bool { source != .catalog }
}

/// What Settings shows for an installed pack, read from its own `pack.json`.
struct InstalledWidgetInfo: Equatable, Sendable {
    let name: String
    let kind: String
    let description: String
    let previewPath: String?
}

/// One installed, enabled pack of a kind (`WidgetStore.installed(ofKind:)`): its registry row, its
/// own `pack.json` info and its folder.
struct InstalledPack: Equatable, Sendable {
    let row: InstalledWidget
    let info: InstalledWidgetInfo
    let dir: URL
}

/// `widgets.json`: the installed packs and the newest catalog `generated_at` seen (the rollback
/// marker, FORMAT 7 rule 2). Windows' shape, so the same file reads on both.
struct WidgetRegistry: Equatable {
    static let schemaVersion = 1
    static let empty = WidgetRegistry(catalogSeen: nil, packs: [])

    var catalogSeen: String?
    var packs: [InstalledWidget]

    func find(_ id: String) -> InstalledWidget? { packs.first { $0.id == id } }

    /// Reads `widgets.json`. Lenient: a file that is not JSON reads as empty, and a row that is not
    /// well formed is skipped (its folder stays until uninstalled or Reset).
    static func parse(_ data: Data?) -> WidgetRegistry {
        guard let data, let root = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any] else { return .empty }
        var seen: String?
        if let s = root["catalog_generated_at"] as? String, WidgetCatalog.parseStamp(s) != nil { seen = s }
        var packs: [InstalledWidget] = []
        for case let row as [String: Any] in (root["packs"] as? [Any]) ?? [] {
            guard let id = row["id"] as? String, WidgetStore.isSafeId(id),
                  let version = row["version"] as? String,
                  let source = (row["source"] as? String).flatMap(WidgetSource.init(rawValue:)),
                  let at = WidgetCatalog.parseStamp(row["installed_at"] as? String),
                  !packs.contains(where: { $0.id == id })
            else { continue }
            let enabled = (row["enabled"] as? Bool) ?? true
            packs.append(InstalledWidget(id: id, version: version, source: source, installedAt: at, enabled: enabled))
        }
        return WidgetRegistry(catalogSeen: seen, packs: sorted(packs))
    }

    private static func sorted(_ packs: [InstalledWidget]) -> [InstalledWidget] {
        packs.sorted { Array($0.id.utf8).lexicographicallyPrecedes(Array($1.id.utf8)) }
    }

    func json() -> Data {
        func q(_ s: String) -> String {
            (try? JSONEncoder().encode(s)).flatMap { String(data: $0, encoding: .utf8) } ?? "\"\""
        }
        var out = "{\n  \"schema_version\": \(Self.schemaVersion),\n"
        out += "  \"catalog_generated_at\": \(catalogSeen.map(q) ?? "null"),\n"
        if packs.isEmpty {
            out += "  \"packs\": []\n}\n"
        } else {
            out += "  \"packs\": [\n"
            out += Self.sorted(packs).map { p in
                "    {\n      \"id\": \(q(p.id)),\n      \"version\": \(q(p.version)),\n"
                    + "      \"source\": \(q(p.source.rawValue)),\n      \"installed_at\": \(q(Self.stamp(p.installedAt))),\n"
                    + "      \"enabled\": \(p.enabled)\n    }"
            }.joined(separator: ",\n")
            out += "\n  ]\n}\n"
        }
        return Data(out.utf8)
    }

    /// `YYYY-MM-DDTHH:MM:SSZ`, UTC.
    static func stamp(_ t: Date) -> String {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime]
        return f.string(from: t)
    }
}

/// The result of an install or uninstall: done, or the reason in a sentence for Settings.
struct WidgetStoreResult: Equatable {
    let ok: Bool
    let error: String?

    static let done = WidgetStoreResult(ok: true, error: nil)
    static func failed(_ why: String) -> WidgetStoreResult { WidgetStoreResult(ok: false, error: why) }
}

/// The installed packs on disk (spec 2): `~/Library/Application Support/Sanduhr/widgets/<id>/`, one
/// folder per pack, and `widgets.json` beside them. Nothing outside the app reads them. A port of
/// Windows' WidgetStore.cs.
///
/// `install` is atomic (FORMAT 7 rule 6): the pack's files are written to a fresh folder under
/// `.staging/`, pixel sheets are rasterized to PNG beside them (`.raster/<sheet>.png`), and only
/// then is the folder renamed into place and the row written. A failure at any step leaves the
/// previous install (or nothing) exactly as it was. Install takes files that already passed
/// `PackValidator` (its manifest is the proof); it re-checks every path against the staging folder
/// anyway.
///
/// Writes honor `ResetGate` like every other writer: after Reset Sanduhr… nothing is put back.
final class WidgetStore: WidgetCatalogMarker, @unchecked Sendable {
    static let registryName = "widgets.json"
    static let stagingName = ".staging"
    /// The folder inside a pack's install folder holding the PNGs rasterized from its pixel
    /// sheets. A pack path can never start with a dot (FORMAT 3.5), so this can't collide with a
    /// pack file.
    static let rasterName = ".raster"

    /// `~/Library/Application Support/Sanduhr/widgets`. Not created until something is installed.
    static var standardRoot: URL {
        HistoryStore.Files.standard.dir.appendingPathComponent(rootName, isDirectory: true)
    }
    static let rootName = "widgets"

    /// The app's one store, over the real folder. Tests make their own over a temp folder.
    static let shared = WidgetStore(root: standardRoot)

    private let encodePng: (PixelSheetImage) throws -> Data
    private let clock: () -> Date
    private let lock = NSRecursiveLock()

    /// Test seams: run after the pack is staged and before it is renamed into place, and after the
    /// rename and before the row is written. A throw there must leave the old install untouched.
    var beforeSwap: (() throws -> Void)?
    var beforeRegistryWrite: (() throws -> Void)?

    let root: URL

    /// - Parameters:
    ///   - root: The widgets folder.
    ///   - encodePng: Encodes a rasterized pixel sheet as PNG (the default is ImageIO's).
    init(root: URL, encodePng: ((PixelSheetImage) throws -> Data)? = nil, clock: @escaping () -> Date = Date.init) {
        self.root = root.standardizedFileURL
        self.encodePng = encodePng ?? { image in
            guard let data = image.pngData() else { throw StoreFailure("the sheet couldn't be encoded") }
            return data
        }
        self.clock = clock
    }

    var registryFile: URL { root.appendingPathComponent(Self.registryName) }
    var stagingDir: URL { root.appendingPathComponent(Self.stagingName, isDirectory: true) }

    /// A pack id that is safe as a folder name: FORMAT's id rule (lowercase kebab-case, 3 to 40
    /// characters).
    static func isSafeId(_ id: String?) -> Bool {
        guard let id else { return false }
        let b = Array(id.utf8)
        guard b.count >= 3, b.count <= 40, b[0] >= 0x61, b[0] <= 0x7A else { return false }
        for c in b where !((c >= 0x61 && c <= 0x7A) || (c >= 0x30 && c <= 0x39) || c == 0x2D) { return false }
        return b.last != 0x2D && !id.contains("--")
    }

    func packDir(_ id: String) -> URL {
        precondition(Self.isSafeId(id), "not a pack id")
        return root.appendingPathComponent(id, isDirectory: true)
    }

    /// Where the PNG for a pixel sheet `sheet` (a pack path ending `.json`) is written.
    func rasterPath(_ id: String, _ sheet: String) -> URL {
        packDir(id).appendingPathComponent(Self.rasterName, isDirectory: true).appendingPathComponent(sheet + ".png")
    }

    func load() -> WidgetRegistry {
        lock.lock(); defer { lock.unlock() }
        return WidgetRegistry.parse(try? Data(contentsOf: registryFile))
    }

    // MARK: The rollback marker (WidgetCatalogMarker)

    /// The newest catalog `generated_at` seen.
    func newestCatalogSeen() -> Date? { WidgetCatalog.parseStamp(load().catalogSeen) }

    /// Records `generatedAtText` (a catalog's own string) when it is newer than the marker.
    func rememberCatalog(_ generatedAtText: String) {
        guard !ResetGate.isOn, let t = WidgetCatalog.parseStamp(generatedAtText) else { return }
        lock.lock(); defer { lock.unlock() }
        var reg = load()
        if let seen = WidgetCatalog.parseStamp(reg.catalogSeen), seen >= t { return }
        reg.catalogSeen = generatedAtText
        try? save(reg)
    }

    // MARK: Reading

    /// The installed packs of one kind (`theme`, `palette`, `font`, ...) that are enabled, ordered
    /// by id: what the theme list and the message palettes read. A pack whose folder is gone or
    /// whose `pack.json` can't be read is skipped. Claude-made `my-` packs are rows like any other,
    /// so they flow through unchanged.
    func installed(ofKind kind: String) -> [InstalledPack] {
        var found: [InstalledPack] = []
        for row in load().packs where row.enabled && Self.isSafeId(row.id) {
            let dir = packDir(row.id)
            if FileManager.default.fileExists(atPath: dir.path), let info = readInfo(row.id), info.kind == kind {
                found.append(InstalledPack(row: row, info: info, dir: dir))
            }
        }
        return found
    }

    /// The installed pack's name, kind, description and preview from its own pack.json, or nil.
    func readInfo(_ id: String) -> InstalledWidgetInfo? {
        guard Self.isSafeId(id) else { return nil }
        let dir = packDir(id)
        guard let bytes = try? Data(contentsOf: dir.appendingPathComponent("pack.json")),
              let scalars = PackJson.decodeUTF8([UInt8](bytes)),
              case .success(let value) = PackJson.parse(scalars), let o = value.object
        else { return nil }
        var preview: String?
        if let pv = o["preview"]?.string, PackValidator.safeRelPath(pv) {
            let path = dir.appendingPathComponent(pv).path
            if FileManager.default.fileExists(atPath: path) { preview = path }
        }
        return InstalledWidgetInfo(name: o["name"]?.string ?? id, kind: o["kind"]?.string ?? "",
                                   description: o["description"]?.string ?? "", previewPath: preview)
    }

    /// An installed pack's own files read back into memory (relative POSIX path to bytes; the
    /// rasterized `.raster` folder is Sanduhr's, not the pack's, and is left out). Nil when the
    /// pack is missing, a file can't be read, or it holds more than a pack may
    /// (`PackValidator.maxPackBytes`, `PackValidator.maxFiles`).
    func readFiles(_ id: String) -> [String: Data]? {
        guard Self.isSafeId(id) else { return nil }
        let dir = packDir(id)
        guard FileManager.default.fileExists(atPath: dir.path),
              let walker = FileManager.default.enumerator(at: dir, includingPropertiesForKeys: [.isRegularFileKey, .isSymbolicLinkKey, .fileSizeKey]) else { return nil }
        let base = dir.standardizedFileURL.path + "/"
        var files: [String: Data] = [:]
        var total = 0
        for case let url as URL in walker {
            guard let v = try? url.resourceValues(forKeys: [.isRegularFileKey, .isSymbolicLinkKey, .fileSizeKey]) else { return nil }
            if v.isSymbolicLink == true { return nil }
            guard v.isRegularFile == true else { continue }
            let rel = String(url.standardizedFileURL.path.dropFirst(base.count))
            if rel.hasPrefix(Self.rasterName + "/") { continue }
            total += v.fileSize ?? 0
            if total > PackValidator.maxPackBytes || files.count > PackValidator.maxFiles + 3 { return nil }
            guard let data = try? Data(contentsOf: url) else { return nil }
            files[rel] = data
        }
        return files
    }

    // MARK: Install and uninstall

    /// Installs a validated pack from `source`. Refused when the id is installed from a different
    /// source. Replaces an install from the same source (an update) atomically. `replaceMadeHere`
    /// lets a pack made here change hands between made-here sources (a Claude-made sprite the user
    /// gave their own art becomes `.local`); a catalog pack is never replaced that way.
    func install(_ files: [String: Data], _ manifest: PackManifest, source: WidgetSource,
                 replaceMadeHere: Bool = false) -> WidgetStoreResult {
        guard Self.isSafeId(manifest.id) else { return .failed("The pack has no valid id.") }
        guard !ResetGate.isOn else { return .failed("Sanduhr is resetting, so nothing was installed.") }
        let id = manifest.id
        lock.lock(); defer { lock.unlock() }
        let fm = FileManager.default
        var reg = load()
        if let existing = reg.find(id), existing.source != source,
           !(replaceMadeHere && existing.madeHere && source != .catalog) {
            return .failed(existing.madeHere
                ? "A widget made here is already installed as \(id). Uninstall it first."
                : "A catalog widget is already installed as \(id). Uninstall it first.")
        }

        let staging = stagingDir.appendingPathComponent(id + "-" + Self.token(), isDirectory: true)
        let target = packDir(id)
        var old: URL?
        var swapped = false
        do {
            try fm.createDirectory(at: staging, withIntermediateDirectories: true)
            for (rel, bytes) in files {
                let full = try Self.inside(staging, rel)
                try fm.createDirectory(at: full.deletingLastPathComponent(), withIntermediateDirectories: true)
                try bytes.write(to: full)
            }
            if manifest.kind == "sprite" {
                for sheet in Self.pixelSheets(of: files) {
                    guard let data = files[sheet] else { continue }
                    switch PixelSheet.rasterize(data) {
                    case .failure(let e): throw StoreFailure("pixel sheet \(sheet): \(e.message)")
                    case .success(let image):
                        let png = try Self.inside(staging, Self.rasterName + "/" + sheet + ".png")
                        try fm.createDirectory(at: png.deletingLastPathComponent(), withIntermediateDirectories: true)
                        try encodePng(image).write(to: png)
                    }
                }
            }

            try beforeSwap?()
            if fm.fileExists(atPath: target.path) {
                let aside = stagingDir.appendingPathComponent(id + "-old-" + Self.token(), isDirectory: true)
                try fm.moveItem(at: target, to: aside)
                old = aside
            }
            try fm.moveItem(at: staging, to: target)
            swapped = true

            try beforeRegistryWrite?()
            let row = InstalledWidget(id: id, version: manifest.version, source: source, installedAt: clock(),
                                      enabled: reg.find(id)?.enabled ?? true)
            reg.packs = reg.packs.filter { $0.id != id } + [row]
            try save(reg)
        } catch {
            // Put the previous install back exactly as it was.
            if swapped { Self.removeQuietly(target) }
            if let old, !fm.fileExists(atPath: target.path) {
                if (try? fm.moveItem(at: old, to: target)) != nil { /* back in place */ }
                // else: left in .staging, cleaned on the next open
            }
            Self.removeQuietly(staging)
            return .failed("The widget couldn't be installed: " + Self.plain(error))
        }
        if let old { Self.removeQuietly(old) }
        return .done
    }

    /// Removes the pack's folder and its row. The folder is first renamed into `.staging/`, so a
    /// delete that stops halfway never leaves half a pack where Sanduhr reads packs.
    func uninstall(_ id: String) -> WidgetStoreResult {
        guard Self.isSafeId(id) else { return .failed("That isn't an installed widget.") }
        guard !ResetGate.isOn else { return .failed("Sanduhr is resetting, so nothing was removed.") }
        lock.lock(); defer { lock.unlock() }
        let fm = FileManager.default
        var reg = load()
        let target = packDir(id)
        if reg.find(id) == nil, !fm.fileExists(atPath: target.path) { return .failed("That widget isn't installed.") }
        do {
            if fm.fileExists(atPath: target.path) {
                try fm.createDirectory(at: stagingDir, withIntermediateDirectories: true)
                let gone = stagingDir.appendingPathComponent(id + "-gone-" + Self.token(), isDirectory: true)
                try fm.moveItem(at: target, to: gone)
                Self.removeQuietly(gone)
            }
            reg.packs = reg.packs.filter { $0.id != id }
            try save(reg)
            return .done
        } catch {
            return .failed("The widget couldn't be removed: " + Self.plain(error))
        }
    }

    /// Deletes what an interrupted install or uninstall left in `.staging/`.
    func cleanStaging() {
        guard !ResetGate.isOn else { return }
        lock.lock(); defer { lock.unlock() }
        Self.removeQuietly(stagingDir)
    }

    /// The pixel sheets (`.json` sheets) a validated sprite pack's `sprite.json` names.
    static func pixelSheets(of files: [String: Data]) -> [String] {
        guard let bytes = files["sprite.json"], let scalars = PackJson.decodeUTF8([UInt8](bytes)),
              case .success(let value) = PackJson.parse(scalars), let o = value.object
        else { return [] }
        var sheets: [String] = []
        func add(_ v: PackValue?) {
            if let path = v?.string, path.hasSuffix(".json"), files[path] != nil, !sheets.contains(path) { sheets.append(path) }
        }
        add(o["sheet"])
        for stage in o["stages"]?.array ?? [] { add(stage.object?["sheet"]) }
        return sheets
    }

    // MARK: Internals

    private func save(_ reg: WidgetRegistry) throws {
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        let tmp = registryFile.appendingPathExtension("tmp")
        try reg.json().write(to: tmp)
        if FileManager.default.fileExists(atPath: registryFile.path) {
            _ = try FileManager.default.replaceItemAt(registryFile, withItemAt: tmp)
        } else {
            try FileManager.default.moveItem(at: tmp, to: registryFile)
        }
    }

    private static func token() -> String { String(UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased().prefix(12)) }

    /// The URL of `rel` under `base`; throws if it would land anywhere else.
    private static func inside(_ base: URL, _ rel: String) throws -> URL {
        if rel.isEmpty || WidgetZip.unsafeName(rel) != nil { throw StoreFailure("unsafe path") }
        let full = base.appendingPathComponent(rel).standardizedFileURL
        guard full.path.hasPrefix(base.standardizedFileURL.path + "/") else { throw StoreFailure("unsafe path") }
        return full
    }

    private static func removeQuietly(_ url: URL) {
        try? FileManager.default.removeItem(at: url)
    }

    struct StoreFailure: Error {
        let message: String
        init(_ message: String) { self.message = message }
    }

    /// A short reason without a path in it.
    private static func plain(_ e: Error) -> String {
        if let f = e as? StoreFailure { return f.message + "." }
        let ns = e as NSError
        if ns.domain == NSCocoaErrorDomain, ns.code == NSFileWriteNoPermissionError || ns.code == NSFileReadNoPermissionError {
            return "access to the widgets folder was denied."
        }
        if ns.domain == NSCocoaErrorDomain || ns.domain == NSPOSIXErrorDomain { return "the widgets folder couldn't be written." }
        return "something went wrong."
    }
}
