import SwiftUI

/// The "other statusline" choice (items 63, 63b): Combine keeps the user's line and adds
/// Sanduhr's, with the segments of both picked as chips and a live preview of the final line;
/// Replace swaps theirs out until Remove. The folder's mods that draw status entries are listed
/// too, read-only: Claude Code draws those beside the statusline, not the command.
struct CombineChoice: View {
    let other: String
    let folder: String
    let folderPath: String
    var model: IntegrationsModel
    @Binding var join: StatuslineJoin
    @Binding var selection: StatuslineSelection
    @State private var chips: StatuslineChips?
    @State private var mods: [ModStatusEntry]?
    @State private var loading = true
    @State private var live: LiveTest?

    /// The last Test with live data: when, what it used.
    struct LiveTest: Equatable {
        let at: Date
        let input: StatuslineLiveInput.Result
        let ran: Bool
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("\(folder) already has a statusline:")
                .fixedSize(horizontal: false, vertical: true)
            OtherCommandBox(text: other)
            Text("Combine keeps it: Sanduhr runs your statusline first, then adds its meters. If yours is slow (over 1.5 seconds) or fails, Sanduhr's meters still show. Replace shows only Sanduhr's line. Either way, Remove puts yours back.")
                .fixedSize(horizontal: false, vertical: true)
            picker
            JoinRowPicker(join: $join)
            ModChips(mods: mods)
            CombinePreview(theirs: chips?.inspection.theirs, join: join,
                           selection: chips?.previewSelection ?? selection, loading: loading,
                           input: live?.input.data, mods: mods ?? [], model: model)
            LiveTestRow(live: live, busy: loading, enabled: chips != nil, test: testLive)
        }
        .task(id: other) {
            loading = true
            chips = await model.inspectStatusline(chain: other).map(StatuslineChips.init)
            selection = chips?.selection ?? StatuslineSelection()
            mods = await model.modStatusEntries(folder: folderPath)
            loading = false
        }
    }

    @ViewBuilder
    private var picker: some View {
        if let chips {
            SegmentChips(chips: chips, change: change)
        } else {
            Text(loading ? "Running your statusline once…" : "Your statusline's segments can't be shown: Python or the scripts weren't found. Combine keeps all of it.")
                .font(.caption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    private func change(_ body: (inout StatuslineChips) -> Void) {
        guard var c = chips else { return }
        body(&c)
        chips = c
        selection = c.selection
    }

    /// Runs their statusline again with live numbers and refills the chips, keeping the picks.
    private func testLive() {
        Task {
            loading = true
            let input = await model.liveStatuslineInput(folder: folderPath)
            let next = await model.inspectStatusline(chain: other, input: input.data)
            if let next, let c = chips { chips = c.refilled(next) }
            if let c = chips { selection = c.selection }
            live = LiveTest(at: Date(), input: input, ran: next != nil)
            loading = false
        }
    }
}

/// A caption under a control.
private struct Caption: View {
    let text: String
    init(_ text: String) { self.text = text }

    var body: some View {
        Text(text)
            .font(.caption)
            .foregroundStyle(.secondary)
            .fixedSize(horizontal: false, vertical: true)
    }
}

/// Their lines, the join glyph, the keep-new switch and Sanduhr's segments as chips.
private struct SegmentChips: View {
    let chips: StatuslineChips
    let change: ((inout StatuslineChips) -> Void) -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Caption("Click a segment to keep or drop it. Yours are matched by how they start, so one that comes and goes (a git branch outside a repository) doesn't move the others.")
            ForEach(chips.lineIndices, id: \.self) { line in
                LineChips(chips: chips, line: line, change: change)
            }
            JoinWithPicker(chips: chips, change: change)
            VStack(alignment: .leading, spacing: 2) {
                Toggle("Keep segments yours shows later", isOn: Binding(
                    get: { chips.keepNew },
                    set: { on in change { $0.keepNew = on } }))
                    .help("Keep segments yours shows later: what happens to a segment Sanduhr hasn't seen yet.")
                Caption("When your statusline shows a segment that isn't here now, on keeps it and off leaves it out.")
            }
            VStack(alignment: .leading, spacing: 4) {
                Text("Sanduhr's").font(.caption.weight(.medium))
                ChipFlow {
                    ForEach(chips.mineChips) { chip in
                        ChipButton(chip: chip, help: Self.help(chip)) { change { $0.toggle(chip) } }
                    }
                }
            }
        }
    }

    /// What one of Sanduhr's segments prints.
    static func help(_ chip: StatuslineChips.Chip) -> String {
        let now = chip.label
        switch SanduhrSegment(rawValue: chip.key) {
        case .session: return "Session: the 5-hour limit, as in \(now)."
        case .weekly: return "Weekly: the 7-day limit, as in \(now), and a model's own weekly limit when it nears full."
        case .resets: return "Weekly reset: when the 7-day limit resets, as in \(now)."
        case .context: return "Context: how full the session's context window is, as in \(now), from Claude Code."
        case .model: return "Model: the session's model, as in \(now), from Claude Code."
        case nil: return now
        }
    }
}

