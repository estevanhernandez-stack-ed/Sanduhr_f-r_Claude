import SwiftUI
import AppKit

/// Wraps `NSVisualEffectView` so SwiftUI can use real macOS vibrancy as the
/// window background. `.hudWindow` matches the feel of menu-bar HUDs — a nice
/// upgrade over the flat `#1c1c1c` the Python/Tk widget has to settle for.
struct VisualEffectView: NSViewRepresentable {
    var material: NSVisualEffectView.Material = .hudWindow
    var blendingMode: NSVisualEffectView.BlendingMode = .behindWindow
    var state: NSVisualEffectView.State = .active

    func makeNSView(context: Context) -> NSVisualEffectView {
        let v = NSVisualEffectView()
        v.material = material
        v.blendingMode = blendingMode
        v.state = state
        v.isEmphasized = true
        return v
    }

    func updateNSView(_ v: NSVisualEffectView, context: Context) {
        v.material = material
        v.blendingMode = blendingMode
        v.state = state
    }
}
