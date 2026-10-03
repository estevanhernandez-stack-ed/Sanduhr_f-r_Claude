import Testing
@testable import Sanduhr

@Suite("When the widget shows")
struct WidgetVisibilityTests {
    let events: [WidgetVisibilityEvent] = [.launch, .deskChanged, .signedIn, .signedOut, .choiceChanged]

    func rule(_ setting: WidgetVisibility, deskOn: Bool, key: Bool = true,
              _ event: WidgetVisibilityEvent) -> Bool? {
        WidgetVisibilityRule.shouldShow(setting: setting, deskOn: deskOn, hasSessionKey: key, event: event)
    }

    @Test func alwaysNeverMovesTheWidgetOnceChosen() {
        for event in events where event != .choiceChanged {
            for deskOn in [false, true] {
                for key in [false, true] {
                    #expect(rule(.always, deskOn: deskOn, key: key, event) == nil)
                }
            }
        }
    }

    @Test func choosingAlwaysShowsAWidgetAnEarlierChoiceHid() {
        for deskOn in [false, true] {
            for key in [false, true] {
                #expect(rule(.always, deskOn: deskOn, key: key, .choiceChanged) == true)
            }
        }
        // Signed in under "Hidden while Desk is on", tucked away, then "Always shown": it comes
        // back, and a later launch keeps it (panelHidden is now false).
        var showing = WidgetVisibilityRule.resolve(showing: true, setting: .whileDeskOff, deskOn: true,
                                                   hasSessionKey: true, event: .signedIn)
        #expect(showing == false)
        showing = WidgetVisibilityRule.resolve(showing: showing, setting: .always, deskOn: true,
                                               hasSessionKey: true, event: .choiceChanged)
        #expect(showing)
        showing = WidgetVisibilityRule.resolve(showing: showing, setting: .always, deskOn: true,
                                               hasSessionKey: true, event: .launch)
        #expect(showing)
    }

    @Test func whileDeskOffFollowsDesk() {
        for event in events {
            #expect(rule(.whileDeskOff, deskOn: true, event) == false)
            #expect(rule(.whileDeskOff, deskOn: false, event) == true)
        }
    }

    @Test func onRequestHidesAtEveryEvent() {
        for event in events {
            #expect(rule(.onRequest, deskOn: true, event) == false)
            #expect(rule(.onRequest, deskOn: false, event) == false)
        }
    }

