import AppKit

// ModsPageModel's By Seat and Sanduhr's-own-mod sections, split out of ModsSettings.swift on
// 2026-10-19. Their stored properties stay in ModsPageModel.swift: an extension can't hold them.

extension ModsPageModel {
    // MARK: By Seat (item 71, slice 1b)

    /// The seats By Seat offers: in demo mode, no work seat.
    func pickerSeats(demo: Bool) -> [Seat] { seats.filter { !(demo && $0.isWork) } }

    /// The selected seat, else the first one offered.
    func shownSeat(demo: Bool) -> Seat? {
        let offered = pickerSeats(demo: demo)
        return offered.first { $0.path == selectedSeat } ?? offered.first
    }

    func report(for seat: String) -> SeatReport? { reports.first { $0.seat == seat } }

    /// Drift against the seats demo mode still shows (no work seat in demo mode).
    func shownDrift(_ report: SeatReport, demo: Bool) -> [SeatReport.Drift] {
        report.drift.filter { !(demo && isWork($0.other)) }
    }

    /// The report as By Seat summarizes it in demo mode: drift against shown seats only.
    func shownReport(_ report: SeatReport, demo: Bool) -> SeatReport {
        var r = report
        r.drift = shownDrift(report, demo: demo)
        return r
    }

    func label(of path: String) -> String { seats.first { $0.path == path }?.label ?? (path as NSString).lastPathComponent }

    func mark(of path: String) -> SeatMark { seats.first { $0.path == path }?.mark ?? .unmarked }

    /// The account linked to a seat, by its label (Sanduhr's own name for it).
    func linkedAccount(_ path: String) -> String? { AccountData.account(linkedTo: path, in: store) }

    /// A seat's counts with its running dot, for state.yaml.
    func counts(_ seat: String) -> SeatCounts {
        var c = report(for: seat)?.counts ?? SeatCounts()
        c.running = running.contains(seat)
        return c
    }