/// One line of theirs: how Sanduhr splits it (detected, changeable) and its segments.
private struct LineChips: View {
    let chips: StatuslineChips
    let line: Int
    let change: ((inout StatuslineChips) -> Void) -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack(spacing: 6) {
                Text(chips.lineIndices.count > 1 ? "Split your line \(line + 1) on" : "Split yours on")
                    .font(.caption.weight(.medium))
                separatorMenu
            }
            Caption("How Sanduhr reads your line into segments: the separator it found, or one you choose. It changes the chips; your line reads the same until you drop a segment or choose Join with.")
            ChipFlow {
                ForEach(chips.chips(line: line)) { chip in
                    ChipButton(chip: chip, help: "Click to keep or drop it. Matched by how it starts (\u{201C}\(chip.key)\u{201D}), so it's found wherever it appears.") {
                        change { $0.toggle(chip) }
                    }
                }
            }
            if chips.isWhole(line: line) {
                Caption("Sanduhr keeps this line whole: split this way it isn't clear where its segments are.")
            }
        }
    }

    private var separatorMenu: some View {
        Picker("Split yours on", selection: Binding(
            get: { chips.separator(line: line) },
            set: { sep in change { $0.setSeparator(sep, line: line) } })) {
            ForEach(StatuslineSeparator.allCases, id: \.self) { sep in
                Text(sep == chips.detected(line: line) ? "\(sep.title) (found)" : sep.title).tag(sep)
            }
        }
        .labelsHidden()
        .pickerStyle(.menu)
        .fixedSize()
        .disabled(!chips.separatorChangeable(line: line))
        .help("Split yours on: the separator Sanduhr cuts your line at to find its segments.")
    }
}

/// The glyph between segments in the final line.
private struct JoinWithPicker: View {
    let chips: StatuslineChips
    let change: ((inout StatuslineChips) -> Void) -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            HStack(spacing: 6) {
                Text("Join with").font(.caption.weight(.medium))
                Picker("Join with", selection: Binding(
                    get: { chips.joinWith },
                    set: { glyph in change { $0.joinWith = glyph } })) {
                    Text("Same as yours").tag(StatuslineJoinGlyph?.none)
                    ForEach(StatuslineJoinGlyph.allCases, id: \.self) { g in
                        Text(g.title).tag(StatuslineJoinGlyph?.some(g))
                    }
                }
                .labelsHidden()
                .pickerStyle(.menu)
                .fixedSize()
                .help("Join with: what goes between the segments that remain, in your part and Sanduhr's.")
            }
            Caption("The glyph between segments in the final line. Same as yours keeps your own separators.")
        }
    }
}

