namespace Sanduhr.Core;

/// <summary>
/// When the usage transport's WebView2 browser process is gone (a crash, End task, an Edge runtime
/// update), the transport has to be built again. Seen 2026-10-22 on 3.7.1: the widget showed
/// "The WebView control is no longer valid because the browser process exited" and never recovered
/// (issue #331).
/// <para>
/// This is the schedule, away from WebView2 so it can be tested: the first rebuild 5 s after the
/// exit, the next 30 s after a failed one, then every 5 minutes. After two failed rebuilds the widget
/// says the connection stopped and offers a restart, and it keeps trying on the 5-minute cadence;
/// a rebuild that works clears all of it.
/// </para>
/// </summary>
public sealed class TransportRecovery
{
    /// <summary>The first rebuild after the browser is gone.</summary>
    public static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(5);

    /// <summary>The rebuild after one failed rebuild.</summary>
    public static readonly TimeSpan SecondDelay = TimeSpan.FromSeconds(30);

    /// <summary>Every rebuild after that.</summary>
    public static readonly TimeSpan SteadyDelay = TimeSpan.FromMinutes(5);

    /// <summary>Failed rebuilds before the widget offers a restart.</summary>
    public const int FailuresBeforeRestartOffer = 2;

    /// <summary>The widget's notice once the transport would not come back.</summary>
    public const string StoppedNotice = "Sanduhr's usage connection stopped. Restart Sanduhr.";

    /// <summary>The notice's button.</summary>
    public const string RestartButton = "Restart";

    /// <summary>The status line while a rebuild is due.</summary>
    public const string ReconnectingStatus = "Reconnecting…";

    /// <summary>True while the browser is gone and no rebuild has worked yet.</summary>
    public bool Lost { get; private set; }

    /// <summary>Rebuilds that failed since the browser was lost.</summary>
    public int FailedRebuilds { get; private set; }

    /// <summary>When the next rebuild is due, while <see cref="Lost"/>.</summary>
    public DateTimeOffset? NextAttemptAt { get; private set; }

    /// <summary>How the browser went, for the log: the exit kind, or <c>invalid-control</c>.</summary>
    public string? LastExitKind { get; private set; }

    /// <summary>True once enough rebuilds failed that the widget should offer a restart.</summary>
    public bool OffersRestart => Lost && FailedRebuilds >= FailuresBeforeRestartOffer;

    /// <summary>The wait before the next rebuild, given how many failed so far.</summary>
    public static TimeSpan DelayAfter(int failedRebuilds) => failedRebuilds switch
    {
        <= 0 => FirstDelay,
        1 => SecondDelay,
        _ => SteadyDelay,
    };

    /// <summary>
    /// The browser is gone. The first report schedules the first rebuild; a second report of the same
    /// loss (the event and the exception both arrive) changes nothing. Returns true for a new loss.
    /// </summary>
    public bool BrowserLost(string exitKind, DateTimeOffset now)
    {
        if (Lost)
            return false;
        Lost = true;
        FailedRebuilds = 0;
        LastExitKind = exitKind;
        NextAttemptAt = now + DelayAfter(0);
        return true;
    }

    /// <summary>Time for the rebuild? A clock that moved backwards counts as due.</summary>
    public bool Due(DateTimeOffset now)
        => Lost && NextAttemptAt is { } at && (now >= at || at - now > SteadyDelay);

    /// <summary>A rebuild failed: schedule the next one on the backoff.</summary>
    public void RebuildFailed(DateTimeOffset now)
    {
        if (!Lost)
            return;
        FailedRebuilds++;
        NextAttemptAt = now + DelayAfter(FailedRebuilds);
    }

    /// <summary>The browser is back (by a scheduled rebuild or any fetch that brought it up).</summary>
    public void Recovered()
    {
        Lost = false;
        FailedRebuilds = 0;
        NextAttemptAt = null;
    }

    /// <summary>
    /// True when <paramref name="error"/> says the WebView2 behind the transport is dead: the control
    /// reports itself no longer valid, or the call failed with the invalid-state or process-aborted
    /// HRESULT that WebView2 raises once its browser process is gone. Ordinary script, network and
    /// HTTP failures are not.
    /// </summary>
    public static bool IsDeadBrowserError(Exception? error)
    {
        for (var e = error; e is not null; e = e.InnerException)
        {
            if (e.HResult is ErrorInvalidState or ErrorProcessAborted)
                return true;
            var message = e.Message ?? "";
            if (message.Contains("no longer valid", StringComparison.OrdinalIgnoreCase))
                return true;
            if (message.Contains("browser process", StringComparison.OrdinalIgnoreCase)
                && (message.Contains("exited", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("crashed", StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        return false;
    }

    /// <summary>HRESULT_FROM_WIN32(ERROR_INVALID_STATE): a closed controller.</summary>
    public const int ErrorInvalidState = unchecked((int)0x8007139F);

    /// <summary>HRESULT_FROM_WIN32(ERROR_PROCESS_ABORTED): the browser process ended.</summary>
    public const int ErrorProcessAborted = unchecked((int)0x8007042B);
}
