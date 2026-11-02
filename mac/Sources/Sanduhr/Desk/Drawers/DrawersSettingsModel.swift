import Foundation
import AppKit
import Observation

/// One row of the rules list.
struct DrawerRuleRow: Equatable, Identifiable {
    var id: Int
    var title: String
    var on: Bool
    var isCustom: Bool
    var canMoveUp: Bool
    var canMoveDown: Bool
}

/// One run in the Recent runs list. It carries a count and a time, never a file name.
struct DrawerRunRow: Equatable, Identifiable {
    var id: String
    var at: Date
    var files: Int
    var byHand: Bool
    /// A crash left this run open; Undo reverses what moved.
    var recovered: Bool

    var title: String { "\(files) \(files == 1 ? "file" : "files")\(byHand ? ", by hand" : "")\(recovered ? ", recovered" : "")" }
}

/// What Settings, Drawers shows and does (docs/mac-drawers-setup-spec.md, section 8). Every action
/// goes through `DrawerService`; this holds only the sentence the page shows and the question
/// waiting for a yes. The file and folder pickers stay in the view, which hands paths here.
@MainActor
@Observable
final class DrawersSettingsModel {
    /// Said under the page's controls: a refusal in the service's words, or the last result.
    private(set) var message: String?
    /// "Put N files back on the Desktop?" while the user decides.
    private(set) var offQuestion: String?

    @ObservationIgnored private let service: DrawerService
    @ObservationIgnored private let tidyAction: @MainActor () -> Void
    @ObservationIgnored private let openURL: (URL) -> Void

    init(service: DrawerService, tidy: @escaping @MainActor () -> Void = { DrawerPanelController.shared.tidy() },
         openURL: @escaping (URL) -> Void = { NSWorkspace.shared.open($0) }) {
        self.service = service
        self.tidyAction = tidy
        self.openURL = openURL
    }

    var settings: DrawerSettings { service.settings }

    // MARK: The switch, the root and Sort

    var enabled: Bool { settings.enabled }

    /// The Desktop Folder permission is denied: the row below the switch shows, with its button.
    var desktopDenied: Bool { service.desktopPermission == .denied }
    static let deniedLine = "Sanduhr can't see your Desktop."
    static let deniedDetail = "Allow Sanduhr under Files and Folders in Privacy & Security, then press Sort my desktop now."
    static let deniedButton = "Open Files and Folders…"

    /// Opens Privacy & Security, Files and Folders.
    func openFilesAndFolders() { openURL(DesktopPermission.settingsURL) }

    /// The switch: on at once, off only after the question.
    func setEnabled(_ on: Bool) {
        message = nil
        if on {
            offQuestion = nil
            service.turnOn()
        } else if enabled {
            offQuestion = service.offPrompt()
        }
    }

    /// Yes to the question: the files go back, and the result line says how many.
    func confirmOff() {
        guard offQuestion != nil else { return }
        offQuestion = nil
        message = Self.returnLine(service.confirmTurnOff())
    }

    func cancelOff() { offQuestion = nil }

    var rootPath: String { service.effectiveRoot }
    /// Choose… is offered only while no drawer of Sanduhr's own exists.
    var canChooseRoot: Bool { !service.hasOwnedDrawer }

    func chooseRoot(_ path: String) { message = service.chooseRoot(path) }
    func useDefaultRoot() { message = service.chooseRoot(nil) }

    /// Sort my desktop now: the review opens on the Desk. Nothing moves until Apply there.
    func sort() {
        guard enabled else { message = "Turn Drawers on first."; return }
        let review = service.sort()
        if desktopDenied { message = nil; return }
        message = review == nil ? "Nothing on the Desktop matches a rule." : "The review is on your Desk."
    }

    /// "12 put back on the Desktop." with what stayed behind, in counts.
    static func returnLine(_ r: DrawerRunResult) -> String {
        if let why = r.refusal { return why }
        let n = r.moved.count
        var line = "\(n) \(n == 1 ? "file" : "files") put back on the Desktop."
        if !r.skipped.isEmpty { line += " \(r.skipped.count) left where \(r.skipped.count == 1 ? "it was" : "they were")." }
        if !r.foldersKept.isEmpty { line += " \(r.foldersKept.count) \(r.foldersKept.count == 1 ? "folder kept" : "folders kept") something." }
        return line
    }

    // MARK: Rules

    var rules: [DrawerRuleRow] {
        let all = settings.rules
        return all.enumerated().map { i, r in
            DrawerRuleRow(id: i, title: Self.title(r), on: r.on, isCustom: r.isCustom,
                          canMoveUp: i > 0, canMoveDown: i < all.count - 1)
        }
    }

    static func title(_ r: DrawerRule) -> String {
        let text = r.text ?? ""
        switch r.kind {
        case .screenshots: return "Screenshots"
        case .images: return "Images"
        case .documents: return "Documents"
        case .installers: return "Installers"
        case .archives: return "Archives"
        case .links: return "Links"
        case .olderThan: return "Older files"
        case .nameContains: return "Name contains \"\(text)\" into \(r.drawerName ?? r.drawer)"
        case .nameStartsWith: return "Name starts with \"\(text)\" into \(r.drawerName ?? r.drawer)"
        }
    }

