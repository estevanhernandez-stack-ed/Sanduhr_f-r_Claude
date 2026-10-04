import AppKit
import os
import Vision

/// sanduhr://debug/... for the smoke tools in mac/smoke/: window renders, the accessibility tree
/// and the app's state as YAML, and a few actions that drive the app. Off unless DebugGate says
/// so; then every such link is ignored with one log line. Development tooling only.
@MainActor
enum DebugHooks {
    private static let log = Logger(subsystem: "com.626labs.sanduhr", category: "debug")

    static var isOn: Bool {
        DebugGate.isOn(defaultsFlag: UserDefaults.standard.bool(forKey: DebugGate.defaultsKey),
                       environment: ProcessInfo.processInfo.environment)
    }

    static func handle(_ url: URL, app: AppDelegate) {
        guard isOn else {
            log.info("debug link ignored: hooks are off (smoke enable turns them on)")
            return
        }
        let request = DebugLink.parse(url)
        let dir = request.dir.map { URL(fileURLWithPath: $0, isDirectory: true) }
        if let dir {
            do {
                try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
                for stale in ["done", "error"] {
                    try? FileManager.default.removeItem(at: dir.appendingPathComponent(stale))
                }
            } catch {
                log.error("debug: cannot create \(dir.path, privacy: .public): \(error.localizedDescription, privacy: .public)")
                return
            }
        }
        guard let command = request.command else {
            finish(dir, error: request.error ?? "bad debug link")
            return
        }
        log.info("debug: \(url.absoluteString, privacy: .public)")
        switch command {
        case .snapshot:
            guard let dir else { return }
            let run = {
                do {
                    try snapshot(into: dir, app: app)
                    finish(dir)
                } catch {
                    finish(dir, error: error.localizedDescription)
                }
            }
            if primeAccessibility() {
                // SwiftUI builds its element tree a moment after it hears a client is listening.
                DispatchQueue.main.asyncAfter(deadline: .now() + 0.8) { run() }
            } else {
                run()
            }
        case .action(.theme(let id), _) where ThemeRegistry.theme(id: id) == nil:
            finish(dir, error: "unknown theme: \(id) (one of \(ThemeRegistry.themes.map(\.id).joined(separator: ", ")))")
        case .action(let action, _):
            perform(action, app: app) { finish(dir) }
        }
    }

    private static var accessibilityPrimed = false

    /// SwiftUI hosting views expose no children until an assistive client says it is listening,
    /// which VoiceOver does by setting AXEnhancedUserInterface (and Electron-style clients
    /// AXManualAccessibility) on the app. The app sets them on itself once, the first time the
    /// hooks need the tree. Returns true on that first call, so the caller can let SwiftUI
    /// build the tree before walking it.
    private static func primeAccessibility() -> Bool {
        guard !accessibilityPrimed else { return false }
        accessibilityPrimed = true
        let set = NSSelectorFromString("accessibilitySetValue:forAttribute:")
        for attribute in ["AXEnhancedUserInterface", "AXManualAccessibility"] where NSApp.responds(to: set) {
            _ = NSApp.perform(set, with: NSNumber(value: true), with: attribute)
        }
        return true
    }

    /// `done` (empty) or `error` (the message), written last so the caller can wait on it.
    private static func finish(_ dir: URL?, error: String? = nil) {
        if let error { log.error("debug: \(error, privacy: .public)") }
        guard let dir else { return }
        let name = error == nil ? "done" : "error"
        try? Data((error ?? "").utf8).write(to: dir.appendingPathComponent(name), options: .atomic)
    }

    // MARK: Actions

    private static func perform(_ action: DebugAction, app: AppDelegate, then done: @escaping @MainActor () -> Void) {
        // Most actions change SwiftUI state; a short beat lets the windows lay out before the
        // caller snapshots them.
        let settle = { DispatchQueue.main.asyncAfter(deadline: .now() + 0.25) { MainActor.assumeIsolated { done() } } }
        switch action {
        case .showWidget: app.showPanel()
        case .hideWidget: app.hidePanel()
        case .settings(let section): SettingsWindowController.shared.show(section)
        case .closeSettings: SettingsWindowController.shared.close()
        case .refresh:
            Task { @MainActor in
                await app.viewModel.refresh()
                settle()
            }
            return
        case .testAlert: Notifier.shared.sendTest()
        case .pulse(let tier): DeskController.shared.pulse([tier])
        case .tool(let command): app.perform(command)
        case .desk(let on):
            UserDefaults.desk.set(on, forKey: DeskController.enabledKey)
            DeskController.shared.apply()
        case .notch(let on):
            UserDefaults.desk.set(on, forKey: DeskController.notchKey)
        case .cameraLight(let on): CameraLightController.shared.setManual(on)
        case .glow: NotchGlowController.shared.fire()
        case .demo(let on): DeskController.shared.model.setDemo(on)
        case .theme(let id): app.viewModel.selectTheme(id: id)
        case .cycleAccount: app.viewModel.cycleAccount()
        }
        settle()
    }

