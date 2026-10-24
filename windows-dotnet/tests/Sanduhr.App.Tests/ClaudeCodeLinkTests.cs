using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Reopening Settings, Claude Code must not unlink a folder from its account. Reload used to empty the account list
/// under the folder rows' bound combos, and WPF answered by writing a null selection into every row, which unlinked
/// the folder and showed "Couldn't save that link.".
/// </summary>
[Collection("SettingsWindow")]
public sealed class ClaudeCodeLinkTests : AppWindowFixture
{
    private const string SaveFailed = "Couldn't save that link.";

    private string Linked(WidgetViewModel widget)
    {
        var folder = Path.Combine(Profile, ".claude");
        widget.SetAccountFolder("Work", folder);
        return folder;
    }

    /// <summary>What settings.json holds for account_folders, read fresh from the scratch profile.</summary>
    private Dictionary<string, string> Saved() => new SettingsStore(ScratchPaths()).LoadAccountFolders();

    private static IEnumerable<ComboBox> LinkCombos(System.Windows.DependencyObject node)
    {
        if (node is ComboBox { } combo && System.Windows.Automation.AutomationProperties.GetName(combo).StartsWith("Linked account"))
            yield return combo;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            foreach (var c in LinkCombos(VisualTreeHelper.GetChild(node, i)))
                yield return c;
    }

    [Fact]
    public void ReopeningTheClaudeCodePageKeepsTheLinkThroughTheRealCombos() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Work", "Personal");
        var folder = Linked(widget);
        var before = Saved();
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        var window = new SettingsWindow(svm);
        OffScreen(window);
        window.Show();
        try
        {
            var tabs = (TabControl)window.FindName("Tabs");
            var general = tabs.Items.OfType<TabItem>().First(t => (string)t.Tag != "ClaudeCode");
            var cc = tabs.Items.OfType<TabItem>().First(t => (string)t.Tag == "ClaudeCode");
            for (int open = 0; open < 3; open++)
            {
                tabs.SelectedItem = general;
                NamesOf(window);
                tabs.SelectedItem = cc; // Settings reloads the page every time it is opened
                NamesOf(window);
            }

            Assert.Equal(folder, widget.AccountFolder("Work"));
            Assert.Equal(before, Saved());
            Assert.NotEqual(SaveFailed, svm.ClaudeCodePage.StatusText);
            var combo = LinkCombos(window).First(c => System.Windows.Automation.AutomationProperties.GetName(c) == @"Linked account for ~\.claude");
            Assert.Equal("Work", combo.SelectedItem);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void AnAccountRenamedOrRemovedWhileThePageIsOpenLeavesTheOtherLinksIntact() => AppHost.Run(() =>
    {
        var other = Path.Combine(Profile, ".claude-personal");
        Directory.CreateDirectory(Path.Combine(other, "projects"));
        var (widget, accounts) = ScratchWidget("Work", "Personal", "Extra", "Gone");
        var work = Linked(widget);
        widget.SetAccountFolder("Personal", other);
        var before = Saved();
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        var window = new SettingsWindow(svm);
        OffScreen(window);
        window.Show();
        try
        {
            var tabs = (TabControl)window.FindName("Tabs");
            var general = tabs.Items.OfType<TabItem>().First(t => (string)t.Tag != "ClaudeCode");
            var cc = tabs.Items.OfType<TabItem>().First(t => (string)t.Tag == "ClaudeCode");
            tabs.SelectedItem = cc;
            NamesOf(window);

            tabs.SelectedItem = general;
            accounts.RenameAccount("Extra", "Spare");
            accounts.RemoveAccount("Gone");
            tabs.SelectedItem = cc; // Settings reloads the page every time it is opened
            NamesOf(window);

            var page = svm.ClaudeCodePage;
            Assert.Equal(before, Saved());
            Assert.Equal(work, widget.AccountFolder("Work"));
            Assert.Equal(other, widget.AccountFolder("Personal"));
            Assert.NotEqual(SaveFailed, page.StatusText);
            Assert.Equal(["None", "Work", "Personal", "Spare"], page.AccountOptions);
            Assert.Equal("Work", page.Folders.Single(f => f.Path == work).LinkedAccount);
            Assert.Equal("Personal", page.Folders.Single(f => f.Path == other).LinkedAccount);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void ReloadingTwiceKeepsTheLink() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Work", "Personal");
        var folder = Linked(widget);
        var before = Saved();
        var page = new ClaudeCodeViewModel(widget);

        page.Reload();
        page.Reload();

        Assert.Equal(folder, widget.AccountFolder("Work"));
        Assert.Equal(before, Saved());
        Assert.NotEqual(SaveFailed, page.StatusText);
        Assert.Equal("Work", page.Folders.Single(f => f.Path == folder).LinkedAccount);
    });

    [Fact]
    public void ANullSelectionLeavesTheLinksAsTheyWere() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Work", "Personal");
        var folder = Linked(widget);
        var before = Saved();
        var page = new ClaudeCodeViewModel(widget);
        var row = page.Folders.Single(f => f.Path == folder);

        row.LinkedAccount = null!;
        row.LinkedAccount = "";
        row.LinkedAccount = "   ";

        Assert.Equal(folder, widget.AccountFolder("Work"));
        Assert.Equal(before, Saved());
        Assert.NotEqual(SaveFailed, page.StatusText);
    });

    [Fact]
    public void AnUnknownAccountIsNeverTreatedAsUnlinkAll() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Work", "Personal");
        var folder = Linked(widget);
        var page = new ClaudeCodeViewModel(widget);
        var row = page.Folders.Single(f => f.Path == folder);

        page.OnLinkChanged(row, null!);

        Assert.Equal(folder, widget.AccountFolder("Work"));
        Assert.NotEqual(SaveFailed, page.StatusText);
    });

    [Fact]
    public void PickingNoneStillUnlinksTheFolder() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Work", "Personal");
        var folder = Linked(widget);
        var page = new ClaudeCodeViewModel(widget);

        page.Folders.Single(f => f.Path == folder).LinkedAccount = ClaudeCodeViewModel.NoAccount;

        Assert.Null(widget.AccountFolder("Work"));
        Assert.Empty(Saved());
    });
}
