using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using Sanduhr.App.Desk;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// The live review's floating bar and window (spec, the agent-designed Desk, the live review): the accessible names of Apply, Dismiss
/// and the lists, Apply disabled while a font is missing, the font links (https only, clickable), and the keys: Escape dismisses,
/// Enter applies. Real controls on an STA thread; nothing is shown.
/// </summary>
public sealed class DeskReviewBarTests
{
    private static DeskReviewModel Model(bool fontMissing = false, string? link = "https://fonts.example/zed", bool deskOff = false, bool withNote = true) =>
        new("Dusk", withNote ? "Warm ink for the sunset wallpaper." : null,
            ["Clock and date: moved from bottom left to top right", "Plate behind the text: on"],
            fontMissing ? [new DeskReviewFont("Zed Sans", link)] : [],
            deskOff, ["theme.accent: reads low against the glass"]);

    private static void OnSta(Action body) => Sta.Run(() =>
    {
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack;   // registers the pack scheme the compiled XAML loads through
        body();
    });

    private static DeskReviewBar Build(DeskReviewModel model)
    {
        var bar = new DeskReviewBar();
        bar.SetModel(model);
        bar.Measure(new Size(600, 900));
        bar.Arrange(new Rect(0, 0, 600, 900));
        bar.UpdateLayout();
        return bar;
    }

    private static IEnumerable<AutomationPeer> Walk(AutomationPeer peer)
    {
        yield return peer;
        foreach (var child in peer.GetChildren() ?? [])
            foreach (var p in Walk(child)) yield return p;
    }

    private static List<AutomationPeer> Peers(DeskReviewBar bar) =>
        [.. Walk(UIElementAutomationPeer.CreatePeerForElement(bar))];

    [Fact]
    public void ApplyAndDismissAreNamedForNarrator()
    {
        OnSta(() =>
        {
            var bar = Build(Model());
            var peers = Peers(bar);
            var apply = peers.Single(p => p.GetClassName() == "Button" && p.GetName() == "Apply");
            var dismiss = peers.Single(p => p.GetClassName() == "Button" && p.GetName() == "Dismiss");
            Assert.True(apply.IsEnabled());
            Assert.True(dismiss.IsEnabled());
            Assert.True(bar.Apply.Focusable && bar.Dismiss.Focusable);   // reachable by keyboard
            Assert.Contains("Escape", AutomationProperties.GetHelpText(bar.Dismiss));
            Assert.Equal("Apply this design to your Desk and widget", AutomationProperties.GetHelpText(bar.Apply));
        });
    }

    [Fact]
    public void TheBarNamesItselfItsTitleItsNoteAndItsLists()
    {
        OnSta(() =>
        {
            var bar = Build(Model(fontMissing: true));
            var names = Peers(bar).Select(p => p.GetName()).ToList();
            Assert.Contains(DeskReviewCopy.BarName, names);
            Assert.Contains("Dusk", names);
            Assert.Contains("Warm ink for the sunset wallpaper.", names);
            Assert.Contains(DeskReviewCopy.ChangesName, names);
            Assert.Contains("Clock and date: moved from bottom left to top right", names);
            Assert.Contains("Plate behind the text: on", names);
            Assert.Contains("Zed Sans is not installed. Get it from https://fonts.example/zed, then install it.", names);
            Assert.Contains("theme.accent: reads low against the glass", names);
            Assert.Contains(DeskReviewCopy.KeysLineWaiting, names);
        });
    }

