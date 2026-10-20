import SwiftUI

// The catalog's group, one-line and available rows and filter bar, split out of
// ModsCatalogView.swift; the views are unchanged.

/// A name with the search's words marked (slice A): bold on a tint of the accent color.
func highlighted(_ text: String, _ search: String) -> AttributedString {
    var out = AttributedString(text)
    for r in ConfigCatalog.matchRanges(in: text, search: search) {
        guard let lower = AttributedString.Index(r.lowerBound, within: out),
              let upper = AttributedString.Index(r.upperBound, within: out) else { continue }
        out[lower..<upper].inlinePresentationIntent = .stronglyEmphasized
        out[lower..<upper].backgroundColor = Color.accentColor.opacity(0.25)
    }
    return out
}

/// One group: a header that opens and closes it, then its runs of cards.
struct CatalogGroupView: View {
    let section: CatalogSection
    var model: ModsPageModel
    /// A search opens every group and holds them open, so the header doesn't toggle meanwhile.
    let searching: Bool
    let demo: Bool
    /// The line or card header with keyboard focus, kept as a line opens into its card.
    @FocusState private var focused: String?

    var body: some View {
        let open = searching || model.openGroups.contains(section.group)
        VStack(alignment: .leading, spacing: 8) {
            Button {
                if model.openGroups.contains(section.group) { model.openGroups.remove(section.group) } else { model.openGroups.insert(section.group) }
            } label: {
                HStack(spacing: 6) {
                    Image(systemName: open ? "chevron.down" : "chevron.right")
                        .font(.caption.weight(.semibold))
                        .frame(width: 12)
                    Text(section.group.title).font(.callout.weight(.semibold))
                        .foregroundStyle(section.group == .attention ? Color.orange : Color.primary)
                    Text("\(section.count)").font(.caption.monospacedDigit()).foregroundStyle(.secondary)
                    Spacer()
                }
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .disabled(searching)
            .help(searching ? "Every group is open while you search." : "")
            .accessibilityLabel("\(section.group.title), \(section.count)")
            .accessibilityValue(open ? "Open" : "Closed")
            if open {
                ForEach(section.runs) { run in
                    let key = section.group.rawValue + "/" + run.title
                    let runOpen = searching || run.title.isEmpty || !model.closedRuns.contains(key)
                    if !run.title.isEmpty {
                        Button {
                            if model.closedRuns.contains(key) { model.closedRuns.remove(key) } else { model.closedRuns.insert(key) }
                        } label: {
                            HStack(spacing: 5) {
                                Image(systemName: runOpen ? "chevron.down" : "chevron.right")
                                    .font(.caption2.weight(.semibold))
                                    .frame(width: 10)
                                Text(run.title + " · \(run.cards.count)")
                                    .font(.caption.weight(.semibold))
                                Spacer()
                            }
                            .foregroundStyle(.secondary)
                            .contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)
                        .disabled(searching)
                        .padding(.top, 2)
                        .accessibilityLabel("\(run.title), \(run.cards.count)")
                        .accessibilityValue(runOpen ? "Open" : "Closed")
                    }
                    if runOpen {
                        LazyVStack(alignment: .leading, spacing: 4) {
                            ForEach(run.cards) { card in
                                if section.group == .available {
                                    AvailableRow(card: card, search: model.filter.search)
                                } else if model.isOpen(card) {
                                    CatalogCardView(card: card, model: model, demo: demo, focused: $focused)
                                } else {
                                    CatalogLine(card: card, model: model, demo: demo, focused: $focused)
                                }
                            }
                        }
                    }
                }
            }
        }
        .padding(.top, 4)
    }
}

/// A card folded to one line (item 75, slice A): what it is, its name, a dot per seat in that
/// seat's state color, and the Needs a look mark. A click opens the full card in its place.
private struct CatalogLine: View {
    let card: CatalogCard
    var model: ModsPageModel
    let demo: Bool
    var focused: FocusState<String?>.Binding

