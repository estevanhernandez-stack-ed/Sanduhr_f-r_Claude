namespace Sanduhr.Core;

/// <summary>One entry of the Monitor dropdown: the value stored for the piece (null for every monitor) and what it is called.</summary>
public sealed record DeskMonitorChoice(string? Value, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// How a person is told which monitor is which (spec, Desk pieces per monitor): by size and position, the main one first,
/// "3440×1440, main" and "1920×1080, right of main". Pure naming and the dropdown's choices; the App reads the monitors.
/// </summary>
public static class DeskMonitorNames
{
    public const string AllMonitors = "All monitors";
    public const string MainMonitor = "Main monitor";

    /// <summary>The name of a monitor that was chosen and is not connected now: its pieces draw on the main one meanwhile.</summary>
    public const string NotConnected = "A monitor that is not connected (on the main monitor for now)";

    /// <summary>"3440×1440": the size, with the multiplication sign.</summary>
    public static string Size(int width, int height) => $"{width}×{height}";

    /// <summary>
    /// The monitors with their names filled in. The main monitor (the one flagged primary, else the one at 0,0, else the first) is
    /// "W×H, main". Another monitor is named for where it sits against the main one: "right of main", "left of main", "below main"
    /// or "above main"; one that overlaps it (a mirrored or odd layout) by its position, "at X,Y". Two with the same name get
    /// " (2)", " (3)" in screen order (left to right, then top to bottom). The input's own names are ignored.
    /// </summary>
    public static IReadOnlyList<DeskMonitorInfo> Named(IReadOnlyList<DeskMonitorInfo> monitors)
    {
        if (monitors.Count == 0) return monitors;
        var main = monitors.FirstOrDefault(m => m.Primary)
                   ?? monitors.FirstOrDefault(m => m.Left == 0 && m.Top == 0)
                   ?? monitors[0];
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        var used = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var m in monitors.OrderBy(m => m.Left).ThenBy(m => m.Top).ThenBy(m => m.Id, StringComparer.Ordinal))
        {
            string name = ReferenceEquals(m, main) || m.Id == main.Id
                ? $"{Size(m.Width, m.Height)}, main"
                : $"{Size(m.Width, m.Height)}, {Where(m, main)}";
            used[name] = used.GetValueOrDefault(name) + 1;
            names[m.Id] = used[name] == 1 ? name : $"{name} ({used[name]})";
        }
        return [.. monitors.Select(m => m with { Name = names[m.Id] })];
    }

    private static string Where(DeskMonitorInfo m, DeskMonitorInfo main)
    {
        if (m.Left >= main.Left + main.Width) return "right of main";
        if (m.Left + m.Width <= main.Left) return "left of main";
        if (m.Top >= main.Top + main.Height) return "below main";
        if (m.Top + m.Height <= main.Top) return "above main";
        return $"at {m.Left},{m.Top}";
    }

    /// <summary>
    /// The Monitor dropdown for a piece: All monitors, Main monitor, then each connected monitor by name, in screen order. When the piece's
    /// stored monitor is an id that is not connected, that id gets an entry of its own (<see cref="NotConnected"/>) so the box shows
    /// what is stored and choosing something else is a real change.
    /// </summary>
    public static IReadOnlyList<DeskMonitorChoice> Choices(IReadOnlyList<DeskMonitorInfo> connected, string? stored)
    {
        var list = new List<DeskMonitorChoice> { new(null, AllMonitors), new(DeskMonitors.Main, MainMonitor) };
        foreach (var m in connected.OrderBy(m => m.Left).ThenBy(m => m.Top).ThenBy(m => m.Id, StringComparer.Ordinal))
            list.Add(new DeskMonitorChoice(m.Id, m.Name));
        if (DeskMonitors.IsId(stored) && !connected.Any(m => string.Equals(m.Id, stored, StringComparison.OrdinalIgnoreCase)))
            list.Add(new DeskMonitorChoice(DeskMonitors.FromWord(stored), NotConnected));
        return list;
    }

    /// <summary>The dropdown entry for a stored monitor (a stored id that is connected matches its entry without regard to case).</summary>
    public static DeskMonitorChoice Select(IReadOnlyList<DeskMonitorChoice> choices, string? stored)
    {
        var s = DeskMonitors.FromWord(stored);
        return choices.FirstOrDefault(c => DeskMonitors.Same(c.Value, s)) ?? choices[0];
    }

    /// <summary>The name of a monitor id for a sentence ("1920×1080, right of main"), null when it is not connected.</summary>
    public static string? NameOf(IReadOnlyList<DeskMonitorInfo> connected, string id) =>
        connected.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase))?.Name;
}
