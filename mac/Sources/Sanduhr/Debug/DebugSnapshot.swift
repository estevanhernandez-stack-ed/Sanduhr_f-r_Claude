import Foundation
import CoreGraphics

/// The plain values state.yaml is made from, gathered by DebugHooks from the running app.
struct DebugStateInput {
    var deskEnabled = false
    var deskRunning = false
    /// The Desk layout string as saved, nil when never set.
    var layout: String?
    /// The pieces the Desk draws from it (item 59), in order: each anchor's stack top to bottom.
    var deskPieces: [DeskPlacement] = []
    /// Arrange mode (item 60): on or off, an unsaved change, the layout being edited, and whether
    /// the Desk window takes clicks over its whole frame or only where it draws.
    var deskArrange = DeskArrangeDebug()
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
    /// What a notch place on Now playing shows while there is no line (Settings, Desk, Now
    /// Playing, When nothing is playing).
    var nowPlayingIdle = NowPlayingIdle.automatic
    /// What each notch place shows now (NotchContent.effective): the saved choice, or the When
    /// nothing is playing choice while a place on Now playing has nothing to show.
    var notchShows = NotchShowsDebug()
    var widgetVisible = false
    /// When the widget shows on its own (WidgetVisibility raw value).
    var widgetVisibility = WidgetVisibility.always
    /// What the menu bar percent follows (MenuBarMode raw value).
    var menuBar = MenuBarMode.higher
    var settingsOpen = false
    var settingsSection: SettingsSection?
    /// The preview card the open Settings section shows (item 68), nil for none or closed.
    var settingsPreview: SettingsPreviewKind?
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
    /// The Dock as the Desk sees it (item 56): its side, auto-hide, and the inset applied now.
    var dock = DockDebug()
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
    /// Claude's suggestions waiting for the user (items 54, 55): flags only, never their content.
    var pendingMessages = false
    var pendingTheme = false
    var menu: [MenuGroup] = []
    /// What's New (item 57): the last version seen, the cards the next launch would show, the
    /// window, and Don't show after updates.
    var whatsNew = WhatsNewDebug()
    /// The welcome tour (item 61): the window, its step, how many steps show, and its state.
    var tour = TourDebug()
    /// Watchers (item 66): counts, states and places, never a title or a note.
    var watchers = WatchersDebug()
    /// The camera and mic indicators (item 67): in-use booleans and where they show, never an app.
    var avIndicators = AVIndicatorsDebug()
    /// The Mods page (item 64): open, read, and counts only, never a name or a path.
    var modsPage = ModsPageDebug()
    /// Settings, Message's editor (item 69): counts and flags, never a line of the user's.
    var messageEditor = MessageEditorDebug()
    var version = ""
    var build = ""
}

/// state.yaml's `desk_arrange:` (item 60). `working` is a layout string, never anything typed.
struct DeskArrangeDebug: Equatable {
    var active = false
    var changed = false
    var working: String?
    var clickThrough = "drawn"
    /// The bar's floating panel (Cancel and Done) is on screen.
    var barVisible = false
}

/// state.yaml's `dock:` (item 56): the Dock's side and auto-hide (its own settings, read only),
/// and how far the Desk's corners on that side are moved in now, in whole points.
struct DockDebug: Equatable {
    var side = DockSide.bottom
    var autohide = false
    var inset = 0
}

/// state.yaml's `whats_new:`.
struct WhatsNewDebug: Equatable {
    var lastSeen: String?
    var pending = 0
    var open = false
    var hidden = false
}

/// state.yaml's `tour:`.
struct TourDebug: Equatable {
    var open = false
    /// 1-based while open, 0 when closed.
    var step = 0
    var stepsShown = 0
    /// Finished or skipped.
    var done = false
    /// Waiting for a fresh install's first successful fetch.
    var pending = false
}

/// state.yaml's `watchers:` (item 66): how many show, their states most urgent first, where they
/// are placed and the two switches. Never a title, a note, a link or a description.
struct WatchersDebug: Equatable {
    var count = 0
    var states: [WatcherState] = []
    var placements: [String] = []
    var agents = false
    var background = false
    /// The notch plays the top watcher's intro (the full line) rather than resting on the short one.
    var intro = false
}

/// state.yaml's `av_indicators:` (item 67): whether a camera and the microphone are in use (the
/// real signal while Desk runs and that indicator's switch is on, or faked by `av-test`) and where
/// the indicators show (AVIndicatorSpot.name: none, beside_left, beside_right, places, badge).
/// Never which app.
struct AVIndicatorsDebug: Equatable {
    var camera = false
    var mic = false
    var shown = "none"
}

