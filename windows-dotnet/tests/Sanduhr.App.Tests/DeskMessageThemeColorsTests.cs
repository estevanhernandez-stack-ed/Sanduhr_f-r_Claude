using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>"Use the theme's colors" (settings.json <c>desk_message.theme_colors</c>): the message draws in the active widget theme's message
/// colors and glow whether or not the Desk follows the theme, and a line's own <c>{ink:}</c> is ignored; off, every line and setting is exactly
/// as before. The message's own Color and the text's tags are never rewritten.</summary>
public sealed class DeskMessageThemeColorsTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly ThemeDefinition Obsidian = ThemeCatalog.BuiltIns["obsidian"];   // message 6c63ff,e8e4dc; glow 6c63ff

    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-msgtheme-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private static DeskFrame FrameWith(DeskMessageSettings settings, string line = "keep building.") =>
        new() { Now = Now, Message = new DeskMessageFrame(new MessageToday(usual: line), settings) };

    // ---- the resolver ----

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void On_the_message_draws_the_themes_stops_and_glow_following_or_not(bool follows)
    {
        var own = DeskMessageSettings.Default with { ThemeColors = true, Color = "ff00ff", Glow = false };
        var draw = DeskLookResolver.For(follows, Obsidian);
        var themed = DeskLookResolver.Frame(FrameWith(own), draw, Obsidian);
        Assert.Equal("6c63ff,e8e4dc", themed.Message!.Settings.Color);
        Assert.True(themed.Message.Settings.Glow);
        Assert.Equal("6c63ff", themed.Message.GlowHex);
        Assert.True(themed.Message.Settings.ThemeColors);   // the piece reads this to ignore a line's {ink:}
    }

    [Fact]
    public void On_while_not_following_only_the_message_changes()
    {
        var own = DeskMessageSettings.Default with { ThemeColors = true };
        var frame = FrameWith(own) with { PlateHex = null };
        var themed = DeskLookResolver.Frame(frame, DeskLookResolver.For(false, Obsidian), Obsidian);
        Assert.Null(themed.PlateHex);
        Assert.Null(themed.MeterStops);
    }

    [Fact]
    public void On_with_no_theme_known_the_message_keeps_its_own()
    {
        var own = DeskMessageSettings.Default with { ThemeColors = true, Color = "ff00ff" };
        var themed = DeskLookResolver.Frame(FrameWith(own), null, null);
        Assert.Equal("ff00ff", themed.Message!.Settings.Color);
        Assert.Null(themed.Message.GlowHex);
    }

    [Fact]
    public void Off_the_message_is_what_it_was_following_or_not()
    {
        var own = DeskMessageSettings.Default with { ThemeColors = false, Color = "ff00ff", Glow = false };
        var notFollowing = DeskLookResolver.Frame(FrameWith(own), DeskLookResolver.For(false, Obsidian), Obsidian);
        Assert.Equal("ff00ff", notFollowing.Message!.Settings.Color);
        Assert.False(notFollowing.Message.Settings.Glow);
        Assert.Null(notFollowing.Message.GlowHex);
        var following = DeskLookResolver.Frame(FrameWith(own), DeskLookResolver.For(true, Obsidian), Obsidian);
        Assert.Equal("6c63ff,e8e4dc", following.Message!.Settings.Color);   // follow still themes the message, as today
        Assert.False(following.Message.Settings.ThemeColors);               // and a line's own ink still wins
    }

    [Fact]
    public void The_own_settings_are_never_rewritten()
    {
        var own = DeskMessageSettings.Default with { ThemeColors = true, Color = "ff00ff", Glow = false };
        _ = DeskLookResolver.Frame(FrameWith(own), DeskLookResolver.For(true, Obsidian), Obsidian);
        Assert.Equal("ff00ff", own.Color);
        Assert.False(own.Glow);
    }

    // ---- the piece ----

    private static DeskView View(DeskFrame frame)
    {
        var bounds = new PxRect(0, 0, 1920, 1080);
        var work = new PxRect(0, 0, 1920, 1032);
        var screen = new DeskScreen("TEST", bounds, work, 1.0, true, TaskbarClearance.Insets(bounds, work, [], 1.0));
        var settings = (DeskSettings.Default with { Enabled = true, Layout = "message:tl", Font = "Segoe UI" }).Normalized();
        var view = new DeskView(screen, settings, new DeskFonts(["Segoe UI"]));
        view.Update(frame, DeskInkMap.Light);
        return view;
    }

    private static IEnumerable<T> All<T>(DependencyObject node) where T : DependencyObject
    {
        if (node is T hit) yield return hit;
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            foreach (var found in All<T>(child))
                yield return found;
    }

    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString("#" + hex)!;

    private static TextBlock Drawn(DeskFrame frame, string text) => All<TextBlock>(View(frame)).Single(t => t.Text == text);

    [Fact]
    public void On_a_lines_own_ink_is_ignored_and_its_other_tags_still_apply() => Sta.Run(() =>
    {
        var own = DeskMessageSettings.Default with { ThemeColors = true };
        var themed = DeskLookResolver.Frame(FrameWith(own, "{ink:#ff0000}{size:1.5}keep building."), DeskLookResolver.For(false, Obsidian), Obsidian);
        var block = Drawn(themed, "keep building.");
        var brush = Assert.IsType<LinearGradientBrush>(block.Foreground);
        Assert.Equal([C("6c63ff"), C("e8e4dc")], brush.GradientStops.Select(s => s.Color));
        Assert.Equal(C("6c63ff"), Assert.IsType<DropShadowEffect>(block.Effect).Color);
        Assert.True(block.FontSize > 84);   // {size:} still scales the base
    });

    [Fact]
    public void Off_a_lines_own_ink_wins_as_before() => Sta.Run(() =>
    {
        var own = DeskMessageSettings.Default with { ThemeColors = false };
        var themed = DeskLookResolver.Frame(FrameWith(own, "{ink:#ff0000}keep building."), DeskLookResolver.For(true, Obsidian), Obsidian);
        var block = Drawn(themed, "keep building.");
        Assert.Equal(C("ff0000"), Assert.IsType<SolidColorBrush>(block.Foreground).Color);
    });

    [Fact]
    public void On_a_lines_glow_tag_still_applies() => Sta.Run(() =>
    {
        var own = DeskMessageSettings.Default with { ThemeColors = true };
        var themed = DeskLookResolver.Frame(FrameWith(own, "{noglow}keep building."), DeskLookResolver.For(false, Obsidian), Obsidian);
        var fx = Drawn(themed, "keep building.").Effect as DropShadowEffect;
        Assert.True(fx is null || fx.BlurRadius != DeskMessageLook.GlowBlur, "the line asked for no glow");
    });

    // ---- the setting ----

    private Paths ScratchPaths() => new(appDataBase: _root, localAppDataBase: Path.Combine(_root, "local"));

    private SettingsStore Store() => new(ScratchPaths());

    private void WriteSettings(string json)
    {
        var paths = ScratchPaths();
        Directory.CreateDirectory(paths.AppDataDir);
        File.WriteAllText(paths.SettingsFile, json);
    }

    [Fact]
    public void A_new_install_reads_true()
    {
        Assert.True(Store().LoadDeskMessage().ThemeColors);
        WriteSettings("""{"desk_ink":{"mode":"automatic"}}""");
        Assert.True(Store().LoadDeskMessage().ThemeColors);   // the whole desk_message object is absent
    }

    [Fact]
    public void An_upgrade_reads_false_so_no_desk_changes_color()
    {
        WriteSettings("""{"desk_message":{"pin":"","rotate":"daily","size":84,"color":"ff00ff","glow":true}}""");
        Assert.False(Store().LoadDeskMessage().ThemeColors);
    }

    [Fact]
    public void The_saved_value_reads_back_under_theme_colors()
    {
        var store = Store();
        store.SaveDeskMessage(DeskMessageSettings.Default with { ThemeColors = false, Color = "ff00ff" });
        Assert.False(store.LoadDeskMessage().ThemeColors);
        store.SaveDeskMessage(DeskMessageSettings.Default with { ThemeColors = true });
        Assert.True(store.LoadDeskMessage().ThemeColors);
        var root = JsonNode.Parse(File.ReadAllText(ScratchPaths().SettingsFile))!;
        Assert.True(root["desk_message"]!["theme_colors"]!.GetValue<bool>());
    }

    // ---- the page ----

    [Fact]
    public void Toggling_the_page_property_saves_theme_colors_and_keeps_the_own_color()
    {
        var store = Store();
        store.SaveDeskMessage(DeskMessageSettings.Default with { ThemeColors = false, Color = "ff00ff" });
        var page = new DeskMessagePageViewModel(store, Path.Combine(_root, "messages.txt"), () => true, () => { }, ["Segoe UI"]);
        page.Activate();
        Assert.False(page.ThemeColors);
        Assert.True(page.OwnColorsEnabled);
        page.ThemeColors = true;
        Assert.False(page.OwnColorsEnabled);
        var saved = store.LoadDeskMessage();
        Assert.True(saved.ThemeColors);
        Assert.Equal("ff00ff", saved.Color);   // kept for when it is turned off
        page.ThemeColors = false;
        Assert.False(store.LoadDeskMessage().ThemeColors);
        Assert.Equal("ff00ff", store.LoadDeskMessage().Color);
    }

    // ---- designs and proposals ----

    [Fact]
    public void Lines_with_their_own_ink_turn_theme_colors_off_so_they_show()
    {
        var on = DeskMessageSettings.Default with { ThemeColors = true };
        Assert.False(on.AfterLinesAdded(["{ink:#ff2a6d,#05d9e8}hello."]).ThemeColors);
        Assert.False(on.AfterLinesAdded(["plain.", "Mon: {glow}{ink:ff0000}red monday."]).ThemeColors);
        Assert.True(on.AfterLinesAdded(["plain.", "{glow}{size:1.2}no color."]).ThemeColors);
        Assert.True(on.AfterLinesAdded(["# {ink:#ff0000} a note"]).ThemeColors);
        var off = on with { ThemeColors = false };
        Assert.Equal(off, off.AfterLinesAdded(["{ink:#ff0000}x"]));
    }
}
