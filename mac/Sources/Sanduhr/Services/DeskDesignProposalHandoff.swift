import AppKit
import Foundation
import Observation

/// The words of the Desk design review (Windows' `DeskReviewCopy`, with the Mac's own places).
enum DeskDesignReviewCopy {
    static let heading = "Claude suggests a Desk design"
    static let changesTitle = "What changed"
    static let fontsTitle = "Fonts to install"
    static let nothingChanges = "This design leaves your Desk as it is."
    static let untitled = "A new look for your Desk"
    static let previewOnDesk = "Preview on the Desk"
    static let endPreview = "End preview"
    static let apply = "Apply"
    static let dismiss = "Dismiss"
    /// Shown instead of the preview's effect when the Desk is off (the preview needs it).
    static let deskOff = "Turn the Desk on to preview it."
    static let applyWaiting = "Apply waits until the font is installed."

    static func fontLine(family: String, link: String?) -> String {
        link.map { "\(family) is not installed. Get it from \($0), then install it." }
            ?? "\(family) is not installed. Install it to apply this design."
    }
}

/// A font a previewed design names that this Mac lacks, with the link to get it when the design gave a safe one.
struct DeskReviewFont: Equatable {
    let family: String
    let link: String?
    var line: String { DeskDesignReviewCopy.fontLine(family: family, link: link) }
}

/// What the banner shows for the design under review.
struct DeskDesignReviewModel: Equatable {
    let id: String
    let heading: String
    let name: String
    let note: String?
    let changesTitle: String
    let changes: [String]
    let missingFonts: [DeskReviewFont]
    let deskOff: Bool
    let previewing: Bool

    /// Apply is available once every font the design names is installed.
    var canApply: Bool { missingFonts.isEmpty }

    /// Why Preview on the Desk can't be pressed, or nil when it can (or is showing, and End preview takes its place).
    var previewHint: String? { deskOff && !previewing ? DeskDesignReviewCopy.deskOff : nil }

    /// A link the banner may show: absolute https only, no user info (the design's validator already held this).
    static func safeLink(_ link: String?) -> String? {
        guard let link, link.utf16.count <= 300, DeskDesign.linkProblem(link) == nil else { return nil }
        return link
    }
}

/// What a Desk preview draws. A nil field is left as the saved setting.
struct DeskDesignPreviewState: Equatable {
    var layout: DeskArrangement?
    var font: String?
    /// The Desk ink spec (`"ff2a6d,d16ba5"`), as `inkColor` stores it.
    var ink: String?
    var plate: Bool?
    var shadow: Bool?
    var weatherNickname: String?
}

/// The Desk side of a review, behind a seam so the review's tests need no Desk window.
@MainActor
protocol DeskDesignPreviewHost: AnyObject {
    /// The Desk is on, so a preview would be seen.
    var deskRunning: Bool { get }
    /// The Desk is taken (its layout is being arranged), so a design waits: Windows' `DeskBusy`.
    var deskBusy: Bool { get }
    /// Shows `preview` on the Desk, replacing a preview already up.
    func show(_ preview: DeskDesignPreviewState)
    /// Puts the Desk back as it was.
    func end()
}

