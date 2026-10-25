using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>What the machine adds to the rules (the context of <see cref="DeskDesign.Validate(JsonNode?, DeskDesignContext)"/>).
/// A member that is null is unknown, and the rule that needs it is skipped (the widget, which always knows, checks again).</summary>
/// <param name="InstalledFonts">The font families installed on this PC.</param>
/// <param name="Wallpaper">What the wallpaper looks like behind each anchor.</param>
/// <param name="StartMenuApps">The IDs of the apps the Start menu lists (the Launcher's membership rule).</param>
/// <param name="Current">The Desk as it is now, so a partial design is judged as it would look once merged.</param>
/// <param name="Monitors">The monitors connected now (their ids are what a layout piece's <c>monitor</c> may name).</param>
public sealed record DeskDesignContext(
    IReadOnlyCollection<string>? InstalledFonts = null,
    WallpaperOverview? Wallpaper = null,
    IReadOnlyCollection<string>? StartMenuApps = null,
    DeskDesign? Current = null,
    IReadOnlyList<DeskMonitorInfo>? Monitors = null)
{
    public static DeskDesignContext Unknown { get; } = new();
}

/// <summary>Where a design stands.</summary>
public enum DeskDesignStatus
{
    /// <summary>It can be previewed and applied.</summary>
    Ok,
    /// <summary>It breaks a rule; the reasons say which and how to fix it.</summary>
    Rejected,
    /// <summary>It is valid but names a font this PC lacks: it cannot be applied until the font is installed.</summary>
    NeedsFont,
}

/// <summary>A font a design names that is not installed, with the link to get it if the design gave one.</summary>
public sealed record MissingFont(string Family, string? Link)
{
    public JsonObject ToJson() => new() { ["family"] = Family, ["link"] = Link };
}

/// <summary>The verdict on a design. <see cref="Design"/> is the normalized design (trimmed, canonical colors, snapped sizes)
/// whenever it parsed, even for <see cref="DeskDesignStatus.NeedsFont"/>; null when rejected.</summary>
public sealed record DeskValidation(
    DeskDesignStatus Status,
    IReadOnlyList<DeskReason> Reasons,
    IReadOnlyList<DeskReason> Warnings,
    IReadOnlyList<MissingFont> Fonts,
    DeskDesign? Design)
{
    public bool Ok => Status == DeskDesignStatus.Ok;

    /// <summary>The words of the status in tool results and files: ok, rejected, needs_font.</summary>
    public string StatusWord => Status switch
    {
        DeskDesignStatus.Ok => "ok",
        DeskDesignStatus.NeedsFont => "needs_font",
        _ => "rejected",
    };

    public JsonArray ReasonsJson() => ToArray(Reasons);

    public JsonArray WarningsJson() => ToArray(Warnings);

    public JsonArray FontsJson()
    {
        var arr = new JsonArray();
        foreach (var f in Fonts) arr.Add((JsonNode?)f.ToJson());
        return arr;
    }

    private static JsonArray ToArray(IReadOnlyList<DeskReason> reasons)
    {
        var arr = new JsonArray();
        foreach (var r in reasons) arr.Add((JsonNode?)r.ToJson());
        return arr;
    }
}

public sealed partial record DeskDesign
{
    /// <summary>The WCAG contrast the Desk's ink must keep against the wallpaper under each piece.</summary>
    public const double MinContrast = 3.0;

    private const int MaxContrastReasons = 10;