/// state.yaml's `mods_page:` (item 64): whether the page shows and has read the folders, the
/// counts across folders, how many Checks have answered and whether `claude` was found. Never a
/// mod's name, a path or a report.
struct ModsPageDebug: Equatable {
    var open = false
    var loaded = false
    var counts = ModCounts()
    var checked = 0
    var cli = false
}

/// state.yaml's `notch_shows:`: each place's effective content, never its text.
struct NotchShowsDebug: Equatable {
    var left = NotchContent.Place.left.fallback
    var right = NotchContent.Place.right.fallback
    var strip = NotchContent.Place.strip.fallback
}

/// state.yaml's `now_playing:`.
struct NowPlayingDebug: Equatable {
    /// Running: placed somewhere and Desk on.
    var enabled = false
    /// Where it is placed (item 53b), never what plays.
    var placed: [NowPlayingPlacement.Place] = []
    var source = NowPlayingSource.off
    var state = NowPlayingState.none
}

enum DebugState {
    /// `now_playing:` (items 53, 53b): flags and places only, never what plays.
    static func nowPlayingYAML(_ n: NowPlayingDebug) -> YAMLNode {
        .map([YAMLPair("enabled", .bool(n.enabled)),
              YAMLPair("placed", .list(n.placed.map { .string($0.rawValue) })),
              YAMLPair("source", .string(n.source.rawValue)),
              YAMLPair("state", .string(n.state.rawValue))])
    }

    /// `notch_shows:`: what each notch place shows now, as a NotchContent raw value.
    static func notchShowsYAML(_ n: NotchShowsDebug) -> YAMLNode {
        .map([YAMLPair("left", .string(n.left.rawValue)), YAMLPair("right", .string(n.right.rawValue)),
              YAMLPair("strip", .string(n.strip.rawValue))])
    }

    /// `desk_pieces:` (item 59): each drawn piece's widget word, anchor, place in its anchor's
    /// stack (0 at the top) and size.
    static func deskPiecesYAML(_ pieces: [DeskPlacement]) -> YAMLNode {
        var order: [DeskAnchor: Int] = [:]
        let items: [YAMLNode] = pieces.map { p in
            let n = order[p.anchor, default: 0]
            order[p.anchor] = n + 1
            return .map([YAMLPair("piece", .string(p.widget)), YAMLPair("anchor", .string(p.anchor.rawValue)),
                         YAMLPair("order", .int(n)), YAMLPair("scale", .double(p.scale))])
        }
        return .list(items)
    }

    /// `desk_arrange:` (item 60): active, changed, the working layout string (null outside Arrange
    /// mode), click_through (`whole` or `drawn`) and bar_visible (the floating bar is on screen).
    static func deskArrangeYAML(_ a: DeskArrangeDebug) -> YAMLNode {
        .map([YAMLPair("active", .bool(a.active)), YAMLPair("changed", .bool(a.changed)),
              YAMLPair("working", a.working.map(YAMLNode.string) ?? .null),
              YAMLPair("click_through", .string(a.clickThrough)),
              YAMLPair("bar_visible", .bool(a.barVisible))])
    }

