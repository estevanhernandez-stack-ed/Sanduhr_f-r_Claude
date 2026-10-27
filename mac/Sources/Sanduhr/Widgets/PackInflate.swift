import Foundation

/// How many bytes a zlib stream inflates to, for the PNG check. A small inflater in the shape of
/// zlib's `inflate` (RFC 1950 and 1951), written so it behaves like Python's
/// `zlib.decompressobj().decompress(data, limit)`, which the reference validator calls:
/// a truncated stream is not an error (the bytes decoded so far are the answer), a corrupt one is
/// an error worded as zlib words it, the Adler-32 is checked only when the stream ends inside the
/// limit, and nothing is produced past the limit. The Compression framework's zlib would do the
/// same job, but it hands back no count for a truncated stream and no reason for a corrupt one,
/// and the messages have to match the reference.
enum PackInflate {
    /// zlib's reason for refusing a stream, in Python's `zlib.error` wording.
    struct Corrupt: Error {
        let message: String
        init(_ detail: String) { message = "Error -3 while decompressing data: " + detail }
        init(raw: String) { message = raw }
    }

    private struct Truncated: Error {}
    private struct LimitReached: Error {}

    /// The inflated length of `zlib`, reading at most `limit` bytes of output.
    static func inflatedLength(_ zlib: [UInt8], limit: Int) throws -> Int {
        var inflater = Inflater(input: zlib, limit: limit)
        do {
            try inflater.run()
        } catch is Truncated {
        } catch is LimitReached {
        }
        return inflater.produced
    }

    // MARK: - Tables

    private static let lengthBase = [3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31, 35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258]
    private static let lengthExtra = [0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0]
    private static let distBase = [1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193, 257, 385, 513, 769, 1025, 1537,
                                   2049, 3073, 4097, 6145, 8193, 12289, 16385, 24577]
    private static let distExtra = [0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13]
    /// CPython's output buffer: the first block, then the growth sizes (`_BlocksOutputBuffer`).
    /// The fast loop's entry and exit thresholds, fitted against CPython 3.14 on macOS (libz 1.2.12):
    /// input bytes left to enter, input bytes it keeps back, output room it needs. Roughly 98.5
    /// percent of 5,000 forced bad-distance streams agree; the rest sit at the edges.
    private static let fastMinInput = 24
    private static let fastMargin = 20
    private static let fastMinOutput = 300
    private static let firstBlock = 16 * 1024
    private static let blockSizes = [32 << 10, 64 << 10, 256 << 10, 1 << 20, 4 << 20, 8 << 20, 16 << 20, 16 << 20,
                                     32 << 20, 32 << 20, 32 << 20, 32 << 20, 64 << 20, 64 << 20, 64 << 20, 64 << 20,
                                     128 << 20, 128 << 20, 128 << 20, 128 << 20, 256 << 20]
    private static let codeLengthOrder = [16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15]

    /// A canonical Huffman code: how many codes have each length, and the symbols in code order.
    private struct Huffman {
        var counts = [Int](repeating: 0, count: 16)
        var symbols: [Int] = []
        /// The longest code length in use (0 for an empty code).
        var longest = 0
        /// Unused code space: negative when over-subscribed, positive when incomplete.
        var left = 0

        init(lengths: [Int]) {
            for l in lengths { counts[l] += 1 }
            symbols = [Int](repeating: 0, count: lengths.count)
            var offsets = [Int](repeating: 0, count: 16)
            for l in 1..<15 { offsets[l + 1] = offsets[l] + counts[l] }
            for (sym, l) in lengths.enumerated() where l != 0 {
                symbols[offsets[l]] = sym
                offsets[l] += 1
            }
            longest = (1...15).last(where: { counts[$0] > 0 }) ?? 0
            var avail = 1
            for l in 1...15 {
                avail <<= 1
                avail -= counts[l]
                if avail < 0 { left = avail; return }
            }
            left = avail
        }
    }

    // MARK: - The inflater

    private struct Inflater {
        let input: [UInt8]
        let limit: Int
        var pos = 0
        var bitBuffer = 0
        var bitCount = 0
        var produced = 0
        var window = [UInt8](repeating: 0, count: 32768)
        var s1: UInt32 = 1
        var s2: UInt32 = 0
        var blockEnd: Int
        var blockIndex = 0

