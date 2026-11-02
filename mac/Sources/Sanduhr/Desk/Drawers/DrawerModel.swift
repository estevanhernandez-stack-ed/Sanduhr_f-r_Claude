import Foundation

/// Drawer value types (docs/mac-drawers-setup-spec.md, sections 1 to 3). Pure: values in, values
/// out, no AppKit and no file system, so the rules that decide which files may move are testable
/// without touching a real folder. The shapes mirror Windows' `DrawerModel.cs`.

/// A drawer: its id, the name it shows, the folder it shows, and whether Sanduhr owns that folder
/// (one it made under the Drawers root, which turning Drawers off empties) or only shows it (a view).
struct DrawerDef: Equatable, Hashable, Codable, Sendable {
    var id: String
    var name: String
    var home: String
    var owned: Bool
}

/// One file going into a drawer.
struct DrawerMove: Equatable, Hashable, Sendable {
    var source: String
    var drawerId: String
}

/// Who made a plan: the built-in rules, or the user's own hand.
enum DrawerPlanSource: String, Codable, Sendable { case rules, user }

/// A move plan: the drawers it fills and the moves. Moves are the only action a plan can hold.
struct DrawerPlan: Equatable, Sendable {
    var source: DrawerPlanSource
    var drawers: [DrawerDef]
    var moves: [DrawerMove]

    /// Moves grouped per drawer, in the order the drawers first appear.
    var grouped: [(drawer: DrawerDef, moves: [DrawerMove])] {
        drawers.map { d in (d, moves.filter { $0.drawerId == d.id }) }
    }
}

/// What the app read about one Desktop entry. The reading is the mover's job (item 3); the rules
/// see only these values.
struct DrawerItemFacts: Equatable, Sendable {
    var name: String
    var isDirectory: Bool
    var isPackage: Bool
    var isHidden: Bool
    var isSymlink: Bool
    var isAlias: Bool
    /// An iCloud item that is not downloaded; its size and dates can be stale.
    var isDataless: Bool
    /// `kMDItemIsScreenCapture`, set by macOS on screenshots and screen recordings in any language.
    var isScreenCapture: Bool
    var size: Int64
    var modified: Date

    init(name: String, isDirectory: Bool = false, isPackage: Bool = false, isHidden: Bool = false,
         isSymlink: Bool = false, isAlias: Bool = false, isDataless: Bool = false,
         isScreenCapture: Bool = false, size: Int64 = 0, modified: Date = .distantPast) {
        self.name = name
        self.isDirectory = isDirectory
        self.isPackage = isPackage
        self.isHidden = isHidden
        self.isSymlink = isSymlink
        self.isAlias = isAlias
        self.isDataless = isDataless
        self.isScreenCapture = isScreenCapture
        self.size = size
        self.modified = modified
    }

    /// Lowercased extension with its dot (".pdf"), or "" for none. A dotfile has none.
    var fileExtension: String { DrawerNames.extensionOf(name).lowercased() }
}

/// Drawer ids: the stable slug a layout word and a rule point at.
enum DrawerIds {
    static let maxLength = 40

    static func isValid(_ id: String) -> Bool {
        guard !id.isEmpty, id.count <= maxLength, id.first != "-" else { return false }
        return id.unicodeScalars.allSatisfy { isSlug($0) }
    }

    /// The id a name gets: lowercased, each run of other characters one hyphen, trimmed, cut to 40.
    /// A name with no ASCII letter or digit gets "drawer".
    static func from(_ name: String) -> String {
        var out = ""
        var pendingDash = false
        for u in name.lowercased().unicodeScalars {
            if isSlug(u) && u != "-" {
                if pendingDash && !out.isEmpty { out.append("-") }
                pendingDash = false
                out.unicodeScalars.append(u)
            } else {
                pendingDash = true
            }
        }
        if out.isEmpty { return "drawer" }
        return String(out.prefix(maxLength)).trimmingCharacters(in: CharacterSet(charactersIn: "-"))
    }

    private static func isSlug(_ u: Unicode.Scalar) -> Bool {
        (u >= "a" && u <= "z") || (u >= "0" && u <= "9") || u == "-"
    }
}

/// Drawer folder names and the Finder-style name a clash gets.
enum DrawerNames {
    static let maxLength = 40
    /// Numbered candidates tried after the plain name ("Report 2.pdf" to "Report 100.pdf").
    static let maxTries = 99

    /// A name that can be a drawer folder: 1 to 40 characters, no "/" or ":", not "." or "..",
    /// no leading dot, no control character, not blank.
    static func isValid(_ name: String) -> Bool {
        guard !name.trimmingCharacters(in: .whitespaces).isEmpty, name.count <= maxLength else { return false }
        if name == "." || name == ".." || name.hasPrefix(".") { return false }
        if name.contains("/") || name.contains(":") { return false }
        return !name.unicodeScalars.contains { $0.properties.generalCategory == .control }
    }

    /// The extension with its dot (".pdf"), or "". A leading dot or a trailing dot is not one.
    static func extensionOf(_ name: String) -> String {
        guard let dot = name.lastIndex(of: "."), dot != name.startIndex else { return "" }
        let ext = name[dot...]
        return ext.count > 1 ? String(ext) : ""
    }

    /// The name `name` can take: itself when free, else "Report 2.pdf", "Report 3.pdf" and on, as
    /// Finder numbers a clash. A folder is numbered after its whole name. Nil after 99 numbered
    /// tries, so a closure that never says no cannot loop. Never returns a name `exists` reported.
    static func free(_ name: String, isFolder: Bool = false, exists: (String) -> Bool) -> String? {
        if !exists(name) { return name }
        let ext = isFolder ? "" : extensionOf(name)
        let stem = String(name.dropLast(ext.count))
        for n in 2...(maxTries + 1) {
            let candidate = "\(stem) \(n)\(ext)"
            if !exists(candidate) { return candidate }
        }
        return nil
    }
}
