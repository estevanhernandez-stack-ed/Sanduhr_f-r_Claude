using System.IO;
using System.Windows;
using System.Windows.Controls;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Settings v2, slice 5: Accounts, Data for this account (WF1, WF27, WF29). "Clear this account's history…" acts on the account the list
/// selects (it moved here from History, slice 0's fix included); Remove account… says under it everything it removes.
/// </summary>
[Collection("SettingsWindow")]
public sealed class AccountsDataTests : AppWindowFixture
{
    /// <summary>A real (not demo) widget over scratch folders and an in-memory registry: Work active, Personal second, each with history.</summary>
    private (WidgetViewModel Widget, AccountsViewModel Accounts, List<string> Asked, List<string> Told) Open()
    {
        var (widget, _) = ScratchWidget("Work", "Personal");
        widget.History.AppendForAccount("five_hour", 41, "Work");
        widget.History.AppendForAccount("five_hour", 17, "Personal");
        var asked = new List<string>();
        var told = new List<string>();
        var accounts = new AccountsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask, path => "~\\" + Path.GetFileName(path))
        {
            ConfirmClearHook = label => { asked.Add(label); return true; },
            InfoHook = told.Add,
        };
        return (widget, accounts, asked, told);
    }

    private static void Pick(AccountsViewModel accounts, string label) =>
        accounts.SelectedAccount = accounts.Accounts.Single(a => a.Label == label);

    [Fact]
    public void Clearing_clears_the_selected_account_not_the_active_one() => AppHost.Run(() =>
    {
        var (widget, accounts, asked, told) = Open();
        try
        {
            Pick(accounts, "Personal");
            int redraws = 0;
            accounts.HistoryCleared = () => redraws++;

            accounts.ClearHistoryCommand.Execute(null);

            Assert.Equal(["Personal"], asked);
            Assert.Empty(widget.History.Load("five_hour", "Personal"));
            Assert.Equal([41], widget.History.Load("five_hour", "Work"));
            Assert.Equal([SettingsNames.HistoryCleared("Personal")], told);
            Assert.Equal(1, redraws);
            var paths = ScratchPaths();
            Assert.True(File.Exists(paths.HistoryFileFor("Personal")));
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void Declining_clears_nothing() => AppHost.Run(() =>
    {
        var (widget, accounts, _, _) = Open();
        try
        {
            accounts.ConfirmClearHook = _ => false;
            Pick(accounts, "Personal");

            accounts.ClearHistoryCommand.Execute(null);

            Assert.Equal([17], widget.History.Load("five_hour", "Personal"));
            Assert.Equal([41], widget.History.Load("five_hour", "Work"));
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void Demo_mode_never_clears_a_real_history_file() => AppHost.Run(() =>
    {
        var (widget, accounts, asked, _) = Open();
        try
        {
            Pick(accounts, "Personal");
            widget.StartDemo();   // the sample accounts are named Work and Personal too

            accounts.ClearHistoryCommand.Execute(null);   // CanExecute is not enforced on a programmatic Execute

            Assert.Empty(asked);
            Assert.Equal([17], widget.History.Load("five_hour", "Personal"));
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void The_data_section_names_the_linked_folder_and_what_Remove_removes() => AppHost.Run(() =>
    {
        var (widget, accounts, _, _) = Open();
        try
        {
            widget.SetAccountFolder("Personal", Path.Combine(Profile, ".claude-personal"));

            Pick(accounts, "Personal");
            Assert.True(accounts.HasData);
            Assert.Equal(SettingsNames.LinkedFolderLine("~\\.claude-personal"), accounts.LinkedFolderText);
            Assert.Equal(SettingsNames.RemoveAccountDetail("Personal"), accounts.RemoveCaption);
            Assert.Equal(SettingsNames.ClearAccountHistoryNamed("Personal"), accounts.ClearHistoryName);
            Assert.Equal(SettingsNames.RemoveAccountNamed("Personal"), accounts.RemoveName);

            Pick(accounts, "Work");
            Assert.Equal(SettingsNames.LinkedFolderLine(null), accounts.LinkedFolderText);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void Declining_Remove_keeps_the_account() => AppHost.Run(() =>
    {
        var (widget, accounts, _, _) = Open();
        try
        {
            string? asked = null;
            accounts.ConfirmRemoveHook = label => { asked = label; return false; };
            Pick(accounts, "Personal");

            accounts.SignOutCommand.Execute(null);

            Assert.Equal("Personal", asked);
            Assert.Contains("Personal", widget.AccountStore.ListAccounts());
            Assert.Equal([17], widget.History.Load("five_hour", "Personal"));
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void Confirming_Remove_removes_the_account_and_its_history() => AppHost.Run(() =>
    {
        var (widget, accounts, _, _) = Open();
        try
        {
            accounts.ConfirmRemoveHook = _ => true;
            Pick(accounts, "Personal");

            // The sign-out continues on this dispatcher thread: pump it rather than block on the task.
            var signOut = accounts.SignOutCommand.ExecuteAsync(null);
            var until = DateTime.UtcNow.AddSeconds(10);
            while (!signOut.IsCompleted && DateTime.UtcNow < until)
            {
                Pump();
                Thread.Sleep(15);
            }
            Assert.True(signOut.IsCompletedSuccessfully, "remove did not finish");

            Assert.DoesNotContain("Personal", widget.AccountStore.ListAccounts());
            Assert.DoesNotContain(accounts.Accounts, a => a.Label == "Personal");
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void Signed_out_the_page_says_what_fills_in_and_shows_no_data_section() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget();
        try
        {
            var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
            var window = new SettingsWindow(svm);
            var guard = NoActivate.Show(window);
            try
            {
                window.SelectPage(SettingsPage.Accounts, SettingsIndex.AccountList);
                window.UpdateLayout();
                Pump();
                guard.Check();
                var tab = ((TabControl)window.FindName("Tabs")).Items.OfType<TabItem>().Single(t => (string)t.Tag == "Accounts");
                Assert.True(((FrameworkElement)window.FindName("SignedOutAccountsLine")).IsVisible);
                Assert.False(SettingsAnchor.Find((DependencyObject)tab.Content, SettingsIndex.AccountData)!.IsVisible);
                Assert.True(SettingsAnchor.Find((DependencyObject)tab.Content, SettingsIndex.SignInSection)!.IsVisible);
            }
            finally { window.Close(); }
        }
        finally { widget.Dispose(); }
    });
}
