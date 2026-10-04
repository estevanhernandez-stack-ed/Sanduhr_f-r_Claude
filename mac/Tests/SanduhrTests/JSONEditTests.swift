import Foundation
import Testing
@testable import Sanduhr

@Suite("JSON edit")
struct JSONEditTests {
    private let value = JSONEdit.Value.object([
        JSONEdit.Pair("type", .string("command")),
        JSONEdit.Pair("command", .string("/usr/bin/python3 '/A B/s.py'")),
    ])

    private func roundTrip(_ text: String, key: String = "statusLine") throws -> String {
        let b = Array(text.utf8)
        let set = JSONEdit.set(b, in: try JSONEdit.topObject(b), key: key, value: value)
        let parsed = try JSONEdit.root(Data(set.bytes))
        #expect(NSArray(array: [parsed[key] as Any]).isEqual(to: [value.plain]))
        let back = JSONEdit.remove(set.bytes, in: try JSONEdit.topObject(set.bytes), key: key, emptyInner: set.emptyInner)
        #expect(String(decoding: back, as: UTF8.self) == text)
        return String(decoding: set.bytes, as: UTF8.self)
    }

    @Test func appendsLastInTheFilesStyleAndRemovesBackToTheSameBytes() throws {
        let two = try roundTrip("{\n  \"a\": 1,\n  \"b\": [true, null]\n}\n")
        #expect(two == "{\n  \"a\": 1,\n  \"b\": [true, null],\n  \"statusLine\": {\n    \"type\": \"command\",\n    \"command\": \"/usr/bin/python3 '/A B/s.py'\"\n  }\n}\n")
        let four = try roundTrip("{\n    \"model\": \"x\"\n}")
        #expect(four.contains("\n    \"statusLine\": {\n        \"type\""))
        let compact = try roundTrip("{\"a\":{\"b\":\"}\"},\"c\":\"\\\"q\"}")
        #expect(compact == "{\"a\":{\"b\":\"}\"},\"c\":\"\\\"q\",\"statusLine\":{\"type\":\"command\",\"command\":\"/usr/bin/python3 '/A B/s.py'\"}}")
        _ = try roundTrip("{}")
        _ = try roundTrip("{ \n\t }\n")
        _ = try roundTrip("{\"é\": \"ü\", \"k\\u0041\": 2}")
    }

    @Test func replacesAndRestoresAValueInPlace() throws {
        let text = "{\n  \"statusLine\": {\"type\": \"command\", \"command\": \"mine.sh\"},\n  \"z\": 0\n}"
        let b = Array(text.utf8)
        let set = JSONEdit.set(b, in: try JSONEdit.topObject(b), key: "statusLine", value: value)
        #expect(set.previous.map { String(decoding: $0, as: UTF8.self) } == "{\"type\": \"command\", \"command\": \"mine.sh\"}")
        #expect(String(decoding: set.bytes, as: UTF8.self).hasSuffix("\n  },\n  \"z\": 0\n}"))
        let back = JSONEdit.restore(set.bytes, in: try JSONEdit.topObject(set.bytes), key: "statusLine", previous: set.previous!)
        #expect(back == b)
    }

    @Test func removesAMemberThatIsntLast() throws {
        let text = "{\n  \"a\": 1,\n  \"b\": 2,\n  \"c\": 3\n}"
        let b = Array(text.utf8)
        let out = JSONEdit.remove(b, in: try JSONEdit.topObject(b), key: "b")
        #expect(String(decoding: out, as: UTF8.self) == "{\n  \"a\": 1,\n  \"c\": 3\n}")
        let first = JSONEdit.remove(b, in: try JSONEdit.topObject(b), key: "a")
        #expect(String(decoding: first, as: UTF8.self) == "{\n  \"b\": 2,\n  \"c\": 3\n}")
    }

    @Test func findsNestedObjects() throws {
        let text = "{\"mcpServers\": {\"other\": {\"command\": \"x\"}}, \"n\": 1}"
        let b = Array(text.utf8)
        let top = try JSONEdit.topObject(b)
        let servers = try JSONEdit.object(b, at: top.member("mcpServers")!.valueStart)
        #expect(servers.members.map(\.key) == ["other"])
        #expect(throws: JSONEdit.Failure.notAnObject) { try JSONEdit.object(b, at: top.member("n")!.valueStart) }
    }

    @Test func refusesWhatIsntAJSONObject() {
        for text in ["", "[]", "{\"a\": 1,}", "{\"a\": 1} trailing", "// c\n{}", "\"s\""] {
            #expect(throws: JSONEdit.Failure.malformed) { try JSONEdit.root(Data(text.utf8)) }
        }
    }
}
