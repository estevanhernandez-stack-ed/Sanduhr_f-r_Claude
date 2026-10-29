using System.Globalization;

namespace Sanduhr.Core;

/// <summary>
/// When the GitHub build asks for a newer release (Settings v2, slice 6; WF6): settings.json "update_check", on when absent. Off, nothing asks
/// on its own, neither at launch nor on the widget's tick. "Check now" on About is the person asking, so it asks whatever the switch says: it is
/// not this rule's. <see cref="Interval"/> is what the switch's words promise ("once a day"), and Privacy's "What leaves this PC" quotes it.
/// </summary>
public static class UpdateCheck
{
    /// <summary>How long after the last ask the next one is due.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    /// <summary>Ask now? Never with the switch off; else when nothing was asked yet or <see cref="Interval"/> has passed. A stamp in the
    /// future (a clock moved backwards) counts as due, so it can't park the check forever, as <see cref="StoreUpdate.ShouldCheck"/> does.</summary>
    public static bool Due(bool enabled, DateTimeOffset? lastChecked, DateTimeOffset now)
    {
        if (!enabled)
            return false;
        if (lastChecked is not { } last)
            return true;
        if (last > now)
            return true;
        return now - last >= Interval;
    }

    /// <summary>The stamp file's text as a time, null when it is missing or unreadable (which reads as never asked).</summary>
    public static DateTimeOffset? ParseStamp(string? text) =>
        DateTimeOffset.TryParse(text?.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? at : null;

    /// <summary>The stamp file's text: round-trip, UTC.</summary>
    public static string FormatStamp(DateTimeOffset at) => at.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
