using System.Windows.Automation.Peers;
using Sanduhr.App.Controls;

namespace Sanduhr.App.Tests;

/// <summary>A radio group's container reaches UI Automation as a named Group, so a screen reader announces
/// "Message view, group" before "List, radio button". A plain StackPanel has no peer and says nothing.</summary>
public sealed class RadioGroupPanelTests
{
    [Fact]
    public void The_panel_is_a_named_Group_in_the_control_view() => AppHost.Run(() =>
    {
        var panel = new RadioGroupPanel();
        System.Windows.Automation.AutomationProperties.SetName(panel, "Message view");

        var peer = UIElementAutomationPeer.CreatePeerForElement(panel);

        Assert.NotNull(peer);
        Assert.Equal(AutomationControlType.Group, peer.GetAutomationControlType());
        Assert.Equal("Message view", peer.GetName());
        Assert.True(peer.IsControlElement());
    });
}
