namespace Sanduhr.Core;

/// <summary>A feature a tour card may need; a card needing a missing feature hides itself.</summary>
public enum TourFeature { Desk }

/// <summary>A real setting a tour card changes in place.</summary>
public enum TourChoice { None, TrayMode }

/// <summary>Whether the tour was offered, and how it ended.</summary>
public enum TourState { Pending, Finished, Skipped, NotOffered }

/// <summary>
/// One card of the tour, reusing What's New's card (id, title, body, glyph, preview, destination).
/// Versions is empty for tour cards.
/// </summary>
public sealed record TourCard(int Step, WhatsNewCard Card, TourChoice Choice = TourChoice.None,
    TourFeature? Requires = null, string ShowMeTitle = "Show me")
{
    public string Id => Card.Id;
}

/// <summary>
/// The welcome tour: which steps show, who sees it once, and how it ends. Pure; port of the Mac's
/// WelcomeTour.swift.
/// </summary>
public static class WelcomeTour
{
    /// <summary>The settings key holding the tour's state.</summary>
    public const string StateKey = "tour_state";

    public const string AgainLine = "You can take the tour again from Settings, About, or the tray menu.";

    public const string SkipTitle = "Skip the tour";

    /// <summary>
    /// Every card, in step order: five steps numbered 1 to 5. The Desk is on every Windows build,
    /// so its card needs no feature.
    /// </summary>
    public static IReadOnlyList<TourCard> Table { get; } =
    [
        new(1, new WhatsNewCard([], "paced", "Your limits, paced",
            "Each bar is one of your limits, filling as you use it. The tick marks where an even pace would put you right now.",
            "\uE9D2", WhatsNewPreview.Meters, SettingsPage.Widget)),
        new(2, new WhatsNewCard([], "desk", "On your desktop",
            "The Desk puts a clock, your meters, a message and more on the desktop, under every window. Arrange each piece from Settings, Desk.",
            "\uE7F4", WhatsNewPreview.None, SettingsPage.Desk)),
        new(3, new WhatsNewCard([], "tray", "At a glance",
            "The hourglass in the tray shows a percent: your session, your weekly limit, whichever is higher, both in turn, or the highest of any.",
            "\uE7C4", WhatsNewPreview.Tray, SettingsPage.General), TourChoice.TrayMode),
        new(4, new WhatsNewCard([], "accounts", "More than one account",
            "Add each Claude account with its own sign-in and history, then switch from the name chip or the tray's Accounts menu.",
            "\uE716", WhatsNewPreview.AccountChip, SettingsPage.Accounts)),
        new(5, new WhatsNewCard([], "claude-code", "Claude Code, connected",
            "The Claude Usage page shows your Claude Code tokens by day and project. The Claude Code page adds the MCP tools and the statusline, each after you say yes.",
            "\uEA86", WhatsNewPreview.None, SettingsPage.ClaudeCode)),
    ];

    /// <summary>
    /// The tour for someone who chose Just the Desk: three steps with no meters, accounts or Claude
    /// Code in them. The desk and tray cards are the table's, moved up a step (the tray body is
    /// rewritten for a tray with no numbers); a last card says the Desk stands alone and where
    /// sign-in lives.
    /// </summary>
    public static IReadOnlyList<TourCard> DeskOnly { get; } =
    [
        Moved(ById("desk"), 1),
        Moved(ById("tray"), 2,
            "The hourglass in the tray is the way back to Settings and the Desk's pages. Once you sign in it shows a percent of your limits."),
        new(3, new WhatsNewCard([], "sign-in", "Sign in any time",
            "The Desk works without a Claude account. Sign in from Settings, Accounts whenever you have one, and the meters fill in on the desktop.",
            "", WhatsNewPreview.None, SettingsPage.Accounts), ShowMeTitle: "Accounts"),
    ];

    private static TourCard ById(string id) => Table.Single(c => c.Id == id);

    /// <summary><paramref name="card"/> at <paramref name="step"/>, with <paramref name="body"/> when one is given.</summary>
    private static TourCard Moved(TourCard card, int step, string? body = null) =>
        card with
        {
            Step = step,
            Card = body is null
                ? card.Card
                : new WhatsNewCard([], card.Card.Id, card.Card.Title, body, card.Card.Glyph,
                    card.Card.Preview, card.Card.Destination),
        };

