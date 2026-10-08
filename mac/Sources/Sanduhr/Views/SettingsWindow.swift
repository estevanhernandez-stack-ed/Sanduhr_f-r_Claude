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

    /// The anchor the page was opened at is on screen (state.yaml `settings_anchor_visible`), nil
    /// with no anchor or the window closed.
    var anchorVisible: Bool? { isOpen ? navigation.anchorVisible : nil }
    /// The page's preview is folded to its strip (state.yaml `settings_preview_folded`), nil for a
    /// page without a preview or the window closed.
    var previewFolded: Bool? {
        guard isOpen, SettingsPreviewKind.of(navigation.selection) != nil else { return nil }
        return navigation.previewFolds && !navigation.openPreviews.contains(navigation.selection)
    }

    /// Shows the window at `section`, or where it was left (General the first time), scrolled to
    /// `anchor` (opening its Advanced disclosure), and brings it forward. One window, reused.
    /// `highlight` lights the anchor for 1.5 s, as a search pick does.
    func show(_ section: SettingsSection? = nil, anchor: String? = nil, usageTab: UsageTab? = nil,
              highlight: Bool = false) {
        if let section { navigation.open(section, anchor: anchor, highlight: highlight) }
        if let usageTab { navigation.usageTab = usageTab }
        // A Calendar grant made in System Settings shows here and on the Desk.
        DeskController.shared.recheckCalendar()
        if window == nil, let app = NSApp.delegate as? AppDelegate {
            let w = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 760, height: SettingsPreviewFold.defaultWindowHeight),
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
            // The previews fold below 600 pt (slice 3): the page reads the window's height.
            let nav = navigation
            NotificationCenter.default.addObserver(forName: NSWindow.didResizeNotification, object: w, queue: .main) { note in
                let height = (note.object as? NSWindow)?.frame.height ?? 0
                MainActor.assumeIsolated { nav.windowHeight = height }
            }
            // Closing the window folds what was opened on it: the next open starts as documented
            // (previews folded on a short window, Advanced collapsed).
            NotificationCenter.default.addObserver(forName: NSWindow.willCloseNotification, object: w, queue: .main) { _ in
                MainActor.assumeIsolated { nav.windowClosed() }
            }
        }
        if let w = window {
            Self.fitToScreen(w)
            navigation.windowHeight = w.frame.height
        }
        NSApp.activate(ignoringOtherApps: true)
        window?.makeKeyAndOrderFront(nil)
    }

    /// The Message page's Add Line (and the smoke's `message-editor add`): the new row scrolled
    /// into view with its editor open, wherever the list was scrolled.
    func revealNewLine() {
        navigation.open(.message, anchor: SettingsAnchor.newLine, highlight: false)
    }

    /// The sidebar search with `query` typed and Return pressed (the smoke's `settings-search`):
    /// the window opens at the best match, scrolled to it and lit.
    func search(_ query: String) {
        navigation.searchText = query
        navigation.openFirstHit(query)
        show()
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
        didSet {
            guard selection != oldValue else { return }
            anchor = nil
            highlight = nil
            anchorFrames = [:]
        }
    }
    /// Where on the page it was opened (SettingsAnchor), nil for the top.
    var anchor: String?
    /// Bumped each time a page is opened at an anchor: the page scrolls there, even when it was
    /// already showing at the same anchor.
    private(set) var scrollRequest = 0
    /// The anchor a search pick lights for 1.5 s.
    private(set) var highlight: String?
    /// Pages whose Advanced disclosure is open, kept until the window closes.
    var openAdvanced: Set<SettingsSection> = []
    /// Pages whose folded preview was opened, kept until the window closes (slice 3).
    var openPreviews: Set<SettingsSection> = []
    /// The window's height; below 600 pt the previews fold.
    var windowHeight: CGFloat = 0
    /// The sidebar's search field.
    var searchText = ""
    /// Where each marked anchor is on screen, and the page's scrolling area, in the window's
    /// coordinates (SettingsReach). Read only for state.yaml, so changes redraw nothing.
    @ObservationIgnored var anchorFrames: [String: CGRect] = [:]
    @ObservationIgnored var viewport: CGRect?
    @ObservationIgnored private var highlightClear: DispatchWorkItem?

    /// Below 600 pt of window height the previews fold to a strip.
    var previewFolds: Bool { SettingsPreviewFold.folds(windowHeight: windowHeight) }

    /// The anchor the page was opened at is inside the page's scrolling area; nil without one.
    var anchorVisible: Bool? {
        guard let anchor else { return nil }
        guard let frame = anchorFrames[anchor], let viewport else { return false }
        return SettingsAnchorVisibility.isVisible(frame, in: viewport)
    }

    /// The window closed: opened previews and Advanced disclosures fold again for the next open.
    func windowClosed() {
        openPreviews = []
        openAdvanced = []
    }

    /// Opens `section` at `anchor`: an anchor under Advanced opens the disclosure first, and the
    /// page scrolls there. `highlight` lights it for 1.5 s.
    func open(_ section: SettingsSection, anchor: String?, highlight lit: Bool = false) {
        selection = section
        self.anchor = anchor
        guard let anchor else { return }
        if SettingsAnchor.isAdvanced(anchor, on: section) { openAdvanced.insert(section) }
        scrollRequest += 1
        highlightClear?.cancel()
        highlight = lit ? anchor : nil
        guard lit else { return }
        let clear = DispatchWorkItem { [weak self] in
            MainActor.assumeIsolated { if self?.highlight == anchor { self?.highlight = nil } }
        }
        highlightClear = clear
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.5, execute: clear)
    }

    /// The search field's Return: the best hit, opened and lit. False when nothing matches.
    @discardableResult
    func openFirstHit(_ query: String) -> Bool {
        guard let hit = SettingsSearch.hits(query).first?.entry else { return false }
        open(hit.page, anchor: hit.anchor, highlight: hit.anchor != nil)
        return true
    }
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
            SettingsSidebar(navigation: navigation)
                .frame(width: 190)

            Divider()

            ScrollViewReader { proxy in
                detail
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
                    .onAppear { scroll(proxy) }
                    .onChange(of: navigation.scrollRequest) { _, _ in scroll(proxy) }
            }
        }
        .frame(minWidth: 640, minHeight: 480)
        .environment(navigation)
    }

    /// Brings the page's anchor into view, at the top of its scrolling area. Twice: a page that
    /// just appeared, or an Advanced disclosure that just opened, may not have its rows yet.
    /// Never animated: inside `withAnimation`, `scrollTo` does nothing on a grouped `Form` (macOS
    /// 26, 2026-10-08), so links, search and the smoke hook left every Form page at its top.
    private func scroll(_ proxy: ScrollViewProxy) {
        guard let anchor = navigation.anchor else { return }
        for delay in [0.05, 0.3] {
            DispatchQueue.main.asyncAfter(deadline: .now() + delay) {
                guard navigation.anchor == anchor else { return }
                proxy.scrollTo(anchor, anchor: .top)
            }
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
        // SettingsPreviewKind), folding to a strip on a short window (slice 3); Claude Code draws
        // its own, which needs the page's model. A page without a preview is its own viewport.
        switch navigation.selection {
        case .general: GeneralSection().withPreview(.general) { MenuBarPreview(vm: vm) }
        case .deskLayout: DeskLayoutSection().withPreview(.deskLayout) { DeskLayoutPreview(live: deskModel) }
        case .deskLook: DeskLookSection().withPreview(.deskLook) { DeskLookPreview(live: deskModel) }
        case .message: DeskMessageSection(model: deskModel, editor: navigation.messageEditor).withPreview(.message) { DeskMessagePreview(live: deskModel) }
        case .notch: DeskNotchSection().withPreview(.notch) { NotchPreview(live: deskModel) }
        case .nowPlaying: NowPlayingSection().withPreview(.nowPlaying) { NowPlayingPreview(live: deskModel) }
        case .watchers: WatchersSection().withPreview(.watchers) { WatchersPreview(live: deskModel) }
        case .updates: UpdatesSection(updates: updates).settingsViewport()
        case .about: AboutSection().settingsViewport()
        case .credentials: AccountsSettings(vm: vm, navigation: navigation).settingsViewport()
        case .usage: UsageSettings(vm: vm, navigation: navigation, theme: vm.theme.palette).settingsViewport()
        case .integrations: IntegrationsSettings(vm: vm, navigation: navigation).settingsViewport()
        case .mods:
            ModsSettings(vm: vm, model: navigation.modsPage).withPreview(.mods) { ModsSummaryCard(model: navigation.modsPage) }
        case .widgetLook:
            WidgetSettings(vm: vm, section: .widgetLook).id(navigation.selection)
                .withPreview(.widgetLook) { WidgetPreview(vm: vm) }
        case .alerts:
            // Notifications, then Each limit (was Desk, Meters) under the meter bars' preview.
            WidgetSettings(vm: vm, section: .alerts, deskModel: deskModel)
                .id(navigation.selection)
                .withPreview(.alerts) { DeskMetersPreview(live: deskModel) }
        case .themes:
            // A fresh view per section, so a section's unsaved fields start empty.
            WidgetSettings(vm: vm, section: navigation.selection).id(navigation.selection).settingsViewport()
        }
    }
}

