using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings v2, slice 7: the Desk's names (spec, Reach outside Settings: watcher cards, the weather name; decision 3: the Claude line).</summary>
[Collection("SettingsWindow")]
public sealed class StreamerDeskTests : AppWindowFixture
{
    /// <summary>Decision 3: with two accounts the Claude line names the active one, in every style; while on, by its codename.</summary>
    [Fact]
    public void The_claude_line_names_the_account_by_codename() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Qxalpha", "Qxbeta");
        try
        {
            Assert.Equal("Qxalpha", widget.CurrentDeskUsage.Account);
            widget.Streamer.SetOn(true);
            Assert.Equal(StreamerMode.Mask.Name(CodenameCategory.Account, "Qxalpha"), widget.CurrentDeskUsage.Account);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void Moving_the_switch_tells_the_desk_to_read_its_usage_again() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Qxalpha", "Qxbeta");
        try
        {
            int heard = 0;
            widget.DeskUsageChanged += () => heard++;
            widget.Streamer.SetOn(true);
            Assert.True(heard >= 1);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void The_desk_frame_masks_runner_cards_and_the_weather_name()
    {
        StreamerMode.Use(new StreamerMask(true, CodenameBook.New(), Profile));
        var runner = new Watcher
        {
            Id = "r:k:build", Source = WatcherSource.Runner, Title = "qxrunner · build", Short = "build", Runner = "qxrunner", RunnerKey = "k", Job = "build",
            State = WatcherState.Running, Started = DateTimeOffset.UnixEpoch, Touched = DateTimeOffset.UnixEpoch,
        };
        var place = WeatherPlace.Create("Qxcity", "Texas", "United States", 32.75, -97.33);
        var (watchers, nickname) = DeskController.StreamerFrameParts([runner], place, "");
        Assert.DoesNotContain("qx", watchers[0].Title, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(" · build", watchers[0].Title);
        Assert.Equal(StreamerMode.Mask.Name(CodenameCategory.City, place.Label), nickname);
        Assert.Equal("Home", DeskController.StreamerFrameParts([runner], place, "Home").Nickname);
    }

    /// <summary>Preflight P4: Claude's own cards keep their words, but a path in a title or note is masked.</summary>
    [Fact]
    public void The_desk_frame_masks_a_path_in_an_agent_card()
    {
        StreamerMode.Use(new StreamerMask(true, CodenameBook.New(), Profile));
        var card = new Watcher { Id = "a:1", Source = WatcherSource.Automatic, Title = @"build in C:\Users\qxuser\proj", Short = "build", Note = @"see C:\Users\qxuser\proj\out.log", State = WatcherState.Running, Started = DateTimeOffset.UnixEpoch, Touched = DateTimeOffset.UnixEpoch };
        var (watchers, _) = DeskController.StreamerFrameParts([card], null, "");
        Assert.DoesNotContain("qxuser", watchers[0].Title);
        Assert.DoesNotContain("qxuser", watchers[0].Note);
    }
}
