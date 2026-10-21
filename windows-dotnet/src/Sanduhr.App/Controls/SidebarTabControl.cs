using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace Sanduhr.App.Controls;

/// <summary>
/// The Settings window's TabControl, drawn as a sidebar. The sidebar's search box, search results
/// and Quit button live inside the control's template, and WPF's stock
/// <see cref="TabControlAutomationPeer"/> reports only the TabItems and the selected content, so
/// none of them reached the UI Automation tree: Narrator could not land on the search box or Quit,
/// and Quit carried an accessible name nobody could read (Store 10.1.4.4(c)). This subclass hands
/// back a peer that adds the named template parts to the children. Nothing about layout, focus or
/// keyboard behaviour changes; the parts stay where the template puts them.
/// </summary>
public class SidebarTabControl : TabControl
{
    /// <summary>The x:Name of the search box inside the template.</summary>
    public const string SearchBoxPart = "SearchBox";

    /// <summary>The x:Name of the search results list inside the template.</summary>
    public const string SearchResultsPart = "SearchResults";

    /// <summary>The x:Name of the "No setting by that name." live-region text.</summary>
    public const string NoResultsPart = "NoResultsText";

    /// <summary>The x:Name of the Quit Sanduhr button at the sidebar's foot.</summary>
    public const string QuitPart = "QuitButton";

    /// <summary>Parts that sit above the page list, in reading order.</summary>
    public static readonly string[] LeadingParts = { SearchBoxPart, SearchResultsPart, NoResultsPart };

    /// <summary>Parts that sit below the page list.</summary>
    public static readonly string[] TrailingParts = { QuitPart };

    protected override AutomationPeer OnCreateAutomationPeer() => new SidebarTabControlAutomationPeer(this);
}

/// <summary>
/// <see cref="TabControlAutomationPeer"/> plus the sidebar's template parts. The order matches the
/// screen: search box, search results, the page TabItems, Quit, then the selected page's content.
/// </summary>
public class SidebarTabControlAutomationPeer : TabControlAutomationPeer
{
    private readonly SidebarTabControl _owner;

    public SidebarTabControlAutomationPeer(SidebarTabControl owner) : base(owner) => _owner = owner;

    protected override List<AutomationPeer> GetChildrenCore()
    {
        // The peer can be asked before the first layout pass; the parts only exist once the
        // template has been applied.
        _owner.ApplyTemplate();

        var children = base.GetChildrenCore() ?? new List<AutomationPeer>();

        var leading = PartPeers(SidebarTabControl.LeadingParts);
        var trailing = PartPeers(SidebarTabControl.TrailingParts);
        if (leading.Count == 0 && trailing.Count == 0)
            return children;

        // TabItem peers come first in the base list and the selected page's content follows;
        // Quit belongs between the two, where it sits on screen.
        int firstNonItem = children.FindIndex(p => p is not ItemAutomationPeer);
        if (firstNonItem < 0)
            firstNonItem = children.Count;

        var result = new List<AutomationPeer>(children.Count + leading.Count + trailing.Count);
        result.AddRange(leading);
        result.AddRange(children.Take(firstNonItem));
        result.AddRange(trailing);
        result.AddRange(children.Skip(firstNonItem));
        return result;
    }

    private List<AutomationPeer> PartPeers(IEnumerable<string> names)
    {
        var peers = new List<AutomationPeer>();
        foreach (var name in names)
        {
            if (_owner.Template?.FindName(name, _owner) is UIElement part
                && CreatePeerForElement(part) is { } peer)
                peers.Add(peer);
        }
        return peers;
    }
}
