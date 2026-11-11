import Foundation

/// The file side of "Import an Aseprite sheet" (spec 2.43.0 section 23): the picked JSON (at most 1 MB) and the one
/// PNG it names, read only from the JSON's own folder, as a regular file and never a link. Nothing else is read.
enum AsepriteImport {
    struct Loaded: Equatable {
        var png: Data
        var plan: AsepriteSheet.Plan
    }

    static func load(jsonURL: URL) -> Swift.Result<Loaded, AsepriteError> {
        func fail(_ s: String) -> Swift.Result<Loaded, AsepriteError> { .failure(AsepriteError(message: s)) }
        guard let json = read(jsonURL, limit: AsepriteSheet.maxBytes + 1), json.count <= AsepriteSheet.maxBytes else {
            return fail(AsepriteSheet.notAseprite)
        }
        let parsed: AsepriteSheet.Parsed
        switch AsepriteSheet.parse(json) {
        case .success(let p): parsed = p
        case .failure(let e): return fail(e.message)
        }
        let name = parsed.imageName
        let sheetURL = jsonURL.deletingLastPathComponent().appendingPathComponent(name, isDirectory: false)
        guard let values = try? sheetURL.resourceValues(forKeys: [.isSymbolicLinkKey, .isRegularFileKey]) else {
            return fail(AsepriteSheet.missing(name))
        }
        if values.isSymbolicLink == true {
            return fail("The sheet \(name) is a link. Sanduhr reads only a sheet that is a file beside the JSON.")
        }
        guard values.isRegularFile == true else { return fail(AsepriteSheet.missing(name)) }
        guard let png = read(sheetURL, limit: PackValidator.maxImageBytes + 1) else { return fail(AsepriteSheet.missing(name)) }
        let size = WidgetOwnArt.imageSize(png)
        if let why = size.error { return fail(why) }
        switch AsepriteSheet.plan(parsed, sheetWidth: size.width, sheetHeight: size.height) {
        case .success(let plan): return .success(Loaded(png: png, plan: plan))
        case .failure(let e): return fail(e.message)
        }
    }

    private static func read(_ url: URL, limit: Int) -> Data? {
        guard let handle = try? FileHandle(forReadingFrom: url) else { return nil }
        defer { try? handle.close() }
        return try? handle.read(upToCount: limit) ?? Data()
    }
}
