using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.App.Services;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>One "share" checkbox per detected Claude Code home. Default OFF
/// for every root; the user opts each in (VaultRootToggleViewModel's shape).</summary>
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

/// <summary>A dropdown row: the enum value plus its display label.</summary>
public sealed record PublishPresetOption(PublishPreset Value, string Display);

/// <summary>A dropdown row: the enum value plus its display label.</summary>
public sealed record PublishAuthOption(PublishAuthScheme Value, string Display);

/// <summary>
/// Backs the Settings → Publish usage tab. Nothing about usage comes back to
/// 626 Labs; the user sends it to an endpoint they choose. Preset (custom or
/// the 626 Labs dashboard), endpoint URL, auth scheme (+ header name), the
/// masked publish token (Credential Manager), the master toggle (default
/// off), per-home share checkboxes (default off), publish time (default
/// 06:45 local), "Publish now", and the last-publish status line. Null
/// service (unit contexts) renders inert.
/// </summary>
public sealed partial class PublishViewModel : ObservableObject
{
    public static IReadOnlyList<PublishPresetOption> PresetOptions { get; } = new[]
    {
        new PublishPresetOption(PublishPreset.Custom, PublishPresets.CustomDisplay),
        new PublishPresetOption(PublishPreset.Labs626, PublishPresets.Labs626Display),
    };

    public static IReadOnlyList<PublishAuthOption> AuthOptions { get; } = new[]
    {
        new PublishAuthOption(PublishAuthScheme.Bearer, "Bearer token"),
        new PublishAuthOption(PublishAuthScheme.Header, "Custom header"),
        new PublishAuthOption(PublishAuthScheme.None, "None"),
    };

    private readonly UsagePublishService? _service;
    private readonly Action _statusHandler;
    private Window? _owner;
    private bool _loading;

    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private PublishPresetOption _selectedPreset = PresetOptions[0];
    [ObservableProperty] private string _presetHint = "";
    [ObservableProperty] private string _endpointUrl = "";
    [ObservableProperty] private string _endpointError = "";
    [ObservableProperty] private PublishAuthOption _selectedAuth = AuthOptions[0];
    [ObservableProperty] private bool _showHeaderName;
    [ObservableProperty] private string _authHeaderName = PublishSettingsJson.DefaultAuthHeaderName;
    [ObservableProperty] private bool _needsToken = true;
    [ObservableProperty] private string _publishTimeText = PublishScheduler.FormatPublishTime(PublishScheduler.DefaultPublishTime);
    [ObservableProperty] private string _timeError = "";
    [ObservableProperty] private bool _hasToken;
    [ObservableProperty] private string _tokenStatusText = "No token stored.";
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
            LoadDestination(s);
            RefreshToken();
            RefreshStatus();
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Mirror the persisted destination into the fields. Callers hold
    /// <see cref="_loading"/> so the setters do not write back.</summary>
    private void LoadDestination(PublishSettings s)
    {
        SelectedPreset = PresetOptions.First(p => p.Value == s.Preset);
        PresetHint = s.Preset == PublishPreset.Labs626 ? PublishPresets.Labs626Hint : "";
        EndpointUrl = s.EndpointUrl;
        EndpointError = EndpointErrorFor(s.EndpointUrl);
        SelectedAuth = AuthOptions.First(a => a.Value == s.AuthScheme);
        ShowHeaderName = s.AuthScheme == PublishAuthScheme.Header;
        NeedsToken = s.AuthScheme != PublishAuthScheme.None;
        AuthHeaderName = s.AuthHeaderName;
    }

    private static string EndpointErrorFor(string url)
        => string.IsNullOrWhiteSpace(url) || UsagePublisher.IsUsableEndpoint(url)
            ? ""
            : "Use an absolute http(s) URL, e.g. https://example.com/usage.";

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

