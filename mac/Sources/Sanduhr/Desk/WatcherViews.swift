import AppKit
import SwiftUI

/// How watchers draw (item 66): a state dot, the title, the elapsed time, the progress when there
/// is a total, a one-line note. "Waiting on you" pulses (not with Reduce Motion); lost touch and
/// finished draw greyed.
///
/// The camera indicator's red dot (item 67) is the only red dot in Sanduhr, so a red dot on the
/// notch always means the camera: a failed watcher draws a red exclamation mark in a triangle
/// instead, and no other state is red.
enum WatcherLook {
    /// The shape a state draws.
    enum Mark: Equatable {
        case dot
        /// A dot with a check (passed).
        case check
        /// `exclamationmark.triangle.fill` (failed).
        case triangle
    }

    static func mark(_ state: WatcherState) -> Mark {
        switch state {
        case .failed: .triangle
        case .passed: .check
        case .running, .waiting, .finished, .lostTouch: .dot
        }
    }

    /// The color as hex, for the tests: only the failed triangle is red.
    static func hex(_ state: WatcherState) -> String {
        switch state {
        case .running: "60a5fa"
        case .waiting: "fbbf24"
        case .passed: "4ade80"
        case .failed: "f87171"
        case .finished, .lostTouch: "9ca3af"
        }
    }

    /// The mark's color for a state.
    static func dot(_ state: WatcherState) -> Color { Color.hex(hex(state)) }

    /// Lost touch and finished draw dimmer than the rest.
    static func opacity(_ state: WatcherState) -> Double {
        state == .lostTouch || state == .finished ? 0.55 : 1
    }

    /// The dot's room in a notch line: its size and the gap after it.
    static func dotRoom(_ size: CGFloat) -> CGFloat { size * 0.5 + size * 0.35 }
}

/// The state mark (WatcherLook.mark): a dot, passed with a check, failed a red triangle with an
/// exclamation mark in the same room. Waiting on you pulses, unless Reduce Motion is on. Hidden
/// from VoiceOver: the line's label says the state ("failed").
struct WatcherDot: View {
    let state: WatcherState
    let size: CGFloat
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        let dot = mark
            .frame(width: size, height: size)
            .accessibilityHidden(true)
        if state == .waiting && !reduceMotion {
            dot.phaseAnimator([false, true]) { view, dim in
                view.opacity(dim ? 0.3 : 1).scaleEffect(dim ? 0.8 : 1)
            } animation: { _ in .easeInOut(duration: 0.7) }
        } else {
            dot
        }
    }

    @ViewBuilder private var mark: some View {
        switch WatcherLook.mark(state) {
        case .triangle:
            // A little larger than the dot: a triangle reads smaller than a circle of its width.
            Image(systemName: "exclamationmark.triangle.fill")
                .font(.system(size: size * 1.05, weight: .bold))
                .foregroundStyle(WatcherLook.dot(state))
        case .check:
            Circle()
                .fill(WatcherLook.dot(state))
                .overlay {
                    Image(systemName: "checkmark")
                        .font(.system(size: size * 0.7, weight: .heavy))
                        .foregroundStyle(Color.black.opacity(0.75))
                }
        case .dot:
            Circle().fill(WatcherLook.dot(state))
        }
    }
}

/// The intro's scroll for a notch line (WatcherIntro): the intro's key (a new intro scrolls
/// again), the room the line has and its measured width.
struct WatcherScroll: Equatable {
    let key: String
    let room: CGFloat
    let textWidth: CGFloat
}

/// One watcher on a notch place: the dot and the line (WatcherText.notchLine). During the intro
/// (`scroll`) the full line scrolls through once when it doesn't fit, with now playing's timing
/// and fade (ScrollOnceText); at rest the short line. Its clicks are the caller's: the wings take
/// them in SwiftUI, the strip through DeskHitTest.
struct NotchWatcherLine: View {
    let text: String
    let state: WatcherState
    let size: CGFloat
    let font: String
    let ink: String
    var scroll: WatcherScroll? = nil

    var body: some View {
        HStack(spacing: size * 0.35) {
            WatcherDot(state: state, size: size * 0.5)
            if let scroll {
                ScrollOnceText(text: text, trackKey: scroll.key, textWidth: scroll.textWidth, room: scroll.room,
                               font: .custom(font, size: size))
                    .foregroundStyle(LinearGradient.ink(ink))
            } else {
                Text(text)
                    .font(.custom(font, size: size))
                    .lineLimit(1)
                    .truncationMode(.tail)
                    .foregroundStyle(LinearGradient.ink(ink))
            }
        }
        .opacity(0.88 * WatcherLook.opacity(state))
    }
}

/// The Desk's watcher stack: up to four watchers, most urgent first. Each row reports its frame
/// (DeskController routes the clicks: a click opens the link, a two-finger click the menu).
struct DeskWatchers: View {
    var model: DeskModel
    let font: String
    let size: CGFloat
    let alignment: HorizontalAlignment

