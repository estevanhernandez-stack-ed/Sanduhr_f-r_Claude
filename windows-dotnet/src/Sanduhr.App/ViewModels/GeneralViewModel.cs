using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>
/// Backs the Settings → General tab's auto-start control (item 10). Wraps Core's
/// <see cref="StartupManager"/> so the dual mechanism stays a single source of
/// truth: <b>unpackaged</b> installs get a toggle that writes/removes the HKCU Run
/// entry; <b>packaged</b> (MSIX/Store) installs — which can't flip the
/// <c>windows.startupTask</c> without WinRT — get a note + a button that deep-links
/// to Windows Settings → Startup apps. <b>Off by default</b> everywhere.
/// </summary>
public sealed partial class GeneralViewModel : ObservableObject
{
    private readonly StartupManager _startup;

    /// <summary>True when this is an MSIX/Store install (toggle replaced by a
    /// deep-link button).</summary>
    public bool IsPackaged { get; }

    /// <summary>Convenience inverse for the unpackaged checkbox's visibility.</summary>
    public bool IsUnpackaged => !IsPackaged;

    /// <summary>The auto-start checkbox state (unpackaged). Two-way bound; flipping
    /// it writes/removes the Run entry via <see cref="OnAutoStartEnabledChanged"/>.</summary>
    [ObservableProperty] private bool _autoStartEnabled;

    public GeneralViewModel(StartupManager? startup = null)
    {
        _startup = startup ?? StartupManager.CreateDefault();
        IsPackaged = _startup.IsPackaged();
        // Seed via the backing field so the initial read doesn't trigger a write.
        _autoStartEnabled = _startup.IsEnabled();
    }

    partial void OnAutoStartEnabledChanged(bool value)
    {
        // Unpackaged: applies immediately (Run key). Packaged would deep-link to
        // Settings, but the packaged UI shows a button, not this checkbox — so this
        // path only fires for the unpackaged toggle.
        var outcome = _startup.SetEnabled(value);
        if (outcome.Applied)
            Sounds.PlayToggle();
    }

    /// <summary>Packaged fallback — open Windows Settings → Startup apps.</summary>
    [RelayCommand]
    private void OpenStartupSettings() => _startup.OpenStartupSettings();
}
