import Compression
import Foundation

/// Reads a widget pack zip into the in-memory map `PackValidator` takes (relative POSIX path to
/// bytes), or refuses it. FORMAT 7: release zips hold the pack's files at the root, stored without
/// compression, with no directory entries. So this reader never inflates anything: it walks the
/// central directory itself, checks every entry's declared sizes against FORMAT 3's limits before
/// reading a byte of data, then slices each stored entry out and checks it again (the local header
/// agrees with the central one, length, CRC-32). A port of Windows' WidgetZip.cs.
///
/// Refused: compressed or encrypted entries, data descriptors, ZIP64, multi-disk archives,
/// directory entries, symlinks or any non-regular file, names that are empty, absolute, use a
/// backslash or a drive colon, have an empty, `.` or `..` segment or a non-ASCII or control
/// character, two names that differ only in case, more than 67 entries (64 listed files plus
/// `pack.json`, `LICENSE` and the preview), an entry over 5 MB (a font) or a total over 10 MB, and
/// entries whose data overlap. The validator then applies the precise per-kind rules.
///
/// A zip the user picks (`fromUser`, Install widget from file, #459) may also be what Finder's Compress, Explorer's
/// Send to, Compressed folder and zip tools write: deflated entries (inflated here, never past the declared size,
/// which the 5 MB and 10 MB limits bound before any inflating, then CRC-checked), data descriptors, folder entries,
/// `__MACOSX/` and `.DS_Store` (skipped), and the pack's files under one shared top folder (that folder is dropped).
/// The catalog's own zips stay on the strict path: they are signed artifacts in FORMAT 7's form. A port of #469.
///
/// Pure: bytes in, map out. The input is untrusted, so every read is bounds-checked and a short
/// or lying zip comes back as a refusal, never a trap.
enum WidgetZip {
    /// 64 files in `files` plus pack.json, LICENSE and the preview.
    static let maxEntries = PackValidator.maxFiles + 3
    /// The largest single file FORMAT 3 allows (a font, 5 MB; images and pixel sheets are 2 MB).
    static let maxEntryBytes = PackValidator.maxFontBytes
    static let maxTotalBytes = PackValidator.maxPackBytes
    /// The largest zip a pack can be: the catalog's limit on a pack's `size`.
    static let maxZipBytes = WidgetCatalog.maxZipBytes

    private static let eocdSig = 0x06054b50
    private static let centralSig = 0x02014b50
    private static let localSig = 0x04034b50
    private static let zip64LocatorSig = 0x07064b50
    private static let eocdSize = 22
    private static let centralSize = 46
    private static let localSize = 30
    private static let utf8NameFlag = 0x0800
    private static let descriptorFlag = 0x0008
    /// Bits 1 and 2: the compression level deflate recorded. Meaningless to a reader.
    private static let deflateOptionFlags = 0x0006
    private static let deflate = 8
    /// Raw entries a user's zip may list: Finder adds a `__MACOSX/` twin per file and a folder entry per folder, so
    /// the kept files (still at most `maxEntries`) can come with as many again of entries that are skipped.
    static let maxUserRawEntries = maxEntries * 4

    private struct Entry {
        let rawName: String
        var name: String
        let crc: Int
        let size: Int
        let compressedSize: Int
        let method: Int
        let localOffset: Int
    }

    /// A refusal, with the reason the caller shows.
    private struct Refusal: Error {
        let message: String
    }

    /// The pack's files, or the reason. Never traps on bad input. `fromUser`: a zip the user picked from disk, which may
    /// also be what ordinary zip tools write (see the type's comment); false, the catalog path, keeps FORMAT 7's form.
    static func read(_ zip: Data, fromUser: Bool = false) -> Result<[String: Data], WidgetZipError> {
        do {
            return .success(try readCore([UInt8](zip), fromUser: fromUser))
        } catch let r as Refusal {
            return .failure(WidgetZipError(message: r.message))
        } catch {
            return .failure(WidgetZipError(message: "the zip is malformed"))
        }
    }

