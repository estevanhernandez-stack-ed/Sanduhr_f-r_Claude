namespace Sanduhr.Core;

/// <summary>Text for local-burn figures split by agent (spec 3). With one agent the text is exactly today's.</summary>
public static class AgentShares
{
    public static string Describe(long total, IReadOnlyDictionary<string, long> byAgent)
    {
        var parts = Ordered(byAgent);
        var head = TokenFormat.Compact(total);
        return parts.Count <= 1 ? head : $"{head} ({string.Join(", ", parts)})";
    }

    public static string StripName(IReadOnlyDictionary<string, long> byAgent)
        => "30 days, " + string.Join(", ", Ordered(byAgent));

    private static List<string> Ordered(IReadOnlyDictionary<string, long> byAgent) => AgentSources.Burn
        .Where(a => byAgent.GetValueOrDefault(a) > 0)
        .Select(a => $"{AgentSources.BurnName(a)} {TokenFormat.Compact(byAgent[a])}")
        .ToList();
}
