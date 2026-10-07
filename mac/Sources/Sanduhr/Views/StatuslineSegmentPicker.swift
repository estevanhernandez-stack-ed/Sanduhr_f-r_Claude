import AppKit
import SwiftUI

/// The "other statusline" choice (items 63, 63b): Combine keeps the user's line and adds
/// Sanduhr's, with the segments of both picked as chips (named by what they show, styled if
/// wanted) and a live preview of the final line; Replace swaps theirs out until Remove. The
/// folder's mods that draw status entries are listed too, read-only: Claude Code draws those
/// beside the statusline, not the command.
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

    private var duplicates: [StatuslineDuplicate] {
        chips.map { StatuslineDuplicate.find($0, mods: mods ?? []) } ?? []
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
            ModChips(mods: mods, badges: Badges(duplicates, mods: mods ?? []))
            DuplicatesPanel(duplicates: duplicates, mods: mods ?? [], chips: chips, change: change)
            CombinePreview(theirs: chips?.inspection.theirs, join: join,
                           selection: chips?.previewSelection ?? selection, loading: loading,
                           input: live?.input.data, mods: (mods ?? []).filter(\.drawsStatus), model: model)
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
            SegmentChips(chips: chips, badges: Badges(duplicates, mods: mods ?? []), change: change)
        } else {
            Caption(loading ? "Running your statusline once…" : "Your statusline's segments can't be shown: Python or the scripts weren't found. Combine keeps all of it.")
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

/// The duplicate badges per chip: "Also shown by Sanduhr: Context".
struct Badges {
    private var map: [StatuslineDuplicate.Side: [String]] = [:]

    init(_ duplicates: [StatuslineDuplicate], mods: [ModStatusEntry]) {
        func by(_ side: StatuslineDuplicate.Side) -> String {
            switch side {
            case .theirs: "yours"
            case .sanduhr: "Sanduhr"
            case .mod(let path): "the mod \(mods.first { $0.path == path }?.name ?? "")"
            }
        }
        for d in duplicates {
            map[d.a, default: []].append("Also shown by \(by(d.b)): \(d.what)")
            map[d.b, default: []].append("Also shown by \(by(d.a)): \(d.what)")
        }
    }

    func text(_ side: StatuslineDuplicate.Side) -> String? {
        map[side].map { Array(NSOrderedSet(array: $0)) as? [String] ?? $0 }?.joined(separator: "; ")
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
    let badges: Badges
    let change: ((inout StatuslineChips) -> Void) -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Caption("Click a segment to keep or drop it; the brush (or its menu) styles it. Yours are named by what they show and matched by how they start, so one that comes and goes (a git branch outside a repository) doesn't move the others.")
            ForEach(chips.lineIndices, id: \.self) { line in
                LineChips(chips: chips, line: line, badges: badges, change: change)
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
                        SegmentChip(chip: chip, help: Self.help(chip),
                                    badge: SanduhrSegment(rawValue: chip.key).flatMap { badges.text(.sanduhr($0)) },
                                    chips: chips, change: change)
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
    let badges: Badges
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
                    SegmentChip(chip: chip, help: help(chip), badge: badges.text(.theirs(chip.key)),
                                chips: chips, change: change)
                }
            }
            if chips.isWhole(line: line) {
                Caption("Sanduhr keeps this line whole: split this way it isn't clear where its segments are.")
            }
        }
    }

    private func help(_ chip: StatuslineChips.Chip) -> String {
        "\(chip.name), from your statusline: \(chip.label). Click to keep or drop it. Matched by how it starts (\u{201C}\(chip.key)\u{201D}), so it's found wherever it appears."
    }

    private var separatorMenu: some View {
        Picker("Split yours on", selection: Binding(
            get: { chips.separator(line: line) },
            set: { sep in change { $0.setSeparator(sep, line: line) } })) {
            ForEach(StatuslineSeparator.allCases, id: \.self) { sep in
                GlyphMenuLabel(title: sep == chips.detected(line: line) ? "\(sep.title) (found)" : sep.title, shape: sep.shape)
                    .tag(sep)
            }
        }
        .labelsHidden()
        .pickerStyle(.menu)
        .fixedSize()
        .disabled(!chips.separatorChangeable(line: line))
        .help("Split yours on: the separator Sanduhr cuts your line at to find its segments. " + PowerlineGlyph.explanation)
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
                        GlyphMenuLabel(title: g.title, shape: g.shape).tag(StatuslineJoinGlyph?.some(g))
                    }
                }
                .labelsHidden()
                .pickerStyle(.menu)
                .fixedSize()
                .help("Join with: what goes between the segments that remain, in your part and Sanduhr's. " + PowerlineGlyph.explanation)
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

/// The look shared by every chip: name, live text, a duplicate badge; color marks the source.
private struct ChipFace: View {
    let name: String
    let text: String
    let badge: String?
    let tint: Color
    let kept: Bool
    var styled = false

