using System.IO;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings ▸ Mods &amp; Widgets ▸ Install widget from file… (#408): the picked zip installs, refuses or is cancelled, and says so.</summary>
public sealed class WidgetFromFilePageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-wfpage-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Paths _paths;
    private readonly SettingsStore _store;

    public WidgetFromFilePageTests()
    {
        _paths = new Paths(Path.Combine(_root, "roaming"), Path.Combine(_root, "home"), Path.Combine(_root, "local"), Path.Combine(_root, "home", ".sanduhr"));
        _store = new SettingsStore(_paths);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private WidgetStore Installed => new(_paths.WidgetsDir, WidgetCatalogService.EncodePng);

    private string ZipFile(string name, Dictionary<string, byte[]> files)
    {
        Directory.CreateDirectory(_root);
        string path = Path.Combine(_root, name);
        File.WriteAllBytes(path, WidgetKit.Zip(files));
        return path;
    }

    [Fact]
    public void A_picked_pack_installs_lists_as_made_here_and_reloads_the_Desk()
    {
        Sta.Run(() =>
        {
            string zip = ZipFile("palette.zip", WidgetKit.Pack("good-palette"));
            var page = new ModsPageViewModel(() => new WidgetCatalogService(_paths), _store);
            int changed = 0;
            page.Changed = () => changed++;
            page.PickZip = () => zip;

            page.InstallFromFileCommand.Execute(null);

            Assert.Equal("Fixture palette is installed from your file. It stays on this PC.", page.Status);
            Assert.Equal(1, changed);
            Assert.Equal(WidgetSource.Local, Installed.Load().Find("good-palette")!.Source);
            Assert.Contains(page.Installed, r => r.Id == "good-palette");
        });
    }

    [Fact]
    public void A_broken_pack_cancel_or_missing_file_installs_nothing_and_says_why()
    {
        Sta.Run(() =>
        {
            var page = new ModsPageViewModel(() => new WidgetCatalogService(_paths), _store);
            int changed = 0;
            page.Changed = () => changed++;

            var noLicense = WidgetKit.Pack("good-palette");
            noLicense.Remove("LICENSE");
            string bad = ZipFile("bad.zip", noLicense);
            page.PickZip = () => bad;
            page.InstallFromFileCommand.Execute(null);
            Assert.StartsWith("Nothing was installed: the pack didn't pass Sanduhr's checks.", page.Status);
            Assert.Contains("LICENSE", page.Status);

            page.PickZip = () => null;
            page.InstallFromFileCommand.Execute(null);
            Assert.Equal(SettingsNames.FileNoneChosen, page.Status);

            page.PickZip = () => Path.Combine(_root, "gone.zip");
            page.InstallFromFileCommand.Execute(null);
            Assert.Equal(SettingsNames.FileUnreadable, page.Status);

            Assert.Equal(0, changed);
            Assert.Empty(Installed.Load().Packs);
        });
    }
}
