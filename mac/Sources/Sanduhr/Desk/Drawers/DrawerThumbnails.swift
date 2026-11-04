import AppKit
import QuickLookThumbnailing

/// Thumbnails for the drawer grids: `QLThumbnailGenerator` at icon size, cached by path and
/// modification date, with the file's Finder icon standing in until one arrives. A request runs
/// once per path and date, answers on the main queue and never repeats on a timer.
@MainActor
final class DrawerThumbnails {
    static let shared = DrawerThumbnails()
    static let side: CGFloat = 56

    private let cache = NSCache<NSString, NSImage>()
    private var inFlight = Set<String>()

    /// The cache key: a changed file (a new modification date) is a new thumbnail.
    nonisolated static func key(path: String, modified: Date?) -> String {
        "\(path)|\(modified.map { String($0.timeIntervalSince1970) } ?? "-")"
    }

    private func key(_ path: String) -> String { Self.key(path: path, modified: DrawerFS.modified(path)) }

    func cached(_ path: String) -> NSImage? { cache.object(forKey: key(path) as NSString) }

    func placeholder(_ path: String) -> NSImage { NSWorkspace.shared.icon(forFile: path) }

    /// Asks for the thumbnail unless one is cached or already on its way; `done` gets it on the main queue.
    func request(_ path: String, scale: CGFloat, done: @escaping @MainActor (NSImage) -> Void) {
        let k = key(path)
        if let hit = cache.object(forKey: k as NSString) { done(hit); return }
        guard inFlight.insert(k).inserted else { return }
        let req = QLThumbnailGenerator.Request(fileAt: URL(fileURLWithPath: path),
                                               size: CGSize(width: Self.side, height: Self.side),
                                               scale: scale, representationTypes: .thumbnail)
        QLThumbnailGenerator.shared.generateBestRepresentation(for: req) { rep, _ in
            DispatchQueue.main.async {
                MainActor.assumeIsolated {
                    self.inFlight.remove(k)
                    guard let image = rep?.nsImage else { return }
                    self.cache.setObject(image, forKey: k as NSString)
                    done(image)
                }
            }
        }
    }
}
