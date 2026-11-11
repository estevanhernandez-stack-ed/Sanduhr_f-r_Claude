import Foundation

/// "Export..." (spec 21), the pure core: a pack's files in, a zip or a refusal out. The files are validated strict
/// (the same bar an install passes), then written by `WidgetZip.write`, so every zip this makes installs through
/// "Install widget from file..." as the same pack. The save panel and the file write are the caller's. Nothing is sent.
enum WidgetExport {
    enum Outcome: Equatable {
        case zip(data: Data, fileName: String)
        case refused(String)
    }

    static let unreadable = "The pack couldn't be read, so nothing was exported."

    /// The suggested (and reported) file name: `<id>-<version>.zip`.
    static func fileName(id: String, version: String) -> String { "\(id)-\(version).zip" }

    static func export(files: [String: Data], id: String) -> Outcome {
        let verdict = PackValidator.validate(files, folderName: id, strict: true)
        guard verdict.errors.isEmpty, let manifest = verdict.manifest else {
            return .refused(rejected(verdict.errors))
        }
        switch WidgetZip.write(files) {
        case .failure(let e): return .refused("The pack couldn't be zipped (\(e.message)), so nothing was exported.")
        case .success(let data): return .zip(data: data, fileName: fileName(id: id, version: manifest.version))
        }
    }

    /// The installed pack's files, read from its own folder only, then exported. Missing or unreadable: refused.
    static func export(id: String, store: WidgetStore) -> Outcome {
        guard let files = store.readFiles(id) else { return .refused(unreadable) }
        return export(files: files, id: id)
    }

    /// Only a pack made here leaves by this door; a catalog pack's zip is the catalog's.
    static func canExport(_ source: WidgetSource?) -> Bool { source == .local }

    /// The validator's first sentences, then "and N more." (section 20's rule, same helper).
    static func rejected(_ errors: [String]) -> String {
        guard !errors.isEmpty else { return "The pack didn't pass Sanduhr's checks, so nothing was exported." }
        let text = WidgetFileInstall.rejected(errors)
        return text
    }

    static func exportedLine(name: String, fileName: String) -> String { "\(name) is exported to \(fileName)." }
}
