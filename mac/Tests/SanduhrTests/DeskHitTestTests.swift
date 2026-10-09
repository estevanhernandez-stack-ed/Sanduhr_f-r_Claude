import Foundation
import Testing
@testable import Sanduhr

/// Desk geometry (item 41): which element is under a point, what DeskView draws, and the check
/// behind state.yaml's `desk_frames_ok`.

@Suite("Desk hit test")
struct DeskHitTestTests {
    let meters = DeskElement(kind: .meters, frame: CGRect(x: 50, y: 800, width: 360, height: 120))
    let session = DeskElement(kind: .meterRow, key: "five_hour", frame: CGRect(x: 50, y: 800, width: 360, height: 50))
    let weekly = DeskElement(kind: .meterRow, key: "seven_day", frame: CGRect(x: 50, y: 870, width: 360, height: 50))
    let account = DeskElement(kind: .account, frame: CGRect(x: 50, y: 770, width: 60, height: 20))
    let row = DeskElement(kind: .meetingRow, key: "0", frame: CGRect(x: 50, y: 940, width: 300, height: 25))

    var all: [DeskElement] { [account, meters, session, weekly, row] }

    @Test func aMeterRowWinsOverTheMetersBlock() {
        #expect(DeskHitTest.element(at: CGPoint(x: 100, y: 820), in: all) == session)
        #expect(DeskHitTest.element(at: CGPoint(x: 100, y: 900), in: all) == weekly)
        // Between the rows: the block itself.
        #expect(DeskHitTest.element(at: CGPoint(x: 100, y: 860), in: all) == meters)
    }

    @Test func slackCountsAroundEachKind() {
        // 6 points above the meters block is still the block (its slack is 8 by 6), 7 is not.
        let block = [meters]
        #expect(DeskHitTest.element(at: CGPoint(x: 100, y: 794.5), in: block) == meters)
        #expect(DeskHitTest.element(at: CGPoint(x: 100, y: 793), in: block) == nil)
        #expect(DeskHitTest.element(at: CGPoint(x: 417, y: 850), in: block) == meters)
        #expect(DeskHitTest.element(at: CGPoint(x: 419, y: 850), in: block) == nil)
        // The account label's slack is small: 4 by 2.
        #expect(DeskHitTest.element(at: CGPoint(x: 113, y: 780), in: [account]) == account)
        #expect(DeskHitTest.element(at: CGPoint(x: 115, y: 780), in: [account]) == nil)
        #expect(DeskHitTest.slack(.meetingRow) == CGSize(width: 8, height: 4))
    }

    @Test func priorityPutsMeetingRowsFirst() {
        #expect(DeskHitTest.priority == [.meetingRow, .note, .account, .meterRow, .meters, .watcher, .avIndicators, .nowPlayingNext, .nowPlaying,
                                           .clock, .message, .claudeLine])
        // A row laid over the meters (it should never be, but if it is) takes the click.
        let over = DeskElement(kind: .meetingRow, key: "0", frame: CGRect(x: 60, y: 810, width: 100, height: 20))
        #expect(DeskHitTest.element(at: CGPoint(x: 80, y: 820), in: all + [over]) == over)
        let note = DeskElement(kind: .note, frame: CGRect(x: 60, y: 810, width: 100, height: 20))
        #expect(DeskHitTest.element(at: CGPoint(x: 80, y: 820), in: [meters, session, note]) == note)
    }

    @Test func emptyFramesAndUnclickableElementsNeverTakeAClick() {
        var zero = meters
        zero.frame = .zero
        #expect(DeskHitTest.element(at: .zero, in: [zero]) == nil)
        #expect(DeskHitTest.element(at: CGPoint(x: 4, y: 3), in: [zero]) == nil)
        var noLink = row
        noLink.clickable = false
        #expect(DeskHitTest.element(at: CGPoint(x: 100, y: 950), in: [noLink]) == nil)
        let block = DeskElement(kind: .meetings, frame: CGRect(x: 50, y: 940, width: 300, height: 80), clickable: false)
        #expect(DeskHitTest.element(at: CGPoint(x: 100, y: 1000), in: [block]) == nil)
    }

    @Test func nothingUnderThePointerLetsTheMouseThrough() {
        #expect(DeskHitTest.element(at: CGPoint(x: 900, y: 100), in: all) == nil)
        #expect(DeskHitTest.element(at: CGPoint(x: 100, y: 820), in: []) == nil)
    }

    @Test func metersAndTheirRowsOpenTheLimitMenu() {
        #expect(DeskHitTest.isMeters(meters))
        #expect(DeskHitTest.isMeters(session))
        #expect(!DeskHitTest.isMeters(account))
        #expect(!DeskHitTest.isMeters(row))
        #expect(!DeskHitTest.isMeters(nil))
    }
}

