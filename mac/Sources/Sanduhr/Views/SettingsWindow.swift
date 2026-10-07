import AppKit
import SwiftUI

/// The sections of the Settings window, in sidebar order.
///
/// `credentials` is the Accounts page (item 36 replaced Settings, Credentials with it). The case
/// and its raw value stay, so `sanduhr://debug/action?name=settings&arg=credentials`, the smoke
/// scenarios and state.yaml's `settings_section` keep working unchanged.
enum SettingsSection: String, CaseIterable, Identifiable {
    case general, alerts, credentials, usage, integrations, mods
    case deskLayout, deskLook, deskMeters, message, notch, nowPlaying
    case widgetLook, themes, pacing
    case updates, about

    var id: String { rawValue }

    var title: String {
        switch self {
        case .general: "General"
        case .alerts: "Alerts"
        case .credentials: "Accounts"
        case .usage: "Claude Usage"
        case .integrations: "Integrations"
        case .mods: "Mods"
        case .deskLayout: "Layout"
        case .deskLook: "Look"
        case .deskMeters: "Meters"
        case .message: "Message"
        case .notch: "Notch"
        case .nowPlaying: "Now Playing"
        case .widgetLook: "Look"
        case .themes: "Themes"
        case .pacing: "Pacing & Focus"
        case .updates: "Updates"
        case .about: "About"
        }
    }

    var symbol: String {
        switch self {
        case .general: "gearshape"
        case .alerts: "bell"
        case .credentials: "person.2"
        case .usage: "chart.bar.xaxis"
        case .integrations: "puzzlepiece.extension"
        case .mods: "cube"
        case .deskLayout: "rectangle.3.group"
        case .deskLook: "textformat"
        case .deskMeters: "gauge.with.dots.needle.67percent"
        case .message: "text.quote"
        case .notch: "rectangle.topthird.inset.filled"
        case .nowPlaying: "music.note"
        case .widgetLook: "textformat"
        case .themes: "paintpalette"
        case .pacing: "speedometer"
        case .updates: "arrow.triangle.2.circlepath"
        case .about: "info.circle"
        }
    }

    /// Sidebar groups: a header (nil for the first) and its sections. Claude Usage (item 48)
    /// sits under Accounts: it is per account, and its setup lives in each account's Data.
    /// Integrations (item 49) follows: installing the MCP server is the other half of Share with
    /// Claude, and its consent points back at Accounts. Mods (item 64) follows Integrations:
    /// Sanduhr's meters mod is one of the mods it lists.
    static let groups: [(header: String?, sections: [SettingsSection])] = [
        (nil, [.general, .alerts, .credentials, .usage, .integrations, .mods]),
        ("Desk", [.deskLayout, .deskLook, .deskMeters, .message, .notch, .nowPlaying]),
        ("Widget", [.widgetLook, .themes, .pacing]),
        ("Sanduhr", [.updates, .about]),
    ]
}

/// The one Settings window: every Sanduhr, Desk and widget setting, reachable with the widget
/// hidden. Option+S, sanduhr:// and estedesk:// settings links, the notch island, every menu's
/// Settings… item and the widget's gear all open it; onboarding opens it at Accounts.
@MainActor
final class SettingsWindowController {
    static let shared = SettingsWindowController()
    private(set) var window: NSWindow?
    private let navigation = SettingsNavigation()

    /// The window is on screen.
    var isOpen: Bool { window?.isVisible ?? false }
    /// The section showing, or the one it reopens at.
    var section: SettingsSection { navigation.selection }
    /// The Claude Usage page's tab (state.yaml `usage_page.tab`).
    var usageTab: UsageTab { navigation.usageTab }
    /// The Mods page's state (state.yaml `mods_page`).
    var modsPage: ModsPageModel { navigation.modsPage }

    func close() { window?.close() }

    /// Shows the window at `section`, or where it was left (General the first time), and
    /// brings it forward. One window, reused.
    func show(_ section: SettingsSection? = nil, usageTab: UsageTab? = nil) {
        if let section { navigation.selection = section }
        if let usageTab { navigation.usageTab = usageTab }
        // A Calendar grant made in System Settings shows here and on the Desk.
        DeskController.shared.recheckCalendar()
        if window == nil, let app = NSApp.delegate as? AppDelegate {
            let w = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 760, height: 600),
                             styleMask: [.titled, .closable, .miniaturizable, .resizable],
                             backing: .buffered, defer: false)
            w.title = "Sanduhr Settings"
            w.isReleasedWhenClosed = false
            w.minSize = NSSize(width: 640, height: 480)
            let host = NSHostingView(rootView: SettingsRoot(
                vm: app.viewModel, deskModel: DeskController.shared.model, updates: app.updates,
                navigation: navigation))
            // The window keeps the size it has: left to SwiftUI, a page's natural height (the
            // preview cards, item 68) grew it past the screen with no way to scroll or close it
            // (2026-10-07). Each page scrolls inside the window instead.
            host.sizingOptions = []
            w.contentView = host
            w.center()
            window = w
        }
        if let w = window { Self.fitToScreen(w) }
        NSApp.activate(ignoringOtherApps: true)
        window?.makeKeyAndOrderFront(nil)
    }

    /// Keeps the window, title bar included, on the screen it is on: never taller or wider than
    /// the visible frame, moved back inside it when it hangs off an edge.
    static func fitToScreen(_ w: NSWindow) {
        guard let visible = (w.screen ?? NSScreen.main)?.visibleFrame else { return }
        var f = w.frame
        f.size.width = min(f.width, visible.width)
        f.size.height = min(f.height, visible.height)
        f.origin.x = min(max(f.minX, visible.minX), visible.maxX - f.width)
        f.origin.y = min(max(f.minY, visible.minY), visible.maxY - f.height)
        if f != w.frame { w.setFrame(f, display: true) }
    }
}

