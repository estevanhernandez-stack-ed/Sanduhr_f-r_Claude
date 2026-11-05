import Foundation

/// What Sanduhr shows (setup spec, section 10): the Desk's own pieces and each usage source.
/// Drawers keep their own switch; the Desk runs when desktop or drawers is on. Stored in standard
/// defaults under `setup` as `{desktop: Bool, usage: {claude: Bool}}`. Absent means no setup has
/// been made yet (a first run).
struct SetupSettings: Equatable {
    static let key = "setup"
    static let claude = "claude"

    var desktop: Bool
    var usage: [String: Bool]

    var claudeUsage: Bool { usage[Self.claude] ?? false }
    /// Any usage source on.
    var usageOn: Bool { usage.values.contains(true) }

    func withClaude(_ on: Bool) -> SetupSettings {
        var copy = self
        copy.usage[Self.claude] = on
        return copy
    }

    func encode() -> [String: Any] {
        ["desktop": desktop, "usage": usage]
    }

    /// Decoded entry by entry: an entry that is missing or not a bool keeps the fallback's value,
    /// so a damaged value never reads as nothing ticked.
    static func decode(_ raw: Any?, fallback: SetupSettings) -> SetupSettings {
        guard let dict = raw as? [String: Any] else { return fallback }
        var usage = fallback.usage
        if let u = dict["usage"] as? [String: Any] {
            for (source, value) in u {
                if let on = bool(value) { usage[source] = on }
            }
        }
        return SetupSettings(desktop: bool(dict["desktop"]) ?? fallback.desktop, usage: usage)
    }

    /// A real Bool only: a number or string is damage, not a tick.
    private static func bool(_ value: Any?) -> Bool? {
        guard let n = value as? NSNumber, CFGetTypeID(n) == CFBooleanGetTypeID() else { return nil }
        return n.boolValue
    }
}

/// The three ticks on the setup screen. Desktop reads as ticked only while the Desk runs.
struct SetupChoices: Equatable {
    var desktop: Bool
    var drawers: Bool
    var claudeUsage: Bool

    /// A first run's ticks: the desktop and drawers, no usage.
    static let firstRun = SetupChoices(desktop: true, drawers: true, claudeUsage: false)

    var anyTicked: Bool { desktop || drawers || claudeUsage }
    /// The Desk runs whenever desktop or drawers is ticked.
    var deskRuns: Bool { desktop || drawers }

    static func of(_ s: SetupSettings, drawersOn: Bool, deskEnabled: Bool) -> SetupChoices {
        SetupChoices(desktop: s.desktop && deskEnabled, drawers: drawersOn, claudeUsage: s.claudeUsage)
    }

    /// The stored setup for these ticks; sources this build does not show keep their value from `before`.
    func toSettings(before: SetupSettings?) -> SetupSettings {
        var usage = before?.usage ?? [:]
        usage[SetupSettings.claude] = claudeUsage
        return SetupSettings(desktop: desktop, usage: usage)
    }
}

/// Load and save of the setup in a defaults store (standard defaults in the app).
enum SetupStore {
    /// Standard defaults: set once the migration has looked at an older install.
    static let migratedKey = "setupMigrated"

    static func load(from store: DefaultsStore = UserDefaults.standard) -> SetupSettings? {
        guard let raw = store.object(forKey: SetupSettings.key) as? [String: Any] else { return nil }
        let blank = SetupSettings(desktop: false, usage: [:])
        return SetupSettings.decode(raw, fallback: blank)
    }

    static func save(_ setup: SetupSettings, to store: DefaultsStore = UserDefaults.standard) {
        store.set(setup.encode(), forKey: SetupSettings.key)
    }

    /// Claude usage is on. No stored setup reads as on (an upgrade before the migration has run
    /// keeps its meters); a stored setup without a Claude entry reads as on too, so damage never
    /// turns the account's meters off.
    static func claudeUsageOn(from store: DefaultsStore = UserDefaults.standard) -> Bool {
        guard let setup = load(from: store) else { return true }
        return setup.usage[SetupSettings.claude] ?? true
    }
}

/// What the first launch of a setup build makes of an older install.
struct SetupMigrationResult: Equatable {
    var setup: SetupSettings
    var deskOn: Bool
    var reminderOn: Bool
}

enum SetupMigration {
    /// Nil for a first run (no account, no Just the Desk): the setup screen opens. An account wins
    /// over Just the Desk, with desktop on and the Desk's own switch untouched, so an account never
    /// comes out usage-off. The sign-in reminder comes back only where Just the Desk had silenced it.
    static func from(hasAccount: Bool, justTheDesk: Bool, silencedReminder: Bool,
                     deskEnabled: Bool) -> SetupMigrationResult? {
        if hasAccount {
            return SetupMigrationResult(setup: SetupSettings(desktop: true, usage: [SetupSettings.claude: true]),
                                        deskOn: deskEnabled, reminderOn: justTheDesk && silencedReminder)
        }
        if justTheDesk {
            return SetupMigrationResult(setup: SetupSettings(desktop: true, usage: [SetupSettings.claude: false]),
                                        deskOn: true, reminderOn: silencedReminder)
        }
        return nil
    }

    /// Runs the migration once: saves the setup it makes and records that it ran. Returns the
    /// result for the caller to apply (the Desk switch, the reminder), or nil when it already ran
    /// or this is a first run. A setup already stored is never overwritten.
    @discardableResult
    static func runOnce(hasAccount: Bool, justTheDesk: Bool, silencedReminder: Bool, deskEnabled: Bool,
                        in store: DefaultsStore = UserDefaults.standard) -> SetupMigrationResult? {
        guard store.object(forKey: SetupStore.migratedKey) as? Bool != true else { return nil }
        store.set(true, forKey: SetupStore.migratedKey)
        guard SetupStore.load(from: store) == nil,
              let result = from(hasAccount: hasAccount, justTheDesk: justTheDesk,
                                silencedReminder: silencedReminder, deskEnabled: deskEnabled)
        else { return nil }
        SetupStore.save(result.setup, to: store)
        return result
    }
}

enum SetupLaunch {
    /// No setup yet: the setup screen opens first.
    static func opensSetup(_ setup: SetupSettings?) -> Bool { setup == nil }

    /// The widget starts hidden (the menu bar is the way back) with usage off and the Desk running;
    /// with the Desk off as well the widget shows, so a launch never shows nothing. Before any setup
    /// the setup screen is what shows.
    static func widgetStartsHidden(_ setup: SetupSettings?, deskEnabled: Bool) -> Bool {
        guard let setup else { return true }
        return !setup.usageOn && deskEnabled
    }

    /// The sign-in sheet (the welcome sheet's one remaining job): a setup with Track my AI usage ticked
    /// and no session key, not put off for this launch with Not Now. No setup yet means the setup
    /// screen is due instead.
    static func showsSignIn(_ setup: SetupSettings?, hasSessionKey: Bool, notNow: Bool) -> Bool {
        guard let setup else { return false }
        return setup.claudeUsage && !hasSessionKey && !notNow
    }

    /// A menu bar click while no setup exists yet and no account is signed in opens the setup
    /// screen. With an account the widget has meters to show, so the click toggles it as usual.
    static func menuBarClickOpensSetup(setupPending: Bool, hasAccount: Bool) -> Bool {
        setupPending && !hasAccount
    }
}
