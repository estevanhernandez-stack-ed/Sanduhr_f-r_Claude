using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>
/// The widget's now-playing line (spec 6.3, W8): "▶ Title · Artist" under the cards while something
/// plays, a click plays or pauses, Next shows while paused. Off until Settings, Now playing turns it
/// on; while off it does not ask the media sessions for anything. The text lives only here, in memory.
/// </summary>
public sealed partial class NowPlayingLineViewModel : ObservableObject, IDisposable
{
    private readonly NowPlayingHub _hub;
    private readonly SettingsStore _store;
    private NowPlayingSettings _settings = NowPlayingSettings.Default;

    internal NowPlayingLineViewModel(NowPlayingHub hub, SettingsStore store)
    {
        _hub = hub;
        _store = store;
        _hub.Changed += Refresh;
        _hub.PresenceChanged += Refresh;
        Reload();
    }

    [ObservableProperty] private bool _visible;
    [ObservableProperty] private string _text = "";
    [ObservableProperty] private bool _paused;

    /// <summary>The line's accessible name: what a click does.</summary>
    public string ToggleName => Paused ? "Play" : "Pause";

    partial void OnPausedChanged(bool value) => OnPropertyChanged(nameof(ToggleName));

    /// <summary>Settings changed: start or stop listening and redraw.</summary>
    public void Reload()
    {
        _settings = _store.LoadNowPlaying();
        _hub.Configure(_settings.Prefs);
        _hub.Want(this, _settings.Widget);
        Refresh();
    }

    private void Refresh()
    {
        // Up only while the hub's presence rule wants it (playing, or paused and kept, or inside the
        // 5 s grace): the same rule the Desk piece follows.
        var info = _settings.Widget && _hub.Presence.Wanted ? _settings.Prefs.Visible(_hub.Shown) : null;
        var line = NowPlayingText.Line(info);
        Visible = line is not null;
        Text = line ?? "";
        Paused = info?.State == NowPlayingState.Paused;
    }

    [RelayCommand]
    private void Toggle() => _hub.Service.TogglePlayPause();

    [RelayCommand]
    private void Next() => _hub.Service.Next();

    public void Dispose()
    {
        _hub.Changed -= Refresh;
        _hub.PresenceChanged -= Refresh;
        _hub.Want(this, false);
    }
}
