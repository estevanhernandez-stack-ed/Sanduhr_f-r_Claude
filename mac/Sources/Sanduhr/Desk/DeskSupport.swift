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

    /// Not Desk settings: the old apps' own bookkeeping, and the alert settings Sanduhr Desk
    /// kept from its copy of the widget code. Sanduhr's alerts live in the widget's defaults.
    static let skipped: Set<String> = [
        "loginItemSet", "migratedFromDeskAndSanduhr",
        Notifier.Key.enabled, Notifier.Key.sessionPct, Notifier.Key.weeklyPct,
        Notifier.Key.sessionFull, Notifier.Key.sessionReset, Notifier.Key.fired,
    ]

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
            for (k, v) in old where d.object(forKey: k) == nil && !skipped.contains(k) {
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

/// One-time choice on first launch: a brand-new install starts with Desk as home (meters on the
/// desktop, meetings and the notch off, so no permission prompt), and the widget tucks away once
/// it has signed in and fetched. Anyone who already used Sanduhr, or is coming over from Sanduhr
/// Desk or Desk, keeps exactly what they have. Runs before DeskMigration, which would otherwise
/// mark every domain as migrated.
enum DeskFirstRun {
    /// Widget defaults: set once the choice has been made, whatever it was.
    static let doneKey = "deskFirstRunDone"
    /// Widget defaults: set on a fresh install until the first successful fetch hides the widget.
    static let tuckKey = "tuckAfterFirstFetch"
    /// The standard layout with the Claude line swapped for the meters.
    static let layout = "message:tl clock:bl meters:bl meetings:bl"

    /// Widget settings any earlier version writes. windowFrame is saved on the first move or
    /// resize; the others as soon as the setting is touched. The Keychain session key is not a
    /// signal: it outlives a wiped defaults domain, and a fresh install that already has one
    /// takes the same path (widget shows, then tucks after the first fetch).
    static let widgetKeys = [
        "windowFrame", "panelHidden", "theme", "fontFamily", "subtleMode",
        "pacingToolsEnabled", "snakeHighScore",
        Notifier.Key.enabled, Notifier.Key.sessionPct, Notifier.Key.weeklyPct,
        Notifier.Key.sessionFull, Notifier.Key.sessionReset, Notifier.Key.fired,
    ]

    enum Outcome: Equatable { case fresh, existing, migrant, alreadyDecided }

    /// Defaults to the real stores; tests pass in-memory ones. A Desk suite DeskMigration has
    /// already marked means 2.1.0 or later ran here, so that counts as an existing user too.
    @discardableResult
    static func run(widget w: DefaultsStore = UserDefaults.standard,
                    desk d: DefaultsStore = UserDefaults.desk,
                    from domains: [String] = DeskMigration.legacyDomains,
                    reading domain: (String) -> [String: Any]? = { UserDefaults.standard.persistentDomain(forName: $0) }) -> Outcome {
        guard !w.bool(forKey: doneKey) else { return .alreadyDecided }
        w.set(true, forKey: doneKey)
        if domains.contains(where: { domain($0) != nil }) { return .migrant }
        if d.object(forKey: "migrated") != nil || widgetKeys.contains(where: { w.object(forKey: $0) != nil }) {
            return .existing
        }
        d.set(true, forKey: DeskController.enabledKey)
        d.set(layout, forKey: "layout")
        d.set(false, forKey: "showMeetings")
        d.set(false, forKey: DeskController.notchKey)
        w.set(true, forKey: tuckKey)
        return .fresh
    }

    /// Called after every refresh. True once, on the first successful fetch of a fresh install:
    /// the caller hides the widget. The flag clears so the widget is never hidden for it again.
    static func tuck(afterFetch fetched: Bool, widget w: DefaultsStore = UserDefaults.standard) -> Bool {
        guard fetched, w.bool(forKey: tuckKey) else { return false }
        w.set(nil, forKey: tuckKey)
        return true
    }
}
