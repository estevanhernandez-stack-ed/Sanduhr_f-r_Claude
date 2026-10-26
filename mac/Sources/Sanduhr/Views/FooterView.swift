import SwiftUI

/// Shows last-updated timestamp + mode on the left, Sonnet shortcut on the
/// right. sanduhr.py:353-363. The shortcut waits for numbers: signed out or before the first
/// fetch the footer offers nothing to act on (Settings v2, F23).
struct FooterView: View {
    @Bindable var vm: UsageViewModel

    var body: some View {
        let t = vm.theme.palette
        HStack {
            Text(vm.footerText())
                .font(.app(size: 9, design: .rounded))
                .foregroundStyle(t.textMuted)
                .padding(.leading, 10)
                .id(vm.countdownTick)

            Spacer()

            if FooterView.showsPlan(plan: vm.shownPlan, hasNumbers: vm.shownUsage != nil, needsSignIn: vm.status.needsSignIn),
               let plan = vm.shownPlan {
                PlanCapsule(plan: plan, color: t.accent)
            }

            if FooterView.showsModelLink(hasNumbers: vm.shownUsage != nil, needsSignIn: vm.status.needsSignIn) {
                Link("Use Sonnet",
                     destination: URL(string: "https://claude.ai/new?model=claude-sonnet-4-6")!)
                    .font(.app(size: 9, weight: .semibold, design: .rounded))
                    .foregroundStyle(t.accent)
                    .padding(.trailing, 10)
                    .padding(.leading, 4)
            }
        }
        .frame(height: 24)
        .background(
            LinearGradient(
                colors: [t.footerBg.opacity(0.95), t.footerBg],
                startPoint: .top, endPoint: .bottom)
            .opacity(Chrome.opacity))
    }

    /// The plan capsule shows only with a plan, numbers on the widget and a working sign-in.
    static func showsPlan(plan: PlanBadge?, hasNumbers: Bool, needsSignIn: Bool) -> Bool {
        plan != nil && hasNumbers && !needsSignIn
    }

    /// Use Sonnet shows only with numbers on the widget and a working sign-in.
    static func showsModelLink(hasNumbers: Bool, needsSignIn: Bool) -> Bool { hasNumbers && !needsSignIn }
}

/// The subscription name in a small capsule (the Windows footer's #22FFFFFF Border). The tooltip
/// carries the riffs; VoiceOver reads the name only. With two or more riffs and Reduce Motion off,
/// a `.periodic` TimelineView (4 s, this Text only) advances the tooltip; otherwise nothing runs.
/// A hidden or offscreen widget is not redrawn, so the schedule costs nothing there.
private struct PlanCapsule: View {
    let plan: PlanBadge
    let color: Color

    var body: some View {
        Group {
            if PlanRiffs.rotates(riffCount: plan.riffs.count, reduceMotion: AccountSwitchFade.reduceMotion) {
                TimelineView(.periodic(from: Date(), by: PlanRiffs.interval)) { ctx in
                    capsule(tick: Int(ctx.date.timeIntervalSinceReferenceDate / PlanRiffs.interval))
                }
            } else {
                capsule(tick: 0)
            }
        }
        .padding(.trailing, 4)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("Plan: \(plan.name)")
    }

    private func capsule(tick: Int) -> some View {
        let index = PlanRiffs.index(atTick: tick, count: plan.riffs.count, reduceMotion: AccountSwitchFade.reduceMotion)
        return Text(plan.name)
            .font(.app(size: 9, weight: .semibold, design: .rounded))
            .foregroundStyle(color)
            .padding(.horizontal, 6)
            .padding(.vertical, 2)
            .background(Capsule().fill(Color.white.opacity(0.13)))
            .help(PlanRiffs.tooltip(name: plan.name, riffs: plan.riffs, index: index))
    }
}
