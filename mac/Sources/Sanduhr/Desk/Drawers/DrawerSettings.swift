import Foundation

/// How a drawer blends into the Desk (docs/mac-drawers-setup-spec.md, section 7). Stored here, drawn
/// by the panels (item 5).
enum DrawerLook: String, Codable, CaseIterable, Sendable { case solid, glass, outline, bare }

/// Where Tidy lines the drawers up, and which way.
enum DrawerCorner: String, Codable, CaseIterable, Sendable { case topLeft, topRight, bottomLeft, bottomRight }
enum DrawerFlow: String, Codable, CaseIterable, Sendable { case column, row }

/// Where a drawer sits on one screen: an offset from that screen's visible frame, so a layout
/// survives a resolution change. `screen` names the display; it carries no file information.
struct DrawerPlace: Equatable, Codable, Sendable {
    var screen: String
    var x: Double
    var y: Double
}

/// Drawers' preferences, as loaded. Look, places, folded, hidden and tidy are stored values here;
/// the panels (item 5) read and change them. A missing key reads as its default, so an upgrade
/// changes nothing.
struct DrawerSettings: Equatable, Sendable {
    static let opacityRange = 20...100
    static let defaultOpacity = 55

    var enabled = false
    /// The Drawers root the user picked, or nil for `<Desktop>/Drawers`.
    var root: String?
    var drawers: [DrawerDef] = []
    var rules: [DrawerRule] = DrawerRules.defaults
    var look: DrawerLook = .solid
    var opacity = defaultOpacity
    /// A drawer's own look, by id, when it differs from `look`.
    var looks: [String: DrawerLook] = [:]
    var places: [String: DrawerPlace] = [:]
    var folded: [String] = []
    var hidden: [String] = []
    var tidyCorner: DrawerCorner = .topRight
    var tidyFlow: DrawerFlow = .column

    func lookFor(_ id: String) -> DrawerLook { looks[id] ?? look }
}

/// Reads and writes `DrawerSettings` under `drawers.*` in a defaults suite. The caller passes the
/// suite (production: `UserDefaults.desk`), so a test can hand it a throwaway one.
enum DrawerPrefs {
    static let prefix = "drawers."
    enum Key {
        static let enabled = prefix + "enabled"
        static let root = prefix + "root"
        static let drawers = prefix + "drawers"
        static let rules = prefix + "rules"
        static let look = prefix + "look"
        static let opacity = prefix + "opacity"
        static let looks = prefix + "looks"
        static let places = prefix + "places"
        static let folded = prefix + "folded"
        static let hidden = prefix + "hidden"
        static let tidyCorner = prefix + "tidy.corner"
        static let tidyFlow = prefix + "tidy.direction"
    }

    static func load(from d: UserDefaults) -> DrawerSettings {
        var s = DrawerSettings()
        s.enabled = d.bool(forKey: Key.enabled)
        if let r = d.string(forKey: Key.root), !r.isEmpty { s.root = r }
        s.drawers = unique(decode([DrawerDef].self, d, Key.drawers) ?? []) { $0.id }
            .filter { DrawerIds.isValid($0.id) && DrawerNames.isValid($0.name) && !$0.home.isEmpty }
        if let rules = decode([DrawerRule].self, d, Key.rules) {
            s.rules = rules.filter { DrawerIds.isValid($0.drawer) }.map { r in var r = r; r.days = DrawerRules.clampDays(r.days); return r }
        }
        s.look = d.string(forKey: Key.look).flatMap(DrawerLook.init(rawValue:)) ?? .solid
        if let o = d.object(forKey: Key.opacity) as? Int, DrawerSettings.opacityRange.contains(o) { s.opacity = o }
        if let m = d.dictionary(forKey: Key.looks) as? [String: String] {
            s.looks = m.reduce(into: [:]) { out, kv in
                if DrawerIds.isValid(kv.key), let l = DrawerLook(rawValue: kv.value) { out[kv.key] = l }
            }
        }
        s.places = (decode([String: DrawerPlace].self, d, Key.places) ?? [:]).filter { DrawerIds.isValid($0.key) && $0.value.x.isFinite && $0.value.y.isFinite }
        s.folded = ids(d.stringArray(forKey: Key.folded))
        s.hidden = ids(d.stringArray(forKey: Key.hidden))
        s.tidyCorner = d.string(forKey: Key.tidyCorner).flatMap(DrawerCorner.init(rawValue:)) ?? .topRight
        s.tidyFlow = d.string(forKey: Key.tidyFlow).flatMap(DrawerFlow.init(rawValue:)) ?? .column
        return s
    }

    static func save(_ s: DrawerSettings, to d: UserDefaults) {
        d.set(s.enabled, forKey: Key.enabled)
        d.set(s.root ?? "", forKey: Key.root)
        encode(s.drawers, d, Key.drawers)
        encode(s.rules, d, Key.rules)
        d.set(s.look.rawValue, forKey: Key.look)
        d.set(s.opacity, forKey: Key.opacity)
        d.set(s.looks.mapValues(\.rawValue), forKey: Key.looks)
        encode(s.places, d, Key.places)
        d.set(s.folded, forKey: Key.folded)
        d.set(s.hidden, forKey: Key.hidden)
        d.set(s.tidyCorner.rawValue, forKey: Key.tidyCorner)
        d.set(s.tidyFlow.rawValue, forKey: Key.tidyFlow)
    }

    /// Removes every `drawers.*` key. Returns true when none is left.
    @discardableResult
    static func clear(_ d: UserDefaults) -> Bool {
        for k in d.dictionaryRepresentation().keys where k.hasPrefix(prefix) { d.removeObject(forKey: k) }
        return !d.dictionaryRepresentation().keys.contains { $0.hasPrefix(prefix) }
    }

    // JSON text keeps a list of structs in one key and round-trips through Codable exactly.
    private static func decode<T: Decodable>(_ type: T.Type, _ d: UserDefaults, _ key: String) -> T? {
        d.string(forKey: key).flatMap { $0.data(using: .utf8) }.flatMap { try? JSONDecoder().decode(type, from: $0) }
    }

    private static func encode<T: Encodable>(_ value: T, _ d: UserDefaults, _ key: String) {
        let enc = JSONEncoder()
        enc.outputFormatting = [.sortedKeys, .withoutEscapingSlashes]
        if let data = try? enc.encode(value), let text = String(data: data, encoding: .utf8) { d.set(text, forKey: key) }
    }

    private static func ids(_ list: [String]?) -> [String] {
        var seen = Set<String>()
        return (list ?? []).filter { DrawerIds.isValid($0) && seen.insert($0).inserted }
    }

    private static func unique<T>(_ list: [T], by id: (T) -> String) -> [T] {
        var seen = Set<String>()
        return list.filter { seen.insert(id($0)).inserted }
    }
}