/// The sidebar: the search field over the pages (slice 3). With a query, the pages that match,
/// each with its matching sections and controls under it; choosing one opens the page scrolled to
/// it and lit for 1.5 s, Return opens the best match, Escape clears.
private struct SettingsSidebar: View {
    @Bindable var navigation: SettingsNavigation

    var body: some View {
        VStack(spacing: 0) {
            SettingsSearchField(navigation: navigation)
                .padding(.horizontal, 10)
                .padding(.top, 10)
                .padding(.bottom, 4)
            List(selection: Binding(
                get: { navigation.selection },
                set: { if let s = $0 { navigation.selection = s } })) {
                if navigation.searchText.trimmingCharacters(in: .whitespaces).isEmpty {
                    pages
                } else {
                    SettingsSearchResults(navigation: navigation)
                }
            }
            .listStyle(.sidebar)
        }
    }

    private var pages: some View {
        ForEach(SettingsSection.groups.indices, id: \.self) { i in
            let group = SettingsSection.groups[i]
            if let header = group.header {
                Section(header) { SettingsSidebarRows(sections: group.sections) }
            } else {
                SettingsSidebarRows(sections: group.sections)
            }
        }
    }
}

private struct SettingsSearchField: View {
    @Bindable var navigation: SettingsNavigation
    @FocusState private var focused: Bool

