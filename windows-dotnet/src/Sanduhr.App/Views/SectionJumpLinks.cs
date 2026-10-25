using System.Windows;
using System.Windows.Automation;
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
