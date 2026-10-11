import Foundation

/// Finding the Claude Code homes an account can link (item 44), and the one fact Sanduhr reads
/// from them to suggest a link: the organization a home is signed in to.
///
/// A home is `~/.claude`, a `~/.claude-*` folder (a `CLAUDE_CONFIG_DIR` home such as
/// `~/.claude-work`), the folder `CLAUDE_CONFIG_DIR` names, or any folder chosen by hand, as long
/// as it looks like one: it holds `projects/` or its `.claude.json`.
///
/// **Placement of `.claude.json`** (item 71, slice 0): Claude Code keeps it inside the home when
/// the home is selected through `CLAUDE_CONFIG_DIR`, and beside the default `~/.claude`, at
/// `~/.claude.json`, when the variable is unset. A launch with `CLAUDE_CONFIG_DIR` naming
/// `~/.claude` itself uses `~/.claude/.claude.json` instead, and creates a fresh one (no sign-in)
/// when it is missing: that is how a stub appears beside the live file (checked against Claude
/// Code 2.1.296 in a scratch home; docs/mods-and-config-spec.md, open question 1). So for
/// `~/.claude` with both files, the one signed in (`oauthAccount`) wins, then the newer one, and a
/// tie goes to `~/.claude.json`, Claude Code's own default. Remove never uses this rule: it
/// edits the file its receipt names.
///
/// **What is read.** Only `oauthAccount.organizationUuid`, decoded into a type with that one
/// field: the email, names and organization name beside it are never decoded or kept. The uuid
/// is compared in memory and never stored or logged. Paths never go into a log either.
enum ClaudeCodeFolders {
    static let defaultName = ".claude"
    static let prefix = ".claude-"
    static let configName = ".claude.json"

    /// A home Sanduhr found or was given. `path` is absolute and standardized.
    struct Folder: Equatable, Hashable, Identifiable, Sendable {
        let path: String
        var id: String { path }
        var name: String { (path as NSString).lastPathComponent }

        /// The path as Settings shows it, `~/.claude-work` under the home folder.
        func display(home: String) -> String {
            let h = AccountData.normalized(home)
            if path == h { return "~" }
            return path.hasPrefix(h + "/") ? "~" + path.dropFirst(h.count) : path
        }
    }

    /// The `.claude.json` Claude Code reads for the home at `dir`, by the placement rule above.
    static func configFile(for dir: String, home: String,
                           fileManager fm: FileManager = .default) -> String {
        let candidates = configCandidates(for: dir, home: home, fileManager: fm)
        return candidates.first(where: \.inUse)?.path ?? candidates[0].path
    }

    /// One place a home's `.claude.json` can be. Facts only: whether it is signed in is the
    /// presence of `oauthAccount`, never its contents.
    struct ConfigCandidate: Equatable, Sendable {
        let path: String
        let exists: Bool
        /// Holds an `oauthAccount` object.
        let signedIn: Bool
        let modified: Date?
        /// The placement rule's answer.
        let inUse: Bool

        func with(inUse: Bool) -> ConfigCandidate {
            ConfigCandidate(path: path, exists: exists, signedIn: signedIn, modified: modified, inUse: inUse)
        }
    }

    /// Where the home at `dir` can keep its `.claude.json`: `~/.claude.json` then
    /// `~/.claude/.claude.json` for the default home, the inner file alone for any other. Exactly
    /// one is `inUse`. Settings shows both when both exist.
    static func configCandidates(for dir: String, home: String,
                                 fileManager fm: FileManager = .default) -> [ConfigCandidate] {
        let d = AccountData.normalized(dir)
        let h = AccountData.normalized(home)
        let inner = (d as NSString).appendingPathComponent(configName)
        guard d == (h as NSString).appendingPathComponent(defaultName) else {
            return [candidate(inner, inUse: true, fm)]
        }
        let outer = (h as NSString).appendingPathComponent(configName)
        let o = candidate(outer, inUse: false, fm), i = candidate(inner, inUse: false, fm)
        let innerWins: Bool
        switch (o.exists, i.exists) {
        case (false, true): innerWins = true
        case (true, true):
            if o.signedIn != i.signedIn {
                innerWins = i.signedIn
            } else if let om = o.modified, let im = i.modified {
                innerWins = im > om
            } else {
                innerWins = false
            }
        default: innerWins = false
        }
        return [o.with(inUse: !innerWins), i.with(inUse: innerWins)]
    }

    private static func candidate(_ path: String, inUse: Bool, _ fm: FileManager) -> ConfigCandidate {
        var isDir: ObjCBool = false
        let exists = fm.fileExists(atPath: path, isDirectory: &isDir) && !isDir.boolValue
        let modified = exists ? (try? fm.attributesOfItem(atPath: path))?[.modificationDate] as? Date : nil
        return ConfigCandidate(path: path, exists: exists, signedIn: exists && isSignedIn(path, fm), modified: modified, inUse: inUse)
    }

