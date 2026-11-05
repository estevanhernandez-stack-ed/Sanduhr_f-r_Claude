import Foundation
import Observation

/// What Use these drawer rules and Use these drawer settings do to the drawers, behind a seam so the window model tests with a
/// real `DrawerService` on temp folders and no panel.
@MainActor
final class ChatDrawerApplier {
    /// What Undo puts back: the look, opacity, the Tidy choices and where every drawer sat.
    struct Snapshot: Equatable {
        var look: DrawerLook
        var opacity: Int
        var corner: DrawerCorner
        var flow: DrawerFlow
        var places: [String: DrawerPlace]
    }

    private let service: DrawerService
    private let tidy: @MainActor () -> Void
    /// Set by Use these drawer settings, cleared by Undo.
    private(set) var before: Snapshot?

    init(service: DrawerService, tidy: @escaping @MainActor () -> Void = { DrawerPanelController.shared.tidy() }) {
        self.service = service
        self.tidy = tidy
    }

    var canUndo: Bool { before != nil }

    /// Replaces the drawer rules with the chat's, turns drawers on if they are off, and opens a rules review (Sort). Nothing
    /// moves: the review waits for Apply. Refused with a line while a drawers review is pending, saving nothing.
    func useRules(_ rules: [DrawerRule]) -> RulesOutcome {
        // A review is waiting: replacing the rules under it would change what Apply moves.
        if service.pending != nil { return .refused(SetupCopy.chatRulesReviewPending) }
        service.updateSettings { $0.rules = rules }
        if !service.settings.enabled { service.turnOn() }
        if service.sort() != nil { return .saved(SetupCopy.chatRulesShown) }
        return .saved(service.desktopPermission == .denied ? DrawersSettingsModel.deniedLine : SetupCopy.chatRulesSavedNothing)
    }

    /// What Use these drawer rules did: saved the rules, then `then` happened; or refused with a line, saving nothing.
    enum RulesOutcome: Equatable {
        case saved(String)
        case refused(String)
    }

    /// Saves the look, the opacity and the named Tidy parts, then Tidy lines the drawers up while drawers are on. Only the parts
    /// the paste named change; Tidy's other choice stays the user's own.
    func useSettings(_ paste: PastedDrawerLook) {
        let s = service.settings
        before = Snapshot(look: s.look, opacity: s.opacity, corner: s.tidyCorner, flow: s.tidyFlow, places: s.places)
        service.updateSettings { st in
            if let look = paste.look { st.look = look }
            if let o = paste.opacity { st.opacity = min(max(o, DrawerSettings.opacityRange.lowerBound), DrawerSettings.opacityRange.upperBound) }
            if let c = paste.corner { st.tidyCorner = c }
            if let f = paste.flow { st.tidyFlow = f }
        }
        if paste.hasArrange, service.settings.enabled { tidy() }
    }

    /// Puts the previous look, opacity, Tidy choices and places back.
    func undo() {
        guard let b = before else { return }
        before = nil
        service.updateSettings { st in
            st.look = b.look
            st.opacity = b.opacity
            st.tidyCorner = b.corner
            st.tidyFlow = b.flow
            st.places = b.places
        }
    }
}

/// Design it with an AI chat (setup spec, section 12): copy a prompt, paste the answer back, see each problem by field, then
/// Preview the Desk part, use the drawer rules or use the drawer settings. A part is usable only when the whole paste is clean
/// and held that part. The paste is parsed here and never logged, stored or sent. Copy names no product.
@MainActor
@Observable
final class ChatDesignViewModel {
    /// What Use these drawer settings said, spoken for the status line.
    static let settingsDone = "Your drawers look different now. Undo puts them back."
    static let settingsArranged = "Your drawers look different and are lined up. Undo puts them back."
    static let undone = "Your drawers are back as they were."
    static let copyBusy = "The clipboard is busy. Try Copy prompt again."
    static let undoTitle = "Undo"

    var pasteText = "" { didSet { if pasteText != oldValue { check(pasteText) } } }
    private(set) var findings: [PastedFinding] = []
    private(set) var summary = ""
    private(set) var rulesLine = ""
    /// What Use these drawer rules did, kept under its button until the paste changes (the status line below is shared).
    private(set) var rulesResult = ""
    /// The rules were saved: the window offers Settings, Drawers.
    private(set) var rulesSaved = false
    private(set) var drawerSettingsLine = ""
    private(set) var status = ""
    private(set) var canPreview = false
    private(set) var canUseRules = false
    private(set) var canUseDrawerSettings = false
    private(set) var canUndo = false

    /// The window should close (the Close button).
    @ObservationIgnored var onClose: () -> Void = {}
    /// The Desk review is up: the window shows Settings, Desk, where the review banner is.
    @ObservationIgnored var onReviewShown: () -> Void = {}
    /// See them in Settings, Drawers.
    @ObservationIgnored var onShowRules: () -> Void = {}

