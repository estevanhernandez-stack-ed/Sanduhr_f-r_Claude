import Foundation

/// A seat's side of the wall (item 71). A Sanduhr preference or an account's flag, never
/// written into the seat.
enum SeatMark: String, CaseIterable, Sendable {
    case work, personal
    /// No account link and no toggle yet: the page asks. Treated as not personal by the wall.
    case unmarked
}

/// The seat preferences Sanduhr keeps for itself (item 71, slice 0), in the registry's defaults
/// under `seats`: `{"labels": {path: label}, "marks": {path: "work|personal"},
/// "added": [path]}`. Paths are absolute and standardized; they stay in defaults, never in a
/// log, snapshot.json or state.yaml. Nothing here is ever written into a seat folder.
///
/// Labels are the user's own words. Sanduhr never fills one from a seat's files (a seat's
/// identity file may carry an employer's name), so the default is the folder's name.
enum SeatPreferences {
    static let key = "seats"

    private enum Field {
        static let labels = "labels"
        static let marks = "marks"
        static let added = "added"
    }

    private static func all(_ s: DefaultsStore) -> [String: Any] {
        s.object(forKey: key) as? [String: Any] ?? [:]
    }

    private static func write(_ field: String, _ value: Any?, _ s: DefaultsStore) {
        var everything = all(s)
        everything[field] = value
        s.set(everything.isEmpty ? nil : everything, forKey: key)
    }

    // MARK: Labels

    static func label(for path: String, in s: DefaultsStore) -> String? {
        (all(s)[Field.labels] as? [String: String])?[AccountData.normalized(path)]
    }

    /// Sets the user's label; an empty or blank one goes back to the folder's name.
    static func setLabel(_ label: String?, for path: String, in s: DefaultsStore) {
        var labels = all(s)[Field.labels] as? [String: String] ?? [:]
        let trimmed = label?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        labels[AccountData.normalized(path)] = trimmed.isEmpty ? nil : trimmed
        write(Field.labels, labels.isEmpty ? nil : labels, s)
    }

    // MARK: Marks

    /// The per-seat toggle, nil when it was never set.
    static func mark(for path: String, in s: DefaultsStore) -> SeatMark? {
        (all(s)[Field.marks] as? [String: String])?[AccountData.normalized(path)].flatMap(SeatMark.init(rawValue:))
    }

    static func setMark(_ mark: SeatMark, for path: String, in s: DefaultsStore) {
        var marks = all(s)[Field.marks] as? [String: String] ?? [:]
        marks[AccountData.normalized(path)] = mark == .unmarked ? nil : mark.rawValue
        write(Field.marks, marks.isEmpty ? nil : marks, s)
    }

    // MARK: Folders added by hand

    static func added(in s: DefaultsStore) -> [String] {
        all(s)[Field.added] as? [String] ?? []
    }

    /// Remembers a folder chosen with Add Folder…. Already there: nothing changes.
    static func add(_ path: String, in s: DefaultsStore) {
        let p = AccountData.normalized(path)
        var list = added(in: s)
        guard !list.contains(p) else { return }
        list.append(p)
        write(Field.added, list, s)
    }

    static func forget(_ path: String, in s: DefaultsStore) {
        let p = AccountData.normalized(path)
        let list = added(in: s).filter { $0 != p }
        write(Field.added, list.isEmpty ? nil : list, s)
    }
}

/// One Claude Code config folder as Mods & Config sees it (item 71, slice 0): read-only facts
/// and Sanduhr's own preferences about it. Building one reads the folder's listing and the
/// placement facts of its `.claude.json`, nothing else, and writes nothing.
struct Seat: Identifiable, Equatable, Sendable {
    /// How Sanduhr knows the folder.
    enum Origin: String, Sendable {
        /// `~/.claude` or a `~/.claude-*` folder.
        case found
        /// The folder `CLAUDE_CONFIG_DIR` names in Sanduhr's own environment.
        case environment
        /// Chosen with Add Folder….
        case added
        /// An account's linked folder.
        case linked
        /// A folder Sanduhr installed into (its receipts).
        case installed
    }

    /// Where the mark comes from.
    enum MarkSource: String, Sendable {
        /// The linked account's work flag (Settings, Accounts, Data); only ever marks work.
        case account
        /// The per-seat toggle.
        case toggle
    }

    let path: String
    let origin: Origin
    /// `~/.claude`, the folder Claude Code uses when `CLAUDE_CONFIG_DIR` is unset.
    let isDefault: Bool
    /// The user's label, or the folder's name.
    let label: String
    let labelChosen: Bool
    let mark: SeatMark
    let markSource: MarkSource?
    /// Where its `.claude.json` can be, the one in use marked (both, for `~/.claude`).
    let config: [ClaudeCodeFolders.ConfigCandidate]

