using System.Globalization;

namespace Sanduhr.Core;

/// <summary>What an agent's turn did (5.0 spec 4).</summary>
public enum AgentTurnKind
{
    Started,
    Finished,

    /// <summary>The turn ended without finishing: aborted, or an error.</summary>
    Stopped,

    /// <summary>The agent needs the person before it can go on.</summary>
    Waiting,
}

/// <summary>One thing an agent's turn did, the same shape for every agent. <paramref name="Session"/> tells one run from
/// another; <paramref name="Folder"/> is the project folder when the agent's files name one; <paramref name="Duration"/> is
/// how long the turn ran, null when that is not known. Never carries message text.</summary>
public sealed record AgentTurnEvent(string Agent, AgentTurnKind Kind, string Session, string? Folder, DateTimeOffset At,
    TimeSpan? Duration = null);

/// <summary>The words a notification and a Desk card use.</summary>
public static class NotificationText
{
    public static string Title(AgentTurnEvent e) => e.Kind switch
    {
        AgentTurnKind.Waiting => $"{AgentSources.BurnName(e.Agent)} is waiting on you",
        AgentTurnKind.Stopped => $"{AgentSources.BurnName(e.Agent)} stopped",
        _ => $"{AgentSources.BurnName(e.Agent)} finished",
    };

    /// <summary>"alpha, 4m 12s"; the project alone for waiting; whichever part is known; empty with neither.
    /// <paramref name="project"/> arrives already masked for streamer mode.</summary>
    public static string Body(AgentTurnEvent e, string? project)
    {
        var parts = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(project)) parts.Add(project);
        if (e.Kind != AgentTurnKind.Waiting && e.Duration is { } d) parts.Add(Duration(d));
        return string.Join(", ", parts);
    }

    /// <summary>"45s", "4m 12s", "1h 05m".</summary>
    public static string Duration(TimeSpan d)
    {
        long s = (long)Math.Max(0, Math.Floor(d.TotalSeconds));
        if (s < 60) return string.Create(CultureInfo.InvariantCulture, $"{s}s");
        if (s < 3600) return string.Create(CultureInfo.InvariantCulture, $"{s / 60}m {s % 60:00}s");
        return string.Create(CultureInfo.InvariantCulture, $"{s / 3600}h {s % 3600 / 60:00}m");
    }

    /// <summary>A Desk card's title: "Codex · alpha", or the agent alone.</summary>
    public static string CardTitle(string agent, string? project) =>
        string.IsNullOrWhiteSpace(project) ? AgentSources.BurnName(agent) : $"{AgentSources.BurnName(agent)} · {project}";
}
