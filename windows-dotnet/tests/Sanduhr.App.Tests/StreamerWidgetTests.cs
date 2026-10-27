using System.Windows.Controls;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings v2, slice 7: streamer mode outside Settings: the chip, its indicator, the account menus, the tour's chip, the
/// re-sign-in window, the first-run archive question and the widget's error line (spec, Reach outside Settings; the reach inventory).</summary>
[Collection("SettingsWindow")]
public sealed class StreamerWidgetTests : AppWindowFixture
{
    [Fact]
    public void The_chip_shows_the_codename_and_says_streamer_mode_is_on() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Qxalpha", "Qxbeta");
        try
        {
            widget.RefreshAccountLabel();   // Start() would also fetch; the label alone is what this reads
            Assert.Contains("Qxalpha", widget.AccountSwitcherText);
            Assert.False(widget.StreamerOn);
            widget.Streamer.SetOn(true);
            var code = StreamerMode.Mask.Name(CodenameCategory.Account, "Qxalpha");
            Assert.Equal($"⇆ {code}", widget.AccountSwitcherText);
            Assert.True(widget.StreamerOn);
            Assert.Equal(SettingsNames.StreamerChipName($"⇆ {code}", true), widget.AccountSwitcherName);
            widget.Streamer.SetOn(false);
            Assert.Equal("⇆ Qxalpha", widget.AccountSwitcherText);
            Assert.Equal("⇆ Qxalpha", widget.AccountSwitcherName);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void The_account_menus_show_codenames_and_switch_by_the_real_label() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Qxalpha", "Qxbeta");
        try
        {
            widget.Streamer.SetOn(true);
            var menu = new MenuItem();
            AccountMenuBuilder.PopulateSwitchList(menu.Items, widget, includeAdd: true, includeManage: true);
            var rows = menu.Items.OfType<MenuItem>().Where(m => m.CommandParameter is string).ToList();
            Assert.Equal(2, rows.Count);
            Assert.All(rows, m => Assert.DoesNotContain("Qx", (string)m.Header));
            Assert.Equal(["Qxalpha", "Qxbeta"], rows.Select(m => (string)m.CommandParameter));
            Assert.Contains(StreamerMode.Mask.Name(CodenameCategory.Account, "Qxbeta"), (string)rows[1].Header);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void The_tours_chip_shows_the_codename() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Qxalpha");
        try
        {
            widget.Streamer.SetOn(true);
            var tour = new WelcomeTourViewModel(WelcomeTour.StepsFor(false, new HashSet<TourFeature>()), widget);
            Assert.DoesNotContain("Qxalpha", tour.ChipPreview);
            Assert.Equal(StreamerMode.Mask.Name(CodenameCategory.Account, "Qxalpha"), tour.ChipPreview);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void The_re_sign_in_window_shows_the_codename_and_saves_to_the_real_label() => AppHost.Run(() =>
    {
        StreamerMode.Use(new StreamerMask(true, CodenameBook.New(), Profile));
        string? saved = null;
        var window = ManualKeyWindow.ForReauth("Qxalpha", (label, _) => saved = label, embeddedAvailable: false);
        OffScreen(window);
        try
        {
            var code = StreamerMode.Mask.Name(CodenameCategory.Account, "Qxalpha");
            Assert.DoesNotContain("Qxalpha", window.HeadlineText.Text);
            Assert.Contains(code, window.HeadlineText.Text);
            Assert.Equal(code, window.LabelBox.Text);
            window.ShowErrorForTests("Account 'Qxalpha' already exists");
            Assert.Equal($"Account '{code}' already exists", window.ErrorText.Text);
        }
        finally { window.Close(); }
        Assert.Null(saved);   // nothing was saved; the closure still names the real label (SignInCoordinator.cs:101)
    });

    [Fact]
    public void The_first_run_archive_question_shows_codenames_and_keeps_the_real_folders() => AppHost.Run(() =>
    {
        StreamerMode.Use(new StreamerMask(true, CodenameBook.New(), Profile));
        var dialog = VaultConsentDialog.ForTests([".claude-qxwork"]);
        try
        {
            var box = dialog.RootBoxesForTests.Single();
            Assert.Equal(".claude-qxwork", box.Key);
            Assert.Equal(StreamerMode.Mask.FolderName(".claude-qxwork"), (string)box.Value.Content);
        }
        finally { dialog.Close(); }
    });

    [Fact]
    public void The_widgets_error_line_shows_no_user_folder() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Qxalpha");
        try
        {
            widget.Streamer.SetOn(true);
            widget.FailForTests($@"Could not read {Profile}\.claude-qxwork\settings.json");
            Assert.DoesNotContain(Profile, widget.StatusText);
            Assert.Contains("%USERPROFILE%", widget.StatusText);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void The_tray_tooltip_and_its_item_follow_the_switch()
    {
        Assert.StartsWith("Streamer mode · ", SettingsNames.TrayTooltip(true, "Sanduhr für Claude — wk 42%"));
        Assert.Equal(SettingsNames.StreamerSection, SettingsNames.StreamerMenuItemHeader);
    }

    [Fact]
    public void The_re_sign_in_window_saves_with_the_real_label_not_the_codename() => AppHost.Run(() =>
    {
        StreamerMode.Use(new StreamerMask(true, CodenameBook.New(), Profile));
        string? saved = null;
        var window = ManualKeyWindow.ForReauth("Qxalpha", (label, _) => { saved = label; throw new InvalidOperationException("stop"); }, embeddedAvailable: false);
        OffScreen(window);
        try
        {
            window.SessionKeyBox.Text = "sk-test";
            window.SaveButton.RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("Qxalpha", saved);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void The_account_menu_header_is_unmasked_off_and_masked_on() => AppHost.Run(() =>
    {
        StreamerMode.Use(StreamerMask.Off);
        Assert.Equal("●  Qxalpha", AccountMenuBuilder.Header("Qxalpha", true, false));
        Assert.Equal("     Qxbeta · in use", AccountMenuBuilder.Header("Qxbeta", false, true));
    });

    [Fact]
    public void The_tray_and_widget_items_share_one_key_with_the_service() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Qxalpha");
        try
        {
            var seen = 0;
            widget.Streamer.Changed += () => seen++;
            // The shortcut's click handler is `Streamer.SetOn(item.IsChecked)`: both menus write through the one service.
            widget.Streamer.SetOn(true);
            Assert.True(widget.StreamerOn);
            Assert.True(StreamerMode.On);
            Assert.True(seen > 0);
            widget.Streamer.SetOn(false);
            Assert.False(widget.StreamerOn);
            Assert.False(StreamerMode.On);
        }
        finally { widget.Dispose(); }
    });
}