    var id: String { path }
    var name: String { (path as NSString).lastPathComponent }
    var isWork: Bool { mark == .work }

    /// The `.claude.json` the placement rule picks.
    var configFile: String { config.first(where: \.inUse)?.path ?? config.first?.path ?? path }

    /// Both places hold a `.claude.json` (the default seat only): the page shows both.
    var hasTwoConfigFiles: Bool { config.filter(\.exists).count > 1 }
}

enum Seats {
    /// Every seat, in the order Mods & Config lists them: found ones (`~/.claude` first), the
    /// `CLAUDE_CONFIG_DIR` folder, the ones added by hand, the accounts' linked ones, then the
    /// ones Sanduhr installed into. A path appears once, with its first origin. Added, linked
    /// and installed folders are kept only while they look like a Claude Code folder.
    static func discover(home: String, environment: [String: String] = [:], store: DefaultsStore,
                         linked: [String] = [], installed: [String] = [],
                         fileManager fm: FileManager = .default) -> [Seat] {
        let h = AccountData.normalized(home)
        let plain = ClaudeCodeFolders.discover(home: h, fileManager: fm).map(\.path)
        let withEnv = ClaudeCodeFolders.discover(home: h, environment: environment, fileManager: fm).map(\.path)
        var paths: [(String, Seat.Origin)] = plain.map { ($0, .found) }
        for p in withEnv where !plain.contains(p) { paths.append((p, .environment)) }
        let extra: [(String, Seat.Origin)] = SeatPreferences.added(in: store).map { ($0, .added) }
            + linked.map { ($0, .linked) } + installed.map { ($0, .installed) }
        for (raw, origin) in extra {
            let p = AccountData.normalized(raw)
            guard !paths.contains(where: { $0.0 == p }), ClaudeCodeFolders.looksLikeHome(p, home: h, fileManager: fm) else { continue }
            paths.append((p, origin))
        }
        return paths.map { seat($0.0, origin: $0.1, home: h, store: store, fileManager: fm) }
    }

    /// The seat at `path`, with its label, mark and placement facts.
    static func seat(_ path: String, origin: Seat.Origin, home: String, store: DefaultsStore,
                     fileManager fm: FileManager = .default) -> Seat {
        seat(path, origin: origin, home: home, store: store,
             config: ClaudeCodeFolders.configCandidates(for: path, home: home, fileManager: fm))
    }

    /// The seat at `path` with its placement facts already read (off the main thread), so only
    /// the preferences are read here.
    static func seat(_ path: String, origin: Seat.Origin, home: String, store: DefaultsStore,
                     config: [ClaudeCodeFolders.ConfigCandidate]) -> Seat {
        let p = AccountData.normalized(path)
        let h = AccountData.normalized(home)
        let chosen = SeatPreferences.label(for: p, in: store)
        let (mark, source) = mark(for: p, store: store)
        return Seat(path: p, origin: origin,
                    isDefault: p == (h as NSString).appendingPathComponent(ClaudeCodeFolders.defaultName),
                    label: chosen ?? (p as NSString).lastPathComponent, labelChosen: chosen != nil,
                    mark: mark, markSource: source, config: config)
    }

    /// A linked account marked work makes the seat work. A link never makes a seat personal (an
    /// account's work flag is off by default, so "not flagged" says nothing): otherwise the
    /// per-seat toggle decides, and unmarked when it was never set.
    static func mark(for path: String, store: DefaultsStore) -> (SeatMark, Seat.MarkSource?) {
        if let account = AccountData.account(linkedTo: path, in: store),
           AccountData.choices(for: account, in: store).work {
            return (.work, .account)
        }
        if let m = SeatPreferences.mark(for: path, in: store), m != .unmarked {
            return (m, .toggle)
        }
        return (.unmarked, nil)
    }
}

/// The wall's one direction rule (item 71): what may feed a copy, sync or export from one seat
/// into another. Later slices offer such a control only where this says yes; the control is
/// absent otherwise, never disabled with a tooltip.
enum SeatWall {
    /// Only a seat marked personal is a source. A work seat never is (not even into another work
    /// seat: the work tenant's own tools move work config), and an unmarked one isn't until it
    /// is marked. A seat never copies into itself.
    static func allowsCopy(from source: Seat, to target: Seat) -> Bool {
        allowsCopy(from: source.mark, to: target.mark) && source.path != target.path
    }

    static func allowsCopy(from source: SeatMark, to target: SeatMark) -> Bool {
        source == .personal
    }

    /// Values may be shown side by side (drift) only when neither seat is work or unmarked;
    /// across the wall, key names and "differs" only.
    static func showsValues(between a: Seat, and b: Seat) -> Bool {
        a.mark == .personal && b.mark == .personal
    }
}
