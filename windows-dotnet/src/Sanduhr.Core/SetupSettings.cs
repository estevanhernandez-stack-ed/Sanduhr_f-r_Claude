using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>
/// What Sanduhr shows (the setup spec, section 1): the Desk's own pieces and each usage source. Drawers keep their own switch
/// (<c>drawers.enabled</c>); the Desk runs when desktop or drawers is on. settings.json <c>setup</c>:
/// <c>{ "desktop": bool, "usage": { "claude": bool } }</c>. Absent means no setup has been made yet (a first run).
/// </summary>
public sealed record SetupSettings(bool Desktop, IReadOnlyDictionary<string, bool> Usage)
{
    public const string Key = "setup";
    public const string Claude = "claude";

    public bool ClaudeUsage => Usage.TryGetValue(Claude, out var on) && on;

    /// <summary>Any usage source on.</summary>
    public bool UsageOn => Usage.Values.Any(v => v);

    public SetupSettings WithClaude(bool on) =>
        this with { Usage = new Dictionary<string, bool>(Usage, StringComparer.Ordinal) { [Claude] = on } };

    public JsonObject Encode()
    {
        var usage = new JsonObject();
        foreach (var (source, on) in Usage.OrderBy(p => p.Key, StringComparer.Ordinal)) usage[source] = on;
        return new JsonObject { ["desktop"] = Desktop, ["usage"] = usage };
    }

    /// <summary>Decoded entry by entry: an entry that is missing or not a bool keeps <paramref name="fallback"/>'s value, so a damaged
    /// file never reads as nothing ticked.</summary>
    public static SetupSettings Decode(JsonNode? node, SetupSettings fallback)
    {
        if (node is not JsonObject o) return fallback;
        bool desktop = SettingsJson.Bool(o, "desktop", fallback.Desktop);
        var usage = new Dictionary<string, bool>(fallback.Usage, StringComparer.Ordinal);
        if (o["usage"] is JsonObject u)
            foreach (var (source, value) in u)
                if (value is JsonValue v && v.TryGetValue<bool>(out var on)) usage[source] = on;
        return new SetupSettings(desktop, usage);
    }
}

/// <summary>The three ticks on the setup screen (spec 2). Desktop reads as ticked only while the Desk runs (spec 1, the Desk page's own switch).</summary>
public sealed record SetupChoices(bool Desktop, bool Drawers, bool ClaudeUsage)
{
    /// <summary>A first run's ticks: the desktop and drawers, no usage.</summary>
    public static SetupChoices FirstRun { get; } = new(true, true, false);

    public bool AnyTicked => Desktop || Drawers || ClaudeUsage;

    /// <summary>The Desk runs whenever desktop or drawers is ticked.</summary>
    public bool DeskRuns => Desktop || Drawers;

    public static SetupChoices Of(SetupSettings s, bool drawersOn, bool deskEnabled) =>
        new(s.Desktop && deskEnabled, drawersOn, s.ClaudeUsage);

    /// <summary>The stored setup for these ticks; sources this build does not show keep their value from <paramref name="before"/>.</summary>
    public SetupSettings ToSettings(SetupSettings? before)
    {
        var usage = before is null
            ? new Dictionary<string, bool>(StringComparer.Ordinal)
            : new Dictionary<string, bool>(before.Usage, StringComparer.Ordinal);
        usage[SetupSettings.Claude] = ClaudeUsage;
        return new SetupSettings(Desktop, usage);
    }
}

/// <summary>What the first launch of a setup build makes of an older settings file (spec 1, Migration).</summary>
public sealed record SetupMigrationResult(SetupSettings Setup, bool DeskOn, bool ReminderOn);

public static class SetupMigration
{
    /// <summary>Null for a first run (no account, no Just the Desk): the setup screen opens. An account wins over Just the Desk,
    /// as signing in did. The sign-in reminder comes back only where Just the Desk had silenced it.</summary>
    public static SetupMigrationResult? From(bool hasAccount, bool justTheDesk, bool silencedReminder, bool deskEnabled)
    {
        if (hasAccount)
            return new(new SetupSettings(deskEnabled, new Dictionary<string, bool> { [SetupSettings.Claude] = true }), deskEnabled,
                justTheDesk && silencedReminder);
        if (justTheDesk)
            return new(new SetupSettings(true, new Dictionary<string, bool> { [SetupSettings.Claude] = false }), true, silencedReminder);
        return null;
    }
}

public static class SetupLaunch
{
    /// <summary>No setup yet: the setup screen opens first.</summary>
    public static bool OpensSetup(SetupSettings? setup) => setup is null;

    /// <summary>The widget starts hidden (the tray is the way back) with usage off and the Desk running; with the Desk off as well
    /// the widget shows, so a launch never shows nothing. Before any setup the setup screen is what shows.</summary>
    public static bool WidgetStartsHidden(SetupSettings? setup, bool deskEnabled) =>
        setup is null || (!setup.UsageOn && deskEnabled);
}
