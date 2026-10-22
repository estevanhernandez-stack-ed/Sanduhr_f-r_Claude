using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace Sanduhr.App.Tests;

/// <summary>
/// Settings, Now playing, Looks: two switches, the count, Clear Looks…, the card with Save and Dismiss for Claude's suggestion, and the names a
/// screen reader reads. Over a scratch AppData.
/// </summary>
public sealed class NowPlayingLooksSettingsPageTests : NowPlayingLooksFixture
{
    // ---- Claude's suggestion and the page ----

    private static void Drop(NowPlayingLookService s, string id, params SongLook[] looks)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(s.Handoff.Paths.Request)!);
        File.WriteAllText(s.Handoff.Paths.Request, NowPlayingLookProposal.RequestJson(id, DateTimeOffset.UtcNow, looks, "for tonight").ToJsonString());
        s.Handoff.Check();
    }

    private NowPlayingPageViewModel Page(NowPlayingLookService? s) => new(_store, hub: null, s);

    private JsonObject Result() => (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(_paths.ExternalDir, NowPlayingLookProposal.ResultFile)))!;

    [Fact]
    public void TheSwitchesAreOffByDefaultAndSaveAtOnce()
    {
        using var s = Service();
        var page = Page(s);
        int changed = 0;
        page.Changed = () => changed++;
        Assert.False(page.Style);
        Assert.False(page.LooksDirect);

        page.Style = true;
        Assert.True(_store.LoadNowPlaying().Style);
        page.LooksDirect = true;
        Assert.True(_store.LoadNowPlaying().LooksDirect);
        Assert.Equal(2, changed);
        // The page's other switches keep their values when a looks switch saves.
        page.Bars = false;
        Assert.True(_store.LoadNowPlaying().Style && _store.LoadNowPlaying().LooksDirect);
        Assert.False(_store.LoadNowPlaying().Bars);

        page.Style = false;
        page.LooksDirect = false;
        var json = File.ReadAllText(_paths.SettingsFile);
        Assert.DoesNotContain("looks_direct", json);
        Assert.DoesNotContain("\"style\"", json);
    }

    [Fact]
    public void TheCardShowsClaudesSongsEachInItsLookAndSaveAndDismissAnswerTheCaller()
    {
        using var s = Service();
        var page = Page(s);
        Assert.False(page.HasSuggestion);
        Assert.Equal("No saved looks", page.SavedLooksText);
        Assert.False(page.CanClearLooks);

        Drop(s, "r1", Song("One", style: LookStyle.SmallCaps, mood: "late night"), Song("Two", style: LookStyle.BoldItalic, mood: null));
        Assert.True(page.HasSuggestion);
        Assert.Equal("Claude suggested looks for 2 songs", page.SuggestionTitle);
        Assert.Equal("for tonight", page.SuggestionNote);
        Assert.Equal(2, page.SuggestionSongs.Count);
        var one = page.SuggestionSongs[0];
        Assert.Equal(LookStyles.Text("One · The Weeknd", LookStyle.SmallCaps), one.Display);
        Assert.Equal("One by The Weeknd, small caps, mood late night", one.AccessibleName);
        Assert.Equal("Two by The Weeknd, bold italic", page.SuggestionSongs[1].AccessibleName);
        Assert.Equal(FontWeights.Bold, page.SuggestionSongs[1].FontWeight);
        Assert.Equal(FontStyles.Italic, page.SuggestionSongs[1].FontStyle);
        Assert.Equal("pending_approval", (string?)Result()["result"]!["status"]);

        page.SaveSuggestionCommand.Execute(null);
        Assert.False(page.HasSuggestion);
        Assert.Empty(page.SuggestionSongs);
        Assert.Equal("2 saved looks", page.SavedLooksText);
        Assert.True(page.CanClearLooks);
        Assert.Equal("applied", (string?)Result()["result"]!["status"]);
        Assert.Equal(2, (int)Result()["result"]!["looks_saved"]!);
        Assert.Equal(2, NowPlayingLookStore.Load(_paths.NowPlayingLooksFile).Count);

        Drop(s, "r2", Song("Three"));
        page.DismissSuggestionCommand.Execute(null);
        Assert.False(page.HasSuggestion);
        Assert.Equal("rejected", (string?)Result()["result"]!["status"]);
        Assert.Equal("dismissed by the user", (string?)Result()["result"]!["reasons"]![0]);
        Assert.Equal("2 saved looks", page.SavedLooksText);
    }

    [Fact]
    public void LetClaudeStyleSongsDirectlySavesWithNoCardAndTheCountFollows()
    {
        Style(true, direct: true);
        using var s = Service();
        var page = Page(s);
        Drop(s, "r1", Song("One"));
        Assert.False(page.HasSuggestion);
        Assert.Equal("1 saved look", page.SavedLooksText);
        Assert.Equal("applied", (string?)Result()["result"]!["status"]);
        Assert.True((bool)Result()["result"]!["style_on"]!);
    }

    [Fact]
    public void ClearLooksAsksFirstThenEmptiesTheStoreAndTheDeskGoesBackToSeededLooks()
    {
        Style(true);
        using var s = Service();
        s.Handoff.Save([Song("Blinding Lights", ink: ["#112233"])]);
        var page = Page(s);
        Assert.Equal("1 saved look", page.SavedLooksText);
        int redraws = 0;
        s.Changed += () => redraws++;

        page.ConfirmClear = () => false;
        page.ClearLooksCommand.Execute(null);
        Assert.Equal(1, s.Handoff.Book.Count);   // "Cancel": nothing happens
        Assert.True(File.Exists(_paths.NowPlayingLooksFile));

        page.ConfirmClear = () => true;
        page.ClearLooksCommand.Execute(null);
        Assert.Equal(0, s.Handoff.Book.Count);
        Assert.False(File.Exists(_paths.NowPlayingLooksFile));
        Assert.Equal("No saved looks", page.SavedLooksText);
        Assert.False(page.CanClearLooks);
        Assert.Equal(1, redraws);   // the Desk draws the song in its seeded look again
        Assert.Equal(NowPlayingLooks.DrawStops(NowPlayingLooks.Seeded(SongKey.Of("Blinding Lights", "The Weeknd")!), false), s.DrawFor(Track(), false)!.Stops);
    }

    [Fact]
    public void ThePageWorksWithoutAServiceAndSavedLooksPersistAcrossRestarts()
    {
        var page = Page(null);
        Assert.Equal("No saved looks", page.SavedLooksText);
        page.ClearLooksCommand.Execute(null);   // nothing to do, and nothing breaks
        page.SaveSuggestionCommand.Execute(null);

        using (var first = Service()) first.Handoff.Save([Song("One"), Song("Two")]);
        using var second = Service();
        Assert.Equal("2 saved looks", Page(second).SavedLooksText);
    }

    // ---- the panel a screen reader reads ----

    private static IEnumerable<AutomationPeer> Walk(AutomationPeer peer)
    {
        yield return peer;
        foreach (var child in peer.GetChildren() ?? [])
            foreach (var p in Walk(child))
                yield return p;
    }

    [Fact]
    public void ThePanelNamesEveryControlAndTheSwitchesReachTheStore()
    {
        using var s = Service();
        Sta.Run(() =>
        {
            _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
            Drop(s, "r1", Song("One"));
            var page = Page(s);
            var panel = new NowPlayingLooksPanel { DataContext = page };
            var window = new Window { Content = panel, Width = 640, Height = 600, ShowActivated = false, WindowStyle = WindowStyle.None, ShowInTaskbar = false, Left = -20000 };
            window.Show();
            try
            {
                panel.UpdateLayout();
                var peers = Walk(UIElementAutomationPeer.CreatePeerForElement(panel)).ToList();
                var names = peers.Select(p => p.GetName()).ToList();
                Assert.Contains("Style what's playing", names);
                Assert.Contains("Let Claude style songs directly", names);
                Assert.Contains("Clear Looks…", names);
                Assert.Contains("Save Claude's suggested looks", names);
                Assert.Contains("Dismiss Claude's suggested looks", names);
                Assert.Contains("No saved looks", names);
                Assert.Contains("One by The Weeknd, bold, mood neon", names);

                var boxes = peers.Where(p => p.GetAutomationControlType() == AutomationControlType.CheckBox).ToList();
                Assert.Equal(2, boxes.Count);
                var style = boxes.Single(b => b.GetName() == "Style what's playing");
                var toggle = (IToggleProvider)style.GetPattern(PatternInterface.Toggle)!;
                Assert.Equal(ToggleState.Off, toggle.ToggleState);
                toggle.Toggle();
                Assert.True(_store.LoadNowPlaying().Style);
                toggle.Toggle();
                Assert.False(_store.LoadNowPlaying().Style);

                // Clear Looks… is off with nothing saved.
                var clear = peers.First(p => p.GetAutomationControlType() == AutomationControlType.Button && p.GetName() == "Clear Looks…");
                Assert.False(clear.IsEnabled());
            }
            finally { window.Close(); }
        });
    }
}