    /// <summary>
    /// The one gate for a design (spec, the agent-designed Desk, A). Reads it with <see cref="Parse"/> (shapes, ranges,
    /// colors, Launcher target shapes, message lines, the theme lint, text), then applies what needs the machine:
    /// <list type="bullet">
    /// <item><description>Launcher apps must be in the Start menu list (<see cref="DeskDesignContext.StartMenuApps"/>).</description></item>
    /// <item><description>Ink contrast: each placed piece's ink must reach 3:1 against the wallpaper under its anchor, worst monitor
    /// first; with the plate on, against the plate laid over the wallpaper. Automatic ink picks its own, so it is not judged.</description></item>
    /// <item><description>A font that is neither bundled nor installed makes the design <see cref="DeskDesignStatus.NeedsFont"/>
    /// (valid, but it waits for the font); any other reason makes it rejected.</description></item>
    /// </list>
    /// </summary>
    public static DeskValidation Validate(JsonNode? design, DeskDesignContext context)
    {
        if (design is not null && Encoding.UTF8.GetByteCount(design.ToJsonString()) > MaxJsonBytes)
            return Rejected([new DeskReason("design", $"is larger than {MaxJsonBytes / 1024} KB")], []);

        var parsed = Parse(design);
        if (parsed.Design is not { } d) return Rejected(parsed.Reasons, parsed.Warnings);

        var reasons = new List<DeskReason>();
        CheckMembership(d, context, reasons);
        CheckMonitors(d, context, reasons);
        CheckContrast(d, context, reasons);
        CheckLooks(d, context, reasons);
        if (reasons.Count > 0) return Rejected(reasons, parsed.Warnings, d);

        var missing = new List<MissingFont>();
        if (d.Font is { Family.Length: > 0 } font && context.InstalledFonts is { } installed
            && BundledFonts.Bundled(font.Family) is null
            && !installed.Contains(font.Family, StringComparer.OrdinalIgnoreCase))
            missing.Add(new MissingFont(font.Family, font.Link));
        return new DeskValidation(missing.Count > 0 ? DeskDesignStatus.NeedsFont : DeskDesignStatus.Ok, [], parsed.Warnings, missing, d);
    }

    /// <summary>The same gate for a design already in memory: it is written out and read back, so a hand-built design
    /// meets exactly the rules a design from a file does.</summary>
    public static DeskValidation Validate(DeskDesign design, DeskDesignContext context) => Validate(design.ToJson(), context);

    private static DeskValidation Rejected(IReadOnlyList<DeskReason> reasons, IReadOnlyList<DeskReason> warnings, DeskDesign? design = null) =>
        new(DeskDesignStatus.Rejected, reasons, warnings, [], design);

    private static void CheckMembership(DeskDesign d, DeskDesignContext ctx, List<DeskReason> reasons)
    {
        if (d.Launcher is null || ctx.StartMenuApps is not { } apps) return;
        var listed = apps as ISet<string> ?? new HashSet<string>(apps, StringComparer.Ordinal);
        for (int i = 0; i < d.Launcher.Buttons.Count; i++)
        {
            var b = d.Launcher.Buttons[i];
            if (b.Kind == LauncherKind.App && !listed.Contains(b.Target))
                reasons.Add(new DeskReason($"launcher.buttons[{i}].target",
                    "is not an app this PC's Start menu lists; use an id from start_menu_apps in get_desk. A button opens only what the Start menu offers."));
        }
    }

    private static void CheckMonitors(DeskDesign d, DeskDesignContext ctx, List<DeskReason> reasons)
    {
        if (d.Layout is null || ctx.Monitors is not { } monitors) return;
        foreach (var p in d.Layout)
        {
            if (!DeskMonitors.IsId(p.Monitor)) continue;
            if (monitors.Any(m => string.Equals(m.Id, p.Monitor, StringComparison.OrdinalIgnoreCase))) continue;
            reasons.Add(new DeskReason($"layout.{p.Piece}.monitor",
                "is not a monitor connected to this PC; use \"all\", \"main\" or an id from monitors in get_desk."));
        }
    }

    /// <summary>The song looks of a design meet the same contrast rule as <c>propose_now_playing_looks</c>: every stop keeps 3:1 against the
    /// spot behind the now-playing piece, judged on the Desk the design would leave (layout, plate and ink merged over the current ones).</summary>
    private static void CheckLooks(DeskDesign d, DeskDesignContext ctx, List<DeskReason> reasons)
    {
        if (d.Looks is not { Count: > 0 } looks) return;
        var merged = Merge(ctx.Current, d);
        var backgrounds = NowPlayingLookCheck.Backgrounds(ctx with { Current = merged });
        if (backgrounds.Count == 0) return;
        for (int i = 0; i < looks.Count; i++)
            foreach (var stop in looks[i].Look.Ink)
                if (NowPlayingLookCheck.Contrast(stop, backgrounds) is { } why)
                    reasons.Add(new DeskReason("looks", $"look {i + 1} color {why}"));
    }

