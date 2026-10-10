import AppKit
import SwiftUI

/// The What's New window (item 57): one small window, reused, with a card per release highlight.
/// Shown once after an update (AppDelegate, after the widget and Desk are up), and any time from
/// Settings, About and What's New… in the menus.
@MainActor
final class WhatsNewWindowController {
    static let shared = WhatsNewWindowController()
    private(set) var window: NSWindow?

    /// The window is on screen.
    var isOpen: Bool { window?.isVisible ?? false }

    func close() { window?.close() }

    /// Shows `cards`, the ones missed since `lastSeen`, under one header for the releases they
    /// cover; or, from What's New… in About and the menus, WhatsNew.forMenu: this version's
    /// Highlights on a fresh install, else every card up to this version.
    func show(_ cards: [WhatsNewCard]? = nil, lastSeen: String? = nil) {
        let current = AppInfo.current.version
        let shown: [WhatsNewCard], header: String
        if let cards {
            shown = cards
            header = WhatsNew.rangeLabel(cards, lastSeen: lastSeen, current: current) ?? "Version \(current)"
        } else {
            (shown, header) = WhatsNew.forMenu(current: current, installed: WhatsNew.installed())
        }
        let root = WhatsNewView(cards: shown, range: header,
                                close: { [weak self] in self?.close() })
        if let window {
            window.contentView = NSHostingView(rootView: root)
        } else {
            let w = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 640, height: 620),
                             styleMask: [.titled, .closable, .resizable, .fullSizeContentView],
                             backing: .buffered, defer: false)
            w.title = WhatsNewView.title
            w.titleVisibility = .hidden
            w.titlebarAppearsTransparent = true
            w.isMovableByWindowBackground = true
            w.isReleasedWhenClosed = false
            w.minSize = NSSize(width: 520, height: 420)
            w.contentView = NSHostingView(rootView: root)
            w.center()
            window = w
        }
        NSApp.activate(ignoringOtherApps: true)
        window?.makeKeyAndOrderFront(nil)
    }

    /// "Show me": Settings at the card's page, and this window goes.
    func showMe(_ card: WhatsNewCard) {
        close()
        SettingsWindowController.shared.show(card.destination)
    }
}

struct WhatsNewView: View {
    static let title = "What's New in Sanduhr"
    static let hideTitle = "Don't show after updates"

    let cards: [WhatsNewCard]
    /// "New in 2.4.0 – 2.6.0": the releases the cards cover, once for the window.
    let range: String
    let close: () -> Void
    @AppStorage(WhatsNew.hideKey) private var hideAfterUpdates = false

    var body: some View {
        VStack(spacing: 0) {
            header
            ScrollView { grid.padding(20) }
            Divider()
            footer
        }
        .frame(minWidth: 520, minHeight: 420)
        .background(VisualEffectView(material: .windowBackground, state: .followsWindowActiveState))
    }

    private var header: some View {
        HStack(spacing: 12) {
            Image(nsImage: NSApp.applicationIconImage)
                .resizable()
                .frame(width: 40, height: 40)
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 2) {
                Text(Self.title).font(.title2.bold())
                Text(range).foregroundStyle(.secondary)
            }
            Spacer()
        }
        .padding(.horizontal, 20)
        .padding(.top, 30)
        .padding(.bottom, 8)
    }

    @ViewBuilder
    private var grid: some View {
        if cards.isEmpty {
            Text("Nothing new to show for this version.")
                .foregroundStyle(.secondary)
                .frame(maxWidth: .infinity, minHeight: 120)
        } else {
            LazyVGrid(columns: [GridItem(.flexible(), spacing: 14, alignment: .top),
                                GridItem(.flexible(), spacing: 14, alignment: .top)],
                      alignment: .leading, spacing: 14) {
                ForEach(cards) { card in
                    WhatsNewCardView(card: card) { WhatsNewWindowController.shared.showMe(card) }
                }
            }
        }
    }

    private var footer: some View {
        HStack {
            Toggle(Self.hideTitle, isOn: $hideAfterUpdates)
                .toggleStyle(.checkbox)
                .help("What's New stays in Settings, About and the menus either way.")
            Spacer()
            Button("Done", action: close)
                .keyboardShortcut(.defaultAction)
        }
        .padding(.horizontal, 20)
        .padding(.vertical, 12)
    }
}

