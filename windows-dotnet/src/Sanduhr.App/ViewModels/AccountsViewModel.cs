using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.App.Services;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>One row in the Accounts tab / quick-switch menus: a registry label and
/// whether it is the active account (drives the bullet + "(active)" tag).</summary>
public sealed partial class AccountItemViewModel : ObservableObject
{
    public string Label { get; }

    /// <summary>What the row shows: the label, or its codename while streamer mode hides it (slice 7, CP 1). <see cref="Label"/> stays the key.</summary>
    public string Shown { get; }

    [ObservableProperty] private bool _isActive;

    public AccountItemViewModel(string label, bool isActive)
    {
        Label = label;
        Shown = StreamerMode.Mask.Name(CodenameCategory.Account, label);
        _isActive = isActive;
    }

    /// <summary>List glyph — a filled dot for the active account, blank otherwise.</summary>
    public string Marker => IsActive ? "●" : "";

    partial void OnIsActiveChanged(bool value) => OnPropertyChanged(nameof(Marker));

    /// <summary>What a screen reader reads for the row: "Work, active", never the type name.</summary>
    public override string ToString() => IsActive ? Shown + ", active" : Shown;
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
    private readonly Func<string, string> _displayFolder;
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
    [NotifyCanExecuteChangedFor(nameof(ClearHistoryCommand))]
    private bool _isDemo;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SwitchToCommand))]
    [NotifyCanExecuteChangedFor(nameof(RenameCommand))]
    [NotifyCanExecuteChangedFor(nameof(SignOutCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateSignInCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearHistoryCommand))]
    private AccountItemViewModel? _selectedAccount;

    public AccountsViewModel(WidgetViewModel widget, Func<Task> addAccountAsync, Func<string, Task> updateSignInAsync,
        Func<string, string>? displayFolder = null)
    {
        _widget = widget;
        _addAccountAsync = addAccountAsync;
        _updateSignInAsync = updateSignInAsync;
        _displayFolder = displayFolder ?? (path => StreamerMode.ShowFolder(path, ClaudeCodeProfile.Dir));   // slice 7: CP 7's bypass   // streamer: masked - CP 7: ShowFolder masks on this line
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
        var active = _widget.ShownActiveAccount;   // streamer: masked - via AccountItemViewModel: each row's Shown is Mask.Name (CP 1)
        foreach (var label in _widget.ShownAccounts)
            Accounts.Add(new AccountItemViewModel(label, label == active));
        HasNoAccounts = Accounts.Count == 0;
        OnPropertyChanged(nameof(FollowAccount));
        ActiveLabel = active ?? "None";
        SelectedAccount = Accounts.FirstOrDefault(a => a.Label == prev)
                          ?? Accounts.FirstOrDefault(a => a.IsActive)
                          ?? Accounts.FirstOrDefault();
        RefreshData();
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
        string? typed;
        if (RenameHook is { } hook)
            typed = hook(RenamePrefill(item.Label));
        else
        {
            var prompt = new TextPromptWindow("Rename account", "New name", RenamePrefill(item.Label));
            if (_owner is not null && _owner.IsLoaded)
                prompt.Owner = _owner;
            typed = prompt.ShowDialog() == true ? prompt.Value : null;
        }
        if (typed is null)
            return;
        var newName = typed.Trim();
        if (string.IsNullOrEmpty(newName) || newName == item.Label)
            return;
        try
        {
            _widget.RenameAccount(item.Label, newName);
        }
        catch (Exception ex)
        {
            ShowError(RenameError(item.Label, ex));
            return;
        }
        Reload();
    }

    /// <summary>Tests answer the Rename prompt here (given the prompt's prefill, returning the typed text or null for Cancel).</summary>
    internal Func<string, string?>? RenameHook { get; set; }

    /// <summary>The Rename prompt's text: empty while streamer mode hides the name, so a codename can never be saved as the name (decision 12).
    /// The saved value is whatever the person types (or the model's real label), never the text a masked control shows.</summary>
    internal static string RenamePrefill(string label) => StreamerMode.Mask.Hides(CodenameCategory.Account, label) ? "" : label;

    /// <summary>The rename error, naming the account as the page does (CP 25). The new name is the person's own typing and stays.</summary>
    internal static string RenameError(string label, Exception ex) =>
        $"Couldn't rename: {StreamerMode.Mask.Paths(ex.Message.Replace(label, StreamerMode.Mask.Name(CodenameCategory.Account, label), StringComparison.Ordinal))}";   // streamer: masked - CP 18: the rename error passes Mask.Paths and Mask.Name on this line

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
        if (item is null || IsDemo || _widget.IsDemo)
            return;
        if (!ConfirmRemove(item.Label))
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

    // ---- Data for this account (Settings v2, slice 5; WF1, WF27, WF29) ----

    /// <summary>The Data section shows while an account is selected.</summary>
    public bool HasData => SelectedAccount is not null;

    /// <summary>"Linked to ~\.claude-work." or "No Claude Code folder is linked to this account." (an account links one folder).</summary>
    [ObservableProperty] private string _linkedFolderText = "";

    /// <summary>Under Remove account…: everything it removes, the same sentence its question shows.</summary>
    [ObservableProperty] private string _removeCaption = "";

    public string ClearHistoryName => SettingsNames.ClearAccountHistoryNamed(SelectedAccount?.Shown ?? "");
    public string RemoveName => SettingsNames.RemoveAccountNamed(SelectedAccount?.Shown ?? "");

    /// <summary>Connected by Settings: the meter history redraws after a clear.</summary>
    public Action? HistoryCleared { get; set; }

    partial void OnSelectedAccountChanged(AccountItemViewModel? value) => RefreshData();

    private void RefreshData()
    {
        var label = SelectedAccount?.Label;
        var folder = label is null || IsDemo ? null : _widget.AccountFolder(label);
        LinkedFolderText = label is null ? "" : SettingsNames.LinkedFolderLine(folder is null ? null : _displayFolder(folder));
        RemoveCaption = label is null ? "" : SettingsNames.RemoveAccountDetail(SelectedAccount!.Shown, StreamerMode.Mask.Hides(CodenameCategory.Account, label));
        OnPropertyChanged(nameof(HasData));
        OnPropertyChanged(nameof(ClearHistoryName));
        OnPropertyChanged(nameof(RemoveName));
    }

    /// <summary>"Clear this account's history…" (WF1): the selected account's 30-day meter history, after a question with no default
    /// button. Never in demo mode, whose sample labels can match a real account's.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void ClearHistory()
    {
        if (IsDemo || _widget.IsDemo || SelectedAccount is not { } item)
            return;
        if (!ConfirmClear(item.Label))
            return;
        try
        {
            _widget.History.ClearAll(item.Label);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Sounds.PlayError();
            Info(SettingsNames.HistoryNotCleared);
            return;
        }
        HistoryCleared?.Invoke();
        Info(SettingsNames.HistoryCleared(item.Shown));
    }

    /// <summary>Tests answer the questions and read the notices here instead of showing them.</summary>
    internal Func<string, bool>? ConfirmClearHook { get; set; }
    internal Func<string, bool>? ConfirmRemoveHook { get; set; }
    internal Action<string>? InfoHook { get; set; }

    /// <summary>The question last asked (clear or remove), as shown; tests read it.</summary>
    internal string? LastQuestion { get; private set; }

    // The questions name the account as the row does (slice 7, CP 1); the hooks and the action keep the real label.
    private bool ConfirmClear(string label)
    {
        LastQuestion = SettingsNames.ClearHistoryQuestion(StreamerMode.Mask.Name(CodenameCategory.Account, label));
        return ConfirmClearHook is { } hook
            ? hook(label)
            : ThemedDialog.ShowConfirm(_owner, LastQuestion, SettingsNames.ClearHistoryDetail,
                SettingsNames.ClearHistoryConfirm, SettingsNames.ClearHistoryCancel);
    }

    private bool ConfirmRemove(string label)
    {
        LastQuestion = SettingsNames.RemoveAccountQuestion(StreamerMode.Mask.Name(CodenameCategory.Account, label));
        return ConfirmRemoveHook is { } hook
            ? hook(label)
            : ThemedDialog.ShowConfirm(_owner, LastQuestion,
                SettingsNames.RemoveAccountDetail(StreamerMode.Mask.Name(CodenameCategory.Account, label), StreamerMode.Mask.Hides(CodenameCategory.Account, label)),
                SettingsNames.RemoveAccountConfirm, SettingsNames.RemoveAccountCancel);
    }

    private void Info(string message)
    {
        if (InfoHook is { } hook) { hook(message); return; }
        ThemedDialog.Show(_owner, SettingsNames.MeterHistoryDialogTitle, message, MessageBoxButton.OK, ThemedDialogKind.Info);
    }

    private void ShowError(string message)
        => ThemedDialog.Show(_owner, "Accounts", message, MessageBoxButton.OK, ThemedDialogKind.Warning);
}
