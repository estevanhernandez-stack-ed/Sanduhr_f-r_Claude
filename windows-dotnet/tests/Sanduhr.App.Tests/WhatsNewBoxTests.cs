using System.IO;
using System.Reflection;
using System.Windows;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Settings v2, slice 6 (slice 5 carry, the reverse of P14): About and the What's New window both write "whats_new_hide_after_updates". While
/// both are open, About's change must show in the window when the window is brought back, as the window's change shows on About. The window
/// is built without being shown, so nothing takes the foreground.
/// </summary>
[Collection("SettingsWindow")]
public sealed class WhatsNewBoxTests : AppWindowFixture
{
    private static WhatsNewWindow Unshown() =>
        (WhatsNewWindow)Activator.CreateInstance(typeof(WhatsNewWindow), BindingFlags.Instance | BindingFlags.NonPublic, null,
            [(Action<SettingsPage, string?>)((_, _) => { })], null)!;

    private static void Activate(Window window) =>
        typeof(Window).GetMethod("OnActivated", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [EventArgs.Empty]);

    [Fact]
    public void The_box_reads_the_key_again_when_the_window_is_brought_back() => AppHost.Run(() =>
    {
        var paths = ScratchPaths();
        var model = new WhatsNewViewModel([], "Version 4.0.0", new SettingsStore(paths));
        Assert.False(model.HideAfterUpdates);
        var window = Unshown();
        window.DataContext = model;
        try
        {
            new SettingsStore(paths).SaveWhatsNewHideAfterUpdates(true);   // About's box, through its own store
            Assert.False(model.HideAfterUpdates);                           // nothing re-read yet

            Activate(window);

            Assert.True(model.HideAfterUpdates);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Reading_again_writes_nothing()
    {
        var paths = ScratchPaths();
        var store = new SettingsStore(paths);
        store.SaveWhatsNewHideAfterUpdates(false);
        var model = new WhatsNewViewModel([], "Version 4.0.0", store);
        var before = File.ReadAllText(paths.SettingsFile);

        model.Reload();

        Assert.Equal(before, File.ReadAllText(paths.SettingsFile));
        Assert.False(model.HideAfterUpdates);
    }

    [Fact]
    public void Reading_a_changed_file_updates_the_box_and_raises_the_change_without_writing_the_key()
    {
        var paths = ScratchPaths();
        var store = new SettingsStore(paths);
        var model = new WhatsNewViewModel([], "Version 4.0.0", store);
        var raised = new List<string?>();
        model.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        File.WriteAllText(paths.SettingsFile, "{\"whats_new_hide_after_updates\": true}");
        var before = File.ReadAllText(paths.SettingsFile);

        model.Reload();

        Assert.True(model.HideAfterUpdates);
        Assert.Contains(nameof(WhatsNewViewModel.HideAfterUpdates), raised);
        Assert.Equal(before, File.ReadAllText(paths.SettingsFile));
    }

    [Fact]
    public void The_window_calls_Reload_when_activated()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Sanduhr.App")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var source = File.ReadAllText(Path.Combine(dir!.FullName, "src", "Sanduhr.App", "Views", "WhatsNewWindow.xaml.cs"));
        Assert.Contains("Activated += (_, _) => (DataContext as WhatsNewViewModel)?.Reload();", source);
    }
}
