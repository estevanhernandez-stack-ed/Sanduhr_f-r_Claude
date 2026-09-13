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

public readonly record struct PublishDecision(PublishVerdict Verdict, DateOnly TargetDate);

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
        var target = UsagePublisher.DefaultDate(nowLocal);
        if (!enabled)
            return new(PublishVerdict.Disabled, target);
        if (lastOkDate == target)
            return new(PublishVerdict.AlreadyPublished, target);
        if (TimeOnly.FromDateTime(nowLocal.LocalDateTime) < publishTime)
            return new(PublishVerdict.BeforePublishTime, target);
        if (lastAttemptAt is { } attempt && nowLocal - attempt < RetryInterval)
            return new(PublishVerdict.Backoff, target);
        return new(PublishVerdict.Run, target);
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