    /// The version in a native install's path (`…/versions/2.1.296`), nil otherwise.
    nonisolated static func claudeVersion(_ path: String?) -> String? {
        guard let path else { return nil }
        let last = URL(fileURLWithPath: path).resolvingSymlinksInPath().lastPathComponent
        return last.range(of: #"^\d+\.\d+\.\d+"#, options: .regularExpression) != nil ? last : nil
    }

    /// A tab, and on By Seat the nth seat (from 1), as the smoke hook asks for it.
    func showTab(_ tab: ModsTab, seat: Int?) {
        self.tab = tab
        pendingSeat = seat
        applyPendingSeat()
    }

    func applyPendingSeat() {
        guard let n = pendingSeat, !seats.isEmpty else { return }
        pendingSeat = nil
        let seat = seats[min(n, seats.count) - 1]
        selectedSeat = seat.path
        focus = ModsFocus(seat: seat.path, card: "", name: "")
    }

    /// The scroll id of a seat's place on By Seat.
    nonisolated static func seatAnchor(_ path: String) -> String { "seat:" + path }

    func isWork(_ path: String) -> Bool { seats.first { $0.path == path }?.isWork ?? false }

    /// The label and mark a person gives a seat (Sanduhr preferences, never written into it).
    func setLabel(_ label: String, for seat: String) {
        SeatPreferences.setLabel(label, for: seat, in: store)
        refreshSeats()
    }

    func setMark(_ mark: SeatMark, for seat: String) {
        SeatPreferences.setMark(mark, for: seat, in: store)
        refreshSeats()
    }

    /// Add Folder… (item 71, slice 0): a Claude Code folder discovery doesn't find (one outside
    /// the home folder, or named otherwise), remembered as a Sanduhr preference. Nothing is
    /// written into the folder.
    func addFolder(linked: [String]) async {
        let panel = NSOpenPanel()
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.allowsMultipleSelection = false
        panel.showsHiddenFiles = true
        panel.directoryURL = URL(fileURLWithPath: home)
        panel.prompt = "Add"
        panel.message = "Choose a Claude Code folder (one with a projects folder or a .claude.json)."
        guard panel.runModal() == .OK, let url = panel.url else { return }
        guard ClaudeCodeFolders.looksLikeHome(url.path, home: home) else {
            addNote = "That folder doesn't look like a Claude Code folder: it has no projects folder and no .claude.json."
            return
        }
        addNote = nil
        SeatPreferences.add(url.path, in: store)
        await load(linked: linked)
    }

    /// Runs `claude plugin validate --json` on the item's folder (never `plugin test`).
    func check(_ item: ModItem) async {
        guard let path = item.path, !checking.contains(item.id) else { return }
        checking.insert(item.id)
        defer { checking.remove(item.id) }
        let claude = self.claude
        let outcome = await Task.detached(priority: .userInitiated) { ModCheck.run(claude: claude, dir: path) }.value
        checks[item.id] = outcome
    }

    func display(_ path: String) -> String { ClaudeCodeFolders.Folder(path: path).display(home: home) }

    // MARK: Sanduhr's own mod (slice 2)

    /// Off, or On in a folder whose list has the entry. On in a folder without one goes through
    /// the consent sheet and `install` (MetersModRow).
    func setOwn(on: Bool, folder: String, linked: [String]) async {
        await act(folder, linked: linked) { installer, home in
            try installer.switchOwnMod(on: on, folder: folder)
            let now = installer.ownModState(folder: folder)
            let found = ModOverrides.find(key: IntegrationInstaller.ownModKey, folder: folder, home: home)
            if let text = ModOverrides.message(found, on: now.isOn, home: home) { return OwnModNote(text: text, kind: .warning) }
            return OwnModNote(text: ModsPageModel.takesEffect, kind: .info)
        }
    }

    func update(folder: String, confirmed: Bool, linked: [String]) async {
        let claude = self.claude
        let note = await act(folder, linked: linked) { installer, _ in
            let outcome = try installer.updateOwnMod(folder: folder, confirmed: confirmed) { old, new in
                ModCapabilities.added(old: old, new: new) { dir in
                    if case .report(let r) = ModCheck.run(claude: claude, dir: dir), r.passed { return r }
                    return nil
                }
            }
            switch outcome {
            case .upToDate: return OwnModNote(text: "Already the version this copy of Sanduhr carries.", kind: .info)
            case .updated: return OwnModNote(text: "Updated. " + ModsPageModel.takesEffect, kind: .info)
            case .needsConsent(let added): return OwnModNote(text: "", kind: .info, added: added)
            }
        }
        if let added = note?.added { updateQuestion = OwnModUpdateQuestion(folder: folder, added: added) }
    }

    /// The Studio's Layout Save (item 75, slice D): the options into each folder's settings.json,
    /// by receipt. Each folder's note says what happened; options set in Claude Code stay.
    func saveOptions(_ options: MetersOptions, folders: [String], linked: [String]) async {
        for folder in folders {
            await act(folder, linked: linked) { installer, _ in
                switch try installer.setOwnModOptions(options, folder: folder) {
                case .written: return OwnModNote(text: "Layout saved. " + ModsPageModel.optionsTakeEffect, kind: .info)
                case .removed: return OwnModNote(text: "Back to the mod's defaults: Sanduhr's options came out of settings.json. " + ModsPageModel.optionsTakeEffect, kind: .info)
                case .unchanged: return nil
                case .keptTheirs: return OwnModNote(text: ModsPageModel.optionsKept, kind: .warning)
                }
            }
        }
    }

    nonisolated static let optionsTakeEffect = "Sessions started from now on use it; a session already running keeps the layout it started with."
    nonisolated static let optionsKept = "This folder's settings.json already has options for the mod that Sanduhr didn't write (Claude Code's /config writes them there), so Sanduhr left them as they are."

    func remove(folder: String, linked: [String]) async {
        await act(folder, linked: linked) { installer, _ in
            try installer.remove(.meters, folder: folder)
            return OwnModNote(text: "Removed: this folder's settings.json is back to what it was before Sanduhr's mod. " + ModsPageModel.takesEffect, kind: .info)
        }
    }

    /// Installs the mod's entry after the consent sheet, or (`updating`) moves an older entry to
    /// this Sanduhr's scripts. The mod runs inside Claude Code, so no python3 is needed.
    func install(folder: String, updating: Bool, linked: [String]) async {
        await act(folder, linked: linked) { installer, _ in
            try installer.install(.meters, folder: folder, python: "")
            return OwnModNote(text: (updating ? "Updated. " : "Installed. ") + ModsPageModel.takesEffect, kind: .info)
        }
    }

    /// Runs one write off the main thread, then reads the page again. A note carrying `added`
    /// is a question, not a note.
    @discardableResult
    private func act(_ folder: String, linked: [String],
                     _ body: @escaping @Sendable (IntegrationInstaller, String) throws -> OwnModNote?) async -> OwnModNote? {
        guard !ownBusy.contains(folder) else { return nil }
        ownBusy.insert(folder)
        let installer = self.installer, home = self.home
        let note = await Task.detached(priority: .userInitiated) { () -> OwnModNote? in
            do { return try body(installer, home) } catch { return OwnModNote(failure: error) }
        }.value
        ownNotes[folder] = note?.added == nil ? note : nil
        ownBusy.remove(folder)
        await load(linked: linked)
        return note
    }

    /// Forget Folder: Sanduhr stops listing a folder it was given with Add Folder…. The folder
    /// itself is untouched; one discovery finds stays listed.
    func forgetFolder(_ path: String, linked: [String]) async {
        SeatPreferences.forget(path, in: store)
        await load(linked: linked)
    }

    nonisolated static func receiptsSkippedText(_ n: Int) -> String {
        let what = n == 1 ? "One of Sanduhr's install records" : "\(n) of Sanduhr's install records"
        return "\(what) couldn't be read and \(n == 1 ? "was" : "were") set aside, kept as written. What \(n == 1 ? "it" : "they") recorded can't be undone from here; every other record still can."
    }

    /// A seat's settings.json that doesn't parse: unreadable, never empty.
    nonisolated static func unreadableText(_ why: String) -> String {
        "settings.json is unreadable: it \(why). Claude Code can't read it either (comments and trailing commas aren't allowed), so what it holds isn't listed here."
    }

    nonisolated static let takesEffect = "Takes effect in new Claude Code sessions (or after /reload-plugins)."

    static let cliMissing = "Check needs Claude Code's command line (claude), and it wasn't found on your PATH or in the usual install folders (~/.local/bin, /opt/homebrew/bin, /usr/local/bin)."
}
