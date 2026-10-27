using System.IO;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings v2, slice 7, preflight P3: a Launcher row shows a repo URL and a raw path, so it masks them while streamer mode is on.</summary>
[Collection("SettingsWindow")]
public sealed class StreamerLauncherTests : AppWindowFixture
{
    private const string Repo = "https://github.com/qxowner/qxsecretrepo";
    private const string RawPath = @"C:\Users\qxuser\tools\qxtool.exe";

    private (LauncherSettingsViewModel Vm, string Root) Open()
    {
        var root = Path.Combine(Path.GetTempPath(), "sanduhr-sl-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        var paths = new Paths(appDataBase: root, localAppDataBase: Path.Combine(root, "local"));
        var store = new SettingsStore(paths);
        store.SaveLauncher(new LauncherModel(LauncherOrientation.Column,
            [LauncherButton.Create(LauncherKind.Link, Repo)!, LauncherButton.Create(LauncherKind.App, RawPath, null, null, null)!]));
        return (new LauncherSettingsViewModel(store, paths, () => true), root);
    }

    [Fact]
    public void A_repo_link_shows_scheme_host_and_an_ellipsis_while_on()
    {
        var (vm, root) = Open();
        try
        {
            Assert.Equal(Repo, vm.Rows[0].Detail);
            StreamerMode.Use(new StreamerMask(true, CodenameBook.New(), Profile));
            vm.Reload();
            var detail = vm.Rows[0].Detail;
            Assert.StartsWith("https://", detail);
            Assert.EndsWith("/…", detail);
            Assert.DoesNotContain("qx", detail, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("github", detail, StringComparison.OrdinalIgnoreCase);
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }
    }

    [Fact]
    public void An_unlabelled_links_title_and_spoken_subject_name_its_host_by_codename_while_on()
    {
        var root = Path.Combine(Path.GetTempPath(), "sanduhr-sl-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        try
        {
            var paths = new Paths(appDataBase: root, localAppDataBase: Path.Combine(root, "local"));
            var store = new SettingsStore(paths);
            store.SaveLauncher(new LauncherModel(LauncherOrientation.Column, [LauncherButton.Create(LauncherKind.Link, "https://qxowner.dev/notes")!]));
            var vm = new LauncherSettingsViewModel(store, paths, () => true);
            Assert.Equal("qxowner.dev", vm.Rows[0].Title);

            StreamerMode.Use(new StreamerMask(true, CodenameBook.New(), Profile));
            vm.Reload();
            Assert.DoesNotContain("qx", vm.Rows[0].Title, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("qx", vm.Rows[0].Subject, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("qx", vm.Rows[0].UseDefaultImageName, StringComparison.OrdinalIgnoreCase);
            // The title's codename is the same one the detail line shows.
            Assert.Contains(vm.Rows[0].Title, vm.Rows[0].Detail);
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }
    }

    [Fact]
    public void A_raw_target_title_goes_through_the_path_mask_while_on()
    {
        var (vm, root) = Open();
        try
        {
            Assert.Equal(RawPath, vm.Rows[1].Title);
            StreamerMode.Use(new StreamerMask(true, CodenameBook.New(), Profile));
            vm.Reload();
            Assert.DoesNotContain("qxuser", vm.Rows[1].Title);
            Assert.StartsWith("%USERPROFILE%", vm.Rows[1].Title);
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }
    }
}
