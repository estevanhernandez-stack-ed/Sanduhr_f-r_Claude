using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// Parity tests for the cooldown snake logic — ported from the snake cases in
/// <c>test_focus_physics.py</c> (<c>SnakeOverlay</c>). Wall + self collision end
/// the game; eating food scores, speeds up, and raises the high score.
/// </summary>
public class SnakeGameTests
{
    [Fact]
    public void Constructs_in_a_known_state()
    {
        // Ports test_snake_overlay_constructs.
        var game = new SnakeGame(highScore: 0, rng: new Random(0));
        Assert.Equal(0, game.Score);
        Assert.NotEmpty(game.Snake);
        Assert.DoesNotContain(game.Food, game.Snake);
        Assert.Equal(SnakeGame.StartIntervalMs, game.IntervalMs);
    }

    [Fact]
    public void Wall_collision_ends_game()
    {
        // Ports test_snake_wall_collision_ends_game: point the snake into the left
        // wall and tick.
        var game = new SnakeGame(highScore: 0, rng: new Random(0));
        game.ConfigureForTest(new[] { (0, 10), (1, 10), (2, 10) }, dir: (-1, 0), nextDir: (-1, 0));
        game.Step();
        Assert.True(game.GameOver);
    }

    [Fact]
    public void Self_collision_ends_game()
    {
        // Ports test_snake_self_collision_ends_game: a U-shape whose head moves
        // into a body cell.
        var game = new SnakeGame(highScore: 0, rng: new Random(0));
        game.ConfigureForTest(
            new[] { (5, 5), (4, 5), (4, 6), (5, 6), (6, 6), (6, 5) },
            dir: (1, 0), nextDir: (1, 0)); // head (5,5) -> (6,5) which is in the body
        game.Step();
        Assert.True(game.GameOver);
    }

    [Fact]
    public void No_180_reverse_into_self()
    {
        // Heading up; pressing Down must be ignored (no instant reverse).
        var game = new SnakeGame(highScore: 0, rng: new Random(0));
        game.ConfigureForTest(new[] { (10, 10), (10, 11), (10, 12) }, dir: (0, -1), nextDir: (0, -1));
        game.TurnDown(); // should be ignored — currently heading up
        game.Step();
        Assert.False(game.GameOver); // moved up to (10,9), not down into the body
        Assert.Equal((10, 9), game.Snake[0]);
    }

    [Fact]
    public void Eating_food_scores_speeds_up_and_raises_high_score()
    {
        var game = new SnakeGame(highScore: 0, rng: new Random(0));
        int? raised = null;
        game.HighScoreReached += s => raised = s;

        // Place the snake one cell left of food so the next step eats it.
        game.ConfigureForTest(new[] { (4, 5), (3, 5), (2, 5) }, dir: (1, 0), nextDir: (1, 0));
        PlaceFood(game, (5, 5));

        game.Step();

        Assert.Equal(10, game.Score);
        Assert.Equal(10, game.HighScore);
        Assert.Equal(10, raised);
        Assert.True(game.IntervalMs < SnakeGame.StartIntervalMs, "Eating should speed the game up.");
        Assert.Equal((5, 5), game.Snake[0]);
        Assert.Equal(4, game.Snake.Count); // grew by one (tail not dropped)
    }

    [Fact]
    public void Interval_floors_at_minimum()
    {
        var game = new SnakeGame(highScore: 0, rng: new Random(0));
        // A rightward snake; drop food directly ahead each step so every step eats.
        game.ConfigureForTest(new[] { (2, 5), (1, 5), (0, 5) }, dir: (1, 0), nextDir: (1, 0));
        for (int i = 0; i < 100; i++)
        {
            var head = game.Snake[0];
            var next = (head.X + 1, head.Y);
            if (next.Item1 >= game.GridSize)
                break;
            PlaceFood(game, next);
            game.Step();
        }
        Assert.True(game.IntervalMs >= SnakeGame.MinIntervalMs);
    }

    /// <summary>Pin the food cell deterministically via the internal test seam.</summary>
    private static void PlaceFood(SnakeGame game, (int X, int Y) cell)
        => game.SetFoodForTest(cell);
}
