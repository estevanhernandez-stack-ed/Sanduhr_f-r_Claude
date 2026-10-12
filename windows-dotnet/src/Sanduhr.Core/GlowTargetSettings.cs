using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>Where the Claude Code glow lands (settings.json <c>claude_code_glow.target</c>, spec 6.8).
/// <c>Desk</c> is the glow's spot on the Desk (S7); it reads as Window when the Desk can't show it.</summary>
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

    /// <summary>The kinds the Settings page offers.</summary>
    public static IReadOnlyList<GlowTargetKind> Offered { get; } =
        [GlowTargetKind.Window, GlowTargetKind.Edge, GlowTargetKind.Widget, GlowTargetKind.Desk];

    /// <summary>The kind to act on: Desk stays Desk while <paramref name="deskShowsGlow"/> (the Desk is
    /// on, the spot is placed and its island is in sight), else reads as Window. Others are unchanged.</summary>
    public GlowTargetKind Effective(bool deskShowsGlow) =>
        Kind == GlowTargetKind.Desk && !deskShowsGlow ? GlowTargetKind.Window : Kind;

    /// <summary>The caption under the four choices on the Claude Code page.</summary>
    public const string DeskCaption =
        "On the Desk while it is on and in sight; otherwise at the top of the window you're working in.";

    /// <summary>The name of a target as the Settings page lists it.</summary>
    public static string KindName(GlowTargetKind kind) => kind switch
    {
        GlowTargetKind.Edge => "A screen edge",
        GlowTargetKind.Widget => "The widget only",
        GlowTargetKind.Desk => "A spot on the Desk",
        _ => "The window you're working in",
    };

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
