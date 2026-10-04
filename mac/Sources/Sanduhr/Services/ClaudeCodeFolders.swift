import Foundation

/// Finding the Claude Code homes an account can link (item 44), and the one fact Sanduhr reads
/// from them to suggest a link: the organization a home is signed in to.
///
/// A home is `~/.claude`, a `~/.claude-*` folder (a `CLAUDE_CONFIG_DIR` home such as
/// `~/.claude-work`), the folder `CLAUDE_CONFIG_DIR` names, or any folder chosen by hand, as long
/// as it looks like one: it holds `projects/` or its `.claude.json`.
///
/// **Placement of `.claude.json`** (Windows `McpIntegrationInstaller.ConfigPathFor`): Claude Code
/// keeps it inside the home when the home is selected through `CLAUDE_CONFIG_DIR`; for the
/// default `~/.claude` it lives beside the home, at `~/.claude.json`. A file inside `~/.claude`
/// that already exists wins.
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

    /// The `.claude.json` Claude Code reads for the home at `dir`.
    static func configFile(for dir: String, home: String,
                           fileManager fm: FileManager = .default) -> String {
        let d = AccountData.normalized(dir)
        let inner = (d as NSString).appendingPathComponent(configName)
        let defaultHome = (AccountData.normalized(home) as NSString).appendingPathComponent(defaultName)
        if d != defaultHome || fm.fileExists(atPath: inner) { return inner }
        return (AccountData.normalized(home) as NSString).appendingPathComponent(configName)
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

    /// The homes under `home`: `.claude` first, then the `.claude-*` folders by name, then the
    /// `CLAUDE_CONFIG_DIR` folder when it is somewhere else. Only folders that look like homes.
    static func discover(home: String, environment: [String: String] = [:],
                         fileManager fm: FileManager = .default) -> [Folder] {
        let h = AccountData.normalized(home)
        let names = (try? fm.contentsOfDirectory(atPath: h)) ?? []
        let candidates = names
            .filter { $0 == defaultName || ($0.hasPrefix(prefix) && $0.count > prefix.count) }
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
