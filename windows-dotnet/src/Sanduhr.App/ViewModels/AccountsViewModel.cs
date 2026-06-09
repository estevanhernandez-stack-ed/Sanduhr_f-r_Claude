using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.App.Views;

namespace Sanduhr.App.ViewModels;

/// <summary>One row in the Accounts tab / quick-switch menus: a registry label and
/// whether it is the active account (drives the bullet + "(active)" tag).</summary>
public sealed partial class AccountItemViewModel : ObservableObject
{
    public string Label { get; }

    [ObservableProperty] private bool _isActive;

    public AccountItemViewModel(string label, bool isActive)
    {
        Label = label;
        _isActive = isActive;
    }

    /// <summary>List glyph — a filled dot for the active account, blank otherwise.</summary>
    public string Marker => IsActive ? "●" : "";

    partial void OnIsActiveChanged(bool value) => OnPropertyChanged(nameof(Marker));
}

/// <summary>
/// Drives the Settings ▸ Accounts tab — the multi-account registry surface ported
/// from <c>accounts_dialog.py</c>'s <c>AccountsTab</c>. Lists every account, marks
/// the active one, and exposes Switch / Rename / Sign out / Add. All mutations are
/// delegated to the <see cref="WidgetViewModel"/> (the single owner of account
/// operations + the live fetcher), so a change here switches the widget at once and
/// the anti-bleed transport rebuild happens in one place.
///
/// "Add account" is delegated up to the App via an injected callback because it
/// needs the WebView2 sign-in window owner + the runtime/manual-paste fallbacks the
/// <c>SignInCoordinator</c> already owns.
/// </summary>
public sealed partial class AccountsViewModel : ObservableObject
{
    private readonly WidgetViewModel _widget;
    private readonly Func<Task> _addAccountAsync;
    private Window? _owner;

    public ObservableCollection<AccountItemViewModel> Accounts { get; } = new();

    [ObservableProperty] private bool _hasNoAccounts;

    /// <summary>Active account label for the General tab summary, or "None".</summary>
    [ObservableProperty] private string _activeLabel = "None";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SwitchToCommand))]
    [NotifyCanExecuteChangedFor(nameof(RenameCommand))]
    [NotifyCanExecuteChangedFor(nameof(SignOutCommand))]
    private AccountItemViewModel? _selectedAccount;

    public AccountsViewModel(WidgetViewModel widget, Func<Task> addAccountAsync)
    {
        _widget = widget;
        _addAccountAsync = addAccountAsync;
        Reload();
    }

    /// <summary>Owner window for modal dialogs (rename prompt, confirm boxes).
    /// Set by the hosting <c>SettingsWindow</c> once it exists.</summary>
    public void AttachOwner(Window owner) => _owner = owner;

    /// <summary>Re-read the registry and rebuild the row list, preserving the
    /// selection by label when possible.</summary>
    public void Reload()
    {
        var prev = SelectedAccount?.Label;
        Accounts.Clear();
        var active = _widget.ActiveAccount;
        foreach (var label in _widget.ListAccounts())
            Accounts.Add(new AccountItemViewModel(label, label == active));
        HasNoAccounts = Accounts.Count == 0;
        ActiveLabel = active ?? "None";
        SelectedAccount = Accounts.FirstOrDefault(a => a.Label == prev)
                          ?? Accounts.FirstOrDefault(a => a.IsActive)
                          ?? Accounts.FirstOrDefault();
    }

    private bool HasSelection => SelectedAccount is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task SwitchTo()
    {
        var item = SelectedAccount;
        if (item is null || item.IsActive)
            return;
        await _widget.SwitchAccountCommand.ExecuteAsync(item.Label);
        Reload();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Rename()
    {
        var item = SelectedAccount;
        if (item is null)
            return;
        var prompt = new TextPromptWindow("Rename account", "New name", item.Label);
        if (_owner is not null && _owner.IsLoaded)
            prompt.Owner = _owner;
        if (prompt.ShowDialog() != true)
            return;
        var newName = prompt.Value.Trim();
        if (string.IsNullOrEmpty(newName) || newName == item.Label)
            return;
        try
        {
            _widget.RenameAccount(item.Label, newName);
        }
        catch (Exception ex)
        {
            ShowError($"Couldn't rename: {ex.Message}");
            return;
        }
        Reload();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task SignOut()
    {
        var item = SelectedAccount;
        if (item is null)
            return;
        var text =
            $"Remove the '{item.Label}' account from Sanduhr?\n\n" +
            "This deletes the stored credentials and the local " +
            $"history.{item.Label}.json file. Cannot be undone.";
        var result = _owner is not null && _owner.IsLoaded
            ? MessageBox.Show(_owner, text, "Sign out", MessageBoxButton.YesNo, MessageBoxImage.Warning)
            : MessageBox.Show(text, "Sign out", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
            return;
        await _widget.SignOutAccountAsync(item.Label);
        Reload();
    }

    [RelayCommand]
    private async Task Add()
    {
        await _addAccountAsync();
        Reload();
    }

    private void ShowError(string message)
    {
        if (_owner is not null && _owner.IsLoaded)
            MessageBox.Show(_owner, message, "Accounts", MessageBoxButton.OK, MessageBoxImage.Warning);
        else
            MessageBox.Show(message, "Accounts", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
