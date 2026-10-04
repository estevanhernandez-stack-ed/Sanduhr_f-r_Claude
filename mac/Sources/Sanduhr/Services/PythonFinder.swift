import Foundation

/// Finding a python3 (3.9 or later) for the integration scripts (item 49), without ever making
/// macOS offer to install anything.
///
/// `/usr/bin/python3` is on every Mac, but it is a stub: without Apple's Command Line Tools (or
/// Xcode) running it opens the "install the command line developer tools" dialog. So the stub is
/// used only when the developer folder it forwards to holds a python3 (`xcode-select`'s link at
/// `/var/db/xcode_select_link`, else the standard Command Line Tools and Xcode folders), checked
/// by looking at files, never by running it. It comes first when it works: it is the path that
/// survives Homebrew upgrades and uninstalls. Then Homebrew's and python.org's python3.
/// A candidate is run once (`-c` printing its version) only after that check. With none found,
/// Settings explains what is needed and offers "Install Command Line Tools…", which runs
/// `xcode-select --install` (macOS asks first) only when clicked.
enum PythonFinder {
    static let systemStub = "/usr/bin/python3"
    static let others = [
        "/opt/homebrew/bin/python3",
        "/usr/local/bin/python3",
        "/Library/Frameworks/Python.framework/Versions/Current/bin/python3",
    ]
    static let developerDirs = [
        "/Library/Developer/CommandLineTools",
        "/Applications/Xcode.app/Contents/Developer",
    ]
    static let selectLink = "/var/db/xcode_select_link"
    static let minimum = (3, 9)

    enum Result: Equatable {
        case found(path: String, version: String)
        /// Pythons exist but every one is older than 3.9 (the newest found is named).
        case tooOld(path: String, version: String)
        case missing
    }

    /// The paths to try, in order. `fileExists` and `developerDir` stand in for the disk.
    static func candidates(fileExists: (String) -> Bool, developerDir: String?) -> [String] {
        var out: [String] = []
        let devs = developerDir.map { [$0] } ?? developerDirs
        if fileExists(systemStub), devs.contains(where: { fileExists($0 + "/usr/bin/python3") }) {
            out.append(systemStub)
        }
        out += others.filter(fileExists)
        return out
    }

    /// The first candidate whose version is 3.9 or later. `probe` runs a candidate and returns
    /// its (major, minor), or nil when it didn't run.
    static func find(fileExists: (String) -> Bool, developerDir: String?,
                     probe: (String) -> (Int, Int)?) -> Result {
        var old: (String, (Int, Int))?
        for path in candidates(fileExists: fileExists, developerDir: developerDir) {
            guard let v = probe(path) else { continue }
            if v >= minimum { return .found(path: path, version: "\(v.0).\(v.1)") }
            if old.map({ v > $0.1 }) ?? true { old = (path, v) }
        }
        if let old { return .tooOld(path: old.0, version: "\(old.1.0).\(old.1.1)") }
        return .missing
    }

    /// The real search. Runs pythons: call it off the main thread.
    static func find() -> Result {
        let fm = FileManager.default
        let dev = try? fm.destinationOfSymbolicLink(atPath: selectLink)
        return find(fileExists: { fm.isExecutableFile(atPath: $0) || fm.fileExists(atPath: $0) },
                    developerDir: dev, probe: probe)
    }

    /// `python3 -c` printing "major.minor", with a five-second limit.
    static func probe(_ path: String) -> (Int, Int)? {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: path)
        p.arguments = ["-I", "-c", "import sys; print('%d.%d' % sys.version_info[:2])"]
        let out = Pipe()
        p.standardOutput = out
        p.standardError = FileHandle.nullDevice
        p.standardInput = FileHandle.nullDevice
        do { try p.run() } catch { return nil }
        let deadline = Date().addingTimeInterval(5)
        while p.isRunning, Date() < deadline { usleep(20_000) }
        if p.isRunning {
            p.terminate()
            return nil
        }
        guard p.terminationStatus == 0 else { return nil }
        let text = String(decoding: out.fileHandleForReading.readDataToEndOfFile(), as: UTF8.self)
        return parseVersion(text)
    }

    static func parseVersion(_ text: String) -> (Int, Int)? {
        let parts = text.trimmingCharacters(in: .whitespacesAndNewlines).split(separator: ".")
        guard parts.count >= 2, let major = Int(parts[0]), let minor = Int(parts[1]) else { return nil }
        return (major, minor)
    }

    /// "Install Command Line Tools…": `xcode-select --install`, which shows Apple's own dialog.
    static func installCommandLineTools() {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/usr/bin/xcode-select")
        p.arguments = ["--install"]
        p.standardOutput = FileHandle.nullDevice
        p.standardError = FileHandle.nullDevice
        try? p.run()
    }
}
