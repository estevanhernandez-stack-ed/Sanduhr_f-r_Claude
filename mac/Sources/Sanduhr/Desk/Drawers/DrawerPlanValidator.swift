import Foundation

/// Path rules for drawers. Everything is normalized first, so a doubled slash, "." and ".." cannot
/// slip a path past a root. Comparisons ignore case, as the default macOS volume does.
enum DrawerPaths {
    /// The absolute path with "." and ".." resolved and no trailing slash, or nil for a relative,
    /// empty or NUL-bearing one. "/" stays "/".
    static func normalize(_ path: String?) -> String? {
        guard let path, path.hasPrefix("/"), !path.contains("\0") else { return nil }
        var parts: [Substring] = []
        for p in path.split(separator: "/", omittingEmptySubsequences: true) {
            if p == "." { continue }
            if p == ".." { _ = parts.popLast(); continue }
            parts.append(p)
        }
        return "/" + parts.joined(separator: "/")
    }

    static func join(_ dir: String, _ name: String) -> String { dir == "/" ? "/" + name : dir + "/" + name }

    /// The comparison key of a normalized path.
    static func key(_ path: String) -> String { path.precomposedStringWithCanonicalMapping.lowercased() }

    static func parent(_ path: String) -> String {
        guard let i = path.lastIndex(of: "/"), i != path.startIndex else { return "/" }
        return String(path[..<i])
    }

    /// True when `path` sits directly in `dir`. Both normalized.
    static func isChild(_ path: String, of dir: String) -> Bool {
        path != "/" && key(parent(path)) == key(dir)
    }

    /// True when `path` is `root` or anywhere under it. Both normalized.
    static func isInside(_ path: String, _ root: String) -> Bool {
        let p = key(path), r = key(root)
        return p == r || r == "/" || p.hasPrefix(r + "/")
    }

    /// "/" or a mount point directly under /Volumes.
    static func isVolumeRoot(_ path: String) -> Bool {
        if path == "/" { return true }
        let parts = path.split(separator: "/")
        return parts.count == 2 && parts[0].lowercased() == "volumes"
    }
}

/// Where plans may reach, with the volume identifiers the app read (an input, so this stays pure).
struct DrawerRoots: Equatable, Sendable {
    var desktop: String
    var drawersRoot: String
    var home: String
    var desktopVolume: String
    var rootVolume: String
}

/// A checked plan: when `ok`, the drawers to create and the moves to run (no-ops dropped);
/// otherwise one error and nothing to run.
struct DrawerPlanCheck: Equatable, Sendable {
    var ok: Bool
    var error: String?
    var drawers: [DrawerDef] = []
    var moves: [DrawerMove] = []
}

/// Checks a move plan however it arrived. Any entry outside the roots refuses the whole plan, so a
/// half-good plan never runs half. The mover checks each path again with the file system in hand;
/// this is the pure half.
enum DrawerPlanValidator {
    static let maxDrawers = 24
    static let maxMoves = 2000

    /// Why a Drawers root is refused, or nil when it is safe: not the Desktop, not a folder that
    /// holds it, not "/", a volume root or the home folder, and on the Desktop's volume.
    static func rootRefusal(root: String, desktop: String, home: String, rootVolume: String, desktopVolume: String) -> String? {
        guard let r = DrawerPaths.normalize(root), let d = DrawerPaths.normalize(desktop) else {
            return "The Drawers folder must be a full path."
        }
        if DrawerPaths.isVolumeRoot(r) { return "The Drawers folder can't be a whole disk." }
        if let h = DrawerPaths.normalize(home), DrawerPaths.key(r) == DrawerPaths.key(h) {
            return "The Drawers folder can't be your home folder."
        }
        if DrawerPaths.isInside(d, r) { return "The Drawers folder can't be the Desktop or a folder that holds it." }
        if rootVolume != desktopVolume { return "The Drawers folder must be on the same disk as the Desktop." }
        return nil
    }

