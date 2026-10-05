import AppKit
import CoreText
import Foundation

/// EsteFont 26 (item 58): the author's handwriting, Regular and Bold, shipped inside the app at
/// Contents/Resources/Fonts/ and registered for Sanduhr's process only, so the Desk draws in it
/// on a Mac that has never had it installed. Nothing is installed system-wide.
enum BundledFonts {
    static let family = "EsteFont 26"
    static let regularFace = "EsteFont26-Regular"
    static let boldFace = "EsteFont26-Bold"
    /// The folder under the app's Resources the files sit in.
    static let folder = "Fonts"
    static let fileNames = ["\(regularFace).ttf", "\(boldFace).ttf"]

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

    /// The face to draw a family in: EsteFont 26 by PostScript name, Bold where the design wants
    /// weight; any other family by its family name, as before.
    static func face(_ family: String, bold: Bool) -> String {
        guard family == self.family else { return family }
        return bold ? boldFace : regularFace
    }
}

/// The Desk's font (the desk suite's `font`). New installs draw in EsteFont 26; "" is the system
/// font, which existing users who never picked a font keep (`keepExistingDefault`); a saved
/// family that is no longer installed falls back to EsteFont 26.
enum DeskFont {
    static let key = DeskLook.fontKey
    /// Desk suite: set once the upgrade below has run, whatever it did.
    static let settledKey = "fontDefaultSettled"
    static let defaultFamily = BundledFonts.family

    /// The family the Desk draws in for a saved value. nil (never chosen) is EsteFont 26; ""
    /// (System) stays the system font; a family that is not available is EsteFont 26.
    static func resolve(saved: String?, available: (String) -> Bool = DeskFont.isAvailable) -> String {
        guard let saved else { return defaultFamily }
        if saved.isEmpty || saved == defaultFamily || available(saved) { return saved }
        return defaultFamily
    }

    /// The Desk's family from a defaults store.
    static func resolve(_ defaults: DefaultsStore, available: (String) -> Bool = DeskFont.isAvailable) -> String {
        resolve(saved: defaults.object(forKey: key) as? String, available: available)
    }

    /// Once, before DeskMigration marks the suite: a Desk suite an earlier version already used
    /// (`migrated` is set) with no font saved was drawing in the system font, so "" is written to
    /// keep it. A fresh install has no `migrated` yet and keeps nil, which resolves to EsteFont 26.
    static func keepExistingDefault(desk d: DefaultsStore = UserDefaults.desk) {
        guard !d.bool(forKey: settledKey) else { return }
        if d.object(forKey: "migrated") != nil && d.object(forKey: key) == nil {
            d.set("", forKey: key)
        }
        d.set(true, forKey: settledKey)
    }

    // Family -> installed, cached because the Desk resolves its font on every render (the clock
    // ticks each second). Cleared when the Mac's font set changes and after registration.
    private static let lock = NSLock()
    nonisolated(unsafe) private static var cache: [String: Bool] = [:]
    nonisolated(unsafe) private static var watcher: NSObjectProtocol?

    /// Whether a font family is available to this process (installed, or registered from the
    /// bundle).
    static func isAvailable(_ family: String) -> Bool {
        lock.lock()
        if watcher == nil {
            watcher = NotificationCenter.default.addObserver(
                forName: NSFont.fontSetChangedNotification, object: nil, queue: nil) { _ in
                DeskFont.forgetAvailability()
            }
        }
        if let hit = cache[family] { lock.unlock(); return hit }
        lock.unlock()
        let found = NSFontManager.shared.availableMembers(ofFontFamily: family) != nil
        lock.lock(); cache[family] = found; lock.unlock()
        return found
    }

    static func forgetAvailability() {
        lock.lock(); cache.removeAll(); lock.unlock()
    }

    /// The font families for the Desk and widget pickers: EsteFont 26 first, then everything
    /// installed, sorted, without a second EsteFont 26.
    static func pickerFamilies(installed: [String]) -> [String] {
        [defaultFamily] + installed.filter { $0 != defaultFamily }.sorted {
            $0.localizedCaseInsensitiveCompare($1) == .orderedAscending
        }
    }
}
