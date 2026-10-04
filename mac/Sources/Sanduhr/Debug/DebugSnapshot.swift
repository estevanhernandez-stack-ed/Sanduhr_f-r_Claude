import Foundation
import CoreGraphics

/// The plain values state.yaml is made from, gathered by DebugHooks from the running app.
struct DebugStateInput {
    var deskEnabled = false
    var deskRunning = false
    /// The Desk layout string as saved, nil when never set.
    var layout: String?
    /// The notch switch.
    var notch = false
    /// This screen has a camera notch and Desk built the island's window for it.
    var hasNotch = false
    /// What each place on the notch island shows, as saved or its default.
    var notchLeft = NotchContent.Place.left.fallback
    var notchRight = NotchContent.Place.right.fallback
    var notchStrip = NotchContent.Place.strip.fallback
    /// An app is using a camera (only known while the camera light switch is on).
    var cameraInUse = false
    /// The camera light shows (for the camera, or switched on by hand).
    var cameraLight = false
    /// Now playing (item 53): the switch, the source the self-test chose, and whether something
    /// plays. Never a title, an artist or an app.
    var nowPlaying = NowPlayingDebug()
    var widgetVisible = false
    /// When the widget shows on its own (WidgetVisibility raw value).
    var widgetVisibility = WidgetVisibility.always
    /// What the menu bar percent follows (MenuBarMode raw value).
    var menuBar = MenuBarMode.higher
    var settingsOpen = false
    var settingsSection: SettingsSection?
    /// The Claude Usage page (item 48) shows, and its tab. Never a label, project or number.
    var usagePageOpen = false
    var usageTab = UsageTab.overview
    var meters: [DeskMeterRow] = []
    /// The widget's tiers drawing red with a glow (MeterWarning), in display order.
    var widgetWarnings: [Tier] = []
    /// The limits switched off in Settings, Desk, Meters (MeterVisibility), in display order.
    var hiddenLimits: [Tier] = []
    /// The reported limits believed temporary (LimitLifetime), the ones that can be hidden, in
    /// display order.
    var temporaryLimits: [Tier] = []
    /// The limits whose "Warn when nearly full" is off (LimitMenu.silenced), in display order.
    var silencedLimits: [Tier] = []
    var meetingsCount = 0
    /// Every interactive Desk element with its frame (DeskElements), empty while Desk is off.
    var deskFrames: [DeskElement] = []
    /// DeskFrameCheck's answer for `deskFrames`: nil when the geometry holds.
    var deskFramesProblem: String?
    var alerts = AlertSettings()
    var lastFetch: Date?
    /// "deep-work", "snake" or nil.
    var activeTool: String?
    var pacingPinned = false
    var pulseCount = 0
    /// Notch glows fired so far, and the Glow switches.
    var glowCount = 0
    /// What the last glow outlined: island, plain (the hardware notch alone) or none yet.
    var glowShape = NotchGlowShape.none
    var glowSwitches = NotchGlowSwitches()
    /// The widget theme's id.
    var theme = ""
    /// Where credentials live this launch (KeychainStore.kind); never a value.
    var credentialsStore = CredentialStoreKind.file
    /// The active account as snapshot.json names it (AccountRef), never its label.
    var accountRef: String?
    var accountsCount = 0
    /// The active account's Meter history: 30 days, or 0 when off (MeterHistory). No label.
    var historyDays = MeterHistory.days
    /// The active account's data choices (AccountData): values and whether a folder is linked,
    /// never the folder's path or the label.
    var accountData = AccountDataChoices.defaults
    /// Live Claude Code activity (item 45): whether the shown account's folder is read, and the
    /// events counted since the last refresh. Never a path, a project or a model.
    var localActivityReading = false
    var localActivityEvents = 0
    /// The vault (item 46) for the active account: recording, months kept, whether the last
    /// cycle completed. Never a path, a project, an id or the label.
    var vault = VaultState()
    /// Claude Code folders holding Sanduhr's MCP server, statusline (item 49) and meters mod
    /// (item 50) entries. Counts only, never a path.
    var mcpInstalled = 0
    var statuslineInstalled = 0
    var metersInstalled = 0
    /// …and the Claude Code hooks for the notch glow (item 51).
    var hooksInstalled = 0
    /// Follow the account I'm using, and whether a manual switch is pausing it. Never labels.
    var follow = false
    var followPaused = false
    var menu: [MenuGroup] = []
    var version = ""
    var build = ""
}

