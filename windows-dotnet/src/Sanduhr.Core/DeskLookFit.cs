namespace Sanduhr.Core;

/// <summary>A look fitted to this PC: the design to draw and apply, and what the fit changed, one sentence each, font first.</summary>
public sealed record DeskLookFitResult(DeskDesign Design, IReadOnlyList<string> Notes);

/// <summary>A piece a look shows that has nothing to draw yet, and the line that says so.</summary>
public sealed record DeskLookNeed(string Piece, string Line);

/// <summary>
/// Fits a ready-made look to this PC (5.0 spec 5, sections 4 and 5), and tells whether the Desk still is a look. Pure.
/// A look is ours and is applied in one click with no review, so it must always pass the rules a design is held to: the fit
/// swaps a missing font and steps the ink down a ladder (as designed, then the plate on, then automatic ink) until the validator's
/// own contrast judgement is satisfied. A fitted look always passes <see cref="DeskDesign.Validate(DeskDesign, DeskDesignContext)"/>
/// over the same context.
/// </summary>
public static class DeskLookFit
{
    public const string PlateNote = "Uses its plate on your wallpaper.";
    public const string AutoInkNote = "Uses automatic ink on your wallpaper.";

    /// <summary>The note for a font the fit swapped in: the family, or the system font for "".</summary>
    public static string FontNote(string family) =>
        family.Length == 0 ? "Uses the system font on this PC." : $"Uses {family} on this PC.";

    private static bool Has(IReadOnlyCollection<string> installed, string family) =>
        family.Length == 0 || BundledFonts.Bundled(family) is not null || installed.Contains(family, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The look as this PC can draw it. <paramref name="context"/> gives the installed fonts (null: unknown, the font is kept), the
    /// wallpaper (null or empty: the look is used as designed, as the validator does without one) and the Desk as it is now, which
    /// the contrast is judged over: the user's labelled Launcher buttons and each piece's monitor count.
    /// </summary>
    public static DeskLookFitResult Fit(DeskLook look, DeskDesignContext context)
    {
        var design = look.Design;
        var notes = new List<string>();

        if (context.InstalledFonts is { } installed && design.Font is { } font && !Has(installed, font.Family))
        {
            string stand = Has(installed, look.FontFallback) ? look.FontFallback : "";
            design = design with { Font = new DeskFontChoice(stand) };
            notes.Add(FontNote(stand));
        }

        // Automatic ink follows the wallpaper by definition, so it is never adjusted.
        if (design.Ink is { Kind: not DeskInkKind.Auto } && context.Wallpaper is { Monitors.Count: > 0 }
            && DeskDesign.ContrastReasons(design, context).Count > 0)
        {
            var plated = design with { Plate = true };
            if (design.Plate != true && DeskDesign.ContrastReasons(plated, context).Count == 0)
            {
                design = plated;
                notes.Add(PlateNote);
            }
            else
            {
                design = design with { Ink = DeskInkChoice.Of(DeskInkKind.Auto) };
                notes.Add(AutoInkNote);
            }
        }
        return new DeskLookFitResult(design, notes);
    }

    /// <summary>
    /// The layout to write for the look over the Desk as it is: the look's pieces in the file's order (which is their stacking
    /// order), each shown piece on the monitor the user gave it (a piece that was hidden comes up on every monitor), then any piece
    /// the look does not name, as it is. <see cref="DeskDesign.Merge"/> is not used for this: it places a piece into a stack by
    /// Settings order and leaves a piece already at its anchor where it was, so one look would stack differently on two Desks.
    /// </summary>
    public static IReadOnlyList<DeskPieceLayout> LayoutOver(DeskLook look, DeskDesign? current)
    {
        var now = current?.Layout ?? [];
        var named = Named(look);
        var list = (look.Design.Layout ?? [])
            .Select(p => p.IsHidden ? p : p with { Monitor = now.FirstOrDefault(c => c.Piece == p.Piece && !c.IsHidden)?.Monitor })
            .ToList();
        list.AddRange(now.Where(c => !named.Contains(c.Piece)));
        return list;
    }

    private static HashSet<string> Named(DeskLook look) =>
        (look.Design.Layout ?? []).Select(p => p.Piece).ToHashSet(StringComparer.Ordinal);

    /// <summary>The look's pieces of a layout as one comparable string: the placed ones in order, each with its anchor, size, tilt
    /// and style, and no monitor (a piece's monitor is the user's).</summary>
    private static string Words(DeskLook look, IEnumerable<DeskPieceLayout> layout)
    {
        var named = Named(look);
        return DeskDesignApply.Layout([.. layout.Where(p => named.Contains(p.Piece)).Select(p => p with { Monitor = null })]);
    }

    private static bool SameArrangement(DeskLook look, DeskDesign current) =>
        current.Layout is { } now && Words(look, now) == Words(look, look.Design.Layout ?? [])
        && (current.Shadow ?? true) == (look.Design.Shadow ?? true)
        && SameGlow(look.Design.Glow, current.Glow)
        && current.FollowsTheme != true;

    /// <summary>Off matches off; on at a strength matches that strength; plain on matches any strength.</summary>
    private static bool SameGlow(DeskGlowChoice? look, DeskGlowChoice? current)
    {
        bool want = look is { On: true }, has = current is { On: true };
        if (want != has) return false;
        return !want || look!.Strength is not { } s || (current!.Strength is { } c && Math.Abs(c - s) < 0.005);
    }

    private static bool SameFont(string? a, string? b) => string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);

