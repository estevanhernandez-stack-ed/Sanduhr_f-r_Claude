import Foundation

/// The Unicode letter styles as characters (item 63b's statusline styles, item 65's `{font:…}`
/// on the Desk for the styles a font has no face for): the Mathematical Alphanumeric Symbols
/// block, its holes filled from Letterlike Symbols (the letters Unicode had already encoded there,
/// such as script B or double-struck R), and small caps from the phonetic letters.
///
/// A port of `sanduhr_statusline.py`'s FONTS and SMALL_CAPS: `test_sanduhr_statusline.py` reads
/// the tables below from this file and checks them against the Python ones, so keep each table on
/// one `Table(…)` line in this shape.
enum LetterMap {
    struct Table: Equatable, Sendable {
        /// Capital A in the style.
        let upper: UInt32
        /// Small a in the style.
        let lower: UInt32
        /// Digit 0 in the style; nil where the block has no digits (italic, bold italic, script,
        /// fraktur): digits stay as they are.
        let digit: UInt32?
        /// Letters the block leaves out, with where Letterlike Symbols has them.
        let holes: [Character: UInt32]
    }

    static let tables: [LetterStyle: Table] = [
        .bold: Table(upper: 0x1D400, lower: 0x1D41A, digit: 0x1D7CE, holes: [:]),
        .italic: Table(upper: 0x1D434, lower: 0x1D44E, digit: nil, holes: ["h": 0x210E]),
        .boldItalic: Table(upper: 0x1D468, lower: 0x1D482, digit: nil, holes: [:]),
        .script: Table(upper: 0x1D49C, lower: 0x1D4B6, digit: nil, holes: ["B": 0x212C, "E": 0x2130, "F": 0x2131, "H": 0x210B, "I": 0x2110, "L": 0x2112, "M": 0x2133, "R": 0x211B, "e": 0x212F, "g": 0x210A, "o": 0x2134]),
        .fraktur: Table(upper: 0x1D504, lower: 0x1D51E, digit: nil, holes: ["C": 0x212D, "H": 0x210C, "I": 0x2111, "R": 0x211C, "Z": 0x2128]),
        .doubleStruck: Table(upper: 0x1D538, lower: 0x1D552, digit: 0x1D7D8, holes: ["C": 0x2102, "H": 0x210D, "N": 0x2115, "P": 0x2119, "Q": 0x211A, "R": 0x211D, "Z": 0x2124]),
        .sans: Table(upper: 0x1D5A0, lower: 0x1D5BA, digit: 0x1D7E2, holes: [:]),
        .mono: Table(upper: 0x1D670, lower: 0x1D68A, digit: 0x1D7F6, holes: [:]),
    ]

    /// a to z in small caps (x has no small capital and stays x).
    static let smallCaps = "ᴀʙᴄᴅᴇꜰɢʜɪᴊᴋʟᴍɴᴏᴘꞯʀꜱᴛᴜᴠᴡxʏᴢ"
    private static let smallCapsLetters = Array(smallCaps)

    /// `ch` in `style`; anything the style has no form for (accents, punctuation, other scripts,
    /// capitals in small caps) stays as it is.
    static func letter(_ ch: Character, _ style: LetterStyle) -> Character {
        guard let ascii = ch.asciiValue else { return ch }
        let isUpper = (65...90).contains(ascii), isLower = (97...122).contains(ascii)
        let isDigit = (48...57).contains(ascii)
        guard isUpper || isLower || isDigit else { return ch }
        if style == .smallCaps {
            guard isLower else { return ch }
            return smallCapsLetters[Int(ascii) - 97]
        }
        guard let table = tables[style] else { return ch }
        if let hole = table.holes[ch] { return scalar(hole) ?? ch }
        if isUpper { return scalar(table.upper + UInt32(ascii) - 65) ?? ch }
        if isLower { return scalar(table.lower + UInt32(ascii) - 97) ?? ch }
        return table.digit.flatMap { scalar($0 + UInt32(ascii) - 48) } ?? ch
    }

    /// `text` in `style`; nil keeps it as written.
    static func map(_ text: String, _ style: LetterStyle?) -> String {
        guard let style else { return text }
        return String(text.map { letter($0, style) })
    }

    private static func scalar(_ value: UInt32) -> Character? {
        Unicode.Scalar(value).map(Character.init)
    }
}

extension LetterStyle {
    /// A `{font:…}` tag's style: the statusline's names, ignoring case, spaces, hyphens and
    /// underscores, so `smallcaps`, `Small Caps` and `small_caps` are all small caps.
    init?(tag: String) {
        let key = Self.tagKey(tag)
        guard let style = Self.allCases.first(where: { Self.tagKey($0.rawValue) == key }) else { return nil }
        self = style
    }

    /// The names a `{font:…}` tag takes, as the MCP server lists them.
    static let tagNames = allCases.map(\.rawValue).joined(separator: ", ")

    private static func tagKey(_ s: String) -> String {
        s.lowercased().filter { $0 != " " && $0 != "-" && $0 != "_" }
    }
}
