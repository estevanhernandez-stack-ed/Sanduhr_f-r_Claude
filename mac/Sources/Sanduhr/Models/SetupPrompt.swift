import Foundation

/// The prompt Design it with an AI chat copies (setup spec, section 12). It is built only from constants and the Desk's own
/// vocabulary (the widget names, anchors, size and tilt ranges and meter styles `DeskDesign` reads, and the limits `PastedSetup`
/// reads), never from anything about this Mac or its user: no file name, no path, no usage number. It names no product.
/// The answer it asks for has the shape the Windows prompt asks for, so one answer reads on either platform.
enum SetupPrompt {
    /// What the prompt says each Desk piece is. A test fails when a piece the Desk knows has no note here.
    private static let pieceNotes: [String: String] = [
        "message": "a short line of text that changes through the day",
        "clock": "the time and date",
        "claude": "a compact line of AI usage pace",
        "meters": "the AI usage limits as meters",
        "nowPlaying": "the song that is playing",
        "telemetry": "processor, memory and disk meters",
        "weather": "the forecast",
        "watchers": "cards for long jobs, shown only while something is being watched",
        "meetings": "today's meetings",
        "companion": "a small character that shows only when a sprite is installed",
    ]

    /// The pieces the prompt lists: every piece the Mac Desk knows.
    static var pieces: [String] { DeskArrangement.known }

    /// What the prompt says a piece is; empty for a word with no note.
    static func note(for piece: String) -> String { pieceNotes[piece] ?? "" }

    /// The design fields the prompt teaches: every one is a field `DeskDesign.parse` reads.
    static var deskFields: [String] { DeskDesign.fields }

    static func build() -> String {
        func n(_ v: Double) -> String {
            var s = String(format: "%.2f", v)
            while s.hasSuffix("0") && !s.hasSuffix(".0") { s.removeLast() }
            return s
        }
        func deg(_ v: Double) -> String { String(format: "%.0f", v) }
        var lines: [String] = []
        func line(_ s: String = "") { lines.append(s) }

        line("You are helping me set up Sanduhr, a Mac app that puts a clock, a message, the weather and other")
        line("small pieces on my desktop, and can sort my desktop files into drawers.")
        line()
        line("Ask me a few short questions first, one at a time: the look I like, colors, which pieces I want and where,")
        line("how I keep my files today, and what kinds of files I would like sorted into drawers. Then answer with exactly one JSON block and nothing")
        line("after it. I will paste it back into Sanduhr, which checks it and shows me a preview before anything changes.")
        line()
        line("The block has two parts, both optional, and no other part:")
        line("{ \"desk\": { ... }, \"drawers\": { \"rules\": [ ... ], \"look\": ..., \"opacity\": ..., \"arrange\": { ... } } }")
        line()
        line("\"desk\" is a partial design: send only what should change. Use only these fields; any other field is refused.")
        line("- name: up to \(DeskDesign.nameMax) characters. note: one sentence for me, up to \(DeskDesign.noteMax).")
        line("- layout: { piece: { anchor, size, hidden, tilt, style } }, every member optional. Pieces:")
        for piece in pieces { line("  \(piece): \(note(for: piece)).") }
        line("  Each piece takes these members:")
        line("  anchor: " + DeskAnchor.allCases.map(\.rawValue).joined(separator: ", ")
             + " (t top, m middle, b bottom; l left, c center, r right). Pieces at one anchor stack top to bottom.")
        line("  size: a number from \(n(DeskArrangement.scaleRange.lowerBound)) to \(n(DeskArrangement.scaleRange.upperBound)), 1 is the piece's own size.")
        line("  hidden: true removes the piece; send nothing else for it.")
        line("  tilt: whole degrees from \(deg(DeskArrangement.tiltRange.lowerBound)) to \(deg(DeskArrangement.tiltRange.upperBound)), the slant of the whole stack at that anchor, so pieces at one anchor must share one tilt.")
        line("  style: " + DeskMeterStyle.allCases.map(DeskMeterStyles.word).joined(separator: ", ")
             + ", only on " + DeskMeterStyles.pieces.joined(separator: ", ") + ".")
        line("- ink: one #rrggbb color, or a list of two to four #rrggbb colors for a gradient.")
        line("  Sanduhr refuses a color that would be hard to read on my wallpaper.")
        line("- font: a font family name that comes with macOS, up to \(DeskDesign.fontFamilyMax) characters, letters, digits and spaces. Leave it out if unsure.")
        line("- plate: true or false, a soft panel behind each stack of pieces. shadow: true or false.")
        line("- weather: { nickname }, a name of up to \(DeskDesign.weatherNicknameMax) characters shown in place of the city's name.")
        line()
        line("\"drawers\" starts with rules, never file moves: you cannot see my files and you never name one. Each rule sends the")
        line("desktop files it matches to a drawer, a folder Sanduhr makes for me. A rule is { kind, drawer, text, days }:")
        line("- kind: " + PastedSetup.kindWords.joined(separator: ", ") + ".")
        line("- drawer: a short folder name of up to \(DrawerNames.maxLength) characters, a name and never a path.")
        line("- text: for \(PastedSetup.kindWord(.nameContains)) and \(PastedSetup.kindWord(.nameStartsWith)), part of a file name, up to \(PastedSetup.maxTextChars) characters.")
        line("- days: for \(PastedSetup.kindWord(.olderThan)), a whole number from \(DrawerRules.daysRange.lowerBound) to \(DrawerRules.daysRange.upperBound).")
        line("\(PastedSetup.minRules) to \(PastedSetup.maxRules) rules; leave rules out to keep my rules as they are. The first rule that matches a file wins, and a file no rule matches stays on the desktop.")
        line("\"drawers\" can also set how drawers look and line up. Send any of these, and leave out the rest:")
        line("- look: " + DrawerLook.allCases.map(\.rawValue).joined(separator: ", ") + " (solid is a plate, glass a lighter plate, outline a thin frame, bare no frame).")
        line("- opacity: a whole number from \(DrawerSettings.opacityRange.lowerBound) to \(DrawerSettings.opacityRange.upperBound), for solid and glass.")
        line("- arrange: { corner, direction } lines the drawers up from that corner. corner: " + PastedSetup.cornerWords.joined(separator: ", ")
             + " (t top, b bottom, l left, r right). direction: " + DrawerFlow.allCases.map(\.rawValue).joined(separator: ", ") + ". Send at least one of the two.")
        return lines.joined(separator: "\n")
    }
}
