import SwiftUI

/// Minimalist Pomodoro-style timer overlay containing a glowing circular
/// progress ring and the time remaining.
struct FocusView: View {
    let focusMinutes: Int
    @Bindable var vm: UsageViewModel
    let onComplete: () -> Void

    @State private var remainingSeconds: Int
    private let timer = Timer.publish(every: 1.0, on: .main, in: .common).autoconnect()

    init(focusMinutes: Int, vm: UsageViewModel, onComplete: @escaping () -> Void) {
        self.focusMinutes = focusMinutes
        self.vm = vm
        self.onComplete = onComplete
        self._remainingSeconds = State(initialValue: focusMinutes * 60)
    }

    var body: some View {
        let t = vm.theme.palette
        let durationSeconds = max(1, focusMinutes * 60)
        let progress = CGFloat(remainingSeconds) / CGFloat(durationSeconds)

        VStack {
            Spacer()
            ZStack {
                // Background Track
                Circle()
                    .stroke(t.barBg, lineWidth: 6)
                    .frame(width: 140, height: 140)

                // Neon Progress Arc
                Circle()
                    .trim(from: 0.0, to: progress)
                    .stroke(
                        t.accent,
                        style: StrokeStyle(lineWidth: 6, lineCap: .round)
                    )
                    .frame(width: 140, height: 140)
                    .rotationEffect(.degrees(-90))
                    // Soft Neon Glow
                    .shadow(color: t.accent.opacity(t.accentBloom.alpha),
                            radius: t.accentBloom.blur)

                VStack(spacing: 4) {
                    Text(timeString)
                        .font(.system(size: 28, weight: .bold, design: t.numericFontDesign))
                        .foregroundStyle(t.text)
                        // Explicit mono spacing so numbers don't jitter
                        .monospacedDigit()
                    
                    Text("Deep Work")
                        .font(.caption)
                        .foregroundStyle(t.textDim)
                }
            }
            Spacer()
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .padding(.vertical, 20)
        .onReceive(timer) { _ in
            if remainingSeconds > 0 {
                remainingSeconds -= 1
            } else {
                onComplete()
            }
        }
    }

    private var timeString: String {
        let m = remainingSeconds / 60
        let s = remainingSeconds % 60
        return String(format: "%02d:%02d", m, s)
    }
}
