using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>The drawer's drop rules and inline rename, without a live Desk window (drawers plan 2, Task 8). The window side is the live
/// gate in Step 6.</summary>
[Collection("SettingsWindow")]
public sealed class DrawerInteractionTests
{
    private static (DrawerService Service, DrawerPiece Piece) Make(DrawerSandbox box)
    {
        var paths = new Paths(appDataBase: Path.Combine(box.Root, "appdata"), localAppDataBase: Path.Combine(box.Root, "local"));
        var store = new SettingsStore(paths);
        var home = box.Dir(@"Desktop\Drawers\Images");
        store.SaveDrawers(DrawerSettings.Default with { Enabled = true, Drawers = [new DrawerDef("images", "Images", home, true)] });
        var s = new DrawerService(store, paths, new DrawerPlaces(box.Desktop, box.Public, []), Dispatcher.CurrentDispatcher);
        var look = DrawerLook.From(DeskInkMap.Light.Default, null, null, null, new FontFamily("Segoe UI"));
        return (s, new DrawerPiece(s.Get("images")!, s, look, 1));
    }

    private static DataObject Files(params string[] paths) => new(DataFormats.FileDrop, paths);

    [Fact]
    public void A_drop_from_the_Desktop_is_accepted_and_one_from_its_own_drawer_is_not() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, piece) = Make(box);
        var own = box.File(@"Desktop\Drawers\Images\a.png");
        Assert.True(piece.Accepts(Files(box.File(@"Desktop\b.png"))));
        Assert.False(piece.Accepts(Files(own)));
        Assert.False(piece.Accepts(new DataObject(DataFormats.Text, "not files")));
        s.Dispose();
    });

    [Fact]
    public void Inline_rename_commits_through_the_Shell_and_holds_redraws_until_it_ends() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, piece) = Make(box);
        box.File(@"Desktop\Drawers\Images\a.png");
        s.Reload();
        var item = s.Get("images")!.Items.Single();
        piece.BeginRename(item);
        Assert.True(item.Editing);
        Assert.True(s.Busy);
        piece.CommitRename(item, "b.png");
        Assert.False(item.Editing);
        Assert.False(s.Busy);
        Assert.True(File.Exists(Path.Combine(box.Drawers, "Images", "b.png")));
        s.Dispose();
    });

    [Fact]
    public void A_shortcut_is_drawn_without_its_extension_and_a_rename_keeps_the_extension_on_disk() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, piece) = Make(box);
        box.File(@"Desktop\Drawers\Images\Phasmophobia.lnk");
        s.Reload();
        var item = s.Get("images")!.Items.Single();
        Assert.Equal("Phasmophobia.lnk", item.Name);
        Assert.Equal("Phasmophobia", item.DisplayName);
        Assert.Equal("Phasmophobia", item.SpokenName);
        Assert.Equal(("Phasmophobia", 12), DrawerPiece.EditorStart(item));
        piece.BeginRename(item);
        piece.CommitRename(item, "Ghost game");
        Assert.True(File.Exists(Path.Combine(box.Drawers, "Images", "Ghost game.lnk")));
        Assert.False(File.Exists(Path.Combine(box.Drawers, "Images", "Ghost game")));
        s.Dispose();
    });

    [Fact]
    public void A_name_with_its_own_extension_is_not_drawn_short() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var item = new DrawerItem(box.File(@"Desktop\Drawers\Images\report.docx"), isFolder: false, pending: false);
        Assert.Equal("report.docx", item.DisplayName);
        Assert.Equal(("report.docx", 6), DrawerPiece.EditorStart(item));
        var folder = new DrawerItem(Path.Combine(box.Drawers, "Images", "Old.lnk"), isFolder: true, pending: false);
        Assert.Equal("Old.lnk", folder.DisplayName);
    });

    [Fact]
    public void A_rename_to_a_taken_name_says_why_and_changes_nothing() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, piece) = Make(box);
        box.File(@"Desktop\Drawers\Images\a.png");
        box.File(@"Desktop\Drawers\Images\b.png");
        s.Reload();
        var a = s.Get("images")!.Items.First(i => i.Name == "a.png");
        piece.BeginRename(a);
        piece.CommitRename(a, "b.png");
        Assert.Equal("A file of that name is already in this drawer.", piece.Status.Text);
        Assert.Equal(Visibility.Visible, piece.Status.Visibility);
        Assert.True(File.Exists(Path.Combine(box.Drawers, "Images", "a.png")));
        s.Dispose();
    });

    [Fact]
    public void The_drawer_menu_items_use_their_copy()
    {
        Assert.Equal("Rename drawer", DrawerPieceCopy.RenameDrawer);
        Assert.EndsWith("settings…", DrawerPieceCopy.Settings);
    }

    [Fact]
    public void A_drop_with_a_skipped_file_names_no_file_while_streamer_mode_is_on() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, piece) = Make(box);
        var shared = box.File(@"Public\shared-secret-name.txt");
        var mine = box.File(@"Desktop\mine.txt");
        try
        {
            StreamerMode.Use(new StreamerMask(true, CodenameBook.New(), box.Root));
            piece.Land([mine, shared]);
            Assert.DoesNotContain("shared-secret-name", piece.Status.Text);
            Assert.DoesNotContain("mine", piece.Status.Text);
            Assert.Equal(Visibility.Visible, piece.Status.Visibility);
        }
        finally { StreamerMode.Reset(); s.Dispose(); }
    });

    [Fact]
    public void A_move_that_cannot_be_undone_says_so() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, piece) = Make(box);
        piece.Say(new DrawerRun([], []) { UndoLost = true });
        Assert.EndsWith(" Undo is not available for this move.", piece.Status.Text);
        s.Dispose();
    });

    [Fact]
    public void A_press_that_did_not_start_on_an_item_never_starts_a_drag() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, piece) = Make(box);
        box.File(@"Desktop\Drawers\Images\a.png");
        s.Reload();
        var item = s.Get("images")!.Items.Single();
        var far = new Point(200, 200);
        piece.Press(null, new Point(0, 0));
        Assert.False(piece.CanDrag(far));
        piece.Press(item, new Point(0, 0));
        Assert.False(piece.CanDrag(new Point(1, 1)));
        Assert.True(piece.CanDrag(far));
        piece.BeginRename(item);
        piece.Press(item, new Point(0, 0));
        Assert.False(piece.CanDrag(far));
        s.Dispose();
    });

    [Fact]
    public void Disposing_a_piece_mid_edit_closes_the_editor_and_releases_the_hold() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (s, piece) = Make(box);
        box.File(@"Desktop\Drawers\Images\a.png");
        s.Reload();
        var item = s.Get("images")!.Items.Single();
        piece.BeginRename(item);
        piece.Dispose();
        Assert.False(item.Editing);
        Assert.False(s.Busy);
        piece.CommitRename(item, "z.png");
        Assert.True(File.Exists(Path.Combine(box.Drawers, "Images", "a.png")));
        s.Dispose();
    });
}