    var body: some View {
        VStack(alignment: .leading, spacing: 1) {
            HStack(spacing: 3) {
                Text(name).font(.caption2.weight(.semibold))
                if styled { Image(systemName: "paintbrush.fill").font(.system(size: 8)) }
            }
            PowerlineGlyph.text(text)
                .font(.caption.monospaced())
                .strikethrough(!kept)
                .lineLimit(1)
                .truncationMode(.middle)
            if let badge {
                Text(badge).font(.caption2).foregroundStyle(Color.orange).lineLimit(2)
            }
        }
        .frame(maxWidth: 220, alignment: .leading)
        .padding(.horizontal, 8)
        .padding(.vertical, 4)
        .foregroundStyle(kept ? Color.primary : Color.secondary)
        .background(RoundedRectangle(cornerRadius: 8).fill(tint.opacity(kept ? 0.22 : 0.06)))
        .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(badge == nil ? tint.opacity(kept ? 0.9 : 0.35) : Color.orange,
                                                                lineWidth: 1))
    }
}

/// A chip of theirs or Sanduhr's: click keeps or drops it, the brush or its menu styles it.
private struct SegmentChip: View {
    let chip: StatuslineChips.Chip
    let help: String
    let badge: String?
    let chips: StatuslineChips
    let change: ((inout StatuslineChips) -> Void) -> Void
    @State private var styling = false

    private var tint: Color { chip.source == .sanduhr ? Color.hex("f59e0b") : Color.hex("60a5fa") }

    var body: some View {
        HStack(spacing: 2) {
            Button { change { $0.toggle(chip) } } label: {
                ChipFace(name: chip.name, text: chip.label, badge: badge, tint: tint, kept: chip.kept, styled: chip.styled)
            }
            .buttonStyle(.plain)
            .disabled(!chip.enabled)
            .help(chip.enabled ? help : "Kept whole: this can't be dropped.")
            .accessibilityLabel("\(chip.name): \(chip.label)")
            .accessibilityValue(chip.kept ? "kept" : "dropped")
            if chip.enabled && !chip.key.isEmpty {
                Button { styling = true } label: { Image(systemName: "paintbrush") }
                    .buttonStyle(.borderless)
                    .help("Style \(chip.name)…")
                    .popover(isPresented: $styling) { popover }
            }
        }
        .contextMenu {
            if chip.enabled {
                Button(chip.kept ? "Drop" : "Keep") { change { $0.toggle(chip) } }
                Button("Style…") { styling = true }
            }
        }
    }

    private var popover: some View {
        StylePopover(name: chip.name, style: Binding(
            get: { chips.style(for: chip) },
            set: { style in change { $0.setStyle(style, for: chip) } }))
    }
}

/// A segment's look: its own colors or ink (one color or a gradient), attributes, letters.
private struct StylePopover: View {
    let name: String
    @Binding var style: SegmentStyle

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("Style: \(name)").font(.headline)
            Toggle("Keep its own colors", isOn: Binding(
                get: { style.keepsOwnColors },
                set: { on in if on { style.keepOwnColors() } else { style.setSolid("#ff2a6d") } }))
                .help("Keep its own colors: the segment keeps the colors your statusline (or Sanduhr) gives it.")
            if !style.keepsOwnColors { InkEditor(style: $style) }
            HStack {
                Toggle("Bold", isOn: $style.bold)
                Toggle("Italic", isOn: $style.italic)
                Toggle("Dim", isOn: $style.dim)
                Toggle("Underline", isOn: $style.underline)
            }
            .toggleStyle(.checkbox)
            Picker("Letters", selection: $style.font) {
                Text("As written").tag(LetterStyle?.none)
                ForEach(LetterStyle.allCases, id: \.self) { f in Text(f.title).tag(LetterStyle?.some(f)) }
            }
            .help("Letters: Unicode letter styles (math letters and small caps). Some fonts draw them differently; digits change only in bold, double-struck, sans and monospace.")
            Caption("Sanduhr's statusline applies this each refresh. Statuslines can't animate; the Desk can.")
            Button("Reset to its own look") { style = SegmentStyle() }
                .disabled(style.isEmpty)
        }
        .padding(14)
        .frame(width: 340)
    }
}

