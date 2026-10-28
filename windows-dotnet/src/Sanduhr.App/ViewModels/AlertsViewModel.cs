using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>
/// Drives the Settings ▸ Alerts tab (WS-B). Edits persist immediately through
/// the WidgetViewModel passthroughs (the tab is non-modal, matching the other
/// tabs' apply-on-change behavior). Threshold edits are validated on change:
/// values clamp to 1-99 and Warn must stay below Urgent — invalid combinations
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
    [ObservableProperty] private bool _scopedChimeEnabled;
    [ObservableProperty] private bool _snakeAtFull;
    [ObservableProperty] private string _validationHint = "";

    /// <summary>The Desk's sign-in reminder switch (spec 7.5): saved at once, and the Desk redraws at once.</summary>
    [ObservableProperty] private bool _deskSignInReminder = true;

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
        ScopedChimeEnabled = widget.LoadAlertSoundScoped();
        SnakeAtFull = widget.LoadAlertSnakeFull();
        DeskSignInReminder = widget.LoadDeskSignInReminder();
        _loading = false;
        _widget.LimitsChanged += ReloadLimits;
        ReloadLimits();
    }

    /// <summary>Alerts, Each limit (WF13): one row per limit the account has or a setting still names.</summary>
    public ObservableCollection<LimitRowViewModel> Limits { get; } = new();

    /// <summary>What Each limit says while it has no row: signed out, or signed in with nothing fetched yet.</summary>
    [ObservableProperty] private string _limitsEmptyText = "";

    /// <summary>True while Each limit has no row; drives the empty line's live region.</summary>
    [ObservableProperty] private bool _hasNoLimits = true;

    /// <summary>Syncs the rows in place: a row keeps its object (and its box keeps focus) while its state changes; new limits
    /// slot in at their place; gone ones leave.</summary>
    private void ReloadLimits()
    {
        var rows = _widget.LimitRows();
        for (int i = 0; i < rows.Count; i++)
        {
            int at = -1;
            for (int j = i; j < Limits.Count; j++)
            {
                if (Limits[j].Key == rows[i].Key) { at = j; break; }
            }
            if (at < 0)
            {
                Limits.Insert(i, new LimitRowViewModel(_widget, rows[i]));
                continue;
            }
            if (at != i)
                Limits.Move(at, i);
            Limits[i].Sync(rows[i]);
        }
        while (Limits.Count > rows.Count)
            Limits.RemoveAt(Limits.Count - 1);
        LimitsEmptyText = rows.Count > 0 ? "" : _widget.LimitsEmptyLine();
        HasNoLimits = rows.Count == 0;
    }

    /// <summary>Stop following the widget (Settings closed): it outlives every Settings window.</summary>
    public void Detach() => _widget.LimitsChanged -= ReloadLimits;

    partial void OnAlertsEnabledChanged(bool value) => PersistConfig();
    partial void OnWarnPctChanged(int value) => PersistConfig();
    partial void OnUrgentPctChanged(int value) => PersistConfig();
    partial void OnProjectionEnabledChanged(bool value) => PersistConfig();
    partial void OnResetEnabledChanged(bool value) => PersistConfig();

    partial void OnDeskSignInReminderChanged(bool value)
    {
        if (!_loading) _widget.SaveDeskSignInReminder(value);
    }

    partial void OnSoundEnabledChanged(bool value)
    {
        if (!_loading) _widget.SaveAlertSound(value);
    }

    partial void OnScopedChimeEnabledChanged(bool value)
    {
        if (!_loading) _widget.SaveAlertSoundScoped(value);
    }

    partial void OnSnakeAtFullChanged(bool value)
    {
        if (_loading)
            return;
        _widget.SaveAlertSnakeFull(value);
        // Audition on opt-in: the sting only ever plays at a real 100% otherwise,
        // and nobody should meet it for the first time at the worst moment.
        if (value)
            Services.Sounds.PlayAlertSnake();
    }

    private void PersistConfig()
    {
        if (_loading)
            return;
        int warn = Math.Clamp(WarnPct, 1, 99);
        int urgent = Math.Clamp(UrgentPct, 1, 99);
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
