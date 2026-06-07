using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Sanduhr.App.Theming;
using Sanduhr.Core;

namespace Sanduhr.App.Views;

/// <summary>
/// Focus mode (port of <c>focus.py</c>'s <c>FocusTimerWidget</c>): a duration
/// stepper (1–120 min, default 25) + Start/Stop, a 1 Hz <c>mm:ss</c> countdown,
/// and the WPF-rendered hourglass driven by the pure
/// <see cref="HourglassSimulation"/> at ~30 fps.
///
/// <para><b>The split that matters (ported verbatim):</b> the 1 Hz label timer
/// owns ONLY the visible countdown; the physics runs on its own ~30 fps timer and
/// computes elapsed from a <see cref="Stopwatch"/> (millisecond wall clock), so
/// the throttle gets millisecond resolution instead of the integer-second
/// quantization that caused the old visible stutter. The model is the
/// cert-load-bearing CA; this only renders it (the un-fogging).</para>
/// </summary>
public partial class FocusTimer : UserControl
{
    private const int MinMinutes = 1;
    private const int MaxMinutes = 120;
    private const int DefaultMinutes = 25;

    private readonly HourglassSimulation _sim = new();
    private readonly DispatcherTimer _labelTimer;   // 1 Hz visible countdown
    private readonly DispatcherTimer _physicsTimer; // ~30 fps physics
    private readonly Stopwatch _clock = new();      // millisecond wall clock

    private int _minutes = DefaultMinutes;
    private long _durationMs;
    private int _remainingSec;

    /// <summary>Raised once the countdown reaches zero (the session completed).</summary>
    public event Action? Finished;

    /// <summary>Raised when a session starts, carrying the chosen minutes so the
    /// widget can persist <c>focus_mode_duration</c>.</summary>
    public event Action<int>? Started;

    public FocusTimer()
    {
        InitializeComponent();
        Hourglass.Simulation = _sim;
        UpdateMinutesText();

        _labelTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _labelTimer.Tick += (_, _) => OnLabelTick();

        _physicsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _physicsTimer.Tick += (_, _) => OnPhysicsTick();
    }

    /// <summary>True while a session is running.</summary>
    public bool IsActive => _labelTimer.IsEnabled;

    /// <summary>The current stepper value — the widget reads this to persist the
    /// chosen duration.</summary>
    public int Minutes => _minutes;

    /// <summary>Push the active palette into the hourglass + repaint. Text re-tints
    /// automatically via DynamicResource brushes.</summary>
    public void ApplyPalette(ThemePalette palette)
    {
        Hourglass.Palette = palette;
        Hourglass.InvalidateVisual();
    }

    /// <summary>Start a session of <paramref name="minutes"/> minutes (clamped).
    /// Resets the CA, starts the wall clock + both timers, and shows the hourglass.</summary>
    public void Start(int minutes)
    {
        _minutes = Math.Clamp(minutes, MinMinutes, MaxMinutes);
        UpdateMinutesText();

        _durationMs = (long)_minutes * 60 * 1000;
        _remainingSec = _minutes * 60;
        _sim.Reset();
        UpdateTimeLabel();

        SetupPanel.Visibility = Visibility.Collapsed;
        ActiveHeader.Visibility = Visibility.Visible;
        Hourglass.Visibility = Visibility.Visible;
        ActionButton.Content = "Stop";

        _clock.Restart();
        _labelTimer.Start();
        _physicsTimer.Start();
        Hourglass.InvalidateVisual();

        Started?.Invoke(_minutes);
    }

    /// <summary>Stop the current session and return to the setup state (stepper +
    /// Start). Does not raise <see cref="Finished"/> — that's only for completion.</summary>
    public void Stop()
    {
        _labelTimer.Stop();
        _physicsTimer.Stop();
        _clock.Reset();

        ActiveHeader.Visibility = Visibility.Collapsed;
        Hourglass.Visibility = Visibility.Collapsed;
        SetupPanel.Visibility = Visibility.Visible;
        ActionButton.Content = "Start";
    }

    private void OnPhysicsTick()
    {
        if (_remainingSec <= 0 || _durationMs <= 0)
            return;
        if (_sim.Step(_clock.ElapsedMilliseconds, _durationMs))
            Hourglass.InvalidateVisual();
    }

    private void OnLabelTick()
    {
        if (_remainingSec > 0)
        {
            _remainingSec--;
            UpdateTimeLabel();
        }

        if (_remainingSec <= 0)
        {
            Stop();
            Finished?.Invoke();
        }
    }

    private void UpdateTimeLabel()
    {
        int m = _remainingSec / 60;
        int s = _remainingSec % 60;
        TimeLabel.Text = $"{m:00}:{s:00}";
    }

    private void UpdateMinutesText() => MinutesText.Text = _minutes.ToString();

    private void MinusButton_Click(object sender, RoutedEventArgs e)
    {
        _minutes = Math.Max(MinMinutes, _minutes - 1);
        UpdateMinutesText();
    }

    private void PlusButton_Click(object sender, RoutedEventArgs e)
    {
        _minutes = Math.Min(MaxMinutes, _minutes + 1);
        UpdateMinutesText();
    }

    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsActive)
            Stop();
        else
            Start(_minutes);
    }
}
