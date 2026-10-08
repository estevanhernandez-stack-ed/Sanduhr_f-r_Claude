import AppKit
import SwiftUI

/// The pages of the Settings window, in sidebar order (Settings v2, item 72, slice 2: sixteen
/// pages in five groups, each feature with one home).
///
/// Raw values are a public contract: `sanduhr://debug/action?name=settings&arg=<raw>`, the smoke
/// scenarios, state.yaml's `settings_section`, the tour's and What's New's Show me and the Desk
/// menu items. A page keeps its raw value when its title changes: `credentials` is Accounts,
/// `usage` is Usage, `integrations` is Claude Code, `mods` is Mods & Config, `deskLayout` is Desk
/// and `widgetLook` is Widget. The two retired pages parse as aliases (`resolve`).
enum SettingsSection: String, CaseIterable, Identifiable {
    case general, credentials, usage, alerts
    case deskLayout, deskLook, message, notch, nowPlaying, watchers
    case integrations, mods
    case widgetLook, themes
    case updates, about

    var id: String { rawValue }

    var title: String {
        switch self {
        case .general: "General"
        case .credentials: "Accounts"
        case .usage: "Usage"
        case .alerts: "Alerts"
        case .deskLayout: "Desk"
        case .deskLook: "Desk Look"
        case .message: "Message"
        case .notch: "Notch"
        case .nowPlaying: "Now Playing"
        case .watchers: "Watchers"
        case .integrations: "Claude Code"
        case .mods: "Mods & Config"
        case .widgetLook: "Widget"
        case .themes: "Themes"
        case .updates: "Updates"
        case .about: "About"
        }
    }

    var symbol: String {
        switch self {
        case .general: "gearshape"
        case .credentials: "person.2"
        case .usage: "chart.bar.xaxis"
        case .alerts: "bell"
        case .deskLayout: "rectangle.3.group"
        case .deskLook: "paintbrush"
        case .message: "text.quote"
        case .notch: "rectangle.topthird.inset.filled"
        case .nowPlaying: "music.note"
        case .watchers: "eye"
        case .integrations: "puzzlepiece.extension"
        case .mods: "cube"
        case .widgetLook: "textformat"
        case .themes: "paintpalette"
        case .updates: "arrow.triangle.2.circlepath"
        case .about: "info.circle"
        }
    }

    /// Sidebar groups: a header (nil for the first) and its pages.
    static let groups: [(header: String?, sections: [SettingsSection])] = [
        (nil, [.general, .credentials, .usage, .alerts]),
        ("Desktop", [.deskLayout, .deskLook, .message, .notch, .nowPlaying, .watchers]),
        ("Claude Code", [.integrations, .mods]),
        ("Widget", [.widgetLook, .themes]),
        ("Help", [.updates, .about]),
    ]

    /// Raw values of pages that merged into another (slice 2), with the page and the anchor they
    /// open now. They still parse, so old links and scenarios keep working.
    static let aliases: [String: (section: SettingsSection, anchor: String)] = [
        "deskMeters": (.alerts, SettingsAnchor.eachLimit),
        "pacing": (.widgetLook, SettingsAnchor.pacing),
    ]

    /// A raw value or an alias as the page and anchor it opens, nil for neither.
    static func resolve(_ raw: String) -> (section: SettingsSection, anchor: String?)? {
        if let s = SettingsSection(rawValue: raw) { return (s, nil) }
        if let a = aliases[raw] { return (a.section, a.anchor) }
        return nil
    }
}

/// Anchors inside a page: a section a link or an alias opens the page at. Slice 3 gives every
/// section one; these are the ones links use today.
enum SettingsAnchor {
    /// Alerts, Each limit (was the Meters page, raw value `deskMeters`).
    static let eachLimit = "each-limit"
    /// Widget, Pacing calculators (was the Pacing & Focus page, raw value `pacing`).
    static let pacing = "pacing"
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
    /// The anchor the page was last opened at, nil for its top (state.yaml `settings_anchor`).
    var anchor: String? { navigation.anchor }
    /// The Claude Usage page's tab (state.yaml `usage_page.tab`).
    var usageTab: UsageTab { navigation.usageTab }
    /// The Mods page's state (state.yaml `mods_page`).
    var modsPage: ModsPageModel { navigation.modsPage }
    /// Settings, Message's editor (state.yaml `message_editor`, the debug hooks' `message-editor`).
    var messageEditor: MessageEditorModel { navigation.messageEditor }

