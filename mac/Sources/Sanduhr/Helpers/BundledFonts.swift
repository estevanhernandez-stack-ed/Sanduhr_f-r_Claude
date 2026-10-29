import AppKit
import CoreText
import Foundation
import Observation

/// One handwriting family that ships inside Sanduhr: its family name and the PostScript names of
/// its Regular and Bold faces (each file is `<face>.ttf`).
struct BundledFamily: Equatable, Sendable {
    let name: String
    let regularFace: String
    let boldFace: String
    var fileNames: [String] { ["\(regularFace).ttf", "\(boldFace).ttf"] }
}

/// The author's handwriting, shipped inside the app at Contents/Resources/Fonts/ and registered
/// for Sanduhr's process only, so the Desk draws in it on a Mac that has never had it installed.
/// Nothing is installed system-wide. EsteFont Pro (decision 2026-10-06, item 65 (e)) is the
/// default; EsteFont 26 (item 58) stays beside it as the heritage choice.
enum BundledFonts {
    static let esteFontPro = BundledFamily(name: "EsteFont Pro",
                                           regularFace: "EsteFontPro-Regular", boldFace: "EsteFontPro-Bold")
    static let esteFont26 = BundledFamily(name: "EsteFont 26",
                                          regularFace: "EsteFont26-Regular", boldFace: "EsteFont26-Bold")
    /// Every bundled family, in picker order: the default first, the heritage choice after it.
    static let families = [esteFontPro, esteFont26]
    /// The default family (new installs, fallback) and its faces.
    static let family = esteFontPro.name
    static let regularFace = esteFontPro.regularFace
    static let boldFace = esteFontPro.boldFace
    /// The folder under the app's Resources the files sit in.
    static let folder = "Fonts"
    static let fileNames = families.flatMap(\.fileNames)

    /// The bundled font files under a Resources folder, in `fileNames` order, leaving out any
    /// that are missing. Pure apart from the existence check, which tests hand in.
    static func urls(resources: URL?,
                     exists: (URL) -> Bool = { FileManager.default.fileExists(atPath: $0.path) }) -> [URL] {
        guard let resources else { return [] }
        let dir = resources.appendingPathComponent(folder, isDirectory: true)
        return fileNames.map { dir.appendingPathComponent($0) }.filter(exists)
    }

    /// Registers the bundled faces for this process, at launch before any view draws. A face that
    /// is already available (the same font installed in Font Book) refuses a second registration:
    /// harmless, the installed copy draws. Failures are logged by file name, never by path.
    static func register(bundle: Bundle = .main) {
        let found = urls(resources: bundle.resourceURL)
        if found.count < fileNames.count {
            NSLog("[Sanduhr] Bundled fonts: \(fileNames.count - found.count) of \(fileNames.count) missing from the app")
        }
        for url in found {
            var error: Unmanaged<CFError>?
            if !CTFontManagerRegisterFontsForURL(url as CFURL, .process, &error) {
                let code = error.map { CFErrorGetCode($0.takeRetainedValue()) } ?? 0
                if code != CTFontManagerError.alreadyRegistered.rawValue {
                    NSLog("[Sanduhr] Could not register \(url.lastPathComponent) (CTFontManager error \(code))")
                }
            }
        }
        DeskFont.forgetAvailability()
    }

    /// The bundled family by name, nil for any other family.
    static func bundled(_ family: String) -> BundledFamily? {
        families.first { $0.name == family }
    }

    /// The face to draw a family in: a bundled family by PostScript name, Bold where the design
    /// wants weight; any other family by its family name, as before.
    static func face(_ family: String, bold: Bool) -> String {
        guard let bundled = bundled(family) else { return family }
        return bold ? bundled.boldFace : bundled.regularFace
    }
}

/// The Desk's font (the desk suite's `font`). New installs draw in EsteFont Pro; an upgrade keeps
/// the font that was on screen (`keepExistingDefault`); "" is the system font; a saved family
/// that is no longer installed falls back to EsteFont Pro.
enum DeskFont {
    static let key = DeskLook.fontKey
    /// Desk suite: set once the system-font upgrade below has run, whatever it did (2.6.0).
    static let settledKey = "fontDefaultSettled"
    /// Desk suite: set once the EsteFont Pro upgrade below has run, whatever it did.
    static let proSettledKey = "fontProDefaultSettled"
    static let defaultFamily = BundledFonts.family
    /// The default before EsteFont Pro (2.6.0 to 2.8.0), kept for the Desks that drew in it.
    static let heritageFamily = BundledFonts.esteFont26.name

    /// The family the Desk draws in for a saved value. nil (never chosen) is EsteFont Pro; ""
    /// (System) stays the system font; a bundled family always stays; a family that is not
    /// available is EsteFont Pro.
    static func resolve(saved: String?, available: (String) -> Bool = DeskFont.isAvailable) -> String {
        guard let saved else { return defaultFamily }
        if saved.isEmpty || BundledFonts.bundled(saved) != nil || available(saved) { return saved }
        return defaultFamily
    }

    /// The message's family: its own pick while that font is available, otherwise the Desk's
    /// (`desk`, already resolved). "" (Same as Desk) is the Desk's; a pack font that was
    /// uninstalled draws as the Desk's and comes back if the pack is installed again.
    static func resolveMessage(saved: String, desk: String,
                               available: (String) -> Bool = DeskFont.isAvailable) -> String {
        if saved.isEmpty { return desk }
        return BundledFonts.bundled(saved) != nil || available(saved) ? saved : desk
    }

    /// The Desk's family from a defaults store.
    static func resolve(_ defaults: DefaultsStore, available: (String) -> Bool = DeskFont.isAvailable) -> String {
        resolve(saved: defaults.object(forKey: key) as? String, available: available)
    }

