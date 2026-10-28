import CoreGraphics
import Foundation

/// An edge of the screen a companion may roam (FORMAT 5.5 `roam.edges`).
enum CompanionEdge: String, Equatable, Sendable {
    case bottom, left, right
}

/// One growth stage of a sprite (FORMAT 5.5): its own sheet and cell size, shown from `minGrowth`.
struct CompanionStage: Equatable, Sendable {
    let id: String
    let name: String
    let minGrowth: Double
    let sheet: String
    let frameWidth: Int
    let frameHeight: Int

    /// A sprite v2 pixel sheet (`.json`): drawn from the PNG Sanduhr rasterized at install
    /// (`WidgetStore.rasterPath`).
    var isPixelSheet: Bool { sheet.hasSuffix(".json") }
}

/// A sprite pack's `sprite.json` as the Desk companion draws it (FORMAT 5.5; a port of Windows'
/// CompanionSprite.cs): the art's stages and the animation table, the mood and trigger mapping, the
/// display scale and frame rate, and where it may roam. The pack was validated at install
/// (`PackValidator`); `parse` is still lenient and returns nil rather than throw on a file that is
/// not what the validator passed, so a hand-edited install folder can never take the Desk down.
struct CompanionSprite: Equatable, Sendable {
    /// The animation drawn while roaming.
    static let walk = "walk"
    /// The id given to the one stage of a sprite with a single top-level `sheet` and no `stages`.
    static let singleStageId = "only"

    let scale: Int
    let fps: Int
    let stages: [CompanionStage]
    let animations: [String: [Int]]
    let moods: [CompanionMood: String]
    let triggers: [CompanionTrigger: String]
    /// In the order the pack names them, no repeats; bottom alone when it names none.
    let edges: [CompanionEdge]
    let speedPxPerSecond: Double

    /// The frame indexes of `animation`; empty when the pack has no such animation.
    func frames(_ animation: String) -> [Int] { animations[animation] ?? [] }

    /// The highest frame index any animation uses (a sheet must have cells up to it).
    var maxFrame: Int { animations.values.flatMap { $0 }.max() ?? 0 }

    /// The stage drawn at `growth` (`CompanionGrowth.stageIndex`).
    func stage(at growth: Double) -> CompanionStage {
        stages[CompanionGrowth.stageIndex(stages.map(\.minGrowth), growth)]
    }

    /// Reads a `sprite.json`. Nil when it is not a usable sprite: no stage, a stage without a sheet
    /// or a cell size in range, a required animation, mood or trigger missing, or a mapping naming
    /// an animation that is not there.
    static func parse(_ data: Data) -> CompanionSprite? {
        guard let scalars = PackJson.decodeUTF8([UInt8](data)),
              case .success(let value) = PackJson.parse(scalars),
              let o = value.object
        else { return nil }
        let scale = int(o["scale"], 1, 4) ?? 1
        let fps = int(o["fps"], 1, 24) ?? 8
        let fw = int(o["frame_w"], 8, 64), fh = int(o["frame_h"], 8, 64)

        var stages: [CompanionStage] = []
        if let list = o["stages"]?.array {
            for item in list {
                guard let s = item.object,
                      let id = s["id"]?.string, let sheet = s["sheet"]?.string, PackValidator.safeRelPath(sheet),
                      let sw = int(s["frame_w"], 8, 64), let sh = int(s["frame_h"], 8, 64)
                else { return nil }
                var min = 0.0
                if let n = s["min_growth"]?.number, n.isFinite { min = Swift.min(Swift.max(n.value, 0), 1) }
                if let last = stages.last, min <= last.minGrowth { return nil }
                stages.append(CompanionStage(id: id, name: s["name"]?.string ?? id, minGrowth: stages.isEmpty ? 0 : min,
                                             sheet: sheet, frameWidth: sw, frameHeight: sh))
            }
        } else if let single = o["sheet"]?.string, PackValidator.safeRelPath(single), let fw, let fh {
            stages.append(CompanionStage(id: singleStageId, name: singleStageId, minGrowth: 0, sheet: single,
                                         frameWidth: fw, frameHeight: fh))
        }
        if stages.isEmpty { return nil }

        guard let anims = o["animations"]?.object else { return nil }
        var animations: [String: [Int]] = [:]
        for name in anims.keys {
            guard let frames = anims[name]?.array, !frames.isEmpty else { return nil }
            var indexes: [Int] = []
            for f in frames {
                guard let i = int(f, 0, Int.max) else { return nil }
                indexes.append(i)
            }
            animations[name] = indexes
        }
        for need in PackValidator.requiredAnimations where animations[need] == nil { return nil }

        guard let m = o["moods"]?.object else { return nil }
        var moods: [CompanionMood: String] = [:]
        for mood in CompanionMood.allCases {
            guard let anim = m[mood.key]?.string, animations[anim] != nil else { return nil }
            moods[mood] = anim
        }
        guard let t = o["triggers"]?.object else { return nil }
        var triggers: [CompanionTrigger: String] = [:]
        for trigger in CompanionTrigger.allCases {
            guard let anim = t[trigger.key]?.string, animations[anim] != nil else { return nil }
            triggers[trigger] = anim
        }

        var edges: [CompanionEdge] = []
        var speed = 30.0
        if let roam = o["roam"]?.object {
            for e in roam["edges"]?.array ?? [] {
                if let edge = e.string.flatMap(CompanionEdge.init(rawValue:)), !edges.contains(edge) { edges.append(edge) }
            }
            if let sp = roam["speed_px_s"]?.number, sp.isFinite { speed = Swift.min(Swift.max(sp.value, 5), 120) }
        }
        if edges.isEmpty { edges = [.bottom] }

        return CompanionSprite(scale: scale, fps: fps, stages: stages, animations: animations, moods: moods,
                               triggers: triggers, edges: edges, speedPxPerSecond: speed)
    }

