import SwiftUI
import AppKit

/// Drawing what plays in its look (item 65c): on the notch wings, the strip and the Desk line,
/// while Settings, Desk, Now Playing's "Style what's playing" is on.
@MainActor
enum NowPlayingStyled {
    /// The look for what plays, nil when the switch is off or nothing plays.
    static func look(_ info: NowPlayingInfo?, on: Bool) -> SongLook? {
        guard on, let info, info.state != .none else { return nil }
        return NowPlayingLookStore.shared.look(for: info)
    }

    /// The switch as saved (for places that read it outside a view's storage).
    nonisolated static var savedOn: Bool { UserDefaults.desk.bool(forKey: NowPlayingLooks.styleKey) }

    /// The text in the look's letter style, in the Desk font; nil without a look.
    static func plan(_ text: String, look: SongLook?, family: String) -> MessageTypography.Plan? {
        guard let look else { return nil }
        return MessageTypography.plan(text, style: look.font, family: family)
    }

    /// The text's width as drawn: styled when there is a look, else in the plain font.
    static func width(_ text: String?, size: CGFloat, font: String, look: SongLook?) -> CGFloat {
        guard let text, let plan = plan(text, look: look, family: font) else {
            return NotchWingsView.textWidth(text, size, font)
        }
        return MessageTypography.width(plan, size: size)
    }

    /// The ink: the look's gradient, else the place's own.
    static func ink(_ look: SongLook?, fallback: String) -> String { look?.ink ?? fallback }
}

/// A line in a plan, or plain in a font: the one place ScrollOnceText and the Desk line draw it.
struct StyledLineText: View {
    let text: String
    let plan: MessageTypography.Plan?
    let size: CGFloat
    let font: Font

    var body: some View {
        if let plan {
            MessageTypography.text(plan, size: size)
                .modifier(Slant(degrees: plan.slant))
        } else {
            Text(text).font(font)
        }
    }
}

// MARK: - Settings, Desk, Now Playing

/// "Style what's playing", Claude's suggestion card, the direct switch and the saved looks.
struct NowPlayingLooksSection: View {
    @AppStorage(NowPlayingLooks.styleKey, store: .desk) private var styleOn = false
    @AppStorage(NowPlayingLooks.directKey, store: .desk) private var direct = false
    var store = NowPlayingLookStore.shared
    @State private var confirmClear = false

    var body: some View {
        Section("Looks") {
            if let p = store.pending {
                NowPlayingLookSuggestionCard(proposal: p, save: { store.approve() }, dismiss: { store.dismiss() })
            }
            Toggle("Style what's playing", isOn: $styleOn)
                .settingsAnchor(SettingsAnchor.looks)
            Text("Draws each song in its own gradient and letter style on the notch and the Desk. A song gets one of eight palettes and a letter style picked from its name until Claude suggests a look for it.")
                .font(.caption).foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            Toggle("Let Claude style songs directly", isOn: $direct)
            Text("Claude Code can suggest looks for songs you play through Sanduhr's MCP server (propose_now_playing_looks). Off, each suggestion waits here for you to save; on, it is saved at once.")
                .font(.caption).foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            HStack {
                Text(Self.count(store.saved.count))
                    .foregroundStyle(.secondary)
                Spacer()
                Button("Clear Looks…") { confirmClear = true }
                    .disabled(store.saved.isEmpty)
            }
            .confirmationDialog("Clear every saved look?", isPresented: $confirmClear) {
                Button("Clear Looks", role: .destructive) { store.clear() }
            } message: {
                Text("Songs go back to the looks picked from their names.")
            }
        }
        .onAppear { store.loadIfNeeded() }
    }

    /// "No saved looks", "1 saved look", "12 saved looks".
    static func count(_ n: Int) -> String {
        n == 0 ? "No saved looks" : n == 1 ? "1 saved look" : "\(n) saved looks"
    }
}

/// "Claude suggested looks for N songs": each song drawn in its look, Save and Dismiss.
struct NowPlayingLookSuggestionCard: View {
    let proposal: NowPlayingLookProposal
    let save: () -> Void
    let dismiss: () -> Void

    @AppStorage("font", store: .desk) private var savedFont: String?
    private var font: String { DeskFont.resolve(saved: savedFont) }

    static let shown = 6

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(alignment: .firstTextBaseline) {
                Image(systemName: "sparkles").foregroundStyle(.tint)
                Text(Self.headline(proposal)).font(.headline)
                Spacer()
                Button("Dismiss", action: dismiss)
                Button("Save", action: save).buttonStyle(.borderedProminent)
            }
            VStack(alignment: .leading, spacing: 4) {
                ForEach(Array(proposal.looks.prefix(Self.shown).enumerated()), id: \.offset) { _, item in
                    row(item)
                }
                if proposal.looks.count > Self.shown {
                    Text("and \(proposal.looks.count - Self.shown) more")
                        .font(.caption).foregroundStyle(.secondary)
                }
            }
            .padding(8)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(RoundedRectangle(cornerRadius: 6).fill(Color.black))
        }
        .padding(10)
        .background(RoundedRectangle(cornerRadius: 8).fill(Color.accentColor.opacity(0.08)))
        .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(Color.accentColor.opacity(0.25)))
        .accessibilityElement(children: .contain)
    }

    private func row(_ item: ProposedSongLook) -> some View {
        let text = "\(item.title) · \(item.artist)"
        let plan = MessageTypography.plan(text, style: item.look.font, family: font)
        return HStack(alignment: .firstTextBaseline, spacing: 8) {
            StyledLineText(text: text, plan: plan, size: 15, font: .custom(font, size: 15))
                .foregroundStyle(LinearGradient.ink(item.look.ink))
                .lineLimit(1)
            if let mood = item.look.mood {
                Text(mood).font(.caption).foregroundStyle(.white.opacity(0.6))
            }
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(Self.spoken(item))
    }

    static func headline(_ p: NowPlayingLookProposal) -> String {
        p.looks.count == 1 ? "Claude suggested a look for 1 song" : "Claude suggested looks for \(p.looks.count) songs"
    }

    /// "Title by Artist, small caps, mood neon night".
    static func spoken(_ item: ProposedSongLook) -> String {
        var parts = ["\(item.title) by \(item.artist)"]
        if let f = item.look.font { parts.append(f.title.lowercased()) }
        if let m = item.look.mood { parts.append("mood \(m)") }
        return parts.joined(separator: ", ")
    }
}
