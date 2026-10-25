using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using Sanduhr.Core;

namespace Sanduhr.App.Views;

/// <summary>
/// A page's row of jump links, "Pieces · Behavior · Designs · Advanced" (spec, Keyboard and screen reader: pages with four or more
/// sections, WF22). Each is a <see cref="SettingsLinkTo"/> link to its section, so the window scrolls there, opens a collapsed
/// section and puts the keyboard on it. The links come from <see cref="SettingsIndex.JumpLinks"/>.
/// </summary>
public sealed class SectionJumpLinks : WrapPanel
{
    public static readonly DependencyProperty PageProperty = DependencyProperty.Register(
        nameof(Page), typeof(SettingsPage), typeof(SectionJumpLinks),
        new PropertyMetadata(SettingsPage.General, (d, _) => ((SectionJumpLinks)d).Build()));

    public SettingsPage Page
    {
        get => (SettingsPage)GetValue(PageProperty);
        set => SetValue(PageProperty, value);
    }

    public SectionJumpLinks()
    {
        Margin = new Thickness(0, 0, 0, 12);
        AutomationProperties.SetName(this, SettingsNames.JumpLinksName);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new SectionJumpLinksAutomationPeer(this);

    private void Build()
    {
        Children.Clear();
        var links = SettingsIndex.JumpLinks(Page);
        for (int i = 0; i < links.Count; i++)
        {
            if (i > 0)
            {
                var dot = new TextBlock { Text = "·", FontSize = 11, Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
                dot.SetResourceReference(TextBlock.ForegroundProperty, "Sanduhr.Brush.TextDim");
                Children.Add(dot);
            }
            var link = new Button { Content = links[i].Title };
            link.SetResourceReference(StyleProperty, "SettingsLinkButton");
            SettingsLinkTo.SetLink(link, SettingsIndex.PageId(Page) + "#" + links[i].Anchor);
            AutomationProperties.SetName(link, links[i].Title);
            Children.Add(link);
        }
    }
}

/// <summary>
/// The Group peer behind <see cref="SectionJumpLinks"/>. A plain panel has no peer, so UI Automation would hoist the buttons and
/// never announce the row's name; and the "·" separators are code-built TextBlocks, which are control elements unless templated.
/// The peer names the row and exposes the link buttons alone.
/// </summary>
public sealed class SectionJumpLinksAutomationPeer : FrameworkElementAutomationPeer
{
    public SectionJumpLinksAutomationPeer(SectionJumpLinks owner) : base(owner) { }

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

    protected override string GetClassNameCore() => nameof(SectionJumpLinks);

    protected override bool IsControlElementCore() => true;

    protected override List<AutomationPeer>? GetChildrenCore() =>
        base.GetChildrenCore()?.Where(p => p is UIElementAutomationPeer { Owner: Button }).ToList();
}
