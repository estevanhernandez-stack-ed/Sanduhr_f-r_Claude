using CommunityToolkit.Mvvm.ComponentModel;
using Sanduhr.App.Services;

namespace Sanduhr.App.ViewModels;

/// <summary>
/// About, Version (Settings v2, slice 5; WF24): "Don't show What's New after updates" ("whats_new_hide_after_updates"). The What's New
/// window keeps its own box on the same key, so About reads it again on each visit and when Settings is activated (<see cref="Reload"/>).
/// </summary>
public sealed partial class AboutVersionViewModel : ObservableObject
{
    private readonly SettingsStore _store;
    private bool _loading;

    [ObservableProperty] private bool _hideWhatsNewAfterUpdates;

    public AboutVersionViewModel(SettingsStore store)
    {
        _store = store;
        Reload();
    }

    /// <summary>Read the key again (About was opened or Settings activated; the What's New window may have changed it).</summary>
    public void Reload()
    {
        _loading = true;
        HideWhatsNewAfterUpdates = _store.LoadWhatsNewHideAfterUpdates();
        _loading = false;
    }

    partial void OnHideWhatsNewAfterUpdatesChanged(bool value)
    {
        if (!_loading)
            _store.SaveWhatsNewHideAfterUpdates(value);
    }
}