    var body: some View {
        let shown = Array(model.watchers.prefix(WatcherPlacement.deskRows))
        if !shown.isEmpty {
            TimelineView(.periodic(from: .now, by: 1)) { context in
                VStack(alignment: alignment, spacing: size * 0.5) {
                    ForEach(shown) { w in
                        DeskWatcherRow(watcher: w, now: context.date, font: font, size: size, alignment: alignment)
                            .onGlobalFrame { model.watcherRowFrames[w.id] = $0 }
                            .transition(.opacity)
                    }
                    if model.watchers.count > shown.count {
                        Text("+\(model.watchers.count - shown.count) more")
                            .font(.custom(font, size: size * 0.8))
                            .opacity(0.6)
                    }
                }
                .animation(.easeOut(duration: 0.6), value: shown.map(\.id))
            }
            .onGlobalFrame { model.watchersFrame = $0 }
            .padding(.vertical, DeskNowPlaying.padding)
        }
    }
}

/// One Desk watcher: dot, title, elapsed and progress on the first line, the note under it. The
/// faint plate gives the transparent window a pixel under the whole row, so it takes the click.
private struct DeskWatcherRow: View {
    let watcher: Watcher
    let now: Date
    let font: String
    let size: CGFloat
    let alignment: HorizontalAlignment
    @State private var hovering = false

    var body: some View {
        VStack(alignment: alignment, spacing: size * 0.15) {
            HStack(alignment: .firstTextBaseline, spacing: size * 0.45) {
                WatcherDot(state: watcher.state, size: size * 0.55)
                    .alignmentGuide(.firstTextBaseline) { d in d[.bottom] - size * 0.08 }
                Text(watcher.title)
                    .lineLimit(1)
                    .underline(hovering && watcher.link != nil)
                Text(WatcherText.elapsed(watcher.elapsed(now: now)))
                    .opacity(0.7)
                if let p = watcher.progress {
                    Text(p).opacity(0.7)
                }
            }
            .font(.custom(font, size: size))
            if let note = WatcherText.deskNote(watcher) {
                Text(note)
                    .font(.custom(font, size: size * 0.8))
                    .lineLimit(1)
                    .opacity(0.65)
            }
        }
        .opacity(WatcherLook.opacity(watcher.state))
        .frame(maxWidth: size * 22, alignment: alignment == .trailing ? .trailing : .leading)
        .background(Color.black.opacity(DeskPointerMenu.hitPlateOpacity))
        .contentShape(Rectangle())
        .onHover { inside in
            guard watcher.link != nil else { return }
            hovering = inside
            if inside { NSCursor.pointingHand.push() } else { NSCursor.pop() }
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(WatcherText.spoken(watcher, now: now))
        .accessibilityAddTraits(watcher.link != nil ? .isLink : [])
    }
}

/// The watcher menu (a two-finger click on a watcher, on the Desk or the notch): Dismiss,
/// Dismiss All, Watcher Settings…. And the click: a watcher's link, https only.
enum WatcherMenu {
    /// Opens the watcher's link when it has an https one; nothing otherwise.
    static func open(_ w: Watcher?) {
        guard let link = w?.link, link.scheme?.lowercased() == "https" else { return }
        NSWorkspace.shared.open(link)
    }

    /// The Desk's NSMenu (its event monitors pop it up).
    static func menu(for id: String?) -> NSMenu {
        let menu = NSMenu()
        menu.autoenablesItems = false
        let dismiss = NSMenuItem(title: "Dismiss", action: #selector(WatcherMenuTarget.dismiss(_:)), keyEquivalent: "")
        dismiss.target = WatcherMenuTarget.shared
        dismiss.representedObject = id
        dismiss.isEnabled = id != nil
        menu.addItem(dismiss)
        let all = NSMenuItem(title: "Dismiss All", action: #selector(WatcherMenuTarget.dismissAll), keyEquivalent: "")
        all.target = WatcherMenuTarget.shared
        menu.addItem(all)
        menu.addItem(.separator())
        let settings = NSMenuItem(title: "Watcher Settings…", action: #selector(WatcherMenuTarget.settings), keyEquivalent: "")
        settings.target = WatcherMenuTarget.shared
        menu.addItem(settings)
        return menu
    }
}

final class WatcherMenuTarget: NSObject {
    static let shared = WatcherMenuTarget()
    // Menu items act on the main thread.
    @objc func dismiss(_ sender: NSMenuItem) {
        guard let id = sender.representedObject as? String else { return }
        MainActor.assumeIsolated { WatcherStore.shared.dismiss(id) }
    }
    @objc func dismissAll() { MainActor.assumeIsolated { WatcherStore.shared.dismissAll() } }
    @objc func settings() { MainActor.assumeIsolated { SettingsWindowController.shared.show(.integrations) } }
}

/// The same menu as SwiftUI items, for the notch wings' context menu.
struct WatcherMenuItems: View {
    let id: String?

    var body: some View {
        Button("Dismiss") { if let id { WatcherStore.shared.dismiss(id) } }
            .disabled(id == nil)
        Button("Dismiss All") { WatcherStore.shared.dismissAll() }
        Divider()
        Button("Watcher Settings…") { SettingsWindowController.shared.show(.integrations) }
    }
}
