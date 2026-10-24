using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// The widget menu's "Compact mode" is one name in both states, so its state has to be shown: a check mark for
/// sighted users and a Toggle state for screen readers (WF19). The app-wide leaf menu template draws the mark.
/// </summary>
[Collection("SettingsWindow")]
public sealed class MenuItemCheckMarkTests : AppWindowFixture
{
    private static Visibility MarkOf(MenuItem item)
    {
        item.ApplyTemplate();
        var mark = (FrameworkElement)item.Template.FindName("CheckMark", item);
        Assert.NotNull(mark);
        return mark.Visibility;
    }

    [Fact]
    public void The_leaf_template_draws_a_check_mark_only_while_the_item_is_checked() => AppHost.Run(() =>
    {
        var template = (ControlTemplate)Application.Current.FindResource("Sanduhr.Menu.SubmenuItem");
        var item = new MenuItem { Header = "Any item", Template = template, IsCheckable = true };

        Assert.Equal(Visibility.Collapsed, MarkOf(item));
        item.IsChecked = true;
        Assert.Equal(Visibility.Visible, MarkOf(item));
        item.IsChecked = false;
        Assert.Equal(Visibility.Collapsed, MarkOf(item));
    });

    [Fact]
    public void An_unchecked_item_keeps_its_header_where_it_was() => AppHost.Run(() =>
    {
        // The mark sits over the header's left padding: it adds no column, so checked and
        // unchecked items line up.
        var template = (ControlTemplate)Application.Current.FindResource("Sanduhr.Menu.SubmenuItem");
        var plain = new MenuItem { Header = "Same width", Template = template };
        var checkedItem = new MenuItem { Header = "Same width", Template = template, IsCheckable = true, IsChecked = true };
        plain.Measure(new Size(400, 100));
        checkedItem.Measure(new Size(400, 100));
        Assert.Equal(plain.DesiredSize, checkedItem.DesiredSize);
    });

    [Fact]
    public void The_widgets_Compact_mode_item_follows_IsCompact_for_sight_and_for_UI_Automation() => AppHost.Run(() =>
    {
        var widget = DemoWidget();
        var window = new MainWindow { DataContext = widget };
        OffScreen(window);
        window.Show();
        try
        {
            var menu = (ContextMenu)((FrameworkElement)window.FindName("RootBorder")).ContextMenu;
            Assert.NotNull(menu);
            var compact = menu.Items.OfType<MenuItem>().Single(i => (string)i.Header == SettingsNames.CompactMode);
            Assert.True(compact.IsCheckable);

            foreach (var isCompact in new[] { true, false, true })
            {
                widget.IsCompact = isCompact;
                menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));

                Assert.Equal(isCompact, compact.IsChecked);
                var peer = UIElementAutomationPeer.CreatePeerForElement(compact);
                var toggle = (IToggleProvider?)peer.GetPattern(PatternInterface.Toggle);
                Assert.NotNull(toggle);
                Assert.Equal(isCompact ? ToggleState.On : ToggleState.Off, toggle.ToggleState);
            }
        }
        finally { window.Close(); }
    });
}