    func setRule(_ index: Int, on: Bool) {
        guard settings.rules.indices.contains(index) else { return }
        service.updateSettings { $0.rules[index].on = on }
    }

    /// Moves a rule one place up (-1) or down (1). Rules run top to bottom and the first match wins.
    func moveRule(_ index: Int, by step: Int) {
        let to = index + step
        guard settings.rules.indices.contains(index), settings.rules.indices.contains(to) else { return }
        service.updateSettings { $0.rules.swapAt(index, to) }
    }

    /// Removes a custom rule. The built-in ones stay (turn them off instead).
    func removeRule(_ index: Int) {
        guard settings.rules.indices.contains(index), settings.rules[index].isCustom else { return }
        service.updateSettings { $0.rules.remove(at: index) }
    }

    /// The Older files rule's days.
    var olderDays: Int { settings.rules.first { $0.kind == .olderThan }?.days ?? DrawerRules.defaultDays }

    /// Sets the days, clamped to 1 to 3650.
    func setOlderDays(_ days: Int) {
        let d = DrawerRules.clampDays(days)
        service.updateSettings { s in
            for i in s.rules.indices where s.rules[i].kind == .olderThan { s.rules[i].days = d }
        }
    }

    /// Adds a custom rule at the top of the list: names containing or starting with `text` go to
    /// the drawer called `drawer`, made on the first move when it doesn't exist. Returns why it was
    /// refused, or nil.
    @discardableResult
    func addCustomRule(startsWith: Bool, text: String, drawer: String) -> String? {
        let t = text.trimmingCharacters(in: .whitespaces)
        let name = drawer.trimmingCharacters(in: .whitespaces)
        guard !t.isEmpty else { message = "Type the words to look for."; return message }
        guard DrawerNames.isValid(name) else { message = "\"\(name)\" can't be a drawer name."; return message }
        let id = existingDrawerId(named: name) ?? freshId(for: name)
        var rule = DrawerRule(kind: startsWith ? .nameStartsWith : .nameContains, drawer: id, text: t)
        rule.drawerName = name
        service.updateSettings { $0.rules.insert(rule, at: 0) }
        message = nil
        return nil
    }

    private func existingDrawerId(named name: String) -> String? {
        let key = DrawerPaths.key(name)
        if let d = settings.drawers.first(where: { DrawerPaths.key($0.name) == key }) { return d.id }
        if let rule = settings.rules.first(where: { DrawerPaths.key($0.drawerName ?? "") == key }) { return rule.drawer }
        return DrawerRules.defaultNames.first { DrawerPaths.key($0) == key }.map(DrawerIds.from)
    }

    /// An id no drawer or rule uses yet.
    private func freshId(for name: String) -> String {
        let taken = Set(settings.drawers.map(\.id) + settings.rules.map(\.drawer) + DrawerRules.defaultNames.map(DrawerIds.from))
        let base = DrawerIds.from(name)
        var id = base, n = 2
        while taken.contains(id) { id = String(base.prefix(DrawerIds.maxLength - 3)) + "-\(n)"; n += 1 }
        return id
    }

    // MARK: View drawers and hidden ones

    var viewDrawers: [DrawerDef] { settings.drawers.filter { !$0.owned } }

    func addViewDrawer(_ folder: String) { message = service.addViewDrawer(folder: folder) }

    func removeViewDrawer(_ id: String) {
        if !service.removeViewDrawer(id) { message = "That drawer is Sanduhr's own. Turn Drawers off to remove it." }
    }

    var hiddenDrawers: [DrawerDef] {
        settings.hidden.compactMap { id in settings.drawers.first { $0.id == id } }
    }

    /// Show on a hidden drawer: its panel comes back.
    func show(_ id: String) { service.updateSettings { $0.hidden.removeAll { $0 == id } } }

    // MARK: Look and Tidy

    func setLook(_ look: DrawerLook) { service.updateSettings { $0.look = look } }

    /// The opacity, clamped to 20 to 100.
    func setOpacity(_ percent: Int) {
        let r = DrawerSettings.opacityRange
        service.updateSettings { $0.opacity = min(max(percent, r.lowerBound), r.upperBound) }
    }

    func setCorner(_ corner: DrawerCorner) { service.updateSettings { $0.tidyCorner = corner } }
    func setFlow(_ flow: DrawerFlow) { service.updateSettings { $0.tidyFlow = flow } }
    func tidy() { tidyAction() }

    // MARK: Recent runs

    /// The last 10 runs, newest first, then any a crash left open that Undo can still reverse.
    var runs: [DrawerRunRow] {
        let listed = Set(service.runs.map(\.id))
        let open = service.recovered.filter { !listed.contains($0.id) }
        return (service.runs.map { row($0, recovered: false) } + open.map { row($0, recovered: true) })
    }

    private func row(_ e: JournalEntry, recovered: Bool) -> DrawerRunRow {
        DrawerRunRow(id: e.id, at: e.at, files: e.writtenMoves.count, byHand: e.source == .user, recovered: recovered)
    }

    func undo(_ id: String) {
        guard let r = service.undo(id) else { message = "That run is no longer in the journal."; return }
        message = DrawerResultModel(undone: r).line
    }
}
