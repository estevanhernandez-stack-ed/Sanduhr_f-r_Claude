using CommunityToolkit.Mvvm.ComponentModel;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>
/// One row of Settings, Alerts, Each limit (WF13). Its two switches write through the widget, which saves the keys and raises
/// LimitsChanged; the page then syncs every row in place (<see cref="Sync"/>), so a switch never loses keyboard focus to a rebuilt list.
/// </summary>
public sealed partial class LimitRowViewModel : ObservableObject
{
    private readonly WidgetViewModel _widget;
    private bool _syncing;

    public string Key { get; }
    public string Label { get; }

    /// <summary>"Warn for this limit" (not in "alert_muted_tiers").</summary>
    [ObservableProperty] private bool _warnOn;

    /// <summary>"Show this limit" (in neither "hidden_tiers" nor "limits_hidden_until_reset").</summary>
    [ObservableProperty] private bool _shown;

    /// <summary>Hidden until its window renews or refills; the caption under the row says so.</summary>
    [ObservableProperty] private bool _hiddenUntilReset;

    public LimitRowViewModel(WidgetViewModel widget, LimitRow row)
    {
        _widget = widget;
        Key = row.Key;
        Label = row.Label;
        Sync(row);
    }

    public string WarnName => SettingsNames.WarnForLimit(Label);
    public string ShowName => SettingsNames.ShowLimit(Label);

    /// <summary>Takes the row's state without writing anything back.</summary>
    internal void Sync(LimitRow row)
    {
        _syncing = true;
        WarnOn = row.WarnOn;
        Shown = row.Shown;
        HiddenUntilReset = row.HiddenUntilReset;
        _syncing = false;
    }

    partial void OnWarnOnChanged(bool value)
    {
        if (!_syncing) _widget.SetWarnings(Key, value);
    }

    partial void OnShownChanged(bool value)
    {
        if (!_syncing) _widget.SetShown(Key, value);
    }

    /// <summary>A screen reader reads the row by its limit, never the type behind it.</summary>
    public override string ToString() => Label;
}
