using System.Globalization;

namespace Sanduhr.Core;

/// <summary>Why the scheduler did or did not fire — surfaced to the settings
/// status line and to tests; never a reason to throw.</summary>
public enum PublishVerdict
{
    /// <summary>Run the publisher for <see cref="PublishDecision.TargetDate"/>.</summary>
    Run,
    /// <summary>Master toggle off.</summary>
    Disabled,
    /// <summary>Local time has not reached the publish time yet.</summary>
    BeforePublishTime,
    /// <summary>The target date already published successfully.</summary>
    AlreadyPublished,
    /// <summary>A failed attempt happened less than the retry interval ago.</summary>
    Backoff,
}

/// <summary>
/// <paramref name="TargetDate"/> is the day the next run reports (yesterday,
/// local). <paramref name="NextRunAt"/> is when the scheduler will actually
/// fire next, local wall-clock: the earliest publish-time slot at or after now
/// whose target day is still unpublished; <c>now</c> itself when a run is due;
/// the retry moment during backoff; null when publishing is off.
/// </summary>
public readonly record struct PublishDecision(PublishVerdict Verdict, DateOnly TargetDate, DateTime? NextRunAt);

/// <summary>
/// The pure decision behind the widget's daily publish: fire once per target
/// day (yesterday, local time) at or after the configured publish time, and
/// after a failure never retry more often than <see cref="RetryInterval"/>.
/// No clock, no IO — the App's tick loop feeds it the settings it persisted.
/// </summary>
public static class PublishScheduler
{
    public static readonly TimeOnly DefaultPublishTime = new(6, 45);
    public static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(15);

    public static PublishDecision Decide(
        DateTimeOffset nowLocal,
        bool enabled,
        TimeOnly publishTime,
        DateOnly? lastOkDate,
        DateTimeOffset? lastAttemptAt)
    {
        var now = nowLocal.LocalDateTime;
        var today = DateOnly.FromDateTime(now);
        var target = UsagePublisher.DefaultDate(nowLocal);
        if (!enabled)
            return new(PublishVerdict.Disabled, target, null);
        if (lastOkDate == target)
        {
            // Today's slot reports a day that is already out. Whether that slot
            // is still ahead or behind us, the next real run is tomorrow's slot,
            // which reports today.
            return new(PublishVerdict.AlreadyPublished, target, today.AddDays(1).ToDateTime(publishTime));
        }
        if (TimeOnly.FromDateTime(now) < publishTime)
            return new(PublishVerdict.BeforePublishTime, target, today.ToDateTime(publishTime));
        if (lastAttemptAt is { } attempt && nowLocal - attempt < RetryInterval)
            return new(PublishVerdict.Backoff, target, (attempt + RetryInterval).LocalDateTime);
        return new(PublishVerdict.Run, target, now);
    }

    /// <summary>Parse a user-entered "HH:mm" (24h). Null on anything else —
    /// callers keep the previous value rather than guessing.</summary>
    public static TimeOnly? ParsePublishTime(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        return TimeOnly.TryParseExact(text.Trim(), new[] { "HH:mm", "H:mm" },
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            ? t
            : null;
    }

    public static string FormatPublishTime(TimeOnly time)
        => time.ToString("HH:mm", CultureInfo.InvariantCulture);
}
