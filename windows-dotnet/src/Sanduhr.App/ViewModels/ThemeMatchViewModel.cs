using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>
/// Settings, Desk Look, Match the widget theme (theme match): the Follow switch, Match now, a status line, and the plate note while the
/// readability guard holds the plate on. A cancelled Follow puts the switch back; Match now is off while following (Follow already keeps
/// the look).
/// </summary>
public sealed partial class ThemeMatchViewModel : ObservableObject
{
    private readonly ThemeMatchService _service;
    private bool _loading;
    private bool _followLine;   // the status line is the followed line Reload wrote, not an action's message

    internal ThemeMatchViewModel(ThemeMatchService service)
    {
        _service = service;
        Reload();
    }

    /// <summary>The service (tests replace its questions).</summary>
    internal ThemeMatchService Service => _service;

    /// <summary>Called after an action went ahead (SettingsViewModel reads the Desk's own look again and the Desk redraws).</summary>
    public Action? Changed { get; set; }

    /// <summary>True while the running Desk holds the plate on for readability (App sets it).</summary>
    public Func<bool> PlateForced { get; set; } = () => false;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(NotFollowing), nameof(PlateNote)), NotifyCanExecuteChangedFor(nameof(MatchNowCommand))]
    private bool _follow;

    [ObservableProperty] private string _status = "";

    public bool NotFollowing => !Follow;

    public string PlateNote => Follow && PlateForced() ? SettingsNames.ThemeMatchPlateNote : "";

    /// <summary>Reads the switch again (and the plate note) without acting on it. While following the status line says what the Desk
    /// follows; an action's own message is kept when not following.</summary>
    public void Reload()
    {
        _loading = true;
        Follow = _service.Following;
        _loading = false;
        if (Follow)
        {
            Status = SettingsNames.FollowingTheme(_service.ActiveName);
            _followLine = true;
        }
        else if (_followLine)
        {
            Status = "";
            _followLine = false;
        }
        OnPropertyChanged(nameof(PlateNote));
    }

    partial void OnFollowChanged(bool value)
    {
        if (_loading) return;
        var result = value ? _service.FollowOn() : _service.FollowOff();
        Reload();   // a Cancel, or a theme that could not be written, puts the switch back
        Status = result.Message;
        _followLine = false;
        if (result.Ok) Changed?.Invoke();
    }

    [RelayCommand(CanExecute = nameof(NotFollowing))]
    private void MatchNow()
    {
        var result = _service.MatchNow();
        Status = result.Message;
        if (result.Ok) Changed?.Invoke();
    }
}
