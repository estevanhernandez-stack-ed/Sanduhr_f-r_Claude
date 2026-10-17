import Foundation
import Testing
@testable import Sanduhr

/// Item 76, slice F: kept mods published into a clone of the user's own marketplace repo. A bare
/// repo in a made-up home stands in for GitHub.
@Suite("Packs, marketplace of your own")
struct ModMarketplaceTests {
    private static let at = Date(timeIntervalSince1970: 1_790_000_000)

    private func kept(_ h: TempHome, version: String = "0.1.0", body: String = "export function register() {}") throws -> (KeptMods, KeptMod) {
        let root = ".claude-personal/dev-mods/0123456789abcdef/ci-band-\(version)"
        h.dir("\(root)/.claude-plugin/types/claude-code")
        h.dir("\(root)/hooks")
        h.file("\(root)/.claude-plugin/plugin.json", #"{"name": "ci-band", "version": "\#(version)", "description": "CI runs above the prompt."}"#)
        h.file("\(root)/.claude-plugin/types/claude-code/index.d.ts", "declare const x: number")
        h.file("\(root)/hooks/hooks.json", #"{"modules": ["./register.tsx"]}"#)
        h.file("\(root)/hooks/register.tsx", body)
        let store = KeptMods(dir: URL(fileURLWithPath: h.at("support/mods")))
        let mod = try store.keep(h.at(root), session: "0123456789abcdef", folder: h.at(".claude-personal"), now: Self.at)
        return (store, mod)
    }

    @discardableResult
    private func git(_ args: [String], in dir: String? = nil) throws -> String {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: try #require(ModMarketplace.findGit()))
        p.arguments = (dir.map { ["-C", $0] } ?? []) + args
        var env = ProcessInfo.processInfo.environment
        env["GIT_CONFIG_GLOBAL"] = "/dev/null"
        p.environment = env
        let out = Pipe()
        p.standardOutput = out
        p.standardError = FileHandle.nullDevice
        try p.run()
        p.waitUntilExit()
        #expect(p.terminationStatus == 0, "git \(args.joined(separator: " "))")
        return String(decoding: out.fileHandleForReading.readDataToEndOfFile(), as: UTF8.self)
    }

    /// A bare `origin` and a clone of it on `main` with one commit and a committer set.
    private func clone(_ h: TempHome) throws -> URL {
        try git(["init", "-q", "--bare", "-b", "main", h.at("origin.git")])
        try git(["clone", "-q", h.at("origin.git"), h.at("my-mods")])
        let c = h.at("my-mods")
        try git(["config", "user.name", "Test"], in: c)
        try git(["config", "user.email", "test@example.com"], in: c)
        try git(["config", "commit.gpgsign", "false"], in: c)
        try git(["checkout", "-q", "-b", "main"], in: c)
        h.file("my-mods/LICENSE", "MIT")
        try git(["add", "LICENSE"], in: c)
        try git(["commit", "-q", "-m", "init"], in: c)
        try git(["push", "-q", "origin", "main"], in: c)
        return URL(fileURLWithPath: c)
    }

    @Test func gitHubRemotesBecomeOwnerSlashRepo() {
        for remote in ["https://github.com/someone/my-mods.git", "https://github.com/someone/my-mods",
                       "git@github.com:someone/my-mods.git", "ssh://git@github.com/someone/my-mods.git\n"] {
            #expect(ModMarketplace.source(fromRemote: remote) == "someone/my-mods")
        }
        #expect(ModMarketplace.source(fromRemote: "https://gitlab.com/someone/mods.git") == "https://gitlab.com/someone/mods.git")
        #expect(ModMarketplace.defaultName(source: "someone/My_Mods") == "my-mods")
        #expect(ModMarketplace.defaultName(source: "/tmp/x/origin.git") == "origin")
    }

    @Test func anEntryNamesItsAuthorAndKeepsTheUsersOwn() {
        let mod = KeptMod(name: "ci-band", version: "0.1.0", stamp: "abc", keptAt: Date(), session: "s", files: 1, bytes: 1)
        let repo = ModMarketplace.Repo(remote: "", branch: "main", source: "someone/my-mods", name: "my-mods")
        let entry = { (doc: [String: Any]) in (doc["plugins"] as? [[String: Any]])?.first?["author"] as? [String: String] }
        // The repo's owner, unless the mod names its own author (object or string).
        #expect(entry(ModMarketplace.manifest(nil, mod: mod, repo: repo)) == ["name": "someone"])
        #expect(entry(ModMarketplace.manifest(nil, mod: mod, repo: repo, author: ["name": "Ada", "email": "a@example.com"])) == ["name": "Ada"])
        #expect(entry(ModMarketplace.manifest(nil, mod: mod, repo: repo, author: "Ada")) == ["name": "Ada"])
        // A remote that isn't GitHub has no owner to name.
        let other = ModMarketplace.Repo(remote: "", branch: "main", source: "/tmp/origin.git", name: "origin")
        #expect(entry(ModMarketplace.manifest(nil, mod: mod, repo: other)) == nil)
        // An author the user wrote into the entry stays.
        let mine: [String: Any] = ["name": "my-mods", "plugins": [["name": "ci-band", "author": ["name": "Me"]]]]
        #expect(entry(ModMarketplace.manifest(mine, mod: mod, repo: repo)) == ["name": "Me"])
    }

    @Test func gitIsFoundWithoutTouchingTheStub() {
        #expect(ModMarketplace.findGit { $0 == "/usr/bin/git" } == nil)
        #expect(ModMarketplace.findGit { $0 == "/opt/homebrew/bin/git" } == "/opt/homebrew/bin/git")
        #expect(ModMarketplace.findGit { ["/usr/bin/git", "/Library/Developer/CommandLineTools/usr/bin/git"].contains($0) } == "/usr/bin/git")
    }

    @Test func aModIsCommittedPushedAndListed() throws {
        let h = TempHome()
        defer { h.cleanUp() }
        let (store, mod) = try kept(h)
        let folder = try clone(h)
        let market = ModMarketplace(folder: folder, gitPath: try #require(ModMarketplace.findGit()))
        let repo = try market.inspect()
        #expect(repo.branch == "main" && repo.name == "origin" && repo.source.hasSuffix("origin.git"))
        let keptURL = URL(fileURLWithPath: store.path(mod))
        let plan = try market.plan(mod, kept: keptURL, repo: repo)
        #expect(plan.change == .add && plan.canPublish && plan.files == 3)
        #expect(plan.commands == "claude plugin marketplace add \(repo.source)\nclaude plugin install ci-band@origin")

        let outcome = try market.publish(mod, kept: keptURL, plan: plan)
        #expect(outcome.pushFailure == nil && !outcome.commit.isEmpty)
        // The bare origin has the commit: what another machine would clone.
        let files = try git(["ls-tree", "-r", "--name-only", "main"], in: h.at("origin.git")).split(separator: "\n").map(String.init)
        #expect(files == [".claude-plugin/marketplace.json", "LICENSE", "README.md", "plugins/ci-band/.claude-plugin/plugin.json",
                          "plugins/ci-band/hooks/hooks.json", "plugins/ci-band/hooks/register.tsx"])
        #expect(try git(["log", "-1", "--format=%s", "main"], in: h.at("origin.git")) == "Publish ci-band 0.1.0\n")
        let manifest = try #require(try market.readManifest())
        #expect(manifest["name"] as? String == "origin")
        let plugins = try #require(manifest["plugins"] as? [[String: Any]])
        #expect(plugins.count == 1 && plugins[0]["source"] as? String == "./plugins/ci-band" && plugins[0]["version"] as? String == "0.1.0")

        let readme = try String(contentsOf: folder.appendingPathComponent("README.md"), encoding: .utf8)
        #expect(readme.contains("claude plugin marketplace add \(repo.source)"))
        #expect(readme.contains("- **ci-band** 0.1.0: CI runs above the prompt."))
        #expect(readme.contains("claude plugin install ci-band --marketplace \(repo.source)"))

        // Published again unchanged, there is nothing to commit.
        #expect(try market.plan(mod, kept: keptURL, repo: try market.inspect()).change == .same)
    }

    @Test func aNewVersionReplacesTheOldAndKeepsOtherPlugins() throws {
        let h = TempHome()
        defer { h.cleanUp() }
        let folder = try clone(h)
        h.dir("my-mods/.claude-plugin")
        h.dir("my-mods/plugins/ci-band")
        h.file("my-mods/.claude-plugin/marketplace.json", #"{"name": "team-mods", "owner": {"name": "Someone"}, "plugins": [{"name": "other", "source": "./plugins/other"}, {"name": "ci-band", "source": "./plugins/ci-band", "version": "0.0.9", "category": "ui"}]}"#)
        h.file("my-mods/plugins/ci-band/old.tsx", "old")
        h.file("my-mods/README.md", "# Mine\n\n<!-- sanduhr:plugins -->\nstale\n<!-- /sanduhr:plugins -->\n\nMy words.\n")
        try git(["add", "-A"], in: folder.path)
        try git(["commit", "-q", "-m", "seed"], in: folder.path)
        let (store, mod) = try kept(h, version: "0.2.0")
        let market = ModMarketplace(folder: folder, gitPath: try #require(ModMarketplace.findGit()))
        let repo = try market.inspect()
        #expect(repo.name == "team-mods")
        let keptURL = URL(fileURLWithPath: store.path(mod))
        let plan = try market.plan(mod, kept: keptURL, repo: repo)
        #expect(plan.change == .update(from: "0.0.9"))
        _ = try market.publish(mod, kept: keptURL, plan: plan)
        #expect(!FileManager.default.fileExists(atPath: folder.appendingPathComponent("plugins/ci-band/old.tsx").path))
        // The other plugin and the user's own keys on this one stay.
        let plugins = try #require(try market.readManifest()?["plugins"] as? [[String: Any]])
        try #require(plugins.map { $0["name"] as? String } == ["other", "ci-band"])
        #expect(plugins[1]["version"] as? String == "0.2.0" && plugins[1]["category"] as? String == "ui")
        #expect(try git(["status", "--porcelain"], in: folder.path).isEmpty)
        // The user's README keeps its words; only the list between the markers is Sanduhr's.
        let readme = try String(contentsOf: folder.appendingPathComponent("README.md"), encoding: .utf8)
        #expect(readme.hasPrefix("# Mine\n\n<!-- sanduhr:plugins -->") && readme.hasSuffix("<!-- /sanduhr:plugins -->\n\nMy words.\n"))
        #expect(!readme.contains("stale") && readme.contains("- **other**") && readme.contains("- **ci-band** 0.2.0"))
    }

    @Test func aCloneNotReadyIsRefused() throws {
        let h = TempHome()
        defer { h.cleanUp() }
        let gitPath = try #require(ModMarketplace.findGit())
        h.dir("plain")
        #expect(throws: ModMarketplace.Failure.notAClone) { try ModMarketplace(folder: URL(fileURLWithPath: h.at("plain")), gitPath: gitPath).inspect() }
        let folder = try clone(h)
        h.file("my-mods/notes.txt", "mine")
        #expect(throws: ModMarketplace.Failure.dirty(["notes.txt"])) { try ModMarketplace(folder: folder, gitPath: gitPath).inspect() }
        try FileManager.default.removeItem(atPath: h.at("my-mods/notes.txt"))
        try git(["remote", "remove", "origin"], in: folder.path)
        #expect(throws: ModMarketplace.Failure.noRemote) { try ModMarketplace(folder: folder, gitPath: gitPath).inspect() }
    }

    @Test func aSecretIsRefusedAndAWatchWordIsSaid() throws {
        let h = TempHome()
        defer { h.cleanUp() }
        let (store, mod) = try kept(h, body: "// For Acme.\nconst key = \"sk-ant-api03-Zq8xLw2Rt5Vb7Nm1Kp4Hs6Jd9Fg3Cx0Qe\"")
        let folder = try clone(h)
        let market = ModMarketplace(folder: folder, gitPath: try #require(ModMarketplace.findGit()))
        let plan = try market.plan(mod, kept: URL(fileURLWithPath: store.path(mod)), repo: try market.inspect(), watchWords: ["Acme", "Zorblax"])
        #expect(plan.secret == "hooks/register.tsx line 2" && !plan.canPublish)
        #expect(plan.watched == ["Acme"])
    }

    @Test func aFailedPushKeepsTheCommitAndSaysWhy() throws {
        let h = TempHome()
        defer { h.cleanUp() }
        let (store, mod) = try kept(h)
        let folder = try clone(h)
        try git(["remote", "set-url", "origin", h.at("gone.git")], in: folder.path)
        let market = ModMarketplace(folder: folder, gitPath: try #require(ModMarketplace.findGit()))
        let keptURL = URL(fileURLWithPath: store.path(mod))
        let outcome = try market.publish(mod, kept: keptURL, plan: try market.plan(mod, kept: keptURL, repo: try market.inspect()))
        #expect(outcome.pushFailure != nil)
        #expect(try git(["log", "-1", "--format=%s"], in: folder.path) == "Publish ci-band 0.1.0\n")
    }

    @Test func everySeatsWatchWordsApplyToAKeptMod() {
        let h = TempHome()
        defer { h.cleanUp() }
        let store = WatchWords(url: URL(fileURLWithPath: h.at("watch-words.json")))
        store.set(["Acme"], for: h.at(".claude"))
        store.set(["acme", "Rocketsled"], for: h.at(".claude-other"))
        #expect(Set(store.all()) == ["Acme", "Rocketsled"] && store.all().count == 2)
    }
}
