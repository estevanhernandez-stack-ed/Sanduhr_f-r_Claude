import Foundation

/// Claude Code activity for an account (docs/mac-usage-data-spec.md): nothing, the live burn
/// only, or the live burn plus a kept record (the vault). Stored now; item 45 reads the logs and
/// item 46 writes the record.
enum ActivityChoice: String, CaseIterable, Sendable {
    case off, live, record

    var title: String {
        switch self {
        case .off: "Not tracked"
        case .live: "Live only"
        case .record: "Keep a record"
        }
    }
}

/// How the record names projects. Stored now; item 46 acts on it.
enum ProjectNamesChoice: String, CaseIterable, Sendable {
    case names, hidden, full

    var title: String {
        switch self {
        case .names: "Names"
        case .hidden: "Hidden"
        case .full: "Full paths"
        }
    }
}

/// What `sanduhr-mcp` may return for an account. Stored now; item 47 acts on it.
enum ShareChoice: String, CaseIterable, Sendable {
    case off, meters, activity

    var title: String {
        switch self {
        case .off: "Off"
        case .meters: "Meters"
        case .activity: "Meters and activity"
        }
    }
}

/// One account's data choices beside Meter history (which keeps its own key, `MeterHistory`).
/// `folder` is the linked Claude Code home as an absolute, standardized path: kept in defaults
/// only, never in a log, snapshot.json or state.yaml.
struct AccountDataChoices: Equatable, Sendable {
    var activity: ActivityChoice = .off
    var names: ProjectNamesChoice = .names
    var share: ShareChoice = .off
    var folder: String?
    /// Marked as a work account (item 66): watchers from its linked folder hide in demo mode.
    var work = false

    /// The spec's defaults: nothing linked, nothing tracked, names, nothing shared.
    static let defaults = AccountDataChoices()
}

/// The per-account data choices (item 44), in the registry's defaults under `accountData`:
/// `{label: {"activity": "off|live|record", "names": "names|hidden|full",
/// "share": "off|meters|activity", "folder": "/abs/path", "work": "true"}}`. A missing account, field or an
/// unknown value reads as the default. Follows renames and removals like `MeterHistory`.
///
/// Link rules: an account links at most one folder and a folder at most one account.
enum AccountData {
    static let key = "accountData"

    private enum Field {
        static let activity = "activity"
        static let names = "names"
        static let share = "share"
        static let folder = "folder"
        static let work = "work"
    }

    /// The label the choices are kept under: the account, or Personal while a launch runs on the
    /// legacy key with no accounts (as `MeterHistory.label`).
    static func label(_ account: String?) -> String {
        account ?? AccountRegistry.defaultLabel
    }

    private static func all(in store: DefaultsStore) -> [String: [String: String]] {
        store.object(forKey: key) as? [String: [String: String]] ?? [:]
    }

    private static func write(_ all: [String: [String: String]], in store: DefaultsStore) {
        store.set(all.isEmpty ? nil : all, forKey: key)
    }

    static func choices(for account: String?, in store: DefaultsStore) -> AccountDataChoices {
        guard let raw = all(in: store)[label(account)] else { return .defaults }
        var c = AccountDataChoices()
        if let v = raw[Field.activity].flatMap(ActivityChoice.init(rawValue:)) { c.activity = v }
        if let v = raw[Field.names].flatMap(ProjectNamesChoice.init(rawValue:)) { c.names = v }
        if let v = raw[Field.share].flatMap(ShareChoice.init(rawValue:)) { c.share = v }
        if let f = raw[Field.folder], !f.isEmpty { c.folder = f }
        if raw[Field.work] == "true" { c.work = true }
        return c
    }

    /// Every account's choices, for the view model.
    static func allChoices(in store: DefaultsStore) -> [String: AccountDataChoices] {
        var out: [String: AccountDataChoices] = [:]
        for name in all(in: store).keys { out[name] = choices(for: name, in: store) }
        return out
    }

    /// Writes an account's choices. Choices equal to the defaults drop the entry.
    private static func store(_ c: AccountDataChoices, for account: String?, in store: DefaultsStore) {
        var everything = all(in: store)
        let name = label(account)
        if c == .defaults {
            everything.removeValue(forKey: name)
        } else {
            var raw = [Field.activity: c.activity.rawValue, Field.names: c.names.rawValue,
                       Field.share: c.share.rawValue]
            if let f = c.folder { raw[Field.folder] = f }
            if c.work { raw[Field.work] = "true" }
            everything[name] = raw
        }
        write(everything, in: store)
    }

    static func setActivity(_ v: ActivityChoice, for account: String?, in s: DefaultsStore) {
        var c = choices(for: account, in: s)
        c.activity = v
        store(c, for: account, in: s)
    }

    static func setNames(_ v: ProjectNamesChoice, for account: String?, in s: DefaultsStore) {
        var c = choices(for: account, in: s)
        c.names = v
        store(c, for: account, in: s)
    }

    static func setShare(_ v: ShareChoice, for account: String?, in s: DefaultsStore) {
        var c = choices(for: account, in: s)
        c.share = v
        store(c, for: account, in: s)
    }

    static func setWork(_ v: Bool, for account: String?, in s: DefaultsStore) {
        var c = choices(for: account, in: s)
        c.work = v
        store(c, for: account, in: s)
    }

    // MARK: Folder links

    /// A folder path as links compare it: absolute and standardized (no `..`, no trailing slash).
    static func normalized(_ path: String) -> String {
        URL(fileURLWithPath: path).standardizedFileURL.path
    }

    /// The account `path` is linked to, if any.
    static func account(linkedTo path: String, in s: DefaultsStore) -> String? {
        let p = normalized(path)
        return all(in: s).first { $0.value[Field.folder].map(normalized) == p }?.key
    }

    enum LinkOutcome: Equatable {
        case linked
        /// The folder belongs to another account; nothing changed. Asking again with
        /// `move: true` (after a confirmation naming that account) moves it.
        case linkedElsewhere(String)
    }

    /// Links `path` to `account`, replacing the account's previous folder. A folder linked to
    /// another account moves only with `move`.
    @discardableResult
    static func link(_ path: String, to account: String?, move: Bool = false,
                     in s: DefaultsStore) -> LinkOutcome {
        let p = normalized(path)
        let name = label(account)
        if let other = self.account(linkedTo: p, in: s), other != name {
            guard move else { return .linkedElsewhere(other) }
            unlink(other, in: s)
        }
        var c = choices(for: name, in: s)
        c.folder = p
        store(c, for: name, in: s)
        return .linked
    }

    static func unlink(_ account: String?, in s: DefaultsStore) {
        var c = choices(for: account, in: s)
        guard c.folder != nil else { return }
        c.folder = nil
        store(c, for: account, in: s)
    }

    // MARK: Rename and remove

    /// The choices and the link follow a renamed account.
    static func rename(_ old: String, to new: String, in s: DefaultsStore) {
        var everything = all(in: s)
        guard let raw = everything.removeValue(forKey: old) else { return }
        everything[new] = raw
        write(everything, in: s)
    }

    /// Remove Account forgets them, so its folder is free and a new account with the same label
    /// starts from the defaults.
    static func forget(_ account: String, in s: DefaultsStore) {
        var everything = all(in: s)
        guard everything.removeValue(forKey: account) != nil else { return }
        write(everything, in: s)
    }
}
