import AppKit
import CoreGraphics
import Foundation
import QuartzCore

/// What the Desk's companion piece draws, decided apart from the layers so each rule is tested
/// (spec 4): which pack shows, the keyframes the render server steps, the roam's geometry in the
/// layer's y-up space, the demo's fixed stage and slow mood cycle, and state.yaml's field.

/// The pack the companion draws and its parsed sprite.
struct CompanionPick: Equatable {
    let packId: String
    let sprite: CompanionSprite
}

enum CompanionPicker {
    /// The demo mode's pack: the hourglass, when installed as a sprite.
    static let demoPackId = "hourglass"

    /// The pack to draw, or nil (the piece then draws nothing and runs nothing): none installed as a
    /// sprite, or its sprite.json unreadable. Demo mode takes the hourglass only, never the choice.
    static func pick(store: WidgetStore, settings: CompanionSettings, demo: Bool) -> CompanionPick? {
        let id: String?
        if demo {
            id = store.readInfo(demoPackId)?.kind == "sprite" ? demoPackId : nil
        } else {
            id = settings.activePack(store.load(), kindOf: { store.readInfo($0)?.kind })
        }
        guard let id, WidgetStore.isSafeId(id),
              let data = try? Data(contentsOf: store.packDir(id).appendingPathComponent("sprite.json")),
              let sprite = CompanionSprite.parse(data) else { return nil }
        return CompanionPick(packId: id, sprite: sprite)
    }
}

/// The frames a discrete keyframe animation steps through.
enum CompanionFrames {
    /// The `contentsRect` of each frame index. A layer's contents are not flipped, so the unit y
    /// counts from the sheet's bottom edge. Indexes past the sheet are dropped.
    static func rects(_ indexes: [Int], sheetWidth: Int, sheetHeight: Int, frameWidth: Int, frameHeight: Int) -> [CGRect] {
        indexes.compactMap {
            CompanionSheet.contentsRect($0, sheetWidth: sheetWidth, sheetHeight: sheetHeight,
                                        frameWidth: frameWidth, frameHeight: frameHeight, originAtBottom: true)
        }
    }

    /// Seconds one pass over `count` frames takes at `fps`.
    static func duration(count: Int, fps: Int) -> TimeInterval { Double(count) / Double(max(1, fps)) }

    /// The render server's frame stepper: `rects` shown one after another, each for 1/fps, with no
    /// blending between them. A mood loop repeats forever; a trigger plays once and holds.
    static func animation(_ rects: [CGRect], fps: Int, repeatForever: Bool) -> CAKeyframeAnimation {
        let a = CAKeyframeAnimation(keyPath: "contentsRect")
        a.values = rects.map { NSValue(rect: $0) }
        a.calculationMode = .discrete
        a.duration = duration(count: rects.count, fps: fps)
        a.repeatCount = repeatForever ? .infinity : 1
        a.isRemovedOnCompletion = false
        a.fillMode = .forwards
        return a
    }
}

/// The roam's geometry. The path is built y-down in the Desk window's points; the layer lives in
/// AppKit's y-up space inside its view, so every spot is flipped once here.
enum CompanionRoamMath {
    /// How often along a walk a position is sampled (seconds); the keyframes between are linear.
    static let sampleEvery: TimeInterval = 0.25

    /// The Desk window less the menu bar on top and the Dock where it sits, y-down.
    static func area(window: CGSize, topInset: CGFloat, dock: DockInsets) -> CGRect {
        CGRect(x: dock.left, y: topInset,
               width: max(0, window.width - dock.left - dock.right),
               height: max(0, window.height - topInset - dock.bottom))
    }

    /// A spot (window points, y-down, bottom-centre of the sprite) as a point in the view's layer
    /// space (y-up, origin at the view's bottom-left). `viewFrame` is the view in window base
    /// coordinates (y-up); `windowHeight` is the window's height.
    static func layerPoint(_ spot: CompanionSpot, windowHeight: Double, viewFrame: CGRect) -> CGPoint {
        CGPoint(x: spot.x - viewFrame.minX, y: (windowHeight - spot.y) - viewFrame.minY)
    }

    /// The inverse for a view's own bottom-centre: where it stands in window points, y-down.
    static func windowPoint(viewFrame: CGRect, windowHeight: Double) -> CGPoint {
        CGPoint(x: viewFrame.midX, y: windowHeight - viewFrame.minY)
    }

    /// The layer's x scale: mirrored while it walks left (the art faces right).
    static func xScale(facingLeft: Bool) -> CGFloat { facingLeft ? -1 : 1 }

    /// Spots along the route from `from` to `to` points walked, `count` + 1 of them, evenly spaced.
    static func samples(_ path: CompanionPath, from: Double, to: Double, count: Int) -> [CompanionSpot] {
        let n = max(1, count)
        return (0...n).map { path.at(from + (to - from) * Double($0) / Double(n)) }
    }

    /// The position keyframes for a walk, in layer space.
    static func positionAnimation(_ spots: [CompanionSpot], windowHeight: Double, viewFrame: CGRect,
                                  duration: TimeInterval) -> CAKeyframeAnimation {
        let a = CAKeyframeAnimation(keyPath: "position")
        a.values = spots.map { NSValue(point: layerPoint($0, windowHeight: windowHeight, viewFrame: viewFrame)) }
        a.calculationMode = .linear
        a.duration = duration
        a.isRemovedOnCompletion = false
        a.fillMode = .forwards
        return a
    }

    /// The mirror, stepped (no blending) at the samples where the facing changes.
    static func flipAnimation(_ spots: [CompanionSpot], duration: TimeInterval) -> CAKeyframeAnimation {
        let a = CAKeyframeAnimation(keyPath: "transform.scale.x")
        a.values = spots.map { xScale(facingLeft: $0.facingLeft) }
        a.calculationMode = .discrete
        a.duration = duration
        a.isRemovedOnCompletion = false
        a.fillMode = .forwards
        return a
    }
}

/// Demo mode's companion: the hourglass at one fixed stage, the moods taking turns slowly. Never
/// reads real growth or usage.
enum CompanionDemo {
    /// Growth the demo stands at: the stage at 0.5 (the third of four for the hourglass).
    static let growth = 0.5
    /// How long each sample mood stays.
    static let moodSeconds: TimeInterval = 20
    static let moods: [CompanionMood] = [.content, .focused, .worried, .celebrate, .exhausted]

    static func mood(secondsIn elapsed: TimeInterval) -> CompanionMood {
        let i = Int(max(0, elapsed) / moodSeconds) % moods.count
        return moods[i]
    }
}

/// state.yaml's `companion:`: ids and enums only, nil (written `null`) while the piece is not shown.
struct CompanionDebug: Equatable {
    var pack: String
    var stage: String
    var mood: String
    var roaming: Bool

    static func yaml(_ c: CompanionDebug?) -> YAMLNode {
        guard let c else { return .null }
        return .map([YAMLPair("pack", .string(c.pack)), YAMLPair("stage", .string(c.stage)),
                     YAMLPair("mood", .string(c.mood)), YAMLPair("roaming", .bool(c.roaming))])
    }
}
