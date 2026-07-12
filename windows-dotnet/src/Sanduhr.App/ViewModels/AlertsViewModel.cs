using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>
/// Drives the Settings ▸ Alerts tab (WS-B). Edits persist immediately through
/// the WidgetViewModel passthroughs (the tab is non-modal, matching the other
/// tabs' apply-on-change behavior). Threshold edits are validated on change:
/// values clamp to 50-99 and Warn must stay below Urgent — invalid combinations
/// revert and surface a themed inline hint instead of persisting.
/// </summary>
public sealed partial class AlertsViewModel : ObservableObject
{
    private readonly WidgetViewModel _widget;
    private readonly Func<Task> _sendTestAlertAsync;
    private bool _loading;

    [ObservableProperty] private bool _alertsEnabled;
    [ObservableProperty] private int _warnPct;
    [ObservableProperty] private int _urgentPct;
    [ObservableProperty] private bool _projectionEnabled;
    [ObservableProperty] private bool _resetEnabled;
    [ObservableProperty] private bool _soundEnabled;
    [ObservableProperty] private bool _snakeAtFull;
    [ObservableProperty] private string _validationHint = "";

    public AlertsViewModel(WidgetViewModel widget, Func<Task> sendTestAlertAsync)
    {
        _widget = widget;
        _sendTestAlertAsync = sendTestAlertAsync;
        _loading = true;
        var cfg = widget.LoadAlertConfig();
        AlertsEnabled = cfg.Enabled;
        WarnPct = cfg.WarnPct;
        UrgentPct = cfg.UrgentPct;
        ProjectionEnabled = cfg.ProjectionEnabled;
        ResetEnabled = cfg.ResetEnabled;
        SoundEnabled = widget.LoadAlertSound();
        SnakeAtFull = widget.LoadAlertSnakeFull();
        _loading = false;
    }

    partial void OnAlertsEnabledChanged(bool value) => PersistConfig();
    partial void OnWarnPctChanged(int value) => PersistConfig();
    partial void OnUrgentPctChanged(int value) => PersistConfig();
    partial void OnProjectionEnabledChanged(bool value) => PersistConfig();
    partial void OnResetEnabledChanged(bool value) => PersistConfig();

    partial void OnSoundEnabledChanged(bool value)
    {
        if (!_loading) _widget.SaveAlertSound(value);
    }

    partial void OnSnakeAtFullChanged(bool value)
    {
        if (!_loading) _widget.SaveAlertSnakeFull(value);
    }

    private void PersistConfig()
    {
        if (_loading)
            return;
        int warn = Math.Clamp(WarnPct, 50, 99);
        int urgent = Math.Clamp(UrgentPct, 50, 99);
        // Echo clamped values back so the bound UI never diverges from the store
        // (_loading doubles as the re-entrancy latch for these programmatic sets).
        if (WarnPct != warn || UrgentPct != urgent)
        {
            _loading = true;
            WarnPct = warn;
            UrgentPct = urgent;
            _loading = false;
        }
        if (warn >= urgent)
        {
            ValidationHint = "Warn must be below Urgent — not saved.";
            return;
        }
        ValidationHint = "";
        _widget.SaveAlertConfig(new AlertConfig(
            AlertsEnabled, warn, urgent, ProjectionEnabled, ResetEnabled));
    }

    /// <summary>"Send test alert" — a fake Warn event through the real delivery
    /// pipeline (toast + chime), the support answer to "is it working?".</summary>
    [RelayCommand]
    private async Task TestAlert() => await _sendTestAlertAsync();
}
