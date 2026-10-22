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

    /// <summary>What a screen reader reads for the row: "Work, active", never the type name.</summary>
    public override string ToString() => IsActive ? Label + ", active" : Label;
}

/// <summary>
/// Drives the Settings ▸ Accounts tab — the multi-account registry surface ported
/// from <c>accounts_dialog.py</c>'s <c>AccountsTab</c>. Lists every account, marks
/// the active one, and exposes Switch / Rename / Remove account / Add. All mutations are
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
    private readonly Func<string, Task> _updateSignInAsync;
    private Window? _owner;

    public ObservableCollection<AccountItemViewModel> Accounts { get; } = new();

    [ObservableProperty] private bool _hasNoAccounts;

    /// <summary>Active account label, or "None".</summary>
    [ObservableProperty] private string _activeLabel = "None";

    /// <summary>Demo mode: the list shows the sample accounts and their actions are off.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SwitchToCommand))]
    [NotifyCanExecuteChangedFor(nameof(RenameCommand))]
    [NotifyCanExecuteChangedFor(nameof(SignOutCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateSignInCommand))]
    private bool _isDemo;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SwitchToCommand))]
    [NotifyCanExecuteChangedFor(nameof(RenameCommand))]
    [NotifyCanExecuteChangedFor(nameof(SignOutCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateSignInCommand))]
    private AccountItemViewModel? _selectedAccount;

    public AccountsViewModel(WidgetViewModel widget, Func<Task> addAccountAsync, Func<string, Task> updateSignInAsync)
    {
        _widget = widget;
        _addAccountAsync = addAccountAsync;
        _updateSignInAsync = updateSignInAsync;
        Reload();
    }

    /// <summary>"Follow the account I'm using": the widget owns the setting and the 15-minute check.</summary>
    public bool FollowAccount
    {
        get => _widget.FollowAccount;
        set
        {
            if (_widget.FollowAccount == value) return;
            _widget.FollowAccount = value;
            OnPropertyChanged();
        }
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
        IsDemo = _widget.IsDemo;
        var active = _widget.ShownActiveAccount;
        foreach (var label in _widget.ShownAccounts)
            Accounts.Add(new AccountItemViewModel(label, label == active));
        HasNoAccounts = Accounts.Count == 0;
        OnPropertyChanged(nameof(FollowAccount));
        ActiveLabel = active ?? "None";
        SelectedAccount = Accounts.FirstOrDefault(a => a.Label == prev)
                          ?? Accounts.FirstOrDefault(a => a.IsActive)
                          ?? Accounts.FirstOrDefault();
    }

    // Demo mode's samples are not real accounts: nothing to switch, rename, update or remove.
    private bool HasSelection => SelectedAccount is not null && !IsDemo;

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

    /// <summary>Refresh the selected account's credentials IN PLACE — works for
    /// non-active accounts too (pre-WS-A, only the active account could reauth,
    /// and only from the widget's recovery card). Routing by origin happens in
    /// the injected delegate (App owns the coordinator + window ownership).</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task UpdateSignIn()
    {
        var item = SelectedAccount;
        if (item is null)
            return;
        await _updateSignInAsync(item.Label);
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
            "This deletes its saved sign-in from the Windows Credential Manager " +
            $"and its usage-history file (history.{item.Label}.json). Cannot be undone.";
        var result = ThemedDialog.Show(_owner, "Remove account", text, MessageBoxButton.YesNo, ThemedDialogKind.Warning);
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
        => ThemedDialog.Show(_owner, "Accounts", message, MessageBoxButton.OK, ThemedDialogKind.Warning);
}
