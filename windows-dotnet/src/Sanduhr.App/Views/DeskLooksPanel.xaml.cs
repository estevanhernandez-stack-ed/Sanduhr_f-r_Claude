using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Views;

/// <summary>Settings, Desk, Looks (5.0 spec 5): the ready-made looks. The wallpaper may have changed while the page was away, so the
/// looks are fitted again each time it shows.</summary>
public partial class DeskLooksPanel : UserControl
{
    public DeskLooksPanel()
    {
        InitializeComponent();
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) (DataContext as DeskLooksViewModel)?.Reload();
        };
    }
}

/// <summary>
/// The row of look cards. An ItemsControl has no automation peer of its own, so UI Automation would hoist the buttons and never say
/// what they are a row of; this one is a Group named "Ready-made looks". It is not a list: nothing is selected, and the arrow keys
/// only move focus between the buttons.
/// </summary>
public sealed class DeskLooksRow : ItemsControl
{
    public DeskLooksRow() => AutomationProperties.SetName(this, SettingsNames.LooksRowName);

    protected override AutomationPeer OnCreateAutomationPeer() => new DeskLooksRowAutomationPeer(this);
}

public sealed class DeskLooksRowAutomationPeer(DeskLooksRow owner) : FrameworkElementAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

    protected override string GetClassNameCore() => nameof(DeskLooksRow);

    protected override bool IsControlElementCore() => true;
}
