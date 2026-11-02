import AppKit
import Observation
import SwiftUI

/// Draws the drawers on the Desk (docs/mac-drawers-setup-spec.md, section 7): one panel per drawer
/// that is not hidden, while Drawers is on and the Desk is showing, plus the review and result
/// bars. Everything it shows comes from `DrawerService`'s published values, and it redraws only
/// when they change (Observation re-arms after each change). There is no timer and no redraw loop.
@MainActor
final class DrawerPanelController {
    static let shared = DrawerPanelController()

    private let service: DrawerService
    private var panels: [String: DrawerPanelWindow] = [:]
    private var dragging = false
    private var tidyUndo: [String: DrawerPlace]?
    private let bar = DrawerBarModel()
    private var barWindow: DrawerBarWindow?
    private var barHost: NSHostingView<DrawerBarView>?
    private var tracking = false

    init(service: DrawerService? = nil) { self.service = service ?? DrawerService.shared }

    /// True while the panels should be up: Drawers on and the Desk showing.
    private var shouldShow: Bool { service.settings.enabled && DeskController.shared.running }

    // MARK: Refresh

    /// Brings the windows in line with the service: called when the Desk starts, stops or moves
    /// screens, and whenever the service publishes a change.
    func refresh() {
        armTracking()
        guard shouldShow, let screen = DeskController.shared.window?.screen ?? NSScreen.main else {
            closeAll()
            return
        }
        syncPanels(on: screen)
        syncBar(on: screen)
    }

    /// Re-arms the observation: any change to what the panels read calls `refresh` once.
    private func armTracking() {
        guard !tracking else { return }
        tracking = true
        withObservationTracking {
            _ = service.settings
            _ = service.contents
            _ = service.pending
            _ = bar.editing
            _ = bar.result
        } onChange: { [weak self] in
            Task { @MainActor in
                self?.tracking = false
                self?.refresh()
            }
        }
    }

    private func closeAll() {
        for w in panels.values { w.close() }
        panels = [:]
        barWindow?.close()
        barWindow = nil
        barHost = nil
    }

    private var visibleDrawers: [DrawerDef] {
        service.settings.drawers.filter { !service.settings.hidden.contains($0.id) }
    }

    // MARK: Panels

    private func syncPanels(on screen: NSScreen) {
        let drawers = visibleDrawers
        let ids = Set(drawers.map(\.id))
        for (id, w) in panels where !ids.contains(id) { w.close(); panels[id] = nil }
        let frames = effectiveFrames(drawers, screen: screen)
        for d in drawers {
            let w = panels[d.id] ?? makePanel(d.id)
            (w.contentView as? DrawerPanelView)?.configure(state(for: d))
            if let f = frames[d.id], !dragging, !w.frame.equalTo(f) { w.setFrame(f, display: true) }
            if !w.isVisible { w.orderFront(nil) }
        }
    }

    private func makePanel(_ id: String) -> DrawerPanelWindow {
        let w = DrawerPanelWindow()
        let view = DrawerPanelView(frame: NSRect(origin: .zero, size: w.frame.size))
        wire(view.actions, id: id)
        w.contentView = view
        panels[id] = w
        return w
    }

    private func state(for d: DrawerDef) -> DrawerPanelState {
        let s = service.settings
        return DrawerPanelState(id: d.id, name: d.name, entries: service.contents[d.id] ?? [],
                                folded: s.folded.contains(d.id), look: s.lookFor(d.id), opacity: s.opacity,
                                showAllExtensions: UserDefaults.standard.bool(forKey: "AppleShowAllExtensions"),
                                ownLook: s.looks[d.id])
    }

    // MARK: Placement

    private func screenKey(_ screen: NSScreen) -> String {
        let n = (screen.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber)?.uint32Value
        return DrawerPlacement.screenKey(displayID: n)
    }