@Suite("Desk elements")
struct DeskElementsTests {
    func input(_ layout: String) -> DeskElements.Input {
        var i = DeskElements.Input()
        i.placed = DeskLayout.placed(layout)
        return i
    }

    @Test func placedFollowsTheLayoutAndTheOlderSwitches() {
        #expect(DeskLayout.placed(DeskLayout.standard) == ["message", "clock", "claude", "meetings"])
        // Item 59: an unknown anchor falls back to the piece's default and an unreadable size is
        // 1, so neither hides a piece; an unknown widget still does.
        #expect(DeskLayout.placed("meters:xx weather:bl clock:tl:x meetings:br") == ["meters", "clock", "meetings"])
        #expect(DeskLayout.placed("claude:bl meters:bl meetings:br", showMeetings: false, showClaude: true)
                == ["claude", "meters"])
        #expect(DeskLayout.placed("claude:bl meters:bl meetings:br", showMeetings: true, showClaude: false)
                == ["meetings"])
    }

    @Test func metersBlockThenItsRowsKeyedByTier() {
        var i = input("meters:bl")
        i.meterTiers = [.fiveHour, .sevenDay]
        i.metersFrame = CGRect(x: 1, y: 2, width: 3, height: 4)
        i.meterRowFrames = [.fiveHour: CGRect(x: 1, y: 2, width: 3, height: 1)]
        let out = DeskElements.build(i)
        #expect(out.map(DeskFrameCheck.name) == ["meters", "meter_row five_hour", "meter_row seven_day"])
        #expect(out[0].frame == i.metersFrame)
        // A row whose frame never arrived is still listed, with an empty frame.
        #expect(out[2].frame == .zero)
    }

    @Test func metersWithoutRowsDrawForTheSignInLineOrASwitchNote() {
        #expect(DeskElements.build(input("meters:bl")).isEmpty)
        var signIn = input("meters:bl")
        signIn.signInNeeded = true
        #expect(DeskElements.build(signIn).map(\.kind) == [.meters])
        var note = input("meters:bl")
        note.switchNote = true
        #expect(DeskElements.build(note).map(\.kind) == [.meters])
        // With the claude line on the desktop the note sits there instead.
        var onLine = input("claude:bl meters:bl")
        onLine.switchNote = true
        #expect(DeskElements.build(onLine).isEmpty)
    }

    @Test func accountOnlyWithTwoAccountsAndTheLineInTheLayout() {
        var i = input("claude:bl")
        #expect(DeskElements.build(i).isEmpty)
        i.hasAccount = true
        #expect(DeskElements.build(i).map(\.kind) == [.account])
        var noLine = input("meters:bl")
        noLine.hasAccount = true
        #expect(DeskElements.build(noLine).isEmpty)
    }

    @Test func meetingRowsKeyedByIndexClickableWithALink() {
        var i = input("meetings:br")
        i.rows = [.init(id: "a", hasLink: true), .init(id: "b", hasLink: false)]
        i.rowFrames = ["a": CGRect(x: 0, y: 0, width: 10, height: 10)]
        let out = DeskElements.build(i)
        #expect(out.map(DeskFrameCheck.name) == ["meetings", "meeting_row 0", "meeting_row 1"])
        #expect(out.map(\.clickable) == [false, true, false])
        #expect(out[1].frame == CGRect(x: 0, y: 0, width: 10, height: 10))
    }

    @Test func theCalendarNoteReplacesTheRows() {
        var i = input("meetings:br")
        i.calendarNote = true
        i.rows = [.init(id: "a", hasLink: true)]
        #expect(DeskElements.build(i).map(\.kind) == [.meetings, .note])
        // Meetings with nothing today: the block alone.
        #expect(DeskElements.build(input("meetings:br")).map(\.kind) == [.meetings])
    }
}