/// The selected section, kept outside the view so `show(_:)` can move it on an open window.
@MainActor
@Observable
final class SettingsNavigation {
    var selection: SettingsSection = .general
    /// The Claude Usage page's tab, kept while other sections show.
    var usageTab: UsageTab = .overview
    /// An account for Accounts to select on arrival (the Claude Usage page's "Data Settings…").
    var accountToShow: String?
    /// The Claude Usage page's state, kept while the window lives.
    let usagePage = UsagePageModel()
    /// The Mods page's state (item 64), kept while the window lives.
    let modsPage = ModsPageModel()
}

/// Sidebar on the left, the selected section on the right.
struct SettingsRoot: View {
    @Bindable var vm: UsageViewModel
    var deskModel: DeskModel
    var updates: UpdaterSettings
    @Bindable var navigation: SettingsNavigation

    var body: some View {
        HStack(spacing: 0) {
            List(selection: Binding(
                get: { navigation.selection },
                set: { if let s = $0 { navigation.selection = s } })) {
                ForEach(SettingsSection.groups.indices, id: \.self) { i in
                    let group = SettingsSection.groups[i]
                    if let header = group.header {
                        Section(header) { rows(group.sections) }
                    } else {
                        rows(group.sections)
                    }
                }
            }
            .listStyle(.sidebar)
            .frame(width: 190)

            Divider()

            detail
                .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
        .frame(minWidth: 640, minHeight: 480)
    }

    private func rows(_ sections: [SettingsSection]) -> some View {
        ForEach(sections) { s in
            // A drawn dot rather than List's .badge: with a badge showing, the sidebar stopped
            // taking clicks (2026-10-04). The row stays a plain Label, tagged for selection.
            HStack(spacing: 6) {
                Label(s.title, systemImage: s.symbol)
                Spacer(minLength: 0)
                if Self.badge(s) > 0 {
                    Text("\(Self.badge(s))")
                        .font(.caption2.weight(.semibold))
                        .foregroundStyle(.white)
                        .padding(.horizontal, 6)
                        .padding(.vertical, 1)
                        .background(Capsule().fill(Color.accentColor))
                        .accessibilityLabel("Suggestion waiting")
                }
            }
            .tag(s)
        }
    }

    /// A suggestion from Claude waits on Message (item 54) or Themes (item 55).
    @MainActor static func badge(_ s: SettingsSection) -> Int {
        switch s {
        case .message: DeskMessageHandoff.shared.pending != nil ? 1 : 0
        case .themes: ThemeProposalHandoff.shared.pending != nil ? 1 : 0
        default: 0
        }
    }

    @ViewBuilder
    private var detail: some View {
        // Each pane that controls something visible has its live preview on top (item 68,
        // SettingsPreviewKind); Integrations draws its own, which needs the page's model.
        switch navigation.selection {
        case .general: GeneralSection().withPreview { MenuBarPreview(vm: vm) }
        case .deskLayout: DeskLayoutSection().withPreview { DeskLayoutPreview(live: deskModel) }
        case .deskLook: DeskLookSection().withPreview { DeskLookPreview(live: deskModel) }
        case .deskMeters: DeskMetersSection(model: deskModel).withPreview { DeskMetersPreview(live: deskModel) }
        case .message: DeskMessageSection(model: deskModel).padding(20).withPreview { DeskMessagePreview(live: deskModel) }
        case .notch: DeskNotchSection().withPreview { NotchPreview(live: deskModel) }
        case .nowPlaying: NowPlayingSection().withPreview { NowPlayingPreview(live: deskModel) }
        case .updates: UpdatesSection(updates: updates)
        case .about: AboutSection()
        case .credentials: AccountsSettings(vm: vm, navigation: navigation)
        case .usage: UsageSettings(vm: vm, navigation: navigation, theme: vm.theme.palette)
        case .integrations: IntegrationsSettings(vm: vm, navigation: navigation)
        case .mods:
            ModsSettings(vm: vm, model: navigation.modsPage).withPreview { ModsSummaryCard(model: navigation.modsPage) }
        case .widgetLook, .pacing:
            WidgetSettings(vm: vm, section: navigation.selection).id(navigation.selection)
                .withPreview { WidgetPreview(vm: vm) }
        case .themes, .alerts:
            // A fresh view per section, so a section's unsaved fields start empty.
            WidgetSettings(vm: vm, section: navigation.selection).id(navigation.selection)
        }
    }
}
