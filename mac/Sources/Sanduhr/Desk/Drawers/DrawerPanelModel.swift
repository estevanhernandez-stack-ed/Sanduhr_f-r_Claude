import Foundation

/// The values behind a drawer panel's look (docs/mac-drawers-setup-spec.md, section 7). Pure: the
/// panel only reads these numbers, so the four looks are tested without drawing anything.
struct DrawerLookValues: Equatable {
    /// The theme surface's alpha behind the content (Solid), or the faint fill that lets clicks land.
    var fillAlpha: Double
    /// The border's alpha, 0 for none.
    var borderAlpha: Double
    /// A behind-window blur (`NSVisualEffectView`, `.hudWindow`) draws the plate.
    var usesBlur: Bool
    /// The blur's own alpha (Glass follows the opacity).
    var blurAlpha: Double

    /// The faint fill Outline and Bare keep so a click on the empty plate still lands on the panel.
    static let clickFill = 0.02
    static let outlineBorder = 0.7
    static let edge = 0.12

    static func clampOpacity(_ percent: Int) -> Double {
        Double(min(max(percent, DrawerSettings.opacityRange.lowerBound), DrawerSettings.opacityRange.upperBound)) / 100
    }

    static func of(_ look: DrawerLook, opacity: Int, hovering: Bool = false) -> DrawerLookValues {
        let a = clampOpacity(opacity)
        switch look {
        case .solid: return DrawerLookValues(fillAlpha: a, borderAlpha: edge, usesBlur: false, blurAlpha: 0)
        case .glass: return DrawerLookValues(fillAlpha: 0, borderAlpha: edge, usesBlur: true, blurAlpha: a)
        case .outline: return DrawerLookValues(fillAlpha: clickFill, borderAlpha: outlineBorder, usesBlur: false, blurAlpha: 0)
        case .bare: return DrawerLookValues(fillAlpha: clickFill, borderAlpha: hovering ? outlineBorder : 0, usesBlur: false, blurAlpha: 0)
        }
    }

    /// Opacity only matters for the two looks that draw a plate.
    static func usesOpacity(_ look: DrawerLook) -> Bool { look == .solid || look == .glass }

    static func name(_ look: DrawerLook) -> String {
        switch look {
        case .solid: return "Solid"
        case .glass: return "Glass"
        case .outline: return "Outline"
        case .bare: return "Bare"
        }
    }
}

/// The line a panel shows in place of an empty grid when the Desktop can't be read.
enum DrawerPanelNote {
    static let cantSeeDesktop = "Sanduhr can't see your Desktop."

    /// The line to show, or nil: only when the permission is denied and the drawer has nothing to show.
    static func text(denied: Bool, entries: Int) -> String? { denied && entries == 0 ? cantSeeDesktop : nil }
}

/// The words on a panel's title bar.
enum DrawerTitle {
    /// "Screenshots · 14".
    static func text(name: String, count: Int) -> String { "\(name) · \(count)" }
}

/// How an item's name is drawn: extensions follow the Finder's "Show all filename extensions".
enum DrawerDisplay {
    /// The name as shown. With the Finder setting on every extension shows; off, a file whose
    /// extension is flagged hidden (a screenshot's) loses it. A folder keeps its whole name.
    static func name(_ name: String, isFolder: Bool, showAllExtensions: Bool, hiddenExtension: Bool) -> String {
        guard !isFolder, !showAllExtensions, hiddenExtension else { return name }
        let ext = DrawerNames.extensionOf(name)
        return ext.isEmpty ? name : String(name.dropLast(ext.count))
    }
}

/// What a key does in a drawer's grid.
enum DrawerKeyAction: Equatable {
    case open, quickLook, reveal, trash

    /// Return or Enter opens, Space previews, Command-R reveals, Command-Delete trashes.
    static func of(keyCode: UInt16, command: Bool, characters: String?) -> DrawerKeyAction? {
        if command {
            if keyCode == 51 || keyCode == 117 { return .trash }
            if characters?.lowercased() == "r" { return .reveal }
            return nil
        }
        switch keyCode {
        case 36, 76: return .open
        case 49: return .quickLook
        default: return nil
        }
    }
}

/// What Edit changes in a pending review before Apply: the files unticked and the new names.
struct DrawerReviewEdit: Equatable {
    var unticked: Set<String> = []
    var renames: [String: String] = [:]

    var isEmpty: Bool { unticked.isEmpty && renames.isEmpty }

    mutating func toggle(_ source: String) {
        if !unticked.insert(source).inserted { unticked.remove(source) }
    }
}

/// The review bar's content (spec section 3): "N files into M drawers", each drawer with up to
/// three names. Built from the service's pending review and the user's edits, so the counts
/// follow what is ticked.
/// One file in the review's Edit list.
struct DrawerReviewFile: Equatable {
    var source: String
    var name: String
    var ticked: Bool
}

struct DrawerReviewModel: Equatable {
    static let namedFiles = 3

    struct Line: Equatable {
        var id: String
        var name: String
        var isNew: Bool
        var count: Int
        /// Up to three file names, for the collapsed line.
        var names: [String]
        /// Every file the plan puts in the drawer, for Edit: its path, the name shown and whether it is ticked.
        var files: [DrawerReviewFile]
        /// Why the new name can't be used, or nil.
        var nameError: String?

        /// "Screenshots (new): 3 files, including a, b and c".
        var text: String {
            "\(name)\(isNew ? " (new)" : ""): \(count) \(count == 1 ? "file" : "files")\(DrawerReviewModel.including(names))"
        }
    }

