using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>Where the Claude Code glow lands (settings.json <c>claude_code_glow.target</c>, spec 6.8).
/// <c>Desk</c> is Stage 7's placeholder: never offered, and it reads as Window until then.</summary>
public enum GlowTargetKind { Window, Edge, Widget, Desk }

/// <summary>Which edge of the chosen monitor the Edge target lights.</summary>
public enum GlowEdgeSide { Top, Bottom, Left, Right }

/// <summary>
/// The glow's placement settings, kept in the existing <c>claude_code_glow</c> object next to the
/// waiting, done and skip_terminal switches: <c>target</c>, <c>edge_side</c> and <c>edge_monitor</c> (a
/// monitor device name; absent means the primary monitor). Unknown or absent members are the defaults
/// (Window, Top, primary), so an existing install is unchanged.
/// </summary>
public sealed record GlowTargetSettings(GlowTargetKind Kind, GlowEdgeSide Side, string? Monitor)
{
    public static GlowTargetSettings Default { get; } = new(GlowTargetKind.Window, GlowEdgeSide.Top, null);

    /// <summary>The kinds the Settings page offers. Desk joins in Stage 7.</summary>
    public static IReadOnlyList<GlowTargetKind> Offered { get; } =
        [GlowTargetKind.Window, GlowTargetKind.Edge, GlowTargetKind.Widget];

    /// <summary>The kind to act on: Desk reads as Window until Stage 7 gives it a meaning.</summary>
    public GlowTargetKind Effective => Kind == GlowTargetKind.Desk ? GlowTargetKind.Window : Kind;

    public static string KindWord(GlowTargetKind kind) => kind switch
    {
        GlowTargetKind.Edge => "edge",
        GlowTargetKind.Widget => "widget",
        GlowTargetKind.Desk => "desk",
        _ => "window",
    };

    public static string SideWord(GlowEdgeSide side) => side switch
    {
        GlowEdgeSide.Bottom => "bottom",
        GlowEdgeSide.Left => "left",
        GlowEdgeSide.Right => "right",
        _ => "top",
    };

    public static GlowTargetKind ParseKind(string? word) => word?.Trim().ToLowerInvariant() switch
    {
        "edge" => GlowTargetKind.Edge,
        "widget" => GlowTargetKind.Widget,
        "desk" => GlowTargetKind.Desk,
        _ => GlowTargetKind.Window,
    };

    public static GlowEdgeSide ParseSide(string? word) => word?.Trim().ToLowerInvariant() switch
    {
        "bottom" => GlowEdgeSide.Bottom,
        "left" => GlowEdgeSide.Left,
        "right" => GlowEdgeSide.Right,
        _ => GlowEdgeSide.Top,
    };

    /// <summary>Reads the three members from the <c>claude_code_glow</c> object (null = defaults). A blank
    /// <c>edge_monitor</c> is the primary monitor.</summary>
    public static GlowTargetSettings Decode(JsonObject? glow)
    {
        if (glow is null) return Default;
        string? Str(string key) => glow[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
        var monitor = Str("edge_monitor");
        return new GlowTargetSettings(
            ParseKind(Str("target")),
            ParseSide(Str("edge_side")),
            string.IsNullOrWhiteSpace(monitor) ? null : monitor);
    }

    /// <summary>Sets <c>target</c>, <c>edge_side</c> and <c>edge_monitor</c> (removed when null) on
    /// <paramref name="glow"/>, leaving every other member (waiting, done, skip_terminal) untouched.</summary>
    public void WriteInto(JsonObject glow)
    {
        glow["target"] = KindWord(Kind);
        glow["edge_side"] = SideWord(Side);
        if (Monitor is null) glow.Remove("edge_monitor");
        else glow["edge_monitor"] = Monitor;
    }
}