    // MARK: Snapshot

    private struct Failure: LocalizedError {
        let errorDescription: String?
    }

    private static func snapshot(into dir: URL, app: AppDelegate) throws {
        let windows = sanduhrWindows()
        let names = DebugTree.uniqueNames(windows.map(\.kind))
        let primaryHeight = NSScreen.screens.first?.frame.maxY ?? 0
        var entries: [DebugWindowEntry] = []
        for (window, name) in zip(windows.map(\.window), names) {
            let rendered = render(window)
            if let png = rendered?.png {
                try png.write(to: dir.appendingPathComponent("\(name).png"), options: .atomic)
            }
            var budget = DebugTree.maxNodes
            var tree = (window.accessibilityChildren() ?? []).flatMap {
                walk($0, depth: 0, budget: &budget, primaryHeight: primaryHeight)
            }
            if let rendered, let view = window.contentView {
                let area = DebugTree.topLeft(window.convertToScreen(view.convert(view.bounds, to: nil)),
                                             primaryHeight: primaryHeight)
                tree += recognizeText(rendered.image, in: area)
            }
            entries.append(DebugWindowEntry(
                name: name, windowID: window.windowNumber, title: window.title,
                frame: DebugTree.topLeft(window.frame, primaryHeight: primaryHeight),
                visible: window.isVisible, tree: tree))
        }
        try write(.list(entries.map(\.yaml)), to: dir.appendingPathComponent("tree.yaml"))
        try write(DebugState.yaml(state(app)), to: dir.appendingPathComponent("state.yaml"))
    }

    private static func write(_ node: YAMLNode, to url: URL) throws {
        try Data(YAMLEmitter.emit(node).utf8).write(to: url, options: .atomic)
    }

    /// Sanduhr's windows on screen and what each one is. The status item's own window and
    /// open menus are left out.
    private static func sanduhrWindows() -> [(window: NSWindow, kind: String)] {
        let desk = DeskController.shared
        let settings = SettingsWindowController.shared.window
        return NSApp.windows.compactMap { w in
            guard w.isVisible else { return nil }
            let kind: String
            if w is FloatingPanel { kind = "widget" }
            else if w === desk.window { kind = "desk" }
            else if w === desk.wingsWindow { kind = "notch" }
            else if w === CameraLightController.shared.window { kind = "camera" }
            else if w === NotchGlowController.shared.window { kind = "glow" }
            else if w === settings { kind = "settings" }
            else if w.isSheet || w.sheetParent != nil { kind = "sheet" }
            else {
                let cls = String(describing: type(of: w))
                if cls.contains("StatusBar") || cls.contains("Menu") || cls.contains("Popover") { return nil }
                kind = "window"
            }
            return (w, kind)
        }
    }

    /// The window's content drawn in process at backing scale, as PNG. Needs no Screen
    /// Recording permission; vibrancy and other compositor effects may draw flat.
    private static func render(_ window: NSWindow) -> (png: Data, image: CGImage)? {
        guard let view = window.contentView, view.bounds.width > 0, view.bounds.height > 0,
              let rep = view.bitmapImageRepForCachingDisplay(in: view.bounds) else { return nil }
        view.cacheDisplay(in: view.bounds, to: rep)
        guard let png = rep.representation(using: .png, properties: [:]), let image = rep.cgImage else { return nil }
        return (png, image)
    }

    /// The text actually drawn in a window, read from its render with Vision: one `OCRText` node
    /// per line, label = the text, frame on screen. SwiftUI only exposes its accessibility tree to
    /// a real assistive client, which the smoke tools are not, so this is what they match on.
    /// It also catches text that is clipped or drawn outside the window, which a tree would not.
    private static func recognizeText(_ image: CGImage, in area: CGRect) -> [DebugTreeNode] {
        let request = VNRecognizeTextRequest()
        request.recognitionLevel = .accurate
        request.usesLanguageCorrection = false
        do {
            try VNImageRequestHandler(cgImage: image).perform([request])
        } catch {
            log.error("debug: text recognition failed: \(error.localizedDescription, privacy: .public)")
            return []
        }
        return (request.results ?? []).compactMap { observation in
            guard let text = observation.topCandidates(1).first?.string, !text.isEmpty else { return nil }
            return DebugTreeNode(role: "OCRText", label: text,
                                 frame: DebugTree.screenRect(normalized: observation.boundingBox, in: area))
        }
    }