/// Claude's proposed Desk designs, the app's half of `propose_desk` (spec 8); a port of Windows' `DeskDesignService`
/// review path, shaped like 2.36.0's `WidgetProposalHandoff`. The MCP server drops `desk-request.json` into Sanduhr's
/// Application Support folder; this takes it through the shared `HandoffWatch`, checks the design again
/// (`DeskDesignHandoff.decode`: the app never trusts the server), and holds a good one in memory only. Every request is
/// answered in `desk-result.json` (atomic, owner only). The request file is removed as soon as it is read, and one older
/// than ten minutes is dropped unanswered.
///
/// - A good design is held and answered `previewing`, with the lines of what it changes and any font this Mac lacks
///   (`fonts`: Windows answers a missing font the same way, and `needs_font` is the MCP server's own word for it before
///   the app is asked). The banner on Settings, Desk offers **Preview on the Desk**, **Apply** and **Dismiss**. Nothing is
///   drawn on the Desk until the user presses Preview.
/// - **Preview on the Desk** draws the design from memory: the layout, font, plate and shadow through
///   `DeskDesignPreview`, the ink through `DeskInkPreview` (so the notch text and the message follow). No saved setting is
///   written. Dismiss, Apply and a newer request take it down.
/// - **Apply** writes the design into the saved Desk settings (`DeskDesignApply.write`) and answers `applied`; it waits
///   while a font the design names is missing. **Dismiss** answers `rejected`, `dismissed`.
/// - A newer request answers the older one `rejected`, `replaced`. `busy` while Arrange Desk is active; `not_ready` with
///   no Desk wired or after Reset Sanduhr… ran.
/// - No setting lets Claude skip Apply for a whole design: the direct switches (messages, themes, song looks) are never
///   read here. Nothing here logs a design's name or content.
@MainActor
@Observable
final class DeskDesignProposalHandoff {
    static let shared = DeskDesignProposalHandoff()

    struct Paths {
        /// ~/Library/Application Support/Sanduhr: the request and result files, as `Paths.support_dir` in the MCP.
        var support: URL

        static var standard: Paths { Paths(support: HandoffFiles.support) }

        var request: URL { support.appendingPathComponent(DeskDesignHandoff.requestFile) }
        var result: URL { support.appendingPathComponent(DeskDesignHandoff.resultFile) }
    }

    /// A design waiting for the user: the request id, the design Sanduhr checked, the fonts it found missing.
    struct Review {
        let id: String
        let design: DeskDesign
        let fonts: [MissingFont]
    }

    /// The design waiting for the user, if any. In memory only: quitting Sanduhr drops it.
    private(set) var pending: Review?
    /// The pending design is on the Desk now (Preview on the Desk).
    private(set) var previewing = false
    /// One sentence when Preview on the Desk could not show it, else nil.
    private(set) var previewNote: String?
    /// Counts font changes, so the banner re-reads which fonts are still missing.
    private(set) var fontTick = 0

    @ObservationIgnored let paths: Paths
    @ObservationIgnored private let defaults: DefaultsStore
    @ObservationIgnored private let now: () -> Date
    @ObservationIgnored private let installedFamilies: @MainActor () -> [String]
    /// The Desk side of a review; nil answers a design `not_ready` (tests, a build with no Desk).
    @ObservationIgnored var host: DeskDesignPreviewHost?
    /// A design arrived and waits (the notification is the caller's).
    @ObservationIgnored var onSuggestion: (@MainActor (DeskDesignReviewModel) -> Void)?
    /// The design was applied, dismissed or replaced (its notification can go).
    @ObservationIgnored var onDecided: (@MainActor (String) -> Void)?
    /// Every answer written (a test seam: the result file holds only the last).
    @ObservationIgnored var onAnswered: ((String, DeskAnswer) -> Void)?
    @ObservationIgnored private var watching = false

    /// - Parameters:
    ///   - defaults: The Desk defaults suite an Apply writes (the app's, or an isolated one in tests).
    ///   - installedFamilies: The font families this Mac has (the picker's list); tests inject their own.
    init(paths: Paths = .standard, defaults: DefaultsStore = UserDefaults.desk, now: @escaping () -> Date = Date.init,
         installedFamilies: (@MainActor () -> [String])? = nil) {
        self.paths = paths
        self.defaults = defaults
        self.now = now
        self.installedFamilies = installedFamilies ?? {
            DeskFont.pickerFamilies(installed: NSFontManager.shared.availableFontFamilies)
        }
    }

    // MARK: - The file half

    /// Watches Sanduhr's folder (the shared HandoffWatch) and takes a request already waiting.
    func start(watch: HandoffWatch? = nil) {
        guard !watching else { return }
        watching = true
        (watch ?? .shared).add { [weak self] in self?.check() }
        check()
    }

    /// Reads and removes a waiting request, if there is one.
    func check() {
        guard let data = HandoffFiles.take(paths.request, maxBytes: DeskDesignHandoff.maxRequestBytes) else { return }
        receive(data)
    }

