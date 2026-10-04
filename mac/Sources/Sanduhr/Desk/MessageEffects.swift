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
            case .noText: "the line has effects but no text"
            }
        }
    }

    static let effectNames = "ink, glow, noglow, size, write, shimmer"
    static let maxInkColors = 4
    static let sizeRange: ClosedRange<Double> = 0.5...2

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
