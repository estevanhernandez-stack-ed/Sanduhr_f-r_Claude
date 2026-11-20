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
    private readonly Action<string>? _openLink;
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

    /// <summary>The release list to get the Arm build from; null unless this copy is an x64 build on an Arm PC (5.0 spec 7).</summary>
    public string? ArmBuildUrl { get; }
    /// <summary>The standing line and its link show for as long as the notice applies. Never on the Store build.</summary>
    public bool ArmBuildAvailable => ArmBuildUrl is not null && IsGitHubBuild;
    public string ArmBuildLine => ArmBuildNotice.AboutLine;
    public string ArmBuildGet => ArmBuildNotice.GetText;

    /// <param name="checkNow">App's <c>UpdateChecker.CheckNowAsync</c>; null in tests and the harness, which dims Check now.</param>
    /// <param name="armBuildUrl">Where to get the Arm build, or null where the Arm build notice does not apply.</param>
    /// <param name="openLink">Opens an https link in the browser.</param>
    public AboutUpdatesViewModel(SettingsStore store, bool isStoreBuild, Func<Task<UpdateProbeResult>>? checkNow,
        string? armBuildUrl = null, Action<string>? openLink = null)
    {
        _store = store;
        IsStoreBuild = isStoreBuild;
        _checkNow = checkNow;
        ArmBuildUrl = armBuildUrl;
        _openLink = openLink;
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

    [RelayCommand]
    private void GetArmBuild()
    {
        if (ArmBuildUrl is { } url)
            _openLink?.Invoke(url);
    }

    /// <summary>A new message, announced: the flag goes false first, so the live region raises again.</summary>
    private void Say(string text)
    {
        StatusShown = false;
        Status = text;
        StatusShown = true;
    }
}