    var body: some View {
        HStack(spacing: 4) {
            Image(systemName: "magnifyingglass").foregroundStyle(.secondary).accessibilityHidden(true)
            TextField("Search Settings", text: $navigation.searchText)
                .textFieldStyle(.plain)
                .focused($focused)
                .onSubmit { navigation.openFirstHit(navigation.searchText) }
                .onExitCommand { navigation.searchText = "" }
                .accessibilityLabel("Search Settings")
            if !navigation.searchText.isEmpty {
                Button { navigation.searchText = "" } label: { Image(systemName: "xmark.circle.fill") }
                    .buttonStyle(.borderless)
                    .foregroundStyle(.secondary)
                    .accessibilityLabel("Clear the search")
            }
        }
        .padding(.horizontal, 7)
        .padding(.vertical, 5)
        // A field that reads as one (2026-10-08: the faint fill read as empty space): the text
        // background with a hairline, the accent ring while typing. Command-F jumps into it.
        .background(RoundedRectangle(cornerRadius: 7).fill(Color(nsColor: .textBackgroundColor)))
        .overlay(RoundedRectangle(cornerRadius: 7)
            .strokeBorder(focused ? Color.accentColor : Color.secondary.opacity(0.35), lineWidth: focused ? 2 : 1))
        .background(Button("Search Settings") { focused = true }
            .keyboardShortcut("f", modifiers: .command)
            .hidden())
    }
}

/// The search's matches in the sidebar, grouped by page in sidebar order.
private struct SettingsSearchResults: View {
    @Bindable var navigation: SettingsNavigation

    var body: some View {
        let groups = SettingsSearch.grouped(SettingsSearch.hits(navigation.searchText))
        if groups.isEmpty {
            Text("No match").foregroundStyle(.secondary)
        }
        ForEach(groups, id: \.page) { g in
            SettingsSidebarRows(sections: [g.page])
            ForEach(g.entries, id: \.title) { e in
                SettingsSearchHitRow(entry: e) { navigation.open(e.page, anchor: e.anchor, highlight: true) }
            }
        }
    }
}

/// One matching section or control under its page.
private struct SettingsSearchHitRow: View {
    let entry: SettingsEntry
    let choose: () -> Void

    var body: some View {
        Button(action: choose) {
            Text(entry.title).font(.callout).lineLimit(2)
                .frame(maxWidth: .infinity, alignment: .leading)
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .padding(.leading, 24)
        .help(entry.advanced ? "\(entry.page.title), under Advanced" : entry.page.title)
    }
}

/// Sidebar rows for pages, each tagged for the List's selection.
private struct SettingsSidebarRows: View {
    let sections: [SettingsSection]

    var body: some View {
        ForEach(sections) { s in
            // A drawn dot rather than List's .badge: with a badge showing, the sidebar stopped
            // taking clicks (2026-10-04). The row stays a plain Label, tagged for selection.
            HStack(spacing: 6) {
                Label(s.title, systemImage: s.symbol)
                Spacer(minLength: 0)
                if SettingsRoot.badge(s) > 0 {
                    Text("\(SettingsRoot.badge(s))")
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
}
