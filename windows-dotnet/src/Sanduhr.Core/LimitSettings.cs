namespace Sanduhr.Core;

/// <summary>One row of Settings, Alerts, Each limit (WF13): a limit the account has, or one a setting still names.</summary>
/// <param name="WarnOn">Not in "alert_muted_tiers".</param>
/// <param name="Shown">In neither "hidden_tiers" nor "limits_hidden_until_reset".</param>
/// <param name="HiddenUntilReset">In "limits_hidden_until_reset": it comes back on its own when it renews or refills.</param>
public sealed record LimitRow(string Key, string Label, bool WarnOn, bool Shown, bool HiddenUntilReset);

/// <summary>
/// Settings v2, slice 5: the per-limit settings in one place, and the widget's limit order. Pure: the widget's last answer and
/// the three settings.json keys come in as values, so the rules are testable without a widget. Not linked into the MCP.
/// </summary>
public static class LimitSettings
{
    /// <summary>
    /// One row per limit in <paramref name="present"/> (the widget's last answer) and per limit any of the three keys still
    /// names, so a muted or hidden limit that is absent today can be restored. Canonical order; a key the tier registry doesn't
    /// know gets no row (a dynamic scoped limit has one once a fetch has registered it).
    /// </summary>
    public static IReadOnlyList<LimitRow> Rows(IEnumerable<string> present, IEnumerable<string> hidden, IEnumerable<string> muted,
        IEnumerable<string> hiddenUntilReset)
    {
        var hiddenSet = new HashSet<string>(hidden, StringComparer.Ordinal);
        var mutedSet = new HashSet<string>(muted, StringComparer.Ordinal);
        var untilSet = new HashSet<string>(hiddenUntilReset, StringComparer.Ordinal);
        var named = new HashSet<string>(present, StringComparer.Ordinal);
        named.UnionWith(hiddenSet);
        named.UnionWith(mutedSet);
        named.UnionWith(untilSet);

        var rows = new List<LimitRow>();
        foreach (var key in TierModel.EffectiveOrder)
        {
            if (!named.Contains(key))
                continue;
            bool until = untilSet.Contains(key);
            rows.Add(new LimitRow(key, TierModel.Label(key), !mutedSet.Contains(key), !hiddenSet.Contains(key) && !until, until));
        }
        return rows;
    }

    /// <summary>
    /// <paramref name="shown"/> with <paramref name="key"/> moved one place (<paramref name="delta"/> -1 up, +1 down); null when the
    /// key isn't shown, the step is not one place, or the move would leave the list.
    /// </summary>
    public static IReadOnlyList<string>? Move(IReadOnlyList<string> shown, string key, int delta)
    {
        if (delta is not (-1 or 1))
            return null;
        int at = -1;
        for (int i = 0; i < shown.Count; i++)
        {
            if (shown[i] == key) { at = i; break; }
        }
        int to = at + delta;
        if (at < 0 || to < 0 || to >= shown.Count)
            return null;
        var moved = shown.ToList();
        (moved[at], moved[to]) = (moved[to], moved[at]);
        return moved;
    }

    /// <summary>What "tier_order" becomes after a move: the shown order, then every other known limit once in its resolved order,
    /// so a hidden limit keeps its place for when it shows again.</summary>
    public static IReadOnlyList<string> SavedOrder(IReadOnlyList<string> shown, IEnumerable<string>? saved)
    {
        var order = shown.Where(TierModel.IsKnown).Distinct(StringComparer.Ordinal).ToList();
        foreach (var key in TierModel.ResolveOrder(saved))
        {
            if (!order.Contains(key))
                order.Add(key);
        }
        return order;
    }
}
