import CoreText
import Foundation

/// Installed font packs, registered for this process only (spec 11), a port of Windows' PackFonts.cs.
/// Each enabled font pack's `.ttf` and `.otf` faces go through `CTFontManagerRegisterFontsForURL` with
/// `.process` scope, the way `BundledFonts` registers EsteFont: never the system, gone when the app
/// quits, and unregistered when the pack is uninstalled, disabled or replaced.
///
/// Windows copies the files first because WPF keeps a font file open and Uninstall renames the
/// folder. CoreText holds no file open, so the faces are registered from the pack's own folder, and
/// `release` runs before an install or an uninstall touches that folder.
final class PackFonts: @unchecked Sendable {
    static let shared = PackFonts()

    /// What registering one face came to.
    enum Outcome: Equatable {
        case registered
        /// The same font is already available to this process (installed in Font Book): harmless, it draws.
        case alreadyAvailable
        case refused
    }

    /// One pack's faces as registered.
    struct Loaded: Equatable {
        let packId: String
        /// Version and install time: a pack that changed under the same id is registered again.
        let stamp: String
        let urls: [URL]
        let families: [String]
        /// Faces CoreText refused, left out of the lists.
        let refused: Int
    }

    private let lock = NSLock()
    private var loaded: [String: Loaded] = [:]
    private let register: (URL) -> Outcome
    private let unregister: (URL) -> Void
    private let familyNames: (URL) -> [String]

    /// The seams are for tests; the app uses CoreText.
    init(register: @escaping (URL) -> Outcome = PackFonts.coreTextRegister,
         unregister: @escaping (URL) -> Void = PackFonts.coreTextUnregister,
         familyNames: @escaping (URL) -> [String] = PackFonts.coreTextFamilies) {
        self.register = register
        self.unregister = unregister
        self.familyNames = familyNames
    }

    // MARK: Registering

    /// Brings the registered faces in line with the enabled font packs: a removed, disabled or changed
    /// pack is unregistered, a new or changed one registered. Cheap when nothing changed. Returns true
    /// when the set of registered faces changed.
    @discardableResult
    func sync(_ store: WidgetStore = .shared) -> Bool {
        let packs = store.installed(ofKind: "font")
        lock.lock()
        var changed = false
        let wanted = Dictionary(packs.map { ($0.row.id, Self.stamp($0)) }, uniquingKeysWith: { a, _ in a })
        for (id, entry) in loaded where wanted[id] != entry.stamp {
            entry.urls.forEach(unregister)
            loaded[id] = nil
            changed = true
        }
        for pack in packs where loaded[pack.row.id] == nil {
            loaded[pack.row.id] = load(pack)
            changed = true
        }
        lock.unlock()
        if changed { DeskFont.forgetAvailability() }
        return changed
    }

    /// Unregisters one pack's faces now. Called before an install replaces or an uninstall removes the
    /// pack's folder; the next `sync` registers whatever is still installed.
    func release(_ packId: String) {
        lock.lock()
        let entry = loaded.removeValue(forKey: packId)
        entry?.urls.forEach(unregister)
        lock.unlock()
        if entry != nil { DeskFont.forgetAvailability() }
    }

    private func load(_ pack: InstalledPack) -> Loaded {
        var urls: [URL] = []
        var families: [String] = []
        var refused = 0
        for url in Self.faceFiles(in: pack.dir) {
            let names = familyNames(url)
            guard !names.isEmpty, register(url) != .refused else { refused += 1; continue }
            urls.append(url)
            for name in names where !families.contains(where: { $0.caseInsensitiveCompare(name) == .orderedSame }) {
                families.append(name)
            }
        }
        return Loaded(packId: pack.row.id, stamp: Self.stamp(pack), urls: urls, families: families, refused: refused)
    }

    private static func stamp(_ pack: InstalledPack) -> String {
        "\(pack.row.version)@\(pack.row.installedAt.timeIntervalSince1970)@\(pack.dir.path)"
    }

    /// The pack's `.ttf` and `.otf` files, in path order.
    static func faceFiles(in dir: URL) -> [URL] {
        let names = (try? FileManager.default.subpathsOfDirectory(atPath: dir.path)) ?? []
        return names
            .filter { let e = ($0 as NSString).pathExtension.lowercased(); return e == "ttf" || e == "otf" }
            .sorted { Array($0.utf8).lexicographicallyPrecedes(Array($1.utf8)) }
            .map { dir.appendingPathComponent($0) }
    }

    // MARK: Reading

    /// Every registered family name, deduplicated without case and sorted: what joins the pickers.
    var families: [String] {
        lock.lock(); defer { lock.unlock() }
        var seen: [String] = []
        for entry in loaded.values {
            for name in entry.families where !seen.contains(where: { $0.caseInsensitiveCompare(name) == .orderedSame }) {
                seen.append(name)
            }
        }
        return seen.sorted { $0.localizedCaseInsensitiveCompare($1) == .orderedAscending }
    }

    /// The families of one pack.
    func families(of packId: String) -> [String] {
        lock.lock(); defer { lock.unlock() }
        return loaded[packId]?.families ?? []
    }

    /// How many of one pack's faces CoreText refused.
    func refusedCount(of packId: String) -> Int {
        lock.lock(); defer { lock.unlock() }
        return loaded[packId]?.refused ?? 0
    }

    /// The sentence for a pack's row when CoreText refused a face, nil when none was.
    func note(for packId: String) -> String? {
        Self.refusedNote(refusedCount(of: packId))
    }

    static func refusedNote(_ count: Int) -> String? {
        switch count {
        case ..<1: return nil
        case 1: return "One font couldn't be loaded and is left out."
        default: return "\(count) fonts couldn't be loaded and are left out."
        }
    }

    // MARK: CoreText

    static func coreTextRegister(_ url: URL) -> Outcome {
        var error: Unmanaged<CFError>?
        if CTFontManagerRegisterFontsForURL(url as CFURL, .process, &error) { return .registered }
        let code = error.map { CFErrorGetCode($0.takeRetainedValue()) } ?? 0
        return code == CTFontManagerError.alreadyRegistered.rawValue ? .alreadyAvailable : .refused
    }

    static func coreTextUnregister(_ url: URL) {
        var error: Unmanaged<CFError>?
        if !CTFontManagerUnregisterFontsForURL(url as CFURL, .process, &error) { error?.release() }
    }

    /// The family names the file names itself with; empty when CoreText can't read it.
    static func coreTextFamilies(_ url: URL) -> [String] {
        guard let descriptors = CTFontManagerCreateFontDescriptorsFromURL(url as CFURL) as? [CTFontDescriptor] else { return [] }
        return descriptors.compactMap { CTFontDescriptorCopyAttribute($0, kCTFontFamilyNameAttribute) as? String }
            .filter { !$0.isEmpty }
    }
}
