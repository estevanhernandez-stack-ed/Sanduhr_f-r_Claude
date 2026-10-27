using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace Sanduhr.App.Controls;

/// <summary>
/// Text that only decorates a field whose own control already carries the same words (a greyed default shown under an empty box, or a
/// unit sign beside a box whose name already says the unit). A
/// plain TextBlock outside a template is a UIA control element, so Narrator would read the words twice; this one is neither a control
/// nor a content element.
/// </summary>
public sealed class DecorativeText : TextBlock
{
    protected override AutomationPeer OnCreateAutomationPeer() => new DecorativeTextAutomationPeer(this);
}

public sealed class DecorativeTextAutomationPeer(DecorativeText owner) : TextBlockAutomationPeer(owner)
{
    protected override bool IsControlElementCore() => false;

    protected override bool IsContentElementCore() => false;
}
