import Foundation
import CoreGraphics

/// Per-line effects for the Desk message (item 54): tags at the start of a line's text, after any
/// `Mon:` or `10-31:` prefix, in any order.
///
///   {ink:#ff2a6d,#05d9e8}   this line's ink, 1 to 4 hex colors (two or more: a gradient)
///   {glow} / {noglow}       the soft glow on or off for this line (over Settings, Desk, Look)
///   {size:1.3}              0.5 to 2 times the Desk's message size
///   {write}                 the line draws itself in, left to right, once when it appears
///   {shimmer}               a slow light sweep across the line every few seconds
///   {sweep} / {sweep:20}    a three-character light across the line: once when it appears, or
///                           every 2 to 3600 seconds
///   {font:small-caps}       a letter style (item 65): bold, italic, bold-italic and small-caps
///                           in the line's own font, the others as Unicode letters (LetterStyle)
///
/// The Desk is lenient (`MessageMarkup.parse`): an unknown or malformed tag ends the tags, and it
/// and everything after it draw as plain text, so a typo never hides a line. A proposal from
/// Claude is strict (`parseStrict`): every tag must parse and text must follow. The MCP server
/// (`mac/integrations/sanduhr_mcp.py`, parse_effects) mirrors the strict grammar and its wording.
struct MessageEffects: Equatable {
    /// Hex colors without "#", 1 to 4; nil keeps Settings, Desk, Look's message color.
    var ink: [String]?
    /// nil keeps the global glow.
    var glow: Bool?
    /// A factor from 0.5 to 2; nil is 1.
    var size: Double?
    var write = false
    var shimmer = false
    /// `{font:…}`: the line's letter style; nil draws it as written.
    var font: LetterStyle?
    /// `{sweep}`: a three-character light runs across the line.
    var sweep = false
    /// `{sweep:<seconds>}`: and again every this many seconds; nil is once.
    var sweepPeriod: Double?
}

enum MessageMarkup {
    struct Parsed: Equatable {
        var effects = MessageEffects()
        /// What is drawn: the line without its tags.
        var text: String
    }

    /// Why a strict parse failed, worded as the MCP server words it.
    enum Failure: Error, Equatable {
        case unclosed
        case unknown(String)
        case takesNoValue(String)
        case inkMissing, inkTooMany, inkNotHex
        case size
        case font
        case sweep
        case noText

        var reason: String {
            switch self {
            case .unclosed: "an effect tag is not closed with }"
            case .unknown(let name): "unknown effect {\(name.prefix(20))}; known: \(MessageMarkup.effectNames)"
            case .takesNoValue(let name): "{\(name)} takes no value"
            case .inkMissing: "{ink:...} needs 1 to 4 hex colors, like {ink:#ff2a6d,#05d9e8}"
            case .inkTooMany: "{ink:...} takes at most 4 colors"
            case .inkNotHex: "{ink:...} has a color that is not hex (#rgb or #rrggbb)"
            case .size: "{size:...} needs a number from 0.5 to 2, like {size:1.3}"
            case .font: "{font:...} needs a letter style: \(LetterStyle.tagNames)"
            case .sweep: "{sweep:...} takes a period in seconds from 2 to 3600, like {sweep:20}"
            case .noText: "the line has effects but no text"
            }
        }
    }

    static let effectNames = "ink, glow, noglow, size, write, shimmer, sweep, font"
    static let maxInkColors = 4
    static let sizeRange: ClosedRange<Double> = 0.5...2
    static let sweepPeriodRange: ClosedRange<Double> = 2...3600

    /// Every tag must parse and text must follow.
    static func parseStrict(_ body: String) -> Result<Parsed, Failure> {
        let (parsed, failure) = scan(body)
        if let failure { return .failure(failure) }
        return .success(parsed)
    }

