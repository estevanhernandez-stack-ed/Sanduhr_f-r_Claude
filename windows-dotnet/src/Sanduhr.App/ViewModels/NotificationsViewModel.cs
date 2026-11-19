using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>What stands between an agent and its notifications.</summary>
public enum AgentRowState
{
    Ready,

    /// <summary>Codex or Antigravity is off in Sources: nothing of it is read.</summary>
    SourceOff,

    /// <summary>Claude Code's hook is in no folder.</summary>
    HookMissing,

    /// <summary>Claude Code's hook is the 3.5 to 4.9 one: the glow works, the notification and the card do not.</summary>
    HookOutdated,

    /// <summary>No folder has a hook, and a folder's settings.json can't be read: whether it has one is unknown.</summary>
    HookUnreadable,
}

/// <summary>Settings, Notifications (5.0 spec 4): which agents notify, where it shows and when. Every change saves to
/// "agent_notifications" at once and tells the app, which re-reads the glow's switches.</summary>
public sealed partial class NotificationsViewModel : ObservableObject
{
    public sealed record ThresholdChoice(int Seconds, string Name)
    {
        public override string ToString() => Name;
    }

    public static IReadOnlyList<ThresholdChoice> ThresholdChoices { get; } =
    [
        new(0, "Every turn"), new(30, "30 seconds"), new(60, "1 minute"), new(120, "2 minutes"), new(300, "5 minutes"),
    ];

    private readonly SettingsStore _store;
    private readonly Func<string, AgentRowState> _agentState;
    private bool _loading;

    internal NotificationsViewModel(SettingsStore store, Func<string, AgentRowState> agentState)
    {
        _store = store;
        _agentState = agentState;
        _threshold = ThresholdChoices.Single(c => c.Seconds == NotificationSettings.DefaultThreshold);
        foreach (var agent in AgentSources.Burn) Agents.Add(new NotifyAgentRowViewModel(this, agent));
        Reload();
    }

    public ObservableCollection<NotifyAgentRowViewModel> Agents { get; } = [];

    [ObservableProperty] private bool _toast;
    [ObservableProperty] private bool _deskCard;
    [ObservableProperty] private bool _glowFinished;
    [ObservableProperty] private bool _glowWaiting;
    [ObservableProperty] private bool _flash;
    [ObservableProperty] private bool _skipInFront;
    [ObservableProperty] private ThresholdChoice _threshold;

    /// <summary>Set by App: a setting was saved (the glow service re-reads its switches).</summary>
    public Action? Changed { get; set; }

    /// <summary>Set by App: an agent's notifications went off (its Desk cards go).</summary>
    public Action<string>? AgentSwitchedOff { get; set; }

    /// <summary>Set by App: show a sample on the toast and the glow.</summary>
    public Action<AgentTurnKind>? TryIt { get; set; }

    /// <summary>Where the glow shows: the picker stays on the Claude Code page until spec 6 moves it.</summary>
    public SettingsLink GlowWhereLink { get; } = SettingsIndex.Check(SettingsPage.ClaudeCode, SettingsIndex.GlowWhere);

    /// <summary><see cref="GlowWhereLink"/> as the link text a button's <c>SettingsLinkTo.Link</c> carries ("claudecode#glow-where").</summary>
    public string GlowWhereLinkText => SettingsIndex.PageId(GlowWhereLink.Page) + "#" + GlowWhereLink.Anchor;

    /// <summary>Reads the saved settings and each agent's state again (Settings does this on each visit).</summary>
    public void Reload()
    {
        _loading = true;
        try
        {
            var s = _store.LoadAgentNotifications();
            Toast = s.Toast;
            DeskCard = s.DeskCard;
            GlowFinished = s.GlowFinished;
            GlowWaiting = s.GlowWaiting;
            Flash = s.Flash;
            SkipInFront = s.SkipInFront;
            Threshold = ThresholdChoices.Single(c => c.Seconds == s.ThresholdSeconds);
            foreach (var row in Agents) row.Load(s.AgentOn(row.Agent), _agentState(row.Agent));
        }
        finally { _loading = false; }
    }

    partial void OnToastChanged(bool value) => Save();
    partial void OnDeskCardChanged(bool value) => Save();
    partial void OnGlowFinishedChanged(bool value) => Save();
    partial void OnGlowWaitingChanged(bool value) => Save();
    partial void OnFlashChanged(bool value) => Save();
    partial void OnSkipInFrontChanged(bool value) => Save();
    partial void OnThresholdChanged(ThresholdChoice value) => Save();

    internal void AgentToggled(string agent, bool on)
    {
        if (_loading) return;
        // Only a saved switch clears the cards: a failed save snaps the switch back on.
        if (Save() && !on) AgentSwitchedOff?.Invoke(agent);
    }

    /// <summary>Saves every switch; true when settings.json took it. A failed save snaps the switches back to what is saved.</summary>
    private bool Save()
    {
        if (_loading) return false;
        try
        {
            var agents = Agents.ToDictionary(a => a.Agent, a => a.On, StringComparer.Ordinal);
            if (_store.TrySaveAgentNotifications(new NotificationSettings(agents, Toast, GlowFinished, GlowWaiting, Flash, DeskCard,
                    Threshold.Seconds, SkipInFront)))
            {
                Changed?.Invoke();
                return true;
            }
            System.Diagnostics.Debug.WriteLine("[Sanduhr.Notifications] not saved: settings.json could not be read or written");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"[Sanduhr.Notifications] not saved: {e.GetType().Name}");
        }
        Reload();   // the switches snap back to what is saved
        return false;
    }

    [RelayCommand] private void TryFinished() => TryIt?.Invoke(AgentTurnKind.Finished);
    [RelayCommand] private void TryWaiting() => TryIt?.Invoke(AgentTurnKind.Waiting);
}

/// <summary>One agent's row: its name, what its files can tell Sanduhr, its switch, and what stands in its way.</summary>
public sealed partial class NotifyAgentRowViewModel : ObservableObject
{
    private readonly NotificationsViewModel _owner;

    internal NotifyAgentRowViewModel(NotificationsViewModel owner, string agent)
    {
        _owner = owner;
        Agent = agent;
    }

    public string Agent { get; }
    public string Name => AgentSources.BurnName(Agent);
    public string Supports => SettingsNames.NotifySupports(Agent);
    public string SwitchName => SettingsNames.NotifyAgentSwitch(Agent);

    /// <summary>The list item's spoken name: the agent, not the type.</summary>
    public override string ToString() => Name;

    [ObservableProperty] private bool _on;
    [ObservableProperty] private bool _canToggle = true;
    [ObservableProperty] private string _stateText = "";

    public bool HasStateText => StateText.Length > 0;

    partial void OnStateTextChanged(string value) => OnPropertyChanged(nameof(HasStateText));
    partial void OnOnChanged(bool value) => _owner.AgentToggled(Agent, value);

    internal void Load(bool on, AgentRowState state)
    {
        On = on;
        CanToggle = state is AgentRowState.Ready or AgentRowState.HookOutdated;
        StateText = state switch
        {
            AgentRowState.SourceOff => SettingsNames.NotifySourceOff,
            AgentRowState.HookMissing => SettingsNames.NotifyHookMissing,
            AgentRowState.HookOutdated => SettingsNames.NotifyHookOutdated,
            AgentRowState.HookUnreadable => SettingsNames.NotifyHookUnreadable,
            _ => "",
        };
    }
}