    /// Once each, before DeskMigration marks the suite, so an upgrade never changes the font on
    /// screen. First (2.6.0): a Desk suite an earlier version already used (`migrated` is set)
    /// with no font saved was drawing in the system font, so "" is written to keep it. Then
    /// (EsteFont Pro): a suite settled by 2.6.0 to 2.8.0 with no font saved was drawing in
    /// EsteFont 26, the default then, so "EsteFont 26" is written to keep it. A fresh install has
    /// neither key nor `migrated` and keeps nil, which resolves to EsteFont Pro.
    static func keepExistingDefault(desk d: DefaultsStore = UserDefaults.desk) {
        if !d.bool(forKey: settledKey) {
            if d.object(forKey: "migrated") != nil && d.object(forKey: key) == nil {
                d.set("", forKey: key)
            }
            d.set(true, forKey: settledKey)
            d.set(true, forKey: proSettledKey)
            return
        }
        guard !d.bool(forKey: proSettledKey) else { return }
        if d.object(forKey: key) == nil { d.set(heritageFamily, forKey: key) }
        d.set(true, forKey: proSettledKey)
    }

    // Family -> installed, cached because the Desk resolves its font on every render (the clock
    // ticks each second). Cleared when the Mac's font set changes and after registration. A
    // lookup that began before a clear never writes its answer after it (`generation`): the
    // Desk state export asks off the main thread at launch, and its "not installed" from before a
    // font pack registered stuck in the cache, so a picked pack font drew as EsteFont Pro.
    private static let lock = NSLock()
    nonisolated(unsafe) private static var cache: [String: Bool] = [:]
    nonisolated(unsafe) private static var generation = 0
    nonisolated(unsafe) private static var watcher: NSObjectProtocol?
    // Families of font packs released this session, lowercased. CoreText can't be trusted to drop
    // them: after the font set notice that follows launch, unregistering a pack's file fails with
    // "not registered" (201) while the family list goes on naming it, so the Desk kept drawing an
    // uninstalled pack's font. A registration takes its families back out.
    nonisolated(unsafe) private static var withdrawn: Set<String> = []

    /// Whether a font family is available to this process (installed, or registered from the
    /// bundle or a font pack). Asked of CoreText's family list: NSFontManager goes on listing a
    /// family it has just unregistered until the run loop turns (a view that draws it then gets
    /// LastResort's boxes), and stops listing a pack's family after the system's font set notice.
    /// A view reading this on the main thread redraws when the font set changes (FontEpoch).
    static func isAvailable(_ family: String) -> Bool {
        if Thread.isMainThread { _ = FontEpoch.shared.value }
        lock.lock()
        if watcher == nil {
            watcher = NotificationCenter.default.addObserver(
                forName: NSFont.fontSetChangedNotification, object: nil, queue: nil) { _ in
                DeskFont.forgetAvailability()
            }
        }
        if withdrawn.contains(family.lowercased()) { lock.unlock(); return false }
        if let hit = cache[family] { lock.unlock(); return hit }
        let asked = generation
        lock.unlock()
        let found = coreTextHas(family)
        lock.lock()
        if generation == asked { cache[family] = found }
        lock.unlock()
        return found
    }

    /// Whether CoreText lists the family for this process now. The family list, not a descriptor
    /// match: after the font set changed notice that follows a registration, matching a process-only
    /// family by descriptor (and NSFontManager's members) can come back empty while the list has it.
    static func coreTextHas(_ family: String) -> Bool {
        (CTFontManagerCopyAvailableFontFamilyNames() as? [String] ?? []).contains(family)
    }

    /// A font pack's families stop counting as available, whatever CoreText says (see `withdrawn`).
    /// A pack that duplicates an installed family hides it until the next launch.
    static func withdraw(_ families: [String]) {
        lock.lock(); withdrawn.formUnion(families.map { $0.lowercased() }); lock.unlock()
    }

    /// A registered pack's families count again.
    static func restore(_ families: [String]) {
        lock.lock(); withdrawn.subtract(families.map { $0.lowercased() }); lock.unlock()
    }

    /// The font set changed (a pack registered or released, Font Book, the bundled fonts): the
    /// cache is dropped and every view that resolved a family redraws with the new answer.
    static func forgetAvailability() {
        lock.lock(); cache.removeAll(); generation += 1; lock.unlock()
        DispatchQueue.main.async { FontEpoch.shared.bump() }
    }

    /// The font families for the Desk and widget pickers: the bundled families first (EsteFont
    /// Pro, then EsteFont 26), then everything installed and the installed font packs' families,
    /// sorted, without a second copy of any name or a released pack's family.
    static func pickerFamilies(installed: [String], packs: [String] = PackFonts.shared.families) -> [String] {
        let bundled = BundledFonts.families.map(\.name)
        lock.lock(); let gone = withdrawn; lock.unlock()
        var rest: [String] = []
        for name in installed.filter({ !gone.contains($0.lowercased()) }) + packs where !bundled.contains(name)
            && !rest.contains(where: { $0.caseInsensitiveCompare(name) == .orderedSame }) {
            rest.append(name)
        }
        return bundled + rest.sorted { $0.localizedCaseInsensitiveCompare($1) == .orderedAscending }
    }
}

/// A count that goes up each time the font set changes. Views that resolve a font family read it
/// (through `DeskFont.isAvailable`), so Observation redraws them when a pack is installed or
/// uninstalled, including pieces with no clock of their own, such as the message.
@Observable
final class FontEpoch: @unchecked Sendable {
    static let shared = FontEpoch()
    /// Written on the main thread only.
    private(set) var value = 0
    func bump() { value += 1 }
}