/// state.yaml's `now_playing:`.
struct NowPlayingDebug: Equatable {
    var enabled = false
    var source = NowPlayingSource.off
    var state = NowPlayingState.none
}

enum DebugState {
    /// `now_playing:` (item 53): flags only, never what plays.
    static func nowPlayingYAML(_ n: NowPlayingDebug) -> YAMLNode {
        .map([YAMLPair("enabled", .bool(n.enabled)), YAMLPair("source", .string(n.source.rawValue)),
              YAMLPair("state", .string(n.state.rawValue))])
    }

    /// `data:` (item 44): the active account's choices, and `folder_linked` instead of the path.
    static func accountDataYAML(_ c: AccountDataChoices) -> YAMLNode {
        .map([YAMLPair("activity", .string(c.activity.rawValue)),
              YAMLPair("names", .string(c.names.rawValue)),
              YAMLPair("share", .string(c.share.rawValue)),
              YAMLPair("folder_linked", .bool(c.folder != nil))])
    }

    /// `local_activity:` (item 45): counts only.
    static func localActivityYAML(reading: Bool, events: Int) -> YAMLNode {
        .map([YAMLPair("reading", .bool(reading)), YAMLPair("events", .int(events))])
    }

    /// `usage_page:` (item 48): open and the tab, nothing it shows.
    static func usagePageYAML(open: Bool, tab: UsageTab) -> YAMLNode {
        .map([YAMLPair("open", .bool(open)), YAMLPair("tab", .string(tab.rawValue))])
    }

    /// `integrations:` (items 49 to 51): counts of folders, never a path.
    static func integrationsYAML(mcp: Int, statusline: Int, meters: Int = 0, hooks: Int = 0) -> YAMLNode {
        .map([YAMLPair("mcp_installed", .int(mcp)), YAMLPair("statusline_installed", .int(statusline)),
              YAMLPair("meters_installed", .int(meters)), YAMLPair("hooks_installed", .int(hooks))])
    }

    /// `vault:` (item 46): flags and a count only.
    static func vaultYAML(_ v: VaultState) -> YAMLNode {
        .map([YAMLPair("recording", .bool(v.recording)), YAMLPair("months", .int(v.months)),
              YAMLPair("last_ingest_ok", .bool(v.lastIngestOK))])
    }