    func close() { window?.close() }

    /// Shows the window at `section`, or where it was left (General the first time), and
    /// brings it forward. One window, reused.
    func show(_ section: SettingsSection? = nil, anchor: String? = nil, usageTab: UsageTab? = nil) {
        if let section {
            navigation.selection = section
            navigation.anchor = anchor
        }
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

/// A button that opens Settings at a page, titled "<Page> Settings…" with the page's exact sidebar
/// title (Settings v2, slice 1). `before` runs first, for a sheet that closes on the way.
/// The smoke action `settings-link "<title>"` opens the same page the same way.
struct SettingsLinkButton: View {
    let section: SettingsSection
    var anchor: String?
    var before: () -> Void = {}

    init(_ section: SettingsSection, anchor: String? = nil, before: @escaping () -> Void = {}) {
        self.section = section
        self.anchor = anchor
        self.before = before
    }

    var body: some View {
        Button(section.linkTitle) {
            before()
            SettingsWindowController.shared.show(section, anchor: anchor)
        }
    }
}

/// The selected section, kept outside the view so `show(_:)` can move it on an open window.
@MainActor
@Observable
final class SettingsNavigation {
    var selection: SettingsSection = .general {
        didSet { if selection != oldValue { anchor = nil } }
    }
    /// Where on the page it was opened (SettingsAnchor), nil for the top.
    var anchor: String?
    /// The Claude Usage page's tab, kept while other sections show.
    var usageTab: UsageTab = .overview
    /// An account for Accounts to select on arrival (the Claude Usage page's "Accounts Settings…").
    var accountToShow: String?
    /// The Claude Usage page's state, kept while the window lives.
    let usagePage = UsagePageModel()
    /// The Mods page's state (item 64), kept while the window lives.
    let modsPage = ModsPageModel()
    /// Settings, Message's line editor (item 69), kept while the window lives.
    let messageEditor = MessageEditorModel()
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
        // SettingsPreviewKind); Claude Code draws its own, which needs the page's model.
        switch navigation.selection {
        case .general: GeneralSection().withPreview { MenuBarPreview(vm: vm) }
        case .deskLayout: DeskLayoutSection().withPreview { DeskLayoutPreview(live: deskModel) }
        case .deskLook: DeskLookSection().withPreview { DeskLookPreview(live: deskModel) }
        case .message: DeskMessageSection(model: deskModel, editor: navigation.messageEditor).withPreview { DeskMessagePreview(live: deskModel) }
        case .notch: DeskNotchSection().withPreview { NotchPreview(live: deskModel) }
        case .nowPlaying: NowPlayingSection().withPreview { NowPlayingPreview(live: deskModel) }
        case .watchers: WatchersSection().withPreview { WatchersPreview(live: deskModel) }
        case .updates: UpdatesSection(updates: updates)
        case .about: AboutSection()
        case .credentials: AccountsSettings(vm: vm, navigation: navigation)
        case .usage: UsageSettings(vm: vm, navigation: navigation, theme: vm.theme.palette)
        case .integrations: IntegrationsSettings(vm: vm, navigation: navigation)
        case .mods:
            ModsSettings(vm: vm, model: navigation.modsPage).withPreview { ModsSummaryCard(model: navigation.modsPage) }
        case .widgetLook:
            WidgetSettings(vm: vm, section: .widgetLook, anchor: navigation.anchor).id(navigation.selection)
                .withPreview { WidgetPreview(vm: vm) }
        case .alerts:
            // Notifications, then Each limit (was Desk, Meters) under the meter bars' preview.
            WidgetSettings(vm: vm, section: .alerts, deskModel: deskModel, anchor: navigation.anchor)
                .id(navigation.selection)
                .withPreview { DeskMetersPreview(live: deskModel) }
        case .themes:
            // A fresh view per section, so a section's unsaved fields start empty.
            WidgetSettings(vm: vm, section: navigation.selection).id(navigation.selection)
        }
    }
}
