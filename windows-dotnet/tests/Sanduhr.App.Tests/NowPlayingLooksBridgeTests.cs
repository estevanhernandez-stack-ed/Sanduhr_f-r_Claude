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

/// <summary>The bridge with the now-playing mod: the file Sanduhr keeps for it, and what it needs to be there.</summary>
public sealed class NowPlayingLooksBridgeTests : NowPlayingLooksFixture
{
    private NowPlayingLookService Playing(NowPlayingInfo? playing) =>
        new(_paths, _store, hub: null, context: null, now: () => Now) { TrackSource = () => playing };

    private string CurrentFile => Path.Combine(_paths.ExternalDir, NowPlayingLookProposal.CurrentFile);

    // ---- the file for the mod ----

    [Fact]
    public void TheCurrentLookFileIsWrittenOnlyWhileTheIntegrationIsInstalledStyleIsOnAndASongPlays()
    {
        var playing = Track();
        NowPlayingInfo? shown = playing;
        using var s = new NowPlayingLookService(_paths, _store, hub: null, context: null, now: () => Now) { TrackSource = () => shown };
        string key = SongKey.Of("Blinding Lights", "The Weeknd")!;

        // Style on, no integration: nothing is written.
        Style(true);
        s.Sync();
        Assert.False(File.Exists(CurrentFile));

        // The integration installed too: the seeded look, with its key.
        _store.SaveMcpEnabled(true);
        s.Sync();
        var seeded = (JsonObject)JsonNode.Parse(File.ReadAllText(CurrentFile))!;
        Assert.Equal(key, (string?)seeded["key"]);
        Assert.Equal("seeded", (string?)seeded["source"]);
        Assert.Equal(1, (int)seeded["schema_version"]!);
        Assert.Equal(NowPlayingLooks.Seeded(key).Ink, seeded["look"]!["ink"]!.AsArray().Select(n => (string)n!));
        string text = File.ReadAllText(CurrentFile);
        Assert.DoesNotContain("Blinding", text);
        Assert.DoesNotContain("Weeknd", text);

        // Saving a look for the song changes the file to that look, as saved.
        s.Handoff.Save([Song("Blinding Lights", ink: ["#112233", "#445566"], style: LookStyle.SmallCaps)]);
        var saved = (JsonObject)JsonNode.Parse(File.ReadAllText(CurrentFile))!;
        Assert.Equal("saved", (string?)saved["source"]);
        Assert.Equal("smallcaps", (string?)saved["look"]!["style"]);
        Assert.Equal(["#112233", "#445566"], saved["look"]!["ink"]!.AsArray().Select(n => (string)n!));

        // A different song: its own key.
        shown = Track("Starboy");
        s.Sync();
        Assert.Equal(SongKey.Of("Starboy", "The Weeknd"), (string?)((JsonObject)JsonNode.Parse(File.ReadAllText(CurrentFile))!)["key"]);

        // Nothing plays: gone. Plays again: back. Style off: gone. Integration removed: gone.
        shown = null;
        s.Sync();
        Assert.False(File.Exists(CurrentFile));
        shown = playing;
        s.Sync();
        Assert.True(File.Exists(CurrentFile));
        Style(false);
        s.Sync();
        Assert.False(File.Exists(CurrentFile));
        Style(true);
        s.Sync();
        Assert.True(File.Exists(CurrentFile));
        _store.SaveMcpEnabled(false);
        s.Sync();
        Assert.False(File.Exists(CurrentFile));
    }

    [Fact]
    public void AnAppTheUserTurnedOffGetsNoFileAndDisposingTheServiceRemovesIt()
    {
        Style(true);
        _store.SaveMcpEnabled(true);
        _store.SaveNowPlaying(_store.LoadNowPlaying() with { Excluded = ["Spotify.exe"] });
        var s = Playing(Track());
        s.Sync();
        Assert.False(File.Exists(CurrentFile));

        _store.SaveNowPlaying(_store.LoadNowPlaying() with { Excluded = [] });
        s.Sync();
        Assert.True(File.Exists(CurrentFile));
        s.Dispose();
        Assert.False(File.Exists(CurrentFile));
    }
}