    static func yaml(_ s: DebugStateInput) -> YAMLNode {
        // Built in typed steps: one literal holding the whole map is more than Swift 6.0 and 6.1
        // will type-check in reasonable time.
        let iso = ISO8601DateFormatter()
        let meters: [YAMLNode] = s.meters.map(meter)
        let menu: [YAMLNode] = s.menu.map(menuGroup)
        var pairs: [(String, YAMLNode?)] = []
        pairs.append(("desk_enabled", .bool(s.deskEnabled)))
        pairs.append(("desk_running", .bool(s.deskRunning)))
        pairs.append(("layout", s.layout.map(YAMLNode.string) ?? .null))
        pairs.append(("notch", .bool(s.notch)))
        pairs.append(("has_notch", .bool(s.hasNotch)))
        pairs.append(("notch_left", .string(s.notchLeft.rawValue)))
        pairs.append(("notch_right", .string(s.notchRight.rawValue)))
        pairs.append(("notch_strip", .string(s.notchStrip.rawValue)))
        pairs.append(("camera_in_use", .bool(s.cameraInUse)))
        pairs.append(("camera_light", .bool(s.cameraLight)))
        pairs.append(("now_playing", nowPlayingYAML(s.nowPlaying)))
        pairs.append(("widget_visible", .bool(s.widgetVisible)))
        pairs.append(("widget_visibility", .string(s.widgetVisibility.rawValue)))
        pairs.append(("menu_bar", .string(s.menuBar.rawValue)))
        pairs.append(("settings_open", .bool(s.settingsOpen)))
        let section: YAMLNode = s.settingsSection.map { .string($0.rawValue) } ?? .null
        pairs.append(("settings_section", section))
        pairs.append(("usage_page", usagePageYAML(open: s.usagePageOpen, tab: s.usageTab)))
        pairs.append(("meters", .list(meters)))
        let widgetWarnings: [YAMLNode] = s.widgetWarnings.map { .string($0.rawValue) }
        pairs.append(("widget_warnings", .list(widgetWarnings)))
        let hiddenLimits: [YAMLNode] = s.hiddenLimits.map { .string($0.rawValue) }
        pairs.append(("hidden_limits", .list(hiddenLimits)))
        let temporaryLimits: [YAMLNode] = s.temporaryLimits.map { .string($0.rawValue) }
        pairs.append(("temporary_limits", .list(temporaryLimits)))
        let silencedLimits: [YAMLNode] = s.silencedLimits.map { .string($0.rawValue) }
        pairs.append(("silenced_limits", .list(silencedLimits)))
        pairs.append(("meetings_count", .int(s.meetingsCount)))
        let frames: [YAMLNode] = s.deskFrames.map(deskFrame)
        pairs.append(("desk_frames", .list(frames)))
        pairs.append(("desk_frames_ok", .bool(s.deskFramesProblem == nil)))
        pairs.append(("desk_frames_problem", s.deskFramesProblem.map(YAMLNode.string) ?? .null))
        pairs.append(("alerts", alerts(s.alerts)))
        let fetched: YAMLNode = s.lastFetch.map { .string(iso.string(from: $0)) } ?? .null
        pairs.append(("last_fetch", fetched))
        pairs.append(("active_tool", s.activeTool.map(YAMLNode.string) ?? .null))
        pairs.append(("pacing_pinned", .bool(s.pacingPinned)))
        pairs.append(("pulse_count", .int(s.pulseCount)))
        pairs.append(("glow_count", .int(s.glowCount)))
        pairs.append(("glow_shape", .string(s.glowShape.rawValue)))
        pairs.append(("glow_alerts", .bool(s.glowSwitches.alerts)))
        pairs.append(("glow_meetings", .bool(s.glowSwitches.meetings)))
        pairs.append(("glow_camera", .bool(s.glowSwitches.camera)))
        pairs.append(("glow_claude_waiting", .bool(s.glowSwitches.claudeWaiting)))
        pairs.append(("glow_claude_done", .bool(s.glowSwitches.claudeDone)))
        pairs.append(("theme", .string(s.theme)))
        pairs.append(("menu", .list(menu)))
        pairs.append(("credentials_store", .string(s.credentialsStore.rawValue)))
        pairs.append(("account_ref", s.accountRef.map(YAMLNode.string) ?? .null))
        pairs.append(("accounts_count", .int(s.accountsCount)))
        pairs.append(("history_days", .int(s.historyDays)))
        pairs.append(("data", accountDataYAML(s.accountData)))
        pairs.append(("local_activity", localActivityYAML(reading: s.localActivityReading,
                                                          events: s.localActivityEvents)))
        pairs.append(("vault", vaultYAML(s.vault)))
        pairs.append(("integrations", integrationsYAML(mcp: s.mcpInstalled, statusline: s.statuslineInstalled,
                                                                meters: s.metersInstalled, hooks: s.hooksInstalled)))
        pairs.append(("follow", .bool(s.follow)))
        pairs.append(("follow_paused", .bool(s.followPaused)))
        pairs.append(("version", .string(s.version)))
        pairs.append(("build", .string(s.build)))
        return .object(pairs)
    }
    
    private static func meter(_ m: DeskMeterRow) -> YAMLNode {
        let pace: YAMLNode = m.pace.map(YAMLNode.double) ?? .null
        return .object([
            ("tier", .string(m.tier.rawValue)),
            ("label", .string(m.label)),
            ("percent", .int(m.percent)),
            ("fill", .double(m.fill)),
            ("pace", pace),
            ("reset", .string(m.reset)),
            ("warning", .bool(m.warning)),
        ])
    }
    
    /// One Desk element: kind, key (a tier or a row index, never a title or a label), whether it
    /// takes clicks, and its frame in whole points, `[x, y, w, h]` from the Desk window's top left.
    private static func deskFrame(_ e: DeskElement) -> YAMLNode {
        let f = e.frame
        let rounded: [YAMLNode] = [f.minX, f.minY, f.width, f.height].map { .int(Int($0.rounded())) }
        return .object([
            ("kind", .string(e.kind.rawValue)),
            ("key", e.key.map(YAMLNode.string)),
            ("clickable", .bool(e.clickable)),
            ("frame", .list(rounded)),
        ])
    }

