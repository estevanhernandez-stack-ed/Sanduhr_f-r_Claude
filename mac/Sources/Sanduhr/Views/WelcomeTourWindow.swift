import AppKit
import SwiftUI

/// Which step the tour window is on, and the steps it shows.
@Observable
@MainActor
final class TourNavigation {
    var steps: [[TourCard]] = []
    var index = 0

    var count: Int { steps.count }
    var isLast: Bool { index >= count - 1 }
    var cards: [TourCard] { steps.indices.contains(index) ? steps[index] : [] }
}

/// The welcome tour window (item 61): What's New's window and card look, one step at a time.
/// Shown once after a fresh install's first successful fetch (AppDelegate), and any time from
/// Take the Tour… in About and the menus. Show me opens its page and leaves the tour open.
@MainActor
final class WelcomeTourWindowController: NSObject, NSWindowDelegate {
    static let shared = WelcomeTourWindowController()
    private(set) var window: NSWindow?
    let navigation = TourNavigation()
    /// Set while the window closes by Finish, Skip or a debug hook, so the close button alone
    /// counts as Skip.
    private var closingOnPurpose = false

    var isOpen: Bool { window?.isVisible ?? false }

    /// This Mac's features for the cards that need one.
    static func features() -> Set<TourFeature> {
        NSScreen.screens.contains { $0.cameraNotch != nil } ? [.notch] : []
    }

    /// Opens the tour at `step` (0-based, clamped), with the current settings.
    func show(step: Int = 0) {
        navigation.steps = WelcomeTour.steps(features: Self.features())
        navigation.index = max(0, min(step, navigation.count - 1))
        if window == nil {
            let w = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 640, height: 600),
                             styleMask: [.titled, .closable, .fullSizeContentView],
                             backing: .buffered, defer: false)
            w.title = WelcomeTourView.title
            w.titleVisibility = .hidden
            w.titlebarAppearsTransparent = true
            w.isMovableByWindowBackground = true
            w.isReleasedWhenClosed = false
            w.delegate = self
            w.contentView = NSHostingView(rootView: WelcomeTourView(nav: navigation, controller: self))
            w.center()
            window = w
        }
        NSApp.activate(ignoringOtherApps: true)
        window?.makeKeyAndOrderFront(nil)
    }

    func next() {
        if navigation.isLast { finish() } else { navigation.index += 1 }
    }

    func back() {
        if navigation.index > 0 { navigation.index -= 1 }
    }

    func finish() { end(finished: true) }

    /// Skip the Tour, Escape or the close button: every setting stays as it is.
    func skip() { end(finished: false) }

    /// Closes without recording anything (the debug hook).
    func close() {
        closingOnPurpose = true
        window?.close()
        closingOnPurpose = false
    }

    private func end(finished: Bool) {
        WelcomeTour.end(finished, current: AppInfo.current.version)
        close()
    }

    func windowWillClose(_ notification: Notification) {
        guard !closingOnPurpose else { return }
        WelcomeTour.end(false, current: AppInfo.current.version)
    }

    /// Show me: the widget, or Settings at the card's page. The tour stays open behind it.
    func showMe(_ card: TourCard) {
        switch card.showMe {
        case .widget: (NSApp.delegate as? AppDelegate)?.showPanel()
        case .settings(let section): SettingsWindowController.shared.show(section)
        }
    }
}

struct WelcomeTourView: View {
    static let title = "Welcome to Sanduhr"
    static let skipTitle = "Skip the Tour"

    var nav: TourNavigation
    let controller: WelcomeTourWindowController

