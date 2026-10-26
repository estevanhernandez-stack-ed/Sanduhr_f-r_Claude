using System.IO;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Settings v2, slice 4: Meters above the prompt on Mods &amp; Widgets (WF9; the Mac's layout): one row per folder with an On switch (writing
/// enabledPlugins through SwitchOwnMod), Update… and Remove…; On where the mod isn't installed asks and installs (decision 6).
/// </summary>
[Collection("SettingsWindow")]
public sealed class ModsPageMetersTests : ModsPageFixture
{
    private (ModsPageViewModel Page, List<string> Asked, List<bool> Changed) Open(bool consent)
    {
        var page = Page();
        var asked = new List<string>();
        var changed = new List<bool>();
        page.AskMetersConsent = folder => { asked.Add(folder); return consent; };
        page.Ask = (_, _, _) => true;
        page.MetersChanged = installed => changed.Add(installed);
        page.ReloadLocal();
        return (page, asked, changed);
    }

    private static MetersFolderRowViewModel Row(ModsPageViewModel page, string name) => page.Meters.Single(m => m.Display == @"~\" + name);

    private string OldEntry() =>
        Path.Combine(Root, "external", "mods", "v0.1.0-20200101000000", "sanduhr-meters").Replace("\\", "\\\\");

    [Fact]
    public void Every_folder_has_a_row_with_its_state()
    {
        Home(".claude", "{}");
        Home(".claude-work", "{ broken");
        var (page, _, _) = Open(consent: false);

        Assert.Equal([@"~\.claude", @"~\.claude-work"], page.Meters.Select(m => m.Display));
        Assert.Equal((FolderState.NotInstalled, "Not installed"), (Row(page, ".claude").State, Row(page, ".claude").StateText));
        Assert.Equal(SettingsNames.MetersOffVsRemove, page.MetersCaption);
        Assert.True(page.HasMeterFolders);
    }

    [Fact]
    public void An_unreadable_folder_offers_no_switch_and_says_why()
    {
        var work = Home(".claude-work", "{ broken");
        var (page, asked, _) = Open(consent: true);
        var row = Row(page, ".claude-work");

        Assert.False(row.CanSwitch);
        Assert.False(row.CanUpdate);
        Assert.False(row.CanRemove);
        Assert.True(row.HasReason);
        Assert.Equal(SettingsNames.FolderUnreadableReason, row.Reason);
        row.ToggleCommand.Execute(null);
        Assert.Empty(asked);
        Assert.Equal("{ broken", File.ReadAllText(SettingsOf(work)));
    }

    [Fact]
    public void Declining_consent_leaves_the_folder_off_and_unwritten()
    {
        var home = Home(".claude", "{}");
        var (page, asked, changed) = Open(consent: false);

        Row(page, ".claude").ToggleCommand.Execute(null);

        Assert.Equal([@"~\.claude"], asked);
        Assert.Equal("{}", File.ReadAllText(SettingsOf(home)));
        Assert.False(Row(page, ".claude").IsOn);
        Assert.Equal(FolderState.NotInstalled, Row(page, ".claude").State);
        Assert.Empty(changed);
    }

    [Fact]
    public void Turning_on_where_it_isnt_installed_asks_then_installs_into_that_folder_only()
    {
        var home = Home(".claude", "{}");
        var other = Home(".claude-work", "{}");
        var (page, asked, changed) = Open(consent: true);

        Row(page, ".claude").ToggleCommand.Execute(null);

        Assert.Equal([@"~\.claude"], asked);
        Assert.Contains("CLAUDE_CODE_PLUGIN_DIRS", File.ReadAllText(SettingsOf(home)));
        Assert.Equal("{}", File.ReadAllText(SettingsOf(other)));
        Assert.True(Row(page, ".claude").IsOn);
        Assert.True(Row(page, ".claude").CanRemove);
        Assert.False(Row(page, ".claude").CanUpdate);
        Assert.Equal([true], changed);
        Assert.Contains(page.Installed, r => r.IsSanduhrMod);   // the list follows
    }

    [Fact]
    public void Off_keeps_the_entry_and_on_again_asks_nothing()
    {
        var home = Home(".claude", "{}");
        var (page, asked, changed) = Open(consent: true);
        Row(page, ".claude").ToggleCommand.Execute(null);
        asked.Clear();

        Row(page, ".claude").ToggleCommand.Execute(null);

        var text = File.ReadAllText(SettingsOf(home));
        Assert.Contains("CLAUDE_CODE_PLUGIN_DIRS", text);
        Assert.False(System.Text.Json.Nodes.JsonNode.Parse(text)!["enabledPlugins"]!["sanduhr-meters@inline"]!.GetValue<bool>());
        Assert.False(Row(page, ".claude").IsOn);
        Assert.Equal(SettingsNames.FolderOff, Row(page, ".claude").StateText);

        Row(page, ".claude").ToggleCommand.Execute(null);
        Assert.Empty(asked);
        Assert.True(Row(page, ".claude").IsOn);
        Assert.Equal([true, false, false], changed);
    }

    [Fact]
    public void Remove_asks_and_puts_settings_json_back_exactly()
    {
        var home = Home(".claude", "{}");
        var (page, _, _) = Open(consent: true);
        Row(page, ".claude").ToggleCommand.Execute(null);
        var questions = new List<string>();
        page.Ask = (title, _, primary) => { questions.Add(title + "|" + primary); return true; };

        Row(page, ".claude").RemoveCommand.Execute(null);

        Assert.Equal(["Remove the meters mod?|Remove"], questions);
        Assert.Equal("{}", File.ReadAllText(SettingsOf(home)));
        Assert.False(Row(page, ".claude").CanRemove);
        Assert.Equal(FolderState.NotInstalled, Row(page, ".claude").State);
    }

    [Fact]
    public void Update_shows_only_while_the_entry_is_outdated_and_moves_it_to_this_version()
    {
        var home = Home(".claude", "{\"env\":{\"CLAUDE_CODE_PLUGIN_DIRS\":\"" + OldEntry() + "\"}}");
        var (page, _, changed) = Open(consent: true);
        Assert.True(Row(page, ".claude").CanUpdate);

        Row(page, ".claude").UpdateCommand.Execute(null);

        Assert.DoesNotContain("v0.1.0-20200101000000", File.ReadAllText(SettingsOf(home)));
        Assert.False(Row(page, ".claude").CanUpdate);
        Assert.Equal("Meters mod updated.", page.MetersStatus);
        Assert.Equal([false], changed);
    }

    [Fact]
    public void Files_missing_still_shows_the_entry_on_and_the_switch_can_turn_it_off()
    {
        var home = Home(".claude", "{\"env\":{\"CLAUDE_CODE_PLUGIN_DIRS\":\"" + OldEntry() + "\"}}");
        var (page, asked, changed) = Open(consent: true);
        var row = Row(page, ".claude");

        Assert.Equal(FolderState.Missing, row.State);
        Assert.Equal(SettingsNames.FolderMissing, row.StateText);
        Assert.True(row.IsOn);          // what settings.json says (P12)
        Assert.True(row.CanSwitch);
        Assert.True(row.CanUpdate);

        row.ToggleCommand.Execute(null);

        Assert.Empty(asked);
        var text = File.ReadAllText(SettingsOf(home));
        Assert.False(System.Text.Json.Nodes.JsonNode.Parse(text)!["enabledPlugins"]!["sanduhr-meters@inline"]!.GetValue<bool>());
        Assert.False(Row(page, ".claude").IsOn);
        Assert.Equal([false], changed);
    }

    [Fact]
    public void A_refused_switch_shows_the_real_state()
    {
        var home = Home(".claude", "{}");
        var (page, _, _) = Open(consent: true);
        var row = Row(page, ".claude");
        row.ToggleCommand.Execute(null);
        File.WriteAllText(SettingsOf(home), "{ broken");   // the file goes bad behind the back of the page
        row.ToggleCommand.Execute(null);

        Assert.Equal("{ broken", File.ReadAllText(SettingsOf(home)));
        Assert.False(Row(page, ".claude").CanSwitch);
        Assert.False(Row(page, ".claude").IsOn);
    }

    [Fact]
    public void Each_control_names_its_folder_for_a_screen_reader()
    {
        Home(".claude", "{}");
        var (page, _, _) = Open(consent: false);
        var row = Row(page, ".claude");
        Assert.Equal(SettingsNames.MetersOnFor(@"~\.claude"), row.SwitchName);
        Assert.Equal(SettingsNames.UpdateMetersIn(@"~\.claude"), row.UpdateName);
        Assert.Equal(SettingsNames.RemoveMetersFrom(@"~\.claude"), row.RemoveName);
    }
}
