import AppKit
import SwiftUI

/// The sections of the Settings window, in sidebar order.
///
/// `credentials` is the Accounts page (item 36 replaced Settings, Credentials with it). The case
/// and its raw value stay, so `sanduhr://debug/action?name=settings&arg=credentials`, the smoke
/// scenarios and state.yaml's `settings_section` keep working unchanged.
enum SettingsSection: String, CaseIterable, Identifiable {
    case general, alerts, credentials, usage, integrations
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
    /// Claude, and its consent points back at Accounts.
    static let groups: [(header: String?, sections: [SettingsSection])] = [
        (nil, [.general, .alerts, .credentials, .usage, .integrations]),
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
            w.contentView = NSHostingView(rootView: SettingsRoot(
                vm: app.viewModel, deskModel: DeskController.shared.model, updates: app.updates,
                navigation: navigation))
            w.center()
            window = w
        }
        NSApp.activate(ignoringOtherApps: true)
        window?.makeKeyAndOrderFront(nil)
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
            Label(s.title, systemImage: s.symbol).tag(s)
                // A suggestion from Claude waits on Message (item 54).
                .badge(s == .message && DeskMessageHandoff.shared.pending != nil ? 1 : 0)
        }
    }

    @ViewBuilder
    private var detail: some View {
        switch navigation.selection {
        case .general: GeneralSection()
        case .deskLayout: DeskLayoutSection()
        case .deskLook: DeskLookSection()
        case .deskMeters: DeskMetersSection(model: deskModel)
        case .message: DeskMessageSection(model: deskModel).padding(20)
        case .notch: DeskNotchSection()
        case .nowPlaying: NowPlayingSection()
        case .updates: UpdatesSection(updates: updates)
        case .about: AboutSection()
        case .credentials: AccountsSettings(vm: vm, navigation: navigation)
        case .usage: UsageSettings(vm: vm, navigation: navigation, theme: vm.theme.palette)
        case .integrations: IntegrationsSettings(vm: vm, navigation: navigation)
        case .widgetLook, .themes, .pacing, .alerts:
            // A fresh view per section, so a section's unsaved fields start empty.
            WidgetSettings(vm: vm, section: navigation.selection).id(navigation.selection)
        }
    }
}