/// Own row or same row for Sanduhr's segments.
private struct JoinRowPicker: View {
    @Binding var join: StatuslineJoin

    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            Picker("Sanduhr's meters", selection: $join) {
                Text("On their own row").tag(StatuslineJoin.line)
                Text("On the same row").tag(StatuslineJoin.same)
            }
            .pickerStyle(.segmented)
            .help("Sanduhr's meters: where Sanduhr's segments go next to yours.")
            Caption("Own row: your line, then Sanduhr's under it. Same row: Sanduhr's after yours, moving to its own row when the terminal is too narrow.")
        }
    }
}

/// The folder's mods that draw status entries, read-only.
private struct ModChips: View {
    let mods: [ModStatusEntry]?

    var body: some View {
        if let mods {
            VStack(alignment: .leading, spacing: 4) {
                Text("From your mods").font(.caption.weight(.medium))
                if mods.isEmpty {
                    Caption("No mods in this folder draw status entries.")
                } else {
                    ChipFlow {
                        ForEach(mods) { mod in
                            ModChip(mod: mod)
                        }
                    }
                    Caption("Claude Code draws these mods' status entries in its status area, beside the statusline, so Combine can't keep or drop them. To hide one, use the mod's own settings (/config in Claude Code) or turn the mod off for this folder; the Mods page will have a switch for each.")
                }
            }
        }
    }
}

private struct ModChip: View {
    let mod: ModStatusEntry

    private var help: String {
        let from = mod.origin == .pluginDirs ? "a plugin folder this folder's settings list" : "a plugin turned on in this folder"
        var text = "\(mod.name): a mod from \(from). Claude Code draws its status entry, not the statusline."
        if let setting = mod.statusSetting { text += " Its own setting \u{201C}\(setting)\u{201D} may switch the entry." }
        return text
    }

    var body: some View {
        Text(mod.name)
            .font(.caption.monospaced())
            .lineLimit(1)
            .padding(.horizontal, 8)
            .padding(.vertical, 3)
            .background(Capsule().fill(Color.hex("a78bfa").opacity(0.2)))
            .overlay(Capsule().strokeBorder(Color.hex("a78bfa").opacity(0.8), lineWidth: 1))
            .help(help)
            .accessibilityLabel("Mod \(mod.name), drawn by Claude Code")
    }
}

/// Test with live data, and when it last ran.
private struct LiveTestRow: View {
    let live: CombineChoice.LiveTest?
    let busy: Bool
    let enabled: Bool
    let test: () -> Void

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 8) {
            Button("Test with live data", action: test)
                .disabled(busy || !enabled)
                .help("Runs your statusline again now with Sanduhr's current numbers, the time, and the model and context of your latest session in this folder (read from its transcript; nothing else is read).")
            if let live { Caption(Self.status(live)) }
        }
    }

    static func status(_ live: CombineChoice.LiveTest) -> String {
        let time = live.at.formatted(date: .omitted, time: .shortened)
        guard live.ran else { return "Couldn't run your statusline at \(time)." }
        switch (live.input.liveLimits, live.input.liveSession) {
        case (true, true): return "Tested \(time) with live numbers."
        case (true, false): return "Tested \(time) with live numbers; model and context are sample values (no session found)."
        case (false, true): return "Tested \(time); no saved numbers yet, so the limits are sample values."
        case (false, false): return "Tested \(time) with sample values: no saved numbers or session found."
        }
    }
}

/// A chip: colored by source, struck through when dropped.
private struct ChipButton: View {
    let chip: StatuslineChips.Chip
    let help: String
    let toggle: () -> Void

    private var tint: Color { chip.source == .sanduhr ? Color.hex("f59e0b") : Color.hex("60a5fa") }

