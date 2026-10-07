import SwiftUI

/// The "other statusline" choice (items 63, 63b): Combine keeps the user's line and adds
/// Sanduhr's, with the segments of both picked as chips and a live preview of the final line;
/// Replace swaps theirs out until Remove.
struct CombineChoice: View {
    let other: String
    let folder: String
    var model: IntegrationsModel
    @Binding var join: StatuslineJoin
    @Binding var selection: StatuslineSelection
    @State private var chips: StatuslineChips?
    @State private var loading = true

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("\(folder) already has a statusline:")
                .fixedSize(horizontal: false, vertical: true)
            OtherCommandBox(text: other)
            Text("Combine keeps it: Sanduhr runs your statusline first, then adds its meters. If yours is slow (over 1.5 seconds) or fails, Sanduhr's meters still show. Replace shows only Sanduhr's line. Either way, Remove puts yours back.")
                .fixedSize(horizontal: false, vertical: true)
            picker
            Picker("Sanduhr's meters", selection: $join) {
                Text("On their own row").tag(StatuslineJoin.line)
                Text("On the same row").tag(StatuslineJoin.same)
            }
            .pickerStyle(.segmented)
            CombinePreview(theirs: chips?.inspection.theirs, join: join, selection: selection,
                           loading: loading, model: model)
            Text("Status entries from Claude Code mods aren't part of the statusline, so they aren't listed here: each mod has its own switch on the Mods page.")
                .font(.caption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
        .task(id: other) {
            loading = true
            chips = await model.inspectStatusline(chain: other).map(StatuslineChips.init)
            selection = chips?.selection ?? StatuslineSelection()
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
}

/// Their lines and Sanduhr's segments as chips: click keeps or drops one.
private struct SegmentChips: View {
    let chips: StatuslineChips
    let change: ((inout StatuslineChips) -> Void) -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Click a segment to keep or drop it. Yours are matched by how they start, so one that comes and goes (a git branch outside a repository) doesn't move the others.")
                .font(.caption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            ForEach(chips.lineIndices, id: \.self) { line in
                LineChips(chips: chips, line: line, change: change)
            }
            Toggle("Keep segments yours shows later that aren't here now", isOn: Binding(
                get: { chips.keepNew },
                set: { on in change { $0.keepNew = on } }))
                .font(.caption)
            VStack(alignment: .leading, spacing: 4) {
                Text("Sanduhr's").font(.caption.weight(.medium))
                ChipFlow {
                    ForEach(chips.mineChips) { chip in
                        ChipButton(chip: chip) { change { $0.toggle(chip) } }
                    }
                }
            }
        }
    }
}

/// One line of theirs: its separator (detected, changeable) and its segments.
private struct LineChips: View {
    let chips: StatuslineChips
    let line: Int
    let change: ((inout StatuslineChips) -> Void) -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack(spacing: 6) {
                Text(chips.lineIndices.count > 1 ? "Your line \(line + 1), split on" : "Yours, split on")
                    .font(.caption.weight(.medium))
                separatorMenu
            }
            ChipFlow {
                ForEach(chips.chips(line: line)) { chip in
                    ChipButton(chip: chip) { change { $0.toggle(chip) } }
                }
            }
            if chips.isWhole(line: line) {
                Text("Sanduhr keeps this line whole: split this way it isn't clear where its segments are.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
    }

    private var separatorMenu: some View {
        Picker("Separator", selection: Binding(
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
    }
}

/// A chip: colored by source, struck through when dropped.
private struct ChipButton: View {
    let chip: StatuslineChips.Chip
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
        .help(chip.enabled ? (chip.kept ? "Kept: click to drop" : "Dropped: click to keep") : "")
        .accessibilityLabel("\(chip.source == .sanduhr ? "Sanduhr's" : "Your") segment \(chip.label)")
        .accessibilityValue(chip.kept ? "kept" : "dropped")
    }
}

/// The final line with these picks, drawn from the user's output against sample data.
private struct CombinePreview: View {
    let theirs: String?
    let join: StatuslineJoin
    let selection: StatuslineSelection
    let loading: Bool
    var model: IntegrationsModel
    @State private var output: String?

    private struct Key: Equatable {
        let theirs: String?
        let join: StatuslineJoin
        let selection: StatuslineSelection
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text("Preview, with sample data (your statusline ran once to draw it):")
                .font(.caption)
                .foregroundStyle(.secondary)
            Group {
                if let output, !output.isEmpty {
                    Text(ANSIText.attributed(output.trimmingCharacters(in: .newlines)))
                        .lineLimit(8)
                } else {
                    Text(loading ? "Running…" : "No preview: Python or the scripts weren't found.")
                        .foregroundStyle(.secondary)
                }
            }
            .font(.caption.monospaced())
            .padding(6)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(RoundedRectangle(cornerRadius: 4).fill(Color.black.opacity(0.85)))
            .foregroundStyle(Color(white: 0.9))
        }
        .task(id: Key(theirs: theirs, join: join, selection: selection)) {
            guard let theirs else {
                output = nil
                return
            }
            output = await model.composeStatusline(theirs: theirs, join: join, selection: selection)
        }
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
