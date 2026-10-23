using System.Globalization;

namespace Sanduhr.Core;

/// <summary>Where the widget handoff's files live (Sanduhr's external folder, <c>%USERPROFILE%\.sanduhr</c>, beside the design handoff's).</summary>
public sealed record WidgetHandoffPaths(string Support)
{
    public string Request => Path.Combine(Support, WidgetProposal.RequestFile);
    public string Result => Path.Combine(Support, WidgetProposal.ResultFile);
}

/// <summary>
/// Widgets made with Claude, the widget's file half (spec 3.8 section 5). The MCP server's <c>propose_widget</c> validates a
/// pack and drops <c>widget-request.json</c>; the App watches the folder and calls <see cref="Check"/>, which takes the request,
/// checks the pack again (<see cref="WidgetProposal.Decode"/>: the widget never trusts the server) and hands a good one to
/// <see cref="Handler"/>. Every request is answered in <c>widget-result.json</c>, written through a temp sibling and a replace.
/// A request older than ten minutes is dropped unanswered.
///
/// The App's widget review service sets <see cref="Handler"/>: it previews the pack for the user and installs it only on Apply.
/// Nothing here installs anything. Not thread-safe: call it from one thread.
/// </summary>
public sealed class WidgetHandoff
{
    private readonly Func<DateTimeOffset> _now;

