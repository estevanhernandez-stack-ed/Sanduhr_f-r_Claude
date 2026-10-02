import Foundation

extension UserDefaults {
    /// Desk's own settings suite (com.626labs.sanduhr.desk), kept apart from the widget's
    /// defaults so short keys like "font" or "left" never collide.
    static let desk = UserDefaults(suiteName: "com.626labs.sanduhr.desk") ?? .standard
}

/// The slice of UserDefaults the migration uses, so tests can hand it a dictionary.
protocol DefaultsStore: AnyObject {
    func object(forKey defaultName: String) -> Any?
    func bool(forKey defaultName: String) -> Bool
    func set(_ value: Any?, forKey defaultName: String)
}

extension UserDefaults: DefaultsStore {}

/// One-time import from the standalone apps Desk grew out of (Desk in dotclaude, then
/// Sanduhr Desk). Someone who ran either gets Desk switched on with their layout, colors and
/// fonts as they left them. Their old settings stay where they were.
enum DeskMigration {
    /// Newest app first: when both domains hold a key, Sanduhr Desk's value wins.
    static let legacyDomains = ["com.626labs.sanduhrdesk", "com.estevan.desk"]

    /// Defaults to the real stores; tests pass in-memory ones, since a scratch defaults domain
    /// leaves a plist in ~/Library/Preferences that cfprefsd rewrites even after it is deleted.
    static func run(into d: DefaultsStore = UserDefaults.desk,
                    from domains: [String] = legacyDomains,
                    reading domain: (String) -> [String: Any]? = { UserDefaults.standard.persistentDomain(forName: $0) }) {
        guard !d.bool(forKey: "migrated") else { return }
        var found = false
        for name in domains {
            guard let old = domain(name) else { continue }
            found = true
            for (k, v) in old where d.object(forKey: k) == nil
                && !["loginItemSet", "migratedFromDeskAndSanduhr"].contains(k) {
                d.set(v, forKey: k)
            }
        }
        if found {
            d.set(true, forKey: DeskController.enabledKey)
            // The standalone apps defaulted to EsteFont; keep what was on screen.
            if d.object(forKey: "font") == nil { d.set("EsteFont 2.1", forKey: "font") }
            // Their notch was on unless switched off; Sanduhr's is off until switched on.
            if d.object(forKey: DeskController.notchKey) == nil { d.set(true, forKey: DeskController.notchKey) }
        }
        d.set(true, forKey: "migrated")
    }
}