    /// For drawing: good tags apply; from the first bad one on, the rest is text. A line that is
    /// nothing but tags draws as written, never as nothing.
    static func parse(_ body: String) -> Parsed {
        let (parsed, failure) = scan(body)
        if failure == .noText {
            return Parsed(text: body.trimmingCharacters(in: .whitespaces))
        }
        return parsed
    }

    /// Reads tags off the front of `body`. On a failure the returned text starts at the bad tag.
    private static func scan(_ body: String) -> (Parsed, Failure?) {
        var effects = MessageEffects()
        var rest = Substring(body).drop { $0 == " " || $0 == "\t" }
        while rest.first == "{" {
            guard let close = rest.firstIndex(of: "}") else {
                return (Parsed(effects: effects, text: String(rest)), .unclosed)
            }
            let inside = rest[rest.index(after: rest.startIndex)..<close]
            if let failure = apply(String(inside), to: &effects) {
                return (Parsed(effects: effects, text: String(rest)), failure)
            }
            rest = rest[rest.index(after: close)...].drop { $0 == " " || $0 == "\t" }
        }
        let text = String(rest).trimmingCharacters(in: .whitespaces)
        return (Parsed(effects: effects, text: text), text.isEmpty ? .noText : nil)
    }

    private static func apply(_ tag: String, to effects: inout MessageEffects) -> Failure? {
        let parts = tag.split(separator: ":", maxSplits: 1, omittingEmptySubsequences: false)
        let name = parts[0].trimmingCharacters(in: .whitespaces).lowercased()
        let hasValue = parts.count > 1
        let value = hasValue ? parts[1].trimmingCharacters(in: .whitespaces) : ""
        switch name {
        case "glow", "noglow", "write", "shimmer":
            if hasValue { return .takesNoValue(name) }
            switch name {
            case "glow": effects.glow = true
            case "noglow": effects.glow = false
            case "write": effects.write = true
            default: effects.shimmer = true
            }
            return nil
        case "ink":
            let colors = hasValue ? value.split(separator: ",", omittingEmptySubsequences: false)
                .map { $0.trimmingCharacters(in: .whitespaces) } : []
            if colors.isEmpty || colors.contains(where: \.isEmpty) { return .inkMissing }
            if colors.count > maxInkColors { return .inkTooMany }
            guard colors.allSatisfy(isHex) else { return .inkNotHex }
            effects.ink = colors.map { $0.hasPrefix("#") ? String($0.dropFirst()) : $0 }
            return nil
        case "size":
            guard hasValue, value.range(of: #"^\d+(\.\d+)?$"#, options: .regularExpression) != nil,
                  let factor = Double(value), sizeRange.contains(factor) else { return .size }
            effects.size = factor
            return nil
        case "font":
            guard hasValue, let style = LetterStyle(tag: value) else { return .font }
            effects.font = style
            return nil
        case "sweep":
            if hasValue {
                guard value.range(of: #"^\d+(\.\d+)?$"#, options: .regularExpression) != nil,
                      let period = Double(value), sweepPeriodRange.contains(period) else { return .sweep }
                effects.sweepPeriod = period
            } else {
                effects.sweepPeriod = nil
            }
            effects.sweep = true
            return nil
        default:
            return .unknown(name)
        }
    }

    /// "#rgb", "#rrggbb", with or without the "#".
    static func isHex(_ s: String) -> Bool {
        s.range(of: #"^#?([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$"#, options: .regularExpression) != nil
    }
}

/// What the effects mean on screen, decided apart from the drawing so each rule is tested.
enum MessageMotion {
    /// How long `{write}` takes to draw a line in.
    static let writeDuration: TimeInterval = 1.5
    /// A `{shimmer}` sweep every this many seconds while the line shows...
    static let shimmerPeriod: TimeInterval = 8
    /// ...taking this long to cross it; the rest of the period nothing moves.
    static let shimmerSweep: TimeInterval = 1.6
    /// The sweep's band, as a share of the line's width.
    static let shimmerBand: CGFloat = 0.3

    /// The ink spec for a line: its own colors, else the global message color.
    static func inkSpec(_ e: MessageEffects, global: String) -> String {
        e.ink.map { $0.joined(separator: ",") } ?? global
    }

    static func glows(_ e: MessageEffects, global: Bool) -> Bool { e.glow ?? global }

    static func size(_ e: MessageEffects, base: CGFloat) -> CGFloat { base * CGFloat(e.size ?? 1) }

    /// `{write}` animates; with Reduce Motion the line is simply there.
    static func animatesWrite(_ e: MessageEffects, reduceMotion: Bool) -> Bool { e.write && !reduceMotion }

    /// `{shimmer}` runs only while it can be seen: not with Reduce Motion, not while the Desk is
    /// covered, the screens sleep or the session is switched away (`paused`).
    static func shimmers(_ e: MessageEffects, reduceMotion: Bool, paused: Bool) -> Bool {
        e.shimmer && !reduceMotion && !paused
    }

    // MARK: {sweep}, the owner's now-playing mod's light (glow.ts): three characters wide,
    // brightening toward white, about a second to cross.

    /// How long one `{sweep}` takes to cross the line.
    static let sweepRun: TimeInterval = 1.1
    /// How many characters the light spans on each side of its center.
    static let sweepWidth: Double = 3
    /// How far toward white the center goes (0 to 1).
    static let sweepStrength: Double = 0.75
    /// Frames while the light moves (none between sweeps).
    static let sweepFrame: TimeInterval = 1.0 / 30
    /// The first sweep, after the line appears (or the Desk comes back into sight).
    static let sweepAppearDelay: TimeInterval = 0.6
    /// Settings' Replay sweeps sooner.
    static let sweepReplayDelay: TimeInterval = 0.3

    /// Where the light's center sits `fraction` (0 to 1) of the way through a sweep of `length`
    /// characters, or nil outside the sweep. It starts `sweepWidth` before the first character and
    /// ends as far past the last, so it enters and leaves softly.
    static func sweepAt(fraction: Double, length: Int) -> Double? {
        guard fraction >= 0, fraction < 1, length > 0 else { return nil }
        return fraction * (Double(length) + sweepWidth * 2) - sweepWidth
    }

    /// How far character `index` blends toward white with the light at `at`: `sweepStrength` at
    /// the center, falling to 0 at `sweepWidth` characters away.
    static func sweepLight(index: Int, at: Double?) -> Double {
        guard let at else { return 0 }
        let k = 1 - abs(Double(index) - at) / sweepWidth
        return k > 0 ? k * sweepStrength : 0
    }

    /// When `{sweep}` runs: the wait before the first sweep and, for `{sweep:<seconds>}`, the time
    /// from one sweep's start to the next; nil when nothing moves: no `{sweep}`, Reduce Motion,
    /// out of sight (`paused`, as `{shimmer}` rests), or a once-only sweep that has already run
    /// for this line. Coming back into sight starts a periodic one again, and a once-only one that
    /// had not run yet.
    static func sweepPlan(_ e: MessageEffects, reduceMotion: Bool, paused: Bool, sweptOnce: Bool,
                          replay: Bool) -> (first: TimeInterval, every: TimeInterval?)? {
        guard e.sweep, !reduceMotion, !paused else { return nil }
        if e.sweepPeriod == nil && sweptOnce { return nil }
        let every = e.sweepPeriod.map { max($0, sweepRun) }
        return (replay ? sweepReplayDelay : sweepAppearDelay, every)
    }

    /// The Desk is out of sight: its window fully covered, the screens asleep, the screen saver
    /// on, or this user's session switched away.
    static func paused(deskVisible: Bool, screensAsleep: Bool, screenSaver: Bool, sessionAway: Bool) -> Bool {
        !deskVisible || screensAsleep || screenSaver || sessionAway
    }
}

/// The message's look settings that the effects override (Settings, Desk, Look).
enum DeskMessageLook {
    /// The soft glow around the message, on by default; `{glow}` / `{noglow}` override it per line.
    static let glowKey = "messageGlow"
}
