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
    /// Every card, in step order. The Desk is not on Windows yet, so the tour has four steps
    /// numbered 1, 3, 4 and 5. A later stage adds the "desk" card at step 2 with
    /// Requires = TourFeature.Desk.
    /// </summary>
    public static IReadOnlyList<TourCard> Table { get; } =
    [
        new(1, new WhatsNewCard([], "paced", "Your limits, paced",
            "Each bar is one of your limits, filling as you use it. The tick marks where an even pace would put you right now.",
            "\uE9D2", WhatsNewPreview.Meters, SettingsPage.Widget)),
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