@Suite("Desk frame check")
struct DeskFrameCheckTests {
    let window = CGSize(width: 1512, height: 982)
    let meters = DeskElement(kind: .meters, frame: CGRect(x: 52, y: 700, width: 360, height: 120))
    let session = DeskElement(kind: .meterRow, key: "five_hour", frame: CGRect(x: 52, y: 700, width: 360, height: 50))
    let meetings = DeskElement(kind: .meetings, frame: CGRect(x: 52, y: 840, width: 360, height: 60), clickable: false)
    let row = DeskElement(kind: .meetingRow, key: "0", frame: CGRect(x: 52, y: 840, width: 300, height: 25))

    @Test func aSoundDeskPasses() {
        #expect(DeskFrameCheck.problem([meters, session, meetings, row], window: window) == nil)
        #expect(DeskFrameCheck.problem([], window: window) == nil)
    }

    @Test func anEmptyFrameFails() {
        var dead = meters
        dead.frame = .zero
        #expect(DeskFrameCheck.problem([dead, session], window: window) == "meters frame empty")
        var row = session
        row.frame = CGRect(x: 52, y: 700, width: 0, height: 50)
        #expect(DeskFrameCheck.problem([meters, row], window: window) == "meter_row five_hour frame empty")
    }

    @Test func aFrameOffTheWindowFails() {
        var off = meters
        off.frame.origin.y = 900
        #expect(DeskFrameCheck.problem([off], window: window) == "meters off the Desk window")
        var left = meters
        left.frame.origin.x = -10
        #expect(DeskFrameCheck.problem([left], window: window) == "meters off the Desk window")
        // Half a point of rounding is fine.
        var edge = meters
        edge.frame = CGRect(x: -0.3, y: 0, width: 100, height: 982.4)
        #expect(DeskFrameCheck.problem([edge], window: window) == nil)
    }

    @Test func aRowOutsideItsBlockFails() {
        var stray = session
        stray.frame.origin.y = 600
        #expect(DeskFrameCheck.problem([meters, stray], window: window) == "meter_row five_hour outside meters")
        var strayRow = row
        strayRow.frame.origin.x = 500
        #expect(DeskFrameCheck.problem([meetings, strayRow], window: window) == "meeting_row 0 outside meetings")
    }

    @Test func clickAreasOfDifferentKindsMustNotOverlap() {
        // The account label 4 points above the meters: their slack (2 and 6) overlaps.
        let close = DeskElement(kind: .account, frame: CGRect(x: 52, y: 676, width: 60, height: 20))
        #expect(DeskFrameCheck.problem([close, meters], window: window) == "account overlaps meters")
        // 10 points apart, the areas only meet: no overlap.
        let apart = DeskElement(kind: .account, frame: CGRect(x: 52, y: 672, width: 60, height: 20))
        #expect(DeskFrameCheck.problem([apart, meters], window: window) == nil)
        // A row inside its own block is not an overlap, nor are two rows of one kind.
        let weekly = DeskElement(kind: .meterRow, key: "seven_day", frame: CGRect(x: 52, y: 745, width: 360, height: 50))
        #expect(DeskFrameCheck.problem([meters, session, weekly], window: window) == nil)
        // A meeting row without a link takes no click, so it cannot overlap anything.
        var quiet = row
        quiet.frame.origin.y = 815
        quiet.clickable = false
        let wide = DeskElement(kind: .meetings, frame: CGRect(x: 52, y: 700, width: 360, height: 200), clickable: false)
        #expect(DeskFrameCheck.problem([meters, wide, quiet], window: window) == nil)
        quiet.clickable = true
        #expect(DeskFrameCheck.problem([meters, wide, quiet], window: window) == "meters overlaps meeting_row 0")
    }

    @Test func overlapNeedsMoreThanTheTolerance() {
        let a = CGRect(x: 0, y: 0, width: 10, height: 10)
        #expect(!DeskFrameCheck.overlaps(a, CGRect(x: 10, y: 0, width: 10, height: 10)))
        #expect(!DeskFrameCheck.overlaps(a, CGRect(x: 9.6, y: 0, width: 10, height: 10)))
        #expect(DeskFrameCheck.overlaps(a, CGRect(x: 9, y: 0, width: 10, height: 10)))
        #expect(!DeskFrameCheck.overlaps(a, .null))
    }
}

