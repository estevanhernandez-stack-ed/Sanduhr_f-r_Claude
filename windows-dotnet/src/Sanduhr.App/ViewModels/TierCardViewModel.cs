using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Sanduhr.App.Theming;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>
/// One rendered usage tier — the binding surface for <c>TierCard.xaml</c>,
/// ported from the Python <c>TierCard.update_state</c>. Pulls every derived
/// value from <see cref="Pacing"/> / <see cref="TierModel"/> (the shared Core
/// math) so the card stays a thin projection of Core. <see cref="Update"/> is
/// the full refresh on new data; <see cref="Tick"/> re-derives only the
/// time-based labels against cached state (mirrors widget._tick — no refetch).
/// </summary>
public sealed partial class TierCardViewModel : ObservableObject
{
    public string TierKey { get; }

    // Cached state for the 30s tick re-derivation.
    private double _util;
    private string? _resetsAt;
    private ThemePalette _palette;

    [ObservableProperty] private string _label = "";
    [ObservableProperty] private string _valueText = "";
    [ObservableProperty] private double _utilization;
    [ObservableProperty] private Color _barColor = Colors.LimeGreen;
    [ObservableProperty] private double _paceFraction = double.NaN;
    [ObservableProperty] private Color _paceTickColor = Color.FromRgb(0xff, 0x6b, 0x6b);
    [ObservableProperty] private string _resetCountdown = "";
    [ObservableProperty] private string _resetDateTime = "";
    [ObservableProperty] private string _paceLabel = "";
    [ObservableProperty] private Brush _paceBrush = Brushes.Gray;
    [ObservableProperty] private string _burnText = "";
    [ObservableProperty] private Brush _burnBrush = Brushes.Gray;
    [ObservableProperty] private bool _hasBurn;
    [ObservableProperty] private IReadOnlyList<int> _sparkline = Array.Empty<int>();
    [ObservableProperty] private Color _sparklineColor = Colors.White;

    public TierCardViewModel(string tierKey, ThemePalette palette)
    {
        TierKey = tierKey;
        _palette = palette;
        _label = TierModel.Label(tierKey);
    }

    /// <summary>Full refresh on a fresh fetch.</summary>
    public void Update(
        int util, string? resetsAt, int? used, int? limit,
        IReadOnlyList<int> history, ThemePalette palette, DateTimeOffset now)
    {
        _util = util;
        _resetsAt = resetsAt;
        _palette = palette;

        Utilization = util;
        ValueText = TierModel.ValueLabel(TierKey, util, used, limit);
        BarColor = ThemePalette.UsageColor(util);
        PaceTickColor = palette.PaceMarker;
        Sparkline = history;
        SparklineColor = palette.Sparkline;

        RefreshTimeDerived(now);
    }

    /// <summary>Re-derive only the time-based labels (no refetch).</summary>
    public void Tick(DateTimeOffset now) => RefreshTimeDerived(now);

    private void RefreshTimeDerived(DateTimeOffset now)
    {
        PaceFraction = Pacing.PaceFrac(_resetsAt, TierKey, now) ?? double.NaN;
        ResetCountdown = TierModel.ResetCountdown(_resetsAt, now);
        ResetDateTime = ResetDatetimeStr(_resetsAt, now);

        var pace = Pacing.PaceInfo(_util, _resetsAt, TierKey, now);
        PaceLabel = pace?.Label ?? "";
        PaceBrush = pace is { } p ? FromHex(p.Color) : new SolidColorBrush(_palette.TextDim);

        var burn = Pacing.BurnProjection(_util, _resetsAt, TierKey, now);
        BurnText = burn?.Message ?? "";
        HasBurn = burn is not null;
        BurnBrush = burn is { } b ? FromHex(b.Color) : new SolidColorBrush(_palette.TextMuted);
    }

    /// <summary>
    /// Friendly local reset datetime, 1:1 with <c>pacing.reset_datetime_str</c>:
    /// "Today 1:00 AM" / "Tomorrow 1:00 AM" / "Sun 1:00 AM" / "Wed Apr 22 1:00 AM".
    /// </summary>
    private static string ResetDatetimeStr(string? iso, DateTimeOffset now)
    {
        var rd = Pacing.Parse(iso);
        if (rd is null) return "";
        var loc = rd.Value.ToLocalTime();
        var localNow = now.ToLocalTime();
        int days = (loc.Date - localNow.Date).Days;
        string t = loc.ToString("h:mm tt", CultureInfo.InvariantCulture);
        if (days <= 0) return $"Today {t}";
        if (days == 1) return $"Tomorrow {t}";
        if (days < 7) return $"{loc.ToString("ddd", CultureInfo.InvariantCulture)} {t}";
        return $"{loc.ToString("ddd MMM dd", CultureInfo.InvariantCulture)} {t}";
    }

    private static SolidColorBrush FromHex(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        b.Freeze();
        return b;
    }
}