    /// One request's bytes, as read from the file.
    func receive(_ data: Data) {
        let request = DeskDesignHandoff.decode(data, now: now(), context: DeskDesignContext(installedFonts: installedFamilies()))
        switch request.kind {
        case .discard:
            return
        case .refused:
            answer(request.id!, .refused(request.reasons))
        case .proposal:
            answer(request.id!, take(id: request.id!, validation: request.validation!))
        }
    }

    private func answer(_ id: String, _ answer: DeskAnswer) {
        onAnswered?(id, answer)
        HandoffFiles.writeOwnerOnly(DeskDesignHandoff.resultJson(id: id, answer: answer, now: now()), to: paths.result)
    }

    // MARK: - The review

    /// A font the design names that this Mac has not: neither bundled, system, nor installed.
    func fontInstalled(_ family: String) -> Bool {
        if family.isEmpty || BundledFonts.bundled(family) != nil { return true }
        return installedFamilies().contains { $0.lowercased() == family.lowercased() }
    }

    private func missing(_ r: Review) -> [MissingFont] { r.fonts.filter { !fontInstalled($0.family) } }

    /// A design that passed the rules: held for the banner, never written here. The direct switches are deliberately not
    /// read: a whole design always waits for Apply.
    private func take(id: String, validation: DeskValidation) -> DeskAnswer {
        guard !ResetGate.isOn, let host, let design = validation.design else { return .notReady }
        if host.deskBusy {
            return DeskAnswer(DeskAnswer.rejected, reason: "busy",
                reasons: [DeskReason("design", "the Desk is busy (its layout is being arranged); ask the user to finish arranging, then propose the design again")])
        }
        replacePending()
        let fonts = validation.fonts.filter { !fontInstalled($0.family) }
        let review = Review(id: id, design: design, fonts: fonts)
        pending = review
        previewNote = nil
        let changes = changeLines(for: review)
        if let model { onSuggestion?(model) }
        return DeskAnswer(DeskAnswer.previewing, fonts: fonts.isEmpty ? nil : fonts, changes: changes.isEmpty ? nil : changes)
    }

    /// A newer design takes the place of the one waiting: the older one is answered `replaced`.
    private func replacePending() {
        guard let old = pending else { return }
        pending = nil
        endPreview()
        onDecided?(old.id)
        answer(old.id, DeskAnswer(DeskAnswer.rejected, reason: "replaced",
                                  reasons: [DeskReason("design", "replaced by a newer proposal")]))
    }

    private func changeLines(for r: Review) -> [String] {
        let current = DeskDesignLook.read(defaults)
        return DeskDesignApply.changes(from: current, to: DeskDesignApply.merged(current, r.design),
                                       available: { [self] in fontInstalled($0) })
    }

    /// What the banner shows for the pending design, nil with none.
    var model: DeskDesignReviewModel? {
        guard let p = pending else { return nil }
        _ = fontTick
        var lines = changeLines(for: p)
        if lines.isEmpty { lines = [DeskDesignReviewCopy.nothingChanges] }
        let name = p.design.name ?? DeskDesignReviewCopy.untitled
        return DeskDesignReviewModel(
            id: p.id, heading: DeskDesignReviewCopy.heading, name: name, note: p.design.note,
            changesTitle: DeskDesignReviewCopy.changesTitle, changes: lines,
            missingFonts: missing(p).map { DeskReviewFont(family: $0.family, link: DeskDesignReviewModel.safeLink($0.link)) },
            deskOff: !(host?.deskRunning ?? false), previewing: previewing)
    }

    /// The state a preview draws: only the fields the design sets. A font this Mac lacks previews in the nearest look
    /// it has; the design keeps the family it asked for.
    private func previewState(_ p: Review) -> DeskDesignPreviewState {
        let current = DeskDesignLook.read(defaults)
        let next = DeskDesignApply.merged(current, p.design)
        var state = DeskDesignPreviewState()
        if p.design.layout != nil { state.layout = next.arrangement }
        if let asked = p.design.font {
            state.font = DeskDesignApply.previewFont(DeskDesignApply.fontSetting(asked)) { [self] in fontInstalled($0) }
        }
        if p.design.ink != nil { state.ink = next.ink }
        if p.design.plate != nil { state.plate = next.plate }
        if p.design.shadow != nil { state.shadow = next.shadow }
        if p.design.weatherNickname != nil { state.weatherNickname = next.weatherNickname }
        return state
    }