    /// <summary>
    /// The steps to show: the Desk-only tour while Just the Desk is on (it has no meters step, so
    /// signing out changes nothing in it), the full tour otherwise.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<TourCard>> StepsFor(bool justTheDesk,
        IReadOnlySet<TourFeature> features, bool signedIn = true) =>
        justTheDesk ? Steps(features, DeskOnly) : Steps(features, signedIn: signedIn);

    /// <summary>
    /// Choosing Just the Desk offers the tour once a launch, while it is still pending (no fetch is
    /// coming to offer it).
    /// </summary>
    public static bool ShowsForJustTheDesk(TourState? state, bool offeredThisLaunch) =>
        state == TourState.Pending && !offeredThisLaunch;

    /// <summary>The cards this machine shows: features met, sorted by step, ties in table order.</summary>
    public static IReadOnlyList<TourCard> Cards(IReadOnlySet<TourFeature> features,
        IReadOnlyList<TourCard>? table = null) =>
        (table ?? Table)
            .Where(c => c.Requires is null || features.Contains(c.Requires.Value))
            .OrderBy(c => c.Step) // OrderBy is stable
            .ToArray();

    /// <summary>The cards grouped by step, in order; signed out, step 1 invites a sign-in.</summary>
    public static IReadOnlyList<IReadOnlyList<TourCard>> Steps(IReadOnlySet<TourFeature> features,
        IReadOnlyList<TourCard>? table = null, bool signedIn = true)
    {
        var steps = new List<IReadOnlyList<TourCard>>();
        var current = new List<TourCard>();
        foreach (var raw in Cards(features, table))
        {
            var card = signedIn ? raw : SignedOut(raw);
            if (current.Count > 0 && current[0].Step != card.Step)
            {
                steps.Add(current.ToArray());
                current = [];
            }
            current.Add(card);
        }
        if (current.Count > 0) steps.Add(current.ToArray());
        return steps;
    }

    /// <summary>Step 1 with no account: it sends to Sign in instead of showing meters. Other cards are unchanged.</summary>
    public static TourCard SignedOut(TourCard card) =>
        card.Step != 1
            ? card
            : card with
            {
                Card = new WhatsNewCard([], card.Card.Id, card.Card.Title,
                    "Sign in to see your meters here. Each bar is one of your limits, with a tick where an even pace would put you.",
                    card.Card.Glyph, card.Card.Preview, SettingsPage.Accounts),
                ShowMeTitle = SettingsNames.SignIn,
            };

    /// <summary>"1 of 4" for the 0-based index 0 of 4.</summary>
    public static string StepText(int index, int count) => $"{index + 1} of {count}";

    /// <summary>The stored form of a state.</summary>
    public static string Format(TourState state) => state switch
    {
        TourState.Pending => "pending",
        TourState.Finished => "finished",
        TourState.Skipped => "skipped",
        TourState.NotOffered => "not_offered",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    /// <summary>A stored state, exact lower-case; anything else is null.</summary>
    public static TourState? Parse(string? value) => value switch
    {
        "pending" => TourState.Pending,
        "finished" => TourState.Finished,
        "skipped" => TourState.Skipped,
        "not_offered" => TourState.NotOffered,
        _ => null,
    };

    /// <summary>
    /// Decided once: an existing state stands. Otherwise Pending only on a first-ever launch with no
    /// account and nothing seen in What's New; any other launch is NotOffered.
    /// </summary>
    public static TourState Decide(TourState? existing, bool fresh, int accounts, string? lastSeen) =>
        existing ?? (fresh && accounts == 0 && string.IsNullOrEmpty(lastSeen)
            ? TourState.Pending
            : TourState.NotOffered);

    /// <summary>The tour shows after the first successful fetch, once a launch, only while Pending.</summary>
    public static bool ShowsAfterFetch(TourState? state, bool fetched, bool offeredThisLaunch) =>
        state == TourState.Pending && fetched && !offeredThisLaunch;

    /// <summary>
    /// How a tour ends: only a Pending state ends, as Finished or Skipped. Any other state returns
    /// null, meaning record nothing; the caller records What's New's last-seen only when this is non-null.
    /// </summary>
    public static TourState? End(TourState? state, bool finished) =>
        state == TourState.Pending ? (finished ? TourState.Finished : TourState.Skipped) : null;
}