    // -- contrast -----------------------------------------------------------------------------------------

    private static void CheckContrast(DeskDesign proposal, DeskDesignContext ctx, List<DeskReason> reasons)
    {
        if (ctx.Wallpaper is not { Monitors.Count: > 0 } wall) return;
        var merged = Merge(ctx.Current, proposal);
        // Following the widget theme (theme match): the Desk draws the theme's look, whatever its own ink says.
        var look = merged.FollowsTheme == true && merged.Theme is { } followed && ThemeLint.Lint(followed).Theme is { } fdef
            ? ThemeDeskLook.From(fdef) : null;
        var choice = merged.Ink;
        if (look is null && (choice is null || choice.Kind == DeskInkKind.Auto)) return;

        bool inkChanged = look is null && proposal.Ink is not null;
        bool plateChanged = look is null && proposal.Plate is not null;
        bool followChanged = look is not null && proposal.FollowsTheme == true;
        bool themeChanged = proposal.Theme is not null && (look is not null || choice!.Kind == DeskInkKind.Theme);
        bool layoutChanged = proposal.Layout is not null;
        if (!(inkChanged || plateChanged || followChanged || themeChanged || layoutChanged)) return;

        ResolvedInk ink;
        bool plate;
        double[] stopLum;
        if (look is not null)
        {
            ink = DeskInk.Resolve(look.InkSettings, null, null);
            plate = look.Plate;
            // The readability rule judges a followed ink by its first stop (ThemeDeskLook.ReadsOver), as the Desk does.
            stopLum = [InkHex.Luminance(look.Ink[0])];
        }
        else
        {
            string? themeText = merged.Theme?["text"] is JsonValue tv && tv.TryGetValue<string>(out var text) ? text : null;
            if (choice!.Kind == DeskInkKind.Theme && InkHex.Normalize(themeText) is null) return;
            var settings = new DeskInkSettings(
                choice.Kind switch
                {
                    DeskInkKind.Light => DeskInkMode.Light,
                    DeskInkKind.Dark => DeskInkMode.Dark,
                    DeskInkKind.Theme => DeskInkMode.Theme,
                    _ => DeskInkMode.Custom,
                },
                string.Join(",", choice.Colors), merged.Shadow ?? true);
            ink = DeskInk.Resolve(settings, themeText, null);
            plate = merged.Plate ?? false;
            stopLum = ink.Stops.Select(h => InkHex.Luminance(h)).ToArray();
        }
        // A followed look without its plate gets it where its ink would not read (the guard): judged again on the plate first.
        bool guard = look is not null && !plate;

        // A change of ink, plate, theme or following touches every placed piece; a layout change only the pieces it names.
        var named = proposal.Layout?.Select(p => p.Piece).ToHashSet(StringComparer.Ordinal);
        bool everything = inkChanged || plateChanged || followChanged || themeChanged;
        bool launcherLabelled = merged.Launcher?.Buttons.Any(b => b.Label is not null) == true;
        string from = inkChanged ? "ink" : plateChanged ? "plate" : followChanged ? "follows_theme" : themeChanged ? "theme" : "layout";
        // A followed look's failure belongs to the following, whichever field set it off.
        if (look is not null && from != "layout") from = "follows_theme";

        int found = 0;
        foreach (var p in merged.Layout ?? [])
        {
            // The message has its own color; the glow is no text; the companion is the pack's art, not the ink.
            if (p.IsHidden || p.Piece is "message" or "glow" or "companion") continue;
            if (p.Piece == LauncherModel.Widget && !launcherLabelled) continue;   // icons do not take the ink
            if (!everything && named?.Contains(p.Piece) != true) continue;
            if (found >= MaxContrastReasons) break;

            var anchor = p.Anchor ?? DeskArrangement.DefaultAnchor(p.Piece);
            (double Worst, double Bg) Judge(bool onPlate)
            {
                double worst = double.MaxValue, worstBg = 0;
                foreach (var m in MonitorsOf(wall, p.Monitor))
                {
                    double under = m.Summary.At(anchor)?.Brightness ?? m.Summary.Brightness;
                    if (onPlate) under = UnderPlate(under, ink.DarkInk);
                    foreach (var l in stopLum)
                    {
                        double ratio = Ratio(l, under);
                        if (ratio < worst) { worst = ratio; worstBg = under; }
                    }
                }
                return (worst, worstBg);
            }
            bool plated = plate;
            var (worstRatio, worstBack) = Judge(plate);
            if (worstRatio < MinContrast && guard)
            {
                (worstRatio, worstBack) = Judge(true);
                plated = true;
            }
            if (worstRatio >= MinContrast) continue;

            string field = from == "layout" ? $"layout.{p.Piece}" : from;
            // Black beats white as the ground gets brighter than about 0.18 (where (L + .05)^2 = 1.05 * .05).
            string fix = worstBack > 0.179 ? "a darker ink" : "a lighter ink";
            reasons.Add(new DeskReason(field,
                $"the {p.Piece} piece at {DeskAnchors.Word(anchor)} ({DeskAnchors.Name(anchor).ToLowerInvariant()}): the ink would read at "
                + $"{worstRatio.ToString("0.0", CultureInfo.InvariantCulture)}:1 against the wallpaper there "
                + $"(brightness {worstBack.ToString("0.00", CultureInfo.InvariantCulture)}{(plated ? ", with the plate on" : "")}); "
                + $"the Desk needs {MinContrast.ToString("0", CultureInfo.InvariantCulture)}:1. Choose {fix}, {(plated ? "" : "turn the plate on, ")}or move the piece."));
            found++;
        }
    }