/// One card: the art, the title, a sentence or two and Show me. The releases are in the header.
struct WhatsNewCardView: View {
    let card: WhatsNewCard
    let showMe: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            WhatsNewArtView(art: card.art)
                .frame(maxWidth: .infinity)
                .frame(height: 96)
                .background(RoundedRectangle(cornerRadius: 8).fill(Color.primary.opacity(0.05)))
                .clipShape(RoundedRectangle(cornerRadius: 8))
            Text(card.title)
                .font(.headline)
                .fixedSize(horizontal: false, vertical: true)
            Text(card.body)
                .font(.callout)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            Spacer(minLength: 0)
            Button("Show me", action: showMe)
                .help("Opens Settings, \(card.destination.title)")
        }
        .padding(12)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .background(RoundedRectangle(cornerRadius: 10).fill(Color.primary.opacity(0.04)))
        .overlay(RoundedRectangle(cornerRadius: 10).strokeBorder(Color.secondary.opacity(0.2)))
    }
}

/// A card's art: its symbol, large, or one of the small live previews.
struct WhatsNewArtView: View {
    let art: WhatsNewArt

    var body: some View {
        switch art {
        case .symbol(let name):
            Image(systemName: name)
                .font(.system(size: 40, weight: .light))
                .foregroundStyle(Color.accentColor)
                .accessibilityHidden(true)
        case .preview(let preview):
            WhatsNewPreviewView(preview: preview)
                .accessibilityHidden(true)
        }
    }
}

/// The live previews, drawn with the app's own views where that is cheap.
struct WhatsNewPreviewView: View {
    let preview: WhatsNewPreview

    var body: some View {
        switch preview {
        case .deskMessage: deskMessage
        case .font: font
        case .nowPlaying: nowPlaying
        case .menuBar: menuBar
        }
    }

    /// The Desk's message line, with a two-color ink, a glow and the write-in.
    private var deskMessage: some View {
        ZStack {
            LinearGradient(colors: [Color.hex("1b2330"), Color.hex("0d1118")],
                           startPoint: .topLeading, endPoint: .bottomTrailing)
            DeskMessageLine(raw: "{ink:#ff2a6d,#05d9e8} {glow} {write} ship it.", font: BundledFonts.family,
                            baseSize: 30, inkSpec: "9ad7ff", globalGlow: true, alignment: .center,
                            paused: false, lineLimit: 1)
        }
    }

    /// EsteFont 26, both weights.
    private var font: some View {
        HStack(alignment: .firstTextBaseline, spacing: 10) {
            Text("Aa").font(.custom(BundledFonts.esteFont26.boldFace, size: 40))
            Text("hello, Desk").font(.custom(BundledFonts.esteFont26.regularFace, size: 26))
        }
        .foregroundStyle(Color.primary)
    }

    /// A notch wing playing a track.
    private var nowPlaying: some View {
        HStack(spacing: 0) {
            Text("▶ Title · Artist")
                .font(.system(size: 12, weight: .medium))
                .foregroundStyle(.white)
                .padding(.horizontal, 14)
                .frame(width: 150, height: 30, alignment: .leading)
            Color.black.frame(width: 44, height: 30)
        }
        .background(UnevenRoundedRectangle(bottomLeadingRadius: 12, bottomTrailingRadius: 12).fill(.black))
    }

    /// The hourglass and Rotate's two readings.
    private var menuBar: some View {
        VStack(spacing: 6) {
            reading("S 12%")
            reading("W 96%")
        }
    }

    private func reading(_ text: String) -> some View {
        HStack(spacing: 4) {
            Image(systemName: "hourglass")
            Text(text).font(.system(size: 13, weight: .medium).monospacedDigit())
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 3)
        .background(Capsule().fill(Color.primary.opacity(0.08)))
    }
}