    /// One element and what is under it. Elements that are not accessibility elements of their
    /// own (plain container views) are skipped and their children take their place.
    private static func walk(_ any: Any, depth: Int, budget: inout Int,
                             primaryHeight: CGFloat) -> [DebugTreeNode] {
        guard budget > 0, depth < DebugTree.maxDepth,
              let element = any as? NSAccessibilityProtocol else { return [] }
        let children = (element.accessibilityChildren() ?? []).flatMap {
            walk($0, depth: depth + 1, budget: &budget, primaryHeight: primaryHeight)
        }
        guard element.isAccessibilityElement() else { return children }
        budget -= 1
        let label = [element.accessibilityLabel(), element.accessibilityTitle()]
            .compactMap { $0 }.first { !$0.isEmpty }
        return [DebugTreeNode(
            role: element.accessibilityRole()?.rawValue,
            subrole: element.accessibilitySubrole()?.rawValue,
            label: label,
            value: DebugTree.value(element.accessibilityValue()),
            enabled: element.isAccessibilityEnabled(),
            frame: DebugTree.topLeft(element.accessibilityFrame(), primaryHeight: primaryHeight),
            children: children)]
    }

    // MARK: State

    private static func state(_ app: AppDelegate) -> DebugStateInput {
        let desk = DeskController.shared
        let vm = app.viewModel
        let settings = SettingsWindowController.shared
        let info = Bundle.main.infoDictionary ?? [:]
        let widgetVisible = app.widgetVisible
        var s = DebugStateInput()
        s.deskEnabled = desk.enabled
        s.deskRunning = desk.running
        s.layout = UserDefaults.desk.string(forKey: "layout")
        s.notch = UserDefaults.desk.bool(forKey: DeskController.notchKey)
        s.hasNotch = desk.wingsWindow != nil
        s.notchLeft = NotchContent.saved(.left, in: .desk)
        s.notchRight = NotchContent.saved(.right, in: .desk)
        s.notchStrip = NotchContent.saved(.strip, in: .desk)
        s.cameraInUse = CameraLightController.shared.cameraInUse
        s.cameraLight = CameraLightController.shared.showing
        s.widgetVisible = widgetVisible
        s.widgetVisibility = .saved()
        s.menuBar = .saved()
        s.settingsOpen = settings.isOpen
        s.settingsSection = settings.window == nil ? nil : settings.section
        // A `defaults write` from the smoke runner posts no change notice here: apply the saved
        // warning settings before reporting, as Desk's minute refresh and the widget's countdown
        // tick would.
        desk.model.refreshMeterWarnings()
        s.meters = desk.model.meters
        vm.refreshMeterWarnings()
        s.widgetWarnings = Tier.allCases.filter(vm.warningTiers.contains)
        s.hiddenLimits = Tier.allCases.filter(vm.hiddenTiers.contains)
        s.meetingsCount = desk.model.meetings.count
        s.alerts = AlertSettings(UserDefaults.standard)
        s.lastFetch = vm.lastUpdated
        switch vm.activeTool {
        case .deepWork: s.activeTool = "deep-work"
        case .snake: s.activeTool = "snake"
        case nil: s.activeTool = nil
        }
        s.pacingPinned = vm.pacingPinned
        s.pulseCount = desk.model.pulseCount
        s.glowCount = NotchGlowController.shared.count
        s.glowShape = NotchGlowController.shared.lastShape
        s.glowSwitches = NotchGlowController.shared.switches
        s.theme = vm.theme.id
        s.menu = app.currentMenu(widgetVisible: widgetVisible)
        s.credentialsStore = KeychainStore.kind
        s.accountRef = AccountRef.of(KeychainStore.accounts.active)
        s.accountsCount = KeychainStore.accounts.labels.count
        s.follow = vm.followEnabled
        s.followPaused = vm.followPaused
        s.version = info["CFBundleShortVersionString"] as? String ?? ""
        s.build = info["CFBundleVersion"] as? String ?? ""
        return s
    }
}
