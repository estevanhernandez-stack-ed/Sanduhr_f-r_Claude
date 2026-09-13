using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.App.Services;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>One "share with 626 Labs" checkbox per detected Claude Code home.
/// Default OFF for every root; the user opts each in (VaultRootToggleViewModel's shape).</summary>
public sealed partial class PublishRootToggleViewModel : ObservableObject
{
    private readonly PublishViewModel _owner;
    private bool _loading = true;

    public string Name { get; }

    [ObservableProperty] private bool _isShared;

    public PublishRootToggleViewModel(PublishViewModel owner, string name, bool shared)
    {
        _owner = owner;
        Name = name;
        IsShared = shared;
        _loading = false;
    }

    partial void OnIsSharedChanged(bool value)
    {
        if (!_loading)
            _owner.OnRootToggled(Name, value);
    }
}

/// <summary>
/// Backs the Settings → 626 Labs tab: master toggle (default off), per-home
/// share checkboxes (default off), publish time (default 06:45 local), agent
/// key set/clear (Credential Manager, masked entry), "Publish now", and the
/// last-publish status line. Null service (unit contexts) renders inert.
/// </summary>
public sealed partial class PublishViewModel : ObservableObject
{
    private readonly UsagePublishService? _service;
    private readonly Action _statusHandler;
    private Window? _owner;
    private bool _loading;

    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string _publishTimeText = PublishScheduler.FormatPublishTime(PublishScheduler.DefaultPublishTime);
    [ObservableProperty] private string _timeError = "";
    [ObservableProperty] private bool _hasKey;
    [ObservableProperty] private string _keyStatusText = "No agent key stored.";
    [ObservableProperty] private string _statusText = "Never published.";
    [ObservableProperty] private bool _isPublishing;

    public ObservableCollection<PublishRootToggleViewModel> Roots { get; } = new();

    public bool IsAvailable => _service is not null;