    /// An entry a user's zip may carry that is not a pack file: folders, Finder's resource forks and its folder view file.
    private static func skippable(_ name: String) -> Bool {
        name.hasSuffix("/") || name.hasPrefix("__MACOSX/") || name == ".DS_Store" || name.hasSuffix("/.DS_Store")
    }

    private static func readCore(_ zip: [UInt8], fromUser: Bool) throws -> [String: Data] {
        guard zip.count >= eocdSize else { throw Refusal(message: "the file is not a zip") }
        guard zip.count <= maxZipBytes else { throw Refusal(message: "the zip is larger than a pack may be") }

        // End of central directory: the last one, whose comment runs exactly to the end of the file.
        var eocd = -1
        var at = zip.count - eocdSize
        let lowest = zip.count - eocdSize - 0xFFFF
        while at >= 0 && at >= lowest {
            if try u32(zip, at) == eocdSig, try at + eocdSize + u16(zip, at + 20) == zip.count {
                eocd = at
                break
            }
            at -= 1
        }
        guard eocd >= 0 else { throw Refusal(message: "the zip has no end-of-directory record") }
        if eocd >= 20, try u32(zip, eocd - 20) == zip64LocatorSig {
            throw Refusal(message: "ZIP64 archives are not packs")
        }
        let disk = try u16(zip, eocd + 4), cdDisk = try u16(zip, eocd + 6)
        let onDisk = try u16(zip, eocd + 8), total = try u16(zip, eocd + 10)
        let cdSize = try u32(zip, eocd + 12), cdOffset = try u32(zip, eocd + 16)
        if disk != 0 || cdDisk != 0 || onDisk != total {
            throw Refusal(message: "multi-disk zips are not packs")
        }
        if total == 0xFFFF || cdSize == 0xFFFF_FFFF || cdOffset == 0xFFFF_FFFF {
            throw Refusal(message: "ZIP64 archives are not packs")
        }
        if total > (fromUser ? maxUserRawEntries : maxEntries) {
            throw Refusal(message: "the zip has \(total) entries, a pack has at most \(maxEntries) files")
        }
        if cdOffset + cdSize != eocd {
            throw Refusal(message: "the zip's directory is not where its end record says")
        }

        // Central directory: every check that needs no data, before any data is read.
        var entries: [Entry] = []
        let allowedFlags = utf8NameFlag | (fromUser ? descriptorFlag | deflateOptionFlags : 0)
        var sum = 0
        var p = cdOffset
        for i in 0..<total {
            if p + centralSize > eocd { throw Refusal(message: "the zip's directory is malformed") }
            if try u32(zip, p) != centralSig {
                throw Refusal(message: "the zip's directory is malformed")
            }
            let madeBy = try u16(zip, p + 4)
            let flags = try u16(zip, p + 8), method = try u16(zip, p + 10)
            let crc = try u32(zip, p + 16), csize = try u32(zip, p + 20), usize = try u32(zip, p + 24)
            let nameLen = try u16(zip, p + 28), extraLen = try u16(zip, p + 30), commentLen = try u16(zip, p + 32)
            let startDisk = try u16(zip, p + 34)
            let external = try u32(zip, p + 38), local = try u32(zip, p + 42)
            let next = p + centralSize + nameLen + extraLen + commentLen
            if next > eocd { throw Refusal(message: "the zip's directory is malformed") }

            // A user's zip is named for the folder they compressed, and folders have spaces ("My Companion"); a space
            // may appear in a raw name there. The folder is dropped below, and every kept name still meets the
            // validator's path rule, which allows no space.
            guard let name = name(zip, p + centralSize, nameLen, allowSpace: fromUser) else {
                throw Refusal(message: "entry \(i) has a name that is not plain ASCII")
            }
            if fromUser && skippable(name) {
                p = next   // a folder, a resource fork or a folder view file: not part of the pack, never read
                continue
            }
            if name.hasSuffix("/") {
                throw Refusal(message: "\(name): directory entries are not allowed in a pack zip")
            }
            if let why = unsafeName(name) { throw Refusal(message: "\(name): \(why)") }
            if flags & 0x0001 != 0 {
                throw Refusal(message: "\(name): encrypted entries are not allowed")
            }
            if flags & ~allowedFlags != 0 {
                throw Refusal(message: "\(name): encrypted entries and data descriptors are not allowed")
            }
            if method != 0 && !(fromUser && method == deflate) {
                throw Refusal(message: fromUser
                    ? "\(name): the entry is compressed in a way Sanduhr can't read (only deflate)"
                    : "\(name): compressed entries are not allowed (pack zips are stored)")
            }
            if method == 0 && csize != usize { throw Refusal(message: "\(name): a stored entry's sizes must agree") }
            if startDisk != 0 { throw Refusal(message: "\(name): multi-disk zips are not packs") }
            if !isRegularFile(madeBy: madeBy, external: external) {
                throw Refusal(message: "\(name): only regular files are allowed (no links or folders)")
            }
            if usize > maxEntryBytes {
                throw Refusal(message: "\(name): \(usize) bytes is over the 5 MB a pack file may be")
            }
            sum += usize
            if sum > maxTotalBytes {
                throw Refusal(message: "the zip's files add up to more than the 10 MB a pack may be")
            }
            entries.append(Entry(rawName: name, name: name, crc: crc, size: usize, compressedSize: csize,
                                 method: method, localOffset: local))
            p = next
        }
        if p != eocd { throw Refusal(message: "the zip's directory is malformed") }
        if entries.count > maxEntries {
            throw Refusal(message: "the zip has \(entries.count) files, a pack has at most \(maxEntries)")
        }

        // A folder compressed whole (Finder's Compress; Explorer's Send to, Compressed folder) puts every file under it.
        if fromUser, let first = entries.first, let slash = first.name.firstIndex(of: "/"), slash > first.name.startIndex {
            let top = String(first.name[...slash])
            if entries.allSatisfy({ $0.name.hasPrefix(top) && $0.name.count > top.count }) {
                for i in entries.indices { entries[i].name = String(entries[i].name.dropFirst(top.count)) }
            }
        }
        var seen = Set<String>()
        for e in entries where !seen.insert(e.name.lowercased()).inserted {
            throw Refusal(message: "\(e.name): the zip has this name twice (names may not differ only in case)")
        }

        // The data: each local header agrees with its central entry, no two entries overlap, CRCs match.
        var files: [String: Data] = [:]
        var previousEnd = 0
        let ordered = entries.enumerated().sorted {
            $0.element.localOffset != $1.element.localOffset
                ? $0.element.localOffset < $1.element.localOffset : $0.offset < $1.offset
        }
        for (_, e) in ordered {
            let a = e.localOffset
            if a < previousEnd || a + localSize > cdOffset {
                throw Refusal(message: "\(e.name): the entry's header is missing or overlaps another")
            }
            if try u32(zip, a) != localSig {
                throw Refusal(message: "\(e.name): the entry's header is missing or overlaps another")
            }
            let flags = try u16(zip, a + 6), method = try u16(zip, a + 8)
            let crc = try u32(zip, a + 14), csize = try u32(zip, a + 18), usize = try u32(zip, a + 22)
            let nameLen = try u16(zip, a + 26), extraLen = try u16(zip, a + 28)
            let localName = name(zip, a + localSize, nameLen, allowSpace: fromUser)
            // With a data descriptor the local header's CRC and sizes may be zero: the central directory's, checked
            // above, are the ones that count, and the data is checked against them below.
            let descriptor = flags & descriptorFlag != 0
            if localName != e.rawName || flags & ~allowedFlags != 0 || method != e.method
                || (!descriptor && (crc != e.crc || csize != e.compressedSize || usize != e.size)) {
                throw Refusal(message: "\(e.name): the entry's header disagrees with the zip's directory")
            }
            let start = a + localSize + nameLen + extraLen
            let end = start + e.compressedSize
            if end > cdOffset || start < 0 || end > zip.count {
                throw Refusal(message: "\(e.name): the entry runs past the end of the data")
            }
            let stored = Array(zip[start..<end])
            guard let data = e.method == deflate ? inflate(stored, size: e.size) : stored else {
                throw Refusal(message: "\(e.name): the entry's compressed data is damaged or not the size it declares")
            }
            if data.count != e.size { throw Refusal(message: "\(e.name): the entry's size is wrong") }
            if data.count > maxEntryBytes { throw Refusal(message: "\(e.name): the entry's size is wrong") }
            if Int(PackValidator.crc32(data, 0, data.count)) != e.crc {
                throw Refusal(message: "\(e.name): CRC-32 mismatch")
            }
            files[e.name] = Data(data)
            previousEnd = end
        }
        if files.values.reduce(0, { $0 + $1.count }) > maxTotalBytes {
            throw Refusal(message: "the zip's files add up to more than the 10 MB a pack may be")
        }
        return files
    }

