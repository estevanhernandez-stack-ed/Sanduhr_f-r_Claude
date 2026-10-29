using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace Sanduhr.App.Controls;

/// <summary>
/// A StackPanel that reaches UI Automation as a named Group. A plain panel has no automation peer, so a
/// screen reader landing on the first radio of a set heard "List, radio button" with nothing to say what
/// the set is for. Put <c>AutomationProperties.Name</c> on this panel and the set is announced with it.
/// Layout is the StackPanel's own.
/// </summary>
public class RadioGroupPanel : StackPanel
{
    protected override AutomationPeer OnCreateAutomationPeer() => new RadioGroupPanelAutomationPeer(this);
}

/// <summary>The Group peer behind <see cref="RadioGroupPanel"/>; the name comes from AutomationProperties.Name.</summary>
public sealed class RadioGroupPanelAutomationPeer : FrameworkElementAutomationPeer
{
    public RadioGroupPanelAutomationPeer(RadioGroupPanel owner) : base(owner) { }

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

    protected override string GetClassNameCore() => nameof(RadioGroupPanel);

    protected override bool IsControlElementCore() => true;
}