    /// <summary>The wallpaper monitors a piece is judged against: every one for "all monitors"; the primary for "main"; the one with that id.
    /// A monitor the summary does not know (unplugged, or an older state with no ids) is judged against all of them, the safe side.</summary>
    internal static IEnumerable<WallpaperMonitorSummary> MonitorsOf(WallpaperOverview wall, string? monitor)
    {
        switch (DeskMonitors.FromWord(monitor))
        {
            case null:
                return wall.Monitors;
            case DeskMonitors.Main:
                return wall.Monitors.Where(m => m.Primary) is var main && main.Any() ? main : wall.Monitors;
            case var id:
                return wall.Monitors.Where(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase)) is var one && one.Any() ? one : wall.Monitors;
        }
    }

    /// <summary>WCAG contrast ratio of two relative luminances (1 to 21).</summary>
    public static double Ratio(double a, double b)
    {
        double hi = Math.Max(a, b), lo = Math.Min(a, b);
        return (hi + 0.05) / (lo + 0.05);
    }

    /// <summary>The luminance of a wallpaper of luminance <paramref name="wallpaper"/> under the plate: its color
    /// (<see cref="DeskPlate.ColorHex"/>) at <see cref="DeskPlate.Opacity"/>, composited as the screen does, in sRGB values.</summary>
    public static double UnderPlate(double wallpaper, bool darkInk)
    {
        double plate = DeskPlate.ColorHex(darkInk) == "ffffff" ? 1 : 0;
        double v = (1 - DeskPlate.Opacity) * Encode(wallpaper) + DeskPlate.Opacity * plate;
        return Decode(v);
    }

    private static double Encode(double linear) =>
        linear <= 0.0031308 ? 12.92 * linear : 1.055 * Math.Pow(Math.Clamp(linear, 0, 1), 1 / 2.4) - 0.055;

    private static double Decode(double v) =>
        v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
}
