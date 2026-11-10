using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Sanduhr.Core;

/// <summary>Per-source display settings (agent layer): the primary source, a color and an alerts switch per source, and which
/// sections are collapsed. settings.json <c>sources</c>:
/// <c>{"primary":"claude","colors":{"codex":"#2FB5A3"},"alerts":{"codex":true},"collapsed":["codex"]}</c>.
/// A color that is absent or not <c>#RRGGBB</c> falls back to the default; absent alerts mean on.</summary>
public sealed partial record SourceSettings(
    string Primary,
    IReadOnlyDictionary<string, string> Colors,
    IReadOnlyDictionary<string, bool> Alerts,
    IReadOnlySet<string> Collapsed)
{
    public const string Key = "sources";

    public static SourceSettings Default { get; } = new(AgentSources.Claude,
        new Dictionary<string, string>(), new Dictionary<string, bool>(), new HashSet<string>());

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexRe();

    public string ColorOf(string source) =>
        Colors.TryGetValue(source, out var c) && HexRe().IsMatch(c) ? c : AgentSources.DefaultColor(source);

    public bool AlertsOn(string source) => !Alerts.TryGetValue(source, out var on) || on;

    /// <summary>The saved primary if that source is on, else the first ticked source, else Claude.</summary>
    public string PrimaryFor(SetupSettings setup)
    {
        if (setup.Usage.TryGetValue(Primary, out var on) && on) return Primary;
        return AgentSources.All.FirstOrDefault(s => setup.Usage.TryGetValue(s, out var o) && o) ?? AgentSources.Claude;
    }

    public JsonObject Encode()
    {
        var colors = new JsonObject();
        foreach (var (k, v) in Colors) colors[k] = v;
        var alerts = new JsonObject();
        foreach (var (k, v) in Alerts) alerts[k] = v;
        var collapsed = new JsonArray();
        foreach (var s in Collapsed.OrderBy(AgentSources.Order)) collapsed.Add(s);
        return new JsonObject { ["primary"] = Primary, ["colors"] = colors, ["alerts"] = alerts, ["collapsed"] = collapsed };
    }

    public static SourceSettings Decode(JsonNode? node)
    {
        if (node is not JsonObject o) return Default;
        string primary = o["primary"] is JsonValue pv && pv.TryGetValue<string>(out var p) && AgentSources.All.Contains(p) ? p : AgentSources.Claude;
        var colors = new Dictionary<string, string>();
        if (o["colors"] is JsonObject co)
            foreach (var (k, v) in co)
                if (AgentSources.All.Contains(k) && v is JsonValue cv && cv.TryGetValue<string>(out var c) && HexRe().IsMatch(c)) colors[k] = c;
        var alerts = new Dictionary<string, bool>();
        if (o["alerts"] is JsonObject ao)
            foreach (var (k, v) in ao)
                if (AgentSources.All.Contains(k) && v is JsonValue av && av.TryGetValue<bool>(out var b)) alerts[k] = b;
        var collapsed = new HashSet<string>();
        if (o["collapsed"] is JsonArray ca)
            foreach (var v in ca)
                if (v is JsonValue sv && sv.TryGetValue<string>(out var s) && AgentSources.All.Contains(s)) collapsed.Add(s);
        return new(primary, colors, alerts, collapsed);
    }
}