    var body: some View {
        Button(action: toggle) {
            Text(chip.label)
                .font(.caption.monospaced())
                .strikethrough(!chip.kept)
                .lineLimit(1)
                .truncationMode(.middle)
                .frame(maxWidth: 240)
                .padding(.horizontal, 8)
                .padding(.vertical, 3)
                .foregroundStyle(chip.kept ? Color.primary : Color.secondary)
                .background(Capsule().fill(tint.opacity(chip.kept ? 0.25 : 0.06)))
                .overlay(Capsule().strokeBorder(tint.opacity(chip.kept ? 0.9 : 0.35), lineWidth: 1))
        }
        .buttonStyle(.plain)
        .disabled(!chip.enabled)
        .help(chip.enabled ? help : "Kept whole: this can't be dropped.")
        .accessibilityLabel("\(chip.source == .sanduhr ? "Sanduhr's" : "Your") segment \(chip.label)")
        .accessibilityValue(chip.kept ? "kept" : "dropped")
    }
}

/// The final line with these picks, drawn from the user's output, and the mods' entries.
private struct CombinePreview: View {
    let theirs: String?
    let join: StatuslineJoin
    let selection: StatuslineSelection
    let loading: Bool
    let input: Data?
    let mods: [ModStatusEntry]
    var model: IntegrationsModel
    @State private var output: String?

    private struct Key: Equatable {
        let theirs: String?
        let join: StatuslineJoin
        let selection: StatuslineSelection
        let input: Data?
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Caption(input == nil ? "Preview, with sample data (your statusline ran once to draw it):"
                                 : "Preview, with live data:")
            Terminal(text: output.flatMap { $0.isEmpty ? nil : $0.trimmingCharacters(in: .newlines) },
                     placeholder: loading ? "Running…" : "No preview: Python or the scripts weren't found.")
            if !mods.isEmpty {
                Text("Claude Code's status area").font(.caption.weight(.medium))
                Terminal(text: mods.map { "\u{26A0} \($0.name): …" }.joined(separator: "\n"), placeholder: "")
                Caption("Mods draw these themselves, so only their names stand in: their text is known only inside a session.")
            }
        }
        .task(id: Key(theirs: theirs, join: join, selection: selection, input: input)) {
            guard let theirs else {
                output = nil
                return
            }
            output = await model.composeStatusline(theirs: theirs, join: join, selection: selection, input: input)
        }
    }
}

/// Terminal-looking text, ANSI drawn.
private struct Terminal: View {
    let text: String?
    let placeholder: String

    var body: some View {
        Group {
            if let text {
                Text(ANSIText.attributed(text)).lineLimit(8)
            } else {
                Text(placeholder).foregroundStyle(.secondary)
            }
        }
        .font(.caption.monospaced())
        .padding(6)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(RoundedRectangle(cornerRadius: 4).fill(Color.black.opacity(0.85)))
        .foregroundStyle(Color(white: 0.9))
    }
}

/// Chips left to right, wrapping to the width offered.
private struct ChipFlow: Layout {
    var spacing: CGFloat = 6

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let width = proposal.width ?? 520
        let end = place(subviews, width: width) { _, _, _ in }
        return CGSize(width: width, height: end)
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        _ = place(subviews, width: bounds.width) { view, point, size in
            view.place(at: CGPoint(x: bounds.minX + point.x, y: bounds.minY + point.y), proposal: ProposedViewSize(size))
        }
    }

    /// Lays the views out in rows; returns the height used.
    private func place(_ subviews: Subviews, width: CGFloat,
                       _ put: (LayoutSubview, CGPoint, CGSize) -> Void) -> CGFloat {
        var x: CGFloat = 0, y: CGFloat = 0, row: CGFloat = 0
        for view in subviews {
            let size = view.sizeThatFits(ProposedViewSize(width: width, height: nil))
            if x > 0, x + size.width > width {
                x = 0
                y += row + spacing
                row = 0
            }
            put(view, CGPoint(x: x, y: y), size)
            x += size.width + spacing
            row = max(row, size.height)
        }
        return subviews.isEmpty ? 0 : y + row
    }
}
