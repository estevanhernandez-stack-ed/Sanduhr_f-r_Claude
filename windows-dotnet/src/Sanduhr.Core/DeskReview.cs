namespace Sanduhr.Core;

/// <summary>A font a previewed design names that this PC lacks, with the link to get it if the design gave a safe one.</summary>
public sealed record DeskReviewFont(string Family, string? Link)
{
    /// <summary>The line the bar shows.</summary>
    public string Line => Link is null
        ? $"{Family} is not installed. Install it to apply this design."
        : $"{Family} is not installed. Get it from {Link}, then install it.";
}

/// <summary>The words on the review bar and its buttons (the bar's accessible names are these).</summary>
public static class DeskReviewCopy
{
    public const string Apply = "Apply";
    public const string Dismiss = "Dismiss";
    public const string Edit = "Edit";
    public const string BarName = "Review Claude's Desk design";
    public const string ChangesName = "What changed";
    public const string FontsName = "Fonts to install";
    public const string KeysLine = "Enter applies. Escape dismisses.";
    public const string KeysLineWaiting = "Escape dismisses. Apply waits until the font is installed.";
    public const string NothingChanges = "This design leaves your Desk as it is.";
    public const string DeskOffNote = "Your Desk is off. It shows for this preview only; Apply does not turn it on.";
    public const string Untitled = "A new look for your Desk";
    public const string ApplyWaitingName = "Apply, waiting for a font to be installed";

    /// <summary>The help text of the Apply button.</summary>
    public static string ApplyHelp(bool canApply) => canApply ? "Apply this design to your Desk and widget" : "Install the missing font to apply this design";
}

/// <summary>
/// What the live review's floating bar shows (spec, the agent-designed Desk, the live review): the design's name, Claude's note,
/// the "what changed" lines, the fonts still missing with their links, and whether Apply is available. Pure: the App draws it.
/// </summary>
public sealed record DeskReviewModel(
    string Title,
    string? Note,
    IReadOnlyList<string> Changes,
    IReadOnlyList<DeskReviewFont> MissingFonts,
    bool DeskOff,
    IReadOnlyList<string> Warnings)
{
    /// <summary>The small line over the title; null is the Desk design's ("Claude suggests a Desk design"). A widget under review
    /// (spec 3.8 section 5) says "Claude suggests a widget · name".</summary>
    public string? Heading { get; init; }

    /// <summary>The header over the lines; null is "What changed".</summary>
    public string? ChangesTitle { get; init; }

    /// <summary>The bar's and its window's accessible name; null is <see cref="DeskReviewCopy.BarName"/>.</summary>
    public string? BarName { get; init; }

    /// <summary>One choice the user makes with Apply, as a check box (a Claude-made sprite's "Make it the companion"); null for none.</summary>
    public string? Option { get; init; }

    /// <summary>The bar shows an Edit button (the drawers review: untick moves, rename a new drawer).</summary>
    public bool CanEdit { get; init; }

    /// <summary>Apply's and Dismiss's words when not "Apply" and "Dismiss" (the drawers result: "Undo" and "Done"); null keeps them.</summary>
    public string? ApplyLabel { get; init; }
    public string? DismissLabel { get; init; }

    /// <summary>Apply is off for a reason other than a missing font (a result with nothing to undo).</summary>
    public bool ApplyOff { get; init; }

    /// <summary>Apply is available once every font the design names is installed.</summary>
    public bool CanApply => MissingFonts.Count == 0 && !ApplyOff;

    /// <summary>The keys line when it is not the design review's (the drawers result: "Enter undoes. Escape closes."); null keeps it.</summary>
    public string? KeysLine { get; init; }

    /// <summary>The keys line under the changes.</summary>
    public string Keys => KeysLine ?? (CanApply ? DeskReviewCopy.KeysLine : DeskReviewCopy.KeysLineWaiting);

    /// <summary>A link the bar may show and open: absolute https only, no user info; anything else is dropped.</summary>
    public static string? SafeLink(string? link)
    {
        if (string.IsNullOrWhiteSpace(link) || link.Length > 300) return null;
        if (!Uri.TryCreate(link.Trim(), UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) || link.Any(char.IsControl)) return null;
        return uri.AbsoluteUri;
    }

    /// <summary>
    /// The bar's model for a proposal. <paramref name="current"/> is the Desk now and <paramref name="merged"/> what it would become.
    /// <paramref name="fonts"/> are the fonts the check found missing; a font installed since (<paramref name="fontInstalled"/>)
    /// drops off, which is how Apply wakes up when the font arrives.
    /// </summary>
    public static DeskReviewModel Build(
        DeskDesign proposal, DeskDesign current, DeskDesign merged, IEnumerable<MissingFont> fonts,
        Func<string, bool> fontInstalled, bool deskOn, Func<string, string>? pieceName = null,
        IEnumerable<DeskReason>? warnings = null, Func<string, string?>? monitorName = null)
    {
        var changes = DeskDesign.Diff(current, merged, pieceName, monitorName).ToList();
        if (changes.Count == 0) changes.Add(DeskReviewCopy.NothingChanges);
        var missing = fonts
            .Where(f => !fontInstalled(f.Family))
            .Select(f => new DeskReviewFont(f.Family, SafeLink(f.Link)))
            .ToList();
        return new DeskReviewModel(
            string.IsNullOrWhiteSpace(proposal.Name) ? DeskReviewCopy.Untitled : proposal.Name!,
            string.IsNullOrWhiteSpace(proposal.Note) ? null : proposal.Note,
            changes, missing, !deskOn,
            warnings?.Select(w => w.ToString()).ToList() ?? []);
    }
}
