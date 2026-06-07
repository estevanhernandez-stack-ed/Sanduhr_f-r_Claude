namespace Sanduhr.Core;

/// <summary>
/// The cooldown snake game logic — ported from <c>game.py</c>'s
/// <c>SnakeOverlay</c>, with rendering and key/timer plumbing left to the App's
/// <c>CooldownGame</c> overlay. Pure and unit-tested: grid, movement, food,
/// wall + self collision, score, high score, and the speed-up-on-eat curve.
///
/// <para>A cell is <c>(x, y)</c> in grid units. <see cref="Snake"/>[0] is the
/// head. Direction is a unit step; up is <c>(0, -1)</c> (screen coords), matching
/// the Python build.</para>
/// </summary>
public sealed class SnakeGame
{
    /// <summary>Starting tick interval in ms (the Python <c>QTimer</c> 120 ms).</summary>
    public const int StartIntervalMs = 120;

    /// <summary>Floor the speed-up can reach (Python <c>max(60, …)</c>).</summary>
    public const int MinIntervalMs = 60;

    private readonly Random _rng;
    private readonly List<(int X, int Y)> _snake = new();

    /// <summary>Square grid edge length in cells (20, matching <c>game.py</c>).</summary>
    public int GridSize { get; }

    /// <summary>The snake body, head-first. Read-only view for the renderer.</summary>
    public IReadOnlyList<(int X, int Y)> Snake => _snake;

    /// <summary>Current food cell.</summary>
    public (int X, int Y) Food { get; private set; }

    /// <summary>Current score (10 per food).</summary>
    public int Score { get; private set; }

    /// <summary>Best score seen (seeded from settings, raised on a new best).</summary>
    public int HighScore { get; private set; }

    /// <summary><c>true</c> once the snake hit a wall or itself.</summary>
    public bool GameOver { get; private set; }

    /// <summary>Current tick interval in ms — shrinks 5% per food down to
    /// <see cref="MinIntervalMs"/>. The overlay reads this to retime its loop.</summary>
    public int IntervalMs { get; private set; }

    /// <summary>Heading applied on the next <see cref="Step"/>.</summary>
    private (int X, int Y) _dir;
    private (int X, int Y) _nextDir;

    /// <param name="highScore">Persisted best to seed from (settings.json
    /// <c>snake_high_score</c>).</param>
    /// <param name="rng">Injectable RNG for deterministic food placement in tests.</param>
    /// <param name="gridSize">Edge length in cells; defaults to 20 (the locked value).</param>
    public SnakeGame(int highScore = 0, Random? rng = null, int gridSize = 20)
    {
        _rng = rng ?? Random.Shared;
        GridSize = gridSize;
        HighScore = highScore;
        Reset();
    }

    /// <summary>Raised when <see cref="Score"/> overtakes <see cref="HighScore"/> —
    /// the overlay persists the new best.</summary>
    public event Action<int>? HighScoreReached;

    /// <summary>Reset to the opening position (ports <c>_reset_game</c>): a
    /// 3-cell snake heading up, fresh food, score 0, speed reset.</summary>
    public void Reset()
    {
        _snake.Clear();
        _snake.Add((10, 10));
        _snake.Add((10, 11));
        _snake.Add((10, 12));
        _dir = (0, -1);
        _nextDir = (0, -1);
        Score = 0;
        GameOver = false;
        IntervalMs = StartIntervalMs;
        Food = SpawnFood();
    }

    // -- direction (180-reverse guarded, ports keyPressEvent) ------------------

    /// <summary>Queue an upward turn unless currently heading down.</summary>
    public void TurnUp() { if (_dir != (0, 1)) _nextDir = (0, -1); }

    /// <summary>Queue a downward turn unless currently heading up.</summary>
    public void TurnDown() { if (_dir != (0, -1)) _nextDir = (0, 1); }

    /// <summary>Queue a left turn unless currently heading right.</summary>
    public void TurnLeft() { if (_dir != (1, 0)) _nextDir = (-1, 0); }

    /// <summary>Queue a right turn unless currently heading left.</summary>
    public void TurnRight() { if (_dir != (-1, 0)) _nextDir = (1, 0); }

    /// <summary>
    /// Advance one tick — ports <c>_game_loop</c>. No-op once <see cref="GameOver"/>.
    /// Applies the queued direction, moves the head, ends the game on a wall or
    /// self collision, eats food (score + speed-up + high-score), else drops the tail.
    /// </summary>
    public void Step()
    {
        if (GameOver)
            return;

        _dir = _nextDir;
        var (hx, hy) = _snake[0];
        var newHead = (X: hx + _dir.X, Y: hy + _dir.Y);

        // Wall collision.
        if (newHead.X < 0 || newHead.X >= GridSize || newHead.Y < 0 || newHead.Y >= GridSize)
        {
            GameOver = true;
            return;
        }

        // Self collision.
        if (_snake.Contains(newHead))
        {
            GameOver = true;
            return;
        }

        _snake.Insert(0, newHead);

        if (newHead == Food)
        {
            Food = SpawnFood();
            Score += 10;
            if (Score > HighScore)
            {
                HighScore = Score;
                HighScoreReached?.Invoke(HighScore);
            }
            // Increase speed slightly toward the floor.
            IntervalMs = Math.Max(MinIntervalMs, (int)(IntervalMs * 0.95));
        }
        else
        {
            _snake.RemoveAt(_snake.Count - 1);
        }
    }

    private (int X, int Y) SpawnFood()
    {
        while (true)
        {
            var f = (X: _rng.Next(0, GridSize), Y: _rng.Next(0, GridSize));
            if (!_snake.Contains(f))
                return f;
        }
    }

    // -- test seam ------------------------------------------------------------

    /// <summary>Place the snake + heading directly so collision tests can set up a
    /// deterministic about-to-crash state (mirrors the Python tests poking
    /// <c>_snake</c> / <c>_dir</c> / <c>_next_dir</c>).</summary>
    internal void ConfigureForTest(IReadOnlyList<(int X, int Y)> snake, (int X, int Y) dir, (int X, int Y) nextDir)
    {
        _snake.Clear();
        _snake.AddRange(snake);
        _dir = dir;
        _nextDir = nextDir;
        GameOver = false;
    }

    /// <summary>Pin the food to a known cell so eating is deterministic in tests.</summary>
    internal void SetFoodForTest((int X, int Y) cell) => Food = cell;
}
