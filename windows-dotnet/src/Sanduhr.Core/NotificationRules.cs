using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>What the person chose on Settings, Notifications (5.0 spec 4). Stored as <c>agent_notifications</c> in
/// settings.json. An agent with no entry in <see cref="Agents"/> is on. <paramref name="Idle"/> is Claude Code's idle
/// reminder (<see cref="AgentTurnKind.Idle"/>), off by default.</summary>
public sealed record NotificationSettings(
    IReadOnlyDictionary<string, bool> Agents, bool Toast, bool GlowFinished, bool GlowWaiting, bool Flash, bool DeskCard,
    int ThresholdSeconds, bool SkipInFront, bool Idle)
{
    public const string Key = "agent_notifications";

    /// <summary>The threshold's choices in seconds; 0 is "Every turn".</summary>
    public static IReadOnlyList<int> Thresholds { get; } = [0, 30, 60, 120, 300];

    public const int DefaultThreshold = 60;

    public static NotificationSettings Default { get; } =
        new(new Dictionary<string, bool>(), Toast: true, GlowFinished: false, GlowWaiting: false, Flash: false, DeskCard: true,
            ThresholdSeconds: DefaultThreshold, SkipInFront: true, Idle: false);

    public bool AgentOn(string agent) => Agents.GetValueOrDefault(agent, true);

    public NotificationSettings WithAgent(string agent, bool on) =>
        this with { Agents = new Dictionary<string, bool>(Agents, StringComparer.Ordinal) { [agent] = on } };

    /// <summary>The saved settings; with none saved, the defaults with the old Claude Code glow switches carried over
    /// (the taskbar flash on when either glow was on, and the idle reminder on with the old waiting glow, which lit for
    /// it).</summary>
    public static NotificationSettings Decode(JsonNode? node, GlowSwitches legacy)
    {
        if (node is not JsonObject o)
            return Default with
            {
                GlowFinished = legacy.Done, GlowWaiting = legacy.Waiting, Flash = legacy.Done || legacy.Waiting,
                SkipInFront = legacy.SkipTerminal, Idle = legacy.Waiting,
            };

        var agents = new Dictionary<string, bool>(StringComparer.Ordinal);
        if (o["agents"] is JsonObject a)
            foreach (var id in AgentSources.Burn)
                if (a[id] is JsonValue v && v.TryGetValue<bool>(out var on)) agents[id] = on;

        int threshold = o["threshold_seconds"] is JsonValue t && t.TryGetValue<int>(out var seconds) && Thresholds.Contains(seconds)
            ? seconds : DefaultThreshold;
        return new NotificationSettings(agents,
            Toast: Bool(o, "toast", true), GlowFinished: Bool(o, "glow_finished", false), GlowWaiting: Bool(o, "glow_waiting", false),
            Flash: Bool(o, "flash", false), DeskCard: Bool(o, "desk_card", true), ThresholdSeconds: threshold,
            SkipInFront: Bool(o, "skip_in_front", true), Idle: Bool(o, "idle", false));
    }

    public JsonObject Encode()
    {
        var agents = new JsonObject();
        foreach (var kv in Agents.OrderBy(kv => kv.Key, StringComparer.Ordinal)) agents[kv.Key] = kv.Value;
        return new JsonObject
        {
            ["agents"] = agents, ["toast"] = Toast, ["glow_finished"] = GlowFinished, ["glow_waiting"] = GlowWaiting,
            ["flash"] = Flash, ["desk_card"] = DeskCard, ["threshold_seconds"] = ThresholdSeconds, ["skip_in_front"] = SkipInFront,
            ["idle"] = Idle,
        };
    }

    private static bool Bool(JsonObject o, string key, bool fallback) =>
        o[key] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : fallback;
}

/// <summary>Where one event shows.</summary>
[Flags]
public enum NotifySurfaces
{
    None = 0,
    Toast = 1,
    Glow = 2,
    Flash = 4,
    Card = 8,
}

/// <summary>The glow's limiter, kept per agent session: one kind at most every 20 seconds, and no finished within
/// 5 seconds of a waiting. Only an interruption that happens counts.</summary>
public sealed class NotificationLimiter
{
    public const int MaxSessions = 64;

    private readonly Dictionary<(string Agent, string Session), ClaudeCodeGlowLimiter> _bySession = new();

    public bool Allow(AgentTurnEvent e, DateTimeOffset now)
    {
        var key = (e.Agent, e.Session);
        _bySession.TryGetValue(key, out var limiter);
        bool allowed = limiter.Allow(e.Kind is AgentTurnKind.Waiting or AgentTurnKind.Idle ? ClaudeCodeEvent.Waiting : ClaudeCodeEvent.Done, now);
        if (_bySession.Count >= MaxSessions && !_bySession.ContainsKey(key)) _bySession.Clear();
        _bySession[key] = limiter;
        return allowed;
    }
}

