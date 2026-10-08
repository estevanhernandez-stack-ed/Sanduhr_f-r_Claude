import Foundation
import Testing
@testable import Sanduhr

/// The smoke tools' in-app hooks: the gate, the link parsing, the YAML writer and the state and
/// tree shapes. AppKit-free; nothing here reads the real defaults or touches a window.

@Suite("Debug gate")
struct DebugGateTests {
    @Test func offByDefault() {
        #expect(!DebugGate.isOn(defaultsFlag: false, environment: [:]))
        #expect(!DebugGate.isOn(defaultsFlag: false, environment: ["SANDUHR_DEBUG_HOOKS": "0"]))
        #expect(!DebugGate.isOn(defaultsFlag: false, environment: ["SANDUHR_DEBUG_HOOKS": ""]))
        #expect(!DebugGate.isOn(defaultsFlag: false, environment: ["OTHER": "1"]))
    }

    @Test func onByDefaultsOrEnvironment() {
        #expect(DebugGate.isOn(defaultsFlag: true, environment: [:]))
        #expect(DebugGate.isOn(defaultsFlag: false, environment: ["SANDUHR_DEBUG_HOOKS": "1"]))
        #expect(DebugGate.isOn(defaultsFlag: false, environment: ["SANDUHR_DEBUG_HOOKS": "TRUE"]))
    }
}

@Suite("Debug links")
struct DebugLinkTests {
    func parse(_ s: String) -> DebugRequest { DebugLink.parse(URL(string: s)!) }

    @Test func onlySanduhrDebugLinks() {
        #expect(DebugLink.isDebug(URL(string: "sanduhr://debug/snapshot?dir=/tmp/x")!))
        #expect(DebugLink.isDebug(URL(string: "SANDUHR://Debug/action?name=refresh")!))
        #expect(!DebugLink.isDebug(URL(string: "sanduhr://settings")!))
        #expect(!DebugLink.isDebug(URL(string: "estedesk://debug/snapshot")!))
        #expect(!DebugLink.isDebug(URL(string: "sanduhr://join-next")!))
    }

