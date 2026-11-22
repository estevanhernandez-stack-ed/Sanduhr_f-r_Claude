import Foundation

extension Notification.Name {
    /// Posted after Claude Code's hook was installed, updated or removed in a folder: the agent watcher
    /// asks again whether the updated hook is there (Claude Code's notifications follow it).
    static let sanduhrHooksDidChange = Notification.Name("sanduhrHooksDidChange")
}

/// How the app plugs agent notifications in (design sections 1 and 2; decisions D5 and D6). Everything here
/// is pure or takes its inputs as arguments, so the wiring's rules are tested without an app, a window, an
/// FSEvents stream or a timer; `AppDelegate` supplies the real inputs and owns the watcher and notifier.
enum AgentNotificationsWiring {
    /// Which agents the watcher follows.
    typealias Wants = (claude: Bool, codex: Bool, antigravity: Bool)

    /// Codex and Antigravity are followed only while their sources are on, and nothing in demo mode (demo's
    /// samples are no agent activity). Claude Code follows the updated hook (`hookInstalled`) alone, not the
    /// Claude usage source: that switch is about claude.ai limits, and a Mac with usage off still runs Claude
    /// Code. A 2.46-shaped Mac (hook not updated, Codex and Antigravity off) follows nothing: no stream, no
    /// timer, no file read.
    static func wants(setup: SetupSettings?, sources: AgentSourceSettings, demo: Bool, hookInstalled: Bool) -> Wants {
        guard !demo else { return (false, false, false) }
        return (claude: hookInstalled,
                codex: sources.isOn(.codex, setup: setup),
                antigravity: sources.isOn(.antigravity, setup: setup))
    }

    /// True when the 2.47 hook is current in at least one folder (`.installed`: every event's entry as this version
    /// writes it). An outdated hook is the legacy glow's; it never turns Claude Code's notifications on.
    static func hookInstalled(folders: [String], status: (String) -> IntegrationStatus) -> Bool {
        folders.contains { status($0) == .installed }
    }

    // MARK: The Darwin post

    /// Which path a Claude Code post takes, after the signal files were read (design section 1).
    enum Route: Equatable {
        /// The new hook's post: the watcher already raised its events. The legacy glow must not fire too.
        case notifications
        /// An older hook's bare post: the notch glow under today's switches, nothing else.
        case legacyGlow
        /// A bare prompt: only the 2.47 hook posts it, so a bare one is nothing.
        case nothing
    }

    static func route(_ event: ClaudeCodeEvent, posted: ClaudeCodePostResult) -> Route {
        switch posted {
        case .hadFiles: .notifications
        case .bare: event == .prompt ? .nothing : .legacyGlow
        }
    }

    /// One post: reads the signal files (once, whatever the event), then fires the legacy glow only for a bare
    /// waiting or done post.
    static func handlePost(_ event: ClaudeCodeEvent, posted: () -> ClaudeCodePostResult,
                           legacyGlow: (ClaudeCodeEvent) -> Void) {
        if route(event, posted: posted()) == .legacyGlow { legacyGlow(event) }
    }
}