        init(input: [UInt8], limit: Int) {
            self.input = input
            self.limit = limit
            blockEnd = min(limit, PackInflate.firstBlock)
        }

        mutating func bits(_ n: Int) throws -> Int {
            while bitCount < n {
                guard pos < input.count else { throw Truncated() }
                bitBuffer |= Int(input[pos]) << bitCount
                pos += 1
                bitCount += 8
            }
            let v = bitBuffer & ((1 << n) - 1)
            bitBuffer >>= n
            bitCount -= n
            return v
        }

        mutating func emit(_ b: UInt8) throws {
            if produced >= limit { throw LimitReached() }
            window[produced & 32767] = b
            produced += 1
            s1 = (s1 + UInt32(b)) % 65521
            s2 = (s2 + s1) % 65521
        }

        mutating func run() throws {
            guard input.count >= 2 else { return }
            let cmf = Int(input[0]), flg = Int(input[1])
            if ((cmf << 8) + flg) % 31 != 0 { throw Corrupt("incorrect header check") }
            if cmf & 0x0F != 8 { throw Corrupt("unknown compression method") }
            if (cmf >> 4) + 8 > 15 { throw Corrupt("invalid window size") }
            if flg & 0x20 != 0 { throw Corrupt(raw: "Error 2 while decompressing data") }
            pos = 2

            var last = false
            while !last {
                last = try bits(1) == 1
                switch try bits(2) {
                case 0: try stored()
                case 1: try fixed()
                case 2: try dynamic()
                default: throw Corrupt("invalid block type")
                }
            }
            // The Adler-32 of the output, big-endian, after the last block's byte boundary.
            bitBuffer = 0
            bitCount = 0
            guard pos + 4 <= input.count else { return }
            let stored = (UInt32(input[pos]) << 24) | (UInt32(input[pos + 1]) << 16) | (UInt32(input[pos + 2]) << 8) | UInt32(input[pos + 3])
            if stored != (s2 << 16 | s1) { throw Corrupt("incorrect data check") }
        }

        mutating func stored() throws {
            bitBuffer = 0
            bitCount = 0
            let len = try bits(16)
            let nlen = try bits(16)
            if len != (~nlen & 0xFFFF) { throw Corrupt("invalid stored block lengths") }
            for _ in 0..<len { try emit(UInt8(try bits(8))) }
        }

        mutating func fixed() throws {
            var lengths = [Int](repeating: 8, count: 288)
            for i in 144..<256 { lengths[i] = 9 }
            for i in 256..<280 { lengths[i] = 7 }
            let lit = Huffman(lengths: lengths)
            let dist = Huffman(lengths: [Int](repeating: 5, count: 32))
            try codes(lit, dist)
        }

        mutating func dynamic() throws {
            let nlen = try bits(5) + 257
            let ndist = try bits(5) + 1
            let ncode = try bits(4) + 4
            if nlen > 286 || ndist > 30 { throw Corrupt("too many length or distance symbols") }
            var lengths = [Int](repeating: 0, count: 19)
            for i in 0..<ncode { lengths[PackInflate.codeLengthOrder[i]] = try bits(3) }
            let codeLengths = Huffman(lengths: lengths)
            // The code-length code must be complete (zlib's CODES rule).
            if codeLengths.left != 0 && codeLengths.longest != 0 { throw Corrupt("invalid code lengths set") }

            var all = [Int](repeating: 0, count: nlen + ndist)
            var have = 0
            while have < nlen + ndist {
                var sym = 0
                if codeLengths.longest == 0 {
                    _ = try bits(1)   // zlib reads an empty code table as one-bit zeros
                } else if let s = try decode(codeLengths) {
                    sym = s
                } else {
                    throw Corrupt("invalid code lengths set")
                }
                if sym < 16 {
                    all[have] = sym
                    have += 1
                    continue
                }
                var value = 0
                var repeatCount: Int
                switch sym {
                case 16:
                    if have == 0 { throw Corrupt("invalid bit length repeat") }
                    value = all[have - 1]
                    repeatCount = 3 + (try bits(2))
                case 17: repeatCount = 3 + (try bits(3))
                default: repeatCount = 11 + (try bits(7))
                }
                if have + repeatCount > nlen + ndist { throw Corrupt("invalid bit length repeat") }
                for _ in 0..<repeatCount { all[have] = value; have += 1 }
            }
            if all[256] == 0 { throw Corrupt("invalid code -- missing end-of-block") }
            let lit = Huffman(lengths: Array(all[0..<nlen]))
            if lit.left < 0 || (lit.left > 0 && lit.longest != 1) { throw Corrupt("invalid literal/lengths set") }
            let dist = Huffman(lengths: Array(all[nlen...]))
            // No distance codes at all is a valid table (inflate_table's max == 0 case); any distance then fails to decode.
            if dist.left < 0 || (dist.left > 0 && dist.longest > 1) { throw Corrupt("invalid distances set") }
            try codes(lit, dist)
        }