    @Test func snapshotNeedsDir() {
        #expect(parse("sanduhr://debug/snapshot?dir=/tmp/a%20b") ==
                DebugRequest(command: .snapshot(dir: "/tmp/a b"), dir: "/tmp/a b"))
        let missing = parse("sanduhr://debug/snapshot")
        #expect(missing.command == nil)
        #expect(missing.error == "snapshot needs dir=<path>")
    }

    @Test func actions() {
        #expect(parse("sanduhr://debug/action?name=show-widget").command == .action(.showWidget, dir: nil))
        #expect(parse("sanduhr://debug/action?name=hide-widget&dir=/tmp/d").command == .action(.hideWidget, dir: "/tmp/d"))
        #expect(parse("sanduhr://debug/action?name=settings&arg=notch").command == .action(.settings(.notch), dir: nil))
        #expect(parse("sanduhr://debug/action?name=settings&arg=desk-layout").command == .action(.settings(.deskLayout), dir: nil))
        #expect(parse("sanduhr://debug/action?name=settings&arg=updates").command == .action(.settings(.updates), dir: nil))
        #expect(parse("sanduhr://debug/action?name=settings&arg=about").command == .action(.settings(.about), dir: nil))
        #expect(parse("sanduhr://debug/action?name=settings").command == .action(.settings(nil), dir: nil))
        #expect(parse("sanduhr://debug/action?name=close-settings").command == .action(.closeSettings, dir: nil))
        #expect(parse("sanduhr://debug/action?name=refresh").command == .action(.refresh, dir: nil))
        #expect(parse("sanduhr://debug/action?name=test-alert").command == .action(.testAlert, dir: nil))
        #expect(parse("sanduhr://debug/action?name=pulse").command == .action(.pulse(.fiveHour), dir: nil))
        #expect(parse("sanduhr://debug/action?name=pulse&arg=seven_day").command == .action(.pulse(.sevenDay), dir: nil))
        #expect(parse("sanduhr://debug/action?name=tool&arg=deep-work").command == .action(.tool(.deepWork), dir: nil))
        #expect(parse("sanduhr://debug/action?name=tool&arg=pacing").command == .action(.tool(.pacing), dir: nil))
        #expect(parse("sanduhr://debug/action?name=tool&arg=snake").command == .action(.tool(.snake), dir: nil))
        #expect(parse("sanduhr://debug/action?name=desk&arg=on").command == .action(.desk(true), dir: nil))
        #expect(parse("sanduhr://debug/action?name=desk&arg=off").command == .action(.desk(false), dir: nil))
        #expect(parse("sanduhr://debug/action?name=notch&arg=on").command == .action(.notch(true), dir: nil))
        #expect(parse("sanduhr://debug/action?name=camera-light&arg=on").command == .action(.cameraLight(true), dir: nil))
        #expect(parse("sanduhr://debug/action?name=camera-light&arg=off").command == .action(.cameraLight(false), dir: nil))
        #expect(parse("sanduhr://debug/action?name=glow").command == .action(.glow(.alert), dir: nil))
        #expect(parse("sanduhr://debug/action?name=glow&arg=meeting").command == .action(.glow(.meeting), dir: nil))
        #expect(parse("sanduhr://debug/action?name=glow&arg=CAMERA").command == .action(.glow(.camera), dir: nil))
        #expect(parse("sanduhr://debug/action?name=theme&arg=match-desk").command == .action(.theme("match-desk"), dir: nil))
        #expect(parse("sanduhr://debug/action?name=theme&arg=Obsidian").command == .action(.theme("obsidian"), dir: nil))
        #expect(parse("sanduhr://debug/action?name=account&arg=next").command == .action(.cycleAccount, dir: nil))
        #expect(parse("sanduhr://debug/action?name=usage").command == .action(.usage(.overview), dir: nil))
        #expect(parse("sanduhr://debug/action?name=usage&arg=Sessions").command == .action(.usage(.sessions), dir: nil))
        #expect(parse("sanduhr://debug/action?name=usage&arg=trends").command == .action(.usage(.trends), dir: nil))
        #expect(parse("sanduhr://debug/action?name=settings&arg=usage").command == .action(.settings(.usage), dir: nil))
        // Item 57: open or close What's New; nothing is recorded as seen.
        #expect(parse("sanduhr://debug/action?name=whats-new").command == .action(.whatsNew(true), dir: nil))
        #expect(parse("sanduhr://debug/action?name=close-whats-new").command == .action(.whatsNew(false), dir: nil))
        // Item 61: open the tour at step 1 or a given step, or close it; nothing is recorded.
        #expect(parse("sanduhr://debug/action?name=tour").command == .action(.tour(step: 1), dir: nil))
        #expect(parse("sanduhr://debug/action?name=tour-step&arg=3").command == .action(.tour(step: 3), dir: nil))
        #expect(parse("sanduhr://debug/action?name=close-tour").command == .action(.closeTour, dir: nil))
        #expect(parse("sanduhr://debug/action?name=tour-step").error == "tour-step needs arg=<step number from 1>")
        #expect(parse("sanduhr://debug/action?name=tour-step&arg=0").error == "tour-step needs arg=<step number from 1>")
    }

    @Test func badActionsKeepTheDirForTheError() {
        let r = parse("sanduhr://debug/action?name=explode&dir=/tmp/d")
        #expect(r.command == nil)
        #expect(r.dir == "/tmp/d")
        #expect(r.error?.hasPrefix("unknown action: explode") == true)
        #expect(parse("sanduhr://debug/action?name=desk").error == "desk needs arg=on or arg=off")
        #expect(parse("sanduhr://debug/action?name=camera-light").error == "camera-light needs arg=on or arg=off")
        #expect(parse("sanduhr://debug/action?name=glow&arg=sound").error == "glow needs arg=alert, meeting, camera, claude-waiting or claude-done")
        #expect(parse("sanduhr://debug/action?name=usage&arg=ledger").error == "usage needs arg=overview, trends or sessions")
        #expect(parse("sanduhr://debug/action?name=theme").error == "theme needs arg=<theme id>")
        // Only cycling: no hook adds, renames, signs out or removes an account.
        #expect(parse("sanduhr://debug/action?name=account").error == "account needs arg=next")
        #expect(parse("sanduhr://debug/action?name=account&arg=remove").error == "account needs arg=next")
        #expect(parse("sanduhr://debug/action?name=tool&arg=hammer").error == "tool needs arg=deep-work, pacing or snake")
        #expect(parse("sanduhr://debug/action?name=settings&arg=nowhere").error?.hasPrefix("unknown settings section") == true)
        #expect(parse("sanduhr://debug/action?name=pulse&arg=hourly").error == "unknown tier: hourly")
        #expect(parse("sanduhr://debug/action").error == "action needs name=<action>")
        #expect(parse("sanduhr://debug/launch").error == "unknown debug command: launch")
        #expect(parse("sanduhr://debug").error == "no debug command")
    }
}