    var body: some View {
        VStack(spacing: 0) {
            header
            ScrollView { cards.padding(20) }
            Text(AppInfo.independence)
                .font(.caption)
                .foregroundStyle(.secondary)
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(.horizontal, 20)
                .padding(.bottom, 8)
            Divider()
            footer
        }
        .frame(minWidth: 560, minHeight: 520)
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
                Text(WelcomeTour.stepText(nav.index, of: nav.count))
                    .foregroundStyle(.secondary)
                    .accessibilityLabel("Step \(nav.index + 1) of \(nav.count)")
            }
            Spacer()
        }
        .padding(.horizontal, 20)
        .padding(.top, 30)
        .padding(.bottom, 8)
    }

    private var cards: some View {
        let shown = nav.cards
        let columns = Array(repeating: GridItem(.flexible(), spacing: 14, alignment: .top),
                            count: max(1, min(2, shown.count)))
        return LazyVGrid(columns: columns, alignment: .leading, spacing: 14) {
            ForEach(shown) { card in
                TourCardView(card: card) { controller.showMe(card) }
            }
        }
    }

    private var footer: some View {
        HStack(spacing: 10) {
            Button(Self.skipTitle) { controller.skip() }
                .keyboardShortcut(.cancelAction)
                .help("Close the tour; every setting stays as it is")
            if nav.isLast {
                Text(WelcomeTour.againLine)
                    .font(.callout)
                    .foregroundStyle(.secondary)
                    .lineLimit(2)
            }
            Spacer()
            Button("Back") { controller.back() }
                .disabled(nav.index == 0)
            Button(nav.isLast ? "Finish" : "Next") { controller.next() }
                .keyboardShortcut(.defaultAction)
        }
        .padding(.horizontal, 20)
        .padding(.vertical, 12)
    }
}

/// One tour card: What's New's card with the step's choices above Show me.
struct TourCardView: View {
    let card: TourCard
    let showMe: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            TourArtView(art: card.art)
                .frame(maxWidth: .infinity)
                .frame(height: 120)
                .background(RoundedRectangle(cornerRadius: 8).fill(Color.primary.opacity(0.05)))
                .clipShape(RoundedRectangle(cornerRadius: 8))
            Text(card.title)
                .font(.headline)
                .fixedSize(horizontal: false, vertical: true)
            Text(card.body)
                .font(.callout)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            ForEach(card.choices, id: \.self) { TourChoiceView(choice: $0) }
            Spacer(minLength: 0)
            Button("Show me", action: showMe)
                .help(Self.showMeHelp(card.showMe))
        }
        .padding(12)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .background(RoundedRectangle(cornerRadius: 10).fill(Color.primary.opacity(0.04)))
        .overlay(RoundedRectangle(cornerRadius: 10).strokeBorder(Color.secondary.opacity(0.2)))
    }

    static func showMeHelp(_ destination: TourDestination) -> String {
        switch destination {
        case .widget: "Shows the widget"
        case .settings(let section): "Opens Settings, \(section.title)"
        }
    }
}

/// A card's choice: the real setting, written as it changes, so Back shows it as made.
struct TourChoiceView: View {
    let choice: TourChoice
    @AppStorage(DeskController.enabledKey, store: .desk) private var deskEnabled = false
    @AppStorage(MenuBarMode.key) private var menuBarMode = MenuBarMode.higher
    @AppStorage("theme") private var themeID = ""

    var body: some View {
        switch choice {
        case .desk:
            Toggle("Show the Desk", isOn: Binding(
                get: { deskEnabled },
                set: { on in
                    guard on != deskEnabled else { return }
                    WelcomeTour.setDesk(on)
                    DeskController.shared.apply()
                }))
        case .matchDesk:
            Toggle("Widget theme: Match Desk", isOn: Binding(
                get: { WelcomeTour.matchDesk(themeID: themeID) },
                set: { on in
                    guard let vm = (NSApp.delegate as? AppDelegate)?.viewModel else { return }
                    let id = WelcomeTour.theme(matchDesk: on, current: vm.theme.id,
                                               fallback: ThemeRegistry.default.id)
                    if id != vm.theme.id { vm.selectTheme(id: id) }
                }))
            .help("Off goes back to the theme you had before")
        case .menuBar:
            Picker(SettingsNames.menuBarShows, selection: Binding(
                get: { menuBarMode },
                set: { mode in
                    guard mode != menuBarMode else { return }
                    MenuBarMode.save(mode)
                    (NSApp.delegate as? AppDelegate)?.menuBarModeDidChange()
                })) {
                ForEach(MenuBarMode.allCases) { Text($0.label).tag($0) }
            }
        }
    }
}

/// A card's art: its symbol, large, or one of the previews.
struct TourArtView: View {
    let art: TourArt

    var body: some View {
        switch art {
        case .symbol(let name):
            Image(systemName: name)
                .font(.system(size: 44, weight: .light))
                .foregroundStyle(Color.accentColor)
                .accessibilityHidden(true)
        case .preview(let preview):
            TourPreviewView(preview: preview)
        }
    }
}