        /// The next symbol, or nil when the bits name no code.
        mutating func decode(_ h: Huffman) throws -> Int? {
            var code = 0, first = 0, index = 0
            for len in 1...15 {
                code |= try bits(1)
                let count = h.counts[len]
                if code - count < first { return h.symbols[index + (code - first)] }
                index += count
                first += count
                first <<= 1
                code <<= 1
            }
            return nil
        }

        /// Output room in the buffer block Python is filling. `decompress(data, limit)` hands zlib one
        /// block at a time (16 KiB first, then the sizes below, the last one clamped to the limit),
        /// so zlib's `avail_out` is the distance to the block's end, not to the limit.
        mutating func outputLeft() -> Int {
            while produced >= blockEnd && blockEnd < limit {
                blockEnd = min(limit, blockEnd + PackInflate.blockSizes[min(blockIndex, PackInflate.blockSizes.count - 1)])
                blockIndex += 1
            }
            return blockEnd - produced
        }

        /// Bits read from the stream so far.
        var consumed: Int { 8 * pos - bitCount }

        mutating func codes(_ lit: Huffman, _ dist: Huffman) throws {
            // The libz Python links on macOS words a bad code differently while it runs its fast
            // loop (`inflate_fast`): "invalid literal/length/distance code" for both kinds, where
            // stock zlib says "invalid literal/length code" or "invalid distance code". That loop
            // needs plenty of input and output room and refills its bit buffer 8 bytes at a time,
            // so it stops sooner than stock zlib's. `fast` follows it with constants fitted to
            // CPython's behaviour (a differential fuzz, see `PackInflate.fast*`); `pulled` is how
            // many input bytes the loop has taken into its bit buffer.
            var fast = false
            var pulled = 0
            let fastLast = input.count - PackInflate.fastMargin
            while true {
                if !fast, input.count - pos >= PackInflate.fastMinInput, outputLeft() >= PackInflate.fastMinOutput {
                    fast = true
                    pulled = pos
                }
                if fast {
                    let held = 8 * pulled - consumed
                    if held < 57 { pulled += (63 - held) >> 3 }
                }
                let badCode = fast ? "invalid literal/length/distance code" : nil
                guard let sym = try decode(lit) else { throw Corrupt(badCode ?? "invalid literal/length code") }
                if sym < 256 {
                    try emit(UInt8(sym))
                } else if sym == 256 {
                    return
                } else {
                    let li = sym - 257
                    if li >= 29 { throw Corrupt(badCode ?? "invalid literal/length code") }
                    let length = PackInflate.lengthBase[li] + (try bits(PackInflate.lengthExtra[li]))
                    guard let ds = try decode(dist), ds < 30 else { throw Corrupt(badCode ?? "invalid distance code") }
                    let distance = PackInflate.distBase[ds] + (try bits(PackInflate.distExtra[ds]))
                    // zlib's MATCH state leaves when the output is full before it checks the distance.
                    if produced >= limit { throw LimitReached() }
                    if distance > produced { throw Corrupt("invalid distance too far back") }
                    for _ in 0..<length { try emit(window[(produced - distance) & 32767]) }
                }
                if fast, !(pulled < fastLast && outputLeft() >= PackInflate.fastMinOutput) { fast = false }
            }
        }
    }
}
