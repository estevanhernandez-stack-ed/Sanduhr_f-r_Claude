import AppKit
import ApplicationServices

/// Reads other apps' menu shortcuts through Accessibility (item 73, slice b) for
/// AppMenuShortcuts. Off by default: it reads only while "Also check other apps' menus" is on
/// (desk key `hotKeyCheckAppMenus`) and macOS already trusts Sanduhr. It never asks for the
/// permission by itself; only the "Allow in System Settings…" button does (requestAccess).
///
/// What it reads: the menu bars of the frontmost app and the other regular apps running, each
/// item's title and key equivalent, nothing else. No keystroke is read. The walk runs off the
/// main thread with a short timeout per app and caps on depth and items, and its result is
/// kept for a minute.
final class AppMenuReader: @unchecked Sendable {
    static let shared = AppMenuReader()

    /// The switch on General, Shortcuts, in the Desk suite.
    static let switchKey = "hotKeyCheckAppMenus"
    /// How long a read stays fresh.
    static let cacheLife: TimeInterval = 60
    /// How long one app may take to answer one question.
    static let messagingTimeout: Float = 0.25
    /// How deep into submenus the walk goes (menu bar item, menu, item, submenu, item…).
    static let maxDepth = 6
    /// The most items read from one app.
    static let maxItemsPerApp = 2000

    private let queue = DispatchQueue(label: "com.626labs.sanduhr.app-menus", qos: .utility)
    private let lock = NSLock()
    private var cached: AppMenuShortcuts = .none
    private var readAt: Date?
    private var reading = false

    /// The switch is on.
    static func isOn(in d: DefaultsStore = UserDefaults.desk) -> Bool {
        (d.object(forKey: switchKey) as? NSNumber)?.boolValue ?? false
    }

    /// macOS trusts Sanduhr with Accessibility. Asking this never shows a prompt.
    static var isTrusted: Bool { AXIsProcessTrusted() }

    /// Both: the menus may be read.
    static var isActive: Bool { isOn() && isTrusted }

    /// The menus last read while active, `.none` when the switch is off or Sanduhr isn't trusted.
    var current: AppMenuShortcuts {
        guard Self.isActive else { return .none }
        lock.lock(); defer { lock.unlock() }
        return cached
    }

    /// Reads the menus again when the last read is older than cacheLife (or `force`), off the main
    /// thread, then calls `done` on the main thread with the result. Does nothing while inactive,
    /// and forgets what it read when it is.
    @MainActor
    func refresh(force: Bool = false, done: (@MainActor (AppMenuShortcuts) -> Void)? = nil) {
        guard Self.isActive else {
            lock.lock(); cached = .none; readAt = nil; lock.unlock()
            return
        }
        lock.lock()
        let fresh = readAt.map { Date().timeIntervalSince($0) < Self.cacheLife } ?? false
        if reading || (fresh && !force) { lock.unlock(); return }
        reading = true
        lock.unlock()
        let apps = Self.appsToRead()
        queue.async { [self] in
            let result = AppMenuShortcuts.build(apps.flatMap { Self.items(pid: $0.pid, app: $0.name) })
            lock.lock()
            cached = result
            readAt = Date()
            reading = false
            lock.unlock()
            DispatchQueue.main.async { MainActor.assumeIsolated { done?(result) } }
        }
    }

    /// The only place Sanduhr asks for Accessibility: the button on General, Shortcuts. macOS
    /// shows its prompt (or opens System Settings) and the answer arrives later.
    static func requestAccess() {
        let options = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary
        _ = AXIsProcessTrustedWithOptions(options)
    }

    // MARK: Reading

    /// The frontmost app first, then the other regular apps running; never Sanduhr itself.
    @MainActor
    private static func appsToRead() -> [(pid: pid_t, name: String)] {
        let me = ProcessInfo.processInfo.processIdentifier
        let workspace = NSWorkspace.shared
        var apps = workspace.runningApplications.filter { $0.activationPolicy == .regular && $0.processIdentifier != me }
        if let front = workspace.frontmostApplication, let i = apps.firstIndex(of: front) {
            apps.insert(apps.remove(at: i), at: 0)
        }
        return apps.map { ($0.processIdentifier, $0.localizedName ?? $0.bundleIdentifier ?? "Another app") }
    }

    /// One app's menu items with a key equivalent, as Accessibility reports them.
    private static func items(pid: pid_t, app: String) -> [AppMenuShortcuts.Item] {
        let element = AXUIElementCreateApplication(pid)
        AXUIElementSetMessagingTimeout(element, messagingTimeout)
        guard let bar: AXUIElement = attribute(element, kAXMenuBarAttribute) else { return [] }
        AXUIElementSetMessagingTimeout(bar, messagingTimeout)
        var out: [AppMenuShortcuts.Item] = []
        var visited = 0
        walk(bar, depth: 0, app: app, out: &out, visited: &visited)
        return out
    }

    private static func walk(_ element: AXUIElement, depth: Int, app: String,
                             out: inout [AppMenuShortcuts.Item], visited: inout Int) {
        guard depth <= maxDepth, visited < maxItemsPerApp,
              let children: [AXUIElement] = attribute(element, kAXChildrenAttribute) else { return }
        for child in children {
            guard visited < maxItemsPerApp else { return }
            visited += 1
            // The timeout belongs to each element, not to the app's.
            AXUIElementSetMessagingTimeout(child, messagingTimeout)
            if let item = item(child, app: app) { out.append(item) }
            walk(child, depth: depth + 1, app: app, out: &out, visited: &visited)
        }
    }

    /// A menu item with a key equivalent, nil for anything else (menus, separators, items
    /// without keys).
    private static func item(_ element: AXUIElement, app: String) -> AppMenuShortcuts.Item? {
        guard let title: String = attribute(element, kAXTitleAttribute), !title.isEmpty else { return nil }
        let char: String? = attribute(element, kAXMenuItemCmdCharAttribute)
        let virtualKey = (attribute(element, kAXMenuItemCmdVirtualKeyAttribute) as NSNumber?)?.intValue
        let glyph = (attribute(element, kAXMenuItemCmdGlyphAttribute) as NSNumber?)?.intValue
        guard (char?.isEmpty == false) || virtualKey != nil || (glyph ?? 0) != 0 else { return nil }
        let modifiers = (attribute(element, kAXMenuItemCmdModifiersAttribute) as NSNumber?)?.intValue ?? 0
        return AppMenuShortcuts.Item(app: app, title: title, char: char, virtualKey: virtualKey,
                                     glyph: glyph == 0 ? nil : glyph, modifiers: modifiers)
    }

    private static func attribute<T>(_ element: AXUIElement, _ name: String) -> T? {
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, name as CFString, &value) == .success else { return nil }
        return value as? T
    }
}
