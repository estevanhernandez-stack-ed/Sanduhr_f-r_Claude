using System.IO;
using System.Windows.Automation;
using System.Windows.Controls;
using Sanduhr.App.Views;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.App.Tests;

/// <summary>The statusline consent dialog's meter picker has a Codex box beside the other four, so a subset pick keeps Codex.</summary>
[Collection("SettingsWindow")]
public sealed class ConsentCodexSegmentTests : AppWindowFixture
{
    private static IntegrationConsentDialog Dialog() =>
        new(new ConsentSpec("Install the statusline?", "Intro.", @"~\.claude", "Details.", "Install", null, OfferSegments: true));

    private string Command(IReadOnlyList<string> segments) =>
        new StatuslineInstaller(Profile, Path.Combine(Root, "bin"), null, Path.Combine(Root, "sanduhr"))
            .BuildCommand(null, StatuslineInstaller.JoinLine, segments);

    [Fact]
    public void The_dialog_has_a_Codex_box_ticked_by_default_with_a_name() => AppHost.Run(() =>
    {
        var dialog = Dialog();
        var box = Assert.IsType<CheckBox>(dialog.FindName("CodexBox"));
        Assert.Equal(true, box.IsChecked);
        Assert.Equal("Codex", box.Content);
        Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(box)));
        foreach (var name in new[] { "AccountBox", "SessionBox", "WeeklyBox", "ResetsBox", "CodexBox" })
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName((CheckBox)dialog.FindName(name))), name);
    });

    [Fact]
    public void Unticking_only_Weekly_writes_a_Mine_that_still_includes_Codex() => AppHost.Run(() =>
    {
        var dialog = Dialog();
        ((CheckBox)dialog.FindName("WeeklyBox")).IsChecked = false;
        var choice = dialog.Choice();
        Assert.Equal(["account", "session", "resets", "codex"], choice.Segments);
        Assert.EndsWith(" -Mine account,session,resets,codex", Command(choice.Segments));
    });

    [Fact]
    public void Unticking_Codex_and_Weekly_leaves_Codex_out_of_Mine() => AppHost.Run(() =>
    {
        var dialog = Dialog();
        ((CheckBox)dialog.FindName("CodexBox")).IsChecked = false;
        ((CheckBox)dialog.FindName("WeeklyBox")).IsChecked = false;
        var choice = dialog.Choice();
        Assert.Equal(["account", "session", "resets"], choice.Segments);
        Assert.EndsWith(" -Mine account,session,resets", Command(choice.Segments));
    });

    [Fact]
    public void Unticking_only_Codex_writes_the_four_Claude_segments_so_Codex_hides() => AppHost.Run(() =>
    {
        var dialog = Dialog();
        ((CheckBox)dialog.FindName("CodexBox")).IsChecked = false;
        var choice = dialog.Choice();
        Assert.Equal(["account", "session", "weekly", "resets"], choice.Segments);
        Assert.EndsWith(" -Mine account,session,weekly,resets", Command(choice.Segments));
    });

    [Fact]
    public void All_five_ticked_writes_no_Mine() => AppHost.Run(() =>
    {
        var choice = Dialog().Choice();
        Assert.Equal(StatuslineInstaller.DefaultSegments, choice.Segments);
        Assert.DoesNotContain("-Mine", Command(choice.Segments));
    });
}
