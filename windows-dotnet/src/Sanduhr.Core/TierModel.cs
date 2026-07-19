namespace Sanduhr.Core;

/// <summary>
/// A synthesized Routines daily-quota budget. Routines is a count-based card
/// (e.g. "3/15"), not a subscription-tier percentage, so it carries its raw
/// <see cref="Used"/> / <see cref="Limit"/> alongside a derived utilization
/// for the progress bar. Ported from the fetcher's Routines-synthesis branch
/// (<c>fetcher.py</c>): the API exposes no specific reset time for the daily
/// quota, so <c>resets_at</c> is always null for this tier.
/// </summary>
public sealed record RoutineBudget(int Used, int Limit)
{
    /// <summary>Derived utilization in [0,100] for the bar fill: used / limit * 100.</summary>
    public double Utilization => Limit > 0 ? (double)Used / Limit * 100.0 : 0.0;

    /// <summary>Count-based value label the card shows in place of "NN%" — e.g. "3/15".</summary>
    public string CountLabel => $"{Used}/{Limit}";
}

/// <summary>
/// The canonical tier registry + the pure derivations a tier card consumes,
/// ported from the Python build's tier logic spread across <c>widget.py</c>
/// (labels, ordering, hide/reorder) and <c>fetcher.py</c> (the tier set +
/// Routines synthesis). The <c>future use</c> speculative-tier set is lifted
/// from <c>history_chart.py</c>. Pure — no WPF, no IO. All rendering
/// (TierCard.xaml) is item 5 and lives in the App project.
///
/// Tier JSON keys match the Python build verbatim so usage dicts parse
/// identically across both apps.
/// </summary>
public static class TierModel
{
    // -- canonical tier keys --------------------------------------------------

    public const string FiveHour = "five_hour";
    public const string SevenDay = "seven_day";
    public const string SevenDaySonnet = "seven_day_sonnet";
    public const string SevenDayOpus = "seven_day_opus";
    public const string SevenDayFable = "seven_day_fable";
    public const string SevenDayCowork = "seven_day_cowork";
    public const string SevenDayOmelette = "seven_day_omelette";
    public const string SevenDayOauthApps = "seven_day_oauth_apps";
    public const string IguanaNecktie = "iguana_necktie";
    public const string ExtraUsage = "extra_usage";
    public const string Routines = "routines";

    /// <summary>
    /// Canonical display order, matching <c>widget._TIER_LABELS</c> insertion
    /// order and <c>fetcher._HISTORY_TIERS</c> exactly: five_hour, the
    /// seven_day family (all-models then the sonnet/opus/cowork/omelette/
    /// oauth_apps sub-tiers), the iguana_necktie codename, extra_usage, then
    /// the synthesized routines tier last.
    /// </summary>
    public static readonly IReadOnlyList<string> CanonicalOrder = new[]
    {
        FiveHour,
        SevenDay,
        SevenDaySonnet,
        SevenDayOpus,
        SevenDayFable,
        SevenDayCowork,
        SevenDayOmelette,
        SevenDayOauthApps,
        IguanaNecktie,
        ExtraUsage,
        Routines,
    };

    // Display labels — ported from widget.py's _TIER_LABELS (the live widget
    // surface that item 5's TierCard consumes). history_chart.py uses an
    // em-dash variant ("Weekly — Sonnet"); the widget surface uses the ASCII
    // hyphen form below, which is authoritative for the cards.
    private static readonly IReadOnlyDictionary<string, string> LabelsByKey =
        new Dictionary<string, string>
        {
            [FiveHour] = "Session (5hr)",
            [SevenDay] = "Weekly - All Models",
            [SevenDaySonnet] = "Weekly - Sonnet",
            [SevenDayOpus] = "Weekly - Opus",
            [SevenDayFable] = "Weekly - Fable",
            [SevenDayCowork] = "Weekly - Cowork",
            [SevenDayOmelette] = "Weekly - Design",
            [SevenDayOauthApps] = "Weekly - OAuth Apps",
            [IguanaNecktie] = "Weekly - Special",
            [ExtraUsage] = "Capped Extra Usage",
            [Routines] = "Daily Routines",
        };

