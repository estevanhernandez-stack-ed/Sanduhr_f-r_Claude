using System.Windows.Controls;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Services;

/// <summary>
/// Builds the dynamic "quick switch account" menu items shared by the two
/// pick-from-list widget entry points: the tray <c>Accounts ▸</c> submenu and the
/// widget right-click <c>Accounts ▸</c> submenu. (The content-area account label is
/// a separate round-robin cycle-on-click switcher, not a menu.) Each is rebuilt on
/// open (the account registry changes at runtime), so this is a stateless filler
/// over an <see cref="ItemCollection"/>. The active account carries a bullet prefix
/// (matching <c>accounts_dialog.py</c>'s "●  label   (active)" convention); clicking
/// any account routes to <see cref="WidgetViewModel.SwitchAccountCommand"/> with the
/// label, which performs the active-pointer + anti-bleed fetcher rebuild.
/// </summary>
internal static class AccountMenuBuilder
{
    public static void PopulateSwitchList(
        ItemCollection items, WidgetViewModel vm, bool includeAdd, bool includeManage)
    {
        items.Clear();

        // Demo mode lists the sample accounts, so a screenshot of the menu names no real one;
        // picking one shows that sample.
        var active = vm.ShownActiveAccount;   // streamer: masked - via Header: each menu row masks its label (CP 2)
        var accounts = vm.ShownAccounts;
        var inUse = vm.OthersInUse;
        var switchCommand = vm.IsDemo ? vm.ShowSampleAccountCommand : (System.Windows.Input.ICommand)vm.SwitchAccountCommand;

        if (accounts.Count == 0)
        {
            items.Add(new MenuItem { Header = "No accounts yet", IsEnabled = false });
        }
        else
        {
            foreach (var label in accounts)
            {
                bool isActive = label == active;
                // Follow saw this account in use in its latest check: the menu is rebuilt on every
                // open, so the mark is as fresh as the follower's last reading.
                items.Add(new MenuItem
                {
                    Header = Header(label, isActive, !isActive && inUse.Contains(label), vm.IsDemo),
                    Command = switchCommand,
                    CommandParameter = label,
                    // The active account is the no-op target; dim it so the live one reads clearly.
                    IsEnabled = !isActive,
                });
            }
        }

        if (includeAdd || includeManage)
            items.Add(new Separator());

        if (includeAdd)
            items.Add(new MenuItem { Header = SettingsNames.AddAccount, Command = vm.SignInCommand });

        if (includeManage)
            items.Add(new MenuItem { Header = SettingsNames.LinkTitle(SettingsPage.Accounts), Command = vm.OpenSettingsPageCommand, CommandParameter = SettingsPage.Accounts });
    }

    /// <summary>A row's text: the bullet for the active account, the label (its codename while streamer mode is on, slice 7 M2), and
    /// " · in use" when Follow saw it in use. The row's command still takes the real label. Demo's sample labels are not real names
    /// and stay out of the codename book (preflight P17).</summary>
    internal static string Header(string label, bool isActive, bool inUse, bool demo = false)
    {
        var shown = demo ? label : StreamerMode.Mask.Name(CodenameCategory.Account, label);   // streamer: demo - the sample labels of demo mode are not real names and stay out of the book (P17)
        return (isActive ? $"●  {shown}" : $"     {shown}") + (inUse ? " · in use" : "");
    }
}