    /// Each shown drawer's AppKit frame: its saved place on this screen (kept on it), else the next free slot.
    private func effectiveFrames(_ drawers: [DrawerDef], screen: NSScreen) -> [String: CGRect] {
        let work = workArea(screen)
        let key = screenKey(screen)
        let s = service.settings
        var rects: [String: CGRect] = [:]
        var fresh: [(id: String, folded: Bool)] = []
        for d in drawers {
            let size = DrawerPlacement.size(folded: s.folded.contains(d.id))
            if let p = s.places[d.id], p.screen == key {
                let o = DrawerPlacement.clamp(CGPoint(x: p.x, y: p.y), size: size, work: work.size)
                rects[d.id] = CGRect(origin: o, size: size)
            } else {
                fresh.append((d.id, s.folded.contains(d.id)))
            }
        }
        let arrived = DrawerPlacement.arrive(fresh, placed: Array(rects.values), work: work, corner: s.tidyCorner, flow: s.tidyFlow)
        for f in fresh {
            let size = DrawerPlacement.size(folded: f.folded)
            rects[f.id] = CGRect(origin: arrived[f.id] ?? .zero, size: size)
        }
        return rects.mapValues { DrawerPlacement.globalFrame($0, in: screen.visibleFrame) }
    }

    private func workArea(_ screen: NSScreen) -> CGRect { CGRect(origin: .zero, size: screen.visibleFrame.size) }

    /// Where a title-bar drag settles: snapped to the other drawers, then the Desk's pieces, then the margin.
    private func settle(_ id: String, proposed: CGRect, snapping: Bool) -> CGRect {
        guard let screen = DeskController.shared.window?.screen ?? NSScreen.main else { return proposed }
        let visible = screen.visibleFrame
        let others = panels.filter { $0.key != id }.map { DrawerPlacement.workRect(ofFrame: $0.value.frame, in: visible) }
        let snapped = DrawerSnap.snap(DrawerPlacement.workRect(ofFrame: proposed, in: visible), work: workArea(screen),
                                      drawers: others, pieces: deskPieces(on: screen), snapping: snapping)
        return DrawerPlacement.globalFrame(snapped, in: visible)
    }

    /// The Desk's pieces as work-space rects, for snapping against.
    private func deskPieces(on screen: NSScreen) -> [CGRect] {
        DeskController.shared.model.elements()
            .filter { $0.kind != .meterRow && $0.kind != .meetings && !$0.frame.isEmpty }
            .map { DrawerPlacement.workRect(ofDesk: $0.drawnFrame, screenFrame: screen.frame, visible: screen.visibleFrame) }
    }

    private func place(_ id: String, frame: CGRect) {
        guard let screen = DeskController.shared.window?.screen ?? NSScreen.main else { return }
        let o = DrawerPlacement.offset(ofFrame: frame, in: screen.visibleFrame)
        let key = screenKey(screen)
        service.updateSettings { $0.places[id] = DrawerPlace(screen: key, x: Double(o.x), y: Double(o.y)) }
    }

    // MARK: Panel actions

    private func wire(_ a: DrawerPanelActions, id: String) {
        a.toggleFold = { [weak self] in self?.toggleFold(id) }
        a.dragProposed = { [weak self] frame, option in
            self?.dragging = true
            return self?.settle(id, proposed: frame, snapping: !option) ?? frame
        }
        a.dragEnded = { [weak self] frame in
            self?.dragging = false
            self?.place(id, frame: frame)
        }
        a.open = { paths in paths.forEach { NSWorkspace.shared.open(URL(fileURLWithPath: $0)) } }
        a.reveal = { paths in NSWorkspace.shared.activateFileViewerSelecting(paths.map { URL(fileURLWithPath: $0) }) }
        a.renameItem = { [weak self] path in self?.renameItem(path) }
        a.trash = { [weak self] paths in self?.trash(paths) }
        a.drop = { [weak self] paths in self?.drop(paths, onto: id) }
        a.renameDrawer = { [weak self] in self?.renameDrawer(id) }
        a.hide = { [weak self] in self?.service.updateSettings { if !$0.hidden.contains(id) { $0.hidden.append(id) } } }
        a.setLook = { [weak self] look in self?.service.updateSettings { $0.looks[id] = look } }
        a.tidy = { [weak self] in self?.tidy() }
        a.openSettings = { SettingsWindowController.shared.show(.deskLook) }
    }