    static func check(_ plan: DrawerPlan, roots: DrawerRoots, existing: [DrawerDef]) -> DrawerPlanCheck {
        if plan.drawers.count > maxDrawers { return refuse("A plan can hold at most \(maxDrawers) drawers.") }
        if plan.moves.count > maxMoves { return refuse("A plan can hold at most \(maxMoves) moves.") }
        guard let desktop = DrawerPaths.normalize(roots.desktop), let root = DrawerPaths.normalize(roots.drawersRoot) else {
            return refuse("The drawer roots are not full paths.")
        }
        if let why = rootRefusal(root: root, desktop: desktop, home: roots.home,
                                 rootVolume: roots.rootVolume, desktopVolume: roots.desktopVolume) {
            return refuse(why)
        }

        // First wins on a duplicated id, as the rule engine does.
        var known: [String: DrawerDef] = [:]
        for d in existing where known[d.id] == nil { known[d.id] = d }
        var created: [DrawerDef] = []
        for d in plan.drawers {
            guard DrawerIds.isValid(d.id) else { return refuse("\"\(d.id)\" is not a drawer id.") }
            guard DrawerNames.isValid(d.name) else { return refuse("\"\(d.name)\" can't be a folder name.") }
            let home = DrawerPaths.normalize(d.home)
            if let had = known[d.id] {
                if home.map(DrawerPaths.key) != DrawerPaths.normalize(had.home).map(DrawerPaths.key) {
                    return refuse("The drawer \"\(d.name)\" already has a home.")
                }
                continue
            }
            // A new drawer's folder is its own name in the root.
            guard d.owned, let home, DrawerPaths.key(home) == DrawerPaths.key(DrawerPaths.join(root, d.name)) else {
                return refuse("The drawer \"\(d.name)\" must live in the Drawers folder, in a folder of its own name.")
            }
            if known.values.contains(where: { DrawerPaths.normalize($0.home).map(DrawerPaths.key) == DrawerPaths.key(home) }) {
                return refuse("Another drawer already uses the folder \"\(d.name)\".")
            }
            var made = d
            made.home = home
            known[d.id] = made
            created.append(made)
        }

        let homes = known.values.compactMap { DrawerPaths.normalize($0.home) }
        var moves: [DrawerMove] = []
        var seen = Set<String>()
        for m in plan.moves {
            guard let target = known[m.drawerId] else { return refuse("There is no drawer \"\(m.drawerId)\".") }
            guard let source = DrawerPaths.normalize(m.source), source != "/" else {
                return refuse("\"\(m.source)\" is outside the Desktop and the drawers.")
            }
            guard seen.insert(DrawerPaths.key(source)).inserted else { return refuse("\"\(m.source)\" is in the plan twice.") }
            // The root sits on the Desktop by default, so it (or a folder holding it) would otherwise pass as a Desktop item.
            if DrawerPaths.isInside(root, source) { return refuse("\"\(m.source)\" is or holds the Drawers folder, which can't go into a drawer.") }
            if homes.contains(where: { DrawerPaths.isInside($0, source) }) {
                return refuse("\"\(m.source)\" is or holds a drawer's own folder.")
            }
            let fromDesktop = DrawerPaths.isChild(source, of: desktop)
            let fromDrawer = homes.contains { DrawerPaths.isChild(source, of: $0) }
            guard fromDesktop || fromDrawer else { return refuse("\"\(m.source)\" is outside the Desktop and the drawers.") }
            if let th = DrawerPaths.normalize(target.home), DrawerPaths.isChild(source, of: th) { continue } // already there
            moves.append(DrawerMove(source: source, drawerId: m.drawerId))
        }
        return DrawerPlanCheck(ok: true, error: nil, drawers: created, moves: moves)
    }

    private static func refuse(_ error: String) -> DrawerPlanCheck { DrawerPlanCheck(ok: false, error: error) }
}
