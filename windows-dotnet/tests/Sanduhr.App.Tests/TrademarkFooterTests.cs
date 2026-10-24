using System.Windows.Controls;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// The trademark notice is a Store requirement (test 10.1.4.4): wherever Claude is named, the disclaimer shows. The one
/// shared footer must render the whole notice, not just exist.
/// </summary>
[Collection("SettingsWindow")]
public sealed class TrademarkFooterTests
{
    [Fact]
    public void TheFooterRendersTheWholeNotice() => AppHost.Run(() =>
    {
        var footer = new TrademarkFooter();
        var text = Assert.IsType<TextBlock>(footer.Content);
        Assert.Equal(SettingsNames.TrademarkNotice, text.Text);
        Assert.Contains("trademarks of Anthropic", text.Text);
        Assert.Equal(System.Windows.TextWrapping.Wrap, text.TextWrapping);
        Assert.False(footer.Focusable);
    });
}
