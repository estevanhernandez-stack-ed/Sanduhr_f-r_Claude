using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.App.Services;
using Sanduhr.App.Updates;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>
/// About, Updates (Settings v2, slice 6; WF6).
/// <list type="bullet">
/// <item><description>On a GitHub install: "Check for updates once a day" (settings.json "update_check", read by <c>UpdateChecker</c> and
/// <c>StoreUpdateService</c>), and "Check now", which asks once whatever the switch says (decision 7).</description></item>
/// <item><description>On the Store build: one line, "The Microsoft Store keeps Sanduhr up to date."</description></item>
/// </list>
/// Each new status line pulses <see cref="StatusShown"/>, so it is announced: <c>LiveRegion.Shown</c> raises only on the way to true. It
/// subscribes to nothing.
/// </summary>
public sealed partial class AboutUpdatesViewModel : ObservableObject
{
    private readonly SettingsStore _store;
    private readonly Func<Task<UpdateProbeResult>>? _checkNow;
    private bool _loading;

    /// <summary>"Check for updates once a day" (settings.json "update_check").</summary>
    [ObservableProperty] private bool _checkDaily;
    /// <summary>What Check now found, in words; empty until it is pressed.</summary>
    [ObservableProperty] private string _status = "";
    /// <summary>The status line's live-region flag, pulsed for each new message.</summary>
    [ObservableProperty] private bool _statusShown;

    public bool IsStoreBuild { get; }
    public bool IsGitHubBuild => !IsStoreBuild;
    public string Caption => SettingsNames.UpdatesCaption;

    /// <param name="checkNow">App's <c>UpdateChecker.CheckNowAsync</c>; null in tests and the harness, which dims Check now.</param>
    public AboutUpdatesViewModel(SettingsStore store, bool isStoreBuild, Func<Task<UpdateProbeResult>>? checkNow)
    {
        _store = store;
        IsStoreBuild = isStoreBuild;
        _checkNow = checkNow;
        Reload();
    }

    /// <summary>Read the switch again (About was opened).</summary>
    public void Reload()
    {
        _loading = true;
        CheckDaily = _store.LoadUpdateCheck();
        _loading = false;
    }

    partial void OnCheckDailyChanged(bool value)
    {
        if (!_loading)
            _store.SaveUpdateCheck(value);
    }

    private bool CanCheckNow() => _checkNow is not null;

    [RelayCommand(CanExecute = nameof(CanCheckNow))]
    private async Task CheckNow()
    {
        Say(SettingsNames.UpdateChecking);
        var result = await _checkNow!();
        Say(result.Status switch
        {
            UpdateProbeStatus.UpToDate => SettingsNames.UpdateUpToDate,
            UpdateProbeStatus.Ready when result.Version is { } version => SettingsNames.UpdateReady(version),
            UpdateProbeStatus.NotInstalled => SettingsNames.UpdateNotInstalled,
            _ => SettingsNames.UpdateFailed,
        });
    }

    /// <summary>A new message, announced: the flag goes false first, so the live region raises again.</summary>
    private void Say(string text)
    {
        StatusShown = false;
        Status = text;
        StatusShown = true;
    }
}
