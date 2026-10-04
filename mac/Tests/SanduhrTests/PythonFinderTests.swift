import Foundation
import Testing
@testable import Sanduhr

@Suite("Python finder")
struct PythonFinderTests {
    private let clt = "/Library/Developer/CommandLineTools"

    @Test func theSystemStubIsUsedOnlyWithDeveloperPython() {
        let stubOnly: Set<String> = ["/usr/bin/python3"]
        #expect(PythonFinder.candidates(fileExists: stubOnly.contains, developerDir: nil).isEmpty)
        #expect(PythonFinder.candidates(fileExists: stubOnly.contains, developerDir: clt).isEmpty)
        let withCLT: Set<String> = ["/usr/bin/python3", clt + "/usr/bin/python3", "/opt/homebrew/bin/python3"]
        #expect(PythonFinder.candidates(fileExists: withCLT.contains, developerDir: nil)
                == ["/usr/bin/python3", "/opt/homebrew/bin/python3"])
        // xcode-select points elsewhere: that folder decides.
        #expect(PythonFinder.candidates(fileExists: withCLT.contains, developerDir: "/Applications/Xcode.app/Contents/Developer")
                == ["/opt/homebrew/bin/python3"])
    }

    @Test func picksTheFirstNewEnoughPython() {
        let files: Set<String> = ["/usr/bin/python3", clt + "/usr/bin/python3", "/usr/local/bin/python3"]
        var probed: [String] = []
        let found = PythonFinder.find(fileExists: files.contains, developerDir: nil) { path in
            probed.append(path)
            return path == "/usr/bin/python3" ? (3, 8) : (3, 12)
        }
        #expect(found == .found(path: "/usr/local/bin/python3", version: "3.12"))
        #expect(probed == ["/usr/bin/python3", "/usr/local/bin/python3"])
        #expect(PythonFinder.find(fileExists: files.contains, developerDir: nil) { _ in (3, 8) }
                == .tooOld(path: "/usr/bin/python3", version: "3.8"))
        #expect(PythonFinder.find(fileExists: { _ in false }, developerDir: nil) { _ in (3, 12) } == .missing)
        #expect(PythonFinder.find(fileExists: files.contains, developerDir: nil) { _ in nil } == .missing)
    }

    @Test func readsVersions() {
        #expect(PythonFinder.parseVersion("3.9\n").map { [$0.0, $0.1] } == [3, 9])
        #expect(PythonFinder.parseVersion("Python") == nil)
    }
}
