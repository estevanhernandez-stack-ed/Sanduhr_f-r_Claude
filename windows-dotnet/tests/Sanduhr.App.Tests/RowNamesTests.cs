using System.Windows.Media;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// The text a screen reader reads for list rows the Settings window audit cannot reach with sample data (an empty vault, no
/// launcher buttons, no theme findings): a row's ToString is its accessible name, and never a type or a record dump.
/// </summary>
public class RowNamesTests
{
    private static void Reads(string expected, object row)
    {
        var text = row.ToString();
        Assert.Equal(expected, text);
        Assert.False(SettingsWindowNamesTests.Leaks(text!), text);
    }

    [Fact]
    public void LocalUsageRowsReadAsNameAndTokens() => Reads("my-project, 1.2M", new BreakdownRow("my-project", "1.2M"));

    [Fact]
    public void LegendEntriesReadAsTheirLabel() => Reads("Work", new LegendItem("Work", Brushes.Red));

    [Fact]
    public void ThemeFindingsReadAsFieldAndMessage() => Reads("bg: not a hex colour", new ThemeFindingViewModel("bg", "not a hex colour", true));

    [Fact]
    public void ThirdPartyEntriesReadAsNameAndLicenseNotAsRecords()
    {
        Reads("Velopack, MIT", new ThirdPartyPackage("Velopack", "MIT", "https://example.org"));
        Reads("EsteFont Pro font", new ThirdPartyFont("EsteFont Pro", ["a.ttf"], "(c) someone", "Proprietary"));
    }

    [Fact]
    public void WeatherResultsReadAsThePlaceLabel() =>
        Reads("Fort Worth, Texas, United States", WeatherPlace.Create("Fort Worth", "Texas", "United States", 32.75, -97.33));

    [Fact]
    public void ModsLinesAndFolderOptionsReadAsWords()
    {
        Reads("Notification, 2 groups", new ModsLine("Notification", "2 groups"));
        Reads(@"~\.claude-personal", new ModsFolderOption(@"C:\u\.claude-personal", @"~\.claude-personal"));
    }
}