    private func toggleFold(_ id: String) {
        service.updateSettings { s in
            if let i = s.folded.firstIndex(of: id) { s.folded.remove(at: i) } else { s.folded.append(id) }
        }
    }

    /// Trashes what the user asked to (Command-Delete or the menu). Each item is checked against the drawers first.
    private func trash(_ paths: [String]) {
        var failed = 0
        for p in paths where service.trashItem(p) != nil { failed += 1 }
        if failed > 0 {
            bar.result = DrawerResultModel(message: failed == 1 ? "1 item couldn't be moved to the Trash." : "\(failed) items couldn't be moved to the Trash.")
            refresh()
        }
    }

    private func renameItem(_ path: String) {
        let current = DrawerFS.name(of: path)
        guard let name = DrawerPrompt.ask(title: "Rename", message: "A new name for \"\(current)\".", initial: current) else { return }
        if let why = service.renameItem(path, to: name) { DrawerPrompt.tell(why) }
    }

    private func renameDrawer(_ id: String) {
        guard let d = service.settings.drawers.first(where: { $0.id == id }) else { return }
        guard let name = DrawerPrompt.ask(title: "Rename drawer", message: "A new name for the drawer.", initial: d.name) else { return }
        if let why = service.rename(id, to: name) { DrawerPrompt.tell(why) }
    }

    /// Files dropped on a drawer: each goes through the service's hand move, never a direct move.
    private func drop(_ paths: [String], onto id: String) {
        guard let d = service.settings.drawers.first(where: { $0.id == id }) else { return }
        let home = DrawerFS.resolve(d.home)
        var skipped = DrawerRunResult()
        for p in paths {
            // A file already in this drawer stays where it is.
            if let home, let entry = DrawerFS.resolveEntry(p), DrawerPaths.isChild(entry, of: home) { continue }
            let r = service.moveByHand(p, to: id)
            skipped.skipped += r.skipped
            if skipped.refusal == nil { skipped.refusal = r.refusal }
        }
        if DrawerResultModel.worthShowing(skipped) { bar.result = DrawerResultModel(skipped); refresh() }
    }

    // MARK: Tidy

    /// Lines the shown drawers up from the Tidy corner. Undo puts the places back.
    func tidy() {
        guard let screen = DeskController.shared.window?.screen ?? NSScreen.main else { return }
        let visible = screen.visibleFrame
        var current: [String: CGRect] = [:]
        for (id, w) in panels { current[id] = DrawerPlacement.workRect(ofFrame: w.frame, in: visible) }
        let s = service.settings
        let change = DrawerTidyPlan.tidy(current, work: workArea(screen), corner: s.tidyCorner, flow: s.tidyFlow)
        let key = screenKey(screen)
        if !change.isEmpty {
            tidyUndo = change.previous.mapValues { DrawerPlace(screen: key, x: Double($0.x), y: Double($0.y)) }
            service.updateSettings { st in
                for (id, p) in change.next { st.places[id] = DrawerPlace(screen: key, x: Double(p.x), y: Double(p.y)) }
            }
        }
        bar.result = DrawerResultModel(tidied: change.count)
        refresh()
    }

    private func undoTidy() {
        guard let previous = tidyUndo else { return }
        tidyUndo = nil
        service.updateSettings { st in for (id, p) in previous { st.places[id] = p } }
    }

    // MARK: Review and result

    private var barActions: DrawerBarActions {
        DrawerBarActions(apply: { [weak self] in self?.applyReview() },
                         dismiss: { [weak self] in self?.dismissReview() },
                         undo: { [weak self] in self?.undoFromBar() },
                         done: { [weak self] in self?.closeResult() })
    }