/// Ink: one color, or a gradient of 2 to 4 stops.
private struct InkEditor: View {
    @Binding var style: SegmentStyle

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            Picker("Ink", selection: Binding(
                get: { style.isGradient },
                set: { g in if g { style.makeGradient() } else { style.makeSolid() } })) {
                Text("One color").tag(false)
                Text("Gradient").tag(true)
            }
            .pickerStyle(.segmented)
            HStack(spacing: 6) {
                ForEach(Array(style.ink.indices), id: \.self) { i in
                    ColorPicker("", selection: color(i), supportsOpacity: false).labelsHidden()
                    if style.isGradient && style.ink.count > 2 {
                        Button { style.removeStop(at: i) } label: { Image(systemName: "minus.circle") }
                            .buttonStyle(.borderless)
                            .help("Remove this stop")
                    }
                }
                if style.isGradient && style.ink.count < SegmentStyle.maxInk {
                    Button("Add stop") { style.addStop() }
                }
            }
            Caption(style.isGradient ? "A gradient runs across the segment's characters, in truecolor." : "One color for the whole segment, in truecolor.")
        }
    }

    private func color(_ i: Int) -> Binding<Color> {
        Binding(
            get: {
                let c = style.ink.indices.contains(i) ? SegmentStyle.components(style.ink[i]) : nil
                return Color(red: c?.0 ?? 1, green: c?.1 ?? 1, blue: c?.2 ?? 1)
            },
            set: { new in
                guard let ns = NSColor(new).usingColorSpace(.sRGB) else { return }
                style.setStop(i, hex: SegmentStyle.hex(red: ns.redComponent, green: ns.greenComponent, blue: ns.blueComponent))
            })
    }
}

/// The folder's mods that draw status entries, read-only.
private struct ModChips: View {
    let mods: [ModStatusEntry]?
    let badges: Badges

    var body: some View {
        if let mods {
            VStack(alignment: .leading, spacing: 4) {
                Text("From your mods").font(.caption.weight(.medium))
                if mods.isEmpty {
                    Caption("No mods in this folder draw status entries.")
                } else {
                    ChipFlow {
                        ForEach(mods) { mod in ModChip(mod: mod, badge: badges.text(.mod(mod.path))) }
                    }
                    Caption("Claude Code draws these mods' status entries in its status area, beside the statusline, so Combine can't keep, drop or style them. To hide one, use the mod's own settings (/config in Claude Code) or turn the mod off for this folder; the Mods page will have a switch for each.")
                }
            }
        }
    }
}

private struct ModChip: View {
    let mod: ModStatusEntry
    let badge: String?

    private var help: String {
        let from = mod.origin == .pluginDirs ? "a plugin folder this folder's settings list" : "a plugin turned on in this folder"
        let draws = mod.drawsStatus ? "Claude Code draws its status entry, not the statusline" : "it draws Sanduhr's meters above the prompt"
        var text = "\(mod.name): a mod from \(from); \(draws), so Sanduhr can't keep, drop or style it."
        if let setting = mod.statusSetting { text += " Its own setting \u{201C}\(setting)\u{201D} may switch the entry." }
        if badge != nil { text += " To remove the duplicate, change the mod's own settings or turn it off on the Mods page." }
        return text
    }

    var body: some View {
        ChipFace(name: mod.name, text: mod.drawsStatus ? "Status entry" : "Above the prompt", badge: badge,
                 tint: Color.hex("a78bfa"), kept: true)
            .help(help)
            .accessibilityLabel("Mod \(mod.name), drawn by Claude Code")
    }
}

/// Duplicates: a summary, and for yours against Sanduhr's a one-click choice.
private struct DuplicatesPanel: View {
    let duplicates: [StatuslineDuplicate]
    let mods: [ModStatusEntry]
    let chips: StatuslineChips?
    let change: ((inout StatuslineChips) -> Void) -> Void

    var body: some View {
        if let summary = StatuslineDuplicate.summary(duplicates) {
            VStack(alignment: .leading, spacing: 4) {
                Label(summary, systemImage: "exclamationmark.triangle").font(.caption.weight(.medium)).foregroundStyle(Color.orange)
                ForEach(duplicates) { d in row(d) }
            }
        }
    }

    @ViewBuilder
    private func row(_ d: StatuslineDuplicate) -> some View {
        if d.resolvable {
            HStack(spacing: 6) {
                Caption("\(d.what): yours and Sanduhr's both show it.")
                Button("Keep yours") { change { $0.resolve(d, keepYours: true) } }
                    .help("Drops Sanduhr's \(d.what) segment.")
                    .disabled((chips?.mineChips.filter(\.kept).count ?? 0) <= 1)
                Button("Keep Sanduhr's") { change { $0.resolve(d, keepYours: false) } }
                    .help("Drops your \(d.what) segment.")
            }
        } else {
            Caption("\(d.what): \(side(d.a)) and \(side(d.b)) both show it. A mod's entry changes only in its own settings (/config in Claude Code) or on the Mods page.")
        }
    }

    private func side(_ s: StatuslineDuplicate.Side) -> String {
        switch s {
        case .theirs: "yours"
        case .sanduhr: "Sanduhr's"
        case .mod(let path): "the mod \(mods.first { $0.path == path }?.name ?? "")"
        }
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

/// Terminal-looking text, ANSI drawn, powerline glyphs drawn by Sanduhr.
private struct Terminal: View {
    let text: String?
    let placeholder: String

    var body: some View {
        Group {
            if let text {
                ANSIText.text(text).lineLimit(8)
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