    @Test func withoutASessionKeyTheWidgetShowsForSignIn() {
        for setting in WidgetVisibility.allCases {
            for event in events {
                for deskOn in [false, true] {
                    // Never hidden by the rule; shown outright by the two new choices.
                    #expect(rule(setting, deskOn: deskOn, key: false, event) != false)
                    if setting != .always { #expect(rule(setting, deskOn: deskOn, key: false, event) == true) }
                }
            }
        }
    }

    @Test func launchWithAlwaysKeepsTheLastState() {
        // panelHidden decides, as before this setting existed.
        #expect(WidgetVisibilityRule.resolve(showing: false, setting: .always, deskOn: true,
                                             hasSessionKey: true, event: .launch) == false)
        #expect(WidgetVisibilityRule.resolve(showing: true, setting: .always, deskOn: true,
                                             hasSessionKey: true, event: .launch) == true)
    }

    @Test func onRequestStaysHiddenAcrossLaunches() {
        // Shown by hand, then quit: the next launch hides it again.
        #expect(WidgetVisibilityRule.resolve(showing: true, setting: .onRequest, deskOn: false,
                                             hasSessionKey: true, event: .launch) == false)
        #expect(WidgetVisibilityRule.resolve(showing: false, setting: .onRequest, deskOn: true,
                                             hasSessionKey: true, event: .launch) == false)
    }

    @Test func aManualShowOrHideLastsUntilTheNextEvent() {
        // Hidden while Desk is on, Desk on: hidden at launch.
        var showing = WidgetVisibilityRule.resolve(showing: true, setting: .whileDeskOff, deskOn: true,
                                                   hasSessionKey: true, event: .launch)
        #expect(showing == false)
        // Shown from the menu: only events move it, so it stays shown. Desk goes off (shown
        // still), then on: the choice takes over and hides it.
        showing = true
        showing = WidgetVisibilityRule.resolve(showing: showing, setting: .whileDeskOff, deskOn: false,
                                               hasSessionKey: true, event: .deskChanged)
        #expect(showing)
        showing = WidgetVisibilityRule.resolve(showing: showing, setting: .whileDeskOff, deskOn: true,
                                               hasSessionKey: true, event: .deskChanged)
        #expect(showing == false)
        // Hidden by hand while Desk is off, then Desk on: still hidden; Desk off: shown.
        showing = false
        showing = WidgetVisibilityRule.resolve(showing: showing, setting: .whileDeskOff, deskOn: true,
                                               hasSessionKey: true, event: .deskChanged)
        #expect(showing == false)
        showing = WidgetVisibilityRule.resolve(showing: showing, setting: .whileDeskOff, deskOn: false,
                                               hasSessionKey: true, event: .deskChanged)
        #expect(showing)
    }

    @Test func savedFallsBackToAlways() {
        let d = MemoryDefaults()
        #expect(WidgetVisibility.saved(in: d) == .always)
        d.set("sometimes", forKey: WidgetVisibility.key)
        #expect(WidgetVisibility.saved(in: d) == .always)
        d.set(WidgetVisibility.onRequest.rawValue, forKey: WidgetVisibility.key)
        #expect(WidgetVisibility.saved(in: d) == .onRequest)
    }

    @Test func aKeyThatNeverFetchedIsStillSigningIn() {
        let d = MemoryDefaults()
        // Saved but never worked (or an update from 2.3.2, which wrote no marker): still signing in.
        #expect(SignInGate.awaitingSignIn(fresh: false, hasSessionKey: true, in: d))
        #expect(SignInGate.awaitingSignIn(fresh: false, hasSessionKey: false, in: d))
        SignInGate.record(fetched: true, needsSignIn: false, in: d)
        #expect(!SignInGate.awaitingSignIn(fresh: false, hasSessionKey: true, in: d))
        // The marker never stands in for a key, or for a brand-new install's first launch.
        #expect(SignInGate.awaitingSignIn(fresh: false, hasSessionKey: false, in: d))
        #expect(SignInGate.awaitingSignIn(fresh: true, hasSessionKey: true, in: d))
    }

    @Test func signOutOrARefusedKeyClearsTheMarker() {
        let d = MemoryDefaults()
        SignInGate.record(fetched: true, needsSignIn: false, in: d)
        // A refresh in progress or a network error leaves it.
        SignInGate.record(fetched: false, needsSignIn: false, in: d)
        #expect(d.bool(forKey: SignInGate.key))
        // Sign Out, or claude.ai refusing the key: the next launch waits for a good fetch.
        SignInGate.record(fetched: false, needsSignIn: true, in: d)
        #expect(d.object(forKey: SignInGate.key) == nil)
        #expect(SignInGate.awaitingSignIn(fresh: false, hasSessionKey: true, in: d))
        // Signing in again sets it.
        SignInGate.record(fetched: true, needsSignIn: false, in: d)
        #expect(!SignInGate.awaitingSignIn(fresh: false, hasSessionKey: true, in: d))
    }

    @Test func anUpgraderWithAWorkingKeyShowsOnceThenFollowsTheChoice() {
        let d = MemoryDefaults()
        // 2.3.2, Desk on, hidden while Desk is on, key works: first launch shows for sign-in.
        var awaiting = SignInGate.awaitingSignIn(fresh: false, hasSessionKey: true, in: d)
        #expect(WidgetVisibilityRule.resolve(showing: false, setting: .whileDeskOff, deskOn: true,
                                             hasSessionKey: !awaiting, event: .launch))
        // The first fetch succeeds: the choice hides it.
        SignInGate.record(fetched: true, needsSignIn: false, in: d)
        awaiting = false
        #expect(WidgetVisibilityRule.shouldShow(setting: .whileDeskOff, deskOn: true,
                                                hasSessionKey: !awaiting, event: .signedIn) == false)
        // The next launch hides it straight away.
        awaiting = SignInGate.awaitingSignIn(fresh: false, hasSessionKey: true, in: d)
        #expect(WidgetVisibilityRule.resolve(showing: false, setting: .whileDeskOff, deskOn: true,
                                             hasSessionKey: !awaiting, event: .launch) == false)
    }

    @Test func aSavedChoiceMarksAnExistingInstall() {
        let s = ScratchDefaults(), widget = MemoryDefaults()
        widget.set(WidgetVisibility.always.rawValue, forKey: WidgetVisibility.key)
        #expect(s.firstRun(widget: widget) == .existing)
        #expect(WidgetVisibility.saved(in: widget) == .always)
    }
}