    private static func int(_ v: PackValue?, _ min: Int, _ max: Int) -> Int? {
        guard let n = v?.number, n.isInt, n.isFinite, n.value >= Double(min), n.value <= Double(max) else { return nil }
        return Int(n.value)
    }
}

/// A pixel rectangle in a sheet: origin top-left, rows downward.
struct PxRect: Equatable, Sendable {
    let x: Int
    let y: Int
    let width: Int
    let height: Int
}

/// Cutting frames out of a sheet (FORMAT 5.5): cells of the stage's frame size, row-major, frame `i`
/// the `i`-th cell counting left to right, then top to bottom. The same rule for a PNG sheet and for
/// the PNG rasterized from a pixel sheet.
enum CompanionSheet {
    /// Cells across a sheet `sheetWidth` pixels wide; 0 when the width is not a whole number of cells.
    static func columns(sheetWidth: Int, frameWidth: Int) -> Int {
        frameWidth > 0 && sheetWidth >= frameWidth && sheetWidth % frameWidth == 0 ? sheetWidth / frameWidth : 0
    }

    /// Frame `index`'s cell as pixels, or nil when the sheet has no such cell (its size is not a
    /// whole number of cells, or the index is past its last cell).
    static func cell(_ index: Int, sheetWidth: Int, sheetHeight: Int, frameWidth: Int, frameHeight: Int) -> PxRect? {
        guard index >= 0, frameHeight > 0, sheetHeight >= frameHeight, sheetHeight % frameHeight == 0 else { return nil }
        let columns = columns(sheetWidth: sheetWidth, frameWidth: frameWidth)
        guard columns > 0 else { return nil }
        let rows = sheetHeight / frameHeight
        guard index < columns * rows else { return nil }
        return PxRect(x: index % columns * frameWidth, y: index / columns * frameHeight, width: frameWidth, height: frameHeight)
    }

    /// The cell as a `contentsRect` (unit coordinates of the whole sheet). The origin is the sheet's
    /// top-left, rows downward; pass `originAtBottom: true` for a layer whose contents are not
    /// flipped (the unit y then counts from the sheet's bottom edge).
    static func contentsRect(_ index: Int, sheetWidth: Int, sheetHeight: Int, frameWidth: Int, frameHeight: Int,
                             originAtBottom: Bool = false) -> CGRect? {
        guard let c = cell(index, sheetWidth: sheetWidth, sheetHeight: sheetHeight, frameWidth: frameWidth, frameHeight: frameHeight)
        else { return nil }
        let w = Double(sheetWidth), h = Double(sheetHeight)
        let y = originAtBottom ? h - Double(c.y + c.height) : Double(c.y)
        return CGRect(x: Double(c.x) / w, y: y / h, width: Double(c.width) / w, height: Double(c.height) / h)
    }

