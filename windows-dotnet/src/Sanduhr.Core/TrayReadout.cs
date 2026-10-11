namespace Sanduhr.Core;

/// <summary>Which number the tray icon shows. Settings values are the lowercase names (see <see cref="TrayReadout.Raw"/>).</summary>
public enum TrayMode
{
    Highest,
    Session,
    Weekly,
    Higher,
    Rotate,
}

/// <summary>One tray reading: the truncated percent and the label for the tooltip.</summary>
public sealed record TrayReading(int Percent, string Label);

/// <summary>
/// Pure tray-readout rules, a port of the Mac's MenuBarMode reading rules plus Windows'
/// existing highest-tier mode. Null from <see cref="Reading"/> means "--".
/// </summary>
public static class TrayReadout
{
    public static readonly TimeSpan RotateInterval = TimeSpan.FromSeconds(8);

    public static IReadOnlyList<TrayMode> All { get; } =
        new[] { TrayMode.Highest, TrayMode.Session, TrayMode.Weekly, TrayMode.Higher, TrayMode.Rotate };

    public static string Title(TrayMode m) => m switch
    {
        TrayMode.Session => "Session",
        TrayMode.Weekly => "Weekly",
        TrayMode.Higher => "Whichever is higher",
        TrayMode.Rotate => "Rotate (session and weekly)",
        _ => "Highest of any limit",
    };

    public static string Raw(TrayMode m) => m switch
    {
        TrayMode.Session => "session",
        TrayMode.Weekly => "weekly",
        TrayMode.Higher => "higher",
        TrayMode.Rotate => "rotate",
        _ => "highest",
    };

    public static TrayMode Parse(string? raw)
    {
        foreach (var m in All)
        {
            if (string.Equals(Raw(m), raw?.Trim(), StringComparison.OrdinalIgnoreCase)) return m;
        }
        return TrayMode.Highest;
    }

    public static TrayReading? Reading(
        TrayMode mode, double? session, double? weekly, (string Label, double Util)? highestVisible, int step)
    {
        switch (mode)
        {
            case TrayMode.Session:
                return session is not null ? Make(session.Value, "Session") : Weekly(weekly);
            case TrayMode.Weekly:
                return weekly is not null ? Make(weekly.Value, "Weekly") : Session(session);
            case TrayMode.Higher:
                if (session is not null && weekly is not null)
                    return weekly.Value > session.Value ? Make(weekly.Value, "Weekly") : Make(session.Value, "Session");
                return Session(session) ?? Weekly(weekly);
            case TrayMode.Rotate:
                // step & 1 keeps negative steps on the same even/odd cycle.
                return (step & 1) == 0
                    ? Session(session) ?? Weekly(weekly)
                    : Weekly(weekly) ?? Session(session);
            default:
                return highestVisible is { } h ? Make(h.Util, h.Label) : null;
        }
    }

    private static TrayReading? Session(double? v) => v is null ? null : Make(v.Value, "Session");

    private static TrayReading? Weekly(double? v) => v is null ? null : Make(v.Value, "Weekly");

    private static TrayReading Make(double util, string label) => new((int)util, label);
}
