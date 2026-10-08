import SwiftUI

/// Settings v2, slice 3 (reach), the view side: anchors marked on each section, the scrolling area
/// they are checked against, the folding preview and the Advanced disclosure. The model side
/// (anchors, search, links) is SettingsIndex.swift.

extension View {
    /// Marks a section or control as `anchor`: links, search and the smoke hook scroll it into
    /// view, search lights it for 1.5 s, and its frame feeds state.yaml's `settings_anchor_visible`.
    func settingsAnchor(_ anchor: String) -> some View {
        modifier(SettingsAnchorMark(anchor: anchor))
    }

    /// The page's scrolling area, which `settings_anchor_visible` checks an anchor against.
    func settingsViewport() -> some View {
        modifier(SettingsViewportMark())
    }
}

private struct SettingsAnchorMark: ViewModifier {
    let anchor: String
    @Environment(SettingsNavigation.self) private var navigation: SettingsNavigation?

    func body(content: Content) -> some View {
        let lit = navigation?.highlight == anchor
        content
            .background(
                RoundedRectangle(cornerRadius: 6, style: .continuous)
                    .fill(Color.accentColor.opacity(lit ? 0.22 : 0))
                    .padding(-4)
                    .allowsHitTesting(false))
            .animation(.easeOut(duration: 0.25), value: lit)
            .onGeometryChange(for: CGRect.self) { $0.frame(in: .global) } action: { frame in
                navigation?.anchorFrames[anchor] = frame
            }
            .onDisappear { navigation?.anchorFrames[anchor] = nil }
            .id(anchor)
    }
}

private struct SettingsViewportMark: ViewModifier {
    @Environment(SettingsNavigation.self) private var navigation: SettingsNavigation?

    func body(content: Content) -> some View {
        content.onGeometryChange(for: CGRect.self) { $0.frame(in: .global) } action: { frame in
            navigation?.viewport = frame
        }
    }
}

/// A page's preview card, or below 720 pt of window height a 44 pt strip with its title and a
/// disclosure arrow; opening the strip pushes the controls down and is remembered per page while
/// the window lives (SettingsNavigation.openPreviews).
struct PreviewFold<Card: View>: View {
    let page: SettingsSection
    /// Shown on the strip when the card is folded: what the card would say it shows, for a card
    /// that always draws sample data (Claude Code's statusline input).
    var note: String?
    @ViewBuilder let card: () -> Card
    @Environment(SettingsNavigation.self) private var navigation: SettingsNavigation?

    var body: some View {
        if let navigation, navigation.previewFolds {
            let open = navigation.openPreviews.contains(page)
            VStack(spacing: 6) {
                strip(open: open, navigation: navigation)
                if open { card() }
            }
        } else {
            card()
        }
    }

    private func strip(open: Bool, navigation: SettingsNavigation) -> some View {
        Button {
            if open { navigation.openPreviews.remove(page) } else { navigation.openPreviews.insert(page) }
        } label: {
            HStack(spacing: 8) {
                Image(systemName: open ? "chevron.down" : "chevron.right")
                    .font(.caption.weight(.semibold))
                    .frame(width: 12)
                Text(Self.title(page))
                    .font(.callout.weight(.medium))
                if let note, !open {
                    Text(note).font(.caption).foregroundStyle(.secondary)
                }
                Spacer()
                Text(open ? "Hide" : "Show").font(.caption).foregroundStyle(.secondary)
            }
            .padding(.horizontal, 12)
            .frame(maxWidth: .infinity, minHeight: SettingsPreviewFold.stripHeight, maxHeight: SettingsPreviewFold.stripHeight)
            .background(RoundedRectangle(cornerRadius: 10, style: .continuous).fill(.quaternary.opacity(0.5)))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityLabel("\(Self.title(page)), \(open ? "shown" : "folded")")
        .accessibilityHint(open ? "Hides the preview" : "Shows the preview")
    }

    /// "Notch preview".
    static func title(_ page: SettingsSection) -> String { "\(page.title) preview" }
}

/// A page's Advanced disclosure (slice 3): the few expert controls, folded until opened. Search
/// and links to an anchor inside it open it (SettingsNavigation.open). Its own Form section.
struct AdvancedSection<Content: View>: View {
    let page: SettingsSection
    @ViewBuilder let content: () -> Content
    @Environment(SettingsNavigation.self) private var navigation: SettingsNavigation?
    @State private var localOpen = false

    var body: some View {
        Section {
            DisclosureGroup(isExpanded: expanded) {
                content()
            } label: {
                Text(Self.title)
            }
            .settingsAnchor(SettingsAnchor.advanced)
        }
    }

    static var title: String { "Advanced" }

    private var expanded: Binding<Bool> {
        Binding(
            get: { navigation.map { $0.openAdvanced.contains(page) } ?? localOpen },
            set: { open in
                guard let navigation else { localOpen = open; return }
                if open { navigation.openAdvanced.insert(page) } else { navigation.openAdvanced.remove(page) }
            })
    }
}