    /// Preview on the Desk. False (and `previewNote` says why) when the Desk is off. A preview already up is replaced.
    @discardableResult
    func previewOnDesk() -> Bool {
        guard let p = pending, let host else { return false }
        guard host.deskRunning else { previewNote = DeskDesignReviewCopy.deskOff; return false }
        host.show(previewState(p))
        previewing = true
        previewNote = nil
        return true
    }

    /// Takes the preview down: the Desk is as it was. Nothing is answered (the design still waits).
    func endPreview() {
        guard previewing else { return }
        previewing = false
        host?.end()
    }

    /// The Mac's font set changed: the banner re-reads the missing fonts, and a preview up draws with the new font.
    func fontsChanged() {
        fontTick &+= 1
        if previewing { previewOnDesk() }
    }

    /// Dismiss: nothing is written, the preview goes, and the caller is told.
    func dismiss() {
        guard let p = pending else { return }
        pending = nil
        endPreview()
        previewNote = nil
        onDecided?(p.id)
        answer(p.id, DeskAnswer(DeskAnswer.rejected, reason: "dismissed",
                                reasons: [DeskReason("design", "dismissed by the user")]))
    }

    /// Apply: writes the design into the saved Desk settings, takes the preview down and tells the caller. Waits while a
    /// font the design names is missing.
    func apply() {
        guard let p = pending, missing(p).isEmpty else { return }
        pending = nil
        previewNote = nil
        let current = DeskDesignLook.read(defaults)
        let next = DeskDesignApply.merged(current, p.design)
        let changes = DeskDesignApply.changes(from: current, to: next, available: { [self] in fontInstalled($0) })
        DeskDesignApply.recordBefore(next, over: current, name: p.design.name, now: now(), in: defaults)
        DeskDesignApply.write(next, over: current, to: defaults)
        endPreview()   // the preview gives way to what was written
        onDecided?(p.id)
        answer(p.id, DeskAnswer(DeskAnswer.applied, changes: changes.isEmpty ? nil : changes))
    }
}

/// The running app's side: the layout, font, plate and shadow go to `DeskDesignPreview`, the ink to `DeskInkPreview`
/// (the notch text and the message follow it, as in 2.36.0). The previews are the app's shared ones; tests pass their own.
@MainActor
final class DeskDesignDeskHost: DeskDesignPreviewHost {
    private let controllerOverride: DeskController?
    private let preview: DeskDesignPreview
    private let ink: DeskInkPreview

    init(controller: DeskController? = nil, preview: DeskDesignPreview? = nil, ink: DeskInkPreview? = nil) {
        self.controllerOverride = controller
        self.preview = preview ?? .shared
        self.ink = ink ?? .shared
    }

    private var controller: DeskController { controllerOverride ?? .shared }

    var deskRunning: Bool { controller.running }
    var deskBusy: Bool { controller.model.arrange.active }

    func show(_ state: DeskDesignPreviewState) {
        // One preview at a time: a widget proposal's palette uses the same ink.
        WidgetProposalHandoff.shared.endPreview()
        preview.layout = state.layout
        preview.font = state.font
        preview.plate = state.plate
        preview.shadow = state.shadow
        preview.weatherNickname = state.weatherNickname
        ink.spec = state.ink
    }

    func end() {
        preview.clear()
        ink.spec = nil
    }
}

extension DeskDesignProposalHandoff {
    /// The app's wiring: the Desk preview host, the suggestion notification, and the font watch.
    func startForApp() {
        host = DeskDesignDeskHost()
        onSuggestion = { Notifier.shared.suggestDeskDesign($0) }
        onDecided = { Notifier.shared.clearDeskDesignSuggestion(id: $0) }
        NotificationCenter.default.addObserver(forName: NSFont.fontSetChangedNotification, object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.fontsChanged() }
        }
        start()
    }
}
