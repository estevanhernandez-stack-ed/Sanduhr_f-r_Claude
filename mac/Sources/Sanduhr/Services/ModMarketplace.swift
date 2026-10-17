import CryptoKit
import Foundation

/// A marketplace of the user's own (item 76, slice F): kept mods published into a git clone the
/// user picks, each as `plugins/<name>/` and an entry in `.claude-plugin/marketplace.json`,
/// committed, and pushed only after the user confirms. Any machine then adds it once with
/// `claude plugin marketplace add owner/repo` and installs each mod as `<name>@<marketplace>`.
///
/// Sanduhr writes only those two paths and the README's plugin list (a README when the repo has
/// none, else only between its markers), and only into a clean clone, so the commit holds nothing
/// the user didn't see in the confirm.
struct ModMarketplace {
    let folder: URL
    let gitPath: String

    /// The chosen clone, remembered across launches.
    static let folderKey = "modMarketplaceFolder"
    static let pluginsDir = "plugins"
    static let readmeName = "README.md"

    enum Failure: Error, Equatable {
        case noGit
        case notAClone
        case noRemote
        case detached
        /// Changes not committed in the clone, by path (the first few).
        case dirty([String])
        /// `.claude-plugin/marketplace.json` is there but isn't a marketplace Sanduhr can add to.
        case badManifest
        case unreadable
        case writeFailed
        case commitFailed(String)

        var message: String {
            switch self {
            case .noGit: "Publishing needs git. Install Apple's Command Line Tools (xcode-select --install) or Homebrew's git."
            case .notAClone: "That folder isn't the top of a git clone. Clone your marketplace repo, then choose the folder clone made."
            case .noRemote: "The clone has no remote named origin, so there is nowhere to push."
            case .detached: "The clone isn't on a branch. Check out the branch to publish to, then try again."
            case .dirty(let paths): "The clone has changes not committed (\(paths.joined(separator: ", "))). Commit or stash them first, so the publish commit holds only the mod."
            case .badManifest: "The clone's .claude-plugin/marketplace.json isn't a marketplace Sanduhr can add to."
            case .unreadable: "Sanduhr couldn't read the kept copy's files. Check it again, or keep it again."
            case .writeFailed: "Sanduhr couldn't write into the clone."
            case .commitFailed(let why): "Sanduhr wrote the files but git couldn't commit them: \(why)"
            }
        }
    }

    /// The clone as `inspect` found it.
    struct Repo: Equatable, Sendable {
        var remote: String
        var branch: String
        /// What `claude plugin marketplace add` takes: `owner/repo` on GitHub, else the remote URL.
        var source: String
        /// The marketplace's name: the one in its manifest, else made from the repo's name.
        var name: String
    }

    /// What publishing one kept mod would do.
    struct Plan: Equatable, Sendable {
        enum Change: Equatable, Sendable {
            case add
            /// Replaces the published copy (its version, when it had one).
            case update(from: String?)
            /// The published copy has the same files: nothing to commit.
            case same
        }

        var repo: Repo
        var mod: String
        var version: String?
        var change: Change
        var files: Int
        var bytes: Int
        /// The kept copy's first line that looks like a secret (`path line n`): publishing is refused.
        var secret: String?
        /// Watch words (slice G) the mod's files hold, from any seat's list: said in the confirm.
        var watched: [String] = []

        var canPublish: Bool { change != .same && secret == nil }
        /// The two commands that add the marketplace and install this mod on another machine.
        var commands: String {
            "claude plugin marketplace add \(repo.source)\nclaude plugin install \(mod)@\(repo.name)"
        }
    }

    /// What `publish` did.
    struct Outcome: Equatable, Sendable {
        var commit: String
        /// nil when the push went through, else why it didn't (the commit stays in the clone).
        var pushFailure: String?
    }

    // MARK: Finding git

