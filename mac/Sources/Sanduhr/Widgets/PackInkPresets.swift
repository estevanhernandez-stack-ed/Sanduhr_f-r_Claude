import Foundation

/// The gradient palettes of installed palette packs (spec 3), a port of Windows' PackInkPresets.cs.
/// `MessagePalette.all` is the built-ins plus these, so the message editor's Palette menu gains
/// them.
enum PackInkPresets {
    /// A pack palette's name in the menu: `<palette> · <pack name>`, so two packs' "sunset" stay
    /// apart and none shadows a built-in.
    static func label(_ palette: String, packName: String) -> String { "\(palette) · \(packName)" }

    /// Each enabled palette pack's palettes (by pack id, then file order) as a name and the stops
    /// as the ink stores them: hex without `#`, lowercase. A pack whose `palettes.json` can't be
    /// read, and a palette without a usable color, are skipped.
    static func palettes(in store: WidgetStore) -> [(name: String, colors: [String])] {
        var out: [(name: String, colors: [String])] = []
        for pack in store.installed(ofKind: "palette") {
            guard let bytes = try? Data(contentsOf: pack.dir.appendingPathComponent("palettes.json")),
                  let scalars = PackJson.decodeUTF8([UInt8](bytes)),
                  case .success(let value) = PackJson.parse(scalars),
                  let list = value.object?["palettes"]?.array
            else { continue }
            for item in list {
                guard let o = item.object, let name = o["name"]?.string, let colors = o["colors"]?.array else { continue }
                let hexes = colors.compactMap { $0.string }.compactMap(normalize)
                let label = label(name, packName: pack.info.name)
                guard !hexes.isEmpty, !(MessagePalette.builtIn + out).contains(where: { $0.name == label }) else { continue }
                out.append((label, hexes))
            }
        }
        return out
    }

    /// Hands the installed palettes to `MessagePalette`.
    static func register(from store: WidgetStore = .shared) {
        MessagePalette.setPacks(palettes(in: store))
    }

    /// "#FF2A6D" or "ff2a6d" as "ff2a6d"; nil for anything else.
    static func normalize(_ hex: String) -> String? {
        let s = hex.hasPrefix("#") ? String(hex.dropFirst()) : hex
        guard s.utf8.count == 6, s.utf8.allSatisfy({ ($0 >= 0x30 && $0 <= 0x39) || ($0 >= 0x41 && $0 <= 0x46) || ($0 >= 0x61 && $0 <= 0x66) })
        else { return nil }
        return s.lowercased()
    }
}
