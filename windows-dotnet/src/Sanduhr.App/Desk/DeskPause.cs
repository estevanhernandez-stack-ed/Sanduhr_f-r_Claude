namespace Sanduhr.App.Desk;

/// <summary>
/// The gate one Desk island's live pieces watch (S7, 7.1): <see cref="Paused"/> is true while the
/// island is hidden behind other windows. The controller sets it; the pieces in that island's stack
/// stop their timers on pause and restart on resume. <see cref="Changed"/> is raised only when the
/// value actually changes.
/// </summary>
internal sealed class DeskPause
{
    private bool _paused;

    /// <summary>True while the island is covered and its motion rests.</summary>
    public bool Paused
    {
        get => _paused;
        set
        {
            if (_paused == value) return;
            _paused = value;
            Changed?.Invoke();
        }
    }

    /// <summary>Raised after <see cref="Paused"/> changes.</summary>
    public event Action? Changed;
}
