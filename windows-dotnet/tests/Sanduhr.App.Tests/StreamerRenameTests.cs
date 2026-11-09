using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using Sanduhr.App.Services;
using Sanduhr.App.Updates;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Rename… on Show the real name (dashboard task "Streamer mode: let people name their own codenames", 2026-10-27): the service saves
/// it and every masked surface reads it, the statusline included; the row's inline box saves, refuses with a reason, or cancels.
/// </summary>
[Collection("SettingsWindow")]
public sealed class StreamerRenameTests : AppWindowFixture
{
    private (StreamerService Service, Sanduhr.Core.Paths Paths) Make()
    {
        var paths = ScratchPaths();
        Home(".claude-qxwork");
        var service = new StreamerService(paths, new SettingsStore(paths),
            () => ClaudeCodeFolders.Build(Profile, null, []), () => Profile, post: a => a(), resetDone: () => false);
        return (service, paths);
    }

    [Fact]
    public void A_rename_masks_with_the_new_name_saves_the_book_and_raises_changed()
    {
        var (service, paths) = Make();
        service.Load();
        service.SetOn(true);
        StreamerMode.Mask.Name(CodenameCategory.Account, "Qxalpha");
        int changed = 0;
        service.Changed += () => changed++;

        Assert.Equal(CodenameRename.Ok, service.Rename(CodenameCategory.Account, "Qxalpha", "Lantern"));

        Assert.Equal(1, changed);
        Assert.Equal("Lantern", StreamerMode.Mask.Name(CodenameCategory.Account, "Qxalpha"));
        var json = JsonNode.Parse(File.ReadAllText(paths.CodenamesFile))!;
        Assert.Equal("Lantern", (string?)json["chosen"]!["account"]!["Qxalpha"]);
        Assert.Contains(service.Entries(), e => e.Real == "Qxalpha" && e.Codename == "Lantern" && e.Chosen);
    }

    [Fact]
    public void A_refused_rename_writes_nothing_and_raises_nothing()
    {
        var (service, paths) = Make();
        service.Load();
        service.SetOn(true);
        var code = StreamerMode.Mask.Name(CodenameCategory.Account, "Qxalpha");
        StreamerMode.Mask.Name(CodenameCategory.Account, "Qxbeta");
        service.Flush();
        var before = File.ReadAllText(paths.CodenamesFile);
        int changed = 0;
        service.Changed += () => changed++;

        Assert.Equal(CodenameRename.RealName, service.Rename(CodenameCategory.Account, "Qxalpha", "qxbeta"));

        Assert.Equal(0, changed);
        Assert.Equal(before, File.ReadAllText(paths.CodenamesFile));
        Assert.Equal(code, StreamerMode.Mask.Name(CodenameCategory.Account, "Qxalpha"));
    }

