import AppKit
import SwiftUI
import QuartzCore

/// The companion's layers (spec 4), the BreathLayerView pattern: the render server steps the frames
/// (a discrete keyframe animation on `contentsRect`) and moves the sprite (a keyframe animation on
/// `position`), so no timer or timeline view drives a frame on the main thread. The view takes no
/// clicks. Layers: the view's own, `host` (the one whose speed pauses everything under it) and
/// `sprite`, anchored bottom-centre.
struct CompanionLayerView: NSViewRepresentable {
    let driver: CompanionDriver
    let size: CGSize

    func makeNSView(context: Context) -> CompanionNSView {
        let v = CompanionNSView()
        driver.attach(v)
        return v
    }

    func updateNSView(_ view: CompanionNSView, context: Context) {
        view.cell = size
    }

    static func dismantleNSView(_ view: CompanionNSView, coordinator: ()) {
        view.clear()
    }
}

final class CompanionNSView: NSView {
    private let host = CALayer()
    private let sprite = CALayer()
    /// The size the sprite draws at: cell x pack scale x piece scale.
    var cell = CGSize(width: 1, height: 1) {
        didSet { if cell != oldValue { needsLayout = true } }
    }
    /// Standing at the anchor (not somewhere on a roam): layout keeps it bottom-centre.
    private var atHome = true

    override init(frame: NSRect) {
        super.init(frame: frame)
        wantsLayer = true
        layerContentsRedrawPolicy = .never
        layer?.addSublayer(host)
        host.addSublayer(sprite)
        sprite.anchorPoint = CGPoint(x: 0.5, y: 0)
        sprite.magnificationFilter = .nearest
        sprite.minificationFilter = .nearest
        sprite.contentsGravity = .resize
        sprite.isOpaque = false
    }

    required init?(coder: NSCoder) { nil }

    override var isOpaque: Bool { false }
    override func hitTest(_ point: NSPoint) -> NSView? { nil }

    override func layout() {
        super.layout()
        noActions {
            host.frame = bounds
            sprite.bounds = CGRect(origin: .zero, size: cell)
            if atHome { sprite.position = CGPoint(x: bounds.midX, y: 0) }
        }
    }

    private func noActions(_ body: () -> Void) {
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        body()
        CATransaction.commit()
    }

    /// The stage's sheet. Nearest-neighbour, so the pixels stay square.
    func setImage(_ image: CGImage?) {
        noActions {
            sprite.removeAllAnimations()
            sprite.contents = image
            sprite.contentsRect = CGRect(x: 0, y: 0, width: 1, height: 1)
        }
    }

    /// Steps `rects` at `fps` on the render server: forever for a mood loop, once for a trigger.
    func play(_ rects: [CGRect], fps: Int, repeatForever: Bool) {
        guard let first = rects.first else { return }
        noActions {
            sprite.removeAnimation(forKey: "frames")
            sprite.contentsRect = first
            sprite.add(CompanionFrames.animation(rects, fps: fps, repeatForever: repeatForever), forKey: "frames")
        }
    }

    /// The view in window base coordinates (y-up) and the window's height, nil off screen.
    private func windowGeometry() -> (frame: CGRect, height: Double)? {
        guard let window else { return nil }
        return (convert(bounds, to: nil), Double(window.frame.height))
    }

    /// The standing place in window points (y-down), where a roam starts from.
    func homePoint() -> CGPoint? {
        windowGeometry().map { CompanionRoamMath.windowPoint(viewFrame: $0.frame, windowHeight: $0.height) }
    }

    /// Walks the sampled route over `duration`. The model value ends at the last spot.
    func walk(_ spots: [CompanionSpot], duration: TimeInterval) {
        guard let g = windowGeometry(), let last = spots.last else { return }
        atHome = false
        noActions {
            sprite.removeAnimation(forKey: "walk")
            sprite.removeAnimation(forKey: "flip")
            sprite.position = CompanionRoamMath.layerPoint(last, windowHeight: g.height, viewFrame: g.frame)
            sprite.setValue(CompanionRoamMath.xScale(facingLeft: last.facingLeft), forKeyPath: "transform.scale.x")
            sprite.add(CompanionRoamMath.positionAnimation(spots, windowHeight: g.height, viewFrame: g.frame,
                                                           duration: duration), forKey: "walk")
            sprite.add(CompanionRoamMath.flipAnimation(spots, duration: duration), forKey: "flip")
        }
    }

    /// Stands at `spot` (a roam paused for a trigger or sleep).
    func hold(at spot: CompanionSpot) {
        guard let g = windowGeometry() else { return }
        atHome = false
        noActions {
            sprite.removeAnimation(forKey: "walk")
            sprite.removeAnimation(forKey: "flip")
            sprite.position = CompanionRoamMath.layerPoint(spot, windowHeight: g.height, viewFrame: g.frame)
            sprite.setValue(CompanionRoamMath.xScale(facingLeft: spot.facingLeft), forKeyPath: "transform.scale.x")
        }
    }

    /// Back at the anchor, facing right.
    func goHome() {
        atHome = true
        noActions {
            sprite.removeAnimation(forKey: "walk")
            sprite.removeAnimation(forKey: "flip")
            sprite.setValue(1, forKeyPath: "transform.scale.x")
            sprite.position = CGPoint(x: bounds.midX, y: 0)
        }
    }

    /// Speed 0 holds every animation under the host where it is; back to 1 carries on from there.
    func setPaused(_ paused: Bool) {
        if paused, host.speed != 0 {
            let t = host.convertTime(CACurrentMediaTime(), from: nil)
            host.speed = 0
            host.timeOffset = t
        } else if !paused, host.speed == 0 {
            let t = host.timeOffset
            host.speed = 1
            host.timeOffset = 0
            host.beginTime = 0
            host.beginTime = host.convertTime(CACurrentMediaTime(), from: nil) - t
        }
    }

    /// Nothing left running or drawn.
    func clear() {
        setPaused(false)
        noActions {
            sprite.removeAllAnimations()
            sprite.contents = nil
        }
    }
}

/// The Desk piece: the sprite at the stage's cell size times the pack's scale times the piece's
/// size, bottom-centre in its anchor. It takes no clicks. With no sprite picked it draws nothing
/// and the driver runs nothing.
struct CompanionPiece: View {
    var model: DeskModel
    var scale: Double = 1

    var body: some View {
        let driver = model.companion
        // A zero-size anchor is always in the tree, so the driver hears the piece appear even while
        // no sprite is picked (an empty group would never fire onAppear).
        ZStack(alignment: .bottom) {
            Color.clear.frame(width: 0, height: 0)
            if driver.pick != nil {
                let size = CGSize(width: max(1, driver.cell.width * scale), height: max(1, driver.cell.height * scale))
                CompanionLayerView(driver: driver, size: size)
                    .frame(width: size.width, height: size.height)
                    .allowsHitTesting(false)
                    .accessibilityHidden(true)
            }
        }
        .onAppear { driver.show(model) }
        .onDisappear { driver.hide() }
    }
}
