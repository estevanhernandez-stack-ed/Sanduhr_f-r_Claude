import AppKit
import SwiftUI
import QuartzCore

/// The colors of the 16 bars from the Desk ink's stops (spec 14). Off: every bar is the ink's first
/// stop. On ("Gradient bars"): the stops run left to right across the bars. Pure, so it is tested.
enum AudioBarColors {
    static func colors(stops: [NSColor], count: Int, gradient: Bool) -> [NSColor] {
        guard let first = stops.first, count > 0 else { return [] }
        guard gradient, stops.count > 1, count > 1 else { return [NSColor](repeating: first, count: count) }
        return (0..<count).map { i in
            let t = Double(i) / Double(count - 1) * Double(stops.count - 1)
            let lo = min(Int(t), stops.count - 2)
            let a = stops[lo].usingColorSpace(.sRGB) ?? stops[lo]
            let b = stops[lo + 1].usingColorSpace(.sRGB) ?? stops[lo + 1]
            return a.blended(withFraction: CGFloat(t - Double(lo)), of: b) ?? a
        }
    }
}

/// The geometry of one frame of bars inside a box. Pure.
enum AudioBarsGeometry {
    /// The resting slot: a dot at the baseline when a bar is at zero.
    static let restHeight: CGFloat = 2

    /// A bar's frame in a box `size` wide, `count` bars, from a height of 0 to 1.
    static func barFrame(index: Int, count: Int, size: CGSize, height: Float) -> CGRect {
        let pitch = size.width / CGFloat(max(1, count))
        let width = max(1, pitch * 0.62)
        let h = restHeight + CGFloat(min(max(height, 0), 1)) * max(0, size.height - restHeight)
        return CGRect(x: CGFloat(index) * pitch + (pitch - width) / 2, y: 0, width: width, height: h)
    }

    /// A peak cap's frame: a thin bar riding at `height`.
    static func capFrame(index: Int, count: Int, size: CGSize, height: Float) -> CGRect {
        let bar = barFrame(index: index, count: count, size: size, height: height)
        return CGRect(x: bar.minX, y: min(bar.maxY, size.height - restHeight) , width: bar.width, height: restHeight)
    }
}

/// The bars as layers. A `CAReplicatorLayer` repeats the resting dot under every slot; each bar and
/// its peak cap is its own layer, because a replicator copies one sublayer and cannot give each copy
/// its own height. Heights are set only when the driver steps a frame, so while the driver sleeps
/// (idle, paused, switch off) nothing here changes or redraws. The view takes no clicks.
struct AudioBarsLayerView: NSViewRepresentable {
    let colors: [NSColor]
    var count = 16

    func makeNSView(context: Context) -> AudioBarsNSView {
        let v = AudioBarsNSView(count: count)
        v.colors = colors
        AudioBarsDriver.shared.attach(v)
        return v
    }

    func updateNSView(_ view: AudioBarsNSView, context: Context) {
        view.colors = colors
    }

    static func dismantleNSView(_ view: AudioBarsNSView, coordinator: ()) {
        AudioBarsDriver.shared.detach(view)
    }
}

final class AudioBarsNSView: NSView, AudioBarsSink {
    private let count: Int
    private let slots = CAReplicatorLayer()
    private let slot = CALayer()
    private var bars: [CALayer] = []
    private var caps: [CALayer] = []
    private var heights: [Float]
    private var capHeights: [Float]
    private var showsCaps = true

    var colors: [NSColor] = [] {
        didSet { if colors != oldValue { applyColors() } }
    }

    init(count: Int) {
        self.count = count
        heights = [Float](repeating: 0, count: count)
        capHeights = heights
        super.init(frame: .zero)
        wantsLayer = true
        layerContentsRedrawPolicy = .never
        layer?.addSublayer(slots)
        slots.addSublayer(slot)
        slot.opacity = 0.3
        for _ in 0..<count {
            let bar = CALayer(), cap = CALayer()
            bar.cornerRadius = 1
            layer?.addSublayer(bar)
            layer?.addSublayer(cap)
            bars.append(bar)
            caps.append(cap)
        }
    }

    required init?(coder: NSCoder) { nil }

    override var isOpaque: Bool { false }
    override func hitTest(_ point: NSPoint) -> NSView? { nil }

    override func layout() {
        super.layout()
        noActions {
            slots.frame = bounds
            slots.instanceCount = count
            let pitch = bounds.width / CGFloat(max(1, count))
            slots.instanceTransform = CATransform3DMakeTranslation(pitch, 0, 0)
            slot.frame = AudioBarsGeometry.barFrame(index: 0, count: count, size: bounds.size, height: 0)
            place()
        }
    }

    func show(heights: [Float], caps: [Float], showsCaps: Bool) {
        self.heights = heights
        self.capHeights = caps
        self.showsCaps = showsCaps
        noActions { place() }
    }

    private func place() {
        for i in 0..<count {
            let h = i < heights.count ? heights[i] : 0
            let c = i < capHeights.count ? capHeights[i] : 0
            bars[i].frame = AudioBarsGeometry.barFrame(index: i, count: count, size: bounds.size, height: h)
            caps[i].frame = AudioBarsGeometry.capFrame(index: i, count: count, size: bounds.size, height: c)
            caps[i].isHidden = !showsCaps || c <= h + 0.01
        }
    }

    private func applyColors() {
        noActions {
            slot.backgroundColor = (colors.first ?? .white).cgColor
            for i in 0..<count {
                let color = (i < colors.count ? colors[i] : colors.last ?? .white).cgColor
                bars[i].backgroundColor = color
                caps[i].backgroundColor = color
            }
        }
    }

    private func noActions(_ body: () -> Void) {
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        body()
        CATransaction.commit()
    }
}
