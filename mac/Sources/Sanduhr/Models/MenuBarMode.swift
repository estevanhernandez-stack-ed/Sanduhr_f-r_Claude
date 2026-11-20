import AppKit
import Foundation

/// How the status item draws a reading beside the hourglass: the text with a leading space, in
/// the small monospaced-digit system font, colored only when urgency is high (red from 90%,
/// orange from 75%) so the menu bar stays neutral the rest of the time (HIG). The status item and
/// Settings, General's preview (item 68) both draw from this.
struct MenuBarItemLook {
    enum Urgency: Equatable { case normal, high, critical }

    let title: String
    let urgency: Urgency

    init(_ reading: MenuBarReading) {
        title = " \(reading.text)"
        switch reading.percent {
        case 90...: urgency = .critical
        case 75...89: urgency = .high
        default: urgency = .normal
        }
    }

    var color: NSColor {
        switch urgency {
        case .critical: NSColor(red: 0.97, green: 0.44, blue: 0.44, alpha: 1)
        case .high: NSColor(red: 0.98, green: 0.58, blue: 0.24, alpha: 1)
        case .normal: .labelColor
        }
    }

    static var font: NSFont {
        NSFont.monospacedDigitSystemFont(ofSize: NSFont.systemFontSize(for: .small), weight: .medium)
    }
}

/// What the menu bar percent follows (Settings, General, Menu bar). Standard defaults, key
/// `menuBarMode`; absent means `higher`:
///   defaults write com.626labs.sanduhr menuBarMode -string rotate
/// Only the session and the all-models weekly limit ever drive the menu bar: a model-specific or
/// promo limit at 100% would otherwise pin it there.
enum MenuBarMode: String, CaseIterable, Identifiable {
    case session
    case weekly
    /// The higher of the session and the weekly limit.
    case higher
    /// Session and weekly in turn, every `rotateInterval` seconds ("S 12%", then "W 96%").
    case rotate

    static let key = "menuBarMode"
    /// Seconds each limit shows in Rotate.
    static let rotateInterval: TimeInterval = 8

    var id: String { rawValue }

    var label: String {
        switch self {
        case .session: "Session"
        case .weekly: "Weekly"
        case .higher: "Whichever is higher"
        case .rotate: "Rotate (session and weekly)"
        }
    }

    /// The saved choice, or `higher` when none is saved (or it is not one this version knows).
    static func saved(in store: DefaultsStore = UserDefaults.standard) -> MenuBarMode {
        (store.object(forKey: key) as? String).flatMap(MenuBarMode.init(rawValue:)) ?? .higher
    }

    /// Saves `mode` as the choice, under the key Settings, General, Menu bar reads through
    /// @AppStorage, so the picker follows the menu bar item's Menu Bar Shows submenu.
    static func save(_ mode: MenuBarMode, in store: DefaultsStore = UserDefaults.standard) {
        store.set(mode.rawValue, forKey: key)
    }
}

/// Whether the hourglass is in the menu bar at all (Settings, General, Menu bar). Standard
/// defaults, key `menuBarItemHidden`; absent means shown:
///   defaults write com.626labs.sanduhr menuBarItemHidden -bool true
/// Hidden, Sanduhr's menu is still in the widget's menu, the Desk's clock and the notch, and
/// opening Sanduhr again (Finder, Spotlight, the Dock) shows the widget.
enum MenuBarItem {
    static let hiddenKey = "menuBarItemHidden"

    static func isHidden(in store: DefaultsStore = UserDefaults.standard) -> Bool {
        store.bool(forKey: hiddenKey)
    }
}

/// What the menu bar shows: which limit, its percent, and the text beside the hourglass.
struct MenuBarReading: Equatable {
    let tier: Tier
    let percent: Int
    /// "12%", or in Rotate "S 12%" / "W 96%".
    let text: String
}

/// What the status item shows: the reading beside the hourglass and the button's tooltip.
struct MenuBarDisplay: Equatable {
    let reading: MenuBarReading?
    /// Set only when it has something to add: another source further out, or a stale Codex primary.
    let tooltip: String?
}

