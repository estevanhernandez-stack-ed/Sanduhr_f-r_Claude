import Foundation

extension UserDefaults {
    /// Desk's own settings suite (com.626labs.sanduhr.desk), kept apart from the widget's
    /// defaults so short keys like "font" or "left" never collide.
    static let desk = UserDefaults(suiteName: "com.626labs.sanduhr.desk") ?? .standard
}

/// One-time import from the standalone apps Desk grew out of (Desk in dotclaude, then
/// Sanduhr Desk). Someone who ran either gets Desk switched on with their layout, colors and
/// fonts as they left them. Their old settings stay where they were.
enum DeskMigration {
    static func run() {
        let d = UserDefaults.desk
        guard !d.bool(forKey: "migrated") else { return }
        var found = false
        for domain in ["com.626labs.sanduhrdesk", "com.estevan.desk"] {
            guard let old = UserDefaults.standard.persistentDomain(forName: domain) else { continue }
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
        }
        d.set(true, forKey: "migrated")
    }
}