    /// The only part of `.claude.json` the placement rule decodes: whether `oauthAccount` is an
    /// object. Nothing inside it is decoded.
    private struct SignedIn: Decodable {
        /// An object, whatever it holds; its members are never read.
        struct Present: Decodable {
            private struct NoKeys: CodingKey {
                var stringValue: String
                var intValue: Int? { nil }
                init?(stringValue: String) { self.stringValue = stringValue }
                init?(intValue: Int) { nil }
            }

            init(from decoder: Decoder) throws {
                _ = try decoder.container(keyedBy: NoKeys.self)
            }
        }
        let oauthAccount: Present?
    }

    private static func isSignedIn(_ path: String, _ fm: FileManager) -> Bool {
        guard let data = fm.contents(atPath: path),
              let decoded = try? JSONDecoder().decode(SignedIn.self, from: data) else { return false }
        return decoded.oauthAccount != nil
    }

    /// Whether `dir` is a directory that looks like a Claude Code home: it holds a `projects`
    /// folder, or its `.claude.json` (by the placement rule) exists.
    static func looksLikeHome(_ dir: String, home: String, fileManager fm: FileManager = .default) -> Bool {
        let d = AccountData.normalized(dir)
        var isDir: ObjCBool = false
        guard fm.fileExists(atPath: d, isDirectory: &isDir), isDir.boolValue else { return false }
        var projectsIsDir: ObjCBool = false
        if fm.fileExists(atPath: (d as NSString).appendingPathComponent("projects"), isDirectory: &projectsIsDir),
           projectsIsDir.boolValue {
            return true
        }
        return fm.fileExists(atPath: configFile(for: d, home: home, fileManager: fm))
    }

    /// A copy a config tool left beside a home (`.claude-work.config-backup-20261001-174332`, or
    /// `.claude-backup-20261001` from a seat's migration): it looks like a home but nothing loads
    /// it, so discovery skips it.
    static let backupMarker = ".config-backup-"
    static let backupPrefix = ".claude-backup-"

    static func isBackup(_ name: String) -> Bool { name.contains(backupMarker) || name.hasPrefix(backupPrefix) }

    /// The homes under `home`: `.claude` first, then the `.claude-*` folders by name (never a
    /// `*.config-backup-*` or `.claude-backup-*` copy), then the `CLAUDE_CONFIG_DIR` folder when it is somewhere else,
    /// even when its name looks like a backup (that is the one Claude Code uses). Only folders
    /// that look like homes.
    static func discover(home: String, environment: [String: String] = [:],
                         fileManager fm: FileManager = .default) -> [Folder] {
        let h = AccountData.normalized(home)
        let names = (try? fm.contentsOfDirectory(atPath: h)) ?? []
        let candidates = names
            .filter { $0 == defaultName || ($0.hasPrefix(prefix) && $0.count > prefix.count) }
            .filter { !isBackup($0) }
            .sorted { a, b in
                if a == defaultName { return b != defaultName }
                if b == defaultName { return false }
                return a.localizedStandardCompare(b) == .orderedAscending
            }
            .map { (h as NSString).appendingPathComponent($0) }
        var found: [Folder] = []
        for path in candidates where looksLikeHome(path, home: h, fileManager: fm) {
            found.append(Folder(path: AccountData.normalized(path)))
        }
        if let env = environment["CLAUDE_CONFIG_DIR"], !env.isEmpty {
            let p = AccountData.normalized((env as NSString).expandingTildeInPath)
            if !found.contains(where: { $0.path == p }), looksLikeHome(p, home: h, fileManager: fm) {
                found.append(Folder(path: p))
            }
        }
        return found
    }

    /// The only part of `.claude.json` Sanduhr decodes.
    struct SignedInOrganization: Decodable {
        struct OAuthAccount: Decodable {
            let organizationUuid: String?
        }
        let oauthAccount: OAuthAccount?
    }

    /// The organization the home at `dir` is signed in to, or nil (no file, unreadable, not
    /// signed in). Decodes `oauthAccount.organizationUuid` alone. Never logged.
    static func organizationUuid(of dir: String, home: String,
                                 fileManager fm: FileManager = .default) -> String? {
        let file = configFile(for: dir, home: home, fileManager: fm)
        guard let data = fm.contents(atPath: file),
              let decoded = try? JSONDecoder().decode(SignedInOrganization.self, from: data),
              let uuid = decoded.oauthAccount?.organizationUuid, !uuid.isEmpty else { return nil }
        return uuid
    }

    /// The folder to suggest for an account whose organization is `organization`: the one found
    /// folder signed in to it, never already linked to another account. No organization, no
    /// match or several matches suggest nothing, and the found folders are offered as a list.
    /// The uuids are compared here and dropped.
    static func suggestion(among folders: [Folder], organization: String?,
                           isLinkedElsewhere: (Folder) -> Bool = { _ in false },
                           organizationOf: (Folder) -> String?) -> Folder? {
        guard let organization, !organization.isEmpty else { return nil }
        let matches = folders.filter { !isLinkedElsewhere($0) && organizationOf($0) == organization }
        return matches.count == 1 ? matches[0] : nil
    }
}