/// <summary>Section 4 of the spec, in one place: which surfaces an event uses.</summary>
public static class NotificationRules
{
    private static readonly IReadOnlySet<string> CodexProcesses =
        new HashSet<string>(ClaudeCodeGlowRules.TerminalProcessNames, StringComparer.OrdinalIgnoreCase) { "codex" };

    /// <summary>The processes that mean "you are looking at this agent": terminals and editors, plus the Codex app for Codex.</summary>
    public static IReadOnlySet<string> ProcessesFor(string agent) =>
        agent == AgentSources.Codex ? CodexProcesses : ClaudeCodeGlowRules.TerminalProcessNames;

    public static bool InFront(string agent, string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false;
        var n = processName.Trim();
        if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n = n[..^4];
        return ProcessesFor(agent).Contains(n);
    }

    public static NotifySurfaces Decide(AgentTurnEvent e, NotificationSettings s, bool agentInFront, NotificationLimiter limiter,
        DateTimeOffset now) => Decide(e, s, () => agentInFront, limiter, now);

    /// <summary>As the other overload, with the front window asked through <paramref name="agentInFront"/>: at most once, and
    /// only when its answer can matter (the agent is on, the event is not a start, the in-front switch is on, the threshold lets
    /// the event through and some interrupting surface is on; for the idle reminder, its switch is on too). The limiter is
    /// asked last, so an event the front window stops does not use up the limit. The idle reminder is never a card: it is the
    /// interrupting surfaces or nothing.</summary>
    public static NotifySurfaces Decide(AgentTurnEvent e, NotificationSettings s, Func<bool> agentInFront, NotificationLimiter limiter,
        DateTimeOffset now)
    {
        if (!s.AgentOn(e.Agent)) return NotifySurfaces.None;
        bool idle = e.Kind == AgentTurnKind.Idle;
        if (idle && !s.Idle) return NotifySurfaces.None;
        // The idle reminder never touches the Desk card; otherwise it interrupts as a wait does.
        var card = s.DeskCard && !idle ? NotifySurfaces.Card : NotifySurfaces.None;
        bool waits = e.Kind is AgentTurnKind.Waiting or AgentTurnKind.Idle;
        if (e.Kind == AgentTurnKind.Started) return card;
        if (!waits && !LongEnough(e.Duration, s.ThresholdSeconds)) return card;

        var interrupt = NotifySurfaces.None;
        if (s.Toast) interrupt |= NotifySurfaces.Toast;
        if (waits ? s.GlowWaiting : s.GlowFinished) interrupt |= NotifySurfaces.Glow;
        if (s.Flash) interrupt |= NotifySurfaces.Flash;
        if (interrupt == NotifySurfaces.None) return card;
        if (s.SkipInFront && agentInFront()) return card;
        if (!limiter.Allow(e, now)) return card;
        return card | interrupt;
    }

    /// <summary>An older hook's signal (3.5 to 4.9, which names no folder or session) arrived: whether it glows, and whether the
    /// taskbar flashes with it. Nothing while Claude Code is switched off on Settings, Notifications; otherwise the glow switch
    /// for its kind, the in-front rule (a terminal or an editor) and the limiter decide, and the flash follows its own switch.
    /// <paramref name="foregroundProcess"/> is called at most once, and only when its answer can matter (Claude Code on, the
    /// glow switch for the kind on, the in-front switch on). The limiter is asked last, as <see cref="ClaudeCodeGlowRules.Decide"/>
    /// does, so a signal the front window stops does not use up the limit.</summary>
    public static (bool Glow, bool Flash) OldHookSignal(NotificationSettings s, ClaudeCodeEvent kind, Func<string?> foregroundProcess,
        ref ClaudeCodeGlowLimiter limiter, DateTimeOffset now)
    {
        if (!s.AgentOn(AgentSources.Claude)) return (false, false);
        if (!(kind == ClaudeCodeEvent.Waiting ? s.GlowWaiting : s.GlowFinished)) return (false, false);
        if (s.SkipInFront && ClaudeCodeGlowRules.IsTerminal(foregroundProcess())) return (false, false);
        if (!limiter.Allow(kind, now)) return (false, false);
        return (true, s.Flash);
    }

    /// <summary>"Every turn" (0) lets anything through; otherwise the turn's length must be known and long enough.</summary>
    private static bool LongEnough(TimeSpan? duration, int thresholdSeconds) =>
        thresholdSeconds <= 0 || (duration is { } d && d.TotalSeconds >= thresholdSeconds);
}
