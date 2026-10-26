import Foundation
import SystemConfiguration
import os

// The lock screen line's root helper (#137, docs/lock-screen-line-spec.md). launchd runs it once
// a minute (Contents/Library/LaunchDaemons/com.626labs.sanduhr.lockscreen.plist, or its .dev
// twin in an ad-hoc build, registered by the app with SMAppService.daemon). Each run: find the console user, read their lock-line.txt with
// the checks in LockLineHelperRules, set or clear LoginwindowText, exit. No network, no
// arguments, one key. It logs what it did, never the text.

let log = Logger(subsystem: "com.626labs.sanduhr", category: "lockline")
let stateDir = "/Library/Application Support/Sanduhr"
let lastPath = stateDir + "/lockline-last.txt"
let originalPath = stateDir + "/lockline-original.txt"

/// The logged-in user at the console, nil at the login window.
func consoleUser() -> (uid: uid_t, home: String)? {
    var uid: uid_t = 0
    guard let name = SCDynamicStoreCopyConsoleUser(nil, &uid, nil) as String?,
          name != "loginwindow", uid != 0, let pw = getpwuid(uid), let dir = pw.pointee.pw_dir else { return nil }
    return (uid, String(cString: dir))
}

/// The line file if it is a plain file the console user owns and small enough; nil otherwise.
func readLine(uid: uid_t, home: String) -> String? {
    let path = home + "/" + LockLineHelperRules.relativePath
    var st = stat()
    guard lstat(path, &st) == 0 else { return nil }
    guard (st.st_mode & S_IFMT) == S_IFREG, st.st_uid == uid, st.st_size <= LockLineHelperRules.maxBytes else {
        log.error("refused: not a small plain file owned by the console user")
        return nil
    }
    guard let data = FileManager.default.contents(atPath: path) else { return nil }
    guard let text = LockLineHelperRules.accept(data) else {
        log.error("refused: the text did not pass the checks")
        return nil
    }
    return text
}

func readState(_ path: String) -> String? {
    guard let data = FileManager.default.contents(atPath: path) else { return nil }
    return String(data: data, encoding: .utf8)
}

func writeState(_ path: String, _ text: String?) {
    try? FileManager.default.createDirectory(atPath: stateDir, withIntermediateDirectories: true,
                                             attributes: [.posixPermissions: 0o755])
    if let text {
        FileManager.default.createFile(atPath: path, contents: Data(text.utf8), attributes: [.posixPermissions: 0o600])
    } else {
        try? FileManager.default.removeItem(atPath: path)
    }
}

let domain = LockLineHelperRules.domain as CFString
let key = LockLineHelperRules.key as CFString

func currentText() -> String? {
    CFPreferencesCopyValue(key, domain, kCFPreferencesAnyUser, kCFPreferencesAnyHost) as? String
}

func setText(_ value: String?) {
    CFPreferencesSetValue(key, value as CFString?, domain, kCFPreferencesAnyUser, kCFPreferencesAnyHost)
    CFPreferencesSynchronize(domain, kCFPreferencesAnyUser, kCFPreferencesAnyHost)
}

let user = consoleUser()
let line = user.flatMap { readLine(uid: $0.uid, home: $0.home) }
// At the login window there is no user file: leave whatever is there.
if user != nil {
    let (action, keep) = LockLineHelperRules.plan(line: line, current: currentText(),
                                                 lastWritten: readState(lastPath),
                                                 savedOriginal: readState(originalPath))
    if let keep { writeState(originalPath, keep) }
    switch action {
    case .none:
        break
    case .set(let text):
        setText(text)
        writeState(lastPath, text)
        log.notice("set")
    case .restore(let text):
        setText(text)
        writeState(lastPath, nil)
        writeState(originalPath, nil)
        log.notice("restored the earlier message")
    case .clear:
        setText(nil)
        writeState(lastPath, nil)
        log.notice("cleared")
    }
}
exit(0)
