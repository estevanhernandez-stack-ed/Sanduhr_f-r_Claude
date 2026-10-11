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
    /// <summary>One entry in the "Tray shows" combo.</summary>
    public sealed record TrayModeOption(TrayMode Mode, string Title);

    private readonly StartupManager _startup;
    private readonly WidgetViewModel? _widget;

    /// <summary>The five tray modes, titled as the tray menu titles them.</summary>
    public IReadOnlyList<TrayModeOption> TrayModeOptions { get; } =
        TrayReadout.All.Select(m => new TrayModeOption(m, TrayReadout.Title(m))).ToList();

    /// <summary>The "Tray shows" combo selection. Reads and writes the widget's
    /// <see cref="WidgetViewModel.TrayMode"/>, so the tray menu and this combo stay in sync.</summary>
    public TrayModeOption? SelectedTrayMode
    {
        get => TrayModeOptions.FirstOrDefault(o => o.Mode == _widget?.TrayMode);
        set
        {
            if (value is not null && _widget is not null)
                _widget.TrayMode = value.Mode;
        }
    }

    /// <summary>True when this is an MSIX/Store install (toggle replaced by a
    /// deep-link button).</summary>
    public bool IsPackaged { get; }

    /// <summary>Convenience inverse for the unpackaged checkbox's visibility.</summary>
    public bool IsUnpackaged => !IsPackaged;

    /// <summary>The auto-start checkbox state (unpackaged). Two-way bound; flipping
    /// it writes/removes the Run entry via <see cref="OnAutoStartEnabledChanged"/>.</summary>
    [ObservableProperty] private bool _autoStartEnabled;

    public GeneralViewModel(StartupManager? startup = null, WidgetViewModel? widget = null)
    {
        _widget = widget;
        if (_widget is not null)
        {
            _widget.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(WidgetViewModel.TrayMode))
                    OnPropertyChanged(nameof(SelectedTrayMode));
            };
        }
        _startup = startup ?? StartupManager.CreateDefault();
        IsPackaged = _startup.IsPackaged();
        // Seed via the backing field so the initial read doesn't trigger a write.
        _autoStartEnabled = _startup.IsEnabled();
    }

    private void OnWidgetPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WidgetViewModel.TrayMode))
            OnPropertyChanged(nameof(SelectedTrayMode));
    }

    /// <summary>Stop following the widget (Settings closed). The widget outlives every
    /// Settings window, so a handler left on it would keep this view model alive.</summary>
    public void Detach()
    {
        if (_widget is not null)
            _widget.PropertyChanged -= OnWidgetPropertyChanged;
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
