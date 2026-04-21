import SwiftUI

/// Minimalist Pomodoro-style timer overlay containing a glowing circular
/// progress ring and the time remaining.
class HourglassEngine: ObservableObject {
    let gw = 31
    let gh = 31
    var cx: Int { gw / 2 }
    var cy: Int { gh / 2 }
    
    var mask: [[Bool]] = []
    var grid: [[Bool]] = []
    var totalSand = 0
    var sandPassed = 0
    var durationSecs = 0
    
    var lastUpdate: Date?
    
    init() {}
    
    func reset(duration: Int) {
        self.durationSecs = duration
        self.totalSand = 0
        self.sandPassed = 0
        self.lastUpdate = nil
        
        self.mask = Array(repeating: Array(repeating: false, count: gw), count: gh)
        self.grid = Array(repeating: Array(repeating: false, count: gw), count: gh)
        
        for y in 0..<gh {
            for x in 0..<gw {
                let dy = abs(y - cy)
                let dx = abs(x - cx)
                if dx <= dy + 1 {
                    mask[y][x] = true
                    if y < cy {
                        grid[y][x] = true
                        totalSand += 1
                    }
                }
            }
        }
    }
    
    /// Progress sand one frame. `elapsedSecs` is wall-clock elapsed time
    /// since `start()` — a Double, not the integer-second countdown —
    /// so the grain flow advances sub-second instead of stuttering once
    /// per second. Parity fix with Windows commit 455e58d.
    func tickPhysics(elapsedSecs: Double) {
        if durationSecs == 0 { return }
        let expectedPassed = Int((elapsedSecs / Double(durationSecs)) * Double(totalSand))
        
        for y in stride(from: gh - 2, through: 0, by: -1) {
            for x in 0..<gw {
                if !grid[y][x] { continue }
                
                if y == cy - 1 && x == cx {
                    if sandPassed >= expectedPassed {
                        continue
                    }
                }
                
                if mask[y+1][x] && !grid[y+1][x] {
                    grid[y][x] = false
                    grid[y+1][x] = true
                    if y == cy - 1 && x == cx { sandPassed += 1 }
                } else {
                    let dirs = Bool.random() ? [-1, 1] : [1, -1]
                    for dx in dirs {
                        let nx = x + dx
                        if nx >= 0 && nx < gw && mask[y+1][nx] && !grid[y+1][nx] {
                            grid[y][x] = false
                            grid[y+1][nx] = true
                            if y == cy - 1 && x == cx { sandPassed += 1 }
                            break
                        }
                    }
                }
            }
        }
    }
    
    func draw(context: inout GraphicsContext, size: CGSize, palette: Theme.Palette) {
        let cw = size.width / CGFloat(gw)
        let ch = size.height / CGFloat(gh)
        
        var bgPath = Path()
        var fgPath = Path()
        
        for y in 0..<gh {
            for x in 0..<gw {
                if !mask[y][x] { continue }
                let rect = CGRect(x: CGFloat(x)*cw, y: CGFloat(y)*ch, width: cw - 0.5, height: ch - 0.5)
                
                if grid[y][x] {
                    fgPath.addRect(rect)
                } else if x != cx || y != cy {
                    bgPath.addRect(rect)
                }
            }
        }
        
        context.fill(bgPath, with: .color(palette.barBg.opacity(0.4)))
        context.fill(fgPath, with: .color(palette.accent))
    }
}

struct FocusView: View {
    @Bindable var vm: UsageViewModel
    let onComplete: () -> Void

    @State private var remainingSeconds: Int = 0
    @State private var isRunning = false
    @State private var focusMinutes: Int = 25
    @State private var startDate: Date?
    @StateObject private var engine = HourglassEngine()
    
    let timer = Timer.publish(every: 1.0, on: .main, in: .common).autoconnect()

    var body: some View {
        let t = vm.theme.palette

        VStack(spacing: 8) {
            Spacer()
            
            ZStack {
                if isRunning {
                    TimelineView(.animation) { timeline in
                        Canvas { context, size in
                            engine.draw(context: &context, size: size, palette: t)
                        }
                        .onChange(of: timeline.date) { _, newDate in
                            guard let start = startDate else { return }
                            let throttle: TimeInterval = 1.0 / 30.0
                            if let last = engine.lastUpdate,
                               newDate.timeIntervalSince(last) < throttle {
                                return
                            }
                            engine.lastUpdate = newDate
                            engine.tickPhysics(elapsedSecs: newDate.timeIntervalSince(start))
                        }
                    }
                    .frame(width: 140, height: 140)
                    .transition(.opacity)
                } else {
                    Circle()
                        .stroke(t.barBg.opacity(0.3), lineWidth: 2)
                        .frame(width: 140, height: 140)
                }

                VStack(spacing: 6) {
                    Text(timeString)
                        .font(.system(size: 32, weight: .bold, design: t.numericFontDesign))
                        .foregroundStyle(t.text)
                        .monospacedDigit()
                    
                    Text("Deep Work")
                        .font(.caption)
                        .foregroundStyle(t.textDim)
                }
            }
            .frame(height: 150)
            
            if !isRunning {
                HStack {
                    Spacer()
                    Text("Minutes:")
                        .font(.system(size: 11))
                        .foregroundStyle(t.text)
                    TextField("", value: $focusMinutes, format: .number)
                        .textFieldStyle(.roundedBorder)
                        .frame(width: 50)
                    Button("Start Cooldown") {
                        start()
                    }
                    .buttonStyle(.plain)
                    .padding(.horizontal, 10)
                    .padding(.vertical, 4)
                    .background(t.glass)
                    .clipShape(RoundedRectangle(cornerRadius: 6))
                    .foregroundStyle(t.text)
                    Spacer()
                }
                .padding(.top, 10)
                .transition(.opacity)
            }
            
            Spacer()
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .padding(.vertical, 10)
        .overlay(alignment: .topTrailing) {
            // Always-visible exit button. Deep Work had no way out without
            // Escape; broken Touch Bar Macs got stranded. Click to dismiss
            // whether the timer's running or paused.
            Button {
                if isRunning { isRunning = false }
                onComplete()
            } label: {
                Image(systemName: "xmark.circle.fill")
                    .symbolRenderingMode(.palette)
                    .foregroundStyle(t.textDim, t.glass.opacity(0.6))
                    .font(.system(size: 18))
            }
            .buttonStyle(.plain)
            .padding(8)
            .help("Exit Deep Work")
        }
        .onReceive(timer) { _ in
            if isRunning {
                if remainingSeconds > 0 {
                    remainingSeconds -= 1
                } else {
                    isRunning = false
                    onComplete()
                }
            }
        }
    }
    
    private func start() {
        remainingSeconds = max(1, focusMinutes * 60)
        engine.reset(duration: remainingSeconds)
        startDate = Date()
        withAnimation(.easeInOut(duration: 0.3)) {
            isRunning = true
        }
    }

    private var timeString: String {
        if !isRunning { return String(format: "%02d:00", focusMinutes) }
        let m = remainingSeconds / 60
        let s = remainingSeconds % 60
        return String(format: "%02d:%02d", m, s)
    }
}
