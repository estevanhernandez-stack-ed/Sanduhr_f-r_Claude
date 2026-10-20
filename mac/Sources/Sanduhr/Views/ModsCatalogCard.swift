import SwiftUI

// The catalog's card, its header, badge, contents line and seat chips, split out of
// ModsCatalogView.swift; the views are unchanged.

/// One card: what it is, where it comes from, and where it is installed.
struct CatalogCardView: View {
    let card: CatalogCard
    var model: ModsPageModel
    let demo: Bool
    var focused: FocusState<String?>.Binding

    var body: some View {
        VStack(alignment: .leading, spacing: 5) {
            Button { model.toggleCard(card.id) } label: {
                HStack(spacing: 7) {
                    Image(systemName: "chevron.down")
                        .font(.caption2.weight(.semibold))
                        .foregroundStyle(.secondary)
                        .frame(width: 10)
                    CatalogCardHeader(card: card, search: model.filter.search)
                    Spacer(minLength: 0)
                }
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .focused(focused, equals: card.id)
            .onKeyPress(.leftArrow) {
                // A card the search holds open stays open.
                guard model.openCards.contains(card.id) else { return .ignored }
                model.foldCard(card.id)
                focused.wrappedValue = card.id
                return .handled
            }
            .accessibilityLabel(card.name)
            .accessibilityValue("Open")
            .accessibilityHint("Folds it to one line")
            if !card.description.isEmpty {
                Text(card.description)
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .lineLimit(3)
                    .fixedSize(horizontal: false, vertical: true)
            }
            if !card.sources.isEmpty {
                Text("From " + card.sources.joined(separator: ", "))
                    .font(.caption)
                    .fixedSize(horizontal: false, vertical: true)
            }
            if let c = card.contents, !c.summary.isEmpty {
                PluginContentsLine(card: card, contents: c, model: model)
            }
            if let line = loadLine {
                Text(line).font(.caption).foregroundStyle(.secondary)
            }
            if card.isSanduhrs {
                HStack(spacing: 8) {
                    Text((model.studioOnLine(demo: demo) ?? "Sanduhr's own mod") + ".")
                        .font(.caption)
                        .foregroundStyle(Color.hex("a78bfa"))
                    Button("Folders…") { model.openStudio(.folders) }
                        .controlSize(.small)
                    Button("Studio…") { model.openStudio() }
                        .controlSize(.small)
                }
                WatchersAbovePromptLine()
            }
            if let risk = riskLine {
                Text(risk).font(.caption).foregroundStyle(.orange)
            }
            ForEach(card.problems, id: \.self) { Text($0).font(.caption).foregroundStyle(.orange) }
            CatalogChips(card: card, model: model)
        }
        .padding(10)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(RoundedRectangle(cornerRadius: 8, style: .continuous).fill(Color.secondary.opacity(0.08)))
    }

    /// "About 1.2K tokens a session in Work, 300 in Personal": the seats where it is on.
    private var loadLine: String? {
        let parts = card.chips.compactMap { chip -> String? in
            guard let n = card.load[chip.seat], n > 0 else { return nil }
            return "\(SessionLoad.short(n)) in \(chip.label)"
        }
        return parts.isEmpty ? nil : "About " + parts.joined(separator: ", ") + " tokens a session."
    }

    /// The validator's answer for one of its items, when Check ran.
    private var riskLine: String? {
        for id in card.itemIDs {
            guard case .report(let r)? = model.checks[id] else { continue }
            return "Checked: " + r.risk.title + (r.passed ? "" : ", Claude Code would refuse it")
        }
        return nil
    }
}

/// What a plugin brings, and its names behind Show.
private struct PluginContentsLine: View {
    let card: CatalogCard
    let contents: CatalogContents
    var model: ModsPageModel

    var body: some View {
        let open = model.showsContents(card)
        VStack(alignment: .leading, spacing: 3) {
            HStack(spacing: 6) {
                Text(contents.summary).font(.caption)
                Button(open ? "Hide" : "Show") {
                    if open { model.openContents.remove(card.id) } else { model.openContents.insert(card.id) }
                }
                .buttonStyle(.link)
                .font(.caption)
                .accessibilityLabel(open ? "Hide what \(card.name) brings" : "Show what \(card.name) brings")
            }
            if open {
                ForEach(rows, id: \.0) { title, names in
                    Text(title + ": " + names.joined(separator: ", "))
                        .font(.caption)
                        .foregroundStyle(.secondary)
                        .textSelection(.enabled)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
        }
    }

    private var rows: [(String, [String])] {
        [("Skills", contents.skills), ("Agents", contents.agents), ("Commands", contents.commands),
         ("Output styles", contents.outputStyles)].filter { !$0.1.isEmpty }
    }
}

private struct CatalogCardHeader: View {
    let card: CatalogCard
    var search = ""

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 6) {
            Text(highlighted(card.name, search)).font(.body.weight(.semibold))
            if let v = card.version {
                Text(v).font(.caption.monospaced()).foregroundStyle(.secondary)
            }
            CatalogBadge(text: card.kind == .plugin && card.isMod ? "Mod" : card.kind.badge, color: .secondary)
            if card.isDuplicate { CatalogBadge(text: "Duplicate", color: .orange) }
            // Sanduhr's own mod says Not installed in its chips line; Available is a marketplace listing's word.
            if card.isAvailable && !card.isSanduhrs { CatalogBadge(text: "Available", color: Color.hex("4ade80")) }
        }
    }
}

struct CatalogBadge: View {
    let text: String
    let color: Color

    var body: some View {
        Text(text)
            .font(.caption2.weight(.semibold))
            .padding(.horizontal, 6)
            .padding(.vertical, 1)
            .foregroundStyle(color == .secondary ? Color.primary : color)
            .background(Capsule().fill(color.opacity(0.18)))
    }
}

/// The seat chips: label, side of the wall and the state there. A click opens By Seat.
private struct CatalogChips: View {
    let card: CatalogCard
    var model: ModsPageModel

    var body: some View {
        if card.isAvailable {
            Text(card.isSanduhrs ? "Not installed." : "Listed by a marketplace, installed in no folder.")
                .font(.caption)
                .foregroundStyle(.secondary)
        } else {
            ChipFlow(spacing: 6) {
                ForEach(card.chips) { chip in
                    Button { model.open(chip, card: card) } label: { CatalogChipLabel(chip: chip) }
                        .buttonStyle(.plain)
                        .help("Open \(chip.label) on By Folder")
                        .accessibilityLabel("\(chip.label), \(SeatMarkBadge(mark: chip.mark).title), \(chip.state.title)")
                }
            }
        }
    }
}

private struct CatalogChipLabel: View {
    let chip: CatalogChip

    var body: some View {
        HStack(spacing: 4) {
            Text(chip.label).font(.caption.weight(.medium))
            SeatMarkBadge(mark: chip.mark)
            Text(chip.state.title).font(.caption).foregroundStyle(stateColor)
        }
        .padding(.horizontal, 7)
        .padding(.vertical, 3)
        .background(Capsule().strokeBorder(Color.secondary.opacity(0.35)))
        .contentShape(Capsule())
    }

    private var stateColor: Color { chip.state.color }
}

extension CatalogChipState {
    /// A chip's state word, and a compact line's dot, in this color.
    var color: Color {
        switch self {
        case .on: Color.hex("4ade80")
        case .declared: Color.hex("f87171")
        case .session: .orange
        case .off, .projects: .secondary
        }
    }
}
