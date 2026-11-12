using System.Globalization;

namespace Sanduhr.Core;

public sealed record SourcePercent(string Source, double Percent, bool Stale);
public sealed record BarSegment(string Source, double From, double To, bool Stale);
public sealed record BarTick(string Source, double At, bool Stale);
public sealed record SourceBarLayout(BarSegment? Primary, BarSegment? Beyond, IReadOnlyList<BarTick> Ticks);

/// <summary>
/// The two-tone combined bar (spec section 3): the primary fills to its percent; the furthest source past it
/// continues the bar in its own color; every other source is a tick.
/// </summary>
public static class SourceBar
{
    public static SourceBarLayout Layout(string primary, IReadOnlyList<SourcePercent> sources)
    {
        if (sources.Count == 0) return new(null, null, []);
        var ordered = sources
            .Select(s => s with { Percent = Math.Clamp(s.Percent, 0, 100) })
            .OrderBy(s => AgentSources.Order(s.Source))
            .ToList();
        var main = ordered.FirstOrDefault(s => s.Source == primary)
            ?? ordered.OrderByDescending(s => s.Percent).ThenBy(s => AgentSources.Order(s.Source)).First();
        double p = main.Percent / 100;
        var others = ordered.Where(s => s.Source != main.Source).ToList();
        var beyond = others.Where(s => s.Percent / 100 > p)
            .OrderByDescending(s => s.Percent).ThenBy(s => AgentSources.Order(s.Source)).FirstOrDefault();
        return new(
            new BarSegment(main.Source, 0, p, main.Stale),
            beyond is null ? null : new BarSegment(beyond.Source, p, beyond.Percent / 100, beyond.Stale),
            others.Select(s => new BarTick(s.Source, s.Percent / 100, s.Stale)).ToList());
    }

    public static string AccessibleName(string windowLabel, IReadOnlyList<SourcePercent> sources)
    {
        var parts = sources.OrderBy(s => AgentSources.Order(s.Source)).Select(s =>
            string.Create(CultureInfo.InvariantCulture, $"{AgentSources.Name(s.Source)} {(int)Math.Floor(Math.Clamp(s.Percent, 0, 100) + 1e-9)}%{(s.Stale ? ", as of earlier" : "")}."));
        return windowLabel + ". " + string.Join(" ", parts);
    }
}
