import Foundation
import Testing
@testable import Sanduhr

/// A made-up home folder under the temp directory. Nothing here reads the real ~/.claude*.
struct TempHome {
    let path: String

    init() {
        let dir = FileManager.default.temporaryDirectory
            .appendingPathComponent("sanduhr-home-\(UUID().uuidString)", isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        path = AccountData.normalized(dir.path)
    }

    func cleanUp() { try? FileManager.default.removeItem(atPath: path) }

    func at(_ relative: String) -> String { (path as NSString).appendingPathComponent(relative) }

    func dir(_ relative: String) {
        try? FileManager.default.createDirectory(atPath: at(relative), withIntermediateDirectories: true)
    }

    func file(_ relative: String, _ text: String = "{}") {
        FileManager.default.createFile(atPath: at(relative), contents: Data(text.utf8))
    }

    /// A `.claude.json` as Claude Code writes it, with fields Sanduhr must never decode.
    static func config(org: String) -> String {
        """
        {"numStartups": 12, "projects": {"/Users/someone/secret-project": {}},
         "oauthAccount": {"accountUuid": "acct-1", "emailAddress": "someone@example.com",
                          "organizationUuid": "\(org)", "organizationName": "Example Org",
                          "displayName": "Someone"}}
        """
    }
}

@Suite("Claude Code folders")
struct ClaudeCodeFoldersTests {
    @Test func findsHomesByProjectsOrConfig() {
        let h = TempHome()
        defer { h.cleanUp() }
        h.dir(".claude/projects")
        h.dir(".claude-work")
        h.file(".claude-work/.claude.json")
        h.dir(".claude-b/projects")
        h.dir(".claude-empty")                 // no projects, no .claude.json
        h.dir(".claudette/projects")           // not .claude-*
        h.dir(".claude-/projects")             // nothing after the dash
        h.file(".claude-file")                 // a file, not a folder
        h.dir(".claude-notdir")
        h.file(".claude-notdir/projects")      // projects must be a folder
        let found = ClaudeCodeFolders.discover(home: h.path)
        #expect(found.map(\.name) == [".claude", ".claude-b", ".claude-work"])
        #expect(found.allSatisfy { $0.path.hasPrefix(h.path + "/") })
    }

    @Test func nothingFoundInAnEmptyOrMissingHome() {
        let h = TempHome()
        defer { h.cleanUp() }
        #expect(ClaudeCodeFolders.discover(home: h.path).isEmpty)
        #expect(ClaudeCodeFolders.discover(home: h.at("missing")).isEmpty)
    }

    /// The Windows placement rule: beside the home for the default `.claude`, inside it for a
    /// CLAUDE_CONFIG_DIR home; an existing inner file wins for `.claude` too.
    @Test func configPlacement() {
        let h = TempHome()
        defer { h.cleanUp() }
        h.dir(".claude")
        h.dir(".claude-x")
        #expect(ClaudeCodeFolders.configFile(for: h.at(".claude"), home: h.path) == h.at(".claude.json"))
        #expect(ClaudeCodeFolders.configFile(for: h.at(".claude-x"), home: h.path) == h.at(".claude-x/.claude.json"))
        h.file(".claude/.claude.json")
        #expect(ClaudeCodeFolders.configFile(for: h.at(".claude"), home: h.path) == h.at(".claude/.claude.json"))
    }

    @Test func theDefaultHomeCountsByTheFileBesideIt() {
        let h = TempHome()
        defer { h.cleanUp() }
        h.dir(".claude")
        h.dir(".claude-x")
        h.file(".claude.json")
        // ~/.claude.json belongs to ~/.claude only, never to a ~/.claude-* folder.
        #expect(ClaudeCodeFolders.discover(home: h.path).map(\.name) == [".claude"])
    }

    @Test func includesTheConfigDirFromTheEnvironment() {
        let h = TempHome()
        defer { h.cleanUp() }
        h.dir("elsewhere/cc/projects")
        h.dir(".claude-a/projects")
        let found = ClaudeCodeFolders.discover(home: h.path, environment: ["CLAUDE_CONFIG_DIR": h.at("elsewhere/cc/")])
        #expect(found.map(\.path) == [h.at(".claude-a"), h.at("elsewhere/cc")])
        // Already found: not listed twice.
        let twice = ClaudeCodeFolders.discover(home: h.path, environment: ["CLAUDE_CONFIG_DIR": h.at(".claude-a")])
        #expect(twice.count == 1)
    }

    @Test func aChosenFolderMustLookLikeAHome() {
        let h = TempHome()
        defer { h.cleanUp() }
        h.dir("cc-home/projects")
        h.dir("cc-config")
        h.file("cc-config/.claude.json")
        h.dir("documents")
        #expect(ClaudeCodeFolders.looksLikeHome(h.at("cc-home"), home: h.path))
        #expect(ClaudeCodeFolders.looksLikeHome(h.at("cc-config"), home: h.path))
        #expect(!ClaudeCodeFolders.looksLikeHome(h.at("documents"), home: h.path))
        #expect(!ClaudeCodeFolders.looksLikeHome(h.at("nowhere"), home: h.path))
    }

