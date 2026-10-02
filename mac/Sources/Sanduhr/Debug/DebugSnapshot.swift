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
    var widgetVisible = false
    var settingsOpen = false
    var settingsSection: SettingsSection?
    var meters: [DeskMeterRow] = []
    var meetingsCount = 0
    var alerts = AlertSettings()
    var lastFetch: Date?
    /// "deep-work", "snake" or nil.
    var activeTool: String?
    var pacingPinned = false
    var pulseCount = 0
    var menu: [MenuGroup] = []
    var version = ""
    var build = ""
}

enum DebugState {
    static func yaml(_ s: DebugStateInput) -> YAMLNode {
        let iso = ISO8601DateFormatter()
        return .object([
            ("desk_enabled", .bool(s.deskEnabled)),
            ("desk_running", .bool(s.deskRunning)),
            ("layout", s.layout.map(YAMLNode.string) ?? .null),
            ("notch", .bool(s.notch)),
            ("has_notch", .bool(s.hasNotch)),
            ("widget_visible", .bool(s.widgetVisible)),
            ("settings_open", .bool(s.settingsOpen)),
            ("settings_section", s.settingsSection.map { .string($0.rawValue) } ?? .null),
            ("meters", .list(s.meters.map { m in
                .object([
                    ("tier", .string(m.tier.rawValue)),
                    ("label", .string(m.label)),
                    ("percent", .int(m.percent)),
                    ("fill", .double(m.fill)),
                    ("pace", m.pace.map(YAMLNode.double) ?? .null),
                    ("reset", .string(m.reset)),
                ])
            })),
            ("meetings_count", .int(s.meetingsCount)),
            ("alerts", alerts(s.alerts)),
            ("last_fetch", s.lastFetch.map { .string(iso.string(from: $0)) } ?? .null),
            ("active_tool", s.activeTool.map(YAMLNode.string) ?? .null),
            ("pacing_pinned", .bool(s.pacingPinned)),
            ("pulse_count", .int(s.pulseCount)),
            ("menu", .list(s.menu.map { group in
                .object([
                    ("header", group.header.map(YAMLNode.string)),
                    ("items", .list(group.entries.map { e in
                        .object([("title", .string(e.title)), ("checked", .bool(e.checked))])
                    })),
                ])
            })),
            ("version", .string(s.version)),
            ("build", .string(s.build)),
        ])
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
    /// widget, desk, notch, settings, sheet or window, made unique ("sheet-2").
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