enum MenuBarText {
    /// The status item with more than one source (agent sources design, section 2): the reading
    /// follows `primary` under `mode`, and the tooltip is Windows' tray clause. It names the primary
    /// and the other source when that one has a current number further out for the reading's window
    /// ("Claude week 40%. Codex week 70%."), and says "as of earlier" for a Codex primary whose
    /// reading is old. The text stays a number either way. With only Claude on there is no tooltip
    /// and the text is `reading(_:mode:step:usageOn:)`'s, as it always was.
    /// - Parameters:
    ///   - claudeOn: Claude usage is on; off, Claude has no reading and is never named.
    ///   - codexOn: Codex is on; off, whatever numbers are held are ignored.
    ///   - codexFresh: the Codex tiers whose number is current (`CodexUsageService.freshTiers`).
    static func display(primary: AgentSource, claude: UsageResponse?, claudeOn: Bool,
                        codex: UsageResponse?, codexOn: Bool, codexFresh: Set<Tier>,
                        mode: MenuBarMode, step: Int) -> MenuBarDisplay {
        let lead: AgentSource = codexOn && primary == .codex ? .codex : .claude
        let shown = lead == .codex ? codex : claude
        guard let reading = reading(shown, mode: mode, step: step, usageOn: lead == .codex || claudeOn) else {
            return MenuBarDisplay(reading: nil, tooltip: nil)
        }
        let tier = reading.tier
        let word = tier == .fiveHour ? "5h" : "week"
        let leadStale = lead == .codex && !codexFresh.contains(tier)
        var clause = "\(lead.displayName) \(word) \(reading.percent)%" + (leadStale ? ", as of earlier." : ".")

        // The other source, when it has a current number further out in the same window.
        let other: AgentSource = lead == .codex ? .claude : .codex
        let otherOn = other == .codex ? codexOn : claudeOn
        let otherUsage = other == .codex ? codex : claude
        let otherFresh = other == .codex ? codexFresh.contains(tier) : true
        var named = false
        if otherOn, otherFresh, let util = otherUsage?.tiers[tier]?.utilization, Int(util) > reading.percent {
            clause += " \(other.displayName) \(word) \(Int(util))%."
            named = true
        }
        return MenuBarDisplay(reading: reading, tooltip: named || leadStale ? clause : nil)
    }

    /// The reading for `mode`, nil with neither the session nor the weekly limit (the menu bar
    /// then shows the hourglass alone). A chosen limit that is missing falls back to the other.
    /// `step` counts Rotate's turns: even shows the session, odd the weekly limit.
    /// With Claude usage off there is no reading whatever numbers are held: the hourglass alone.
    static func reading(_ usage: UsageResponse?, mode: MenuBarMode, step: Int, usageOn: Bool = true) -> MenuBarReading? {
        guard usageOn else { return nil }
        let session = usage?.tiers[.fiveHour]?.utilization
        let weekly = usage?.tiers[.sevenDay]?.utilization
        let pick: Tier?
        switch mode {
        case .session:
            pick = session != nil ? .fiveHour : (weekly != nil ? .sevenDay : nil)
        case .weekly:
            pick = weekly != nil ? .sevenDay : (session != nil ? .fiveHour : nil)
        case .higher:
            if let s = session, let w = weekly {
                pick = w > s ? .sevenDay : .fiveHour
            } else {
                pick = session != nil ? .fiveHour : (weekly != nil ? .sevenDay : nil)
            }
        case .rotate:
            let wantWeekly = step % 2 != 0
            let first: Tier = wantWeekly ? .sevenDay : .fiveHour
            let other: Tier = wantWeekly ? .fiveHour : .sevenDay
            let has: (Tier) -> Bool = { $0 == .fiveHour ? session != nil : weekly != nil }
            pick = has(first) ? first : (has(other) ? other : nil)
        }
        guard let tier = pick, let util = tier == .fiveHour ? session : weekly else { return nil }
        let percent = Int(util)
        guard mode == .rotate else { return MenuBarReading(tier: tier, percent: percent, text: "\(percent)%") }
        let letter = tier == .fiveHour ? "S" : "W"
        return MenuBarReading(tier: tier, percent: percent, text: "\(letter) \(percent)%")
    }
}
