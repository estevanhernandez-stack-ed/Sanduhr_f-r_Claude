import Foundation

/// What a rule looks at (spec section 2): a kind of file, an age, or the name.
enum DrawerRuleKind: String, Codable, Sendable {
    case screenshots, images, documents, installers, archives, links, olderThan, nameContains, nameStartsWith
}

/// One rule: a filter and the drawer (by id) it fills. `text` is for the name rules, `days` for
/// Older files.
struct DrawerRule: Equatable, Codable, Sendable {
    var kind: DrawerRuleKind
    var drawer: String
    var on: Bool = true
    var text: String? = nil
    var days: Int = DrawerRules.defaultDays
}

/// The built-in sorter. Rules run top to bottom and the first match wins; whatever no rule takes
/// stays on the Desktop, with no catch-all drawer. Nothing here moves a file: a plan is only ever
/// applied after the user's review.
enum DrawerRules {
    static let defaultDays = 30
    static let daysRange = 1...3650

    private static let imageExt: Set<String> = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".heic", ".heif", ".tif", ".tiff", ".svg"]
    // The last three are iWork files saved flat; the bundle form is a package and never matches.
    private static let documentExt: Set<String> = [".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".md", ".rtf",
                                                   ".odt", ".ods", ".odp", ".csv", ".pages", ".numbers", ".key"]
    private static let installerExt: Set<String> = [".dmg", ".pkg", ".mpkg"]
    private static let archiveExt: Set<String> = [".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".xz", ".iso"]
    private static let linkExt: Set<String> = [".webloc", ".inetloc"]

    /// The default drawer names, in rule order.
    static let defaultNames = ["Screenshots", "Images", "Documents", "Installers", "Archives", "Links", "Older files"]

    /// The built-in set, in order, all on. Older files goes last so it takes only what the kind
    /// rules left.
    static let defaults: [DrawerRule] = [
        DrawerRule(kind: .screenshots, drawer: "screenshots"),
        DrawerRule(kind: .images, drawer: "images"),
        DrawerRule(kind: .documents, drawer: "documents"),
        DrawerRule(kind: .installers, drawer: "installers"),
        DrawerRule(kind: .archives, drawer: "archives"),
        DrawerRule(kind: .links, drawer: "links"),
        DrawerRule(kind: .olderThan, drawer: "older-files", days: defaultDays),
    ]

    /// The drawers the defaults fill, each owned and directly in `root`.
    static func defaultDrawers(root: String) -> [DrawerDef] {
        defaultNames.map { DrawerDef(id: DrawerIds.from($0), name: $0, home: DrawerPaths.join(root, $0), owned: true) }
    }

    /// Days clamped to 1 to 3650.
    static func clampDays(_ days: Int) -> Int { min(max(days, daysRange.lowerBound), daysRange.upperBound) }

    /// Never moved by any rule: folders, packages, hidden items, symlinks, dataless items and the
    /// files macOS keeps on a Desktop for itself. The root and drawer folders are folders, and the
    /// plan builder also names them.
    static func isNeverMoved(_ f: DrawerItemFacts) -> Bool {
        f.isDirectory || f.isPackage || f.isHidden || f.isSymlink || f.isDataless
            || f.name.hasPrefix(".") || isHousekeeping(f.name)
    }

    static func matches(_ rule: DrawerRule, _ f: DrawerItemFacts, now: Date) -> Bool {
        guard rule.on, !isNeverMoved(f) else { return false }
        let ext = f.fileExtension
        switch rule.kind {
        case .screenshots:
            return f.isScreenCapture
                || (imageExt.contains(ext) && (hasPrefixCI(f.name, "Screenshot") || hasPrefixCI(f.name, "Screen Shot")))
        case .images: return imageExt.contains(ext)
        case .documents: return documentExt.contains(ext)
        case .installers: return installerExt.contains(ext)
        case .archives: return archiveExt.contains(ext)
        case .links: return linkExt.contains(ext) || f.isAlias
        case .olderThan:
            return now.timeIntervalSince(f.modified) > Double(clampDays(rule.days)) * 86_400
        case .nameContains:
            guard let t = rule.text, !t.isEmpty else { return false }
            return f.name.range(of: t, options: [.caseInsensitive, .diacriticInsensitive]) != nil
        case .nameStartsWith:
            guard let t = rule.text, !t.isEmpty else { return false }
            return f.name.range(of: t, options: [.caseInsensitive, .diacriticInsensitive, .anchored]) != nil
        }
    }

    /// The rules' plan for a Desktop listing: each file goes to the drawer of the first rule that
    /// matches it and names a known drawer (an existing one, or a default id, which is created
    /// under `root` with its default name). The plan carries only the drawers it fills.
    static func plan(facts: [DrawerItemFacts], rules: [DrawerRule], drawers: [DrawerDef],
                     desktop: String, root: String, now: Date) -> DrawerPlan {
        var known: [String: DrawerDef] = [:]
        for d in drawers where known[d.id] == nil { known[d.id] = d }
        for d in defaultDrawers(root: root) where known[d.id] == nil { known[d.id] = d }
        let desktopPath = DrawerPaths.normalize(desktop) ?? desktop
        // A drawer's folder (or the root) sitting on the Desktop is never a source, whatever its flags say.
        let ownFolders = Set((drawers.map(\.home) + [root]).compactMap { DrawerPaths.normalize($0) }
            .filter { DrawerPaths.isChild($0, of: desktopPath) }.map { DrawerPaths.key($0) })

        var moves: [DrawerMove] = []
        var used: [DrawerDef] = []
        let sorted = facts.sorted { $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending }
        for f in sorted {
            let path = DrawerPaths.join(desktopPath, f.name)
            if ownFolders.contains(DrawerPaths.key(path)) { continue }
            guard let rule = rules.first(where: { known[$0.drawer] != nil && matches($0, f, now: now) }),
                  let target = known[rule.drawer] else { continue }
            moves.append(DrawerMove(source: path, drawerId: target.id))
            if !used.contains(where: { $0.id == target.id }) { used.append(target) }
        }
        return DrawerPlan(source: .rules, drawers: used, moves: moves)
    }

    private static func hasPrefixCI(_ s: String, _ p: String) -> Bool {
        s.range(of: p, options: [.caseInsensitive, .anchored]) != nil
    }

    /// Files macOS keeps on a Desktop for itself: ".DS_Store", ".localized", and "Icon\r", a custom
    /// folder icon whose name ends in a carriage return.
    private static func isHousekeeping(_ name: String) -> Bool {
        name == ".DS_Store" || name == ".localized" || name == "Icon\r"
    }
}
