using System.Windows.Controls;
using Sanduhr.App.ViewModels;

namespace Sanduhr.App.Services;

/// <summary>
/// Builds the dynamic "quick switch account" menu items shared by the three widget
/// entry points: the tray <c>Accounts ▸</c> submenu, the widget right-click
/// <c>Accounts ▸</c> submenu, and the title-bar account chip popup. Each is rebuilt
/// on open (the account registry changes at runtime), so this is a stateless filler
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

        var active = vm.ActiveAccount;
        var accounts = vm.ListAccounts();

        if (accounts.Count == 0)
        {
            items.Add(new MenuItem { Header = "No accounts yet", IsEnabled = false });
        }
        else
        {
            foreach (var label in accounts)
            {
                bool isActive = label == active;
                items.Add(new MenuItem
                {
                    Header = isActive ? $"●  {label}" : $"     {label}",
                    Command = vm.SwitchAccountCommand,
                    CommandParameter = label,
                    // The active account is the no-op target; dim it so the live one reads clearly.
                    IsEnabled = !isActive,
                });
            }
        }

        if (includeAdd || includeManage)
            items.Add(new Separator());

        if (includeAdd)
            items.Add(new MenuItem { Header = "Add account…", Command = vm.SignInCommand });

        if (includeManage)
            items.Add(new MenuItem { Header = "Manage accounts…", Command = vm.OpenSettingsCommand });
    }
}
