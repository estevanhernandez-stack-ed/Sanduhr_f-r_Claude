import Foundation

/// The Desk companion's choices (spec 4), a port of Windows' CompanionSettings.cs: which installed
/// sprite pack is the companion (one at a time). Whether it shows at all is the companion piece
/// being placed in the Desk layout, like every other piece; it always roams, unless Reduce Motion
/// is on. Absent reads as no pack chosen (the first installed sprite is used).
struct CompanionSettings: Equatable {
    /// The Desk defaults suite keys (`UserDefaults.desk`).
    static let packKey = "deskCompanionPack"

    var pack: String?

    static func load(_ defaults: UserDefaults = .desk) -> CompanionSettings {
        let pack = defaults.string(forKey: packKey)
        return CompanionSettings(pack: WidgetStore.isSafeId(pack) ? pack : nil)
    }

    /// Writes nothing after Reset Sanduhr… ran.
    func save(_ defaults: UserDefaults = .desk) {
        guard !ResetGate.isOn else { return }
        if let pack { defaults.set(pack, forKey: Self.packKey) } else { defaults.removeObject(forKey: Self.packKey) }
    }

    /// The pack the companion draws: the chosen one while it is installed as a sprite, otherwise
    /// the first installed sprite pack by id (so installing the hourglass and placing the piece is
    /// enough). Nil when no sprite pack is installed. `kindOf` reads an installed pack's kind from
    /// its own pack.json.
    func activePack(_ registry: WidgetRegistry, kindOf: (String) -> String?) -> String? {
        let sprites = registry.packs.filter { kindOf($0.id) == "sprite" }.map(\.id)
            .sorted { Array($0.utf8).lexicographicallyPrecedes(Array($1.utf8)) }
        if let pack, sprites.contains(pack) { return pack }
        return sprites.first
    }
}