    @ObservationIgnored private let copy: (String) -> Bool
    @ObservationIgnored private let validate: (PackValue) -> DeskValidation
    @ObservationIgnored private let previewDesk: ((DeskValidation) -> DeskAnswer)?
    @ObservationIgnored private let drawers: ChatDrawerApplier
    @ObservationIgnored private var desk: DeskValidation?
    @ObservationIgnored private var rules: [DrawerRule]?
    @ObservationIgnored private var rulesDrawerNames: [String] = []
    @ObservationIgnored private var look: PastedDrawerLook?

    /// - Parameters:
    ///   - copy: Puts text on the pasteboard; false when it could not.
    ///   - validate: The Desk's rules for the desk half, the ones `propose_desk` uses.
    ///   - previewDesk: Holds a checked desk half as a Desk review; nil where there is no Desk.
    init(copy: @escaping (String) -> Bool, validate: @escaping (PackValue) -> DeskValidation,
         previewDesk: ((DeskValidation) -> DeskAnswer)?, drawers: ChatDrawerApplier) {
        self.copy = copy
        self.validate = validate
        self.previewDesk = previewDesk
        self.drawers = drawers
    }

    /// The prompt Copy prompt puts on the pasteboard.
    var promptText: String { SetupPrompt.build() }

    // MARK: Checking the paste

    private func check(_ raw: String) {
        desk = nil; rules = nil; look = nil
        findings = []; summary = ""; rulesLine = ""; rulesResult = ""; rulesSaved = false; drawerSettingsLine = ""; status = ""
        canPreview = false; canUseRules = false; canUseDrawerSettings = false
        if raw.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty { return }

        let read = PastedSetup.read(raw)
        var errors = read.reasons
        var verdict: DeskValidation?
        // The desk half is checked whatever the drawers half held, so every problem shows at once.
        if let node = read.desk {
            let v = validate(node)
            verdict = v
            errors += v.reasons
        }
        findings = PastedSetup.findings(errors: errors, warnings: [])
        if !errors.isEmpty { summary = SetupCopy.chatFixCount(findings.filter(\.isError).count); return }

        if let v = verdict, v.design != nil { desk = v; canPreview = previewDesk != nil }
        if let r = read.rules, !r.isEmpty {
            rules = r
            canUseRules = true
            rulesDrawerNames = uniqueNames(read.ruleDrawerNames)
            rulesLine = SetupCopy.chatRulesLine(rules: r.count, drawerNames: rulesDrawerNames)
        }
        if let l = read.drawerLook, l.any {
            look = l
            canUseDrawerSettings = true
            drawerSettingsLine = Self.describe(l)
        }
        summary = read.blocks > 1 ? SetupCopy.chatManyBlocks(read.blocks) : SetupCopy.chatReady
    }

    private func uniqueNames(_ names: [String]) -> [String] {
        var seen = Set<String>()
        return names.filter { seen.insert($0.lowercased()).inserted }
    }

    /// What the drawer settings would do, naming only the parts the paste gave.
    static func describe(_ l: PastedDrawerLook) -> String {
        SetupCopy.chatDrawerSettingsLine(look: l.look?.rawValue, opacity: l.opacity,
                                         corner: l.corner.map(cornerName), flow: l.flow?.rawValue)
    }

    static func cornerName(_ c: DrawerCorner) -> String {
        switch c {
        case .topLeft: return "top left"
        case .topRight: return "top right"
        case .bottomLeft: return "bottom left"
        case .bottomRight: return "bottom right"
        }
    }

    // MARK: Buttons

    func copyPrompt() { status = copy(promptText) ? SetupCopy.chatPromptCopied : Self.copyBusy }

    func preview() {
        guard let v = desk else { return }
        guard let previewDesk else { status = SetupCopy.chatNoDeskService; return }
        let answer = previewDesk(v)
        if answer.status == DeskAnswer.previewing {
            status = SetupCopy.chatDeskShown
            onReviewShown()
        } else {
            let first = PastedSetup.findings(errors: answer.reasons ?? [], warnings: []).first
            status = SetupCopy.chatNotShown(first.map { "\($0.field) \($0.text)" } ?? "it could not be shown")
        }
    }

    func useRules() {
        guard let r = rules else { return }
        switch drawers.useRules(r) {
        case .refused(let line):
            rulesResult = line
            rulesSaved = false
        case .saved(let then):
            rulesResult = SetupCopy.chatRulesSaved(rules: r.count, drawerNames: rulesDrawerNames, then: then)
            rulesSaved = true
        }
    }

    func showRules() { onShowRules() }

    func useDrawerSettings() {
        guard let l = look else { return }
        drawers.useSettings(l)
        canUndo = drawers.canUndo
        status = l.hasArrange ? Self.settingsArranged : Self.settingsDone
    }

    func undoDrawerSettings() {
        drawers.undo()
        canUndo = drawers.canUndo
        status = Self.undone
    }

    func close() { onClose() }
}
