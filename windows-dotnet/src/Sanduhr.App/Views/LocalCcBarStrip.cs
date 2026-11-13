using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Sanduhr.App.Theming;
using Sanduhr.Core;

namespace Sanduhr.App.Views;

/// <summary>
/// The 30-day daily token bar strip for the Local CC tab, ported from
/// <c>local_cc_dialog._DailyBarStrip.paintEvent</c>. One bar per day for the
/// trailing 30 days, height proportional to that day's token total vs the window
/// max; zero-usage days render as a 1px baseline tick so the strip still reads as
/// "30 days" instead of a few sparse events. Empty-state message when the whole
/// window is zero. Custom <see cref="OnRender"/> control; the host pushes data +
/// palette via <see cref="SetData"/>.
/// </summary>
public sealed class LocalCcBarStrip : FrameworkElement
{
    private const int Lookback = 30;

    private IReadOnlyDictionary<DateOnly, long> _byDay = new Dictionary<DateOnly, long>();
    private ThemePalette _palette = ThemePalette.Obsidian;

    public LocalCcBarStrip()
    {
        MinHeight = 60;
    }

    private IReadOnlyDictionary<string, IReadOnlyDictionary<DateOnly, long>> _byAgent =
        new Dictionary<string, IReadOnlyDictionary<DateOnly, long>>();
    private Func<string, Color>? _colorOf;

    /// <summary>True while some non-Claude agent has tokens in the window: only then does the strip stack by agent and word its
    /// empty state for every agent. A consented agent root with no rows leaves the strip as it always was.</summary>
    internal bool Stacked { get; private set; }

    public void SetData(IReadOnlyDictionary<DateOnly, long> byDay, ThemePalette palette)
        => SetData(byDay, palette, new Dictionary<string, IReadOnlyDictionary<DateOnly, long>>(), null);

    public void SetData(IReadOnlyDictionary<DateOnly, long> byDay, ThemePalette palette,
        IReadOnlyDictionary<string, IReadOnlyDictionary<DateOnly, long>> byAgent, Func<string, Color>? colorOf)
    {
        _byDay = byDay ?? new Dictionary<DateOnly, long>();
        _palette = palette;
        _byAgent = byAgent ?? new Dictionary<string, IReadOnlyDictionary<DateOnly, long>>();
        _colorOf = colorOf;
        Stacked = _byAgent.Values.Any(days => days.Values.Sum() > 0);
        var totals = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var (agent, days) in _byAgent) totals[agent] = days.Values.Sum();
        totals[AgentSources.Claude] = Math.Max(0, _byDay.Values.Sum() - totals.Values.Sum());
        System.Windows.Automation.AutomationProperties.SetName(this,   // streamer: notname - agent product names and token figures only, no project or folder
            totals.Count(kv => kv.Value > 0) > 1 ? AgentShares.StripName(totals) : "");
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0)
            return;

        // Contiguous date list so zero days still render a baseline marker.
        var today = DateOnly.FromDateTime(DateTime.Now);
        var days = new List<DateOnly>(Lookback);
        for (int i = Lookback - 1; i >= 0; i--)
            days.Add(today.AddDays(-i));

        long maxV = 0;
        foreach (var v in _byDay.Values)
            if (v > maxV) maxV = v;

        if (maxV == 0)
        {
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var brush = Frozen(_palette.TextDim);
            var ft = new FormattedText(
                Stacked ? "No local activity in the last 30 days." : "No local CC activity in the last 30 days.",
                CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 11, brush, dpi)
            {
                TextAlignment = TextAlignment.Center,
                MaxTextWidth = w,
            };
            dc.DrawText(ft, new Point(0, (h - ft.Height) / 2));
            return;
        }

        const int gap = 1;
        int barArea = Math.Max(1, (int)w - 4);
        int barW = Math.Max(2, (barArea - gap * (Lookback - 1)) / Lookback);
        double x = 2;
        double baselineY = h - 4;

        var barColor = Frozen(_palette.Accent);
        var baselineColor = Frozen(WithAlpha(_palette.TextDim, 80));

        foreach (var d in days)
        {
            long v = _byDay.GetValueOrDefault(d);
            if (v == 0)
            {
                dc.DrawRectangle(baselineColor, null, new Rect(x, baselineY, barW, 1));
            }
            else if (!Stacked || _colorOf is null)
            {
                double barH = Math.Max(2, v / (double)maxV * (h - 8));
                dc.DrawRectangle(barColor, null, new Rect(x, baselineY - barH, barW, barH));
            }
            else
            {
                double scale = (h - 8) / maxV;
                double y = baselineY;
                long agentsSum = 0;
                foreach (var agent in AgentSources.Burn.Where(a => a != AgentSources.Claude))
                    agentsSum += _byAgent.TryGetValue(agent, out var agentDays) ? agentDays.GetValueOrDefault(d) : 0;
                foreach (var agent in AgentSources.Burn)
                {
                    long part = agent == AgentSources.Claude
                        ? Math.Max(0, v - agentsSum)
                        : (_byAgent.TryGetValue(agent, out var agentDays) ? agentDays.GetValueOrDefault(d) : 0);
                    if (part <= 0) continue;
                    double segH = Math.Max(1, part * scale);
                    y -= segH;
                    dc.DrawRectangle(Frozen(_colorOf(agent)), null, new Rect(x, y, barW, segH));
                }
            }
            x += barW + gap;
        }
    }

    private static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
