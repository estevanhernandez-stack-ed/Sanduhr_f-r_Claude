import SwiftUI

/// Snake Game Engine representing the game loop and state.
class SnakeGameEngine: ObservableObject {
    @Published var snake: [(Int, Int)] = []
    @Published var food: (Int, Int) = (0, 0)
    @Published var isGameOver: Bool = false
    @Published var score: Int = 0
    
    let gridSize = 20
    private var direction: (Int, Int) = (0, -1)
    private var nextDirection: (Int, Int) = (0, -1)
    private var timer: Timer?

    func startGame() {
        score = 0
        snake = [(10, 10), (10, 11), (10, 12)]
        direction = (0, -1)
        nextDirection = (0, -1)
        isGameOver = false
        food = spawnFood()
        
        timer?.invalidate()
        timer = Timer.scheduledTimer(withTimeInterval: 0.12, repeats: true) { [weak self] _ in
            self?.tick()
        }
    }
    
    func stopGame() {
        timer?.invalidate()
        timer = nil
    }

    func changeDirection(_ dx: Int, _ dy: Int) {
        if direction.0 != -dx || direction.1 != -dy {
            nextDirection = (dx, dy)
        }
    }

    private func tick() {
        guard !isGameOver else { return }
        direction = nextDirection
        
        let head = snake[0]
        let newHead = (head.0 + direction.0, head.1 + direction.1)
        
        // Wall collision
        if newHead.0 < 0 || newHead.0 >= gridSize || newHead.1 < 0 || newHead.1 >= gridSize {
            isGameOver = true
            return
        }
        
        // Self collision
        if snake.contains(where: { $0 == newHead }) {
            isGameOver = true
            return
        }
        
        snake.insert(newHead, at: 0)
        
        if newHead == food {
            score += 10
            food = spawnFood()
        } else {
            snake.removeLast()
        }
    }
    
    private func spawnFood() -> (Int, Int) {
        while true {
            let f = (Int.random(in: 0..<gridSize), Int.random(in: 0..<gridSize))
            if !snake.contains(where: { $0 == f }) {
                return f
            }
        }
    }
}

/// The overlay view drawing the game.
struct SnakeGameOverlay: View {
    @StateObject private var engine = SnakeGameEngine()
    @AppStorage("snakeHighScore") private var snakeHighScore = 0
    @Bindable var vm: UsageViewModel
    let onExit: () -> Void
    
    var body: some View {
        let t = vm.theme.palette
        ZStack {
            // Dim background
            t.bg.opacity(0.9)
                .edgesIgnoringSafeArea(.all)
                
            // Canvas drawing
            Canvas { context, size in
                let boxSize = min(size.width, size.height) - 40
                let cellSize = boxSize / CGFloat(engine.gridSize)
                let offsetX = (size.width - boxSize) / 2
                let offsetY = (size.height - boxSize) / 2
                
                // Draw Border
                context.stroke(
                    Path(CGRect(x: offsetX, y: offsetY, width: boxSize, height: boxSize)),
                    with: .color(t.border),
                    lineWidth: 2
                )
                
                // Draw Score
                let scoreText = Text("Score: \(engine.score)").font(.system(size: 9, weight: .bold)).foregroundColor(t.textDim)
                let bestText = Text("Best: \(snakeHighScore)").font(.system(size: 9, weight: .bold)).foregroundColor(t.textDim)
                context.draw(scoreText, at: CGPoint(x: offsetX + 20, y: offsetY - 10))
                context.draw(bestText, at: CGPoint(x: offsetX + boxSize - 20, y: offsetY - 10))
                
                // Draw Food
                let fx = offsetX + CGFloat(engine.food.0) * cellSize
                let fy = offsetY + CGFloat(engine.food.1) * cellSize
                context.fill(
                    Path(ellipseIn: CGRect(x: fx + 2, y: fy + 2, width: cellSize - 4, height: cellSize - 4)),
                    with: .color(t.text)
                )
                
                // Draw Snake
                for (index, segment) in engine.snake.enumerated() {
                    let sx = offsetX + CGFloat(segment.0) * cellSize
                    let sy = offsetY + CGFloat(segment.1) * cellSize
                    
                    let alpha = max(0.4, 1.0 - (Double(index) / Double(engine.snake.count)))
                    context.fill(
                        Path(roundedRect: CGRect(x: sx + 1, y: sy + 1, width: cellSize - 2, height: cellSize - 2), cornerRadius: 2),
                        with: .color(t.accent.opacity(alpha))
                    )
                }
                
                if engine.isGameOver {
                    let text = Text("GAME OVER\nPress Space to Restart\nEsc to Exit")
                        .font(.system(size: 14, weight: .bold))
                        .foregroundColor(t.textDim)
                    
                    context.draw(text, at: CGPoint(x: size.width / 2, y: size.height / 2))
                }
            }
        }
        // Local Keyboard Focus capture
        .focusable(true)
        .onKeyPress(.leftArrow) { engine.changeDirection(-1, 0); return .handled }
        .onKeyPress(.rightArrow) { engine.changeDirection(1, 0); return .handled }
        .onKeyPress(.upArrow) { engine.changeDirection(0, -1); return .handled }
        .onKeyPress(.downArrow) { engine.changeDirection(0, 1); return .handled }
        .onKeyPress(.space) {
            if engine.isGameOver { engine.startGame() }
            return .handled
        }
        .onKeyPress(.escape) {
            onExit()
            return .handled
        }
        .onAppear {
            engine.startGame()
        }
        .onChange(of: engine.score) { _, newScore in
            if newScore > snakeHighScore {
                snakeHighScore = newScore
            }
        }
        .onDisappear {
            engine.stopGame()
        }
    }
}