    private static bool SameInk(DeskInkChoice? a, DeskInkChoice? b) =>
        a is not null && b is not null && a.Kind == b.Kind && string.Equals(a.Describe(), b.Describe(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The Desk is this look, untouched (spec section 5, "Untouched"). Judged on what a look carries and nothing else: the layout
    /// of the pieces it names (order, anchor, size, tilt, style), the shadow, the glow and not following the theme are the look's;
    /// the font is the look's or its fallback (or the system font, on a PC with neither); the ink is the look's or automatic; the
    /// plate may be either. Those last three are exactly what fitting may change, so a look applied over one wallpaper is still
    /// recognised after the wallpaper changes. The user's Launcher, messages, sources and monitors never count.
    /// </summary>
    public static bool IsLook(DeskLook look, DeskDesign current, IReadOnlyCollection<string>? installedFonts)
    {
        if (!SameArrangement(look, current)) return false;
        string? font = current.Font?.Family;
        bool fontOk = SameFont(font, look.Design.Font?.Family) || SameFont(font, look.FontFallback)
            || (SameFont(font, "") && installedFonts is { } installed
                && !Has(installed, look.Design.Font?.Family ?? "x") && !Has(installed, look.FontFallback));
        if (!fontOk) return false;
        return current.Ink is { Kind: DeskInkKind.Auto } || SameInk(current.Ink, look.Design.Ink);
    }

    /// <summary>The Desk is this fit of the look: nothing would change if it were applied again.</summary>
    public static bool IsExactly(DeskLook look, DeskLookFitResult fit, DeskDesign current) =>
        SameArrangement(look, current)
        && SameFont(current.Font?.Family, fit.Design.Font?.Family)
        && SameInk(current.Ink, fit.Design.Ink)
        && (current.Plate ?? false) == (fit.Design.Plate ?? false);

    /// <summary>The look the Desk is, untouched, or null when it is the user's own.</summary>
    public static DeskLook? Untouched(IEnumerable<DeskLook> looks, DeskDesign current, IReadOnlyCollection<string>? installedFonts) =>
        looks.FirstOrDefault(l => IsLook(l, current, installedFonts));

    /// <summary>The pieces the look shows that have nothing to draw yet, in the order Settings lists them. The Claude line and the
    /// meters are not here: they say so themselves on the Desk when no one is signed in.</summary>
    public static IReadOnlyList<DeskLookNeed> Needs(DeskLook look, bool weatherHasPlace, bool launcherHasButtons, bool hasWatcher)
    {
        var needs = new List<DeskLookNeed>();
        if (look.Shows("weather") && !weatherHasPlace) needs.Add(new DeskLookNeed("weather", "Weather needs a place."));
        if (look.Shows("launcher") && !launcherHasButtons) needs.Add(new DeskLookNeed("launcher", "The Launcher needs buttons."));
        if (look.Shows("watchers") && !hasWatcher) needs.Add(new DeskLookNeed("watchers", "Watchers needs a watcher."));
        return needs;
    }
}
