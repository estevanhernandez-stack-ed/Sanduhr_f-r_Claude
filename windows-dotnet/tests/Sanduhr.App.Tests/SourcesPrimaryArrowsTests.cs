using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// The arrow keys move the Primary choice between the Sources rows' radios, as a Windows radio group does. Each radio sits in
/// its own row, so WPF's own arrow handling sent Down from Claude's radio to Codex's Track box (4.8.0 hand pass).
/// </summary>
[Collection("SettingsWindow")]
public sealed class SourcesPrimaryArrowsTests : AppWindowFixture
{
    private static RadioButton[] Three(bool middleEnabled = true) =>
        [new RadioButton(), new RadioButton { IsEnabled = middleEnabled }, new RadioButton()];

    [Fact]
    public void Down_and_Right_step_to_the_next_radio_and_wrap() => AppHost.Run(() =>
    {
        var r = Three();
        Assert.Same(r[1], SourcesPage.Step(r, r[0], Key.Down));
        Assert.Same(r[1], SourcesPage.Step(r, r[0], Key.Right));
        Assert.Same(r[0], SourcesPage.Step(r, r[2], Key.Down));
    });

    [Fact]
    public void Up_and_Left_step_back_and_wrap() => AppHost.Run(() =>
    {
        var r = Three();
        Assert.Same(r[0], SourcesPage.Step(r, r[1], Key.Up));
        Assert.Same(r[0], SourcesPage.Step(r, r[1], Key.Left));
        Assert.Same(r[2], SourcesPage.Step(r, r[0], Key.Up));
    });

    [Fact]
    public void A_disabled_radio_is_skipped_and_other_keys_do_nothing() => AppHost.Run(() =>
    {
        var r = Three(middleEnabled: false);
        Assert.Same(r[2], SourcesPage.Step(r, r[0], Key.Down));
        Assert.Null(SourcesPage.Step(r, r[0], Key.Tab));
        Assert.Null(SourcesPage.Step(r, r[0], Key.Space));
        RadioButton[] alone = [new RadioButton(), new RadioButton { IsEnabled = false }];
        Assert.Null(SourcesPage.Step(alone, alone[0], Key.Down));
    });

    [Fact]
    public void Down_on_the_page_makes_Codex_primary() => AppHost.Run(() =>
    {
        Directory.CreateDirectory(Path.Combine(Profile, ".codex"));
        var store = new SettingsStore(ScratchPaths());
        store.SaveSetup(SetupChoices.FirstRun.ToSettings(null).WithClaude(true).WithCodex(true));
        bool claude = true;
        var vm = new SourcesViewModel(store, () => claude, on => claude = on, () => CodexHomes.Discover(null, Profile));
        var window = new Window { Content = new SourcesPage { DataContext = vm } };
        OffScreen(window);
        window.Show();
        try
        {
            window.UpdateLayout();
            Pump();
            var radios = new List<RadioButton>();
            void Collect(DependencyObject n)
            {
                if (n is RadioButton rb) radios.Add(rb);
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(n); i++) Collect(VisualTreeHelper.GetChild(n, i));
            }
            Collect(window);
            Assert.Equal(2, radios.Count);
            Assert.True(radios[0].IsChecked);

            var down = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(radios[0])!, 0, Key.Down)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            radios[0].RaiseEvent(down);

            Assert.True(down.Handled);
            Assert.True(radios[1].IsChecked);
            Assert.True(vm.Rows.Single(r => r.Source == AgentSources.Codex).IsPrimary);
        }
        finally { window.Close(); }
    });
}
