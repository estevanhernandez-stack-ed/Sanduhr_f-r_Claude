import Foundation

/// The themes of installed theme packs (spec 3), a port of Windows' PackThemes.cs. Kept apart from
/// `UserThemes` on purpose: the widget store is the only thing that knows where packs live.
enum PackThemes {
    /// The themes of `packs`, in pack-id then file order. Each id is `<packId>:<stem>`: the colon
    /// can't be in a file name, so a pack theme never shadows a built-in or a user theme. A file
    /// the theme loader would skip is skipped here too, and a pack without a `themes` folder adds
    /// nothing.
    static func themes(of packs: [InstalledPack]) -> [Theme] {
        var out: [Theme] = []
        let fm = FileManager.default
        for pack in packs {
            let dir = pack.dir.appendingPathComponent("themes", isDirectory: true)
            guard let names = try? fm.contentsOfDirectory(atPath: dir.path) else { continue }
            let files = names.filter { $0.hasSuffix(".json") }
                .sorted { Array($0.utf8).lexicographicallyPrecedes(Array($1.utf8)) }
            for file in files {
                let stem = String(file.dropLast(".json".count))
                guard !stem.isEmpty, let data = fm.contents(atPath: dir.appendingPathComponent(file).path),
                      let theme = UserThemes.decodeTheme(data, id: pack.row.id + ":" + stem)
                else { continue }
                out.append(theme)
            }
        }
        return out
    }

    /// The installed theme packs' themes, for the registry.
    static func installed(in store: WidgetStore) -> [Theme] {
        themes(of: store.installed(ofKind: "theme"))
    }

    /// Registers the installed theme packs' themes. `UserThemes.load()` calls it after the user's
    /// own, so a user theme and a pack theme never share an id.
    static func register(from store: WidgetStore = .shared) {
        for theme in installed(in: store) { ThemeRegistry.register(theme) }
    }
}
