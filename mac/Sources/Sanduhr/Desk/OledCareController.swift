import AppKit
import SwiftUI

/// Runs OLED care (OledCare.swift's rules) on the real Desk. It exists only while the switch is on and the
/// Desk shows: one 3-minute timer that advances the drift step, and one IdleClock subscription that watches for
/// the person being away. Off, or with the Desk down, neither exists and every piece, window and opacity is
/// back where it was. The notch island, the pill and the lock screen line (a separate helper) never drift.
@MainActor
final class OledCareController {
    static let shared = OledCareController()

    private let idleClock: IdleClock
    private let reduceMotion: () -> Bool
    private var driftTimer: Timer?
    private var idleToken: IdleClock.Token?
    private var idle = OledIdle()

    /// True while OLED care runs: the switch is on and the Desk shows.
    private(set) var active = false
    /// The Desk and the widget are dimmed now.
    var dimmed: Bool { idle.dimmed }

    init(idleClock: IdleClock = .shared,
         reduceMotion: @escaping () -> Bool = { NSWorkspace.shared.accessibilityDisplayShouldReduceMotion }) {
        self.idleClock = idleClock
        self.reduceMotion = reduceMotion
    }

    /// Starts or stops to match the switch and the Desk. Called when the Desk starts or stops and when the
    /// switch flips; a no-op when nothing changed.
    func apply(deskRunning: Bool, switchOn: Bool = OledCare.isOn()) {
        let want = deskRunning && switchOn
        guard want != active else { return }
        active = want
        if want { begin() } else { end() }
    }

    /// The Desk window was rebuilt (a display change): it comes up at the opacity it should have now.
    func deskWindowRebuilt(_ window: NSWindow?) {
        window?.alphaValue = idle.dimmed ? OledIdle.deskDimmedOpacity : 1
    }

    // MARK: Running

    private func begin() {
        let model = DeskController.shared.model
        model.oledStep = 0
        idle = OledIdle()
        let t = Timer(timeInterval: OledDrift.every, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.advance() }
        }
        t.tolerance = 10
        RunLoop.main.add(t, forMode: .common)
        driftTimer = t
        idleToken = idleClock.subscribe(every: OledIdle.pollEvery) { [weak self] seconds in
            self?.observe(secondsSinceInput: seconds)
        }
    }

    private func end() {
        driftTimer?.invalidate()
        driftTimer = nil
        if let token = idleToken { idleClock.unsubscribe(token) }
        idleToken = nil
        _ = idle.reset()
        setDimmed(false, duration: 0)
        DeskController.shared.model.oledStep = nil
    }

    /// One step of the orbit: a single implicit animation of every piece's offset, nothing between steps.
    private func advance() {
        let model = DeskController.shared.model
        let next = (model.oledStep ?? 0) + 1
        if reduceMotion() {
            model.oledStep = next
        } else {
            withAnimation(.easeInOut(duration: 2)) { model.oledStep = next }
        }
    }

    private func observe(secondsSinceInput seconds: TimeInterval) {
        let edge = idle.observe(lastInputAt: Date().addingTimeInterval(-seconds))
        switch edge {
        case .none: return
        case .dim, .wake:
            let duration = OledIdle.easeFor(edge, animate: !reduceMotion())
            setDimmed(edge == .dim, duration: duration)
        }
    }

    /// The Desk's window to 0.40 and the widget's to 0.70, or both back to 1.
    private func setDimmed(_ dim: Bool, duration: TimeInterval) {
        let windows: [(NSWindow?, CGFloat)] = [
            (DeskController.shared.window, dim ? OledIdle.deskDimmedOpacity : 1),
            (NSApp.windows.first { $0 is FloatingPanel }, dim ? OledIdle.widgetDimmedOpacity : 1),
        ]
        guard duration > 0 else {
            for (w, a) in windows { w?.alphaValue = a }
            return
        }
        NSAnimationContext.runAnimationGroup { ctx in
            ctx.duration = duration
            for (w, a) in windows { w?.animator().alphaValue = a }
        }
    }
}

// MARK: - The drift on a Desk piece

/// A Desk piece at OLED care's orbit offset. The piece's true place is read from a probe behind it (outside
/// the offset, so it never feeds back), and the offset is the orbit's point clamped clear of the menu bar,
/// the notch and the Dock. Off, the offset is zero and the probe is empty.
private struct OledDriftModifier: ViewModifier {
    var model: DeskModel
    @State private var home = CGRect.zero

    func body(content: Content) -> some View {
        let d = offset()
        content
            .offset(x: d.width, y: d.height)
            .background(probe)
    }

    private var drifting: Bool { model.oledStep != nil && !model.arrange.active }

    private func offset() -> CGSize {
        guard drifting, let step = model.oledStep, home.width > 0, model.deskSize.width > 0 else { return .zero }
        let area = CGRect(origin: .zero, size: model.deskSize)
        let moved = OledDrift.drifted(home, step: step, area: area, clearance: clearance())
        return CGSize(width: moved.minX - home.minX, height: moved.minY - home.minY)
    }

    /// The menu bar on top (and the notch's strip for a piece beneath it), the Dock on its side.
    private func clearance() -> OledInsets {
        var top = model.topInset
        if let notch = model.notchRect, home.maxX > notch.minX, home.minX < notch.maxX { top = max(top, notch.maxY) }
        return OledInsets(left: model.dockInsets.left, top: top, right: model.dockInsets.right, bottom: model.dockInsets.bottom)
    }

    @ViewBuilder
    private var probe: some View {
        if drifting {
            GeometryReader { geo in
                let frame = geo.frame(in: .named(DeskArrange.space))
                Color.clear
                    .onAppear { home = frame }
                    .onChange(of: frame) { _, f in home = f }
            }
        }
    }
}

extension View {
    /// OLED care's slow drift for a Desk piece (OledCareController); nothing changes while it is off.
    func oledDrift(_ model: DeskModel) -> some View { modifier(OledDriftModifier(model: model)) }
}