/// The close pointer watch (item 42): only near a Desk block, never for an empty frame.
@Suite("Desk pointer watch")
struct DeskPointerWatchTests {
    let meters = CGRect(x: 52, y: 742, width: 358, height: 136)

    @Test func nearABlockWithinReach() {
        let r = DeskPointerWatch.reach
        #expect(DeskPointerWatch.near(CGPoint(x: 100, y: 800), frames: [meters]))
        #expect(DeskPointerWatch.near(CGPoint(x: meters.minX - r + 1, y: 800), frames: [meters]))
        #expect(DeskPointerWatch.near(CGPoint(x: 100, y: meters.maxY + r - 1), frames: [.zero, meters]))
        #expect(!DeskPointerWatch.near(CGPoint(x: meters.minX - r - 1, y: 800), frames: [meters]))
        #expect(!DeskPointerWatch.near(CGPoint(x: 900, y: 100), frames: [meters]))
    }

    @Test func emptyFramesNeverStartTheWatch() {
        #expect(!DeskPointerWatch.near(.zero, frames: [.zero, .null]))
        #expect(!DeskPointerWatch.near(CGPoint(x: 10, y: 10), frames: []))
    }

    @Test func theWatchIsShortAndTheReachLargerThanAnyClickSlack() {
        #expect(DeskPointerWatch.interval <= 0.1)
        for kind in DeskElement.Kind.allCases {
            let s = DeskHitTest.slack(kind)
            #expect(DeskPointerWatch.reach > max(s.width, s.height))
        }
    }
}

/// "Clock and message take clicks" (DeskPieceClicks): the clock, the message and the claude line
/// are click areas while the switch is on, and let every click through while it is off.
@Suite("Desk piece clicks")
struct DeskPieceClicksTests {
    let window = CGSize(width: 1512, height: 982)
    let clockFrame = CGRect(x: 52, y: 560, width: 300, height: 150)
    let lineFrame = CGRect(x: 52, y: 720, width: 340, height: 24)
    let messageFrame = CGRect(x: 52, y: 60, width: 700, height: 100)

    /// The standard layout with everything drawn, the switch as given.
    func input(on: Bool) -> DeskElements.Input {
        var i = DeskElements.Input()
        i.placed = DeskLayout.placed(DeskLayout.standard)
        i.piecesTakeClicks = on
        i.clockFrame = clockFrame
        i.messageDrawn = true
        i.messageFrame = messageFrame
        i.claudeLineDrawn = true
        i.claudeLineFrame = lineFrame
        return i
    }

    @Test func onTheThreePiecesAreClickAreas() {
        let out = DeskElements.build(input(on: true))
        #expect(out.map(\.kind) == [.meetings, .clock, .message, .claudeLine])
        #expect(out.first { $0.kind == .clock }?.frame == clockFrame)
        let pieces = out.filter { [.clock, .message, .claudeLine].contains($0.kind) }
        #expect(pieces.map(\.clickable) == [true, true, true])
        #expect(DeskHitTest.element(at: CGPoint(x: 100, y: 600), in: out)?.kind == .clock)
        #expect(DeskHitTest.element(at: CGPoint(x: 100, y: 100), in: out)?.kind == .message)
        #expect(DeskHitTest.element(at: CGPoint(x: 100, y: 730), in: out)?.kind == .claudeLine)
    }

    @Test func offTheyLetEveryClickThrough() {
        let out = DeskElements.build(input(on: false))
        #expect(out.map(\.kind) == [.meetings])
        #expect(DeskHitTest.element(at: CGPoint(x: 100, y: 600), in: out) == nil)
        #expect(DeskHitTest.element(at: CGPoint(x: 100, y: 100), in: out) == nil)
        #expect(DeskHitTest.element(at: CGPoint(x: 100, y: 730), in: out) == nil)
    }

    @Test func onlyWhatIsPlacedAndDrawn() {
        var i = input(on: true)
        i.placed = DeskLayout.placed("meters:bl")
        #expect(DeskElements.build(i).isEmpty)
        var nothing = input(on: true)
        nothing.messageDrawn = false
        nothing.claudeLineDrawn = false
        #expect(DeskElements.build(nothing).map(\.kind) == [.meetings, .clock])
    }

