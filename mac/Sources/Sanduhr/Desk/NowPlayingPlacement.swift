import Foundation

/// Where now playing is placed (item 53b), and so whether it runs. There is no switch of its own:
/// it is placed like everything else, in a notch wing or the strip (Settings, Desk, Notch) or as
/// an element of the Desk layout (Settings, Desk, Layout), and it runs while it is placed
/// somewhere and Desk is on.
enum NowPlayingPlacement {
    /// One place now playing can show (state.yaml `now_playing.placed`).
    enum Place: String, CaseIterable, Equatable {
        case wingLeft = "wing_left"
        case wingRight = "wing_right"
        case strip
        case desk
    }

    /// The Desk layout word for the now playing element ("nowPlaying:bl").
    static let widget = "nowPlaying"

    /// The saved settings the places are read from.
    struct Input: Equatable {
        /// The notch island is switched on.
        var notch = false
        /// "Text beside the camera".
        var wingText = true
        var left = NotchContent.Place.left.fallback
        var right = NotchContent.Place.right.fallback
        /// "Text under the camera too".
        var chinText = false
        /// The strip's height; 0 means no strip.
        var chin = 26.0
        var strip = NotchContent.Place.strip.fallback
        /// The Desk layout string.
        var layout = DeskLayout.standard
    }

    /// Every place now playing is drawn, in a fixed order. A wing counts with the island and its
    /// text on; the strip also needs its own text switch and some height; the Desk counts when the
    /// layout gives the element a corner.
    static func places(_ i: Input) -> [Place] {
        var out: [Place] = []
        if i.notch, i.wingText {
            if i.left == .nowPlaying { out.append(.wingLeft) }
            if i.right == .nowPlaying { out.append(.wingRight) }
        }
        if i.notch, i.chinText, i.chin > 0, i.strip == .nowPlaying { out.append(.strip) }
        if DeskLayout.placed(i.layout).contains(widget) { out.append(.desk) }
        return out
    }

    /// Now playing runs (the adapter, the fallback) only while it is placed somewhere and Desk is
    /// on: the wings, the strip and the Desk line all belong to Desk.
    static func isRunning(deskRunning: Bool, places: [Place]) -> Bool {
        deskRunning && !places.isEmpty
    }

    /// The places from the saved Desk settings.
    static func saved(in d: DefaultsStore) -> [Place] {
        places(input(from: d))
    }

    static func input(from d: DefaultsStore) -> Input {
        var i = Input()
        i.notch = d.bool(forKey: DeskController.notchKey)
        i.wingText = d.object(forKey: "notchText") as? Bool ?? true
        i.left = NotchContent.resolve(.left, raw: d.object(forKey: NotchContent.Place.left.key) as? String)
        i.right = NotchContent.resolve(.right, raw: d.object(forKey: NotchContent.Place.right.key) as? String)
        i.chinText = d.bool(forKey: "notchChinText")
        i.chin = (d.object(forKey: "notchChin") as? NSNumber)?.doubleValue ?? 26
        i.strip = NotchContent.resolve(.strip, raw: d.object(forKey: NotchContent.Place.strip.key) as? String)
        i.layout = d.object(forKey: "layout") as? String ?? DeskLayout.standard
        return i
    }

    // MARK: Upgrade from item 53's switch

    /// Set once the upgrade below has run, whatever it did.
    static let upgradedKey = "nowPlayingPlacementUpgraded"

    /// Item 53 had a switch (`nowPlaying`) and a Desk line under the meters (`nowPlayingDesk`, on
    /// by default) that sat under the Claude line with the meters off the desktop. Once, at
    /// launch: a saved "on" with the line on and no now playing in the layout places the element
    /// right after the widget the line sat under, in that widget's corner. After that the old keys
    /// are ignored.
    static func upgrade(_ d: DefaultsStore) {
        guard !d.bool(forKey: upgradedKey) else { return }
        d.set(true, forKey: upgradedKey)
        let layout = d.object(forKey: "layout") as? String ?? DeskLayout.standard
        guard let new = upgradedLayout(
            layout,
            enabled: d.bool(forKey: NowPlayingPrefs.enabledKey),
            deskLine: d.object(forKey: NowPlayingPrefs.deskLineKey) as? Bool ?? true,
            showClaude: d.object(forKey: "showClaude") as? Bool ?? true) else { return }
        d.set(new, forKey: "layout")
    }

    /// The layout with now playing placed where item 53's line was, or nil to leave it alone:
    /// the switch was off, the line was off, the layout already places now playing, or the line
    /// had nothing to sit under (it never showed). The new word goes right after its host's, so
    /// the rest of the layout keeps its order.
    static func upgradedLayout(_ layout: String, enabled: Bool, deskLine: Bool, showClaude: Bool) -> String? {
        guard enabled, deskLine, DeskLayout.parse(layout)[widget] == nil else { return nil }
        let placed = DeskLayout.placed(layout, showClaude: showClaude)
        let host = placed.contains("meters") ? "meters" : (placed.contains("claude") ? "claude" : nil)
        guard let host, let slot = DeskLayout.parse(layout)[host] else { return nil }
        var words = layout.split(separator: " ").map(String.init)
        // The host's last word wins in DeskLayout.parse, so insert after that one.
        guard let at = words.lastIndex(where: { $0 == "\(host):\(slot)" }) else { return nil }
        words.insert("\(widget):\(slot)", at: at + 1)
        return words.joined(separator: " ")
    }
}
