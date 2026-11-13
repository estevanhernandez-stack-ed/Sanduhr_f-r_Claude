namespace Sanduhr.Core;

/// <summary>
/// The widget footer's agent pieces (spec 3): each agent's Vault total for today after the latest ingest minus the total
/// after the ingest before it. The first observation has no "before" and shows nothing; at a day change the earlier total
/// counts as zero; an agent with nothing new is left out, as the footer's "CC +" is.
/// </summary>
public sealed class AgentBurnDeltas
{
    private readonly Dictionary<string, (DateOnly Day, long Total)> _last = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _delta = new(StringComparer.Ordinal);

    public void Observe(string agent, DateOnly day, long todayTotal)
    {
        _delta[agent] = _last.TryGetValue(agent, out var prev)
            ? Math.Max(0, todayTotal - (prev.Day == day ? prev.Total : 0))
            : 0;
        _last[agent] = (day, todayTotal);
    }

    public void Forget(string agent)
    {
        _last.Remove(agent);
        _delta.Remove(agent);
    }

    public string FooterPieces() => string.Concat(AgentSources.Burn
        .Where(a => _delta.GetValueOrDefault(a) > 0)
        .Select(a => $"  ·  {AgentSources.ShortName(a)} +{TokenFormat.Compact(_delta[a])}"));
}