    @Test func theAccountNameKeepsItsOwnClickInsideTheLine() {
        var i = input(on: true)
        i.hasAccount = true
        i.accountFrame = CGRect(x: 52, y: 722, width: 60, height: 20)
        let out = DeskElements.build(i)
        #expect(DeskHitTest.element(at: CGPoint(x: 70, y: 730), in: out)?.kind == .account)
        #expect(DeskHitTest.element(at: CGPoint(x: 300, y: 730), in: out)?.kind == .claudeLine)
        // Nested in the line, not an overlap; outside the line, a problem.
        var sound = out
        sound.removeAll { $0.kind == .meetings }
        #expect(DeskFrameCheck.problem(sound, window: window) == nil)
        var stray = sound
        if let a = stray.firstIndex(where: { $0.kind == .account }) { stray[a].frame.origin.y = 500 }
        #expect(DeskFrameCheck.problem(stray, window: window) == "account outside claude_line")
    }

    @Test func soundFramesWithTheMetersBelowTheLine() {
        // The standard bottom left: the clock, the claude line, then the meters a column gap (10)
        // below the line. The slack (2 under the line, 6 over the meters) only comes close.
        let meters = DeskElement(kind: .meters, frame: CGRect(x: 52, y: 754, width: 358, height: 120))
        let clock = DeskElement(kind: .clock, frame: clockFrame)
        let line = DeskElement(kind: .claudeLine, frame: lineFrame)
        let message = DeskElement(kind: .message, frame: messageFrame)
        #expect(DeskFrameCheck.problem([message, clock, line, meters], window: window) == nil)
        // A clock ten points lower runs into the line.
        var low = clock
        low.frame.origin.y = 570
        #expect(DeskFrameCheck.problem([low, line], window: window) == "clock overlaps claude_line")
    }

    @Test func menus() {
        #expect(DeskPieceClicks.menuItem(.clock)?.title == "Desk Look Settings…")
        #expect(DeskPieceClicks.menuItem(.clock)?.section == .deskLook)
        #expect(DeskPieceClicks.menuItem(.message)?.title == "Edit Messages…")
        #expect(DeskPieceClicks.menuItem(.message)?.section == .message)
        #expect(DeskPieceClicks.menuItem(.claudeLine) == nil)
        for kind in [DeskElement.Kind.clock, .message, .claudeLine] {
            #expect(DeskHitTest.hasSharedMenu(DeskElement(kind: kind, frame: .zero)))
            #expect(DeskHitTest.hasMenu(DeskElement(kind: kind, frame: .zero)))
            #expect(!DeskHitTest.isMeters(DeskElement(kind: kind, frame: .zero)))
        }
    }

    @Test func theSwitchIsOnByDefaultAndRoundTrips() {
        let store = MemoryDefaults()
        #expect(DeskPieceClicks.isOn(in: store))
        DeskPieceClicks.set(false, in: store)
        #expect(!DeskPieceClicks.isOn(in: store))
        #expect(store.values[DeskPieceClicks.key] as? Bool == false)
        DeskPieceClicks.set(true, in: store)
        #expect(DeskPieceClicks.isOn(in: store))
        #expect(DeskPieceClicks.title == "Clock and message take clicks")
        #expect(DeskPieceClicks.caption == "On: two-finger click them for Sanduhr's menu. Desktop icons right beneath them can't be clicked there while it's on.")
    }

    @Test func aDragFromElsewhereNeverTurnsThePlatesOn() {
        // No button down: the window follows the pointer.
        #expect(DeskPointerDrag.takesMouse(wanted: true, buttonDown: false, takingNow: false))
        #expect(!DeskPointerDrag.takesMouse(wanted: false, buttonDown: false, takingNow: true))
        // A file dragged from the desktop onto the clock: the window keeps ignoring the mouse.
        #expect(!DeskPointerDrag.takesMouse(wanted: true, buttonDown: true, takingNow: false))
        // A press on a piece itself keeps the window until the button comes up.
        #expect(DeskPointerDrag.takesMouse(wanted: false, buttonDown: true, takingNow: true))
    }
}