    func applyReview() {
        guard let review = service.pending else { return }
        let existing = service.settings.drawers
        let model = DrawerReviewModel(review: review, existing: existing, edit: bar.edit)
        guard model.canApply else { return }
        if !bar.edit.isEmpty {
            service.replacePending(DrawerReviewModel.editedPlan(review, existing: existing, edit: bar.edit, root: service.effectiveRoot))
        }
        let before = Set(existing.map(\.id))
        let result = service.apply()
        bar.resetEdit()
        // The first two new drawers arrive open, the rest folded.
        let created = service.settings.drawers.filter { !before.contains($0.id) }.map(\.id)
        let fold = DrawerArrival.folded(created: created)
        if !fold.isEmpty { service.updateSettings { s in s.folded.append(contentsOf: fold.filter { !s.folded.contains($0) }) } }
        bar.result = result.map { DrawerResultModel($0) }
        refresh()
    }

    func dismissReview() {
        service.dismissReview()
        bar.resetEdit()
        refresh()
    }

    private func undoFromBar() {
        switch bar.result?.undo {
        case .run(let id): bar.result = service.undo(id).map { DrawerResultModel(undone: $0) }
        case .tidy: undoTidy(); bar.result = nil
        default: break
        }
        refresh()
    }

    private func closeResult() {
        bar.result = nil
        refresh()
    }

    // MARK: Bar window

    private func syncBar(on screen: NSScreen) {
        let wanted = service.pending != nil || bar.result != nil
        guard wanted else { barWindow?.orderOut(nil); return }
        let w = barWindow ?? makeBar()
        let size = barHost?.fittingSize ?? CGSize(width: DrawerBarMetrics.width, height: 120)
        let v = screen.visibleFrame
        w.setFrame(NSRect(x: v.midX - size.width / 2, y: v.maxY - size.height - DrawerPlacement.margin,
                          width: size.width, height: size.height), display: true)
        if !w.isVisible { w.orderFront(nil) }
        // The hosted view lays out after this pass, so the height is read once more then.
        DispatchQueue.main.async { [weak self] in self?.fitBar(on: screen) }
    }

    private func fitBar(on screen: NSScreen) {
        guard let w = barWindow, let size = barHost?.fittingSize, size.height > 0, abs(w.frame.height - size.height) > 0.5 else { return }
        let v = screen.visibleFrame
        w.setFrame(NSRect(x: v.midX - size.width / 2, y: v.maxY - size.height - DrawerPlacement.margin,
                          width: size.width, height: size.height), display: true)
    }

    private func makeBar() -> DrawerBarWindow {
        let w = DrawerBarWindow()
        let host = NSHostingView(rootView: DrawerBarView(service: service, bar: bar, actions: barActions))
        w.contentView = host
        barHost = host
        barWindow = w
        return w
    }
}

/// Small question and answer sheets for the drawer menus.
@MainActor
enum DrawerPrompt {
    static func ask(title: String, message: String, initial: String) -> String? {
        let alert = NSAlert()
        alert.messageText = title
        alert.informativeText = message
        let field = NSTextField(frame: NSRect(x: 0, y: 0, width: 260, height: 24))
        field.stringValue = initial
        alert.accessoryView = field
        alert.addButton(withTitle: "Rename")
        alert.addButton(withTitle: "Cancel")
        NSApp.activate(ignoringOtherApps: true)
        guard alert.runModal() == .alertFirstButtonReturn else { return nil }
        let text = field.stringValue.trimmingCharacters(in: .whitespaces)
        return text.isEmpty ? nil : text
    }

    static func tell(_ message: String) {
        let alert = NSAlert()
        alert.messageText = message
        alert.addButton(withTitle: "OK")
        NSApp.activate(ignoringOtherApps: true)
        alert.runModal()
    }
}