    public PublishViewModel(UsagePublishService? service)
    {
        _service = service;
        _statusHandler = () => Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            try { RefreshStatus(); }
            catch { /* every UI path caught */ }
        });
        Reload();
    }

    public void AttachOwner(Window owner) => _owner = owner;

    /// <summary>Subscribe to worker-thread outcomes (scheduled / MCP-queued
    /// publishes finishing while the tab is open). The window calls Detach on
    /// close — the service outlives every Settings window.</summary>
    public void Attach()
    {
        if (_service is not null)
            _service.StatusChanged += _statusHandler;
    }

    public void Detach()
    {
        if (_service is not null)
            _service.StatusChanged -= _statusHandler;
    }

    private void Reload()
    {
        if (_service is null)
            return;
        _loading = true;
        try
        {
            var s = _service.Load();
            Enabled = s.Enabled;
            PublishTimeText = PublishScheduler.FormatPublishTime(s.PublishTime);
            Roots.Clear();
            foreach (var root in _service.DetectedRootNames())
                Roots.Add(new PublishRootToggleViewModel(this, root, s.Roots.GetValueOrDefault(root)));
            RefreshKey();
            RefreshStatus();
        }
        finally
        {
            _loading = false;
        }
    }

    partial void OnEnabledChanged(bool value)
    {
        if (_loading || _service is null)
            return;
        try
        {
            _service.SetEnabled(value);
            RefreshStatus();
        }
        catch { /* every UI path caught */ }
    }

    internal void OnRootToggled(string root, bool on)
    {
        if (_service is null)
            return;
        try
        {
            _service.SetRootShared(root, on);
            RefreshStatus();
        }
        catch { /* every UI path caught */ }
    }

    partial void OnPublishTimeTextChanged(string value)
    {
        if (_loading || _service is null)
            return;
        try
        {
            if (PublishScheduler.ParsePublishTime(value) is { } t)
            {
                _service.SetPublishTime(t);
                TimeError = "";
                var normalized = PublishScheduler.FormatPublishTime(t);
                if (normalized != value)
                {
                    _loading = true;
                    try { PublishTimeText = normalized; }
                    finally { _loading = false; }
                }
                RefreshStatus();
            }
            else
            {
                TimeError = "Use 24-hour HH:mm, e.g. 06:45.";
            }
        }
        catch { /* every UI path caught */ }
    }

    [RelayCommand]
    private void SetKey()
    {
        if (_service is null)
            return;
        try
        {
            var w = new PublishTokenWindow { Owner = _owner };
            if (w.ShowDialog() == true && !string.IsNullOrWhiteSpace(w.Token))
            {
                _service.SaveToken(w.Token);
                RefreshKey();
                RefreshStatus();
            }
        }
        catch (Exception ex)
        {
            KeyStatusText = $"Couldn't store the key ({ex.GetType().Name}) - see sanduhr.log.";
        }
    }

    [RelayCommand]
    private void ClearKey()
    {
        if (_service is null)
            return;
        try
        {
            var res = ThemedDialog.Show(_owner, "Remove the 626 Labs agent key?",
                "Publishing stops until a new key is stored. The dashboard keeps what was already published.",
                MessageBoxButton.YesNo, ThemedDialogKind.Warning,
                primaryLabel: "Remove key", secondaryLabel: "Keep it");
            if (res != MessageBoxResult.Yes)
                return;
            _service.ClearToken();
            RefreshKey();
            RefreshStatus();
        }
        catch { /* every UI path caught */ }
    }

    /// <summary>Manual publish of yesterday — bypasses the schedule and the
    /// retry backoff, honors the toggle and share map like every other path.</summary>
    [RelayCommand]
    private async Task PublishNow()
    {
        if (_service is null || IsPublishing)
            return;
        IsPublishing = true;
        try
        {
            if (!HasKey)
            {
                StatusText = "Set a 626 Labs agent key first.";
                return;
            }
            if (_service.SharedRootNames().Count == 0)
            {
                StatusText = "Tick at least one home to share first.";
                return;
            }
            var date = UsagePublisher.DefaultDate(DateTimeOffset.Now);
            StatusText = $"Publishing {date:yyyy-MM-dd}…";
            var outcome = await _service.PublishAsync(date);
            if (outcome is null)
                StatusText = "A publish is already running.";
            else
                RefreshStatus();
        }
        catch
        {
            StatusText = "Publish failed - see sanduhr.log.";
        }
        finally
        {
            IsPublishing = false;
        }
    }

    private void RefreshKey()
    {
        if (_service is null)
            return;
        HasKey = _service.HasToken;
        KeyStatusText = HasKey
            ? "Agent key stored in Windows Credential Manager."
            : "No agent key stored.";
    }

    private void RefreshStatus()
    {
        if (_service is null)
            return;
        var s = _service.Load();
        string last = s.LastAttemptAt is { } at
            ? $"Last attempt {at.ToLocalTime():yyyy-MM-dd HH:mm}: {s.LastResult ?? "(no result)"}"
            : "Never published.";
        string ok = s.LastOkDate is { } d
            ? $" Last successful day: {d:yyyy-MM-dd}."
            : "";
        string next;
        if (!s.Enabled)
            next = " Daily publishing is off.";
        else
        {
            var decision = PublishScheduler.Decide(DateTimeOffset.Now, s.Enabled, s.PublishTime, s.LastOkDate, s.LastAttemptAt);
            next = decision.Verdict switch
            {
                PublishVerdict.AlreadyPublished => $" Next: {decision.TargetDate.AddDays(1):yyyy-MM-dd} at {PublishScheduler.FormatPublishTime(s.PublishTime)}.",
                PublishVerdict.BeforePublishTime => $" Next: today at {PublishScheduler.FormatPublishTime(s.PublishTime)} for {decision.TargetDate:yyyy-MM-dd}.",
                PublishVerdict.Backoff => " Retrying within 15 minutes.",
                PublishVerdict.Run => $" Due now for {decision.TargetDate:yyyy-MM-dd}.",
                _ => "",
            };
        }
        StatusText = string.Create(CultureInfo.InvariantCulture, $"{last}{ok}{next}");
    }
}