    public WidgetHandoff(WidgetHandoffPaths paths, Func<DateTimeOffset>? now = null)
    {
        Paths = paths;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public WidgetHandoffPaths Paths { get; }

    /// <summary>What to tell the caller about a pack that passed the rules (the request id, the pack, Claude's note). Until the
    /// App sets one: <see cref="WidgetAnswer.NotReady"/>.</summary>
    public Func<string, WidgetProposalPack, string?, WidgetAnswer> Handler { get; set; } = (_, _, _) => WidgetAnswer.NotReady;

    /// <summary>Takes a waiting request, if there is one, and answers it.</summary>
    public void Check()
    {
        if (MessageFileIo.Take(Paths.Request) is { } data) Receive(data);
    }

    /// <summary>One request's bytes, as read from the file.</summary>
    public void Receive(byte[] data)
    {
        var request = WidgetProposal.Decode(data, _now());
        switch (request.Kind)
        {
            case WidgetRequestKind.Discard:
                return;
            case WidgetRequestKind.Refused:
                Answer(request.Id!, WidgetAnswer.Refused(request.Reasons));
                return;
            default:
                Answer(request.Id!, Handler(request.Id!, request.Pack!, request.Note));
                return;
        }
    }

    /// <summary>Answers a request now or later (a previewing pack the user applied or dismissed, or a newer one replaced). False
    /// when the write failed.</summary>
    public bool Answer(string id, WidgetAnswer answer)
    {
        try
        {
            MessageFileIo.WriteAtomic(Paths.Result, WidgetProposal.ResultJson(id, answer, _now()));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

/// <summary>The words of the widget review (the Desk review bar, worn by a widget).</summary>
public static class WidgetReviewCopy
{
    public const string BarName = "Review Claude's widget";
    public const string ChangesTitle = "What this widget is";
    public const string MakeCompanion = "Make it the companion";
    public const string LocalOnly = "Installs on Apply as a widget made here, on this PC only. Nothing is sent anywhere.";

    public static string Heading(string name) => $"Claude suggests a widget · {name}";
}

/// <summary>
/// What the review bar says about a Claude-made pack (spec 3.8 section 5: "What this widget is" and the file list), and what the
/// Desk previews it with. Pure: reads the pack's own JSON, which already passed the validator.
/// </summary>
public static class WidgetReview
{
    /// <summary>The bar's model: "Claude suggests a widget · name", Claude's note (or the pack's description), what it is, the
    /// files, and for a sprite the "Make it the companion" choice.</summary>
    public static DeskReviewModel Model(WidgetProposalPack pack, string? note, bool deskOn)
    {
        var lines = Describe(pack).ToList();
        lines.Add("Files: " + string.Join(", ", pack.Files.Keys.Order(StringComparer.Ordinal)) + $" ({pack.Manifest.License})");
        lines.Add(WidgetReviewCopy.LocalOnly);
        string? shown = string.IsNullOrWhiteSpace(note) ? pack.Manifest.Description : note;
        return new DeskReviewModel(pack.Name, shown, lines, [], !deskOn, [])
        {
            Heading = WidgetReviewCopy.Heading(pack.Name),
            ChangesTitle = WidgetReviewCopy.ChangesTitle,
            BarName = WidgetReviewCopy.BarName,
            Option = pack.Kind == "sprite" ? WidgetReviewCopy.MakeCompanion : null,
        };
    }

    /// <summary>"What this widget is": the kind and its parts (a sprite's stages, frames and colors; the palettes and their
    /// stops; the effects and their timing).</summary>
    public static IReadOnlyList<string> Describe(WidgetProposalPack pack)
    {
        var lines = new List<string>();
        switch (pack.Kind)
        {
            case "sprite":
                DescribeSprite(pack, lines);
                break;
            case "palette":
                if (Json(pack, "palettes.json") is PackObject po && po["palettes"] is List<object?> palettes)
                {
                    lines.Add(palettes.Count == 1 ? "A palette for the Desk ink: 1 palette." : $"A palette pack for the Desk ink: {palettes.Count} palettes.");
                    foreach (var p in palettes.OfType<PackObject>().Take(8))
                    {
                        int n = p["colors"] is List<object?> c ? c.Count : 0;
                        string mood = p["mood"] is string m && m.Length > 0 ? $", {m}" : "";
                        lines.Add($"{p["name"]}: {n} color{(n == 1 ? "" : "s")} ({string.Join(" ", (p["colors"] as List<object?> ?? []).OfType<string>())}){mood}");
                    }
                    if (palettes.Count > 8) lines.Add($"and {palettes.Count - 8} more");
                    lines.Add("The Desk shows the first palette as its ink while you review.");
                }
                break;
            case "effects":
                if (Json(pack, "effects.json") is PackObject eo && eo["effects"] is List<object?> effects)
                {
                    lines.Add(effects.Count == 1 ? "An effect preset: 1 effect." : $"Effect presets: {effects.Count} effects.");
                    foreach (var e in effects.OfType<PackObject>().Take(8))
                    {
                        double intensity = e["intensity"] is PackNumber i ? i.Value : 0;
                        double period = e["period_ms"] is PackNumber p ? p.Value / 1000.0 : 0;
                        lines.Add(string.Format(CultureInfo.InvariantCulture, "{0}: {1}, {2:0}% intensity, every {3:0.#} s, in {4}",
                            e["name"], e["type"], intensity * 100, period, e["color"] is "ink" ? "the ink" : e["color"]));
                    }
                    if (effects.Count > 8) lines.Add($"and {effects.Count - 8} more");
                }
                break;
        }
        return lines;
    }

    private static void DescribeSprite(WidgetProposalPack pack, List<string> lines)
    {
        if (!pack.Files.TryGetValue("sprite.json", out var bytes) || CompanionSprite.Parse(bytes) is not { } sprite)
        {
            lines.Add("A Desk companion.");
            return;
        }
        lines.Add(sprite.Stages.Count == 1
            ? $"A Desk companion: 1 stage, {sprite.Animations.Count} animations at {sprite.Fps} fps, drawn at {sprite.Scale}x."
            : $"A Desk companion: {sprite.Stages.Count} stages that grow with your coding days, {sprite.Animations.Count} animations at {sprite.Fps} fps, drawn at {sprite.Scale}x.");
        foreach (var stage in sprite.Stages)
        {
            string label = sprite.Stages.Count == 1 ? "Art" : stage.Name;
            if (stage.IsPixelSheet && Json(pack, stage.Sheet) is PackObject sheet)
            {
                int frames = sheet["frames"] is List<object?> f ? f.Count : 0;
                int colors = sheet["palette"] is List<object?> c ? c.Count : 0;
                lines.Add($"{label}: {frames} frame{(frames == 1 ? "" : "s")} of {stage.FrameWidth}x{stage.FrameHeight} pixels, {colors} color{(colors == 1 ? "" : "s")}");
            }
            else lines.Add($"{label}: {stage.FrameWidth}x{stage.FrameHeight} pixel frames");
        }
        string edges = string.Join(", ", sprite.Edges.Select(e => e.ToString().ToLowerInvariant()));
        lines.Add($"Roams the {edges} edge{(sprite.Edges.Count == 1 ? "" : "s")} at {sprite.SpeedPxPerSecond:0} px a second when roaming is on.");
    }

    /// <summary>The ink a palette pack previews with: its first palette's stops as the Custom ink spec (<c>"ff2a6d,d16ba5,05d9e8"</c>).
    /// Null for another kind or a pack without one.</summary>
    public static string? PreviewInk(WidgetProposalPack pack)
    {
        if (pack.Kind != "palette" || Json(pack, "palettes.json") is not PackObject o || o["palettes"] is not List<object?> list) return null;
        if (list.OfType<PackObject>().FirstOrDefault() is not { } first || first["colors"] is not List<object?> colors) return null;
        var hexes = colors.OfType<string>().Select(InkHex.Normalize).OfType<string>().ToList();
        return hexes.Count == 0 ? null : string.Join(",", hexes);
    }

    private static object? Json(WidgetProposalPack pack, string rel) =>
        pack.Files.TryGetValue(rel, out var bytes) && PackJson.TryParse(bytes, out var v, out _) ? v : null;
}