    [Fact]
    public void A_folders_new_name_reaches_the_statusline_through_streamer_json()
    {
        var (service, paths) = Make();
        service.Load();
        service.SetOn(true);
        var folder = Path.Combine(Profile, ".claude-qxwork");

        Assert.Equal(CodenameRename.Ok, service.Rename(CodenameCategory.Folder, StreamerMask.FolderKey(folder), "Work Bench"));

        var json = JsonNode.Parse(File.ReadAllText(paths.StreamerStateFile))!;
        Assert.Equal("Work Bench", (string?)json["folders"]![StreamerFile.FolderRef(folder)]);
        Assert.Equal("Work Bench", StreamerMode.Mask.Folder(folder));
        Assert.DoesNotContain("qx", File.ReadAllText(paths.StreamerStateFile), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Shuffle_keeps_the_named_codename()
    {
        var (service, _) = Make();
        service.Load();
        service.SetOn(true);
        StreamerMode.Mask.Name(CodenameCategory.Account, "Qxalpha");
        var other = StreamerMode.Mask.Name(CodenameCategory.Account, "Qxbeta");
        Assert.Equal(CodenameRename.Ok, service.Rename(CodenameCategory.Account, "Qxalpha", "Lantern"));

        service.Shuffle();

        Assert.Equal("Lantern", StreamerMode.Mask.Name(CodenameCategory.Account, "Qxalpha"));
        Assert.NotEqual("Lantern", StreamerMode.Mask.Name(CodenameCategory.Account, "Qxbeta"));
        _ = other;
    }

    [Fact]
    public void The_rows_box_saves_refuses_with_a_reason_and_cancels() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Qxalpha", "Qxbeta");
        try
        {
            widget.Streamer.SetOn(true);
            var code = StreamerMode.Mask.Name(CodenameCategory.Account, "Qxalpha");
            StreamerMode.Mask.Name(CodenameCategory.Account, "Qxbeta");
            var vm = new StreamerSettingsViewModel(widget.Streamer, _ => "");
            try
            {
                var row = vm.Rows.Single(r => r.Real == "Qxalpha");
                Assert.False(row.Chosen);
                Assert.True(row.NotEditing);
                Assert.Equal(SettingsNames.RenameCodenameName(code), row.RenameName);

                row.RenameCommand.Execute(null);
                Assert.True(row.Editing);
                Assert.Equal(code, row.Draft);   // starts from the codename, never the real name

                row.Draft = "QXBETA";
                row.SaveRenameCommand.Execute(null);
                Assert.True(row.Editing);
                Assert.Equal(SettingsNames.RenameRealName, row.RenameError);
                Assert.True(row.HasRenameError);

                row.CancelRenameCommand.Execute(null);
                Assert.False(row.Editing);
                Assert.False(row.HasRenameError);
                Assert.Equal(code, StreamerMode.Mask.Name(CodenameCategory.Account, "Qxalpha"));

                row.RenameCommand.Execute(null);
                row.Draft = "Lantern";
                row.SaveRenameCommand.Execute(null);
                Assert.False(row.Editing);
                vm.Read();   // not attached to a window: the window's attach is what listens
                row = vm.Rows.Single(r => r.Real == "Qxalpha");
                Assert.Equal("Lantern", row.Text);
                Assert.True(row.Chosen);
                Assert.Equal(SettingsNames.RenameCodenameName("Lantern"), row.RenameName);

                row.RenameCommand.Execute(null);
                row.Draft = "";
                row.SaveRenameCommand.Execute(null);   // blank draws one again
                vm.Read();
                row = vm.Rows.Single(r => r.Real == "Qxalpha");
                Assert.False(row.Chosen);
                Assert.True(Codenames.IsCodename(row.Codename), row.Codename);
            }
            finally { vm.Detach(); }
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void A_real_name_arriving_later_rewrites_the_folders_label_in_streamer_json()
    {
        var (service, paths) = Make();
        service.Load();
        service.SetOn(true);
        var folder = Path.Combine(Profile, ".claude-qxwork");
        Assert.Equal(CodenameRename.Ok, service.Rename(CodenameCategory.Folder, StreamerMask.FolderKey(folder), "Lantern"));
        var before = JsonNode.Parse(File.ReadAllText(paths.StreamerStateFile))!;
        Assert.Equal("Lantern", (string?)before["folders"]![StreamerFile.FolderRef(folder)]);

        StreamerMode.Mask.Name(CodenameCategory.Account, "Lantern");   // an account labelled Lantern arrives
        service.Flush();

        var json = JsonNode.Parse(File.ReadAllText(paths.StreamerStateFile))!;
        var label = (string?)json["folders"]![StreamerFile.FolderRef(folder)];
        Assert.NotEqual("Lantern", label);
        Assert.True(Codenames.IsCodename(label ?? ""), label);
    }

    [Fact]
    public void Clearing_a_folders_name_keeps_its_label_in_streamer_json()
    {
        var (service, paths) = Make();
        service.Load();
        service.SetOn(true);
        var folder = Path.Combine(Profile, ".claude-qxwork");
        var key = StreamerMask.FolderKey(folder);
        Assert.Equal(CodenameRename.Ok, service.Rename(CodenameCategory.Folder, key, "Work Bench"));

        Assert.Equal(CodenameRename.Cleared, service.Rename(CodenameCategory.Folder, key, ""));

        var json = JsonNode.Parse(File.ReadAllText(paths.StreamerStateFile))!;
        var label = (string?)json["folders"]![StreamerFile.FolderRef(folder)];
        Assert.True(Codenames.IsCodename(label ?? ""), label);
        Assert.Contains(service.Entries(), e => e.Real == key && e.Codename == label && !e.Chosen);
    }

    [Fact]
    public void Saving_an_unchanged_draft_or_a_blank_on_an_unnamed_row_writes_nothing() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Qxalpha");
        try
        {
            widget.Streamer.SetOn(true);
            var code = StreamerMode.Mask.Name(CodenameCategory.Account, "Qxalpha");
            var vm = new StreamerSettingsViewModel(widget.Streamer, _ => "");
            try
            {
                var row = vm.Rows.Single(r => r.Real == "Qxalpha");
                int changed = 0;
                widget.Streamer.Changed += () => changed++;

                row.RenameCommand.Execute(null);
                row.SaveRenameCommand.Execute(null);   // Enter on the drawn name, untouched
                Assert.False(row.Editing);
                Assert.Equal(0, changed);

                row.RenameCommand.Execute(null);
                row.Draft = "  ";
                row.SaveRenameCommand.Execute(null);   // blank on a drawn (never named) codename
                Assert.False(row.Editing);
                Assert.Equal(0, changed);

                vm.Read();
                row = vm.Rows.Single(r => r.Real == "Qxalpha");
                Assert.False(row.Chosen);
                Assert.Equal(code, row.Codename);
                Assert.DoesNotContain(widget.Streamer.Entries(), e => e.Real == "Qxalpha" && e.Chosen);

                // A real change still saves, and a named codename saved again unchanged is also left alone.
                row.RenameCommand.Execute(null);
                row.Draft = "Lantern";
                row.SaveRenameCommand.Execute(null);
                Assert.Equal(1, changed);
                vm.Read();
                row = vm.Rows.Single(r => r.Real == "Qxalpha");
                row.RenameCommand.Execute(null);
                row.SaveRenameCommand.Execute(null);
                Assert.Equal(1, changed);
            }
            finally { vm.Detach(); }
        }
        finally { widget.Dispose(); }
    });

    private static IEnumerable<T> Below<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var deeper in Below<T>(child)) yield return deeper;
        }
    }

    private static string Spoken(UIElement e) => UIElementAutomationPeer.CreatePeerForElement(e)?.GetName() ?? "";

    /// <summary>The page itself: each row has a Rename… button spoken with the row's codename (never the real name while on), and the
    /// box it opens takes focus, saves on Enter and hands focus back to the button.</summary>
    [Fact]
    public void On_the_page_rename_opens_in_place_and_speaks_only_the_codename() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Qxalpha");
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask,
            checkForUpdatesNow: () => Task.FromResult(new UpdateProbeResult(UpdateProbeStatus.UpToDate)), isStoreBuild: false);
        var window = new SettingsWindow(svm);
        var guard = NoActivate.Show(window);
        void Settle()
        {
            window.UpdateLayout();
            Pump();
            window.UpdateLayout();
            Pump();
            guard.Check();
        }
        try
        {
            widget.Streamer.SetOn(true);
            var code = StreamerMode.Mask.Name(CodenameCategory.Account, "Qxalpha");
            window.SelectPage(SettingsPage.Privacy, SettingsIndex.Streamer);
            svm.Privacy.Streamer!.Read();
            Settle();

            var renames = Below<Button>(window).Where(b => Spoken(b).StartsWith("Rename the codename", StringComparison.Ordinal)).ToList();
            Assert.NotEmpty(renames);
            Assert.All(renames, b => Assert.DoesNotContain("qx", Spoken(b), StringComparison.OrdinalIgnoreCase));
            var button = renames.Single(b => Spoken(b) == SettingsNames.RenameCodenameName(code));

            button.Command.Execute(null);
            Settle();
            var box = Below<TextBox>(window).Single(t => t.IsVisible && Spoken(t) == SettingsNames.RenameField);
            Assert.True(box.IsKeyboardFocusWithin || box.IsFocused, "the box takes focus when it opens");
            Assert.Equal(code, box.Text);

            box.Text = "Lantern";
            Assert.Contains(box.InputBindings.OfType<System.Windows.Input.KeyBinding>(), k => k.Key == System.Windows.Input.Key.Return);
            box.InputBindings.OfType<System.Windows.Input.KeyBinding>().Single(k => k.Key == System.Windows.Input.Key.Return).Command.Execute(null);
            Settle();

            Assert.Equal("Lantern", StreamerMode.Mask.Name(CodenameCategory.Account, "Qxalpha"));
            var back = Below<Button>(window).Single(b => Spoken(b) == SettingsNames.RenameCodenameName("Lantern"));
            Assert.True(back.IsFocused, "focus goes back to the row's Rename button");
        }
        finally { window.Close(); widget.Dispose(); }
    });
}
