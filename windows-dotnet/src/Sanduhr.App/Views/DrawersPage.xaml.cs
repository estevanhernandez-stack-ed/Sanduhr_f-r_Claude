using System.Windows;
using System.Windows.Controls;
using Sanduhr.Core;

namespace Sanduhr.App.Views;

/// <summary>Settings, Drawers (5.0 drawers spec). Gives the view model its two dialogs: the turn-off question and the folder picker.</summary>
public partial class DrawersPage : UserControl
{
    public DrawersPage()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not ViewModels.DrawersSettingsViewModel vm) return;
            vm.Confirm = q => ThemedDialog.ShowConfirm(Window.GetWindow(this), SettingsNames.DrawersTurnOffTitle, q, SettingsNames.DrawersPutThemBack, SettingsNames.DrawersCancel);
            vm.PickFolder = () =>
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog { Title = SettingsNames.DrawersFolder };
                return dialog.ShowDialog(Window.GetWindow(this)) == true ? dialog.FolderName : null;   // streamer: notname - a folder the user picked, used for IO
            };
            vm.Refresh();
        };
    }
}