@Suite("Claude Usage page state")
struct UsagePageStateTests {
    @Test func stateYAMLSaysOpenAndTabOnly() {
        let yaml = YAMLEmitter.emit(.object([("usage_page", DebugState.usagePageYAML(open: true, tab: .sessions))]))
        #expect(yaml == "usage_page:\n  open: true\n  tab: sessions\n")
        #expect(UsageTab.allCases.map(\.rawValue) == ["overview", "trends", "sessions"])
    }

    @Test func stateYAMLCountsIntegrationsWithoutPaths() {
        let yaml = YAMLEmitter.emit(.object([("integrations", DebugState.integrationsYAML(mcp: 2, statusline: 1, meters: 3, hooks: 4))]))
        #expect(yaml == "integrations:\n  mcp_installed: 2\n  statusline_installed: 1\n  meters_installed: 3\n  hooks_installed: 4\n")
    }
}

@Suite("YAML emitter")
struct YAMLEmitterTests {
    func emit(_ n: YAMLNode) -> String { YAMLEmitter.emit(n) }

    @Test func plainStringsStayPlain() {
        for s in ["Sanduhr Settings", "AXToggle", "Weekly — All Models", "Settings…", "hello world", "a-b", "x.y"] {
            #expect(YAMLEmitter.scalar(s) == s, "\(s)")
        }
    }

    @Test func stringsThatNeedQuotes() {
        #expect(YAMLEmitter.scalar("") == "\"\"")
        #expect(YAMLEmitter.scalar("Resets: today") == "\"Resets: today\"")
        #expect(YAMLEmitter.scalar("ends with:") == "\"ends with:\"")
        #expect(YAMLEmitter.scalar("a # comment") == "\"a # comment\"")
        #expect(YAMLEmitter.scalar("- dash") == "\"- dash\"")
        #expect(YAMLEmitter.scalar("*star") == "\"*star\"")
        #expect(YAMLEmitter.scalar("@at") == "\"@at\"")
        #expect(YAMLEmitter.scalar("'single'") == "\"'single'\"")
        #expect(YAMLEmitter.scalar("say \"hi\"") == "say \"hi\"")   // inner quotes are fine plain
        #expect(YAMLEmitter.scalar("\"lead") == "\"\\\"lead\"")
        #expect(YAMLEmitter.scalar(" padded") == "\" padded\"")
        #expect(YAMLEmitter.scalar("two\nlines") == "\"two\\nlines\"")
        #expect(YAMLEmitter.scalar("tab\there") == "\"tab\\there\"")
        #expect(YAMLEmitter.scalar("back\\slash: x") == "\"back\\\\slash: x\"")
        #expect(YAMLEmitter.scalar("bell\u{07}") == "\"bell\\u0007\"")
        #expect(YAMLEmitter.scalar("a, b") == "\"a, b\"")
        #expect(YAMLEmitter.scalar("[x]") == "\"[x]\"")
    }

    @Test func wordsAndNumbersThatWouldChangeType() {
        for s in ["true", "False", "yes", "No", "on", "OFF", "y", "n", "null", "~", ".inf", ".nan", "<<"] {
            #expect(YAMLEmitter.scalar(s) == "\"\(s)\"", "\(s)")
        }
        for s in ["1", "42", "3.5", "-1", "+2", ".5", "2026-10-02", "0x1F", "1e3", "10:30"] {
            #expect(YAMLEmitter.scalar(s) == "\"\(s)\"", "\(s)")
        }
    }