    var title: String
    /// Every drawer the plan fills, ticked or not, for Edit.
    var lines: [Line]
    /// The drawers with something ticked, for the summary.
    var summary: [Line] { lines.filter { $0.count > 0 } }
    var fileCount: Int
    var drawerCount: Int
    /// Apply can run: something ticked, and no new name refused.
    var canApply: Bool

    static func title(files: Int, drawers: Int) -> String {
        "\(files) \(files == 1 ? "file" : "files") into \(drawers) \(drawers == 1 ? "drawer" : "drawers")"
    }

    static func including(_ names: [String]) -> String {
        switch names.count {
        case 0: return ""
        case 1: return ", including \(names[0])"
        default: return ", including \(names.dropLast().joined(separator: ", ")) and \(names[names.count - 1])"
        }
    }

    /// `existing` are the user's drawers now: a plan drawer not among them is new and can be renamed.
    init(review: DrawerReview, existing: [DrawerDef], edit: DrawerReviewEdit = DrawerReviewEdit()) {
        let existingIds = Set(existing.map(\.id))
        let kept = review.plan.moves.filter { !edit.unticked.contains($0.source) }
        var lines: [Line] = []
        for d in review.plan.drawers {
            let all = review.plan.moves.filter { $0.drawerId == d.id }
            guard !all.isEmpty else { continue }
            let isNew = !existingIds.contains(d.id)
            let shown = isNew ? (edit.renames[d.id] ?? d.name) : d.name
            let files = all.map { DrawerReviewFile(source: $0.source, name: DrawerFS.name(of: $0.source), ticked: !edit.unticked.contains($0.source)) }
            let ticked = files.filter(\.ticked)
            lines.append(Line(id: d.id, name: shown, isNew: isNew, count: ticked.count,
                              names: ticked.prefix(Self.namedFiles).map(\.name), files: files,
                              nameError: isNew ? Self.renameError(d.id, shown, plan: review.plan, existing: existing, edit: edit) : nil))
        }
        self.lines = lines
        fileCount = kept.count
        let shownLines = lines.filter { $0.count > 0 }
        drawerCount = shownLines.count
        title = Self.title(files: kept.count, drawers: shownLines.count)
        canApply = !kept.isEmpty && !shownLines.contains { $0.nameError != nil }
    }

    /// Why `name` can't be the folder name of the new drawer `id`: not a valid folder name, or the
    /// same (ignoring case) as another drawer's, nil when it can. The drawer's own name is fine.
    static func renameError(_ id: String, _ name: String, plan: DrawerPlan, existing: [DrawerDef], edit: DrawerReviewEdit) -> String? {
        guard DrawerNames.isValid(name) else { return "\"\(name)\" can't be a folder name." }
        let existingIds = Set(existing.map(\.id))
        var others = existing.map(\.name)
        for d in plan.drawers where d.id != id && !existingIds.contains(d.id) { others.append(edit.renames[d.id] ?? d.name) }
        if others.contains(where: { DrawerPaths.key($0) == DrawerPaths.key(name) }) { return "A drawer named \"\(name)\" exists." }
        return nil
    }

    /// The plan after the edits: unticked moves dropped, a drawer left with none dropped, a new
    /// drawer taking its new name and the folder of that name under `root`. Its id stays.
    static func editedPlan(_ review: DrawerReview, existing: [DrawerDef], edit: DrawerReviewEdit, root: String) -> DrawerPlan {
        let existingIds = Set(existing.map(\.id))
        let moves = review.plan.moves.filter { !edit.unticked.contains($0.source) }
        let used = Set(moves.map(\.drawerId))
        let drawers = review.plan.drawers.filter { used.contains($0.id) }.map { d -> DrawerDef in
            guard !existingIds.contains(d.id), let name = edit.renames[d.id], DrawerNames.isValid(name) else { return d }
            var r = d
            r.name = name
            r.home = DrawerPaths.join(root, name)
            return r
        }
        return DrawerPlan(source: review.plan.source, drawers: drawers, moves: moves)
    }
}

/// The result bar's content: what a run did, the skips with their reasons, and what Undo does.
struct DrawerResultModel: Equatable {
    enum Undo: Equatable { case none, run(String), tidy }
    static let namedSkips = 3

    var line: String
    var undo: Undo
    var canUndo: Bool { undo != .none }

    /// "13 moved, 2 skipped: a.txt changed since the review. And 1 more." Names the first three.
    init(_ result: DrawerRunResult) {
        if let why = result.refusal { line = why; undo = .none; return }
        let moved = result.moved.count
        if result.skipped.isEmpty {
            line = "\(moved) moved."
        } else {
            let named = result.skipped.prefix(Self.namedSkips).map { "\(DrawerFS.name(of: $0.path)) \($0.reason)." }.joined(separator: " ")
            let more = result.skipped.count > Self.namedSkips ? " And \(result.skipped.count - Self.namedSkips) more." : ""
            line = "\(moved) moved, \(result.skipped.count) skipped: \(named)\(more)"
        }
        if moved > 0, let id = result.entryId { undo = .run(id) } else { undo = .none }
    }

    /// A bar that only says something.
    init(message: String) {
        line = message
        undo = .none
    }

    /// The bar after Tidy.
    init(tidied count: Int) {
        line = count == 0 ? "The drawers are already lined up." : (count == 1 ? "1 drawer lined up." : "\(count) drawers lined up.")
        undo = count == 0 ? .none : .tidy
    }

    /// The bar after an Undo: the line, and nothing further to undo.
    init(undone result: DrawerRunResult) {
        var r = DrawerResultModel(result)
        r.undo = .none
        self = r
    }

    /// A run that moved nothing and skipped nothing needs no bar.
    static func worthShowing(_ result: DrawerRunResult) -> Bool {
        result.refusal != nil || !result.skipped.isEmpty
    }
}