    /// <summary>
    /// Tier keys the upstream API references but for which most consumer
    /// accounts return null — codenames (iguana_necktie, omelette) and
    /// entitlement-gated rows (cowork, oauth_apps). The render filter already
    /// drops null-utilization tiers, so these stay checked by default; the
    /// Settings list tags them with <see cref="FutureUseTag"/> so the user
    /// understands why they may never show data. Lifted from
    /// <c>history_chart.SPECULATIVE_TIERS</c>.
    /// </summary>
    public static readonly IReadOnlySet<string> SpeculativeTiers =
        new HashSet<string>
        {
            SevenDayCowork,
            SevenDayOmelette,
            SevenDayOauthApps,
            IguanaNecktie,
        };

    /// <summary>The soft tag appended to a speculative tier's label (settings_dialog.py).</summary>
    public const string FutureUseTag = "future use";

    // -- registry accessors ---------------------------------------------------

    /// <summary>True when <paramref name="tierKey"/> is a recognized tier (static or dynamically registered).</summary>
    public static bool IsKnown(string tierKey)
    {
        if (LabelsByKey.ContainsKey(tierKey)) return true;
        lock (DynamicGate) return DynamicLabels.ContainsKey(tierKey);
    }

    /// <summary>
    /// True when <paramref name="tierKey"/> is a per-model weekly meter: the
    /// statically-registered sonnet/opus/fable rows, or any tier synthesized
    /// via <see cref="RegisterScopedTier"/> (future per-model rollouts land
    /// there — see the DynamicLabels registry below). These are sub-limits
    /// scoped to a single model rather than the account's aggregate pools, so
    /// an account that never touches that model can sit pinned near 100%
    /// indefinitely — not a critical signal while five_hour, seven_day,
    /// extra_usage, and routines still have headroom. WidgetViewModel's
    /// alert-delivery path uses this to make model-scoped crossings
    /// visual-only (toast, no chime) by default, restorable per-user via the
    /// Settings "Chime for model-scoped weekly tiers" checkbox.
    /// seven_day_cowork/omelette/oauth_apps and iguana_necktie are excluded
    /// deliberately: those are speculative NULL rows (see
    /// <see cref="SpeculativeTiers"/>), not per-model meters, so a future
    /// surprise there should still chime.
    /// </summary>
    public static bool IsModelScoped(string tierKey)
    {
        if (tierKey == SevenDaySonnet || tierKey == SevenDayOpus || tierKey == SevenDayFable)
            return true;
        lock (DynamicGate) return DynamicLabels.ContainsKey(tierKey);
    }

    /// <summary>Display label for a known tier. Throws for unknown keys (mirrors the Python dict lookup).</summary>
    public static string Label(string tierKey)
    {
        if (LabelsByKey.TryGetValue(tierKey, out var label)) return label;
        lock (DynamicGate)
        {
            if (DynamicLabels.TryGetValue(tierKey, out var dyn)) return dyn;
        }
        throw new KeyNotFoundException($"Unknown tier key: {tierKey}");
    }

    /// <summary>True when the tier is a speculative "future use" codename / gated row.</summary>
    public static bool IsSpeculative(string tierKey) => SpeculativeTiers.Contains(tierKey);

    /// <summary>
    /// Label with the speculative-tier suffix applied — e.g.
    /// "Weekly - Cowork  ·  future use". Non-speculative tiers return the
    /// plain label. Matches <c>settings_dialog.py</c>'s
    /// <c>f"{label}  ·  future use"</c> (two spaces, middle dot, two spaces).
    /// </summary>
    public static string LabelWithTag(string tierKey) =>
        IsSpeculative(tierKey) ? $"{Label(tierKey)}  ·  {FutureUseTag}" : Label(tierKey);

    // -- dynamic scoped tiers (synthesized from the API's limits[] array) -----

    // Append-only for the process lifetime: a tier seen once stays orderable
    // even when a later fetch omits it (its utilization goes null, the card
    // drops, the order slot remains). Fetch runs on a background thread while
    // render reads on the UI thread — hence the gate.
    private static readonly Dictionary<string, string> DynamicLabels = new(StringComparer.Ordinal);
    private static readonly List<string> DynamicOrder = new();
    private static readonly object DynamicGate = new();

    /// <summary>Register a scoped tier synthesized from a limits[] entry.
    /// Idempotent; statically registered keys are a no-op (the static label wins).</summary>
    public static void RegisterScopedTier(string tierKey, string displayName)
    {
        lock (DynamicGate)
        {
            if (LabelsByKey.ContainsKey(tierKey) || DynamicLabels.ContainsKey(tierKey))
                return;
            DynamicLabels[tierKey] = $"Weekly - {displayName}";
            DynamicOrder.Add(tierKey);
        }
    }

