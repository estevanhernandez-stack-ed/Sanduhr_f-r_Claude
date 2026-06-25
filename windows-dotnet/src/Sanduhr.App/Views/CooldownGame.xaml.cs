using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Sanduhr.App.Theming;
using Sanduhr.Core;

namespace Sanduhr.App.Views;

/// <summary>
/// The cooldown snake overlay (port of <c>game.py</c>'s <c>SnakeOverlay</c>):
/// arrow-key control, themed rendering via <see cref="Controls.SnakeBoard"/>, and
/// a persisted high score. The game LOGIC is the pure <see cref="SnakeGame"/>;
/// this owns the loop timer, key handling, and persistence bridge.
///
/// <para>Esc exits; on GAME OVER, Space/Enter restarts. The loop interval tracks
/// <see cref="SnakeGame.IntervalMs"/> so the game speeds up as you eat.</para>
/// </summary>
public partial class CooldownGame : UserControl
{
    private readonly DispatcherTimer _timer;
    private SnakeGame? _game;

    /// <summary>Raised when the overlay closes (Esc) so the host can restore focus
    /// and hide the overlay.</summary>
    public event Action? Finished;

    /// <summary>Raised when a new high score is reached, carrying the score so the
    /// host can persist <c>snake_high_score</c>.</summary>
    public event Action<int>? HighScoreChanged;

    public CooldownGame()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(SnakeGame.StartIntervalMs) };
        _timer.Tick += (_, _) => OnTick();
    }

    /// <summary>True while a game loop is running.</summary>
    public bool IsActive => _timer.IsEnabled;

    /// <summary>Push the active palette into the board + repaint.</summary>
    public void ApplyPalette(ThemePalette palette)
    {
        Board.Palette = palette;
        Board.InvalidateVisual();
    }

    /// <summary>Open and start a fresh game. <paramref name="highScore"/> seeds the
    /// best on first open; later opens keep the in-session best.</summary>
    public void Start(int highScore)
    {
        if (_game is null)
        {
            _game = new SnakeGame(highScore);
            _game.HighScoreReached += score => HighScoreChanged?.Invoke(score);
            Board.Game = _game;
        }
        _game.Reset();

        Visibility = Visibility.Visible;
        _timer.Interval = TimeSpan.FromMilliseconds(_game.IntervalMs);
        _timer.Start();
        Board.InvalidateVisual();

        // Take keyboard focus so arrow keys + Esc land here. Deferred: Focus() no-ops on
        // an element that isn't laid out yet (we just set Visibility=Visible), which left
        // the game unresponsive to keys — you could neither play nor Esc out.
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            Focusable = true;
            Focus();
            Keyboard.Focus(this);
        }));
    }

    /// <summary>Stop the loop, hide the overlay, and signal completion.</summary>
    public void Stop()
    {
        _timer.Stop();
        Visibility = Visibility.Collapsed;
        Finished?.Invoke();
    }

    private void OnTick()
    {
        if (_game is null)
            return;
        _game.Step();
        // Track the speed-up curve.
        _timer.Interval = TimeSpan.FromMilliseconds(_game.IntervalMs);
        Board.InvalidateVisual();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (_game is null)
            return;

        switch (e.Key)
        {
            case Key.Escape:
                Stop();
                e.Handled = true;
                return;
        }

        if (_game.GameOver)
        {
            if (e.Key is Key.Space or Key.Enter)
            {
                _game.Reset();
                _timer.Interval = TimeSpan.FromMilliseconds(_game.IntervalMs);
                _timer.Start();
                Board.InvalidateVisual();
                e.Handled = true;
            }
            return;
        }

        switch (e.Key)
        {
            case Key.Up: _game.TurnUp(); e.Handled = true; break;
            case Key.Down: _game.TurnDown(); e.Handled = true; break;
            case Key.Left: _game.TurnLeft(); e.Handled = true; break;
            case Key.Right: _game.TurnRight(); e.Handled = true; break;
        }
    }
}
