import SwiftUI

/// "Claude suggested a theme" over Settings, Widget, Themes (item 55): the theme drawn as its
/// gallery card would be, its name and description (Claude's note), where it would be saved when
/// the name is taken, the lint's warnings, and Save, Save and Apply, Dismiss.
struct ThemeSuggestionBanner: View {
    let proposal: ThemeProposal
    let placement: ThemeProposal.Placement?
    let save: () -> Void
    let saveAndApply: () -> Void
    let dismiss: () -> Void

    var body: some View {
        HStack(alignment: .top, spacing: 14) {
            card
            VStack(alignment: .leading, spacing: 6) {
                header
                details
                Spacer(minLength: 0)
                buttons
            }
        }
        .padding(10)
        .background(RoundedRectangle(cornerRadius: 8).fill(Color.accentColor.opacity(0.08)))
        .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(Color.accentColor.opacity(0.25)))
        .accessibilityElement(children: .contain)
    }

    @ViewBuilder
    private var card: some View {
        if let theme = proposal.preview(id: placement?.key) {
            ThemeCard(item: ThemeGalleryItem(theme: theme, isCurrent: false, isUser: true))
                .frame(width: 140)
                .accessibilityHidden(true)
        }
    }

    private var header: some View {
        HStack(alignment: .firstTextBaseline, spacing: 6) {
            Image(systemName: "sparkles").foregroundStyle(.tint)
            Text("Claude suggested a theme").font(.headline)
        }
    }

    @ViewBuilder
    private var details: some View {
        Text(proposal.name).font(.callout.weight(.semibold))
        if let note = proposal.summary {
            Text(note).font(.callout).foregroundStyle(.secondary).lineLimit(3)
                .fixedSize(horizontal: false, vertical: true)
        }
        if let line = Self.placementLine(proposal, placement) {
            Text(line).font(.caption).foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
        if !proposal.findings.isEmpty {
            let n = proposal.findings.count
            Label("\(n) design note\(n == 1 ? "" : "s") from the theme check", systemImage: "exclamationmark.triangle")
                .font(.caption).foregroundStyle(.secondary)
                .help(proposal.findings.map(\.message).joined(separator: "\n"))
        }
    }

    private var buttons: some View {
        HStack {
            Button("Dismiss", action: dismiss)
            Spacer()
            Button("Save", action: save)
                .help("Adds the theme to your themes without switching to it.")
                .disabled(placement == nil)
            Button("Save and Apply", action: saveAndApply)
                .buttonStyle(.borderedProminent)
                .help("Adds the theme and puts it on the widget.")
                .disabled(placement == nil)
        }
    }

    /// Where the theme goes when its name is taken, or why it cannot be saved.
    static func placementLine(_ p: ThemeProposal, _ place: ThemeProposal.Placement?) -> String? {
        guard let place else { return "Your themes already use \(p.key) and the keys after it; it cannot be saved." }
        if place.sameAsExisting { return "You already have this theme (\(place.key).json)." }
        if let from = place.renamedFrom {
            return "You have a different theme called \(from); this one is saved as \(place.key).json."
        }
        return nil
    }
}