    @Test func displaysUnderTheHomeWithATilde() {
        let f = ClaudeCodeFolders.Folder(path: "/Users/u/.claude-work")
        #expect(f.display(home: "/Users/u") == "~/.claude-work")
        #expect(f.display(home: "/Users/u/") == "~/.claude-work")
        #expect(f.display(home: "/Users/someone") == "/Users/u/.claude-work")
    }

    // MARK: The organization field

    @Test func readsOnlyTheOrganization() {
        let h = TempHome()
        defer { h.cleanUp() }
        h.dir(".claude-work")
        h.file(".claude-work/.claude.json", TempHome.config(org: "org-work"))
        h.dir(".claude")
        h.file(".claude.json", TempHome.config(org: "org-home"))
        #expect(ClaudeCodeFolders.organizationUuid(of: h.at(".claude-work"), home: h.path) == "org-work")
        #expect(ClaudeCodeFolders.organizationUuid(of: h.at(".claude"), home: h.path) == "org-home")
    }

    /// The decoded type holds the one field and nothing else: no email, names or org name.
    @Test func theDecodedShapeHasOneField() throws {
        let decoded = try JSONDecoder().decode(ClaudeCodeFolders.SignedInOrganization.self,
                                               from: Data(TempHome.config(org: "o").utf8))
        let top = Mirror(reflecting: decoded).children.map(\.label)
        #expect(top == ["oauthAccount"])
        let inner = try #require(decoded.oauthAccount)
        #expect(Mirror(reflecting: inner).children.map(\.label) == ["organizationUuid"])
    }

    @Test func missingOrBrokenConfigsReadAsNothing() {
        let h = TempHome()
        defer { h.cleanUp() }
        for (name, text) in [("a", "not json"), ("b", "{}"), ("c", "{\"oauthAccount\": {}}"),
                             ("d", "{\"oauthAccount\": {\"organizationUuid\": \"\"}}"),
                             ("e", "{\"oauthAccount\": {\"organizationUuid\": 42}}")] {
            h.dir(".claude-\(name)")
            h.file(".claude-\(name)/.claude.json", text)
            #expect(ClaudeCodeFolders.organizationUuid(of: h.at(".claude-\(name)"), home: h.path) == nil, "\(name)")
        }
        h.dir(".claude-none/projects")
        #expect(ClaudeCodeFolders.organizationUuid(of: h.at(".claude-none"), home: h.path) == nil)
    }

    // MARK: The suggestion

    static let a = ClaudeCodeFolders.Folder(path: "/h/.claude")
    static let b = ClaudeCodeFolders.Folder(path: "/h/.claude-b")
    static let c = ClaudeCodeFolders.Folder(path: "/h/.claude-c")
    static let orgs = ["/h/.claude": "org-1", "/h/.claude-b": "org-2", "/h/.claude-c": "org-2"]

    static func suggest(_ org: String?, linkedElsewhere: Set<String> = []) -> ClaudeCodeFolders.Folder? {
        ClaudeCodeFolders.suggestion(among: [a, b, c], organization: org,
                                     isLinkedElsewhere: { linkedElsewhere.contains($0.path) },
                                     organizationOf: { orgs[$0.path] })
    }

    @Test func suggestsTheOneMatchingFolder() {
        #expect(Self.suggest("org-1") == Self.a)
    }

    @Test func noMatchOrSeveralSuggestNothing() {
        #expect(Self.suggest("org-9") == nil)
        #expect(Self.suggest("org-2") == nil)
        #expect(Self.suggest(nil) == nil)
        #expect(Self.suggest("") == nil)
    }

    @Test func aFolderOfAnotherAccountIsNotSuggested() {
        #expect(Self.suggest("org-1", linkedElsewhere: ["/h/.claude"]) == nil)
        // One of two matches taken: the other one is the suggestion.
        #expect(Self.suggest("org-2", linkedElsewhere: ["/h/.claude-b"]) == Self.c)
    }

    @Test func suggestsWithRealFilesEndToEnd() {
        let h = TempHome()
        defer { h.cleanUp() }
        h.dir(".claude/projects")
        h.file(".claude.json", TempHome.config(org: "org-home"))
        h.dir(".claude-work/projects")
        h.file(".claude-work/.claude.json", TempHome.config(org: "org-work"))
        let found = ClaudeCodeFolders.discover(home: h.path)
        let pick = ClaudeCodeFolders.suggestion(among: found, organization: "org-work",
                                                organizationOf: { ClaudeCodeFolders.organizationUuid(of: $0.path, home: h.path) })
        #expect(pick?.name == ".claude-work")
    }
}