    var body: some View {
        HStack(spacing: 8) {
            line
            if card.isSanduhrs {
                // Sanduhr's own mod (2026-10-14: in the list, not above it): where it's on, and its two doors.
                if let on = model.studioOnLine(demo: demo) {
                    Text(on).font(.caption).foregroundStyle(.secondary).lineLimit(1)
                }
                Button("Folders…") { model.openStudio(.folders) }
                    .controlSize(.small)
                    .help("Switch it on or off, update or remove it, per Claude Code folder.")
                Button("Studio…") { model.openStudio() }
                    .controlSize(.small)
                    .help("Preview the band with demo numbers and style it.")
            }
        }
        .padding(.trailing, card.isSanduhrs ? 8 : 0)
        .background(RoundedRectangle(cornerRadius: 6, style: .continuous).fill(Color.secondary.opacity(0.06)))
    }

    private var line: some View {
        Button { model.toggleCard(card.id) } label: {
            HStack(spacing: 7) {
                Image(systemName: "chevron.right")
                    .font(.caption2.weight(.semibold))
                    .foregroundStyle(.secondary)
                    .frame(width: 10)
                Image(systemName: card.kind.symbol)
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .frame(width: 16)
                Text(highlighted(card.name, model.filter.search)).font(.callout.weight(.medium)).lineLimit(1)
                if card.kind == .plugin && card.isMod { CatalogBadge(text: "Mod", color: .secondary) }
                if needsLook {
                    Image(systemName: "exclamationmark.triangle.fill").font(.caption2).foregroundStyle(.orange)
                }
                Spacer(minLength: 8)
                HStack(spacing: 4) {
                    ForEach(card.chips) { chip in
                        Circle()
                            .fill(chip.state.color)
                            .frame(width: 8, height: 8)
                            .help("\(chip.label): \(chip.state.title)")
                    }
                }
            }
            .padding(.horizontal, 8)
            .padding(.vertical, 5)
            .frame(maxWidth: .infinity, alignment: .leading)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .focused(focused, equals: card.id)
        .onKeyPress(.rightArrow) {
            model.openCard(card.id)
            focused.wrappedValue = card.id
            return .handled
        }
        .help(card.description)
        .accessibilityLabel(accessibilityText)
        .accessibilityValue("Closed")
        .accessibilityHint("Opens its card. Right Arrow opens it, Left Arrow folds it.")
    }

    private var needsLook: Bool { card.needsLook || model.failedChecks.contains(card.id) }

    private var accessibilityText: String {
        var parts = [card.name, card.kind == .plugin && card.isMod ? "Mod" : card.kind.badge]
        if needsLook { parts.append("needs a look") }
        parts += card.chips.map { "\($0.label) \($0.state.title)" }
        return parts.joined(separator: ", ")
    }
}

/// A marketplace's plugin installed nowhere: one line, so a list of hundreds stays short.
private struct AvailableRow: View {
    let card: CatalogCard
    let search: String

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 6) {
            Text(highlighted(card.name, search)).font(.caption.weight(.semibold))
            if let v = card.version {
                Text(v).font(.caption2.monospaced()).foregroundStyle(.secondary)
            }
            Text(card.description).font(.caption).foregroundStyle(.secondary).lineLimit(1)
        }
        .help(card.description)
    }
}

struct CatalogFilterBar: View {
    @Bindable var model: ModsPageModel
    let demo: Bool
    /// Search always; Kind and Show with the toolbar, Folder with two or more folders.
    let controls: ModsControls

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            if controls.toolbar {
                HStack(spacing: 8) {
                    Picker("Kind", selection: $model.filter.kind) {
                        Text("All kinds").tag(CatalogKind?.none)
                        ForEach(CatalogKind.allCases, id: \.self) { Text($0.title).tag(CatalogKind?.some($0)) }
                    }
                    .frame(maxWidth: 200)
                    if controls.picker {
                        Picker("Folder", selection: $model.filter.seat) {
                            Text("All folders").tag(String?.none)
                            ForEach(model.filterSeats(demo: demo)) { Text($0.label).tag(String?.some($0.path)) }
                        }
                        .frame(maxWidth: 200)
                    }
                }
            }
            HStack(spacing: 8) {
                if controls.toolbar {
                    Picker("Show", selection: $model.filter.show) {
                        ForEach(CatalogFilter.Show.allCases, id: \.self) { Text($0.title).tag($0) }
                    }
                    .frame(maxWidth: 240)
                }
                TextField("Search name, description or marketplace", text: $model.filter.search)
                    .textFieldStyle(.roundedBorder)
                    .frame(maxWidth: 280)
            }
        }
        .controlSize(.small)
    }
}