    @Test func numbers() {
        #expect(YAMLEmitter.number(12) == "12")
        #expect(YAMLEmitter.number(-3) == "-3")
        #expect(YAMLEmitter.number(0.5) == "0.5")
        #expect(YAMLEmitter.number(0.123456) == "0.1235")
        #expect(YAMLEmitter.number(-0.00001) == "0")
        #expect(YAMLEmitter.number(.nan) == ".nan")
        #expect(YAMLEmitter.number(.infinity) == ".inf")
        #expect(YAMLEmitter.number(-.infinity) == "-.inf")
    }

    @Test func scalarsAtTheTop() {
        #expect(emit(.string("x")) == "x\n")
        #expect(emit(.int(3)) == "3\n")
        #expect(emit(.bool(true)) == "true\n")
        #expect(emit(.null) == "null\n")
        #expect(emit(.list([])) == "[]\n")
        #expect(emit(.map([])) == "{}\n")
    }

    @Test func nestedMapsAndLists() {
        let node: YAMLNode = .list([
            .object([
                ("window", .string("settings")),
                ("title", .string("Sanduhr Settings")),
                ("frame", .rect(CGRect(x: 10, y: 20, width: 760, height: 600.5))),
                ("visible", .bool(true)),
                ("tree", .list([
                    .object([
                        ("role", .string("AXCheckBox")),
                        ("label", .string("Extend the camera notch")),
                        ("value", .int(1)),
                        ("children", .list([.object([("role", .string("AXStaticText"))])])),
                    ]),
                ])),
                ("empty", .list([])),
            ]),
        ])
        #expect(emit(node) == """
        - window: settings
          title: Sanduhr Settings
          frame: [10, 20, 760, 600.5]
          visible: true
          tree:
            - role: AXCheckBox
              label: Extend the camera notch
              value: 1
              children:
                - role: AXStaticText
          empty: []

        """)
    }

    @Test func mapsOfMapsAndListsOfStringsAndLists() {
        let node: YAMLNode = .object([
            ("alerts", .object([("enabled", .bool(false)), ("sound", .string("default"))])),
            ("names", .list([.string("a: b"), .string("plain")])),
            ("grid", .list([.list([.int(1), .int(2)]), .list([.string("x")])])),
            ("nothing", .null),
            ("dropped", nil),
        ])
        #expect(emit(node) == """
        alerts:
          enabled: false
          sound: default
        names:
          - "a: b"
          - plain
        grid:
          - [1, 2]
          -
            - x
        nothing: null

        """)
    }

    @Test func emptyTextIsLeftOut() {
        #expect(YAMLNode.text(nil) == nil)
        #expect(YAMLNode.text("") == nil)
        #expect(YAMLNode.text("x") == .string("x"))
    }
}

@Suite("Debug state and tree")
struct DebugStateTests {
    func meter(_ tier: Tier, _ pct: Int, pace: Double?) -> DeskMeterRow {
        DeskMeterRow(tier: tier, label: tier.label, percent: pct, fill: Double(pct) / 100, pace: pace, reset: "Today 5:00 PM")
    }

    @Test func stateKeysInOrder() {
        var s = DebugStateInput()
        s.deskEnabled = true
        s.deskRunning = true
        s.layout = "message:tl clock:bl meters:bl meetings:bl"
        s.widgetVisible = true
        s.widgetVisibility = .whileDeskOff
        s.settingsOpen = true
        s.settingsSection = .notch
        s.settingsPreview = SettingsPreviewKind.of(.notch)
        s.meters = [meter(.fiveHour, 7, pace: 0.25), meter(.sevenDay, 63, pace: nil)]
        s.meetingsCount = 2
        s.lastFetch = Date(timeIntervalSince1970: 0)
        s.activeTool = "snake"
        s.pulseCount = 3
        s.glowCount = 2
        s.glowShape = .plain
        s.glowSwitches = NotchGlowSwitches(alerts: true, meetings: false, camera: true)
        s.theme = "aurora"
        s.menuBar = .rotate
        s.notchRight = .message
        s.cameraLight = true
        s.menu = SanduhrMenu.groups(widgetVisible: true, deepWork: false, pacing: true, snake: false)
        s.version = "2.1.0"
        s.build = "3"
        let yaml = YAMLEmitter.emit(DebugState.yaml(s))
        let keys = yaml.split(separator: "\n").filter { !$0.hasPrefix(" ") && !$0.hasPrefix("-") }
            .map { String($0.split(separator: ":")[0]) }
        #expect(keys == ["desk_enabled", "desk_running", "layout", "desk_pieces", "desk_arrange", "notch", "has_notch",
                         "notch_left", "notch_right", "notch_strip", "camera_in_use", "camera_light", "now_playing", "now_playing_idle", "notch_shows", "widget_visible", "widget_visibility",
                         "menu_bar", "settings_open", "settings_section", "settings_anchor", "settings_anchor_visible", "usage_page", "meters", "widget_warnings", "hidden_limits",
                         "temporary_limits", "silenced_limits", "meetings_count", "desk_frames", "desk_frames_ok", "desk_frames_problem", "desk_piece_clicks", "dock", "alerts",
                         "last_fetch", "active_tool", "pacing_pinned", "pulse_count", "glow_count", "glow_shape", "glow_alerts", "glow_meetings",
                         "glow_camera", "glow_claude_waiting", "glow_claude_done", "theme", "menu", "menu_submenus", "credentials_store", "account_ref", "accounts_count", "history_days", "data",
                         "local_activity", "vault", "integrations", "pending_suggestions", "follow", "follow_paused", "version", "build", "whats_new", "tour", "watchers", "av_indicators", "settings_preview", "settings_preview_folded", "mods_page", "message_editor", "hot_keys"])
        #expect(yaml.contains("settings_section: notch\nsettings_anchor: null\nsettings_anchor_visible: null\nusage_page:\n  open: false\n  tab: overview\nmeters:"))
        #expect(yaml.contains("hot_keys:\n  join: true\n  settings: true\n  registered: 0\n"))
        #expect(yaml.contains("  shown: none\n  place: besideRight\n"))
        #expect(yaml.contains("settings_preview: notch"))
        #expect(yaml.contains("widget_visible: true\nwidget_visibility: whileDeskOff\nmenu_bar: rotate\nsettings_open: true\n"))
        #expect(yaml.contains("notch_left: meetingOrTime\nnotch_right: message\nnotch_strip: meetingOrMeters\ncamera_in_use: false\ncamera_light: true\nnow_playing:\n"))
        #expect(yaml.contains("pulse_count: 3\nglow_count: 2\nglow_shape: plain\nglow_alerts: true\nglow_meetings: false\nglow_camera: true\nglow_claude_waiting: false\nglow_claude_done: false\ntheme: aurora\nmenu:\n"))
        #expect(yaml.contains("layout: message:tl") == false)   // the colons force quotes
        #expect(yaml.contains("layout: \"message:tl clock:bl meters:bl meetings:bl\"\n"))
        #expect(yaml.contains("""
        meters:
          - tier: five_hour
            label: Session (5hr)
            percent: 7
            fill: 0.07
            pace: 0.25
            reset: "Today 5:00 PM"
            warning: false
          - tier: seven_day
        """))
        #expect(yaml.contains("    pace: null\n"))
        #expect(yaml.contains("last_fetch: \"1970-01-01T00:00:00Z\"\n"))
        #expect(yaml.contains("credentials_store: file\naccount_ref: null\naccounts_count: 0\nhistory_days: 30\ndata:\n  activity: \"off\"\n  names: names\n  share: \"off\"\n  folder_linked: false\nlocal_activity:\n  reading: false\n  events: 0\nvault:\n  recording: false\n  months: 0\n  last_ingest_ok: false\nintegrations:\n  mcp_installed: 0\n  statusline_installed: 0\n  meters_installed: 0\n  hooks_installed: 0\npending_suggestions:\n  messages: false\n  theme: false\nfollow: false\nfollow_paused: false\nversion: \"2.1.0\"\n"))
        #expect(yaml.contains("version: \"2.1.0\"\nbuild: \"3\"\nwhats_new:\n  last_seen: null\n  pending: 0\n  open: false\n  hide_after_updates: false\n"))
        #expect(yaml.contains("tour:\n  open: false\n  step: 0\n  steps_shown: 0\n  done: false\n  pending: false\n"))
        #expect(yaml.contains("""
          - header: Tools
            items:
              - title: Deep Work
                checked: false
              - title: Pacing Calculators
                checked: true
        """))
    }

    @Test func warningRowShowsInMeters() {
        var s = DebugStateInput()
        var row = meter(.sevenDay, 92, pace: nil)
        row.warning = true
        s.meters = [row]
        let yaml = YAMLEmitter.emit(DebugState.yaml(s))
        #expect(yaml.contains("    reset: \"Today 5:00 PM\"\n    warning: true\n"))
    }

    @Test func widgetWarningsListTheTiers() {
        var s = DebugStateInput()
        s.widgetWarnings = [.sevenDay, .sevenDayOpus]
        let yaml = YAMLEmitter.emit(DebugState.yaml(s))
        #expect(yaml.contains("widget_warnings:\n  - seven_day\n  - seven_day_opus\nhidden_limits: []\ntemporary_limits: []\nsilenced_limits: []\nmeetings_count: 0\n"))
        #expect(YAMLEmitter.emit(DebugState.yaml(DebugStateInput())).contains("widget_warnings: []\n"))
        var hidden = DebugStateInput()
        hidden.hiddenLimits = [.iguanaNecktie]
        #expect(YAMLEmitter.emit(DebugState.yaml(hidden)).contains("hidden_limits:\n  - iguana_necktie\ntemporary_limits: []\n"))
        var temporary = DebugStateInput()
        temporary.temporaryLimits = [.sevenDayOpus, .iguanaNecktie]
        #expect(YAMLEmitter.emit(DebugState.yaml(temporary))
            .contains("temporary_limits:\n  - seven_day_opus\n  - iguana_necktie\nsilenced_limits: []\n"))
        var silenced = DebugStateInput()
        silenced.silencedLimits = [.fiveHour, .sevenDay]
        #expect(YAMLEmitter.emit(DebugState.yaml(silenced)).contains("silenced_limits:\n  - five_hour\n  - seven_day\nmeetings_count: 0\n"))
    }

    @Test func deskFramesListKindsKeysAndRoundedFrames() {
        var s = DebugStateInput()
        s.deskFrames = [
            DeskElement(kind: .meters, frame: CGRect(x: 52.4, y: 880.6, width: 358.5, height: 120)),
            DeskElement(kind: .meterRow, key: "five_hour", frame: CGRect(x: 52, y: 881, width: 358, height: 50)),
            DeskElement(kind: .meetingRow, key: "0", frame: CGRect(x: 52, y: 1010, width: 300, height: 25), clickable: false),
        ]
        s.deskFramesProblem = "meters frame empty"
        let yaml = YAMLEmitter.emit(DebugState.yaml(s))
        #expect(yaml.contains("""
        desk_frames:
          - kind: meters
            clickable: true
            frame: [52, 881, 359, 120]
          - kind: meter_row
            key: five_hour
            clickable: true
            frame: [52, 881, 358, 50]
          - kind: meeting_row
            key: "0"
            clickable: false
            frame: [52, 1010, 300, 25]
        desk_frames_ok: false
        desk_frames_problem: meters frame empty
        """))
        let empty = YAMLEmitter.emit(DebugState.yaml(DebugStateInput()))
        #expect(empty.contains("desk_frames: []\ndesk_frames_ok: true\ndesk_frames_problem: null\n"))
    }

    /// Item 56: the Dock's side and auto-hide, and the inset the Desk applies now.
    @Test func dockFollowsTheDeskFrames() {
        var s = DebugStateInput()
        s.dock = DockDebug(side: .left, autohide: true, inset: 73)
        let yaml = YAMLEmitter.emit(DebugState.yaml(s))
        #expect(yaml.contains("desk_frames_problem: null\ndesk_piece_clicks: true\ndock:\n  side: left\n  autohide: true\n  inset: 73\nalerts:\n"))
        let rest = YAMLEmitter.emit(DebugState.yaml(DebugStateInput()))
        #expect(rest.contains("dock:\n  side: bottom\n  autohide: false\n  inset: 0\n"))
    }

    @Test func emptyState() {
        let yaml = YAMLEmitter.emit(DebugState.yaml(DebugStateInput()))
        #expect(yaml.contains("layout: null\n"))
        #expect(yaml.contains("settings_section: null\n"))
        #expect(yaml.contains("settings_preview: null"))
        #expect(yaml.contains("meters: []\n"))
        #expect(yaml.contains("last_fetch: null\n"))
        #expect(yaml.contains("active_tool: null\n"))
        #expect(yaml.contains("""
        alerts:
          enabled: false
          session_line: 80
          weekly_line: 80
        """))
        #expect(yaml.contains("  delivery: banner\n  quiet_enabled: false\n  quiet_start: 1320\n  quiet_end: 420\n  sound: default\n"))
    }

    @Test func recognizedTextLandsOnScreen() {
        // A box in the top-left quarter of a 400 x 200 window at (100, 50): Vision measures from
        // the bottom-left, the tree from the top-left.
        let area = CGRect(x: 100, y: 50, width: 400, height: 200)
        #expect(DebugTree.screenRect(normalized: CGRect(x: 0, y: 0.5, width: 0.5, height: 0.5), in: area)
                == CGRect(x: 100, y: 50, width: 200, height: 100))
        #expect(DebugTree.screenRect(normalized: CGRect(x: 0.25, y: 0, width: 0.5, height: 0.1), in: area)
                == CGRect(x: 200, y: 230, width: 200, height: 20))
    }

    @Test func topLeftFrames() {
        // A 1000-point-tall primary screen: a window 100 tall at y 50 (bottom-left) sits 850 down.
        #expect(DebugTree.topLeft(CGRect(x: 10, y: 50, width: 200, height: 100), primaryHeight: 1000)
                == CGRect(x: 10, y: 850, width: 200, height: 100))
    }

    @Test func uniqueWindowNames() {
        #expect(DebugTree.uniqueNames(["widget", "sheet", "desk", "sheet", "sheet"])
                == ["widget", "sheet", "desk", "sheet-2", "sheet-3"])
        #expect(DebugTree.uniqueNames([]) == [])
    }

    @Test func accessibilityValues() {
        #expect(DebugTree.value(NSNumber(value: true)) == .int(1))
        #expect(DebugTree.value(NSNumber(value: false)) == .int(0))
        #expect(DebugTree.value(NSNumber(value: 42)) == .int(42))
        #expect(DebugTree.value(NSNumber(value: 0.25)) == .double(0.25))
        #expect(DebugTree.value("text") == .string("text"))
        #expect(DebugTree.value("") == nil)
        #expect(DebugTree.value(NSAttributedString(string: "rich")) == .string("rich"))
        #expect(DebugTree.value(nil) == nil)
        #expect(DebugTree.value(CGRect.zero) == nil)
    }

    @Test func nodesLeaveOutWhatIsEmpty() {
        let leaf = DebugTreeNode(role: "AXStaticText", label: nil, value: .string("7%"),
                                 frame: CGRect(x: 1, y: 2, width: 3, height: 4))
        let window = DebugWindowEntry(name: "widget", windowID: 42, title: "", frame: .zero,
                                      visible: true, tree: [DebugTreeNode(role: "AXGroup", label: "Cards", children: [leaf])])
        #expect(YAMLEmitter.emit(.list([window.yaml])) == """
        - window: widget
          window_id: 42
          frame: [0, 0, 0, 0]
          visible: true
          tree:
            - role: AXGroup
              label: Cards
              enabled: true
              frame: [0, 0, 0, 0]
              children:
                - role: AXStaticText
                  value: "7%"
                  enabled: true
                  frame: [1, 2, 3, 4]

        """)
    }
}