    private static func menuGroup(_ group: MenuGroup) -> YAMLNode {
        let items: [YAMLNode] = group.entries.map { e in
            .object([("title", .string(e.title)), ("checked", .bool(e.checked))])
        }
        return .object([("header", group.header.map(YAMLNode.string)), ("items", .list(items))])
    }
    
    static func alerts(_ a: AlertSettings) -> YAMLNode {
        .object([
            ("enabled", .bool(a.enabled)),
            ("session_line", .double(a.sessionLine)),
            ("weekly_line", .double(a.weeklyLine)),
            ("session_full", .bool(a.sessionFull)),
            ("session_reset", .bool(a.sessionReset)),
            ("weekly_reset", .bool(a.weeklyReset)),
            ("pace", .bool(a.pace)),
            ("delivery", .string(a.delivery.rawValue)),
            ("quiet_enabled", .bool(a.quietEnabled)),
            ("quiet_start", .int(a.quietStart)),
            ("quiet_end", .int(a.quietEnd)),
            ("sound", .string(a.sound)),
        ])
    }
}

/// One accessibility element as tree.yaml lists it.
struct DebugTreeNode: Equatable {
    var role: String?
    var subrole: String?
    var label: String?
    var value: YAMLNode?
    var enabled = true
    /// Screen points, top-left origin.
    var frame: CGRect = .zero
    var children: [DebugTreeNode] = []

    var yaml: YAMLNode {
        .object([
            ("role", .text(role)),
            ("subrole", .text(subrole)),
            ("label", .text(label)),
            ("value", value),
            ("enabled", .bool(enabled)),
            ("frame", .rect(frame)),
            ("children", children.isEmpty ? nil : .list(children.map(\.yaml))),
        ])
    }
}

/// One window in tree.yaml.
struct DebugWindowEntry: Equatable {
    /// widget, desk, notch, camera, settings, sheet or window, made unique ("sheet-2").
    var name: String
    /// The window number, which is also its CGWindowID: `screencapture -l` takes it.
    var windowID: Int
    var title: String?
    var frame: CGRect
    var visible: Bool
    var tree: [DebugTreeNode]

    var yaml: YAMLNode {
        .object([
            ("window", .string(name)),
            ("window_id", .int(windowID)),
            ("title", .text(title)),
            ("frame", .rect(frame)),
            ("visible", .bool(visible)),
            ("tree", .list(tree.map(\.yaml))),
        ])
    }
}

enum DebugTree {
    /// Hard limits on the walk, so a runaway hierarchy cannot stall the app.
    static let maxDepth = 40
    static let maxNodes = 4000

    /// AppKit screen coordinates (bottom-left origin on the primary screen) to top-left ones.
    /// A Vision bounding box (normalized, bottom-left origin) inside a content area given in
    /// screen points with a top-left origin, as a screen rect with a top-left origin.
    static func screenRect(normalized box: CGRect, in area: CGRect) -> CGRect {
        CGRect(x: area.minX + box.minX * area.width,
               y: area.minY + (1 - box.maxY) * area.height,
               width: box.width * area.width,
               height: box.height * area.height)
    }

    static func topLeft(_ r: CGRect, primaryHeight: CGFloat) -> CGRect {
        CGRect(x: r.minX, y: primaryHeight - r.maxY, width: r.width, height: r.height)
    }

    /// Window kinds made unique in order: the first keeps its name, later ones get -2, -3.
    static func uniqueNames(_ kinds: [String]) -> [String] {
        var seen: [String: Int] = [:]
        return kinds.map { kind in
            seen[kind, default: 0] += 1
            let n = seen[kind]!
            return n == 1 ? kind : "\(kind)-\(n)"
        }
    }

    /// An accessibility value as YAML: booleans and numbers as numbers (a switch reads 1 or 0),
    /// text as a string, nil for nothing or anything else.
    static func value(_ any: Any?) -> YAMLNode? {
        switch any {
        case let n as NSNumber:
            if CFGetTypeID(n) == CFBooleanGetTypeID() { return .int(n.boolValue ? 1 : 0) }
            let d = n.doubleValue
            return d == d.rounded() && abs(d) < 1e15 ? .int(n.intValue) : .double(d)
        case let s as String: return .text(s)
        case let a as NSAttributedString: return .text(a.string)
        default: return nil
        }
    }
}
