import Foundation

/// A Claude Code mod a folder loads that draws a status entry (item 63b): Claude Code shows
/// those in its status area beside the statusline, not through the statusline command.
struct ModStatusEntry: Identifiable, Equatable, Sendable {
    enum Origin: Equatable, Sendable {
        /// Listed in the folder's `env.CLAUDE_CODE_PLUGIN_DIRS` (an `@inline` mod).
        case pluginDirs
        /// An installed plugin turned on in the folder's `enabledPlugins`.
        case enabledPlugins
    }

    /// The mod's name (its manifest's, else its folder's).
    let name: String
    let path: String
    let origin: Origin
    /// The title of a setting of its own (`userConfig`) that mentions its status, if any.
    let statusSetting: String?
    var id: String { path }
}

/// Finds the mods a Claude Code folder loads that draw status entries, by reading files only:
/// the folder's settings.json, its installed plugins list, each mod's manifest and source. No
/// mod code runs and the `claude` CLI isn't called.
enum ModStatusEntries {
    /// Source files read per mod, and the most bytes read from one.
    static let fileLimit = 400
    static let byteLimit = 512 * 1024
    static let sourceExtensions: Set<String> = ["ts", "tsx", "js", "mjs", "cjs", "jsx"]

    static func scan(folder: String, home: String = NSHomeDirectory()) -> [ModStatusEntry] {
        let root = URL(fileURLWithPath: AccountData.normalized(folder))
        guard let data = FileManager.default.contents(atPath: root.appendingPathComponent("settings.json").path),
              let settings = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any] else { return [] }
        let enabled = settings["enabledPlugins"] as? [String: Any] ?? [:]
        var dirs: [(String, ModStatusEntry.Origin)] = []
        if let env = settings["env"] as? [String: Any], let list = env["CLAUDE_CODE_PLUGIN_DIRS"] as? String {
            for raw in list.components(separatedBy: IntegrationInstaller.pluginDirsSeparator) {
                let p = raw.trimmingCharacters(in: .whitespaces)
                guard !p.isEmpty else { continue }
                dirs.append((p.hasPrefix("~/") ? (home as NSString).appendingPathComponent(String(p.dropFirst(2))) : p, .pluginDirs))
            }
        }
        let installed = installedPaths(root)
        for (key, on) in enabled.sorted(by: { $0.key < $1.key }) where (on as? Bool) == true {
            if let path = installed[key] { dirs.append((path, .enabledPlugins)) }
        }
        var out: [ModStatusEntry] = []
        for (path, origin) in dirs {
            let dir = URL(fileURLWithPath: path)
            guard isMod(dir), drawsStatus(dir) else { continue }
            let manifest = manifest(dir)
            let name = (manifest["name"] as? String).flatMap { $0.isEmpty ? nil : $0 } ?? dir.lastPathComponent
            // An @inline mod turned off in this folder draws nothing here.
            if origin == .pluginDirs, (enabled["\(name)@inline"] as? Bool) == false { continue }
            guard !out.contains(where: { $0.path == dir.path }) else { continue }
            out.append(ModStatusEntry(name: name, path: dir.path, origin: origin, statusSetting: statusSetting(manifest)))
        }
        return out
    }

    /// `plugins/installed_plugins.json`'s install folder per plugin key.
    static func installedPaths(_ root: URL) -> [String: String] {
        guard let data = FileManager.default.contents(atPath: root.appendingPathComponent("plugins/installed_plugins.json").path),
              let o = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any],
              let plugins = o["plugins"] as? [String: Any] else { return [:] }
        var out: [String: String] = [:]
        for (key, value) in plugins {
            if let first = (value as? [Any])?.first as? [String: Any], let path = first["installPath"] as? String {
                out[key] = path
            }
        }
        return out
    }

    /// A plugin folder that is a mod: its hooks.json lists modules.
    static func isMod(_ dir: URL) -> Bool {
        guard let data = FileManager.default.contents(atPath: dir.appendingPathComponent("hooks/hooks.json").path),
              let o = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any] else { return false }
        return o["modules"] != nil
    }

    /// The mod's source calls `ui.status` (its status entry), read as text, never run.
    static func drawsStatus(_ dir: URL) -> Bool {
        guard let e = FileManager.default.enumerator(at: dir, includingPropertiesForKeys: [.isRegularFileKey, .fileSizeKey],
                                                     options: [.skipsHiddenFiles]) else { return false }
        var read = 0
        while let url = e.nextObject() as? URL {
            if url.lastPathComponent == "node_modules" {
                e.skipDescendants()
                continue
            }
            guard sourceExtensions.contains(url.pathExtension.lowercased()),
                  let values = try? url.resourceValues(forKeys: [.isRegularFileKey, .fileSizeKey]),
                  values.isRegularFile == true, (values.fileSize ?? 0) <= byteLimit else { continue }
            read += 1
            if read > fileLimit { return false }
            if let text = try? String(contentsOf: url, encoding: .utf8), text.contains("ui.status") { return true }
        }
        return false
    }

    static func manifest(_ dir: URL) -> [String: Any] {
        guard let data = FileManager.default.contents(atPath: dir.appendingPathComponent(".claude-plugin/plugin.json").path),
              let o = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any] else { return [:] }
        return o
    }

    /// A `userConfig` entry whose key, title or description mentions status: its title (or key).
    static func statusSetting(_ manifest: [String: Any]) -> String? {
        guard let config = manifest["userConfig"] as? [String: Any] else { return nil }
        for key in config.keys.sorted() {
            let entry = config[key] as? [String: Any] ?? [:]
            let words = [key, entry["title"] as? String ?? "", entry["description"] as? String ?? ""].joined(separator: " ")
            if words.lowercased().contains("status") { return (entry["title"] as? String) ?? key }
        }
        return nil
    }
}
