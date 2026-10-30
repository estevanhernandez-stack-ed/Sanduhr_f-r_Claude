using System.IO;
using Sanduhr.App.Interop;

namespace Sanduhr.App.Tests;

/// <summary>The Shell calls drawers make, against a sandbox (drawers plan 2, Task 4).</summary>
public sealed class ShellFilesTests
{
    [Fact]
    public void Move_moves_a_file_and_reports_where_it_landed() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var a = box.File(@"Desktop\a.txt", "hello");
        var home = box.Dir(@"Desktop\Drawers\Docs");
        var r = ShellFiles.Move(a, home, "a.txt");
        Assert.True(r.Ok, $"0x{r.HResult:X8}");
        Assert.False(File.Exists(a));
        Assert.Equal(Path.Combine(home, "a.txt"), r.NewPath, ignoreCase: true);
        Assert.Equal("hello", File.ReadAllText(Path.Combine(home, "a.txt")));
    });

    [Fact]
    public void Move_never_overwrites_a_file_already_there() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var a = box.File(@"Desktop\a.txt", "new");
        var home = box.Dir(@"Desktop\Drawers\Docs");
        File.WriteAllText(Path.Combine(home, "a.txt"), "old");
        var r = ShellFiles.Move(a, home, "a.txt");
        Assert.True(r.Ok);
        Assert.Equal("old", File.ReadAllText(Path.Combine(home, "a.txt")));
        Assert.Equal(2, Directory.GetFiles(home).Length);
    });

    [Fact]
    public void Move_of_a_locked_file_fails_and_leaves_it_in_place() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var a = box.File(@"Desktop\locked.txt");
        var home = box.Dir(@"Desktop\Drawers\Docs");
        using (new FileStream(a, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.False(ShellFiles.Move(a, home, "locked.txt").Ok);
            Assert.False(string.IsNullOrEmpty(ShellFiles.Holder(a)));
        }
        Assert.True(File.Exists(a));
    });

    [Fact]
    public void Rename_renames_through_the_Shell() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var a = box.File(@"Desktop\a.txt");
        Assert.True(ShellFiles.Rename(a, "b.txt").Ok);
        Assert.True(File.Exists(Path.Combine(box.Desktop, "b.txt")));
    });

    [SkippableFact]
    public void FinalPath_sees_through_a_junction() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var real = box.Dir("Elsewhere");
        box.File(@"Elsewhere\secret.txt");
        var link = Path.Combine(box.Desktop, "link");
        Skip.IfNot(box.Junction(link, real), "mklink /J is not available here.");
        Assert.Equal(Path.Combine(real, "secret.txt"), ShellFiles.FinalPath(Path.Combine(link, "secret.txt")), ignoreCase: true);
        Assert.Equal(real, ShellFiles.FinalPath(link), ignoreCase: true);
    });

    [Fact]
    public void FinalPath_of_a_missing_file_is_null()
    {
        using var box = new DrawerSandbox();
        Assert.Null(ShellFiles.FinalPath(Path.Combine(box.Desktop, "nope.txt")));
    }

    [Fact]
    public void Holder_of_a_file_nobody_holds_is_null()
    {
        using var box = new DrawerSandbox();
        Assert.Null(ShellFiles.Holder(box.File(@"Desktop\free.txt")));
    }

    [Fact]
    public void Thumbnail_gives_a_frozen_image_for_a_text_file() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var img = ShellFiles.Thumbnail(box.File(@"Desktop\a.txt"), 64);
        Assert.NotNull(img);
        Assert.True(img!.IsFrozen);
        Assert.True(img.PixelWidth > 0);
    });

    [Fact]
    public void Dark_menus_turn_on_on_this_Windows() => Assert.True(DarkMenus.Enable());
}
