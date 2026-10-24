using System.IO;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings, Message: List | Text is a two-radio segment (WF48). Checking a radio switches the view;
/// the false WPF writes to the radio that loses its check never switches back. Everything lives in a temp
/// folder: settings, the external folder and messages.txt.</summary>
public sealed class MessageModeSegmentTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-msgmode-" + Guid.NewGuid().ToString("N")[..8]);

    public MessageModeSegmentTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private DeskMessagePageViewModel Page()
    {
        var paths = new Paths(appDataBase: Path.Combine(_root, "appdata"), homeDir: Path.Combine(_root, "home"),
            localAppDataBase: Path.Combine(_root, "local"), externalDir: Path.Combine(_root, "external"));
        var page = new DeskMessagePageViewModel(new SettingsStore(paths), Path.Combine(_root, "messages.txt"),
            () => true, () => { }, ["Segoe UI"]);
        page.Activate();
        return page;
    }

    [Fact]
    public void Checking_Text_then_List_switches_and_says_so() => AppHost.Run(() =>
    {
        var page = Page();
        var raised = new List<string?>();
        page.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        Assert.True(page.IsListMode);

        page.IsTextMode = true;
        Assert.True(page.IsTextMode);
        Assert.False(page.IsListMode);
        Assert.Contains(nameof(DeskMessagePageViewModel.IsListMode), raised);
        Assert.Contains(nameof(DeskMessagePageViewModel.IsTextMode), raised);

        page.IsListMode = true;
        Assert.True(page.IsListMode);
    });

    [Fact]
    public void Unchecking_a_mode_does_not_switch_back() => AppHost.Run(() =>
    {
        var page = Page();
        page.IsTextMode = true;
        page.IsListMode = false;   // what WPF writes to the List radio as Text takes the check
        Assert.True(page.IsTextMode);
        page.IsTextMode = false;
        Assert.True(page.IsTextMode);
    });

    [Fact]
    public void Text_edits_carry_over_to_the_list() => AppHost.Run(() =>
    {
        var page = Page();
        page.IsTextMode = true;
        page.EditorText = "A line typed in the Text view";
        page.IsListMode = true;
        // The list shows the typed line as a row...
        Assert.Contains(page.Rows, r => r.IsStyled ? r.Text == "A line typed in the Text view" : r.RawText.Contains("A line typed in the Text view"));
        page.IsTextMode = true;
        // ...and the text view still has it after the round trip.
        Assert.Contains("A line typed in the Text view", page.EditorText);
    });
}