    /// `dock:` (item 56): side, auto-hide and the inset applied now.
    static func dockYAML(_ d: DockDebug) -> YAMLNode {
        .map([YAMLPair("side", .string(d.side.rawValue)), YAMLPair("autohide", .bool(d.autohide)),
              YAMLPair("inset", .int(d.inset))])
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

    /// `pending_suggestions:` (items 54, 55): whether a suggestion from Claude waits, no content.
    static func pendingSuggestionsYAML(messages: Bool, theme: Bool) -> YAMLNode {
        .map([YAMLPair("messages", .bool(messages)), YAMLPair("theme", .bool(theme))])
    }

    /// `whats_new:` (item 57): versions, a count and two flags.
    static func whatsNewYAML(_ w: WhatsNewDebug) -> YAMLNode {
        .map([YAMLPair("last_seen", w.lastSeen.map(YAMLNode.string) ?? .null),
              YAMLPair("pending", .int(w.pending)), YAMLPair("open", .bool(w.open)),
              YAMLPair("hide_after_updates", .bool(w.hidden))])
    }

    /// `tour:` (item 61): flags and counts.
    static func tourYAML(_ t: TourDebug) -> YAMLNode {
        .map([YAMLPair("open", .bool(t.open)), YAMLPair("step", .int(t.step)),
              YAMLPair("steps_shown", .int(t.stepsShown)), YAMLPair("done", .bool(t.done)),
              YAMLPair("pending", .bool(t.pending))])
    }

    /// `watchers:` (item 66): a count, states, places and switches only.
    static func watchersYAML(_ w: WatchersDebug) -> YAMLNode {
        .map([YAMLPair("count", .int(w.count)),
              YAMLPair("states", .list(w.states.map { .string($0.rawValue) })),
              YAMLPair("placements", .list(w.placements.map(YAMLNode.string))),
              YAMLPair("agents", .bool(w.agents)), YAMLPair("background", .bool(w.background)),
              YAMLPair("intro", .bool(w.intro))])
    }

    /// `av_indicators:` (item 67): two booleans and a place.
    static func avIndicatorsYAML(_ a: AVIndicatorsDebug) -> YAMLNode {
        .map([YAMLPair("camera", .bool(a.camera)), YAMLPair("mic", .bool(a.mic)),
              YAMLPair("shown", .string(a.shown))])
    }

    /// `message_editor:` (item 69): the view, row counts and flags; `added` is only the smoke's own
    /// line from `message-editor add`.
    static func messageEditorYAML(_ m: MessageEditorDebug) -> YAMLNode {
        .map([YAMLPair("open", .bool(m.open)), YAMLPair("mode", .string(m.mode)), YAMLPair("rows", .int(m.rows)),
              YAMLPair("styled", .int(m.styled)), YAMLPair("raw", .int(m.raw)), YAMLPair("notes", .int(m.notes)),
              YAMLPair("unsaved", .bool(m.unsaved)), YAMLPair("today_special", .int(m.todaySpecial)), YAMLPair("added", m.added.map(YAMLNode.string) ?? .null)])
    }

    /// `mods_page:` (item 64): flags and counts only.
    static func modsPageYAML(_ m: ModsPageDebug) -> YAMLNode {
        .map([YAMLPair("open", .bool(m.open)), YAMLPair("loaded", .bool(m.loaded)),
              YAMLPair("folders", .int(m.counts.folders)), YAMLPair("mods", .int(m.counts.mods)),
              YAMLPair("plugins", .int(m.counts.plugins)), YAMLPair("enabled", .int(m.counts.on)),
              YAMLPair("missing", .int(m.counts.missing)), YAMLPair("checked", .int(m.checked)),
              YAMLPair("cli", .bool(m.cli))])
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
        pairs.append(("desk_pieces", deskPiecesYAML(s.deskPieces)))
        pairs.append(("desk_arrange", deskArrangeYAML(s.deskArrange)))
        pairs.append(("notch", .bool(s.notch)))
        pairs.append(("has_notch", .bool(s.hasNotch)))
        pairs.append(("notch_left", .string(s.notchLeft.rawValue)))
        pairs.append(("notch_right", .string(s.notchRight.rawValue)))
        pairs.append(("notch_strip", .string(s.notchStrip.rawValue)))
        pairs.append(("camera_in_use", .bool(s.cameraInUse)))
        pairs.append(("camera_light", .bool(s.cameraLight)))
        pairs.append(("now_playing", nowPlayingYAML(s.nowPlaying)))
        pairs.append(("now_playing_idle", .string(s.nowPlayingIdle.rawValue)))
        pairs.append(("notch_shows", notchShowsYAML(s.notchShows)))
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
        pairs.append(("dock", dockYAML(s.dock)))
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
        pairs.append(("pending_suggestions", pendingSuggestionsYAML(messages: s.pendingMessages, theme: s.pendingTheme)))
        pairs.append(("follow", .bool(s.follow)))
        pairs.append(("follow_paused", .bool(s.followPaused)))
        pairs.append(("version", .string(s.version)))
        pairs.append(("build", .string(s.build)))
        pairs.append(("whats_new", whatsNewYAML(s.whatsNew)))
        pairs.append(("tour", tourYAML(s.tour)))
        pairs.append(("watchers", watchersYAML(s.watchers)))
        pairs.append(("av_indicators", avIndicatorsYAML(s.avIndicators)))
        // Item 68: which preview card the open Settings section shows (SettingsPreviewKind).
        pairs.append(("settings_preview", s.settingsPreview.map { .string($0.rawValue) } ?? .null))
        pairs.append(("mods_page", modsPageYAML(s.modsPage)))
        pairs.append(("message_editor", messageEditorYAML(s.messageEditor)))
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