    /// The file a stage is drawn from: its PNG, or for a pixel sheet the PNG rasterized beside the
    /// pack's files.
    static func drawnFile(store: WidgetStore, packId: String, stage: CompanionStage) -> URL {
        if stage.isPixelSheet { return store.rasterPath(packId, stage.sheet) }
        return stage.sheet.split(separator: "/").reduce(store.packDir(packId)) { $0.appendingPathComponent(String($1)) }
    }
}

/// What to draw this frame: the animation, the frame index into the sheet, and whether it is a one-shot trigger.
struct CompanionPose: Equatable, Sendable {
    let animation: String
    let frame: Int
    let oneShot: Bool
}

/// Which frame the companion draws (pure: the time comes in). A looping animation (the mood's, or
/// `walk` while roaming) starts at its first frame whenever it changes and runs at the pack's `fps`.
/// A trigger's animation plays once from its first frame, then the loop resumes from its first
/// frame. Reduce Motion draws every animation at 1 fps. Exhausted sleeps: it never walks.
///
/// The Desk steps frames on the render server (a `contentsRect` keyframe animation at `fps`), so
/// this is what the caller asks when it needs the pose now: to start the loop, to size the trigger's
/// run (`duration`) and to know whether the roam clock may move (`playing`).
final class CompanionAnimator {
    private let sprite: CompanionSprite
    private var shot: (animation: String, start: Date)?
    private var loop: String?
    private var loopStart = Date.distantPast

    init(_ sprite: CompanionSprite) { self.sprite = sprite }

    /// Frames a second: the pack's, or 1 with Reduce Motion on.
    static func fps(for sprite: CompanionSprite, reduceMotion: Bool) -> Int {
        reduceMotion ? 1 : min(max(sprite.fps, 1), 24)
    }

    /// Starts `trigger`'s animation from its first frame (a trigger already playing is left to finish).
    func play(_ trigger: CompanionTrigger, at now: Date) {
        guard let anim = sprite.triggers[trigger], !sprite.frames(anim).isEmpty else { return }
        if let s = shot, s.animation == anim { return }
        shot = (anim, now)
    }

    /// How long a trigger's animation takes to play once.
    func duration(of trigger: CompanionTrigger, reduceMotion: Bool) -> TimeInterval {
        guard let anim = sprite.triggers[trigger] else { return 0 }
        return Double(sprite.frames(anim).count) / Double(Self.fps(for: sprite, reduceMotion: reduceMotion))
    }

    /// True while a trigger's animation is still playing at `now`.
    func playing(at now: Date, reduceMotion: Bool) -> Bool {
        guard let s = shot else { return false }
        return Self.index(now.timeIntervalSince(s.start), Self.fps(for: sprite, reduceMotion: reduceMotion))
            < sprite.frames(s.animation).count
    }

    /// True when the companion may walk now: not sleeping and no trigger playing.
    func canWalk(mood: CompanionMood, at now: Date, reduceMotion: Bool) -> Bool {
        mood != .exhausted && !playing(at: now, reduceMotion: reduceMotion)
    }

    /// The frame to draw at `now`.
    func pose(mood: CompanionMood, walking: Bool, at now: Date, reduceMotion: Bool) -> CompanionPose {
        let fps = Self.fps(for: sprite, reduceMotion: reduceMotion)
        if let s = shot {
            let frames = sprite.frames(s.animation)
            let i = Self.index(now.timeIntervalSince(s.start), fps)
            if i < frames.count { return CompanionPose(animation: s.animation, frame: frames[i], oneShot: true) }
            shot = nil
            loop = nil   // the mood resumes from its first frame
        }
        let name: String
        if walking, mood != .exhausted, !sprite.frames(CompanionSprite.walk).isEmpty {
            name = CompanionSprite.walk
        } else {
            name = sprite.moods[mood] ?? sprite.moods[.content] ?? CompanionSprite.walk
        }
        if loop != name {
            loop = name
            loopStart = now
        }
        let frames = sprite.frames(name)
        if frames.isEmpty { return CompanionPose(animation: name, frame: 0, oneShot: false) }
        return CompanionPose(animation: name, frame: frames[Self.index(now.timeIntervalSince(loopStart), fps) % frames.count], oneShot: false)
    }

    private static func index(_ elapsed: TimeInterval, _ fps: Int) -> Int {
        elapsed <= 0 ? 0 : Int(min(Double(Int.max / 2), (elapsed * Double(fps) + 1e-9).rounded(.down)))
    }
}