    partial void OnSelectedPresetChanged(PublishPresetOption value)
    {
        if (_loading || _service is null || value is null)
            return;
        try
        {
            _service.ApplyPreset(value.Value);
            _loading = true;
            try { LoadDestination(_service.Load()); }
            finally { _loading = false; }
            RefreshToken();
            RefreshStatus();
        }
        catch { /* every UI path caught */ }
    }

    partial void OnEndpointUrlChanged(string value)
    {
        if (_loading || _service is null)
            return;
        try
        {
            _service.SetEndpointUrl(value ?? "");
            EndpointError = EndpointErrorFor(value ?? "");
            var normalized = (value ?? "").Trim();
            if (normalized != value)
            {
                _loading = true;
                try { EndpointUrl = normalized; }
                finally { _loading = false; }
            }
            RefreshStatus();
        }
        catch { /* every UI path caught */ }
    }

    partial void OnSelectedAuthChanged(PublishAuthOption value)
    {
        if (_loading || _service is null || value is null)
            return;
        try
        {
            _service.SetAuthScheme(value.Value);
            ShowHeaderName = value.Value == PublishAuthScheme.Header;
            NeedsToken = value.Value != PublishAuthScheme.None;
            RefreshToken();
            RefreshStatus();
        }
        catch { /* every UI path caught */ }
    }

    partial void OnAuthHeaderNameChanged(string value)
    {
        if (_loading || _service is null)
            return;
        try
        {
            _service.SetAuthHeaderName(value ?? "");
            var stored = _service.Load().AuthHeaderName;
            if (stored != value)
            {
                _loading = true;
                try { AuthHeaderName = stored; }
                finally { _loading = false; }
            }
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
    private void SetToken()
    {
        if (_service is null)
            return;
        try
        {
            var w = new PublishTokenWindow(PresetHint) { Owner = _owner };
            if (w.ShowDialog() == true && !string.IsNullOrWhiteSpace(w.Token))
            {
                _service.SaveToken(w.Token);
                RefreshToken();
                RefreshStatus();
            }
        }
        catch (Exception ex)
        {
            TokenStatusText = $"Couldn't store the token ({ex.GetType().Name}) - see sanduhr.log.";
        }
    }

    [RelayCommand]
    private void ClearToken()
    {
        if (_service is null)
            return;
        try
        {
            var res = ThemedDialog.Show(_owner, "Remove the publish token?",
                "Publishing stops until a new token is stored. Anything already published stays where you sent it.",
                MessageBoxButton.YesNo, ThemedDialogKind.Warning,
                primaryLabel: "Remove token", secondaryLabel: "Keep it");
            if (res != MessageBoxResult.Yes)
                return;
            _service.ClearToken();
            RefreshToken();
            RefreshStatus();
        }
        catch { /* every UI path caught */ }
    }

    /// <summary>Manual publish of yesterday — bypasses the schedule and the
    /// retry backoff, honors the destination, toggle and share map like every other path.</summary>
    [RelayCommand]
    private async Task PublishNow()
    {
        if (_service is null || IsPublishing)
            return;
        IsPublishing = true;
        try
        {
            var s = _service.Load();
            if (!s.HasEndpoint)
            {
                StatusText = "Set an endpoint URL first.";
                return;
            }
            if (!UsagePublisher.IsUsableEndpoint(s.EndpointUrl))
            {
                StatusText = "The endpoint URL must be an absolute http(s) URL.";
                return;
            }
            if (s.AuthScheme != PublishAuthScheme.None && !HasToken)
            {
                StatusText = "Set a publish token first.";
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

    private void RefreshToken()
    {
        if (_service is null)
            return;
        HasToken = _service.HasToken;
        TokenStatusText = !NeedsToken
            ? (HasToken ? "Token stored, but not sent: auth is set to None." : "No token needed: auth is set to None.")
            : HasToken
                ? "Token stored in Windows Credential Manager."
                : "No token stored.";
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
        else if (!s.HasEndpoint)
            next = " No endpoint set, so the daily publish will refuse.";
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
