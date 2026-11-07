import Foundation

/// "Install widget from file..." (spec 20), the pure core: a pack zip's bytes in, an outcome out.
/// The gates run in the catalog's order: the byte cap, `WidgetZip.read` (paths, sizes, file count, no
/// links), the `sanduhr_min` floor, `PackValidator` in strict mode, then the store as the user's own
/// (`.local`, listed as made here). Installing the same id again updates it; a catalog pack of that id
/// is never replaced, the store refuses with its own sentence. The file panel, reading the file and the
/// companion choice are the caller's. Nothing is sent anywhere.
enum WidgetFileInstall {
    /// The validator's sentences shown; the rest are counted.
    static let errorsShown = 3

    enum Outcome: Equatable {
        case installed(manifest: PackManifest, wasUpdate: Bool)
        case refused(String)
    }

    static let tooLarge = "The file is larger than a widget pack can be, so nothing was installed."

    static func install(zip: Data, store: WidgetStore, supported: String = PackFormat.supported) -> Outcome {
        guard zip.count <= WidgetZip.maxZipBytes else { return .refused(tooLarge) }

        let files: [String: Data]
        switch WidgetZip.read(zip) {
        case .failure(let e): return .refused(unsafeZip(e.message))
        case .success(let f): files = f
        }

        // The floor comes before validation: a pack from a newer format fails the validator's rules
        // for reasons that only say "newer". The validator's manifest exists only for a pack that
        // passed, so this reads the two keys it needs from pack.json.
        if let need = floor(files), !WidgetCatalog.meetsMinimum(need.min, supported: supported) {
            return .refused("\(need.name) needs a newer Sanduhr (it asks for widget format \(need.min)).")
        }

        let folder = floor(files)?.id
        let verdict = PackValidator.validate(files, folderName: folder, strict: true)
        guard verdict.errors.isEmpty, let manifest = verdict.manifest else {
            return .refused(rejected(verdict.errors))
        }

        let wasUpdate = store.load().find(manifest.id)?.source == .local
        let result = store.install(files, manifest, source: .local, replaceMadeHere: true)
        guard result.ok else { return .refused(result.error ?? "The widget couldn't be installed.") }
        return .installed(manifest: manifest, wasUpdate: wasUpdate)
    }

    /// A `sprite.json` at the pack root: the test the own-art path uses (`WidgetOwnArt.sprite`).
    static func isSprite(_ files: [String: Data]) -> Bool {
        files["sprite.json"] != nil
    }

    /// The status sentence for an installed pack. `isSprite` packs become the companion, so the line
    /// says where they went; any other kind stays as it was.
    static func line(for manifest: PackManifest, wasUpdate: Bool, isSprite: Bool,
                     deskOn: Bool, companionShown: Bool) -> String {
        let name = manifest.name
        guard isSprite else {
            return "\(name) is \(wasUpdate ? "updated" : "installed") from your file. It stays on this Mac."
        }
        if !deskOn { return "\(name) is your companion now. Turn the Desk on in Settings, Desk to see it." }
        if !companionShown {
            return "\(name) is your companion now. It is hidden; tick Companion in Settings, Desk to show it."
        }
        return "\(name) is on your Desk now. It came from your file and stays on this Mac."
    }

    // MARK: Sentences

    private static func unsafeZip(_ why: String) -> String {
        "That file isn't a widget pack Sanduhr can open (\(why)), so nothing was installed."
    }

    /// The first `errorsShown` sentences joined, then "and N more." when there are more. The validator's messages
    /// carry no closing period, so each gets one here (a status line of run-together clauses isn't a sentence).
    static func rejected(_ errors: [String]) -> String {
        guard !errors.isEmpty else { return "The pack didn't pass Sanduhr's checks, so nothing was installed." }
        let shown = errors.prefix(errorsShown).map(sentence).joined(separator: " ")
        let more = errors.count - errorsShown
        return more > 0 ? "\(shown) and \(more) more." : shown
    }

    private static func sentence(_ message: String) -> String {
        guard let last = message.last, !".!?".contains(last) else { return message }
        return message + "."
    }

    /// `id`, `name` and `sanduhr_min` from `pack.json`, or nil when any is missing or unreadable (the
    /// validator then says why).
    private static func floor(_ files: [String: Data]) -> (id: String, name: String, min: String)? {
        guard let bytes = files["pack.json"], let scalars = PackJson.decodeUTF8([UInt8](bytes)),
              case .success(let value) = PackJson.parse(scalars), let o = value.object,
              let id = o["id"]?.string, let min = o["sanduhr_min"]?.string else { return nil }
        return (id, o["name"]?.string ?? id, min)
    }
}
