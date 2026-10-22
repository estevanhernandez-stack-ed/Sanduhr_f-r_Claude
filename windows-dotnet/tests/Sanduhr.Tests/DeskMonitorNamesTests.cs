using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>How a person is told which monitor is which, and the Monitor dropdown's choices (spec, Desk pieces per monitor).</summary>
public sealed class DeskMonitorNamesTests
{
    private static DeskMonitorInfo M(string id, int left, int top, int w, int h, bool primary = false) =>
        new(id, "", w, h, primary, left, top);

    private static string[] NamesOf(params DeskMonitorInfo[] monitors) =>
        [.. DeskMonitorNames.Named(monitors).Select(m => m.Name)];

    [Fact]
    public void NebsTwoMonitorsAreNamedBySizeAndPosition()
    {
        var named = DeskMonitorNames.Named([M("a", 0, 0, 3440, 1440, primary: true), M("b", 3440, 0, 1920, 1080)]);
        Assert.Equal("3440×1440, main", named[0].Name);
        Assert.Equal("1920×1080, right of main", named[1].Name);
        Assert.Equal(["a", "b"], named.Select(m => m.Id));
        Assert.Equal(["3440×1440, main", "1920×1080, right of main"], NamesOf(M("a", 0, 0, 3440, 1440, true), M("b", 3440, 0, 1920, 1080)));
    }

    [Fact]
    public void AMonitorIsNamedByWhichSideOfTheMainOneItSitsOn()
    {
        var main = M("m", 0, 0, 1920, 1080, true);
        Assert.Equal("1280×720, left of main", NamesOf(main, M("l", -1280, 100, 1280, 720))[1]);
        Assert.Equal("1920×1080, below main", NamesOf(main, M("d", 0, 1080, 1920, 1080))[1]);
        Assert.Equal("1920×1080, above main", NamesOf(main, M("u", 0, -1080, 1920, 1080))[1]);
        Assert.Equal("1920×1080, right of main", NamesOf(main, M("r", 1920, -300, 1920, 1080))[1]);
    }

    [Fact]
    public void AMonitorThatOverlapsMainIsNamedForItsPosition()
    {
        Assert.Equal("1024×768, at 100,50", NamesOf(M("m", 0, 0, 1920, 1080, true), M("o", 100, 50, 1024, 768))[1]);
    }

    [Fact]
    public void TheListOrderIsKeptAndNamesThatWouldRepeatAreNumberedInScreenOrder()
    {
        var named = DeskMonitorNames.Named([
            M("far", 3840, 0, 1920, 1080),
            M("m", 0, 0, 1920, 1080, true),
            M("near", 1920, 0, 1920, 1080),
        ]);
        Assert.Equal(["far", "m", "near"], named.Select(m => m.Id));
        Assert.Equal("1920×1080, right of main (2)", named[0].Name);
        Assert.Equal("1920×1080, main", named[1].Name);
        Assert.Equal("1920×1080, right of main", named[2].Name);
    }

    [Fact]
    public void WithNoPrimaryFlagTheOneAtTheOriginIsMain()
    {
        Assert.Equal(["800×600, right of main", "1024×768, main"],
            NamesOf(M("b", 1024, 0, 800, 600), M("a", 0, 0, 1024, 768)));
        Assert.Equal(["640×480, main"], NamesOf(M("x", 5, 5, 640, 480)));
        Assert.Empty(DeskMonitorNames.Named([]));
    }

    [Fact]
    public void TheDropdownOffersAllMainThenEachMonitorInScreenOrder()
    {
        var connected = DeskMonitorNames.Named([M("b", 3440, 0, 1920, 1080), M("a", 0, 0, 3440, 1440, true)]);
        var choices = DeskMonitorNames.Choices(connected, null);
        Assert.Equal(["All monitors", "Main monitor", "3440×1440, main", "1920×1080, right of main"], choices.Select(c => c.Name));
        Assert.Equal([null, "main", "a", "b"], choices.Select(c => c.Value));
        Assert.Equal("All monitors", DeskMonitorNames.Select(choices, null).Name);
        Assert.Equal("Main monitor", DeskMonitorNames.Select(choices, "main").Name);
        Assert.Equal("1920×1080, right of main", DeskMonitorNames.Select(choices, "B").Name);
    }

    [Fact]
    public void AMonitorThatWentKeepsAnEntryOfItsOwnSoWhatIsStoredShows()
    {
        var connected = DeskMonitorNames.Named([M("a", 0, 0, 1920, 1080, true)]);
        var choices = DeskMonitorNames.Choices(connected, "gone-id");
        Assert.Equal(DeskMonitorNames.NotConnected, choices[^1].Name);
        Assert.Equal("gone-id", choices[^1].Value);
        Assert.Same(choices[^1], DeskMonitorNames.Select(choices, "gone-id"));
        // A stored "main" or "all" never adds one.
        Assert.Equal(3, DeskMonitorNames.Choices(connected, "main").Count);
        Assert.Equal(3, DeskMonitorNames.Choices(connected, null).Count);
        // Nothing connected (Windows would not say): still All and Main, and the stored id is kept visible.
        Assert.Equal(3, DeskMonitorNames.Choices([], "x").Count);
    }

    [Fact]
    public void NameOfFindsAConnectedMonitorOrNothing()
    {
        var connected = DeskMonitorNames.Named([M("a", 0, 0, 1920, 1080, true), M("b", 1920, 0, 1280, 1024)]);
        Assert.Equal("1280×1024, right of main", DeskMonitorNames.NameOf(connected, "B"));
        Assert.Null(DeskMonitorNames.NameOf(connected, "zzz"));
        Assert.Equal("3440×1440", DeskMonitorNames.Size(3440, 1440));
    }
}