    /// The entry's bytes, inflated into a buffer one byte longer than the declared size (already held to the 5 MB
    /// limit), or nil when the data is damaged, ends early or would run past that size. Nothing is ever inflated beyond
    /// what was declared plus that one byte, so a small zip can't expand without bound.
    private static func inflate(_ compressed: [UInt8], size: Int) -> [UInt8]? {
        if compressed.isEmpty { return size == 0 ? [] : nil }
        var out = [UInt8](repeating: 0, count: size + 1)
        let n = compressed.withUnsafeBufferPointer { src in
            out.withUnsafeMutableBufferPointer { dst in
                compression_decode_buffer(dst.baseAddress!, dst.count, src.baseAddress!, src.count, nil, COMPRESSION_ZLIB)
            }
        }
        guard n == size else { return nil }   // short (damaged or truncated), or one byte more than declared
        out.removeLast()
        return out
    }

    /// Why a zip entry name is unsafe, or nil. The validator's own path rule (FORMAT 3.5) runs after this.
    static func unsafeName(_ name: String) -> String? {
        if name.isEmpty { return "an empty name" }
        if name.hasPrefix("/") { return "absolute paths are not allowed" }
        if name.contains("\\") { return "backslashes are not allowed" }
        if name.contains(":") { return "drive letters and colons are not allowed" }
        for segment in name.split(separator: "/", omittingEmptySubsequences: false) {
            if segment.isEmpty || segment == "." || segment == ".." {
                return "empty, . and .. path segments are not allowed"
            }
        }
        return nil
    }