    [Fact]
    public void ApplyIsDisabledWhileAFontIsMissingAndPressingItDoesNothing()
    {
        OnSta(() =>
        {
            var bar = Build(Model(fontMissing: true));
            int applied = 0, dismissed = 0;
            bar.ApplyRequested += () => applied++;
            bar.DismissRequested += () => dismissed++;

            Assert.False(bar.Apply.IsEnabled);
            var peers = Peers(bar);
            Assert.False(peers.Single(p => p.GetClassName() == "Button" && p.GetName() == "Apply").IsEnabled());
            Assert.Equal("Install the missing font to apply this design", AutomationProperties.GetHelpText(bar.Apply));

            bar.Apply.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));   // even a programmatic click
            Assert.Equal(0, applied);

            bar.Dismiss.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.Equal(1, dismissed);
        });
    }

    [Fact]
    public void ApplyWakesUpWhenTheFontArrives()
    {
        OnSta(() =>
        {
            var bar = Build(Model(fontMissing: true));
            int applied = 0;
            bar.ApplyRequested += () => applied++;
            Assert.False(bar.Apply.IsEnabled);

            bar.SetModel(Model(fontMissing: false));
            Assert.True(bar.Apply.IsEnabled);
            Assert.Equal(System.Windows.Visibility.Collapsed, FontsPanel(bar).Visibility);
            bar.Apply.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.Equal(1, applied);
        });
    }

    private static StackPanel FontsPanel(DeskReviewBar bar) =>
        (StackPanel)bar.FindName("Fonts");

    private static IEnumerable<Hyperlink> Links(DeskReviewBar bar) =>
        FontsPanel(bar).Children.OfType<TextBlock>().SelectMany(t => t.Inlines.OfType<Hyperlink>());

    [Fact]
    public void AFontLinkIsClickableAndOpensOnlyThroughTheSeam()
    {
        OnSta(() =>
        {
            var bar = Build(Model(fontMissing: true));
            var opened = new List<string>();
            bar.OpenLink = opened.Add;

            var link = Assert.Single(Links(bar));
            Assert.Equal("Open the download page for Zed Sans", AutomationProperties.GetName(link));
            Assert.Equal(System.Windows.Visibility.Visible, FontsPanel(bar).Visibility);
            link.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));
            Assert.Equal(["https://fonts.example/zed"], opened);
            // The text names the font and says Sanduhr downloads nothing.
            Assert.Contains("Zed Sans is not installed", AutomationProperties.GetName(FontsPanel(bar).Children.OfType<TextBlock>().Single()));
            Assert.Contains("downloads nothing", link.ToolTip.ToString());
        });
    }

    [Fact]
    public void AFontWithoutASafeLinkShowsNoLink()
    {
        OnSta(() =>
        {
            var bar = Build(Model(fontMissing: true, link: null));
            Assert.Empty(Links(bar));
            Assert.Contains("Zed Sans is not installed", AutomationProperties.GetName(FontsPanel(bar).Children.OfType<TextBlock>().Single()));
        });
    }

    [Fact]
    public void TheNoteAndTheDeskOffLineShowOnlyWhenThereIsSomethingToSay()
    {
        OnSta(() =>
        {
            var plain = Build(Model(withNote: false));
            Assert.Equal(System.Windows.Visibility.Collapsed, ((TextBlock)plain.FindName("Note")).Visibility);
            Assert.Equal(System.Windows.Visibility.Collapsed, ((TextBlock)plain.FindName("DeskOff")).Visibility);

            var full = Build(Model(deskOff: true));
            Assert.Equal(System.Windows.Visibility.Visible, ((TextBlock)full.FindName("Note")).Visibility);
            var off = (TextBlock)full.FindName("DeskOff");
            Assert.Equal(System.Windows.Visibility.Visible, off.Visibility);
            Assert.Equal(DeskReviewCopy.DeskOffNote, off.Text);
        });
    }

    // ---- the window's keys ----

    [Fact]
    public void EscapeDismissesAndEnterApplies()
    {
        OnSta(() =>
        {
            var window = new DeskReviewWindow();
            window.SetModel(Model());
            int applied = 0, dismissed = 0;
            window.ApplyRequested += () => applied++;
            window.DismissRequested += () => dismissed++;

            Assert.True(window.HandleKey(Key.Escape, null));
            Assert.Equal(1, dismissed);
            Assert.True(window.HandleKey(Key.Enter, window.ReviewBar.Apply));
            Assert.Equal(1, applied);
            Assert.True(window.HandleKey(Key.Return, null));
            Assert.Equal(2, applied);
            Assert.False(window.HandleKey(Key.A, null));
            Assert.Equal((2, 1), (applied, dismissed));
            Assert.Equal(DeskReviewCopy.BarName, AutomationProperties.GetName(window));
        });
    }

    [Fact]
    public void EnterDoesNothingWhileAFontIsMissingAndNeverDismisses()
    {
        OnSta(() =>
        {
            var window = new DeskReviewWindow();
            window.SetModel(Model(fontMissing: true));
            int applied = 0, dismissed = 0;
            window.ApplyRequested += () => applied++;
            window.DismissRequested += () => dismissed++;

            Assert.True(window.HandleKey(Key.Enter, null));
            Assert.Equal((0, 0), (applied, dismissed));
            window.HandleKey(Key.Escape, null);
            Assert.Equal(1, dismissed);
        });
    }

    [Fact]
    public void EnterOnAFocusedEditButtonIsLeftToThatControl()
    {
        OnSta(() =>
        {
            var window = new DeskReviewWindow();
            window.SetModel(Model() with { CanEdit = true });
            int applied = 0;
            window.ApplyRequested += () => applied++;
            Assert.False(window.HandleKey(Key.Enter, window.ReviewBar.Edit));
            Assert.Equal(0, applied);
        });
    }

    [Fact]
    public void EnterOnTheDismissButtonOrALinkIsLeftToThatControl()
    {
        OnSta(() =>
        {
            var window = new DeskReviewWindow();
            window.SetModel(Model(fontMissing: true));
            int applied = 0;
            window.ApplyRequested += () => applied++;

            Assert.False(window.HandleKey(Key.Enter, window.ReviewBar.Dismiss));   // the button's own click dismisses
            Assert.False(window.HandleKey(Key.Enter, new Hyperlink()));            // the link's own click opens
            window.SetModel(Model());
            Assert.Equal(0, applied);
        });
    }

    [Fact]
    public void TheBarHidesTheHeaderAndListWhenTheModelHasNoChangesTitle()
    {
        OnSta(() =>
        {
            var bar = Build(DrawerReview.Result("1 moved.", canUndo: true));
            var header = (TextBlock)bar.FindName("ChangesHeader");
            Assert.Equal(Visibility.Collapsed, header.Visibility);
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)bar.FindName("Changes")).Visibility);
            Assert.Equal("Enter or Escape closes.", ((TextBlock)bar.FindName("Keys")).Text);
            bar.SetModel(Model());   // a design review brings them back
            Assert.Equal(Visibility.Visible, header.Visibility);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)bar.FindName("Changes")).Visibility);
        });
    }

    [Fact]
    public void EditShowsOnlyWhenTheModelAllowsItAndRaisesEditRequested()
    {
        OnSta(() =>
        {
            var bar = Build(Model());
            Assert.Equal(Visibility.Collapsed, bar.Edit.Visibility);
            bar.SetModel(Model() with { CanEdit = true });
            Assert.Equal(Visibility.Visible, bar.Edit.Visibility);
            Assert.Equal("Edit", AutomationProperties.GetName(bar.Edit));
            int edits = 0;
            bar.EditRequested += () => edits++;
            bar.Edit.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.Equal(1, edits);
        });
    }

    [Fact]
    public void ApplyAndDismissTakeTheModelsLabels()
    {
        OnSta(() =>
        {
            var bar = Build(DrawerReview.Result("1 moved.", canUndo: true));
            Assert.Equal("Undo", bar.Apply.Content);
            Assert.Equal("Done", bar.Dismiss.Content);
            Assert.Equal("Undo", AutomationProperties.GetName(bar.Apply));
            Assert.Equal("Done", AutomationProperties.GetName(bar.Dismiss));
        });
    }
}
