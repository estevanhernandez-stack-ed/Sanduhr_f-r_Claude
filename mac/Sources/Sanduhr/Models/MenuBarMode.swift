import Foundation

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
}

/// What the menu bar shows: which limit, its percent, and the text beside the hourglass.
struct MenuBarReading: Equatable {
    let tier: Tier
    let percent: Int
    /// "12%", or in Rotate "S 12%" / "W 96%".
    let text: String
}

enum MenuBarText {
    /// The reading for `mode`, nil with neither the session nor the weekly limit (the menu bar
    /// then shows the hourglass alone). A chosen limit that is missing falls back to the other.
    /// `step` counts Rotate's turns: even shows the session, odd the weekly limit.
    static func reading(_ usage: UsageResponse?, mode: MenuBarMode, step: Int) -> MenuBarReading? {
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