    /// A regular file by its attributes. Unix-made entries (host 3) carry a mode in the high 16 bits:
    /// it must be a regular file (or absent). The MS-DOS directory bit is refused from any host.
    private static func isRegularFile(madeBy: Int, external: Int) -> Bool {
        if external & 0x10 != 0 { return false }
        if madeBy >> 8 == 3 {
            let mode = external >> 16
            if mode != 0 && mode & 0xF000 != 0x8000 { return false }
        }
        return true
    }

    /// The name at `at`, plain printable ASCII only (nil otherwise, or when it runs off the end). `allowSpace`: a
    /// user's zip, where a space may name the folder that was compressed.
    private static func name(_ zip: [UInt8], _ at: Int, _ length: Int, allowSpace: Bool = false) -> String? {
        if length == 0 { return "" }
        guard at >= 0, at + length <= zip.count else { return nil }
        var out = String.UnicodeScalarView()
        for i in 0..<length {
            let b = zip[at + i]
            if (b < 0x21 && !(allowSpace && b == 0x20)) || b > 0x7e { return nil }
            out.append(Unicode.Scalar(b))
        }
        return String(out)
    }

    private static func u16(_ b: [UInt8], _ at: Int) throws -> Int {
        guard at >= 0, at + 2 <= b.count else { throw Refusal(message: "the zip is malformed") }
        return Int(b[at]) | Int(b[at + 1]) << 8
    }

    private static func u32(_ b: [UInt8], _ at: Int) throws -> Int {
        guard at >= 0, at + 4 <= b.count else { throw Refusal(message: "the zip is malformed") }
        return Int(b[at]) | Int(b[at + 1]) << 8 | Int(b[at + 2]) << 16 | Int(b[at + 3]) << 24
    }
}

/// Why a pack zip was refused, in a sentence.
struct WidgetZipError: Error, Equatable {
    let message: String
}
