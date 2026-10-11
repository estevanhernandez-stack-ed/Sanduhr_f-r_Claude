namespace Sanduhr.Core;

/// <summary>The Settings pages a What's New card's "Show me" can open (Widget shows the widget).</summary>
public enum SettingsPage { Accounts, Themes, Alerts, History, ClaudeUsage, Publish, General, Widget }

/// <summary>The small live previews a card can ask for in place of a glyph.</summary>
public enum WhatsNewPreview { None, Tray, Sparkline, Shortcut }

/// <summary>
/// One highlight: what changed, in a sentence or two, and where "Show me" goes. A card may cover
/// more than one release; it sits with the newest of them in the table.
/// </summary>
public sealed record WhatsNewCard
{
    /// <summary>Every release it covers, newest first.</summary>
    public IReadOnlyList<string> Versions { get; }
    public string Id { get; }
    public string Title { get; }
    public string Body { get; }
    /// <summary>A Segoe Fluent Icons codepoint; previews still carry one as a fallback.</summary>
    public string Glyph { get; }
    public WhatsNewPreview Preview { get; }
    public SettingsPage Destination { get; }

    public WhatsNewCard(IReadOnlyList<string> versions, string id, string title, string body,
        string glyph, WhatsNewPreview preview, SettingsPage destination)
    {
        // OrderBy is stable; equal versions keep their given order.
        Versions = versions.OrderByDescending(v => v, Comparer<string>.Create(WhatsNew.Compare)).ToArray();
        Id = id;
        Title = title;
        Body = body;
        Glyph = glyph;
        Preview = preview;
        Destination = destination;
    }

    /// <summary>A card of one release.</summary>
    public static WhatsNewCard Of(string version, string id, string title, string body, string glyph,
        WhatsNewPreview preview, SettingsPage destination) =>
        new([version], id, title, body, glyph, preview, destination);

    /// <summary>The newest release it covers.</summary>
    public string Version => Versions.Count > 0 ? Versions[0] : "";
}

/// <summary>
/// What's New after an update: which release highlights to show. Pure; port of the Mac's WhatsNew.swift.
/// </summary>
public static class WhatsNew
{
    /// <summary>Most cards shown automatically after an update, newest first.</summary>
    public const int Cap = 8;

    /// <summary>
    /// Dotted versions compared number by number ("2.10.0" after "2.9.1"); a missing part is 0 and
    /// anything after a part's leading digits ("0-mac", "1b") is ignored.
    /// </summary>
    public static int Compare(string a, string b)
    {
        var x = Numbers(a);
        var y = Numbers(b);
        for (var i = 0; i < Math.Max(x.Length, y.Length); i++)
        {
            var l = i < x.Length ? x[i] : 0;
            var r = i < y.Length ? y[i] : 0;
            if (l != r) return l < r ? -1 : 1;
        }
        return 0;
    }

    private static int[] Numbers(string v) =>
        v.Trim().Split('.', StringSplitOptions.RemoveEmptyEntries).Select(part =>
        {
            var digits = new string(part.TakeWhile(c => c >= '0' && c <= '9').ToArray());
            return int.TryParse(digits, out var n) ? n : 0;
        }).ToArray();

    /// <summary>The releases of <paramref name="card"/> newer than lastSeen and not past current, newest first.</summary>
    public static IReadOnlyList<string> Shown(WhatsNewCard card, string? lastSeen, string current) =>
        card.Versions
            .Where(v => Compare(v, current) <= 0 && (lastSeen is null || Compare(v, lastSeen) > 0))
            .ToArray();

    /// <summary>
    /// The cards of releases newer than lastSeen, up to and including current, newest release first
    /// and in table order within one, at most <paramref name="limit"/> (null = no cap).
    /// </summary>
    public static IReadOnlyList<WhatsNewCard> Cards(string? lastSeen, string current, int? limit = Cap,
        IReadOnlyList<WhatsNewCard>? table = null)
    {
        table ??= WhatsNewCards.Table;
        var picked = new List<(WhatsNewCard Card, int Index, string Newest)>();
        for (var i = 0; i < table.Count; i++)
        {
            var shown = Shown(table[i], lastSeen, current);
            if (shown.Count > 0) picked.Add((table[i], i, shown[0]));
        }
        picked.Sort((a, b) =>
        {
            var order = Compare(b.Newest, a.Newest); // newest first
            return order != 0 ? order : a.Index.CompareTo(b.Index); // ties keep table order
        });
        var newestFirst = picked.Select(p => p.Card);
        return (limit is null ? newestFirst : newestFirst.Take(Math.Max(0, limit.Value))).ToArray();
    }

    /// <summary>"New in 3.4.0 – 3.5.0", or "New in 3.5.0" when that is the only release; null with no cards.</summary>
    public static string? RangeLabel(IReadOnlyList<WhatsNewCard> cards, string? lastSeen, string current)
    {
        var versions = cards.SelectMany(c => Shown(c, lastSeen, current)).ToArray();
        if (versions.Length == 0) return null;
        var oldest = versions[0];
        foreach (var v in versions) if (Compare(v, oldest) < 0) oldest = v;
        return Compare(oldest, current) == 0 ? $"New in {current}" : $"New in {oldest} – {current}";
    }

    /// <summary>Every card up to current, newest first.</summary>
    public static IReadOnlyList<WhatsNewCard> All(string current, IReadOnlyList<WhatsNewCard>? table = null) =>
        Cards(null, current, null, table);

    /// <summary>
    /// What's New... from the tray and Settings: on the installed version, before any update, that
    /// version's own cards as "Highlights in x"; otherwise every card under its range.
    /// </summary>
    public static (IReadOnlyList<WhatsNewCard> Cards, string Header) ForMenu(string current, string? installed,
        IReadOnlyList<WhatsNewCard>? table = null)
    {
        table ??= WhatsNewCards.Table;
        if (installed is not null && Compare(installed, current) == 0)
        {
            var own = table.Where(c => c.Versions.Any(v => Compare(v, current) == 0)).ToArray();
            if (own.Length > 0) return (own, $"Highlights in {current}");
        }
        var every = All(current, table);
        return (every, RangeLabel(every, null, current) ?? $"Version {current}");
    }

    /// <summary>What a launch does: the cards to show and whether to record current as seen.</summary>
    public sealed record LaunchDecision(IReadOnlyList<WhatsNewCard> Show, bool Record);

    /// <summary>
    /// A fresh install records the version and shows nothing. While onboarding or the tour is up,
    /// nothing happens. Otherwise the missed cards show once and the version is recorded; hidden
    /// only records.
    /// </summary>
    public static LaunchDecision AtLaunch(string? lastSeen, string current, bool fresh, bool onboarding,
        bool hidden, bool tour = false, IReadOnlyList<WhatsNewCard>? table = null)
    {
        if (fresh) return new LaunchDecision([], true);
        if (onboarding || tour) return new LaunchDecision([], false);
        IReadOnlyList<WhatsNewCard> missed = hidden ? [] : Cards(lastSeen, current, Cap, table);
        return new LaunchDecision(missed, lastSeen != current);
    }
}