    /// Homebrew's git, else Apple's `/usr/bin/git` only when a developer folder holds the real
    /// one: without the Command Line Tools the stub opens an install dialog (as `PythonFinder`).
    static func findGit(fileExists: (String) -> Bool = { FileManager.default.isExecutableFile(atPath: $0) }) -> String? {
        for path in ["/opt/homebrew/bin/git", "/usr/local/bin/git"] where fileExists(path) { return path }
        let devs = [(try? FileManager.default.destinationOfSymbolicLink(atPath: PythonFinder.selectLink))].compactMap { $0 }
            + PythonFinder.developerDirs
        if fileExists("/usr/bin/git"), devs.contains(where: { fileExists($0 + "/usr/bin/git") }) { return "/usr/bin/git" }
        return nil
    }

    // MARK: Reading the clone

    /// `owner/repo` for a GitHub remote in any of its spellings, else the remote as given.
    static func source(fromRemote remote: String) -> String {
        let r = remote.trimmingCharacters(in: .whitespacesAndNewlines)
        let patterns = [#"^(?:https?://)(?:[^@/]+@)?github\.com/([^/]+)/([^/]+?)(?:\.git)?/?$"#,
                        #"^(?:ssh://)?git@github\.com[:/]([^/]+)/([^/]+?)(?:\.git)?/?$"#]
        for p in patterns {
            guard let re = try? NSRegularExpression(pattern: p),
                  let m = re.firstMatch(in: r, range: NSRange(r.startIndex..., in: r)),
                  let owner = Range(m.range(at: 1), in: r), let repo = Range(m.range(at: 2), in: r) else { continue }
            return "\(r[owner])/\(r[repo])"
        }
        return r
    }

    /// A marketplace name from the repo's: lowercase letters, digits and single hyphens.
    static func defaultName(source: String) -> String {
        var base = source.split(separator: "/").last.map(String.init) ?? source
        if base.hasSuffix(".git") { base = String(base.dropLast(4)) }
        let kebab = base.lowercased().map { $0.isLetter && $0.isASCII || $0.isNumber ? String($0) : "-" }.joined()
            .split(separator: "-").joined(separator: "-")
        return kebab.isEmpty ? "my-mods" : kebab
    }

    /// The clone's remote, branch and marketplace name, refusing a clone that isn't ready.
    func inspect() throws -> Repo {
        guard let top = git(["rev-parse", "--show-toplevel"]), top.ok,
              AccountData.normalized(top.out.trimmingCharacters(in: .whitespacesAndNewlines)) == AccountData.normalized(folder.path)
        else { throw Failure.notAClone }
        guard let remote = git(["remote", "get-url", "origin"]), remote.ok else { throw Failure.noRemote }
        guard let branch = git(["symbolic-ref", "--short", "-q", "HEAD"]), branch.ok else { throw Failure.detached }
        guard let status = git(["status", "--porcelain", "--untracked-files=all"]), status.ok else { throw Failure.notAClone }
        let dirty = status.out.split(separator: "\n").map { String($0.dropFirst(3)) }
        if !dirty.isEmpty { throw Failure.dirty(Array(dirty.prefix(3)) + (dirty.count > 3 ? ["…"] : [])) }
        let source = Self.source(fromRemote: remote.out)
        let manifest = try readManifest()
        let name = (manifest?["name"] as? String) ?? Self.defaultName(source: source)
        return Repo(remote: remote.out.trimmingCharacters(in: .whitespacesAndNewlines),
                    branch: branch.out.trimmingCharacters(in: .whitespacesAndNewlines), source: source, name: name)
    }

    private var manifestURL: URL { folder.appendingPathComponent(ModPack.marketplacePath) }

    /// The clone's marketplace manifest, nil when it has none yet.
    func readManifest() throws -> [String: Any]? {
        guard let data = FileManager.default.contents(atPath: manifestURL.path) else { return nil }
        guard let o = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              (o["plugins"] == nil || o["plugins"] is [[String: Any]]) else { throw Failure.badManifest }
        return o
    }

    // MARK: Planning

    /// What publishing the kept copy at `kept` would change, its scan and its watch words.
    func plan(_ mod: KeptMod, kept: URL, repo: Repo, watchWords: [String] = []) throws -> Plan {
        guard let rels = try? KeptMods.files(in: kept) else { throw Failure.unreadable }
        var plan = Plan(repo: repo, mod: mod.name, version: mod.version, change: .add, files: rels.count, bytes: 0)
        var text = mod.name + "\n" + mod.description
        for rel in rels {
            guard let data = FileManager.default.contents(atPath: kept.appendingPathComponent(rel).path) else {
                throw Failure.unreadable
            }
            plan.bytes += data.count
            let s = String(decoding: data, as: UTF8.self)
            text += "\n" + s
            if plan.secret == nil, let line = ConfigPack.secretLine(in: s) { plan.secret = "\(rel) line \(line)" }
        }
        var item = ConfigPack.Item(kind: .plugin, name: mod.name)
        item.files = [.init(path: "", text: text)]
        plan.watched = WatchWords.matches(item, words: watchWords)
        let published = folder.appendingPathComponent(Self.pluginsDir).appendingPathComponent(mod.name)
        if FileManager.default.fileExists(atPath: published.path) {
            let old = (try readManifest()?["plugins"] as? [[String: Any]])?.first { $0["name"] as? String == mod.name }
            plan.change = Self.digest(published) == Self.digest(kept, files: rels) ? .same : .update(from: old?["version"] as? String)
        }
        return plan
    }

    /// A hash line per file with its relative path: `files` of `folder`, else every plain file in it.
    static func digest(_ folder: URL, files: [String]? = nil) -> String? {
        var rels = files ?? []
        if files == nil {
            guard let walk = FileManager.default.enumerator(atPath: folder.path) else { return nil }
            while let rel = walk.nextObject() as? String {
                if walk.fileAttributes?[.type] as? FileAttributeType == .typeRegular,
                   (rel as NSString).lastPathComponent != ".DS_Store" { rels.append(rel) }
            }
        }
        var parts: [String] = []
        for rel in rels {
            guard let data = FileManager.default.contents(atPath: folder.appendingPathComponent(rel).path) else { return nil }
            parts.append(rel + ":" + SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined())
        }
        return parts.sorted().joined(separator: "\n")
    }

    // MARK: Publishing

    /// The manifest with `mod`'s entry added or replaced, other plugins and keys as they were.
    /// `author` is the mod's own `plugin.json` author, when it names one.
    static func manifest(_ existing: [String: Any]?, mod: KeptMod, repo: Repo, author: Any? = nil) -> [String: Any] {
        // A GitHub `owner/repo`; a path or a URL names no owner.
        let owner = repo.source.range(of: #"^[A-Za-z0-9-]+/[A-Za-z0-9._-]+$"#, options: .regularExpression) != nil
            ? String(repo.source.split(separator: "/")[0]) : nil
        var doc = existing ?? [
            "name": repo.name,
            "description": "Mods for Claude Code, published from Sanduhr.",
            "owner": ["name": owner ?? "Published with Sanduhr"],
        ]
        var entry: [String: Any] = ["name": mod.name, "source": "./\(pluginsDir)/\(mod.name)", "description": mod.description]
        if let v = mod.version { entry["version"] = v }
        // Who made it: the mod's own author, else the repo's owner. A name only, never an email.
        if let name = authorName(author) ?? owner { entry["author"] = ["name": name] }
        var plugins = doc["plugins"] as? [[String: Any]] ?? []
        if let i = plugins.firstIndex(where: { $0["name"] as? String == mod.name }) {
            // Keep keys the user added to the entry (category, tags, an author of their own) under
            // the new source and version.
            if plugins[i]["author"] != nil, authorName(author) == nil { entry["author"] = nil }
            plugins[i] = plugins[i].merging(entry) { $1 }
            if mod.version == nil { plugins[i]["version"] = nil }
        } else {
            plugins.append(entry)
        }
        doc["plugins"] = plugins
        return doc
    }

    /// The name in a `plugin.json` author, given as an object or a plain string.
    static func authorName(_ author: Any?) -> String? {
        let name = (author as? [String: Any])?["name"] as? String ?? author as? String
        return name.map { $0.trimmingCharacters(in: .whitespaces) }.flatMap { $0.isEmpty ? nil : $0 }
    }

    static let listStart = "<!-- sanduhr:plugins -->"
    static let listEnd = "<!-- /sanduhr:plugins -->"

    /// The plugin list between the markers: each one's install line, which adds the marketplace
    /// first when it isn't yet (Claude Code 2.1.275 or later).
    static func pluginList(_ manifest: [String: Any], repo: Repo) -> String {
        let plugins = (manifest["plugins"] as? [[String: Any]] ?? []).compactMap { p -> String? in
            guard let name = p["name"] as? String else { return nil }
            let version = (p["version"] as? String).map { " " + $0 } ?? ""
            let about = (p["description"] as? String).flatMap { $0.isEmpty ? nil : ": " + $0 } ?? ""
            return "- **\(name)**\(version)\(about)\n\n  ```\n  claude plugin install \(name) --marketplace \(repo.source)\n  ```"
        }
        return ([listStart] + plugins + [listEnd]).joined(separator: "\n\n")
    }

    /// The README with the plugin list: a new one when the repo has none, the list replaced when
    /// the user's README has the markers, nil (left alone) when it has none.
    static func readme(_ existing: String?, manifest: [String: Any], repo: Repo) -> String? {
        let list = pluginList(manifest, repo: repo)
        guard let existing else {
            return """
            # \(repo.name)

            Claude Code plugins, published from Sanduhr.

            Add the marketplace once, then install any plugin in it by name:

            ```
            claude plugin marketplace add \(repo.source)
            claude plugin install <plugin>@\(repo.name)
            ```

            ## Plugins

            \(list)

            """
        }
        guard let a = existing.range(of: listStart), let b = existing.range(of: listEnd, range: a.upperBound..<existing.endIndex)
        else { return nil }
        return existing.replacingCharacters(in: a.lowerBound..<b.upperBound, with: list)
    }

    /// Writes the kept copy into the clone, commits it, then pushes to `origin`. Run only after
    /// the user confirmed `plan`. A failed push leaves the commit, and says why.
    func publish(_ mod: KeptMod, kept: URL, plan: Plan) throws -> Outcome {
        let fm = FileManager.default
        guard let rels = try? KeptMods.files(in: kept) else { throw Failure.unreadable }
        let target = folder.appendingPathComponent(Self.pluginsDir).appendingPathComponent(mod.name)
        var paths = [ModPack.marketplacePath, "\(Self.pluginsDir)/\(mod.name)"]
        do {
            let pluginJSON = fm.contents(atPath: kept.appendingPathComponent(".claude-plugin/plugin.json").path)
                .flatMap { try? JSONSerialization.jsonObject(with: $0) as? [String: Any] }
            let manifest = Self.manifest(try readManifest(), mod: mod, repo: plan.repo, author: pluginJSON?["author"])
            if fm.fileExists(atPath: target.path) { try fm.removeItem(at: target) }
            for rel in rels {
                let to = target.appendingPathComponent(rel)
                try fm.createDirectory(at: to.deletingLastPathComponent(), withIntermediateDirectories: true)
                try fm.copyItem(at: kept.appendingPathComponent(rel), to: to)
                // Kept copies are read-only; the clone's files are the user's to edit.
                try fm.setAttributes([.posixPermissions: 0o644], ofItemAtPath: to.path)
            }
            try fm.createDirectory(at: manifestURL.deletingLastPathComponent(), withIntermediateDirectories: true)
            var data = try JSONSerialization.data(withJSONObject: manifest, options: [.prettyPrinted, .sortedKeys, .withoutEscapingSlashes])
            data.append(0x0A)
            try data.write(to: manifestURL)
            let readme = folder.appendingPathComponent(Self.readmeName)
            let current = fm.contents(atPath: readme.path).map { String(decoding: $0, as: UTF8.self) }
            if let text = Self.readme(current, manifest: manifest, repo: plan.repo), text != current {
                try Data(text.utf8).write(to: readme)
                paths.append(Self.readmeName)
            }
        } catch let failure as Failure {
            throw failure
        } catch {
            throw Failure.writeFailed
        }
        let add = git(["add", "-A", "--"] + paths)
        guard add?.ok == true else { throw Failure.commitFailed(Self.reason(add)) }
        let message = "Publish \(mod.name)" + (mod.version.map { " " + $0 } ?? "")
        let commit = git(["commit", "-q", "-m", message, "--"] + paths)
        guard commit?.ok == true else { throw Failure.commitFailed(Self.reason(commit)) }
        let sha = git(["rev-parse", "--short", "HEAD"])?.out.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        let push = git(["push", "-q", "origin", "HEAD:refs/heads/" + plan.repo.branch], timeout: 120)
        return Outcome(commit: sha, pushFailure: push?.ok == true ? nil : Self.reason(push))
    }

    // MARK: Running git

    struct Run: Equatable, Sendable {
        var status: Int32
        var out: String
        var err: String
        var ok: Bool { status == 0 }
    }

    static func reason(_ r: Run?) -> String {
        guard let r else { return "git didn't finish in time." }
        let line = (r.err.isEmpty ? r.out : r.err).split(separator: "\n")
            .map { $0.trimmingCharacters(in: .whitespaces) }.first { !$0.isEmpty && !$0.hasPrefix("hint:") } ?? ""
        return line.isEmpty ? "git stopped with exit \(r.status)." : String(line.prefix(300))
    }

    /// Runs git in the clone: never a prompt (no terminal, no askpass), stopped after `timeout`.
    func git(_ args: [String], timeout: TimeInterval = 20) -> Run? {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: gitPath)
        p.arguments = ["-C", folder.path] + args
        var env = ProcessInfo.processInfo.environment
        env["GIT_TERMINAL_PROMPT"] = "0"
        env["GIT_ASKPASS"] = "/usr/bin/false"
        env["SSH_ASKPASS"] = "/usr/bin/false"
        env["GIT_SSH_COMMAND"] = "ssh -o BatchMode=yes"
        env["LC_ALL"] = "C"
        env["PATH"] = ["/opt/homebrew/bin", "/usr/local/bin", "/usr/bin", "/bin", env["PATH"] ?? ""].joined(separator: ":")
        p.environment = env
        let out = Pipe(), err = Pipe()
        p.standardInput = FileHandle.nullDevice
        p.standardOutput = out
        p.standardError = err
        final class Box: @unchecked Sendable { var out = Data(); var err = Data() }
        let box = Box()
        let done = DispatchGroup()
        do { try p.run() } catch { return Run(status: -1, out: "", err: "Couldn't start git.") }
        done.enter()
        DispatchQueue.global(qos: .userInitiated).async { box.out = out.fileHandleForReading.readDataToEndOfFile(); done.leave() }
        done.enter()
        DispatchQueue.global(qos: .userInitiated).async { box.err = err.fileHandleForReading.readDataToEndOfFile(); done.leave() }
        if done.wait(timeout: .now() + timeout) == .timedOut {
            p.terminate()
            p.waitUntilExit()
            return nil
        }
        p.waitUntilExit()
        return Run(status: p.terminationStatus, out: String(decoding: box.out, as: UTF8.self),
                   err: String(decoding: box.err, as: UTF8.self))
    }
}
