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
    var widgetVisible = false
    /// When the widget shows on its own (WidgetVisibility raw value).
    var widgetVisibility = WidgetVisibility.always
    var settingsOpen = false
    var settingsSection: SettingsSection?
    var meters: [DeskMeterRow] = []
    /// The widget's tiers drawing red with a glow (MeterWarning), in display order.
    var widgetWarnings: [Tier] = []
    var meetingsCount = 0
    var alerts = AlertSettings()
    var lastFetch: Date?
    /// "deep-work", "snake" or nil.
    var activeTool: String?
    var pacingPinned = false
    var pulseCount = 0
    /// Notch glows fired so far, and the three Glow switches.
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
    /// Follow the account I'm using, and whether a manual switch is pausing it. Never labels.
    var follow = false
    var followPaused = false
    var menu: [MenuGroup] = []
    var version = ""
    var build = ""
}

enum DebugState {    static func yaml(_ s: DebugStateInput) -> YAMLNode {
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
        pairs.append(("widget_visible", .bool(s.widgetVisible)))
        pairs.append(("widget_visibility", .string(s.widgetVisibility.rawValue)))
        pairs.append(("settings_open", .bool(s.settingsOpen)))
        let section: YAMLNode = s.settingsSection.map { .string($0.rawValue) } ?? .null
        pairs.append(("settings_section", section))
        pairs.append(("meters", .list(meters)))
        let widgetWarnings: [YAMLNode] = s.widgetWarnings.map { .string($0.rawValue) }
        pairs.append(("widget_warnings", .list(widgetWarnings)))
        pairs.append(("meetings_count", .int(s.meetingsCount)))
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
        pairs.append(("theme", .string(s.theme)))
        pairs.append(("menu", .list(menu)))
        pairs.append(("credentials_store", .string(s.credentialsStore.rawValue)))
        pairs.append(("account_ref", s.accountRef.map(YAMLNode.string) ?? .null))
        pairs.append(("accounts_count", .int(s.accountsCount)))
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