    /// <summary>Canonical order with dynamic scoped tiers folded in after the
    /// seven_day family (post-SevenDayOauthApps, pre-IguanaNecktie), in
    /// registration order. Consumers that used to iterate CanonicalOrder
    /// iterate this so dynamic tiers render/persist/chart without edits.</summary>
    public static IReadOnlyList<string> EffectiveOrder
    {
        get
        {
            lock (DynamicGate)
            {
                if (DynamicOrder.Count == 0)
                    return CanonicalOrder;
                var order = new List<string>(CanonicalOrder.Count + DynamicOrder.Count);
                foreach (var key in CanonicalOrder)
                {
                    order.Add(key);
                    if (key == SevenDayOauthApps)
                        order.AddRange(DynamicOrder);
                }
                return order;
            }
        }
    }

    internal static void ResetDynamicTiersForTests()
    {
        lock (DynamicGate)
        {
            DynamicLabels.Clear();
            DynamicOrder.Clear();
        }
    }

    // -- Routines synthesis ---------------------------------------------------

    /// <summary>
    /// Synthesize a Routines budget into a tier, or <c>null</c> when the
    /// account has no Routines quota. Mirrors the fetcher guard
    /// <c>if budget and budget.get("limit", 0) &gt; 0</c>: a non-positive
    /// limit (404 / no code access) yields no tier.
    /// </summary>
    public static RoutineBudget? SynthesizeRoutines(int used, int limit) =>
        limit > 0 ? new RoutineBudget(used, limit) : null;

    // -- per-tier derivations the card consumes -------------------------------

    /// <summary>
    /// The value-label the card shows in its header. Routines is count-based
    /// ("used/limit") when both counts are supplied; every other tier shows
    /// the integer percentage ("NN%"). Mirrors widget.py's value_label
    /// override path.
    /// </summary>
    public static string ValueLabel(string tierKey, int utilization, int? used = null, int? limit = null)
    {
        if (tierKey == Routines && used is not null && limit is not null)
            return $"{used}/{limit}";
        return $"{utilization}%";
    }

    /// <summary>
    /// The reset-countdown line: empty when there's no reset time (e.g. the
    /// daily Routines quota), else "Resets in {countdown}" using the shared
    /// pacing math. Mirrors widget.py:
    /// <c>"" if not resets_at else f"Resets in {pacing.time_until(resets_at)}"</c>.
    /// </summary>
    public static string ResetCountdown(string? resetsAt, DateTimeOffset? now = null) =>
        string.IsNullOrEmpty(resetsAt) ? "" : $"Resets in {Pacing.TimeUntil(resetsAt, now)}";

    // -- hide / reorder model (state, not UI) ---------------------------------

    /// <summary>
    /// Resolve the user's saved tier order against the canonical registry:
    /// valid saved keys first (de-duplicated, unknown keys dropped), then any
    /// remaining canonical tiers in default order — so a release that adds a
    /// tier surfaces it without manual config. Mirrors the order-resolution
    /// loop shared by widget._render_cards and settings_dialog.
    /// </summary>
    public static IReadOnlyList<string> ResolveOrder(IEnumerable<string>? savedOrder)
    {
        var seen = new HashSet<string>();
        var ordered = new List<string>();
        if (savedOrder is not null)
        {
            foreach (var key in savedOrder)
            {
                if (IsKnown(key) && seen.Add(key))
                    ordered.Add(key);
            }
        }
        foreach (var key in EffectiveOrder)
        {
            if (seen.Add(key))
                ordered.Add(key);
        }
        return ordered;
    }

    /// <summary>
    /// The tiers a card layout should render, in resolved order: drop hidden
    /// tiers, then keep only tiers that have data (a non-null utilization in
    /// <paramref name="utilizationByKey"/>). Mirrors the <c>active</c> list in
    /// widget._render_cards — a key absent from the map, or mapped to null,
    /// has no card. Keys present in the map but unknown to the registry never
    /// appear (ResolveOrder dropped them).
    /// </summary>
    public static IReadOnlyList<string> ActiveTiers(
        IReadOnlyDictionary<string, double?> utilizationByKey,
        IEnumerable<string>? savedOrder = null,
        ISet<string>? hiddenTiers = null)
    {
        var active = new List<string>();
        foreach (var key in ResolveOrder(savedOrder))
        {
            if (hiddenTiers is not null && hiddenTiers.Contains(key))
                continue;
            if (utilizationByKey.TryGetValue(key, out var util) && util is not null)
                active.Add(key);
        }
        return active;
    }
}
