using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class SetupDeskTests
{
    [Fact]
    public void Desktop_on_keeps_the_layout_as_it_is()
    {
        Assert.Equal("clock:bl:1 claude:bl:1", SetupDesk.Layout("clock:bl:1 claude:bl:1", desktop: true));
    }

    [Fact]
    public void Desktop_off_keeps_only_drawer_words_and_the_saved_layout_is_not_touched()
    {
        Assert.Equal("drawer.images:tr", SetupDesk.Layout("clock:bl:1 drawer.images:tr:1 weather:tr:1", desktop: false));
        Assert.Equal("", SetupDesk.Layout("clock:bl:1 weather:tr:1", desktop: false));
        Assert.False(SetupDesk.Shows("clock", desktop: false));
        Assert.True(SetupDesk.Shows("drawer.images", desktop: false));
        Assert.True(SetupDesk.Shows("clock", desktop: true));
    }
}
