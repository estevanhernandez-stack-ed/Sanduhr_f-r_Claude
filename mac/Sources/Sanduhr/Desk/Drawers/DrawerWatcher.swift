import CoreServices
import Foundation

/// One FSEvents stream over the drawer folders (docs/mac-drawers-setup-spec.md, section 7). It is
/// given the folders to watch and nothing else, so the Desktop is never in it. The kernel pushes
/// changes to `handler` on `queue`; there is no timer and no polling, so an idle drawer costs
/// nothing. FSEvents' own latency coalesces a burst into one call.
final class DrawerWatcher {
    private var stream: FSEventStreamRef?
    private let paths: [String]
    private let queue: DispatchQueue
    private let latency: CFTimeInterval
    private let handler: ([String]) -> Void

    init(paths: [String], queue: DispatchQueue = DispatchQueue(label: "sanduhr.drawers.fsevents"),
         latency: CFTimeInterval = 0.2, handler: @escaping ([String]) -> Void) {
        self.paths = paths
        self.queue = queue
        self.latency = latency
        self.handler = handler
    }

    deinit { stop() }

    var isRunning: Bool { stream != nil }

    /// Starts watching. False when there is nothing to watch or the stream could not start.
    @discardableResult
    func start() -> Bool {
        guard stream == nil, !paths.isEmpty else { return false }
        var context = FSEventStreamContext(version: 0, info: Unmanaged.passUnretained(self).toOpaque(),
                                           retain: nil, release: nil, copyDescription: nil)
        let callback: FSEventStreamCallback = { _, info, count, eventPaths, _, _ in
            guard let info else { return }
            let me = Unmanaged<DrawerWatcher>.fromOpaque(info).takeUnretainedValue()
            let list = unsafeBitCast(eventPaths, to: NSArray.self) as? [String] ?? []
            me.handler(Array(list.prefix(count)))
        }
        let flags = FSEventStreamCreateFlags(kFSEventStreamCreateFlagUseCFTypes | kFSEventStreamCreateFlagFileEvents
                                             | kFSEventStreamCreateFlagNoDefer)
        guard let s = FSEventStreamCreate(nil, callback, &context, paths as CFArray,
                                          FSEventStreamEventId(kFSEventStreamEventIdSinceNow), latency, flags) else { return false }
        FSEventStreamSetDispatchQueue(s, queue)
        guard FSEventStreamStart(s) else {
            FSEventStreamInvalidate(s)
            FSEventStreamRelease(s)
            return false
        }
        stream = s
        return true
    }

    func stop() {
        guard let s = stream else { return }
        FSEventStreamStop(s)
        FSEventStreamInvalidate(s)
        FSEventStreamRelease(s)
        stream = nil
    }
}
