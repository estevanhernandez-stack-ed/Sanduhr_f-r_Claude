import Foundation

/// A receipts file read record by record (item 71, slice 0), so one record that doesn't decode
/// (written by a newer Sanduhr, cut short, hand-edited) is reported and set aside instead of
/// blinding every undo. Before, `installs.json` decoded as one array, all or nothing, and a
/// failure read as "no receipts".
///
/// Every record keeps the bytes it was read from. On save, a record that didn't decode is
/// written back byte for byte after the others, and a record that decoded and didn't change is
/// written back from its own bytes too, so fields this version doesn't know (a newer Sanduhr's)
/// survive an older app's save. A file whose top level isn't a JSON array is unreadable as a
/// whole: the next save moves it aside (`<name>.unreadable`), never over it. Generic over the
/// record type, so later receipt kinds (slice 2's `configEdit`) use the same reader.
enum ReceiptLog {
    struct Decoded<Record> {
        var records: [Record] = []
        /// Each decoded record's bytes as read, parallel to `records`.
        var raws: [Data] = []
        /// Each record that didn't decode, byte for byte, in file order.
        var unreadable: [Data] = []
        /// The file is there but its top level isn't a JSON array.
        var fileUnreadable = false

        /// Records reported and skipped (a whole unreadable file counts as one).
        var skipped: Int { unreadable.count + (fileUnreadable ? 1 : 0) }
    }

    static let asideSuffix = ".unreadable"

    static func decode<Record: Decodable>(_ type: Record.Type, from data: Data,
                                          decoder: JSONDecoder = JSONDecoder()) -> Decoded<Record> {
        var out = Decoded<Record>()
        let b = Array(data)
        guard (try? JSONSerialization.jsonObject(with: data, options: [.fragmentsAllowed])) is [Any],
              let array = try? JSONEdit.array(b, at: 0) else {
            out.fileUnreadable = true
            return out
        }
        for element in array.elements {
            let one = Data(b[element.start..<element.end])
            if let record = try? decoder.decode(Record.self, from: one) {
                out.records.append(record)
                out.raws.append(one)
            } else {
                out.unreadable.append(one)
            }
        }
        return out
    }

    /// The file's bytes for `records`, followed by `previous`'s unreadable records as they were.
    /// A record equal to one `previous` decoded is written from that record's bytes.
    static func encode<Record: Codable & Equatable>(_ records: [Record],
                                                     previous: Decoded<Record> = Decoded()) -> Data? {
        let enc = JSONEncoder()
        enc.outputFormatting = [.sortedKeys, .prettyPrinted, .withoutEscapingSlashes]
        var used = Set<Int>()
        var parts: [Data] = []
        for record in records {
            if let i = previous.records.indices.first(where: { !used.contains($0) && previous.records[$0] == record }),
               i < previous.raws.count {
                used.insert(i)
                parts.append(previous.raws[i])
            } else {
                guard let fresh = try? enc.encode(record) else { return nil }
                parts.append(fresh)
            }
        }
        parts += previous.unreadable
        var out = Data("[\n".utf8)
        for (i, part) in parts.enumerated() {
            out += part
            out += Data((i == parts.count - 1 ? "\n" : ",\n").utf8)
        }
        out += Data("]\n".utf8)
        // Bytes kept from the file are whole JSON values, so this parses; if it somehow doesn't,
        // the readable records are written fresh and nothing unreadable is lost from the file
        // (the caller only writes what this returns).
        guard (try? JSONSerialization.jsonObject(with: out)) is [Any] else { return nil }
        return out
    }

    /// Where an unreadable file goes before a save writes a new one.
    static func asideURL(_ url: URL) -> URL {
        url.deletingLastPathComponent().appendingPathComponent(url.lastPathComponent + asideSuffix)
    }
}
