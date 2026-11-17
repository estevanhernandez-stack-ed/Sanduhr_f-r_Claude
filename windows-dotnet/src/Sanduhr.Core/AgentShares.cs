namespace Sanduhr.Core;

/// <summary>Text for local-burn figures split by agent (spec 3). Claude Code alone reads exactly as before; another agent
/// alone is named, so a Codex-only project never reads as Claude Code burn.</summary>
public static class AgentShares
{
    public static string Describe(long total, IReadOnlyDictionary<string, long> byAgent)
    {
        var parts = Ordered(byAgent);
        var head = TokenFormat.Compact(total);
        if (parts.Count > 1)
            return $"{head} ({string.Join(", ", parts)})";
        var only = AgentSources.Burn.FirstOrDefault(a => byAgent.GetValueOrDefault(a) > 0);
        return only is null || only == AgentSources.Claude ? head : $"{head} ({AgentSources.BurnName(only)})";
    }

    public static string StripName(IReadOnlyDictionary<string, long> byAgent)
        => "30 days, " + string.Join(", ", Ordered(byAgent));

    private static List<string> Ordered(IReadOnlyDictionary<string, long> byAgent) => AgentSources.Burn
        .Where(a => byAgent.GetValueOrDefault(a) > 0)
        .Select(a => $"{AgentSources.BurnName(a)} {TokenFormat.Compact(byAgent[a])}")
        .ToList();
}
